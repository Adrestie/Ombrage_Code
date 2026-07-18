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
    /// Rendu Markdown minimal vers des VisualElements UI Toolkit : titres, paragraphes,
    /// listes, citations, règles horizontales, blocs de code (copiables) et formatage inline
    /// (gras, italique, code, liens) via le rich text de TextCore.
    /// </summary>
    public static class MarkdownRenderer
    {
        public static void Render(VisualElement container, string markdown)
        {
            if (string.IsNullOrEmpty(markdown)) return;

            var lines = markdown.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            var paragraph = new List<string>();
            int i = 0;

            void FlushParagraph()
            {
                if (paragraph.Count == 0) return;
                container.Add(MakeInlineLabel(string.Join("\n", paragraph), "cc-md-p"));
                paragraph.Clear();
            }

            while (i < lines.Length)
            {
                string line = lines[i];
                string trimmed = line.TrimStart();

                // Bloc de code clôturé ```
                if (trimmed.StartsWith("```"))
                {
                    FlushParagraph();
                    string lang = trimmed.Substring(3).Trim();
                    var sb = new StringBuilder();
                    i++;
                    while (i < lines.Length && !lines[i].TrimStart().StartsWith("```"))
                    {
                        sb.AppendLine(lines[i]);
                        i++;
                    }
                    i++; // saute la clôture
                    AddCodeBlock(container, lang, sb.ToString().TrimEnd('\n'));
                    continue;
                }

                // Titre
                var h = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
                if (h.Success)
                {
                    FlushParagraph();
                    int level = Mathf.Clamp(h.Groups[1].Value.Length, 1, 3);
                    container.Add(MakeInlineLabel(h.Groups[2].Value, $"cc-md-h{level}"));
                    i++;
                    continue;
                }

                // Règle horizontale
                if (Regex.IsMatch(trimmed, @"^(-{3,}|\*{3,}|_{3,})$"))
                {
                    FlushParagraph();
                    var hr = new VisualElement();
                    hr.AddToClassList("cc-md-hr");
                    container.Add(hr);
                    i++;
                    continue;
                }

                // Citation
                if (trimmed.StartsWith("> "))
                {
                    FlushParagraph();
                    container.Add(MakeInlineLabel(trimmed.Substring(2), "cc-md-quote"));
                    i++;
                    continue;
                }

                // Tableau : ligne avec '|' suivie d'une ligne de séparation
                if (line.Contains('|') && i + 1 < lines.Length && IsTableSeparator(lines[i + 1]))
                {
                    FlushParagraph();
                    var headerCells = SplitRow(line);
                    i += 2; // saute l'en-tête et la ligne de séparation
                    var bodyRows = new List<string[]>();
                    while (i < lines.Length && lines[i].Contains('|') && !string.IsNullOrWhiteSpace(lines[i]))
                    {
                        bodyRows.Add(SplitRow(lines[i]));
                        i++;
                    }
                    AddTable(container, headerCells, bodyRows);
                    continue;
                }

                // Élément de liste
                var li = Regex.Match(line, @"^(\s*)([-*+]|\d+\.)\s+(.*)$");
                if (li.Success)
                {
                    FlushParagraph();
                    int indentWidth = CountIndent(li.Groups[1].Value);
                    bool ordered = char.IsDigit(li.Groups[2].Value[0]);
                    string bullet = ordered ? li.Groups[2].Value : (indentWidth >= 2 ? "◦" : "•");
                    AddListItem(container, bullet, li.Groups[3].Value, indentWidth);
                    i++;
                    continue;
                }

                // Ligne vide = fin de paragraphe
                if (string.IsNullOrWhiteSpace(line))
                {
                    FlushParagraph();
                    i++;
                    continue;
                }

                paragraph.Add(line);
                i++;
            }

            FlushParagraph();
        }

        private static void AddListItem(VisualElement container, string bullet, string text, int indentWidth)
        {
            var row = new VisualElement();
            row.AddToClassList("cc-md-li-row");
            // 8 px par niveau d'indentation pour bien distinguer les sous-éléments.
            row.style.marginLeft = 8 + Mathf.Clamp(indentWidth, 0, 16) * 8;

            var b = new Label(bullet);
            b.AddToClassList("cc-md-bullet");
            row.Add(b);

            var content = MakeInlineLabel(text, "cc-md-p");
            content.style.flexGrow = 1;
            row.Add(content);

            container.Add(row);
        }

        // ---- Tableaux ----

        private static bool IsTableSeparator(string line)
        {
            string t = line.Trim();
            if (t.Length == 0 || !t.Contains('-')) return false;
            foreach (char c in t)
                if (c != '|' && c != '-' && c != ':' && c != ' ') return false;
            return true;
        }

        private static string[] SplitRow(string line)
        {
            string t = line.Trim();
            if (t.StartsWith("|")) t = t.Substring(1);
            if (t.EndsWith("|")) t = t.Substring(0, t.Length - 1);
            return t.Split('|').Select(c => c.Trim()).ToArray();
        }

        private static void AddTable(VisualElement container, string[] header, List<string[]> body)
        {
            int cols = header?.Length ?? 0;
            foreach (var r in body) cols = Mathf.Max(cols, r?.Length ?? 0);

            // Scroll horizontal : un tableau plus large que la zone défile au lieu de déborder.
            var scroll = new ScrollView(ScrollViewMode.Horizontal);
            scroll.AddToClassList("cc-md-tablewrap");

            var table = new VisualElement();
            table.AddToClassList("cc-md-table");
            table.Add(MakeTableRow(header, head: true));
            foreach (var r in body) table.Add(MakeTableRow(r, head: false));
            scroll.Add(table);

            // Une fois les cellules mesurées (une ligne chacune), on aligne les colonnes : largeur
            // de colonne = max des largeurs mesurées. Une seule passe (drapeau pour éviter la boucle).
            if (cols > 0)
            {
                bool done = false;
                table.RegisterCallback<GeometryChangedEvent>(_ =>
                {
                    if (done) return;
                    var rows = table.Children().ToList();
                    if (rows.Count == 0) return;

                    var widths = new float[cols];
                    foreach (var row in rows)
                    {
                        var cells = row.Children().ToList();
                        for (int c = 0; c < cells.Count && c < cols; c++)
                            widths[c] = Mathf.Max(widths[c], cells[c].resolvedStyle.width);
                    }
                    for (int c = 0; c < cols; c++)
                        if (widths[c] <= 0f) return; // pas encore mesuré : on attend la prochaine passe

                    foreach (var row in rows)
                    {
                        var cells = row.Children().ToList();
                        for (int c = 0; c < cells.Count && c < cols; c++)
                            cells[c].style.width = widths[c];
                    }
                    done = true;
                });
            }

            container.Add(scroll);
        }

        private static VisualElement MakeTableRow(string[] cells, bool head)
        {
            var row = new VisualElement();
            row.AddToClassList("cc-md-tr");
            foreach (string cell in cells)
            {
                var c = new Label(ToRichText(cell));
                c.AddToClassList(head ? "cc-md-th" : "cc-md-td");
                c.enableRichText = true;
                c.selection.isSelectable = true;
                c.style.whiteSpace = WhiteSpace.NoWrap; // une seule ligne : aucune mesure de hauteur fragile
                row.Add(c);
            }
            return row;
        }

        private static int CountIndent(string whitespace)
        {
            int n = 0;
            foreach (char c in whitespace) n += (c == '\t') ? 4 : 1;
            return n;
        }

        private static void AddCodeBlock(VisualElement container, string lang, string code)
        {
            var box = new VisualElement();
            box.AddToClassList("cc-md-code");

            var bar = new VisualElement();
            bar.AddToClassList("cc-md-codebar");
            var langLabel = new Label(string.IsNullOrEmpty(lang) ? "code" : lang);
            langLabel.AddToClassList("cc-md-codelang");
            bar.Add(langLabel);
            var create = new Button(() => CreateScriptFromCode(code)) { text = "＋ Script" };
            create.tooltip = "Créer un script C# à partir de ce bloc";
            bar.Add(create);

            Button copy = null;
            copy = new Button(() =>
            {
                EditorGUIUtility.systemCopyBuffer = code;
                copy.text = "✓ Copié";
                copy.SetEnabled(false);
                copy.schedule.Execute(() => { copy.text = "⎘"; copy.SetEnabled(true); }).ExecuteLater(1200);
            }) { text = "⎘" };
            copy.tooltip = "Copier le bloc de code";
            bar.Add(copy);
            box.Add(bar);

            var text = new Label(code);
            text.AddToClassList("cc-md-codetext");
            text.enableRichText = false;
            text.selection.isSelectable = true;
            text.style.whiteSpace = WhiteSpace.Normal;
            box.Add(text);

            container.Add(box);
        }

        private static void CreateScriptFromCode(string code)
        {
            string cls = Regex.Match(code ?? "", @"class\s+([A-Za-z_][A-Za-z0-9_]*)").Groups[1].Value;
            string defaultName = string.IsNullOrEmpty(cls) ? "NewScript" : cls;
            string path = EditorUtility.SaveFilePanelInProject(
                "Créer un script", defaultName, "cs", "Emplacement du nouveau script", "Assets");
            if (string.IsNullOrEmpty(path)) return;

            File.WriteAllText(path, code ?? "");
            AssetDatabase.ImportAsset(path);
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            if (asset != null) EditorGUIUtility.PingObject(asset);
        }

        private static Label MakeInlineLabel(string text, string className)
        {
            var lbl = new Label(ToRichText(text));
            lbl.AddToClassList(className);
            lbl.enableRichText = true;
            lbl.selection.isSelectable = true;
            lbl.style.whiteSpace = WhiteSpace.Normal;
            return lbl;
        }

        /// <summary>Convertit le formatage inline Markdown en rich text TextCore.</summary>
        private static string ToRichText(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";

            // Code inline : on protège le contenu avec <noparse> pour neutraliser les '<' éventuels.
            s = Regex.Replace(s, "`([^`]+)`",
                m => $"<color=#d6b36a><noparse>{m.Groups[1].Value}</noparse></color>");

            // Gras
            s = Regex.Replace(s, @"\*\*([^*]+)\*\*", "<b>$1</b>");
            s = Regex.Replace(s, @"__([^_]+)__", "<b>$1</b>");
            // Barré (avant l'italique pour ne pas confondre ~~ et _)
            s = Regex.Replace(s, @"~~([^~]+)~~", "<s>$1</s>");
            // Italique
            s = Regex.Replace(s, @"(?<!\*)\*([^*\n]+)\*(?!\*)", "<i>$1</i>");
            s = Regex.Replace(s, @"(?<!_)_([^_\n]+)_(?!_)", "<i>$1</i>");

            // Liens [texte](url) -> texte (url)
            s = Regex.Replace(s, @"\[([^\]]+)\]\(([^)]+)\)", "<b>$1</b> ($2)");

            return s;
        }
    }
}
