using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PerguntaAi.Backend.Models;

[ApiController]
[Route("api/[controller]")]
public class RoomController : ControllerBase
{
    private readonly IConfiguration _configuration;
    public RoomController(IConfiguration config) => _configuration = config;

    [HttpPost]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        try
        {
            var roomId = Guid.NewGuid();
            string pin = Random.Shared.Next(100000, 999999).ToString();
            await using var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
            await conn.OpenAsync();
            var sql = "INSERT INTO public.room (room_id, quiz_id, pin_code, status, created_at, max_players) VALUES (@id, @qid, @p, 'WAITING', NOW(), 50)";
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", roomId);
            cmd.Parameters.AddWithValue("qid", request.QuizId);
            cmd.Parameters.AddWithValue("p", pin);
            await cmd.ExecuteNonQueryAsync();
            return Ok(new { roomId, pinCode = pin });
        }
        catch (Exception ex) { return StatusCode(500, ex.Message); }
    }

    [HttpPost("join")]
    public async Task<IActionResult> JoinRoom([FromBody] JoinRoomRequest request)
    {
        await using var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await conn.OpenAsync();
        var roomId = await new NpgsqlCommand("SELECT room_id FROM public.room WHERE pin_code = @p AND status = 'WAITING'", conn)
        { Parameters = { new("p", request.PinCode) } }.ExecuteScalarAsync();

        if (roomId == null) return NotFound("Sala não encontrada.");

        var rpId = Guid.NewGuid();
        var sql = "INSERT INTO public.roomplayer (room_player_id, room_id, player_id, display_name, join_time, is_host, status, total_points) VALUES (@rp, @r, @p, @n, NOW(), false, 'ACTIVE', 0)";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("rp", rpId);
        cmd.Parameters.AddWithValue("r", (Guid)roomId);
        cmd.Parameters.AddWithValue("p", request.PlayerId);
        cmd.Parameters.AddWithValue("n", request.DisplayName);
        await cmd.ExecuteNonQueryAsync();
        return Ok(new { roomPlayerId = rpId });
    }

    [HttpGet("{id}/leaderboard")]
    public async Task<IActionResult> GetLeaderboard(Guid id)
    {
        var list = new List<object>();
        await using var conn = new NpgsqlConnection(_configuration.GetConnectionString("DefaultConnection"));
        await conn.OpenAsync();
        await using var r = await new NpgsqlCommand("SELECT display_name, total_points FROM public.roomplayer WHERE room_id = @id ORDER BY total_points DESC", conn)
        { Parameters = { new("id", id) } }.ExecuteReaderAsync();
        while (await r.ReadAsync()) list.Add(new { Name = r.GetString(0), Points = r.GetInt32(1) });
        return Ok(list);
    }
}