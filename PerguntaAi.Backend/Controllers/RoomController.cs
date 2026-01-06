using Microsoft.AspNetCore.Mvc;
using Npgsql;
using PerguntaAi.Backend.Models;

[ApiController]
[Route("api/[controller]")]
public class RoomController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public RoomController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    // Criar uma nova sala (Host)
    [HttpPost]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        if (request == null || request.QuizId == Guid.Empty)
            return BadRequest("QuizId inválido.");

        try
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // Geramos o ID e o PIN no C# para evitar erros de extensão no Azure
            var roomId = Guid.NewGuid();
            string pinCode = Random.Shared.Next(100000, 999999).ToString();

            var sql = @"INSERT INTO public.room (room_id, quiz_id, pin_code, status, created_at, max_players) 
                        VALUES (@room_id, @quiz_id, @pin_code, 'WAITING', NOW(), 50)";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("room_id", roomId);
            cmd.Parameters.AddWithValue("quiz_id", request.QuizId);
            cmd.Parameters.AddWithValue("pin_code", pinCode);

            await cmd.ExecuteNonQueryAsync();

            return Ok(new { roomId = roomId, pinCode = pinCode });
        }
        catch (NpgsqlException ex) when (ex.SqlState == "23503")
        {
            return BadRequest(new { error = $"O QuizId '{request.QuizId}' não existe." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Erro: {ex.Message}" });
        }
    }

    // Jogador entrar na sala via PIN
    [HttpPost("join")]
    public async Task<IActionResult> JoinRoom([FromBody] JoinRoomRequest request)
    {
        if (string.IsNullOrEmpty(request.PinCode))
            return BadRequest("PIN é obrigatório.");

        try
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // 1. Verificar se a sala existe e está ativa
            var findRoomSql = "SELECT room_id FROM public.room WHERE pin_code = @pin AND status = 'WAITING' LIMIT 1";
            await using var cmdFind = new NpgsqlCommand(findRoomSql, conn);
            cmdFind.Parameters.AddWithValue("pin", request.PinCode);
            var roomId = await cmdFind.ExecuteScalarAsync();

            if (roomId == null)
                return NotFound(new { error = "Sala não encontrada ou já iniciada." });

            // 2. Adicionar o jogador à sala
            var roomPlayerId = Guid.NewGuid();
            var joinSql = @"INSERT INTO public.roomplayer (room_player_id, room_id, player_id, display_name, join_time, is_host, status, total_points) 
                            VALUES (@rp_id, @room_id, @p_id, @name, NOW(), false, 'ACTIVE', 0)";

            await using var cmdJoin = new NpgsqlCommand(joinSql, conn);
            cmdJoin.Parameters.AddWithValue("rp_id", roomPlayerId);
            cmdJoin.Parameters.AddWithValue("room_id", (Guid)roomId);
            cmdJoin.Parameters.AddWithValue("p_id", request.PlayerId);
            cmdJoin.Parameters.AddWithValue("name", request.DisplayName);

            await cmdJoin.ExecuteNonQueryAsync();

            return Ok(new { roomPlayerId = roomPlayerId, roomId = roomId, message = "Entrou com sucesso!" });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}