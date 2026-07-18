using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.ProjectBoard
{
    public class ProjectBoardWindow : EditorWindow
    {
        private BoardData _boardData;
        private Vector2 _scrollPosition;

        // Drag state
        private Card _draggedCard;
        private int _dragSourceColumnIndex;
        private int _dragSourceCardIndex;
        private bool _isDragging;
        private Vector2 _dragOffset;
        private Rect _dragCardRect;

        // Column resize
        private float _columnWidth = 260f;
        private const float CardHeight = 100f;
        private const float HeaderHeight = 36f;
        private const float CardPadding = 6f;
        private const float ColumnPadding = 8f;

        // Styles
        private GUIStyle _cardStyle;
        private GUIStyle _cardTitleStyle;
        private GUIStyle _cardDescStyle;
        private GUIStyle _priorityBadgeStyle;
        private GUIStyle _columnHeaderStyle;
        private GUIStyle _refStyle;
        private GUIStyle _addButtonStyle;
        private bool _stylesInitialized;

        // Textures
        private Texture2D _cardBgTex;
        private Texture2D _cardHoverTex;
        private Texture2D _dropZoneTex;

        [MenuItem("Window/Ombrage Tools/Management/Project Board %&b")]
        public static void ShowWindow()
        {
            var window = GetWindow<ProjectBoardWindow>("Project Board");
            window.minSize = new Vector2(800, 400);
        }

        private void OnEnable()
        {
            _boardData = ProjectBoardStorage.Load();
            CreateTextures();
        }

        private void OnDisable()
        {
            if (_boardData != null)
                ProjectBoardStorage.Save(_boardData);
            DestroyTextures();
        }

        private void CreateTextures()
        {
            _cardBgTex = MakeTex(1, 1, new Color(0.22f, 0.22f, 0.22f));
            _cardHoverTex = MakeTex(1, 1, new Color(0.28f, 0.28f, 0.28f));
            _dropZoneTex = MakeTex(1, 1, new Color(0.3f, 0.5f, 0.8f, 0.25f));
        }

        private void DestroyTextures()
        {
            if (_cardBgTex != null) DestroyImmediate(_cardBgTex);
            if (_cardHoverTex != null) DestroyImmediate(_cardHoverTex);
            if (_dropZoneTex != null) DestroyImmediate(_dropZoneTex);
        }

        private void InitStyles()
        {
            if (_stylesInitialized) return;

            _cardStyle = new GUIStyle(GUI.skin.box)
            {
                padding = new RectOffset(8, 8, 6, 6),
                margin = new RectOffset(4, 4, 2, 2),
                normal = { background = _cardBgTex }
            };

            _cardTitleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                wordWrap = true,
                normal = { textColor = Color.white }
            };

            _cardDescStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                wordWrap = true,
                normal = { textColor = new Color(0.7f, 0.7f, 0.7f) }
            };

            _priorityBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                padding = new RectOffset(4, 4, 1, 1),
                normal = { textColor = Color.white }
            };

            _columnHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 13,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(10, 10, 0, 0),
                normal = { textColor = Color.white }
            };

            _refStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                fontSize = 9,
                normal = { textColor = new Color(0.55f, 0.75f, 1f) }
            };

            _addButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize = 18,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            _stylesInitialized = true;
        }

        private void OnGUI()
        {
            InitStyles();

            if (_boardData == null)
                _boardData = ProjectBoardStorage.Load();

            DrawToolbar();

            _scrollPosition = GUILayout.BeginScrollView(_scrollPosition, false, false);
            EditorGUILayout.BeginHorizontal();

            for (int colIdx = 0; colIdx < _boardData.Columns.Count; colIdx++)
            {
                DrawColumn(colIdx);
            }

            EditorGUILayout.EndHorizontal();
            GUILayout.EndScrollView();

            HandleDragVisualization();
            HandleDragDrop();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            if (GUILayout.Button("Add Column", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                _boardData.Columns.Add(new Column
                {
                    Name = "New Column",
                    HeaderColor = new Color(0.5f, 0.5f, 0.5f)
                });
                Save();
            }

            GUILayout.FlexibleSpace();

            GUILayout.Label($"Cards: {ProjectBoardStorage.GetAllCards(_boardData).Count}", EditorStyles.toolbarButton);

            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(50)))
            {
                Save();
            }

            if (GUILayout.Button("Reset Board", EditorStyles.toolbarButton, GUILayout.Width(80)))
            {
                if (EditorUtility.DisplayDialog("Reset Board",
                    "Delete ALL columns and cards? This cannot be undone.", "Reset", "Cancel"))
                {
                    UnityEditor.EditorPrefs.DeleteKey("ProjectBoard_Data");
                    _boardData = ProjectBoardStorage.Load();
                    Repaint();
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawColumn(int colIdx)
        {
            var column = _boardData.Columns[colIdx];

            EditorGUILayout.BeginVertical(GUILayout.Width(_columnWidth));

            // Header background
            var headerRect = GUILayoutUtility.GetRect(_columnWidth, HeaderHeight);
            EditorGUI.DrawRect(headerRect, column.HeaderColor);

            // Column name
            var nameRect = new Rect(headerRect.x + 8, headerRect.y, headerRect.width - 70, headerRect.height);
            GUI.Label(nameRect, $"{column.Name} ({column.Cards.Count})", _columnHeaderStyle);

            // Header buttons
            float btnX = headerRect.xMax - 58;
            float btnY = headerRect.y + 8;

            // Color picker button
            var colorBtnRect = new Rect(btnX, btnY, 20, 20);
            var oldColor = GUI.backgroundColor;
            GUI.backgroundColor = column.HeaderColor;
            if (GUI.Button(colorBtnRect, ""))
            {
                int capturedIdx = colIdx;
                ColorPickerPopup.Show(column.HeaderColor, color =>
                {
                    if (_boardData != null && capturedIdx < _boardData.Columns.Count)
                    {
                        _boardData.Columns[capturedIdx].HeaderColor = color;
                        Save();
                        Repaint();
                    }
                });
            }
            GUI.backgroundColor = oldColor;

            // Delete column button
            var delColRect = new Rect(btnX + 24, btnY, 20, 20);
            if (GUI.Button(delColRect, "X"))
            {
                if (EditorUtility.DisplayDialog("Delete Column",
                    $"Delete column '{column.Name}' and all its cards?", "Delete", "Cancel"))
                {
                    _boardData.Columns.RemoveAt(colIdx);
                    Save();
                    GUIUtility.ExitGUI();
                    return;
                }
            }

            // Rename on double click
            if (Event.current.type == EventType.MouseDown && Event.current.clickCount == 2 && nameRect.Contains(Event.current.mousePosition))
            {
                RenameColumnPopup.Show(column.Name, newName =>
                {
                    column.Name = newName;
                    Save();
                    Repaint();
                });
                Event.current.Use();
            }

            // Cards area
            var cardsAreaRect = GUILayoutUtility.GetRect(_columnWidth, Mathf.Max(200, column.Cards.Count * (CardHeight + CardPadding) + 50));
            var columnBgColor = new Color(0.18f, 0.18f, 0.18f);
            EditorGUI.DrawRect(cardsAreaRect, columnBgColor);

            // Draw drop zone when dragging
            if (_isDragging && cardsAreaRect.Contains(Event.current.mousePosition))
            {
                EditorGUI.DrawRect(cardsAreaRect, new Color(0.3f, 0.5f, 0.8f, 0.1f));
            }

            float cardY = cardsAreaRect.y + CardPadding;
            for (int cardIdx = 0; cardIdx < column.Cards.Count; cardIdx++)
            {
                var card = column.Cards[cardIdx];
                var cardRect = new Rect(cardsAreaRect.x + CardPadding, cardY, _columnWidth - CardPadding * 2, CardHeight);

                // Skip rendering the dragged card in its original position
                if (_isDragging && _draggedCard == card)
                {
                    cardY += CardHeight + CardPadding;
                    continue;
                }

                DrawCard(card, cardRect, colIdx, cardIdx);
                cardY += CardHeight + CardPadding;
            }

            // Add card button
            var addBtnRect = new Rect(cardsAreaRect.x + CardPadding, cardY + 2, _columnWidth - CardPadding * 2, 28);
            if (GUI.Button(addBtnRect, "+ Add Card"))
            {
                var newCard = new Card();
                column.Cards.Add(newCard);
                Save();
            }

            EditorGUILayout.EndVertical();

            GUILayout.Space(ColumnPadding);
        }

        private void DrawCard(Card card, Rect rect, int colIdx, int cardIdx)
        {
            var ev = Event.current;
            bool isHover = rect.Contains(ev.mousePosition);

            // Card background
            EditorGUI.DrawRect(rect, isHover ? new Color(0.28f, 0.28f, 0.28f) : new Color(0.22f, 0.22f, 0.22f));

            // Left priority bar
            var priorityBarRect = new Rect(rect.x, rect.y, 4, rect.height);
            EditorGUI.DrawRect(priorityBarRect, ProjectBoardStorage.PriorityColors[card.Priority]);

            // Priority badge
            var badgeRect = new Rect(rect.x + 10, rect.y + 4, 60, 14);
            EditorGUI.DrawRect(badgeRect, ProjectBoardStorage.PriorityColors[card.Priority] * 0.8f);
            GUI.Label(badgeRect, ProjectBoardStorage.PriorityLabels[card.Priority], _priorityBadgeStyle);

            // Delete button
            var deleteBtnRect = new Rect(rect.xMax - 20, rect.y + 4, 16, 16);
            if (isHover && GUI.Button(deleteBtnRect, "x", EditorStyles.miniButton))
            {
                if (EditorUtility.DisplayDialog("Delete Card", $"Delete '{card.Title}'?", "Delete", "Cancel"))
                {
                    _boardData.Columns[colIdx].Cards.RemoveAt(cardIdx);
                    // Clean up references pointing to this card
                    RemoveReferencesTo(card.Id);
                    Save();
                    GUIUtility.ExitGUI();
                    return;
                }
            }

            // Title
            var titleRect = new Rect(rect.x + 10, rect.y + 20, rect.width - 20, 20);
            GUI.Label(titleRect, card.Title, _cardTitleStyle);

            // Description (truncated)
            if (!string.IsNullOrEmpty(card.Description))
            {
                var descRect = new Rect(rect.x + 10, rect.y + 40, rect.width - 20, 30);
                string truncDesc = card.Description.Length > 80
                    ? card.Description.Substring(0, 80) + "..."
                    : card.Description;
                GUI.Label(descRect, truncDesc, _cardDescStyle);
            }

            // References count
            if (card.References != null && card.References.Count > 0)
            {
                var refRect = new Rect(rect.x + 10, rect.yMax - 18, rect.width - 20, 14);
                GUI.Label(refRect, $"Links: {card.References.Count}", _refStyle);
            }

            // Card ID
            var idRect = new Rect(rect.xMax - 55, rect.yMax - 18, 50, 14);
            GUI.Label(idRect, $"#{card.Id}", new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                normal = { textColor = new Color(0.4f, 0.4f, 0.4f) }
            });

            // Handle interactions
            if (ev.type == EventType.MouseDown && rect.Contains(ev.mousePosition))
            {
                if (ev.button == 0 && !deleteBtnRect.Contains(ev.mousePosition))
                {
                    // Start drag
                    _draggedCard = card;
                    _dragSourceColumnIndex = colIdx;
                    _dragSourceCardIndex = cardIdx;
                    _dragOffset = ev.mousePosition - rect.position;
                    _dragCardRect = rect;
                    _isDragging = true;
                    ev.Use();
                }
                else if (ev.button == 1)
                {
                    // Right-click context menu
                    ShowCardContextMenu(card, colIdx, cardIdx);
                    ev.Use();
                }
            }

            // Double-click to edit
            if (ev.type == EventType.MouseDown && ev.clickCount == 2 && rect.Contains(ev.mousePosition))
            {
                ProjectBoardCardPopup.Show(card, _boardData, () =>
                {
                    Save();
                    Repaint();
                });
                ev.Use();
            }
        }

        private void HandleDragVisualization()
        {
            if (!_isDragging || _draggedCard == null) return;

            var ev = Event.current;
            if (ev.type == EventType.Repaint)
            {
                var dragRect = new Rect(ev.mousePosition.x - _dragOffset.x, ev.mousePosition.y - _dragOffset.y,
                    _columnWidth - CardPadding * 2, CardHeight);

                // Semi-transparent card
                var oldColor = GUI.color;
                GUI.color = new Color(1, 1, 1, 0.7f);
                EditorGUI.DrawRect(dragRect, new Color(0.3f, 0.35f, 0.45f));

                var barRect = new Rect(dragRect.x, dragRect.y, 4, dragRect.height);
                EditorGUI.DrawRect(barRect, ProjectBoardStorage.PriorityColors[_draggedCard.Priority]);

                var titleRect = new Rect(dragRect.x + 10, dragRect.y + 10, dragRect.width - 20, 20);
                GUI.Label(titleRect, _draggedCard.Title, _cardTitleStyle);
                GUI.color = oldColor;
            }

            Repaint();
        }

        private void HandleDragDrop()
        {
            if (!_isDragging) return;

            var ev = Event.current;

            if (ev.type == EventType.MouseUp && ev.button == 0)
            {
                // Find target column
                int targetColIdx = FindColumnAtPosition(ev.mousePosition);

                if (targetColIdx >= 0 && _draggedCard != null)
                {
                    var sourceColumn = _boardData.Columns[_dragSourceColumnIndex];
                    var targetColumn = _boardData.Columns[targetColIdx];

                    if (sourceColumn != targetColumn || targetColIdx != _dragSourceColumnIndex)
                    {
                        sourceColumn.Cards.Remove(_draggedCard);

                        // Find insertion index based on Y position
                        int insertIdx = FindInsertIndex(targetColumn, ev.mousePosition.y);
                        targetColumn.Cards.Insert(insertIdx, _draggedCard);
                        Save();
                    }
                    else
                    {
                        // Reorder within same column
                        int insertIdx = FindInsertIndex(targetColumn, ev.mousePosition.y);
                        sourceColumn.Cards.Remove(_draggedCard);
                        if (insertIdx > sourceColumn.Cards.Count)
                            insertIdx = sourceColumn.Cards.Count;
                        sourceColumn.Cards.Insert(insertIdx, _draggedCard);
                        Save();
                    }
                }

                _isDragging = false;
                _draggedCard = null;
                ev.Use();
                Repaint();
            }
            else if (ev.type == EventType.MouseDrag)
            {
                ev.Use();
                Repaint();
            }
        }

        private int FindColumnAtPosition(Vector2 pos)
        {
            float x = -_scrollPosition.x;
            for (int i = 0; i < _boardData.Columns.Count; i++)
            {
                float colLeft = x;
                float colRight = x + _columnWidth + ColumnPadding;
                if (pos.x >= colLeft && pos.x <= colRight)
                    return i;
                x = colRight;
            }
            return -1;
        }

        private int FindInsertIndex(Column column, float mouseY)
        {
            // Approximate card positions (header + toolbar offset)
            float approxStartY = HeaderHeight + 40; // toolbar + header
            for (int i = 0; i < column.Cards.Count; i++)
            {
                float cardCenterY = approxStartY + i * (CardHeight + CardPadding) + CardHeight / 2;
                if (mouseY < cardCenterY)
                    return i;
            }
            return column.Cards.Count;
        }

        private void ShowCardContextMenu(Card card, int colIdx, int cardIdx)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Edit Card"), false, () =>
            {
                ProjectBoardCardPopup.Show(card, _boardData, () =>
                {
                    Save();
                    Repaint();
                });
            });

            menu.AddSeparator("");

            // Move to column options
            for (int i = 0; i < _boardData.Columns.Count; i++)
            {
                if (i == colIdx) continue;
                int targetIdx = i;
                menu.AddItem(new GUIContent($"Move to/{_boardData.Columns[i].Name}"), false, () =>
                {
                    _boardData.Columns[colIdx].Cards.Remove(card);
                    _boardData.Columns[targetIdx].Cards.Add(card);
                    Save();
                    Repaint();
                });
            }

            menu.AddSeparator("");

            if (card.References != null && card.References.Count > 0)
            {
                menu.AddItem(new GUIContent("Clear All References"), false, () =>
                {
                    card.References.Clear();
                    Save();
                    Repaint();
                });
            }

            menu.AddItem(new GUIContent("Delete"), false, () =>
            {
                if (EditorUtility.DisplayDialog("Delete Card", $"Delete '{card.Title}'?", "Delete", "Cancel"))
                {
                    _boardData.Columns[colIdx].Cards.RemoveAt(cardIdx);
                    RemoveReferencesTo(card.Id);
                    Save();
                    Repaint();
                }
            });

            menu.ShowAsContext();
        }

        private void RemoveReferencesTo(string cardId)
        {
            foreach (var col in _boardData.Columns)
            {
                foreach (var card in col.Cards)
                {
                    card.References.RemoveAll(r => r.TargetCardId == cardId);
                }
            }
        }

        private void Save()
        {
            ProjectBoardStorage.Save(_boardData);
        }

        private static Texture2D MakeTex(int width, int height, Color color)
        {
            var pixels = new Color[width * height];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = color;
            var tex = new Texture2D(width, height);
            tex.SetPixels(pixels);
            tex.Apply();
            return tex;
        }
    }

    // ─── Small utility popups ────────────────────────────────────

    public class RenameColumnPopup : EditorWindow
    {
        private string _newName;
        private System.Action<string> _onConfirm;

        public static void Show(string currentName, System.Action<string> onConfirm)
        {
            var popup = CreateInstance<RenameColumnPopup>();
            popup._newName = currentName;
            popup._onConfirm = onConfirm;
            popup.titleContent = new GUIContent("Rename Column");
            popup.ShowUtility();
            popup.minSize = new Vector2(250, 60);
            popup.maxSize = new Vector2(250, 60);
        }

        private void OnGUI()
        {
            _newName = EditorGUILayout.TextField("Name", _newName);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("OK"))
            {
                _onConfirm?.Invoke(_newName);
                Close();
            }
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }

    public class ColorPickerPopup : EditorWindow
    {
        private Color _color;
        private System.Action<Color> _onConfirm;

        public static void Show(Color current, System.Action<Color> onConfirm)
        {
            var popup = CreateInstance<ColorPickerPopup>();
            popup._color = current;
            popup._onConfirm = onConfirm;
            popup.titleContent = new GUIContent("Column Color");
            popup.ShowUtility();
            popup.minSize = new Vector2(250, 70);
            popup.maxSize = new Vector2(250, 70);
        }

        private void OnGUI()
        {
            _color = EditorGUILayout.ColorField("Color", _color);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Apply"))
            {
                _onConfirm?.Invoke(_color);
                Close();
            }
            if (GUILayout.Button("Cancel"))
            {
                Close();
            }
            EditorGUILayout.EndHorizontal();
        }
    }
}
