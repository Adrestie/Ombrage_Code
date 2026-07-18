using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Ombrage.Tools.ProjectBoard
{
    [Serializable]
    public class CardReference
    {
        public string Type;
        public string TargetCardId;
    }

    [Serializable]
    public class Card
    {
        public string Id;
        public string Title;
        public string Description;
        public int Priority; // 0=VeryLow, 1=Low, 2=Medium, 3=High, 4=Blocker
        public List<CardReference> References = new List<CardReference>();

        public Card()
        {
            Id = Guid.NewGuid().ToString("N").Substring(0, 8);
            Title = "New Card";
            Description = "";
            Priority = 2;
            References = new List<CardReference>();
        }
    }

    [Serializable]
    public class Column
    {
        public string Name;
        public Color HeaderColor;
        public List<Card> Cards = new List<Card>();
    }

    [Serializable]
    public class BoardData
    {
        public List<Column> Columns = new List<Column>();
    }

    public static class ProjectBoardStorage
    {
        private const string EditorPrefsKey = "ProjectBoard_Data";

        public static readonly string[] PriorityLabels = { "Very Low", "Low", "Medium", "High", "Blocker" };
        public static readonly Color[] PriorityColors =
        {
            new Color(0.5f, 0.5f, 0.5f),
            new Color(0.3f, 0.6f, 0.9f),
            new Color(1f, 0.75f, 0.2f),
            new Color(0.9f, 0.4f, 0.2f),
            new Color(0.9f, 0.1f, 0.1f)
        };

        public static readonly string[] ReferenceTypes = { "None", "Blocked By", "Relative To", "Before Than", "After Than" };

        public static BoardData Load()
        {
            string json = UnityEditor.EditorPrefs.GetString(EditorPrefsKey, "");
            if (string.IsNullOrEmpty(json))
            {
                return CreateDefault();
            }

            try
            {
                return JsonUtility.FromJson<BoardData>(json);
            }
            catch
            {
                return CreateDefault();
            }
        }

        public static void Save(BoardData data)
        {
            string json = JsonUtility.ToJson(data, false);
            UnityEditor.EditorPrefs.SetString(EditorPrefsKey, json);
        }

        public static Card FindCard(BoardData data, string cardId)
        {
            if (string.IsNullOrEmpty(cardId)) return null;
            foreach (var column in data.Columns)
            {
                foreach (var card in column.Cards)
                {
                    if (card.Id == cardId) return card;
                }
            }
            return null;
        }

        public static List<Card> GetAllCards(BoardData data)
        {
            return data.Columns.SelectMany(c => c.Cards).ToList();
        }

        private static BoardData CreateDefault()
        {
            var board = new BoardData();
            board.Columns.Add(new Column { Name = "To Do", HeaderColor = new Color(0.3f, 0.5f, 0.8f) });
            board.Columns.Add(new Column { Name = "In Progress", HeaderColor = new Color(0.9f, 0.65f, 0.2f) });
            board.Columns.Add(new Column { Name = "Done", HeaderColor = new Color(0.3f, 0.75f, 0.4f) });
            board.Columns.Add(new Column { Name = "Bug", HeaderColor = new Color(0.85f, 0.25f, 0.25f) });
            board.Columns.Add(new Column { Name = "Blocked", HeaderColor = new Color(0.6f, 0.6f, 0.6f) });
            return board;
        }
    }
}
