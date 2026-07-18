using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Éditeur « par blocs » d'un fichier MEMORY.md (session ou global). Le contenu est découpé en
    /// deux niveaux : d'abord par thématique (titres Markdown #…), puis, à l'intérieur de chaque
    /// thématique, par puce. Chaque puce est rendue en Markdown (via <see cref="MarkdownRenderer"/>)
    /// et supprimable individuellement ; une thématique entière peut aussi être supprimée. Pratique
    /// pour élaguer la mémoire sans toucher au Markdown brut. Le fichier étant aussi écrit par Claude
    /// (Write/Edit), on le relit à l'ouverture et via « Recharger ».
    /// </summary>
    public class MemoryEditorWindow : EditorWindow
    {
        private const string PKG = "Packages/com.ombrage.claude-code-gui";

        /// <summary>Une thématique : un en-tête (titre + texte d'intro) et ses puces.</summary>
        private class MemBlock
        {
            public string       Header = "";   // titre + lignes d'intro (peut être vide : préambule)
            public List<string> Items  = new(); // une entrée par puce (lignes brutes, préfixe inclus)
        }

        private string         _path;
        private string         _title;
        private List<MemBlock> _blocks = new();

        private VisualElement _list;     // conteneur défilant
        private Label         _countLbl;

        public static void Show(string title, string absolutePath)
        {
            var w = GetWindow<MemoryEditorWindow>(true, "Mémoire — blocs", true);
            w._title = title;
            w._path  = absolutePath;
            w.minSize = new Vector2(560, 460);
            w.ReadFromDisk();
            w.Rebuild();
        }

        public static void Show(Session session)
        {
            if (session == null) return;
            Show($"Session — {session.GetDisplayTitle()}", SessionStore.MemoryPath(session.id));
        }

        public void CreateGUI()
        {
            Rebuild();
        }

        private void ReadFromDisk()
        {
            string content = !string.IsNullOrEmpty(_path) && File.Exists(_path)
                ? File.ReadAllText(_path) : "";
            _blocks = Parse(content);
        }

        private void SaveToDisk()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(_path, Serialize());
        }

        // Reconstruit toute l'interface (root). Idempotent : appelé par CreateGUI et par Show.
        private void Rebuild()
        {
            var root = rootVisualElement;
            root.Clear();
            root.style.flexGrow = 1;
            root.AddToClassList(EditorGUIUtility.isProSkin ? "cc-theme-dark" : "cc-theme-light");

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{PKG}/Editor/UI/uss/claude.uss");
            if (uss != null && !root.styleSheets.Contains(uss)) root.styleSheets.Add(uss);

            if (string.IsNullOrEmpty(_path)) return; // pas encore initialisé (1er CreateGUI)

            // En-tête
            var header = new VisualElement { style = { paddingLeft = 8, paddingRight = 8, paddingTop = 6 } };
            header.Add(new Label($"MEMORY.md — {_title}") { style = { unityFontStyleAndWeight = FontStyle.Bold } });
            header.Add(new Label(_path) { style = { opacity = 0.6f, fontSize = 10, whiteSpace = WhiteSpace.Normal } });
            root.Add(header);

            // Liste défilante
            var scroll = new ScrollView(ScrollViewMode.Vertical) { style = { flexGrow = 1, minHeight = 0 } };
            _list = scroll.contentContainer;
            root.Add(scroll);

            // Pied : actions + compteur
            var footer = new VisualElement();
            footer.style.flexDirection = FlexDirection.Row;
            footer.style.flexShrink = 0;
            footer.style.paddingLeft = 8; footer.style.paddingRight = 8;
            footer.style.paddingTop = 4; footer.style.paddingBottom = 6;
            footer.Add(new Button(() => { ReadFromDisk(); RebuildList(); }) { text = "Recharger" });
            footer.Add(new Button(() => { if (File.Exists(_path)) EditorUtility.RevealInFinder(_path); })
                { text = "Ouvrir le fichier" });
            footer.Add(new VisualElement { style = { flexGrow = 1 } });
            _countLbl = new Label { style = { opacity = 0.6f, fontSize = 11, unityTextAlign = TextAnchor.MiddleRight } };
            footer.Add(_countLbl);
            root.Add(footer);

            RebuildList();
        }

        // Reconstruit uniquement la liste (après suppression / rechargement).
        private void RebuildList()
        {
            if (_list == null) return;
            _list.Clear();

            int bulletCount = 0;

            if (_blocks.Count == 0)
            {
                _list.Add(new Label("(mémoire vide)")
                    { style = { opacity = 0.6f, marginLeft = 10, marginTop = 10, whiteSpace = WhiteSpace.Normal } });
            }
            else
            {
                foreach (var block in _blocks)
                {
                    _list.Add(MakeSection(block));
                    bulletCount += block.Items.Count;
                }
            }

            if (_countLbl != null)
                _countLbl.text = $"{_blocks.Count} thématique(s), {bulletCount} puce(s)";
        }

        private VisualElement MakeSection(MemBlock block)
        {
            var section = new VisualElement();
            section.style.marginLeft = 8; section.style.marginRight = 8; section.style.marginTop = 8;

            // En-tête de thématique (titre Markdown + bouton « Supprimer la thématique »).
            if (!string.IsNullOrWhiteSpace(block.Header))
            {
                var head = new VisualElement { style = { flexDirection = FlexDirection.Row, alignItems = Align.FlexStart } };
                var md = new VisualElement { style = { flexGrow = 1 } };
                MarkdownRenderer.Render(md, block.Header);
                head.Add(md);
                var delSec = new Button(() => DeleteSection(block)) { text = "Supprimer la thématique" };
                delSec.tooltip = "Retirer cette thématique et toutes ses puces";
                head.Add(delSec);
                section.Add(head);
            }

            // Puces : chaque puce dans sa propre carte, indentée sous la thématique.
            foreach (var item in block.Items)
                section.Add(MakeBulletCard(block, item));

            return section;
        }

        private VisualElement MakeBulletCard(MemBlock block, string item)
        {
            var card = new VisualElement();
            card.style.marginLeft = 14; card.style.marginTop = 4;
            card.style.paddingLeft = 8; card.style.paddingRight = 8;
            card.style.paddingTop = 4; card.style.paddingBottom = 4;
            card.style.borderTopLeftRadius = 4; card.style.borderTopRightRadius = 4;
            card.style.borderBottomLeftRadius = 4; card.style.borderBottomRightRadius = 4;
            var border = new Color(0.5f, 0.5f, 0.5f, 0.4f);
            card.style.borderTopWidth = 1; card.style.borderBottomWidth = 1;
            card.style.borderLeftWidth = 1; card.style.borderRightWidth = 1;
            card.style.borderTopColor = border; card.style.borderBottomColor = border;
            card.style.borderLeftColor = border; card.style.borderRightColor = border;
            card.style.backgroundColor = new Color(0.5f, 0.5f, 0.5f, 0.07f);

            var md = new VisualElement { style = { flexGrow = 1 } };
            MarkdownRenderer.Render(md, item);
            card.Add(md);

            var actions = new VisualElement { style = { flexDirection = FlexDirection.Row, marginTop = 2 } };
            actions.Add(new VisualElement { style = { flexGrow = 1 } });
            string captured = item;
            var del = new Button(() => DeleteBullet(block, captured)) { text = "Supprimer" };
            del.tooltip = "Retirer cette puce";
            actions.Add(del);
            card.Add(actions);

            return card;
        }

        private void DeleteBullet(MemBlock block, string item)
        {
            if (!_blocks.Contains(block)) return;
            string preview = item.Length > 240 ? item.Substring(0, 240) + "…" : item;
            if (!EditorUtility.DisplayDialog("Supprimer cette puce ?", preview, "Supprimer", "Annuler"))
                return;

            block.Items.Remove(item);
            // Thématique vide (plus de puce ni d'en-tête) : on la retire aussi.
            if (block.Items.Count == 0 && string.IsNullOrWhiteSpace(block.Header))
                _blocks.Remove(block);

            SaveToDisk();
            RebuildList();
        }

        private void DeleteSection(MemBlock block)
        {
            if (!_blocks.Contains(block)) return;
            string title = block.Header.Replace("\n", " ");
            if (title.Length > 200) title = title.Substring(0, 200) + "…";
            if (!EditorUtility.DisplayDialog("Supprimer cette thématique ?",
                    $"{title}\n\n({block.Items.Count} puce(s) seront aussi supprimées)",
                    "Supprimer", "Annuler"))
                return;

            _blocks.Remove(block);
            SaveToDisk();
            RebuildList();
        }

        // ---- Parsing / sérialisation ----

        private static bool IsHeading(string l) => Regex.IsMatch(l, @"^#{1,6}\s");
        private static bool IsBullet(string l)  => Regex.IsMatch(l, @"^([-*+]|\d+\.)\s");

        /// <summary>
        /// Découpe le Markdown en thématiques (titres #…) ; à l'intérieur, une entrée par puce de
        /// premier niveau. Le texte avant le premier titre forme une thématique « préambule » sans
        /// en-tête. S'il n'y a aucun titre, tout est regroupé dans une seule thématique sans en-tête.
        /// </summary>
        private static List<MemBlock> Parse(string content)
        {
            var blocks = new List<MemBlock>();
            if (string.IsNullOrWhiteSpace(content)) return blocks;

            var lines = content.Replace("\r\n", "\n").Split('\n');

            MemBlock cur = null;
            var headerBuf = new StringBuilder();
            var itemBuf   = new StringBuilder();
            bool inItems  = false;

            void FlushItem()
            {
                if (itemBuf.Length > 0)
                {
                    string t = itemBuf.ToString().Trim('\n');
                    if (!string.IsNullOrWhiteSpace(t)) cur.Items.Add(t);
                    itemBuf.Clear();
                }
            }

            void FlushBlock()
            {
                if (cur == null) return;
                FlushItem();
                cur.Header = headerBuf.ToString().Trim('\n');
                headerBuf.Clear();
                if (!string.IsNullOrWhiteSpace(cur.Header) || cur.Items.Count > 0)
                    blocks.Add(cur);
                cur = null;
                inItems = false;
            }

            foreach (var line in lines)
            {
                if (IsHeading(line))
                {
                    FlushBlock();
                    cur = new MemBlock();
                    headerBuf.Append(line).Append('\n');
                    continue;
                }

                cur ??= new MemBlock(); // préambule avant tout titre

                if (IsBullet(line))
                {
                    FlushItem();
                    inItems = true;
                    itemBuf.Append(line).Append('\n');
                }
                else if (inItems)
                {
                    itemBuf.Append(line).Append('\n'); // continuation d'une puce
                }
                else
                {
                    headerBuf.Append(line).Append('\n'); // intro de la thématique
                }
            }

            FlushBlock();
            return blocks;
        }

        private string Serialize()
        {
            var parts = new List<string>();
            foreach (var b in _blocks)
            {
                var sb = new StringBuilder();
                if (!string.IsNullOrWhiteSpace(b.Header)) sb.Append(b.Header.TrimEnd('\n'));
                foreach (var it in b.Items)
                {
                    if (sb.Length > 0) sb.Append('\n');
                    sb.Append(it.TrimEnd('\n'));
                }
                string s = sb.ToString().Trim('\n');
                if (!string.IsNullOrWhiteSpace(s)) parts.Add(s);
            }
            return parts.Count == 0 ? "" : string.Join("\n\n", parts).TrimEnd('\n') + "\n";
        }
    }
}
