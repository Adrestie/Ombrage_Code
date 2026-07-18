using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.ProjectBoard
{
    public class ProjectBoardCardPopup : EditorWindow
    {
        private Card _card;
        private BoardData _boardData;
        private System.Action _onSave;
        private Vector2 _scrollPosition;

        // Cached card list for reference dropdown
        private List<Card> _allCards;
        private string[] _cardLabels;
        private string[] _cardIds;

        // New reference temp fields
        private int _newRefTypeIndex;
        private int _newRefCardIndex;

        public static void Show(Card card, BoardData boardData, System.Action onSave)
        {
            var popup = CreateInstance<ProjectBoardCardPopup>();
            popup._card = card;
            popup._boardData = boardData;
            popup._onSave = onSave;
            popup.titleContent = new GUIContent($"Edit Card: {card.Title}");
            popup.RefreshCardList();
            popup.ShowUtility();
            popup.minSize = new Vector2(420, 450);
        }

        private void RefreshCardList()
        {
            _allCards = ProjectBoardStorage.GetAllCards(_boardData)
                .Where(c => c.Id != _card.Id)
                .ToList();

            var labels = new List<string>();
            var ids = new List<string>();

            foreach (var c in _allCards)
            {
                string colName = GetColumnName(c);
                labels.Add($"[{colName}] {c.Title} (#{c.Id})");
                ids.Add(c.Id);
            }

            _cardLabels = labels.ToArray();
            _cardIds = ids.ToArray();
        }

        private string GetColumnName(Card card)
        {
            foreach (var col in _boardData.Columns)
            {
                if (col.Cards.Contains(card))
                    return col.Name;
            }
            return "?";
        }

        private void OnGUI()
        {
            if (_card == null)
            {
                Close();
                return;
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);

            EditorGUILayout.LabelField("Card Details", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Title
            EditorGUILayout.LabelField("Title");
            _card.Title = EditorGUILayout.TextField(_card.Title);

            EditorGUILayout.Space(4);

            // Description
            EditorGUILayout.LabelField("Description");
            _card.Description = EditorGUILayout.TextArea(_card.Description, GUILayout.Height(80));

            EditorGUILayout.Space(4);

            // Priority
            _card.Priority = EditorGUILayout.Popup("Priority", _card.Priority, ProjectBoardStorage.PriorityLabels);

            // Priority color preview
            var priorityRect = GUILayoutUtility.GetRect(0, 8);
            EditorGUI.DrawRect(new Rect(priorityRect.x, priorityRect.y, priorityRect.width, 4),
                ProjectBoardStorage.PriorityColors[_card.Priority]);

            EditorGUILayout.Space(8);

            // Card ID
            EditorGUILayout.LabelField("ID", $"#{_card.Id}");

            EditorGUILayout.Space(8);

            // ── References ──
            EditorGUILayout.LabelField("References", EditorStyles.boldLabel);

            if (_card.References == null)
                _card.References = new List<CardReference>();

            int removeIdx = -1;
            for (int i = 0; i < _card.References.Count; i++)
            {
                var reference = _card.References[i];
                var targetCard = ProjectBoardStorage.FindCard(_boardData, reference.TargetCardId);
                string targetLabel = targetCard != null
                    ? $"{targetCard.Title} (#{targetCard.Id})"
                    : $"<missing> (#{reference.TargetCardId})";

                EditorGUILayout.BeginHorizontal();

                // Reference type badge
                var badgeColor = GetRefTypeColor(reference.Type);
                var oldBg = GUI.backgroundColor;
                GUI.backgroundColor = badgeColor;
                GUILayout.Label(reference.Type, EditorStyles.helpBox, GUILayout.Width(100));
                GUI.backgroundColor = oldBg;

                GUILayout.Label(targetLabel, GUILayout.ExpandWidth(true));

                if (GUILayout.Button("X", GUILayout.Width(22)))
                {
                    removeIdx = i;
                }

                EditorGUILayout.EndHorizontal();
            }

            if (removeIdx >= 0)
            {
                _card.References.RemoveAt(removeIdx);
            }

            EditorGUILayout.Space(4);

            // Add new reference
            EditorGUILayout.LabelField("Add Reference", EditorStyles.miniLabel);

            if (_cardLabels.Length > 0)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Type", GUILayout.Width(40));
                _newRefTypeIndex = EditorGUILayout.Popup(_newRefTypeIndex, ProjectBoardStorage.ReferenceTypes, GUILayout.Width(100));

                string selectedRefType = ProjectBoardStorage.ReferenceTypes[_newRefTypeIndex];
                bool validType = selectedRefType != "None";

                if (validType)
                {
                    _newRefCardIndex = EditorGUILayout.Popup(_newRefCardIndex, _cardLabels);
                }
                EditorGUILayout.EndHorizontal();

                if (validType)
                {
                    if (GUILayout.Button("+ Add Reference", GUILayout.Height(24)))
                    {
                        if (_newRefCardIndex >= 0 && _newRefCardIndex < _cardIds.Length)
                        {
                            string targetId = _cardIds[_newRefCardIndex];

                            bool duplicate = _card.References.Any(r =>
                                r.TargetCardId == targetId && r.Type == selectedRefType);

                            if (!duplicate)
                            {
                                _card.References.Add(new CardReference
                                {
                                    Type = selectedRefType,
                                    TargetCardId = targetId
                                });
                            }
                        }
                    }
                }
            }
            else
            {
                EditorGUILayout.HelpBox("No other cards available to reference.", MessageType.Info);
            }

            EditorGUILayout.Space(12);

            // Buttons
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Save & Close", GUILayout.Width(120), GUILayout.Height(28)))
            {
                _onSave?.Invoke();
                Close();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.EndScrollView();
        }

        private static Color GetRefTypeColor(string type)
        {
            switch (type)
            {
                case "Blocked By": return new Color(0.9f, 0.3f, 0.3f);
                case "Relative To": return new Color(0.4f, 0.6f, 0.9f);
                case "Before Than": return new Color(0.9f, 0.7f, 0.2f);
                case "After Than": return new Color(0.5f, 0.8f, 0.4f);
                default: return Color.gray;
            }
        }
    }
}
