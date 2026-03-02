using System.Linq;
using Microsoft.AspNetCore.Mvc;
using UnoGame.API.Models;
using UnoGame.API.Services;

namespace UnoGame.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class GameController : ControllerBase
    {
        private readonly GameService _gameService;
        public GameController(GameService gameService) => _gameService = gameService;

        [HttpGet("rooms")]
        public IActionResult GetRooms() =>
            Ok(_gameService.GetPublicRooms().Select(r => new
            {
                r.Id, r.Name, r.DeckType, r.State,
                PlayerCount = r.Players.Count,
                r.MaxPlayers, r.HasPassword
            }));

        [HttpPost("rooms")]
        public IActionResult CreateRoom([FromBody] CreateRoomRequest req)
        {
            var (room, player) = _gameService.CreateRoom(req);
            return Ok(new { roomId = room.Id, playerId = player.Id, playerName = player.Name });
        }

        [HttpPost("rooms/{roomId}/join")]
        public IActionResult JoinRoom(string roomId, [FromBody] JoinRoomRequest req)
        {
            var (room, player, error) = _gameService.JoinRoom(roomId, req);
            if (error != null) return BadRequest(new { error });
            return Ok(new { roomId = room!.Id, playerId = player!.Id, playerName = player.Name });
        }

        [HttpGet("rooms/{roomId}/state/{playerId}")]
        public IActionResult GetState(string roomId, string playerId)
        {
            var state = _gameService.GetStateForPlayer(roomId, playerId);
            return Ok(state);
        }

        [HttpGet("rooms/{roomId}")]
        public IActionResult GetRoom(string roomId)
        {
            var room = _gameService.GetRoom(roomId);
            if (room == null) return NotFound();
            return Ok(new { room.Id, room.Name, room.DeckType, room.State, room.MaxPlayers, PlayerCount = room.Players.Count, room.HasPassword });
        }
    }
}
