// Ficheiro: Controllers/QuestionController.cs

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PerguntaAi.Backend.Models; // Importa os DTOs

[ApiController]
[Route("api")] // Rota base
public class QuestionController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public QuestionController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    //=========================================================
    // CREATE (Adiciona uma pergunta a um quiz existente)
    // POST /api/quiz/{quizId}/question
    //=========================================================
    [HttpPost("quiz/{quizId}/question")]
    public async Task<IActionResult> CreateQuestionForQuiz([FromRoute] Guid quizId, [FromBody] QuestionRequest request)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // 1. Inserir a Pergunta
            var questionSql = "INSERT INTO Question (question_id, quiz_id, text, type, order_index, points_base, created_at) " +
                              "VALUES (uuid_generate_v4(), @quiz_id, @text, @type, @order_index, @points_base, NOW()) " +
                              "RETURNING question_id";

            await using var questionCmd = new NpgsqlCommand(questionSql, conn, transaction);
            questionCmd.Parameters.AddWithValue("quiz_id", quizId); // Usa o ID da Rota
            questionCmd.Parameters.AddWithValue("text", request.Text);
            questionCmd.Parameters.AddWithValue("type", request.Type);
            questionCmd.Parameters.AddWithValue("order_index", request.OrderIndex);
            questionCmd.Parameters.AddWithValue("points_base", request.PointsBase);

            var newQuestionId = (Guid)await questionCmd.ExecuteScalarAsync();

            // 2. Loop para inserir as Opções
            foreach (var option in request.Options)
            {
                var optionSql = "INSERT INTO QuestionOption (option_id, question_id, text, is_correct, option_index) " +
                                "VALUES (uuid_generate_v4(), @question_id, @text, @is_correct, @option_index)";

                await using var optionCmd = new NpgsqlCommand(optionSql, conn, transaction);
                optionCmd.Parameters.AddWithValue("question_id", newQuestionId);
                optionCmd.Parameters.AddWithValue("text", option.Text);
                optionCmd.Parameters.AddWithValue("is_correct", option.IsCorrect);
                optionCmd.Parameters.AddWithValue("option_index", option.OptionIndex);

                await optionCmd.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();

            return Ok(new { message = "Pergunta e opções criadas com sucesso!", questionId = newQuestionId });
        }
        catch (NpgsqlException ex) when (ex.SqlState == "23503") // Erro de Foreign Key
        {
            await transaction.RollbackAsync();
            return BadRequest(new { error = $"O QuizId '{quizId}' não existe. A pergunta não foi criada." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { error = $"Erro ao criar pergunta: {ex.Message}" });
        }
    }

    //=========================================================
    // READ (Ver todas as perguntas de um quiz)
    // GET /api/quiz/{quizId}/question
    //=========================================================
    [HttpGet("quiz/{quizId}/question")]
    public async Task<IActionResult> GetQuestionsForQuiz(Guid quizId)
    {
        var questions = new List<object>();
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var sql = "SELECT question_id, text, type, order_index, points_base " +
                  "FROM Question WHERE quiz_id = @quiz_id ORDER BY order_index";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("quiz_id", quizId);

        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            questions.Add(new
            {
                QuestionId = reader.GetGuid(0),
                Text = reader.GetString(1),
                Type = reader.GetString(2),
                OrderIndex = reader.GetInt32(3),
                PointsBase = reader.GetInt32(4)
            });
        }
        return Ok(questions);
    }

    //=========================================================
    // READ (Single) (Ver uma pergunta e as suas opções)
    // GET /api/question/{questionId}
    //=========================================================
    [HttpGet("question/{questionId}")]
    public async Task<IActionResult> GetQuestionById(Guid questionId)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var questionSql = "SELECT question_id, quiz_id, text, type, order_index, points_base " +
                          "FROM Question WHERE question_id = @question_id";

        await using var cmd = new NpgsqlCommand(questionSql, conn);
        cmd.Parameters.AddWithValue("question_id", questionId);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (!await reader.ReadAsync())
        {
            return NotFound(new { message = "Pergunta não encontrada." });
        }

        var questionResponse = new QuestionResponse
        {
            QuestionId = reader.GetGuid(0),
            Text = reader.GetString(2),
            Type = reader.GetString(3),
            OrderIndex = reader.GetInt32(4),
            PointsBase = reader.GetInt32(5),
            Options = new List<OptionResponse>()
        };
        await reader.CloseAsync();

        var optionSql = "SELECT option_id, text, is_correct, option_index " +
                        "FROM QuestionOption WHERE question_id = @question_id ORDER BY option_index";

        await using var optCmd = new NpgsqlCommand(optionSql, conn);
        optCmd.Parameters.AddWithValue("question_id", questionId);

        await using var optReader = await optCmd.ExecuteReaderAsync();
        while (await optReader.ReadAsync())
        {
            questionResponse.Options.Add(new OptionResponse
            {
                OptionId = optReader.GetGuid(0),
                Text = optReader.GetString(1),
                IsCorrect = optReader.GetBoolean(2),
                OptionIndex = optReader.GetString(3)
            });
        }

        return Ok(questionResponse);
    }

    //=========================================================
    // UPDATE (Editar os dados de uma pergunta)
    // PUT /api/question/{questionId}
    //=========================================================
    [HttpPut("question/{questionId}")]
    public async Task<IActionResult> UpdateQuestion(Guid questionId, [FromBody] QuestionUpdateRequest request)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var sql = "UPDATE Question SET " +
                  "text = @text, " +
                  "type = @type, " +
                  "order_index = @order_index, " +
                  "points_base = @points_base " +
                  "WHERE question_id = @question_id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("question_id", questionId);
        cmd.Parameters.AddWithValue("text", request.Text);
        cmd.Parameters.AddWithValue("type", request.Type);
        cmd.Parameters.AddWithValue("order_index", request.OrderIndex);
        cmd.Parameters.AddWithValue("points_base", request.PointsBase);

        var rowsAffected = await cmd.ExecuteNonQueryAsync();

        if (rowsAffected == 0)
        {
            return NotFound(new { message = "Pergunta não encontrada." });
        }

        return Ok(new { message = "Pergunta atualizada com sucesso." });
    }

    //=========================================================
    // DELETE (Apagar uma pergunta e as suas opções)
    // DELETE /api/question/{questionId}
    //=========================================================
    [HttpDelete("question/{questionId}")]
    public async Task<IActionResult> DeleteQuestion(Guid questionId)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        await using var transaction = await conn.BeginTransactionAsync();
        try
        {
            // 1. Apaga as Opções (filhos) primeiro
            var optionSql = "DELETE FROM QuestionOption WHERE question_id = @question_id";
            await using (var cmd = new NpgsqlCommand(optionSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("question_id", questionId);
                await cmd.ExecuteNonQueryAsync();
            }

            // 2. Apaga a Pergunta (pai)
            var questionSql = "DELETE FROM Question WHERE question_id = @question_id";
            await using (var cmd = new NpgsqlCommand(questionSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("question_id", questionId);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    await transaction.RollbackAsync();
                    return NotFound(new { message = "Pergunta não encontrada." });
                }
            }

            await transaction.CommitAsync();
            return Ok(new { message = "Pergunta e opções associadas foram apagadas." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { error = $"Erro ao apagar pergunta: {ex.Message}" });
        }
    }
}