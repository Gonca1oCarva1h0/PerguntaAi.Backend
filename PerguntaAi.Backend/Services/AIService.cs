using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using PerguntaAi.Backend.Models;

namespace PerguntaAi.Backend.Services
{
    public class AIService
    {
        // ⚠️ SUBSTITUI ISTO PELA TUA CHAVE DO GOOGLE GEMINI (Cria em aistudio.google.com)
        private readonly string _apiKey = "AIzaSyB4y_uVgnNYGCMDfgJELPVMeW0GDfgwTDw";

        public async Task<CreateQuizRequest> GenerateQuizAsync(string theme)
        {
            using var client = new HttpClient();

            // URL oficial do Gemini 1.5 Flash
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/gemini-2.5-flash:generateContent?key={_apiKey}";

            // 1. Número aleatório de perguntas (5 a 10)
            int qtdPerguntas = new Random().Next(5, 11);

            // 2. Prompt
            var prompt = $@"Gera um quiz sobre '{theme}' em PT-PT com {qtdPerguntas} perguntas.
    
            REGRAS DE CONTEÚDO:
            1. Varia entre 'MULTIPLE_CHOICE' e 'WRITTEN'.
            2. Para 'WRITTEN': A resposta correta deve ser MUITO BREVE (máximo 1 a 3 palavras).

            REGRAS TÉCNICAS (JSON):
            1. Responde APENAS com JSON.
            2. O campo 'optionIndex' tem de ser SEMPRE STRING (ex: ""A"", ""1"").

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

            // 3. Estrutura do Body para o Gemini
            var requestBody = new
            {
                contents = new[]
                {
                    new { parts = new[] { new { text = prompt } } }
                },
                generationConfig = new
                {
                    response_mime_type = "application/json"
                }
            };

            var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

            // Envio do pedido
            var response = await client.PostAsync(url, content);
            var jsonResponse = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
                throw new Exception($"Erro Gemini: {jsonResponse}");

            // 4. Parse da Resposta
            using var doc = JsonDocument.Parse(jsonResponse);

            var candidates = doc.RootElement.GetProperty("candidates");
            if (candidates.GetArrayLength() == 0) throw new Exception("Gemini não gerou resposta.");

            var rawJson = candidates[0]
                            .GetProperty("content")
                            .GetProperty("parts")[0]
                            .GetProperty("text")
                            .GetString();

            // Limpeza de segurança
            var cleanJson = rawJson.Replace("```json", "").Replace("```", "").Trim();

            var result = JsonSerializer.Deserialize<CreateQuizRequest>(cleanJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (result == null) throw new Exception("JSON inválido recebido da IA.");
            if (result.Questions == null) result.Questions = new List<QuestionRequest>();

            return result;
        }
    }
}