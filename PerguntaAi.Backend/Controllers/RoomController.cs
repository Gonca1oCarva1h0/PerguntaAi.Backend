// Ficheiro: Controllers/RoomController.cs

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Threading.Tasks;
using PerguntaAi.Backend.Models; // Importa o DTO

[ApiController]
[Route("api/[controller]")] // Rota base: /api/room
public class RoomController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private static readonly Random _random = new Random(); // Para gerar o PIN

    public RoomController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    //=========================================================
    // CREATE (Cria uma sala e devolve o PIN)
    // POST /api/room
    //=========================================================
    [HttpPost]
    public async Task<IActionResult> CreateRoom([FromBody] CreateRoomRequest request)
    {
        if (request == null || request.QuizId == Guid.Empty)
        {
            return BadRequest("QuizId inválido.");
        }

        try
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // TODO: Adicionar lógica para verificar se o PIN já existe
            string pinCode = _random.Next(100000, 999999).ToString();

            var sql = "INSERT INTO Room (room_id, quiz_id, pin_code, status, created_at, max_players)" +
                      "VALUES (uuid_generate_v4(), @quiz_id, @pin_code, 'WAITING', NOW(), 50)";

            await using var cmd = new NpgsqlCommand(sql, conn);

            cmd.Parameters.AddWithValue("quiz_id", request.QuizId);
            cmd.Parameters.AddWithValue("pin_code", pinCode);

            await cmd.ExecuteNonQueryAsync();

            return Ok(new { pinCode = pinCode });
        }
        catch (NpgsqlException ex) when (ex.SqlState == "23503") // Erro de Foreign Key
        {
            return BadRequest(new { error = $"O QuizId '{request.QuizId}' não existe. A sala não foi criada." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Erro interno do servidor: {ex.Message}" });
        }
    }
}