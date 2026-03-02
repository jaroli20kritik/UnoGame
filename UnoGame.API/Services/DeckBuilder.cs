using System;
using System.Collections.Generic;
using UnoGame.API.Models;

namespace UnoGame.API.Services
{
    public static class DeckBuilder
    {
        private static readonly CardColor[] StandardColors = { CardColor.Red, CardColor.Green, CardColor.Blue, CardColor.Yellow };

        public static List<Card> BuildDeck(DeckType deckType)
        {
            return deckType switch
            {
                DeckType.Classic => BuildClassicDeck(),
                DeckType.ActionPacked => BuildActionPackedDeck(),
                DeckType.FlipEdition => BuildFlipEditionDeck(),
                DeckType.NoPeekMode => BuildNoPeekDeck(),
                _ => BuildClassicDeck()
            };
        }

        private static List<Card> BuildClassicDeck()
        {
            var cards = new List<Card>();
            foreach (var color in StandardColors)
            {
                // One 0
                cards.Add(new Card { Color = color, Type = CardType.Number, Number = 0 });
                // Two of each 1-9
                for (int n = 1; n <= 9; n++)
                    for (int i = 0; i < 2; i++)
                        cards.Add(new Card { Color = color, Type = CardType.Number, Number = n });
                // Action cards x2
                for (int i = 0; i < 2; i++)
                {
                    cards.Add(new Card { Color = color, Type = CardType.Skip });
                    cards.Add(new Card { Color = color, Type = CardType.Reverse });
                    cards.Add(new Card { Color = color, Type = CardType.DrawTwo });
                }
            }
            // Wild x4, Wild Draw Four x4
            for (int i = 0; i < 4; i++)
            {
                cards.Add(new Card { Color = CardColor.Wild, Type = CardType.Wild });
                cards.Add(new Card { Color = CardColor.Wild, Type = CardType.WildDrawFour });
            }
            return Shuffle(cards);
        }

        private static List<Card> BuildActionPackedDeck()
        {
            var cards = BuildClassicDeck();
            // Add extra action cards
            foreach (var color in StandardColors)
            {
                cards.Add(new Card { Color = color, Type = CardType.DrawTwo });
                cards.Add(new Card { Color = color, Type = CardType.Skip });
                cards.Add(new Card { Color = color, Type = CardType.SwapHands });
                cards.Add(new Card { Color = color, Type = CardType.DiscardAll });
            }
            for (int i = 0; i < 2; i++)
            {
                cards.Add(new Card { Color = CardColor.Wild, Type = CardType.WildShuffleHands });
                cards.Add(new Card { Color = CardColor.Wild, Type = CardType.DrawFive });
            }
            return Shuffle(cards);
        }

        private static List<Card> BuildFlipEditionDeck()
        {
            var cards = new List<Card>();
            
            var lightSides = new List<(CardColor color, CardType type, int? number)>();
            foreach (var color in StandardColors)
            {
                lightSides.Add((color, CardType.Number, 0));
                for(int i = 1; i <= 9; i++) {
                    lightSides.Add((color, CardType.Number, i));
                    lightSides.Add((color, CardType.Number, i));
                }
                for(int i = 0; i < 2; i++) {
                    lightSides.Add((color, CardType.DrawTwo, null));
                    lightSides.Add((color, CardType.Reverse, null));
                    lightSides.Add((color, CardType.Skip, null));
                    lightSides.Add((color, CardType.Flip, null));
                }
            }
            for(int i = 0; i < 4; i++) {
                lightSides.Add((CardColor.Wild, CardType.Wild, null));
                lightSides.Add((CardColor.Wild, CardType.WildDrawFour, null));
            }

            var darkSides = new List<(CardColor color, CardType type, int? number)>();
            var darkColors = new[] { CardColor.Orange, CardColor.Pink, CardColor.Teal, CardColor.Purple };
            foreach (var color in darkColors)
            {
                darkSides.Add((color, CardType.DarkNumber, 0));
                for(int i = 1; i <= 9; i++) {
                    darkSides.Add((color, CardType.DarkNumber, i));
                    darkSides.Add((color, CardType.DarkNumber, i));
                }
                for(int i = 0; i < 2; i++) {
                    darkSides.Add((color, CardType.DrawFiveDark, null));
                    darkSides.Add((color, CardType.DarkReverse, null));
                    darkSides.Add((color, CardType.SkipEveryone, null));
                    darkSides.Add((color, CardType.DarkFlip, null));
                }
            }
            for(int i = 0; i < 4; i++) {
                darkSides.Add((CardColor.Black, CardType.DarkWild, null));
                darkSides.Add((CardColor.Black, CardType.WildDrawColor, null));
            }

            // Shuffle dark sides before mapping them to the backs
            var rng = new Random();
            for (int i = darkSides.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (darkSides[i], darkSides[j]) = (darkSides[j], darkSides[i]);
            }

            for(int i=0; i<lightSides.Count; i++) {
                cards.Add(new Card {
                    Color = lightSides[i].color,
                    Type = lightSides[i].type,
                    Number = lightSides[i].number,
                    DarkColor = darkSides[i].color,
                    DarkType = darkSides[i].type,
                    DarkNumber = darkSides[i].number
                });
            }

            return Shuffle(cards);
        }

        private static List<Card> BuildNoPeekDeck()
        {
            // Smaller deck, faster gameplay
            var cards = new List<Card>();
            foreach (var color in StandardColors)
            {
                for (int n = 0; n <= 9; n++)
                    cards.Add(new Card { Color = color, Type = CardType.Number, Number = n });
                cards.Add(new Card { Color = color, Type = CardType.Skip });
                cards.Add(new Card { Color = color, Type = CardType.Reverse });
                cards.Add(new Card { Color = color, Type = CardType.DrawTwo });
            }
            for (int i = 0; i < 3; i++)
            {
                cards.Add(new Card { Color = CardColor.Wild, Type = CardType.Wild });
                cards.Add(new Card { Color = CardColor.Wild, Type = CardType.WildDrawFour });
            }
            return Shuffle(cards);
        }

        public static List<Card> Shuffle(List<Card> cards)
        {
            var rng = new Random();
            var list = new List<Card>(cards);
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j = rng.Next(i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        public static Card DrawCard(List<Card> drawPile, List<Card> discardPile)
        {
            if (drawPile.Count == 0)
            {
                if (discardPile.Count <= 1) return new Card { Color = CardColor.Wild, Type = CardType.Wild };
                var top = discardPile[^1];
                var reshuffled = discardPile.GetRange(0, discardPile.Count - 1);
                // Reset wild colors
                reshuffled.ForEach(c => { if (c.IsWild) c.Number = null; });
                var shuffled = Shuffle(reshuffled);
                drawPile.AddRange(shuffled);
                discardPile.Clear();
                discardPile.Add(top);
            }
            var card = drawPile[^1];
            drawPile.RemoveAt(drawPile.Count - 1);
            return card;
        }
    }
}
