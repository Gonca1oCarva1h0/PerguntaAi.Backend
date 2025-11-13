// Ficheiro: Controllers/QuizController.cs

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PerguntaAi.Backend.Models; // Importa os DTOs

[ApiController]
[Route("api/[controller]")] // Rota base: /api/quiz
public class QuizController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public QuizController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    //=========================================================
    // CREATE (Devolve o objeto completo com IDs)
    // POST /api/quiz
    //=========================================================
    [HttpPost]
    public async Task<IActionResult> CreateFullQuiz([FromBody] CreateQuizRequest request)
    {
        var quizResponse = new QuizResponse
        {
            Title = request.Title,
            Description = request.Description,
            TimePerQuestion = request.TimePerQuestion,
            Questions = new List<QuestionResponse>()
        };

        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        await using var transaction = await conn.BeginTransactionAsync();

        try
        {
            // 1. Inserir o Quiz
            var quizSql = "INSERT INTO Quiz (quiz_id, title, description, time_per_question, allow_powerups, is_published, version, created_at) " +
                          "VALUES (uuid_generate_v4(), @title, @description, @time_per_question, @allow_powerups, true, 1, NOW()) " +
                          "RETURNING quiz_id";

            await using var quizCmd = new NpgsqlCommand(quizSql, conn, transaction);
            quizCmd.Parameters.AddWithValue("title", request.Title);
            quizCmd.Parameters.AddWithValue("description", request.Description ?? (object)DBNull.Value);
            quizCmd.Parameters.AddWithValue("time_per_question", request.TimePerQuestion);
            quizCmd.Parameters.AddWithValue("allow_powerups", request.AllowPowerups);

            quizResponse.QuizId = (Guid)await quizCmd.ExecuteScalarAsync();

            // 2. Loop para inserir as Perguntas
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

                // 3. Loop para inserir as Opções
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

    //=========================================================
    // READ (Ver todos os quizzes)
    // GET /api/quiz
    //=========================================================
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

    //=========================================================
    // READ (Single) (Ver um quiz completo com perguntas e opções)
    // GET /api/quiz/{id}
    //=========================================================
    [HttpGet("{id}")]
    public async Task<IActionResult> GetQuizById(Guid id)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        try
        {
            var quizSql = "SELECT quiz_id, title, description, time_per_question " +
                          "FROM Quiz WHERE quiz_id = @quiz_id";

            await using var quizCmd = new NpgsqlCommand(quizSql, conn);
            quizCmd.Parameters.AddWithValue("quiz_id", id);

            await using var quizReader = await quizCmd.ExecuteReaderAsync();
            if (!await quizReader.ReadAsync())
            {
                return NotFound(new { message = "Quiz não encontrado." });
            }

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

            var questionsSql = "SELECT question_id, text, type, order_index, points_base " +
                               "FROM Question WHERE quiz_id = @quiz_id ORDER BY order_index";

            await using var questionCmd = new NpgsqlCommand(questionsSql, conn);
            questionCmd.Parameters.AddWithValue("quiz_id", id);

            await using var questionReader = await questionCmd.ExecuteReaderAsync();
            while (await questionReader.ReadAsync())
            {
                var question = new QuestionResponse
                {
                    QuestionId = questionReader.GetGuid(0),
                    Text = questionReader.GetString(1),
                    Type = questionReader.GetString(2),
                    OrderIndex = questionReader.GetInt32(3),
                    PointsBase = questionReader.GetInt32(4),
                    Options = new List<OptionResponse>()
                };
                questionsMap.Add(question.QuestionId, question);
                quizResponse.Questions.Add(question);
            }
            await questionReader.CloseAsync();

            var optionsSql = "SELECT option_id, question_id, text, is_correct, option_index " +
                             "FROM QuestionOption " +
                             "WHERE question_id IN (SELECT question_id FROM Question WHERE quiz_id = @quiz_id) " +
                             "ORDER BY option_index";

            await using var optionCmd = new NpgsqlCommand(optionsSql, conn);
            optionCmd.Parameters.AddWithValue("quiz_id", id);

            await using var optionReader = await optionCmd.ExecuteReaderAsync();
            while (await optionReader.ReadAsync())
            {
                var option = new OptionResponse
                {
                    OptionId = optionReader.GetGuid(0),
                    Text = optionReader.GetString(2),
                    IsCorrect = optionReader.GetBoolean(3),
                    OptionIndex = optionReader.GetString(4)
                };

                var questionId = optionReader.GetGuid(1);

                if (questionsMap.TryGetValue(questionId, out var parentQuestion))
                {
                    parentQuestion.Options.Add(option);
                }
            }

            return Ok(quizResponse);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Erro ao obter quiz: {ex.Message}" });
        }
    }

    //=========================================================
    // DELETE (Apagar um quiz e tudo associado)
    // DELETE /api/quiz/{id}
    //=========================================================
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteQuiz(Guid id)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        await using var transaction = await conn.BeginTransactionAsync();
        try
        {
            // Lógica de "delete em cascata"

            var sqlDeleteAnswers = "DELETE FROM Answer WHERE room_player_id IN (SELECT room_player_id FROM RoomPlayer WHERE room_id IN (SELECT room_id FROM Room WHERE quiz_id = @quiz_id))";
            await using (var cmd = new NpgsqlCommand(sqlDeleteAnswers, conn, transaction))
            {
                cmd.Parameters.AddWithValue("quiz_id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            var sqlDeleteLeaderboard = "DELETE FROM LeaderboardEntry WHERE room_id IN (SELECT room_id FROM Room WHERE quiz_id = @quiz_id)";
            await using (var cmd = new NpgsqlCommand(sqlDeleteLeaderboard, conn, transaction))
            {
                cmd.Parameters.AddWithValue("quiz_id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            var sqlDeleteRoomPlayer = "DELETE FROM RoomPlayer WHERE room_id IN (SELECT room_id FROM Room WHERE quiz_id = @quiz_id)";
            await using (var cmd = new NpgsqlCommand(sqlDeleteRoomPlayer, conn, transaction))
            {
                cmd.Parameters.AddWithValue("quiz_id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            var sqlDeleteRoom = "DELETE FROM Room WHERE quiz_id = @quiz_id";
            await using (var cmd = new NpgsqlCommand(sqlDeleteRoom, conn, transaction))
            {
                cmd.Parameters.AddWithValue("quiz_id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            var optionSql = "DELETE FROM QuestionOption WHERE question_id IN (SELECT question_id FROM Question WHERE quiz_id = @quiz_id)";
            await using (var cmd = new NpgsqlCommand(optionSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("quiz_id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            var questionSql = "DELETE FROM Question WHERE quiz_id = @quiz_id";
            await using (var cmd = new NpgsqlCommand(questionSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("quiz_id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            await using (var cmd = new NpgsqlCommand("DELETE FROM QuizArchive WHERE quiz_id = @quiz_id", conn, transaction))
            {
                cmd.Parameters.AddWithValue("quiz_id", id);
                await cmd.ExecuteNonQueryAsync();
            }

            var quizSql = "DELETE FROM Quiz WHERE quiz_id = @quiz_id";
            await using (var cmd = new NpgsqlCommand(quizSql, conn, transaction))
            {
                cmd.Parameters.AddWithValue("quiz_id", id);
                var rowsAffected = await cmd.ExecuteNonQueryAsync();

                if (rowsAffected == 0)
                {
                    await transaction.RollbackAsync();
                    return NotFound(new { message = "Quiz não encontrado." });
                }
            }

            await transaction.CommitAsync();
            return Ok(new { message = "Quiz e todos os seus dados foram apagados." });
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            return StatusCode(500, new { error = $"Erro ao apagar quiz: {ex.Message}" });
        }
    }
}