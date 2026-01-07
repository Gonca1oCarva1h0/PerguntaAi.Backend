using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using PerguntaAi.Backend.Models;

namespace PerguntaAi.Backend.Services
{
    public class AIService
    {
        private readonly string _apiKey = "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";
        private readonly string _apiUrl = "https://api.groq.com/openai/v1/chat/completions";

        public async Task<CreateQuizRequest> GenerateQuizAsync(string theme)
        {
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);

            var prompt = $@"Gera um quiz sobre '{theme}' em português. 
            Responde APENAS com JSON puro no formato:
            {{
              ""title"": ""string"",
              ""description"": ""string"",
              ""timePerQuestion"": 30,
              ""questions"": [
                {{
                  ""text"": ""string"",
                  ""type"": ""MULTIPLE_CHOICE"",
                  ""orderIndex"": 1,
                  ""pointsBase"": 100,
                  ""options"": [
                    {{ ""text"": ""string"", ""isCorrect"": true, ""optionIndex"": ""A"" }},
                    {{ ""text"": ""string"", ""isCorrect"": false, ""optionIndex"": ""B"" }}
                  ]
                }}
              ]
            }}";

            var requestBody = new
            {
                model = "llama-3.3-70b-versatile",
                messages = new[] { new { role = "user", content = prompt } },
                response_format = new { type = "json_object" }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
            var response = await client.PostAsync(_apiUrl, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Erro Groq: {jsonResponse}");

            using var doc = JsonDocument.Parse(jsonResponse);
            var rawJson = doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

            // Criada a variável cleanJson para limpar possíveis marcações da IA
            var cleanJson = rawJson.Replace("```json", "").Replace("```", "").Trim();

            var result = JsonSerializer.Deserialize<CreateQuizRequest>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null) throw new Exception("A IA devolveu um formato inválido.");

            // Inicializa a lista se for nula para evitar o erro de referência
            if (result.Questions == null) result.Questions = new List<QuestionRequest>();

            return result;
        }
    }
}