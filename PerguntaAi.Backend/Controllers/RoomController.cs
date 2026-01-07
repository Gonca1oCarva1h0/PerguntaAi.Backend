using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PerguntaAi.Backend.Models;

[ApiController]
[Route("api/[controller]")]
public class RoomController : ControllerBase
{
    private readonly IConfiguration _configuration;
    public RoomController(IConfiguration configuration) => _configuration = configuration;

    // POST: api/room (Cria a sala e gera o PIN)
    [HttpPost]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        string roomCode = new Random().Next(100000, 999999).ToString(); // Gera PIN de 6 dígitos

        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var sql = "INSERT INTO Room (room_id, quiz_id, room_code, status, created_at) " +
                  "VALUES (uuid_generate_v4(), @quiz, @code, 'waiting', NOW()) RETURNING room_id";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("quiz", request.QuizId);
        cmd.Parameters.AddWithValue("code", roomCode);

        var roomId = await cmd.ExecuteScalarAsync();
        return Ok(new { roomId, roomCode }); // Retorna o PIN para quem criou a sala
    }

    // POST: api/room/join (Jogador usa o PIN para entrar)
    [HttpPost("join")]
    public async Task<IActionResult> JoinRoom([FromBody] JoinRoomRequest request)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // 1. Verifica se a sala existe e está à espera
        var roomSql = "SELECT room_id FROM Room WHERE room_code = @pin AND status = 'waiting'";
        await using var roomCmd = new NpgsqlCommand(roomSql, conn);
        roomCmd.Parameters.AddWithValue("pin", request.PinCode);
        var roomId = await roomCmd.ExecuteScalarAsync();

        if (roomId == null) return NotFound("Sala não encontrada ou já em jogo.");

        // 2. Cria o RoomPlayer (O ID gerado aqui é usado para responder)
        var joinSql = "INSERT INTO RoomPlayer (room_player_id, room_id, player_id, total_points, joined_at) " +
                      "VALUES (uuid_generate_v4(), @room, @player, 0, NOW()) RETURNING room_player_id";

        await using var joinCmd = new NpgsqlCommand(joinSql, conn);
        joinCmd.Parameters.AddWithValue("room", roomId);
        joinCmd.Parameters.AddWithValue("player", request.PlayerId);

        var roomPlayerId = await joinCmd.ExecuteScalarAsync();
        return Ok(new { roomPlayerId, roomId });
    }

    // GET: api/room/{id}/leaderboard
    [HttpGet("{id}/leaderboard")]
    public async Task<IActionResult> GetLeaderboard(Guid id)
    {
        var leaderboard = new List<object>();
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var sql = "SELECT p.preferred_name, rp.total_points " +
                  "FROM RoomPlayer rp JOIN PlayerProfile p ON rp.player_id = p.player_id " +
                  "WHERE rp.room_id = @id ORDER BY rp.total_points DESC";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            leaderboard.Add(new { name = reader.GetString(0), points = reader.GetInt32(1) });
        }
        return Ok(leaderboard);
    }
}