using Microsoft.AspNetCore.Mvc;
using Npgsql;
using NpgsqlTypes; // Necessário para definir os tipos explicitamente
using PerguntaAi.Backend.Models;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

[ApiController]
[Route("api/[controller]")]
public class AnswerController : ControllerBase
{
    private readonly IConfiguration _configuration;
    public AnswerController(IConfiguration config) => _configuration = config;

    [HttpPost]
    public async Task<IActionResult> SubmitAnswer([FromBody] AnswerRequest request)
    {
        try
        {
            await using var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();

            // 1. Verificar status da sala
            var sqlCheckStatus = @"
                SELECT r.status 
                FROM public.Room r 
                JOIN public.RoomPlayer rp ON r.room_id = rp.room_id 
                WHERE rp.room_player_id = @rp";

            await using var cmdStatus = new NpgsqlCommand(sqlCheckStatus, conn);
            cmdStatus.Parameters.AddWithValue("rp", request.RoomPlayerId);
            var status = await cmdStatus.ExecuteScalarAsync() as string;

            if (status != "STARTED")
            {
                return BadRequest(new { error = "O jogo ainda não começou ou já terminou." });
            }

            // 2. Obter tipo da pergunta
            var sqlQuestion = "SELECT type, points_base FROM public.Question WHERE question_id = @q";
            await using var cmdQ = new NpgsqlCommand(sqlQuestion, conn);
            cmdQ.Parameters.AddWithValue("q", request.QuestionId);

            using var readerQ = await cmdQ.ExecuteReaderAsync();
            if (!await readerQ.ReadAsync()) return NotFound("Pergunta não encontrada.");

            string type = readerQ.GetString(0);
            int basePoints = readerQ.GetInt32(1);
            await readerQ.CloseAsync();

            bool correct = false;

            // 3. Validação por tipo
            if (type == "WRITTEN")
            {
                var sqlWritten = "SELECT text FROM public.QuestionOption WHERE question_id = @q AND is_correct = true LIMIT 1";
                await using var cmdW = new NpgsqlCommand(sqlWritten, conn);
                cmdW.Parameters.AddWithValue("q", request.QuestionId);
                string correctText = await cmdW.ExecuteScalarAsync() as string;

                correct = string.Equals(correctText?.Trim(), request.AnswerText?.Trim(), StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                var sqlMC = "SELECT is_correct FROM public.QuestionOption WHERE option_id = @o AND question_id = @q";
                await using var cmdMC = new NpgsqlCommand(sqlMC, conn);

                // Definição explícita do tipo UUID para evitar o erro de parâmetro nulo
                var paramO = cmdMC.Parameters.Add("o", NpgsqlDbType.Uuid);
                paramO.Value = (object)request.SelectedOptionId ?? DBNull.Value;

                cmdMC.Parameters.AddWithValue("q", request.QuestionId);

                var result = await cmdMC.ExecuteScalarAsync();
                correct = result != null && (bool)result;
            }

            int pts = correct ? basePoints : 0;

            // 4. Inserir resposta com tipos explícitos
            var sqlInsert = @"INSERT INTO public.answer 
                (answer_id, room_player_id, question_id, selected_option_id, answer_text, points_awarded, is_correct, answered_at) 
                VALUES (@id, @rp, @q, @o, @at, @p, @c, NOW())";

            await using var cmdInsert = new NpgsqlCommand(sqlInsert, conn);
            cmdInsert.Parameters.AddWithValue("id", Guid.NewGuid());
            cmdInsert.Parameters.AddWithValue("rp", request.RoomPlayerId);
            cmdInsert.Parameters.AddWithValue("q", request.QuestionId);

            // Parâmetros opcionais com tipo UUID e Text definidos
            var pO = cmdInsert.Parameters.Add("o", NpgsqlDbType.Uuid);
            pO.Value = (object)request.SelectedOptionId ?? DBNull.Value;

            var pAt = cmdInsert.Parameters.Add("at", NpgsqlDbType.Text);
            pAt.Value = (object)request.AnswerText ?? DBNull.Value;

            cmdInsert.Parameters.AddWithValue("p", pts);
            cmdInsert.Parameters.AddWithValue("c", correct);
            await cmdInsert.ExecuteNonQueryAsync();

            // 5. Atualizar jogador
            var sqlUpdatePlayer = @"UPDATE public.roomplayer SET 
                current_question_index = current_question_index + 1, 
                total_points = total_points + @p 
                WHERE room_player_id = @rp";

            await using var cmdUpdate = new NpgsqlCommand(sqlUpdatePlayer, conn);
            cmdUpdate.Parameters.AddWithValue("rp", request.RoomPlayerId);
            cmdUpdate.Parameters.AddWithValue("p", pts);
            await cmdUpdate.ExecuteNonQueryAsync();

            return Ok(new { correct, pointsEarned = pts });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}