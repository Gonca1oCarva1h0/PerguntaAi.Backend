using Microsoft.AspNetCore.SignalR;

namespace PerguntaAi.Backend.Hubs
{
    public class GameHub : Hub
    {
        // Jogadores entram num "grupo" baseado no ID da sala
        public async Task JoinRoomGroup(string roomId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
        }

        // Quando o admin clica em "Start", todos na sala recebem o evento
        public async Task StartGame(string roomId)
        {
            await Clients.Group(roomId).SendAsync("GameStarted");
        }

        // Notificar quando alguém responde para atualizar o progresso (opcional)
        public async Task SendProgress(string roomId, string playerName)
        {
            await Clients.Group(roomId).SendAsync("PlayerAnswered", playerName);
        }
    }
}