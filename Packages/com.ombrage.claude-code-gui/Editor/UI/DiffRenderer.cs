using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine.UIElements;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Construit un rendu visuel des modifications de fichiers (Edit / MultiEdit / Write)
    /// à partir de l'input canonique de l'outil : lignes supprimées en rouge, ajoutées en vert.
    /// </summary>
    public static class DiffRenderer
    {
        private const int MAX_LINES = 200;

        public static VisualElement Build(string toolName, JToken input)
        {
            var root = new VisualElement();
            root.AddToClassList("cc-diff");
            if (input == null) return root;

            string fp = (string)input["file_path"];
            if (!string.IsNullOrEmpty(fp))
            {
                var file = new Label(Path.GetFileName(fp));
                file.tooltip = fp;
                file.AddToClassList("cc-diff-file");
                root.Add(file);
            }

            switch (toolName)
            {
                case "Edit":
                    AddHunk(root, (string)input["old_string"], (string)input["new_string"]);
                    break;

                case "MultiEdit":
                    var edits = input["edits"] as JArray;
                    if (edits != null)
                        for (int i = 0; i < edits.Count; i++)
                        {
                            if (i > 0) root.Add(Spacer());
                            AddHunk(root, (string)edits[i]["old_string"], (string)edits[i]["new_string"]);
                        }
                    break;

                case "Write":
                    // Nouveau contenu : tout en ajout.
                    AddLines(root, (string)input["content"], add: true);
                    break;
            }

            return root;
        }

        private static void AddHunk(VisualElement root, string oldStr, string newStr)
        {
            AddLines(root, oldStr, add: false);
            AddLines(root, newStr, add: true);
        }

        private static void AddLines(VisualElement root, string text, bool add)
        {
            if (string.IsNullOrEmpty(text)) return;
            var lines = text.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n');
            int count = 0;
            string prefix = add ? "+ " : "- ";
            string cls = add ? "cc-diff-add" : "cc-diff-del";

            foreach (var line in lines)
            {
                if (count >= MAX_LINES)
                {
                    var more = new Label("… (tronqué)");
                    more.AddToClassList(cls);
                    root.Add(more);
                    break;
                }
                var lbl = new Label(prefix + line);
                lbl.AddToClassList(cls);
                lbl.enableRichText = false;
                lbl.selection.isSelectable = true;
                lbl.style.whiteSpace = WhiteSpace.Normal;
                root.Add(lbl);
                count++;
            }
        }

        private static VisualElement Spacer()
        {
            var s = new VisualElement();
            s.style.height = 4;
            return s;
        }
    }
}
