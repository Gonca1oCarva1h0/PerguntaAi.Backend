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
        await using var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await conn.OpenAsync();

        var cmdCheck = new NpgsqlCommand("SELECT qo.is_correct, q.points_base FROM public.questionoption qo JOIN public.question q ON qo.question_id = q.question_id WHERE qo.option_id = @o AND q.question_id = @q", conn);
        cmdCheck.Parameters.AddWithValue("o", request.SelectedOptionId);
        cmdCheck.Parameters.AddWithValue("q", request.QuestionId);

        using var reader = await cmdCheck.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return NotFound();
        bool correct = reader.GetBoolean(0);
        int pts = correct ? reader.GetInt32(1) : 0;
        await reader.CloseAsync();

        await new NpgsqlCommand("INSERT INTO public.answer (answer_id, room_player_id, question_id, selected_option_id, points_earned, created_at) VALUES (@id, @rp, @q, @o, @p, NOW())", conn)
        { Parameters = { new("id", Guid.NewGuid()), new("rp", request.RoomPlayerId), new("q", request.QuestionId), new("o", request.SelectedOptionId), new("p", pts) } }.ExecuteNonQueryAsync();

        if (correct)
            await new NpgsqlCommand("UPDATE public.roomplayer SET total_points = total_points + @p WHERE room_player_id = @rp", conn)
            { Parameters = { new("p", pts), new("rp", request.RoomPlayerId) } }.ExecuteNonQueryAsync();

        return Ok(new { correct, pointsEarned = pts });
    }
}