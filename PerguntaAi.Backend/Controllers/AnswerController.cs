using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PerguntaAi.Backend.Models;

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

            var cmdCheck = new NpgsqlCommand("SELECT qo.is_correct, q.points_base FROM public.questionoption qo JOIN public.question q ON qo.question_id = q.question_id WHERE qo.option_id = @o AND q.question_id = @q", conn);
            cmdCheck.Parameters.AddWithValue("o", request.SelectedOptionId);
            cmdCheck.Parameters.AddWithValue("q", request.QuestionId);

            using var reader = await cmdCheck.ExecuteReaderAsync();
            if (!await reader.ReadAsync()) return NotFound("Pergunta ou opção não encontrada.");
            bool correct = reader.GetBoolean(0);
            int pts = correct ? reader.GetInt32(1) : 0;
            await reader.CloseAsync();

            // 1. Inserir a resposta na tabela Answer
            var sqlInsert = "INSERT INTO public.answer (answer_id, room_player_id, question_id, selected_option_id, points_awarded, is_correct, answered_at) " +
                            "VALUES (@id, @rp, @q, @o, @p, @c, NOW())";

            await using var cmdInsert = new NpgsqlCommand(sqlInsert, conn);
            cmdInsert.Parameters.AddWithValue("id", Guid.NewGuid());
            cmdInsert.Parameters.AddWithValue("rp", request.RoomPlayerId);
            cmdInsert.Parameters.AddWithValue("q", request.QuestionId);
            cmdInsert.Parameters.AddWithValue("o", request.SelectedOptionId);
            cmdInsert.Parameters.AddWithValue("p", pts);
            cmdInsert.Parameters.AddWithValue("c", correct);
            await cmdInsert.ExecuteNonQueryAsync();

            // 2. Atualizar o índice da pergunta (para avançar) e somar pontos se estiver correto
            var sqlUpdatePlayer = "UPDATE public.roomplayer SET " +
                                  "current_question_index = current_question_index + 1" +
                                  (correct ? ", total_points = total_points + @p " : " ") +
                                  "WHERE room_player_id = @rp";

            await using var cmdUpdate = new NpgsqlCommand(sqlUpdatePlayer, conn);
            cmdUpdate.Parameters.AddWithValue("rp", request.RoomPlayerId);
            if (correct) cmdUpdate.Parameters.AddWithValue("p", pts);
            await cmdUpdate.ExecuteNonQueryAsync();

            return Ok(new { correct, pointsEarned = pts });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}