// Ficheiro: Controllers/PlayerProfileController.cs

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Npgsql;
using System;
using System.Threading.Tasks;
using PerguntaAi.Backend.Models; // Importa o DTO

[ApiController]
[Route("api/[controller]")] // Rota base: /api/playerprofile
public class PlayerProfileController : ControllerBase
{
    private readonly IConfiguration _configuration;

    public PlayerProfileController(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    //=========================================================
    // CREATE (Regista um jogador vindo do Firebase)
    // POST /api/playerprofile/register
    //=========================================================
    [HttpPost("register")]
    public async Task<IActionResult> RegisterPlayer([FromBody] RegisterRequest request)
    {
        if (request == null || string.IsNullOrEmpty(request.FirebaseUid) || string.IsNullOrEmpty(request.DisplayName))
        {
            return BadRequest("Dados inválidos.");
        }

        try
        {
            string connString = _configuration.GetConnectionString("DefaultConnection");
            await using var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();

            // Usa a tabela de 11 colunas
            var sql = "INSERT INTO PlayerProfile (player_id, external_ref, preferred_name, country, created_at, stats)" +
                      "VALUES (uuid_generate_v4(), @external_ref, @preferred_name, 'PT', NOW(), null)";

            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("external_ref", request.FirebaseUid);
            cmd.Parameters.AddWithValue("preferred_name", request.DisplayName);

            await cmd.ExecuteNonQueryAsync();

            return Ok(new { message = "Jogador registado com sucesso!" });
        }
        catch (NpgsqlException ex) when (ex.SqlState == "23505") // Unique violation
        {
            // Isto pode acontecer se o external_ref for único e o user tentar registar-se 2x
            return Conflict(new { error = "Este jogador (external_ref) já está registado." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { error = $"Erro interno do servidor: {ex.Message}" });
        }
    }
}