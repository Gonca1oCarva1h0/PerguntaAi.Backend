// Ficheiro: Models/QuizDtos.cs
using System;
using System.Collections.Generic;

// Define o namespace (mude se o nome do seu projeto for diferente)
namespace PerguntaAi.Backend.Models
{
    // --- DTOs de REQUEST (o que o Postman ENVIA) ---

    // Usado para: POST /api/quiz
    public class CreateQuizRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public int TimePerQuestion { get; set; }
        public bool AllowPowerups { get; set; } = false;
        public List<QuestionRequest> Questions { get; set; }
    }

    // Usado para: POST /api/quiz E POST /api/quiz/{id}/question
    public class QuestionRequest
    {
        public string Text { get; set; }
        public string Type { get; set; }
        public int OrderIndex { get; set; }
        public int PointsBase { get; set; }
        public List<OptionRequest> Options { get; set; }
    }

    // Usado dentro dos DTOs de Request
    public class OptionRequest
    {
        public string Text { get; set; }
        public bool IsCorrect { get; set; }
        public string OptionIndex { get; set; }
    }

    // Usado para: PUT /api/question/{id}
    public class QuestionUpdateRequest
    {
        public string Text { get; set; }
        public string Type { get; set; }
        public int OrderIndex { get; set; }
        public int PointsBase { get; set; }
    }

    // Usado para: POST /api/room
    public class CreateRoomRequest
    {
        public Guid QuizId { get; set; }
    }

    // Usado para: POST /api/playerprofile/register
    public class RegisterRequest
    {
        public string FirebaseUid { get; set; }
        public string DisplayName { get; set; }
    }


    // --- DTOs de RESPONSE (o que o Servidor DEVOLVE) ---

    // Usado para: Resposta do POST /api/quiz e GET /api/quiz/{id}
    public class QuizResponse
    {
        public Guid QuizId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int TimePerQuestion { get; set; }
        public List<QuestionResponse> Questions { get; set; }
    }

    // Usado dentro do QuizResponse e GET /api/question/{id}
    public class QuestionResponse
    {
        public Guid QuestionId { get; set; }
        public string Text { get; set; }
        public string Type { get; set; }
        public int OrderIndex { get; set; }
        public int PointsBase { get; set; }
        public List<OptionResponse> Options { get; set; }
    }

    // Usado dentro do QuestionResponse
    public class OptionResponse
    {
        public Guid OptionId { get; set; }
        public string Text { get; set; }
        public string OptionIndex { get; set; }
        public bool IsCorrect { get; set; }
    }
}