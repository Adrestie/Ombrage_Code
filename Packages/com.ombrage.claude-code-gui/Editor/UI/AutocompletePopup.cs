using System;
using System.Collections.Generic;
using UnityEngine.UIElements;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Popup d'autocomplétion flottant (positionné en absolu) réutilisé pour les slash commands
    /// et les références @fichier. Navigation clavier pilotée par la fenêtre hôte.
    /// </summary>
    public class AutocompletePopup : VisualElement
    {
        public readonly struct Item
        {
            public readonly string Label;
            public readonly string Detail;
            public readonly string InsertValue;
            public Item(string label, string detail, string insert)
            {
                Label = label; Detail = detail; InsertValue = insert;
            }
        }

        private readonly List<Item> _items = new();
        private readonly ScrollView _list;
        private int _selected;
        private Action<Item> _onAccept;

        public bool IsOpen => style.display == DisplayStyle.Flex;

        public AutocompletePopup()
        {
            AddToClassList("cc-ac");
            style.position = Position.Absolute;
            style.display = DisplayStyle.None;
            focusable = false;
            pickingMode = PickingMode.Position;

            _list = new ScrollView(ScrollViewMode.Vertical);
            _list.style.maxHeight = 196;
            Add(_list);
        }

        public void Open(List<Item> items, Action<Item> onAccept, float left, float top, float width)
        {
            _items.Clear();
            _items.AddRange(items);
            _onAccept = onAccept;
            _selected = 0;
            style.left = left;
            style.top = top;
            style.width = width;
            style.display = DisplayStyle.Flex;
            Rebuild();
        }

        public void Close()
        {
            style.display = DisplayStyle.None;
            _items.Clear();
        }

        public void Move(int delta)
        {
            if (_items.Count == 0) return;
            _selected = (_selected + delta + _items.Count) % _items.Count;
            Rebuild();
        }

        public void AcceptSelected()
        {
            if (_selected < 0 || _selected >= _items.Count) { Close(); return; }
            var item = _items[_selected];
            Close();
            _onAccept?.Invoke(item);
        }

        private void Rebuild()
        {
            _list.Clear();
            VisualElement selectedRow = null;

            for (int i = 0; i < _items.Count; i++)
            {
                int idx = i;
                var it = _items[i];

                var row = new VisualElement();
                row.AddToClassList("cc-ac__item");
                if (i == _selected) { row.AddToClassList("cc-ac__item--sel"); selectedRow = row; }

                var lbl = new Label(it.Label);
                lbl.AddToClassList("cc-ac__label");
                row.Add(lbl);

                if (!string.IsNullOrEmpty(it.Detail))
                {
                    var d = new Label(it.Detail);
                    d.AddToClassList("cc-ac__detail");
                    row.Add(d);
                }

                row.RegisterCallback<MouseDownEvent>(e =>
                {
                    _selected = idx;
                    AcceptSelected();
                    e.StopPropagation();
                });
                _list.Add(row);
            }

            if (selectedRow != null)
                _list.schedule.Execute(() => _list.ScrollTo(selectedRow)).ExecuteLater(1);
        }
    }
}
