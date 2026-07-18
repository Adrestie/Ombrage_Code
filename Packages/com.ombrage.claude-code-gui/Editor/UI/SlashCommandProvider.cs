using System;
using System.Collections.Generic;
using System.IO;

namespace Ombrage.ClaudeCodeGUI
{
    public readonly struct SlashCommand
    {
        public readonly string Name;        // sans le slash
        public readonly string Description;
        public readonly bool   IsCustom;
        public SlashCommand(string name, string desc, bool custom) { Name = name; Description = desc; IsCustom = custom; }
    }

    /// <summary>
    /// Fournit la liste des slash commands : une sélection d'intégrées + les commandes
    /// personnalisées découvertes dans .claude/commands/*.md (projet puis utilisateur).
    /// </summary>
    public static class SlashCommandProvider
    {
        private static readonly (string name, string desc)[] BuiltIns =
        {
            ("init",            "Analyser le projet et générer/mettre à jour CLAUDE.md"),
            ("deep-research",   "Recherche approfondie multi-sources avec rapport sourcé"),
            ("review",          "Relire les changements en cours"),
            ("security-review", "Revue de sécurité des changements"),
            ("clear",           "Effacer le contexte de conversation"),
            ("compact",         "Compacter la conversation"),
            ("cost",            "Afficher le coût et l'usage de la session"),
            ("model",           "Changer de modèle"),
            ("memory",          "Éditer les fichiers de mémoire"),
            ("agents",          "Gérer les sous-agents"),
            ("mcp",             "Gérer les serveurs MCP"),
            ("help",            "Aide"),
        };

        public static List<SlashCommand> GetCommands(string projectRoot)
        {
            var list = new List<SlashCommand>();
            foreach (var (name, desc) in BuiltIns) list.Add(new SlashCommand(name, desc, false));

            AddCustom(list, Path.Combine(projectRoot ?? "", ".claude", "commands"));
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            AddCustom(list, Path.Combine(home, ".claude", "commands"));
            return list;
        }

        private static void AddCustom(List<SlashCommand> list, string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                foreach (string file in Directory.GetFiles(dir, "*.md"))
                {
                    string name = Path.GetFileNameWithoutExtension(file);
                    if (list.Exists(c => c.Name == name)) continue;
                    list.Add(new SlashCommand(name, ReadDescription(file), true));
                }
            }
            catch { /* dossier illisible : on ignore */ }
        }

        private static string ReadDescription(string file)
        {
            try
            {
                foreach (string raw in File.ReadLines(file))
                {
                    string line = raw.Trim();
                    if (line.StartsWith("description:"))
                        return line.Substring("description:".Length).Trim().Trim('"');
                    if (line.Length > 0 && !line.StartsWith("---") && !line.StartsWith("#"))
                        return line.Length > 80 ? line.Substring(0, 80) + "…" : line;
                }
            }
            catch { /* ignore */ }
            return "Commande personnalisée";
        }
    }
}
