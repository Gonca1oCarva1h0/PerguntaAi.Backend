using System;
using System.Collections.Generic;

namespace PerguntaAi.Backend.Models
{
    // --- AUTH & PLAYER ---
    public class RegisterRequest
    {
        public string FirebaseUid { get; set; }
        public string DisplayName { get; set; }
    }

    public class UpdatePlayerRequest
    {
        public string PreferredName { get; set; }
    }

    // --- QUIZ CREATE ---
    public class CreateQuizRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public int TimePerQuestion { get; set; }
        public bool AllowPowerups { get; set; }
        public List<QuestionRequest> Questions { get; set; }
    }

    public class QuestionRequest
    {
        public string Text { get; set; }
        public string Type { get; set; }
        public int OrderIndex { get; set; }
        public int PointsBase { get; set; }
        public List<OptionRequest> Options { get; set; }
    }

    public class OptionRequest
    {
        public string Text { get; set; }
        public bool IsCorrect { get; set; }
        public string OptionIndex { get; set; }
    }

    // --- QUIZ UPDATE ---
    public class QuizUpdateRequest
    {
        public string Title { get; set; }
        public string Description { get; set; }
    }

    public class QuestionUpdateRequest
    {
        public string Text { get; set; }
        public string Type { get; set; }
        public int OrderIndex { get; set; }
        public int PointsBase { get; set; }
    }

    // --- ROOM & GAMEPLAY ---
    public class CreateRoomRequest
    {
        public Guid QuizId { get; set; }
    }

    public class JoinRoomRequest
    {
        public string PinCode { get; set; }
        public Guid PlayerId { get; set; }
        public string DisplayName { get; set; }
    }

    public class AnswerRequest
    {
        public Guid RoomPlayerId { get; set; }
        public Guid QuestionId { get; set; }
        public Guid SelectedOptionId { get; set; }
    }

    // --- RESPONSES (OUTPUT) ---
    public class QuizResponse
    {
        public Guid QuizId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public int TimePerQuestion { get; set; }
        public List<QuestionResponse> Questions { get; set; }
    }

    public class QuestionResponse
    {
        public Guid QuestionId { get; set; }
        public string Text { get; set; }
        public string Type { get; set; }
        public int OrderIndex { get; set; }
        public int PointsBase { get; set; }
        public List<OptionResponse> Options { get; set; }
    }

    public class OptionResponse
    {
        public Guid OptionId { get; set; }
        public string Text { get; set; }
        public bool IsCorrect { get; set; }
        public string OptionIndex { get; set; }
    }
}