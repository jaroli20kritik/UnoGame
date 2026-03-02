using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using UnoGame.API.Models;

namespace UnoGame.API.Services
{
    public class GameService
    {
        private readonly ConcurrentDictionary<string, GameRoom> _rooms = new();

        public IEnumerable<GameRoom> GetPublicRooms() =>
            _rooms.Values.Where(r => !r.HasPassword && r.State == GameState.Waiting);

        public GameRoom? GetRoom(string id) => _rooms.TryGetValue(id, out var r) ? r : null;

        public (GameRoom room, Player player) CreateRoom(CreateRoomRequest req)
        {
            var room = new GameRoom
            {
                Name = req.RoomName,
                DeckType = req.DeckType,
                MaxPlayers = req.MaxPlayers,
                HasPassword = !string.IsNullOrEmpty(req.Password),
                Password = req.Password
            };

            var player = new Player { Name = req.PlayerName };
            room.Players.Add(player);

            // Add bots
            var botNames = new[] { "Bot-Rex", "Bot-Ace", "Bot-Nova", "Bot-Zara", "Bot-Jinx" };
            for (int i = 0; i < req.BotCount && room.Players.Count < room.MaxPlayers; i++)
            {
                room.Players.Add(new Player { Name = botNames[i % botNames.Length], IsBot = true });
            }

            _rooms[room.Id] = room;
            return (room, player);
        }

        public (GameRoom? room, Player? player, string? error) JoinRoom(string roomId, JoinRoomRequest req)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return (null, null, "Room not found");
            if (room.State != GameState.Waiting)
                return (null, null, "Game already started");
            if (room.Players.Count >= room.MaxPlayers)
                return (null, null, "Room is full");
            if (room.HasPassword && room.Password != req.Password)
                return (null, null, "Wrong password");

            var player = new Player { Name = req.PlayerName };
            room.Players.Add(player);
            return (room, player, null);
        }

        public (bool success, string? error) StartGame(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room))
                return (false, "Room not found");
            if (room.Players.Count < room.MinPlayers)
                return (false, "Not enough players");
            if (room.State != GameState.Waiting && room.State != GameState.Finished)
                return (false, "Game already started");

            if (room.State == GameState.Finished)
            {
                room.RoundNumber++;
                foreach (var p in room.Players) p.HasCalledUno = false;
            }

            room.State = GameState.Playing;
            room.StartedAt = DateTime.UtcNow;
            room.DrawPile = DeckBuilder.BuildDeck(room.DeckType);
            room.DiscardPile = new List<Card>();
            room.Direction = GameDirection.Clockwise;
            room.CurrentPlayerIndex = 0;

            // Deal 7 cards each
            foreach (var p in room.Players)
            {
                p.Hand = new List<Card>();
                for (int i = 0; i < 7; i++)
                    p.Hand.Add(DeckBuilder.DrawCard(room.DrawPile, room.DiscardPile));
            }

            // Flip first card (must not be Wild Draw Four to start)
            Card firstCard;
            do { firstCard = DeckBuilder.DrawCard(room.DrawPile, room.DiscardPile); }
            while (firstCard.Type == CardType.WildDrawFour);
            room.DiscardPile.Add(firstCard);

            room.AddEvent($"Game started! Deck: {room.DeckType}. First card: {firstCard.DisplayName}");
            
            // Apply First Card Rules
            if (firstCard.Type == CardType.Skip) {
                AdvanceTurn(room);
            } else if (firstCard.Type == CardType.Reverse) {
                room.Direction = GameDirection.CounterClockwise;
                if (room.Players.Count == 2) AdvanceTurn(room);
            } else if (firstCard.Type == CardType.DrawTwo) {
                for (int i = 0; i < 2; i++) room.Players[room.CurrentPlayerIndex].Hand.Add(DeckBuilder.DrawCard(room.DrawPile, room.DiscardPile));
                AdvanceTurn(room);
            }

            return (true, null);
        }

        public (bool success, string? error) PlayCard(string roomId, string playerId, PlayCardRequest req)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return (false, "Room not found");
            if (room.State != GameState.Playing) return (false, "Game not in progress");

            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) return (false, "Player not found");
            if (room.CurrentPlayer?.Id != playerId) return (false, "Not your turn");

            var card = player.Hand.FirstOrDefault(c => c.Id == req.CardId);
            if (card == null) return (false, "Card not in hand");
            if (!CanPlay(card, room, player)) return (false, "Cannot play this card");

            player.Hand.Remove(card);
            
            bool isDark = room.IsDarkSide && room.DeckType == DeckType.FlipEdition;
            bool isWild = isDark ? card.IsDarkWild : card.IsWild;

            if (isWild && req.ChosenColor.HasValue)
                room.CurrentWildColor = req.ChosenColor;
            room.DiscardPile.Add(card);
            player.HasCalledUno = player.Hand.Count == 1;

            room.AddEvent($"{player.Name} played {card.DisplayName}");

            if (player.Hand.Count == 0)
            {
                room.State = GameState.Finished;
                room.WinnerId = player.Id;
                CalculateScores(room, player);
                room.AddEvent($"🎉 {player.Name} wins Round {room.RoundNumber}!");
                return (true, null);
            }

            ApplyCardEffect(room, card, player);
            return (true, null);
        }

        public (bool success, string? error) PassTurn(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return (false, "Room not found");
            if (room.CurrentPlayer?.Id != playerId) return (false, "Not your turn");
            var player = room.Players.First(p => p.Id == playerId);
            if (!player.HasDrawnThisTurn) return (false, "Must draw a card before passing");
            
            AdvanceTurn(room);
            room.AddEvent($"{player.Name} passed turn");
            return (true, null);
        }

        public (bool success, Card? card, string? error) DrawCard(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return (false, null, "Room not found");
            if (room.CurrentPlayer?.Id != playerId) return (false, null, "Not your turn");

            var player = room.Players.First(p => p.Id == playerId);
            if (player.HasDrawnThisTurn) return (false, null, "Already drew a card this turn");

            var card = DeckBuilder.DrawCard(room.DrawPile, room.DiscardPile);
            player.Hand.Add(card);
            player.HasDrawnThisTurn = true;
            room.AddEvent($"{player.Name} drew a card");

            return (true, card, null);
        }

        public (bool success, string? error) CallUno(string roomId, string playerId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return (false, "Room not found");
            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            if (player == null) return (false, "Not found");
            player.HasCalledUno = true;
            room.AddEvent($"{player.Name} called UNO!");
            return (true, null);
        }

        public (bool success, string? error) CatchUno(string roomId, string catcherId, string targetId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return (false, "Room not found");
            var target = room.Players.FirstOrDefault(p => p.Id == targetId);
            if (target == null || target.Hand.Count != 1 || target.HasCalledUno)
                return (false, "Cannot catch");
            // Penalize: draw 2
            for (int i = 0; i < 2; i++)
                target.Hand.Add(DeckBuilder.DrawCard(room.DrawPile, room.DiscardPile));
            room.AddEvent($"{target.Name} was caught not calling UNO! +2 cards");
            return (true, null);
        }

        public GameStateDto GetStateForPlayer(string roomId, string playerId)
        {
            var room = GetRoom(roomId);
            if (room == null) return new GameStateDto();

            var player = room.Players.FirstOrDefault(p => p.Id == playerId);
            var isNoPeek = room.DeckType == DeckType.NoPeekMode;

            return new GameStateDto
            {
                RoomId = room.Id,
                RoomName = room.Name,
                DeckType = room.DeckType,
                State = room.State,
                Direction = room.Direction,
                CurrentWildColor = room.CurrentWildColor,
                IsDarkSide = room.IsDarkSide,
                TopCard = room.TopCard,
                DrawPileCount = room.DrawPile.Count,
                CurrentPlayerId = room.CurrentPlayer?.Id,
                WinnerId = room.WinnerId,
                RoundNumber = room.RoundNumber,
                YourHand = player?.Hand ?? new(),
                EventLog = room.EventLog.TakeLast(20).ToList(),
                Players = room.Players.Select(p => new PlayerDto
                {
                    Id = p.Id,
                    Name = p.Name,
                    HandCount = p.Hand.Count,
                    IsBot = p.IsBot,
                    Score = p.Score,
                    HasDrawnThisTurn = p.HasDrawnThisTurn,
                    IsConnected = p.IsConnected,
                    IsCurrentPlayer = room.CurrentPlayer?.Id == p.Id,
                    VisibleHand = isNoPeek ? p.Hand : new()
                }).ToList()
            };
        }

        // Bot AI
        public void ProcessBotTurns(string roomId)
        {
            if (!_rooms.TryGetValue(roomId, out var room)) return;
            if (room.State != GameState.Playing) return;

            var current = room.CurrentPlayer;
            if (current == null || !current.IsBot) return;

            System.Threading.Thread.Sleep(1500); // Bot "thinking"

            var playable = current.Hand.Where(c => CanPlay(c, room, current)).ToList();
            if (playable.Any())
            {
                var card = ChooseBotCard(playable);
                CardColor? color = null;
                if (card.IsWild) color = ChooseBotColor(current.Hand);

                PlayCard(roomId, current.Id, new PlayCardRequest { CardId = card.Id, ChosenColor = color });
                if (current.Hand.Count == 1) CallUno(roomId, current.Id);
            }
            else if (!current.HasDrawnThisTurn)
            {
                var (_, drawnCard, _) = DrawCard(roomId, current.Id);
                if (drawnCard != null && CanPlay(drawnCard, room, current))
                {
                    CardColor? color = null;
                    if (drawnCard.IsWild) color = ChooseBotColor(current.Hand);
                    PlayCard(roomId, current.Id, new PlayCardRequest { CardId = drawnCard.Id, ChosenColor = color });
                }
                else
                {
                    PassTurn(roomId, current.Id);
                }
            }
            else
            {
                PassTurn(roomId, current.Id);
            }
        }

        private Card ChooseBotCard(List<Card> playable)
        {
            // Prefer action cards, then highest number
            return playable.OrderByDescending(c => c.Points).First();
        }

        private CardColor ChooseBotColor(List<Card> hand)
        {
            var colorCounts = hand.Where(c => !c.IsWild)
                .GroupBy(c => c.Color)
                .OrderByDescending(g => g.Count())
                .FirstOrDefault();
            return colorCounts?.Key ?? CardColor.Red;
        }

        private bool CanPlay(Card card, GameRoom room, Player player)
        {
            var top = room.TopCard;
            if (top == null) return true;

            bool isDark = room.IsDarkSide && room.DeckType == DeckType.FlipEdition;
            var effectiveColor = room.CurrentWildColor ?? (isDark ? top.DarkColor ?? top.Color : top.Color);
            var effectiveType = isDark ? top.DarkType ?? top.Type : top.Type;
            var effectiveNumber = isDark ? top.DarkNumber ?? top.Number : top.Number;

            var cardColor = isDark ? card.DarkColor ?? card.Color : card.Color;
            var cardType = isDark ? card.DarkType ?? card.Type : card.Type;
            var cardNumber = isDark ? card.DarkNumber ?? card.Number : card.Number;
            var cardIsWild = isDark ? card.IsDarkWild : card.IsWild;

            // Wild Draw Four specific rule: Can only play if no other card matches the current effective color
            if ((!isDark && card.Type == CardType.WildDrawFour) || (isDark && cardType == CardType.WildDrawColor))
            {
                if (player.Hand.Any(c => c.Id != card.Id && (isDark ? (c.DarkColor ?? c.Color) : c.Color) == effectiveColor))
                {
                    return false;
                }
            }

            if (cardIsWild) return true;

            if (cardColor == effectiveColor) return true;
            if (cardType == effectiveType && !cardIsWild) return true;
            if (cardNumber != null && effectiveNumber != null && cardNumber == effectiveNumber) return true;
            return false;
        }

        private void ApplyCardEffect(GameRoom room, Card card, Player? player)
        {
            bool isDark = room.IsDarkSide && room.DeckType == DeckType.FlipEdition;
            var cardType = isDark ? card.DarkType ?? card.Type : card.Type;

            // Reset wild color if non-wild played
            if (!(isDark ? card.IsDarkWild : card.IsWild)) room.CurrentWildColor = null;

            switch (cardType)
            {
                case CardType.DarkSkip:
                case CardType.Skip:
                    AdvanceTurn(room); AdvanceTurn(room); break;
                case CardType.DarkReverse:
                case CardType.Reverse:
                    room.Direction = room.Direction == GameDirection.Clockwise
                        ? GameDirection.CounterClockwise : GameDirection.Clockwise;
                    if (room.Players.Count == 2) AdvanceTurn(room); // 2-player: act as skip
                    AdvanceTurn(room); break;
                case CardType.DrawTwo:
                    AdvanceTurn(room);
                    var next2 = room.CurrentPlayer;
                    if (next2 != null)
                        for (int i = 0; i < 2; i++) next2.Hand.Add(DeckBuilder.DrawCard(room.DrawPile, room.DiscardPile));
                    AdvanceTurn(room); break;
                case CardType.WildDrawColor:
                case CardType.WildDrawFour:
                    AdvanceTurn(room);
                    var next4 = room.CurrentPlayer;
                    if (next4 != null)
                        for (int i = 0; i < 4; i++) next4.Hand.Add(DeckBuilder.DrawCard(room.DrawPile, room.DiscardPile));
                    AdvanceTurn(room); break;
                case CardType.DrawFiveDark:
                case CardType.DrawFive:
                    AdvanceTurn(room);
                    var next5 = room.CurrentPlayer;
                    if (next5 != null)
                        for (int i = 0; i < 5; i++) next5.Hand.Add(DeckBuilder.DrawCard(room.DrawPile, room.DiscardPile));
                    AdvanceTurn(room); break;
                case CardType.SwapHands:
                    // Swap with next player
                    AdvanceTurn(room);
                    if (player != null && room.CurrentPlayer != null)
                    {
                        var tmp = player.Hand;
                        player.Hand = room.CurrentPlayer.Hand;
                        room.CurrentPlayer.Hand = tmp;
                    }
                    AdvanceTurn(room); break;
                case CardType.DiscardAll:
                    if (player != null)
                    {
                        var matchingColorCards = player.Hand.Where(c => c.Color == card.Color).ToList();
                        room.DiscardPile.AddRange(matchingColorCards);
                        player.Hand.RemoveAll(c => c.Color == card.Color);
                        room.AddEvent($"{player.Name} discarded {matchingColorCards.Count} {card.Color} cards");
                    }
                    AdvanceTurn(room); break;
                case CardType.SkipEveryone:
                    // Skip everyone - play again
                    break; // Don't advance
                case CardType.WildShuffleHands:
                    var allCards = room.Players.SelectMany(p => p.Hand).ToList();
                    allCards = DeckBuilder.Shuffle(allCards);
                    int idx = 0;
                    foreach (var p in room.Players)
                    {
                        int count = p.Hand.Count;
                        p.Hand = allCards.Skip(idx).Take(count).ToList();
                        idx += count;
                    }
                    AdvanceTurn(room); break;
                case CardType.DarkFlip:
                case CardType.Flip:
                    room.IsDarkSide = !room.IsDarkSide;
                    room.CurrentWildColor = null;
                    AdvanceTurn(room); break;
                case CardType.FlashWild:
                    // Play again
                    break;
                default:
                    AdvanceTurn(room); break;
            }
        }

        private void AdvanceTurn(GameRoom room)
        {
            if (room.CurrentPlayer != null)
                room.CurrentPlayer.HasDrawnThisTurn = false;

            int count = room.Players.Count;
            if (room.Direction == GameDirection.Clockwise)
                room.CurrentPlayerIndex = (room.CurrentPlayerIndex + 1) % count;
            else
                room.CurrentPlayerIndex = (room.CurrentPlayerIndex - 1 + count) % count;
        }

        private void CalculateScores(GameRoom room, Player winner)
        {
            int points = room.Players.Where(p => p.Id != winner.Id)
                .SelectMany(p => p.Hand).Sum(c => c.Points);
            winner.Score += points;
        }
    }
}
