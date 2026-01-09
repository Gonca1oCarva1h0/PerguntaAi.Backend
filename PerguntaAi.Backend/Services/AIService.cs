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

            // 1. Gera um número aleatório entre 5 e 10
            int qtdPerguntas = new Random().Next(5, 11);

            // 2. Prompt atualizado com instrução de brevidade
            var prompt = $@"Gera um quiz sobre '{theme}' em PT-PT com {qtdPerguntas} perguntas.
    
            REGRAS DE CONTEÚDO:
            1. Varia entre 'MULTIPLE_CHOICE' e 'WRITTEN'.
            2. Para 'WRITTEN': A resposta correta deve ser MUITO BREVE (máximo 1 a 3 palavras). Evita frases longas.

            REGRAS TÉCNICAS (JSON ESTRITO):
            1. Responde APENAS com JSON válido.
            2. O campo 'optionIndex' tem de ser SEMPRE UMA STRING entre aspas (ex: ""A"", ""1"").

            ESTRUTURA JSON OBRIGATÓRIA:
            {{
              ""title"": ""Título"",
              ""description"": ""Descrição"",
              ""timePerQuestion"": 30,
              ""questions"": [
                {{
                  ""text"": ""Pergunta?"",
                  ""type"": ""MULTIPLE_CHOICE"",
                  ""orderIndex"": 1,
                  ""pointsBase"": 100,
                  ""options"": [
                    {{ ""text"": ""Opção"", ""isCorrect"": true, ""optionIndex"": ""A"" }} 
                  ]
                }}
              ]
            }}
    
            Para 'WRITTEN', envia 1 opção correta com 'optionIndex': ""1"".";

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

            // Limpeza de segurança
            var cleanJson = rawJson.Replace("```json", "").Replace("```", "").Trim();

            var result = JsonSerializer.Deserialize<CreateQuizRequest>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null) throw new Exception("A IA devolveu um formato inválido.");
            if (result.Questions == null) result.Questions = new List<QuestionRequest>();

            return result;
        }
    }
}