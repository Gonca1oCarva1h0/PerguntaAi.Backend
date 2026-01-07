using Microsoft.AspNetCore.SignalR;

namespace PerguntaAi.Backend.Hubs
{
    public class GameHub : Hub
    {
        public async Task JoinRoomGroup(string pinCode)
            => await Groups.AddToGroupAsync(Context.ConnectionId, pinCode);

        public async Task StartGame(string pinCode)
            => await Clients.Group(pinCode).SendAsync("GameStarted");

        public async Task SendQuestion(string pinCode, Guid questionId)
            => await Clients.Group(pinCode).SendAsync("ReceiveQuestion", questionId);
    }
}