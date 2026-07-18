using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Ombrage.ClaudeCodeGUI
{
    public static class ToolDescriptions
    {
        /// <summary>Explication courte (tooltip) de ce que fait un outil donné.</summary>
        public static string GetToolHelp(string toolName)
        {
            switch (toolName)
            {
                case "Read":        return "Lit le contenu d'un fichier (texte, image, PDF).";
                case "Write":       return "Crée un fichier ou écrase entièrement un fichier existant.";
                case "Edit":        return "Remplace une portion de texte précise dans un fichier.";
                case "MultiEdit":   return "Applique plusieurs remplacements dans un même fichier en une fois.";
                case "Bash":        return "Exécute une commande shell (compilation, git, scripts…).";
                case "Glob":        return "Recherche des fichiers par motif (ex : Assets/**/*.cs).";
                case "Grep":        return "Recherche du texte/regex dans le contenu des fichiers.";
                case "WebSearch":   return "Effectue une recherche sur le web.";
                case "WebFetch":    return "Récupère une URL et en analyse le contenu.";
                case "Task":
                case "Agent":       return "Lance un sous-agent autonome pour une tâche complexe.";
                case "TodoWrite":   return "Gère une liste de tâches interne pour suivre l'avancement.";
                case "NotebookEdit":return "Modifie les cellules d'un notebook Jupyter (.ipynb).";
                case "AskUserQuestion":
                    return "Pose une ou plusieurs questions à choix à l'utilisateur (gérées par une boîte de dialogue dédiée).";
                default:
                    if (!string.IsNullOrEmpty(toolName) && toolName.StartsWith("mcp__"))
                        return "Outil fourni par un serveur MCP externe.";
                    return "Outil Claude Code.";
            }
        }

        public static string BuildDescription(string toolName, JToken input)
        {
            switch (toolName)
            {
                case "Read":
                {
                    string fp = (string)input?["file_path"];
                    return $"Lecture de {(fp != null ? Path.GetFileName(fp) : "?")}";
                }
                case "Write":
                {
                    string fp = (string)input?["file_path"];
                    return $"Écriture dans {(fp != null ? Path.GetFileName(fp) : "?")}";
                }
                case "Edit":
                {
                    string fp = (string)input?["file_path"];
                    return $"Modification de {(fp != null ? Path.GetFileName(fp) : "?")}";
                }
                case "MultiEdit":
                {
                    string fp = (string)input?["file_path"];
                    return $"Modifications multiples dans {(fp != null ? Path.GetFileName(fp) : "?")}";
                }
                case "Bash":
                {
                    string cmd = (string)input?["command"];
                    if (cmd != null && cmd.Length > 80) cmd = cmd.Substring(0, 80) + "…";
                    return $"Exécution : {cmd ?? "?"}";
                }
                case "Glob":
                    return $"Recherche de fichiers : {(string)input?["pattern"] ?? "?"}";
                case "Grep":
                {
                    string pattern = (string)input?["pattern"];
                    string path = (string)input?["path"];
                    string dir = path != null ? Path.GetFileName(path) : "";
                    return $"Grep \"{pattern ?? "?"}\" dans {dir}";
                }
                case "Agent":
                case "Task":
                    return $"Sous-agent : {(string)input?["description"] ?? "tâche"}";
                case "WebSearch":
                    return $"Recherche web : {(string)input?["query"] ?? "?"}";
                case "WebFetch":
                    return $"Récupération : {(string)input?["url"] ?? "?"}";
                case "AskUserQuestion":
                {
                    var questions = input?["questions"] as JArray;
                    if (questions is { Count: > 0 })
                    {
                        string q = (string)questions[0]["question"];
                        if (!string.IsNullOrEmpty(q))
                        {
                            if (q.Length > 80) q = q.Substring(0, 80) + "…";
                            return $"Question : {q}";
                        }
                    }
                    return "Question à l'utilisateur";
                }
                default:
                    return toolName ?? "outil inconnu";
            }
        }

        public static string BuildInputDisplay(string toolName, JToken input)
        {
            if (input == null) return null;

            switch (toolName)
            {
                case "Read":
                {
                    string fp = (string)input["file_path"];
                    return fp != null ? $"Fichier : {fp}" : null;
                }
                case "Write":
                {
                    string fp = (string)input["file_path"];
                    string content = (string)input["content"];
                    var sb = new StringBuilder();
                    if (fp != null) sb.AppendLine($"Fichier : {fp}");
                    if (content != null)
                    {
                        if (content.Length > 1000) content = content.Substring(0, 1000) + "\n… (tronqué)";
                        sb.AppendLine("Contenu :");
                        sb.Append(content);
                    }
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                case "Edit":
                {
                    string fp = (string)input["file_path"];
                    string oldStr = (string)input["old_string"];
                    string newStr = (string)input["new_string"];
                    var sb = new StringBuilder();
                    if (fp != null) sb.AppendLine($"Fichier : {fp}");
                    if (oldStr != null)
                    {
                        if (oldStr.Length > 500) oldStr = oldStr.Substring(0, 500) + "…";
                        sb.AppendLine($"- {oldStr}");
                    }
                    if (newStr != null)
                    {
                        if (newStr.Length > 500) newStr = newStr.Substring(0, 500) + "…";
                        sb.AppendLine($"+ {newStr}");
                    }
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                case "Bash":
                    return (string)input["command"];
                case "Glob":
                case "Grep":
                {
                    string pattern = (string)input["pattern"];
                    string path = (string)input["path"];
                    var sb = new StringBuilder();
                    if (pattern != null) sb.AppendLine($"Pattern : {pattern}");
                    if (path != null) sb.AppendLine($"Chemin : {path}");
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                case "AskUserQuestion":
                {
                    var questions = input["questions"] as JArray;
                    if (questions == null) return null;
                    var sb = new StringBuilder();
                    for (int i = 0; i < questions.Count; i++)
                    {
                        string q = (string)questions[i]["question"];
                        if (!string.IsNullOrEmpty(q))
                            sb.AppendLine($"• {q}");
                        var opts = questions[i]["options"] as JArray;
                        if (opts != null)
                            foreach (var o in opts)
                            {
                                string l = (string)o["label"];
                                if (!string.IsNullOrEmpty(l)) sb.AppendLine($"    - {l}");
                            }
                    }
                    return sb.Length > 0 ? sb.ToString() : null;
                }
                default:
                    return null;
            }
        }
    }
}
