using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using PerguntaAi.Backend.Models;

[ApiController]
[Route("api/[controller]")]
public class PlayerProfileController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public PlayerProfileController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    [HttpPost("register")]
    public async Task<IActionResult> RegisterPlayer([FromBody] RegisterRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.FirebaseUid) || string.IsNullOrEmpty(request.DisplayName))
            return BadRequest("Dados inválidos.");

        try
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            var sql = "INSERT INTO PlayerProfile (player_id, external_ref, preferred_name, country, created_at, stats) " +
                      "VALUES (uuid_generate_v4(), @external_ref, @preferred_name, 'PT', NOW(), null) " +
                      "RETURNING player_id";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("external_ref", request.FirebaseUid);
            cmd.Parameters.AddWithValue("preferred_name", request.DisplayName);

            var playerId = await cmd.ExecuteScalarAsync();

            return Ok(new
            {
                message = "Jogador registado com sucesso!",
                player_id = playerId,
                // Retornamos também o ID formatado para o Unity apanhar logo no registo se quiser
                id = playerId
            });
        }
        catch (NpgsqlException ex) when (ex.SqlState == "23505")
        {
            return Conflict(new { error = "Este jogador já está registado." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = ex.Message });
        }
    }

    // Este mantém-se igual (busca pelo GUID da base de dados)
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPlayer(Guid id)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var sql = "SELECT player_id, external_ref, preferred_name, country, created_at FROM PlayerProfile WHERE player_id = @id";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("id", id);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return Ok(new
            {
                player_id = reader.GetGuid(0),
                external_ref = reader.GetString(1),
                preferred_name = reader.GetString(2),
                country = reader.GetString(3),
                created_at = reader.GetDateTime(4)
            });
        }
        return NotFound();
    }

    // --- NOVO MÉTODO (ADICIONADO AQUI) ---
    // Procura pelo ID do Firebase (external_ref) e devolve o GUID (player_id)
    [HttpGet("firebase/{uid}")]
    public async Task<IActionResult> GetPlayerByFirebase(string uid)
    {
        if (string.IsNullOrEmpty(uid))
            return BadRequest("Firebase UID inválido.");

        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        // AQUI ESTÁ A MAGIA: WHERE external_ref = @uid
        var sql = "SELECT player_id, external_ref, preferred_name, country, created_at FROM PlayerProfile WHERE external_ref = @uid";

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("uid", uid);

        await using var reader = await cmd.ExecuteReaderAsync();
        if (await reader.ReadAsync())
        {
            return Ok(new
            {
                // Campos essenciais para o Unity:
                id = reader.GetGuid(0),             // O GUID que precisamos para entrar na sala
                displayName = reader.GetString(2),  // O Nome

                // Campos originais do Backend:
                player_id = reader.GetGuid(0),
                external_ref = reader.GetString(1),
                preferred_name = reader.GetString(2),
                country = reader.GetString(3),
                created_at = reader.GetDateTime(4)
            });
        }
        return NotFound("Jogador não encontrado com esse Firebase UID.");
    }
    // -------------------------------------

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePlayer(Guid id, [FromBody] UpdatePlayerRequest request)
    {
        string connString = _configuration.GetConnectionString("DefaultConnection");
        await using var conn = new NpgsqlConnection(connString);
        await conn.OpenAsync();

        var sql = "UPDATE PlayerProfile SET preferred_name = @name WHERE player_id = @id";
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("name", request.PreferredName);
        cmd.Parameters.AddWithValue("id", id);

        int rows = await cmd.ExecuteNonQueryAsync();
        return rows > 0 ? NoContent() : NotFound();
    }
}