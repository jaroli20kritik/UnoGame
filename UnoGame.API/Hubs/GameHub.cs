using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using UnoGame.API.Models;
using UnoGame.API.Services;

namespace UnoGame.API.Hubs
{
    public class GameHub : Hub
    {
        private readonly GameService _gameService;

        public GameHub(GameService gameService)
        {
            _gameService = gameService;
        }

        public async Task JoinGameRoom(string roomId, string playerId)
        {
            await Groups.AddToGroupAsync(Context.ConnectionId, roomId);
            var room = _gameService.GetRoom(roomId);
            if (room != null)
            {
                var player = room.Players.Find(p => p.Id == playerId);
                if (player != null) player.ConnectionId = Context.ConnectionId;
                await BroadcastState(roomId, playerId);
            }
        }

        public async Task StartGame(string roomId, string playerId)
        {
            var (success, error) = _gameService.StartGame(roomId, playerId);
            if (success)
            {
                await BroadcastState(roomId, null);
                await ProcessBots(roomId);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", error);
            }
        }

        public async Task PlayCard(string roomId, string playerId, string cardId, string? colorStr)
        {
            CardColor? color = null;
            if (!string.IsNullOrEmpty(colorStr) && Enum.TryParse<CardColor>(colorStr, out var c)) color = c;

            var (success, error) = _gameService.PlayCard(roomId, playerId, new PlayCardRequest { CardId = cardId, ChosenColor = color });
            if (success)
            {
                await BroadcastState(roomId, null);
                await ProcessBots(roomId);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", error);
            }
        }

        public async Task DrawCard(string roomId, string playerId)
        {
            var (success, card, error) = _gameService.DrawCard(roomId, playerId);
            if (success)
            {
                await BroadcastState(roomId, null);
                await ProcessBots(roomId);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", error);
            }
        }

        public async Task PassTurn(string roomId, string playerId)
        {
            var (success, error) = _gameService.PassTurn(roomId, playerId);
            if (success)
            {
                await BroadcastState(roomId, null);
                await ProcessBots(roomId);
            }
            else
            {
                await Clients.Caller.SendAsync("Error", error);
            }
        }

        public async Task CallUno(string roomId, string playerId)
        {
            _gameService.CallUno(roomId, playerId);
            await BroadcastState(roomId, null);
        }

        public async Task CatchUno(string roomId, string catcherId, string targetId)
        {
            var (success, error) = _gameService.CatchUno(roomId, catcherId, targetId);
            if (success)
                await BroadcastState(roomId, null);
            else
                await Clients.Caller.SendAsync("Error", error);
        }

        private async Task BroadcastState(string roomId, string? specificPlayerId)
        {
            var room = _gameService.GetRoom(roomId);
            if (room == null) return;

            foreach (var player in room.Players)
            {
                if (player.IsBot) continue;
                var state = _gameService.GetStateForPlayer(roomId, player.Id);
                if (!string.IsNullOrEmpty(player.ConnectionId))
                    await Clients.Client(player.ConnectionId).SendAsync("GameStateUpdate", state);
            }
            // Also send to anyone in the group who may not have a player id yet
            var publicState = _gameService.GetStateForPlayer(roomId, "");
            await Clients.Group(roomId).SendAsync("RoomUpdate", publicState);
        }

        private async Task ProcessBots(string roomId)
        {
            var room = _gameService.GetRoom(roomId);
            if (room == null || room.State != GameState.Playing) return;

            // Process all consecutive bot turns
            int maxBotTurns = 20;
            while (room.CurrentPlayer?.IsBot == true && room.State == GameState.Playing && maxBotTurns-- > 0)
            {
                await Task.Delay(1200);
                _gameService.ProcessBotTurns(roomId);
                await BroadcastState(roomId, null);
                room = _gameService.GetRoom(roomId);
                if (room == null) break;
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }
}
