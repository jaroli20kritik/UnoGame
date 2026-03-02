using System;
using System.Collections.Generic;
using System.Linq;

namespace UnoGame.API.Models
{
    public enum CardColor { Red, Green, Blue, Yellow, Wild, Black, Teal, Pink, Orange, Purple }
    public enum CardType
    {
        Number, Skip, Reverse, DrawTwo, Wild, WildDrawFour,
        // Special deck cards (Action Packed)
        SwapHands, DiscardAll, DrawFive, WildShuffleHands,
        // Flip Edition (Light)
        Flip, 
        // Flip Edition (Dark)
        SkipEveryone, DrawFiveDark, WildDrawColor, DarkNumber, DarkSkip, DarkReverse, DarkFlip, DarkWild,
        // Flash card
        FlashWild
    }
    public enum DeckType
    {
        Classic,
        ActionPacked,
        FlipEdition,
        NoPeekMode
    }

    public enum GameState { Waiting, Playing, Finished }
    public enum GameDirection { Clockwise, CounterClockwise }

    public class Card
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public CardColor Color { get; set; }
        public CardType Type { get; set; }
        public int? Number { get; set; }
        
        // Flip Edition Dark Side
        public CardColor? DarkColor { get; set; }
        public CardType? DarkType { get; set; }
        public int? DarkNumber { get; set; }

        public bool IsWild => Color == CardColor.Wild || Color == CardColor.Black;
        public bool IsDarkWild => DarkColor == CardColor.Wild || DarkColor == CardColor.Black;

        public string DisplayName => GetDisplayName();
        public int Points => GetPoints();

        private string GetDisplayName()
        {
            if (Type == CardType.Number) return $"{Color} {Number}";
            return $"{(IsWild ? "" : Color + " ")}{Type}";
        }

        private int GetPoints()
        {
            return Type switch
            {
                CardType.Number => Number ?? 0,
                CardType.DarkNumber => DarkNumber ?? 0,
                CardType.Skip or CardType.Reverse or CardType.DrawTwo => 20,
                CardType.DarkSkip or CardType.DarkReverse or CardType.DrawFiveDark => 20,
                CardType.Wild or CardType.WildDrawFour => 50,
                CardType.DarkWild or CardType.WildDrawColor => 50,
                CardType.DrawFive or CardType.SwapHands or CardType.DiscardAll => 30,
                CardType.Flip or CardType.DarkFlip or CardType.SkipEveryone => 25,
                CardType.WildShuffleHands or CardType.FlashWild => 50,
                _ => 0
            };
        }
    }

    public class Player
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public string ConnectionId { get; set; } = "";
        public List<Card> Hand { get; set; } = new();
        public bool IsBot { get; set; } = false;
        public int Score { get; set; } = 0;
        public bool HasCalledUno { get; set; } = false;
        public bool IsConnected { get; set; } = true;
        public bool HasDrawnThisTurn { get; set; } = false;
    }

    public class GameRoom
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public string Name { get; set; } = "";
        public DeckType DeckType { get; set; } = DeckType.Classic;
        public List<Player> Players { get; set; } = new();
        public List<Card> DrawPile { get; set; } = new();
        public List<Card> DiscardPile { get; set; } = new();
        public GameState State { get; set; } = GameState.Waiting;
        public GameDirection Direction { get; set; } = GameDirection.Clockwise;
        public int CurrentPlayerIndex { get; set; } = 0;
        public CardColor? CurrentWildColor { get; set; }
        public bool IsDarkSide { get; set; } = false;
        public int MaxPlayers { get; set; } = 6;
        public int MinPlayers { get; set; } = 2;
        public bool HasPassword { get; set; } = false;
        public string? Password { get; set; }
        public int BotCount { get; set; } = 0;
        public List<string> EventLog { get; set; } = new();
        public string? WinnerId { get; set; }
        public int RoundNumber { get; set; } = 1;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }

        public Player? CurrentPlayer => Players.Count > 0 ? Players[CurrentPlayerIndex % Players.Count] : null;
        public Card? TopCard => DiscardPile.LastOrDefault();

        public void AddEvent(string message) {
            EventLog.Add($"[{DateTime.UtcNow:HH:mm:ss}] {message}");
            if (EventLog.Count > 100) EventLog.RemoveAt(0);
        }
    }

    // DTOs
    public class CreateRoomRequest
    {
        public string RoomName { get; set; } = "";
        public string PlayerName { get; set; } = "";
        public DeckType DeckType { get; set; } = DeckType.Classic;
        public int MaxPlayers { get; set; } = 6;
        public int BotCount { get; set; } = 0;
        public string? Password { get; set; }
    }

    public class JoinRoomRequest
    {
        public string PlayerName { get; set; } = "";
        public string? Password { get; set; }
    }

    public class PlayCardRequest
    {
        public string CardId { get; set; } = "";
        public CardColor? ChosenColor { get; set; }
    }

    public class GameStateDto
    {
        public string RoomId { get; set; } = "";
        public string RoomName { get; set; } = "";
        public DeckType DeckType { get; set; }
        public GameState State { get; set; }
        public GameDirection Direction { get; set; }
        public CardColor? CurrentWildColor { get; set; }
        public bool IsDarkSide { get; set; }
        public Card? TopCard { get; set; }
        public int DrawPileCount { get; set; }
        public string? CurrentPlayerId { get; set; }
        public string? WinnerId { get; set; }
        public int RoundNumber { get; set; }
        public List<PlayerDto> Players { get; set; } = new();
        public List<Card> YourHand { get; set; } = new();
        public List<string> EventLog { get; set; } = new();
    }

    public class PlayerDto
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public int HandCount { get; set; }
        public bool IsBot { get; set; }
        public int Score { get; set; }
        public bool HasCalledUno { get; set; }
        public bool IsConnected { get; set; }
        public bool HasDrawnThisTurn { get; set; }
        public bool IsCurrentPlayer { get; set; }
        public List<Card> VisibleHand { get; set; } = new();
    }
}
