using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PerguntaAi.Backend.Models;

[ApiController]
[Route("api/[controller]")]
public class RoomController : ControllerBase
{
    private readonly IConfiguration _configuration;
    public RoomController(IConfiguration configuration) => _configuration = configuration;

    [HttpPost("join")]
    public async Task<IActionResult> JoinRoom([FromBody] JoinRoomRequest request)
    {
        try
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            var roomSql = "SELECT room_id FROM public.room WHERE pin_code = @pin AND status = 'WAITING'";
            await using var roomCmd = new NpgsqlCommand(roomSql, conn);
            roomCmd.Parameters.AddWithValue("pin", request.PinCode);
            var roomId = await roomCmd.ExecuteScalarAsync();

            if (roomId == null) return NotFound("Sala não encontrada ou já em jogo.");

            Guid newRoomPlayerId = Guid.NewGuid();
            var joinSql = "INSERT INTO public.roomplayer (room_player_id, room_id, player_id, display_name, join_time, status, total_points, current_question_index) " +
                  "VALUES (@rp_id, @room, @player, @name, NOW(), 'ACTIVE', 0, 0)";

            await using var joinCmd = new NpgsqlCommand(joinSql, conn);
            joinCmd.Parameters.AddWithValue("rp_id", newRoomPlayerId);
            joinCmd.Parameters.AddWithValue("RoomId", roomId);
            joinCmd.Parameters.AddWithValue("player", request.PlayerId);
            joinCmd.Parameters.AddWithValue("name", request.DisplayName);

            await joinCmd.ExecuteNonQueryAsync();
            return Ok(new { roomPlayerId = newRoomPlayerId, roomId });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        try
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");
            string pinCode = new Random().Next(100000, 999999).ToString();
            Guid newRoomId = Guid.NewGuid();

            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // Inclui o host_admin_id na criação da sala
            var sql = "INSERT INTO public.room (room_id, quiz_id, pin_code, status, max_players, created_at, host_admin_id) " +
                      "VALUES (@id, @quiz, @code, 'WAITING', 50, NOW(), @host)";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("id", newRoomId);
            cmd.Parameters.AddWithValue("quiz", request.QuizId);
            cmd.Parameters.AddWithValue("code", pinCode);
            cmd.Parameters.AddWithValue("host", request.HostId);

            await cmd.ExecuteNonQueryAsync();
            return Ok(new { roomId = newRoomId, pinCode });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpGet("{id}/leaderboard")]
    public async Task<IActionResult> GetLeaderboard(Guid id)
    {
        var leaderboard = new List<object>();
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var sql = "SELECT display_name, total_points FROM public.roomplayer WHERE room_id = @id ORDER BY total_points DESC";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("RoomId", id);
        await using var reader = await cmd.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            leaderboard.Add(new { name = reader.GetString(0), points = reader.GetInt32(1) });
        }
        return Ok(leaderboard);
    }

    // POST: api/Room/{id}/start
    [HttpPost("{id}/start")]
    public async Task<IActionResult> StartRoom(Guid id, [FromQuery] Guid hostId)
    {
        try
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // Valida se quem está a tentar começar é o Host da sala
            var sql = "UPDATE public.room SET status = 'STARTED', started_at = NOW() " +
                      "WHERE room_id = @id AND host_admin_id = @hostId AND status = 'WAITING'";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("RoomId", id);
            cmd.Parameters.AddWithValue("hostId", hostId);

            int rowsAffected = await cmd.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return Unauthorized("Apenas o criador da sala pode iniciar o jogo.");
            }

            return Ok(new { message = "O jogo começou!", started_at = DateTime.Now });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    [HttpPost("{id}/end")]
    public async Task<IActionResult> EndRoom(Guid id, [FromQuery] Guid hostId)
    {
        try
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // Utilizando host_admin_id conforme solicitado
            var sql = "UPDATE public.room SET status = 'FINISHED', ended_at = NOW() " +
                      "WHERE room_id = @id AND host_admin_id = @hostId AND status = 'STARTED'";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("RoomId", id);
            cmd.Parameters.AddWithValue("hostId", hostId);

            int rowsAffected = await cmd.ExecuteNonQueryAsync();

            if (rowsAffected == 0)
            {
                return BadRequest(new { error = "Não foi possível encerrar a sala. Verifique o host_admin_id ou o estado da sala." });
            }

            var leaderboard = new List<object>();
            var leaderSql = "SELECT display_name, total_points FROM public.roomplayer WHERE room_id = @id ORDER BY total_points DESC";

            await using var leaderCmd = new NpgsqlCommand(leaderSql, conn);
            leaderCmd.Parameters.AddWithValue("id", id);
            await using var reader = await leaderCmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                leaderboard.Add(new
                {
                    name = reader.GetString(0),
                    points = reader.GetInt32(1)
                });
            }

            return Ok(new
            {
                message = "Jogo encerrado com sucesso!",
                ended_at = DateTime.UtcNow,
                leaderboard = leaderboard
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }
}