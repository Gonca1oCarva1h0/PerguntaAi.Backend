using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PerguntaAi.Backend.Models;
using PerguntaAi.Backend.Services;

[ApiController]
[Route("api/[controller]")]
public class QuizController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly AIService _aiService = new AIService();

    public QuizController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("generate")]
    public async Task<IActionResult> GenerateAIQuiz([FromBody] string theme)
    {
        try
        {
            var quizRequest = await _aiService.GenerateQuizAsync(theme);

            // Verifica se a IA falhou ao gerar o objeto
            if (quizRequest == null) return BadRequest("Não foi possível gerar o quiz para este tema.");

            return await CreateFullQuiz(quizRequest);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = "Erro ao gerar quiz com IA: " + ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateFullQuiz([FromBody] CreateQuizRequest request)
    {
        // Validação de segurança
        if (request == null || request.Questions == null)
            return BadRequest("Dados do quiz ou perguntas estão em falta.");

        var quizResponse = new QuizResponse
        {
            Title = request.Title ?? "Quiz Sem Título",
            Description = request.Description,
            TimePerQuestion = request.TimePerQuestion == 0 ? 30 : request.TimePerQuestion,
            Questions = new List<QuestionResponse>()
        };

        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            var quizSql = "INSERT INTO Quiz (quiz_id, title, description, time_per_question, is_published, version, created_at) " +
                          "VALUES (uuid_generate_v4(), @title, @description, @time_per_question, true, 1, NOW()) " +
                          "RETURNING quiz_id";

            await using var quizCmd = new NpgsqlCommand(quizSql, conn, transaction);
            quizCmd.Parameters.AddWithValue("title", request.Title);
            quizCmd.Parameters.AddWithValue("description", request.Description ?? (object)DBNull.Value);
            quizCmd.Parameters.AddWithValue("time_per_question", request.TimePerQuestion);

            quizResponse.QuizId = (Guid)await quizCmd.ExecuteScalarAsync();

            foreach (var questionRequest in request.Questions)
            {
                var questionResponse = new QuestionResponse
                {
                    Text = questionRequest.Text,
                    Type = questionRequest.Type,
                    OrderIndex = questionRequest.OrderIndex,
                    PointsBase = questionRequest.PointsBase,
                    Options = new List<OptionResponse>()
                };

                var questionSql = "INSERT INTO Question (question_id, quiz_id, text, type, order_index, points_base, created_at) " +
                                  "VALUES (uuid_generate_v4(), @quiz_id, @text, @type, @order_index, @points_base, NOW()) " +
                                  "RETURNING question_id";

                await using var questionCmd = new NpgsqlCommand(questionSql, conn, transaction);
                questionCmd.Parameters.AddWithValue("quiz_id", quizResponse.QuizId);
                questionCmd.Parameters.AddWithValue("text", questionRequest.Text);
                questionCmd.Parameters.AddWithValue("type", questionRequest.Type);
                questionCmd.Parameters.AddWithValue("order_index", questionRequest.OrderIndex);
                questionCmd.Parameters.AddWithValue("points_base", questionRequest.PointsBase);

                questionResponse.QuestionId = (Guid)await questionCmd.ExecuteScalarAsync();

                foreach (var optionRequest in questionRequest.Options)
                {
                    var optionResponse = new OptionResponse
                    {
                        Text = optionRequest.Text,
                        OptionIndex = optionRequest.OptionIndex,
                        IsCorrect = optionRequest.IsCorrect
                    };

                    var optionSql = "INSERT INTO QuestionOption (option_id, question_id, text, is_correct, option_index) " +
                                    "VALUES (uuid_generate_v4(), @question_id, @text, @is_correct, @option_index) " +
                                    "RETURNING option_id";

                    await using var optionCmd = new NpgsqlCommand(optionSql, conn, transaction);
                    optionCmd.Parameters.AddWithValue("question_id", questionResponse.QuestionId);
                    optionCmd.Parameters.AddWithValue("text", optionRequest.Text);
                    optionCmd.Parameters.AddWithValue("is_correct", optionRequest.IsCorrect);
                    optionCmd.Parameters.AddWithValue("option_index", optionRequest.OptionIndex);

                    optionResponse.OptionId = (Guid)await optionCmd.ExecuteScalarAsync();
                    questionResponse.Options.Add(optionResponse);
                }
                quizResponse.Questions.Add(questionResponse);
            }

            await transaction.CommitAsync();
            return Ok(quizResponse);
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { error = $"Erro ao criar quiz: {ex.Message}" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllQuizzes()
    {
        var quizzes = new List<object>();
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var sql = "SELECT quiz_id, title, description, time_per_question FROM Quiz WHERE is_published = true";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            quizzes.Add(new
            {
                QuizId = reader.GetGuid(0),
                Title = reader.GetString(1),
                Description = reader.IsDBNull(2) ? null : reader.GetString(2),
                TimePerQuestion = reader.GetInt32(3)
            });
        }
        return Ok(quizzes);
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetQuizById(Guid id)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var quizSql = "SELECT quiz_id, title, description, time_per_question FROM Quiz WHERE quiz_id = @quiz_id";
        await using var quizCmd = new NpgsqlCommand(quizSql, conn);
        quizCmd.Parameters.AddWithValue("quiz_id", id);

        await using var quizReader = await quizCmd.ExecuteReaderAsync();
        if (!await quizReader.ReadAsync()) return NotFound(new { message = "Quiz não encontrado." });

        var quizResponse = new QuizResponse
        {
            QuizId = quizReader.GetGuid(0),
            Title = quizReader.GetString(1),
            Description = quizReader.IsDBNull(2) ? null : quizReader.GetString(2),
            TimePerQuestion = quizReader.GetInt32(3),
            Questions = new List<QuestionResponse>()
        };
        await quizReader.CloseAsync();

        var questionsMap = new Dictionary<Guid, QuestionResponse>();
        var questionsSql = "SELECT question_id, text, type, order_index, points_base FROM Question WHERE quiz_id = @quiz_id ORDER BY order_index";
        await using var qCmd = new NpgsqlCommand(questionsSql, conn);
        qCmd.Parameters.AddWithValue("quiz_id", id);

        await using var qReader = await qCmd.ExecuteReaderAsync();
        while (await qReader.ReadAsync())
        {
            var q = new QuestionResponse
            {
                QuestionId = qReader.GetGuid(0),
                Text = qReader.GetString(1),
                Type = qReader.GetString(2),
                OrderIndex = qReader.GetInt32(3),
                PointsBase = qReader.GetInt32(4),
                Options = new List<OptionResponse>()
            };
            questionsMap.Add(q.QuestionId, q);
            quizResponse.Questions.Add(q);
        }
        await qReader.CloseAsync();

        var optionsSql = "SELECT option_id, question_id, text, is_correct, option_index FROM QuestionOption WHERE question_id IN (SELECT question_id FROM Question WHERE quiz_id = @quiz_id) ORDER BY option_index";
        await using var oCmd = new NpgsqlCommand(optionsSql, conn);
        oCmd.Parameters.AddWithValue("quiz_id", id);
        await using var oReader = await oCmd.ExecuteReaderAsync();
        while (await oReader.ReadAsync())
        {
            if (questionsMap.TryGetValue(oReader.GetGuid(1), out var parent))
            {
                parent.Options.Add(new OptionResponse
                {
                    OptionId = oReader.GetGuid(0),
                    Text = oReader.GetString(2),
                    IsCorrect = oReader.GetBoolean(3),
                    OptionIndex = oReader.GetString(4)
                });
            }
        }
        return Ok(quizResponse);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteQuiz(Guid id)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();
        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            string[] tables = { "Answer", "RoomPlayer", "Room", "QuestionOption", "Question" };
            foreach (var table in tables)
            {
                var filter = table.StartsWith("Room") || table == "Answer"
                             ? "room_id IN (SELECT room_id FROM Room WHERE quiz_id = @id)"
                             : "quiz_id = @id";
                if (table == "Answer") filter = "room_player_id IN (SELECT room_player_id FROM RoomPlayer WHERE room_id IN (SELECT room_id FROM Room WHERE quiz_id = @id))";
                if (table == "QuestionOption") filter = "question_id IN (SELECT question_id FROM Question WHERE quiz_id = @id)";

                await using var cmd = new NpgsqlCommand($"DELETE FROM {table} WHERE {filter}", conn, transaction);
                cmd.Parameters.AddWithValue("id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            await using var quizCmd = new NpgsqlCommand("DELETE FROM Quiz WHERE quiz_id = @id", conn, transaction);
            quizCmd.Parameters.AddWithValue("id", id);
            if (await quizCmd.ExecuteNonQueryAsync() == 0)
            {
                await transaction.RollbackAsync();
                return NotFound();
            }

            await transaction.CommitAsync();
            return Ok(new { message = "Quiz apagado com sucesso." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateQuiz(Guid id, [FromBody] QuizUpdateRequest request)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var sql = "UPDATE Quiz SET title = @title, description = @desc WHERE quiz_id = @id";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("title", request.Title);
        cmd.Parameters.AddWithValue("desc", request.Description ?? (object)DBNull.Value);
        cmd.Parameters.AddWithValue("id", id);

        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? Ok(new { message = "Quiz atualizado." }) : NotFound();
    }
}