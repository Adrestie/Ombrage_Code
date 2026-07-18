using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>Un modèle proposé dans le sélecteur.</summary>
    public readonly struct ModelEntry
    {
        public readonly string Id;       // valeur passée à --model (alias ou ID complet)
        public readonly string Label;    // libellé affiché
        public readonly bool   IsAlias;  // true = alias (pointe toujours vers le dernier modèle)

        public ModelEntry(string id, string label, bool isAlias)
        {
            Id = id; Label = label; IsAlias = isAlias;
        }
    }

    /// <summary>
    /// Catalogue des modèles proposés au sélecteur. La CLI Claude Code n'expose pas de
    /// commande « lister les modèles » fiable : on combine donc des alias (toujours à jour),
    /// une liste curatée d'IDs connus, les modèles récemment utilisés (mémorisés), et le
    /// modèle par défaut lu best-effort dans ~/.claude.json.
    /// </summary>
    public static class ModelCatalog
    {
        private const string PREFS_RECENTS = "ClaudeCodeGUI_RecentModels";
        private const int    MAX_RECENTS   = 6;

        // Alias : pointent toujours vers le dernier modèle de la famille côté CLI.
        public static readonly ModelEntry[] Aliases =
        {
            new("opus",   "Opus (dernier)",   true),
            new("sonnet", "Sonnet (dernier)", true),
            new("haiku",  "Haiku (dernier)",  true),
        };

        // IDs complets connus à la date de rédaction. Modifiables sans risque : ce ne sont
        // que des suggestions ; tout ID peut être saisi librement par l'utilisateur.
        public static readonly ModelEntry[] KnownFullIds =
        {
            new ModelEntry("claude-fable-5",           "Fable", false),
            new("claude-opus-4-8",            "Opus 4.8",   false),
            new("claude-sonnet-4-6",          "Sonnet 4.6", false),
            new("claude-haiku-4-5-20251001",  "Haiku 4.5",  false)
        };

        /// <summary>Liste complète proposée au sélecteur (alias + IDs connus + récents).</summary>
        public static List<ModelEntry> BuildList()
        {
            var list = new List<ModelEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void Add(ModelEntry e)
            {
                if (string.IsNullOrWhiteSpace(e.Id) || !seen.Add(e.Id)) return;
                list.Add(e);
            }

            foreach (var a in Aliases) Add(a);
            foreach (var f in KnownFullIds) Add(f);

            foreach (string recent in LoadRecents())
                Add(new ModelEntry(recent, recent, false));

            string def = TryReadDefaultModel();
            if (!string.IsNullOrWhiteSpace(def))
                Add(new ModelEntry(def, $"{def} (défaut CLI)", false));

            return list;
        }

        public static void RememberUsed(string modelId)
        {
            if (string.IsNullOrWhiteSpace(modelId)) return;
            var recents = LoadRecents();
            recents.RemoveAll(m => string.Equals(m, modelId, StringComparison.OrdinalIgnoreCase));
            recents.Insert(0, modelId);
            if (recents.Count > MAX_RECENTS) recents = recents.Take(MAX_RECENTS).ToList();
            EditorPrefs.SetString(PREFS_RECENTS, string.Join("\n", recents));
        }

        public static List<string> LoadRecents()
        {
            string raw = EditorPrefs.GetString(PREFS_RECENTS, "");
            return string.IsNullOrEmpty(raw)
                ? new List<string>()
                : raw.Split('\n').Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        }

        /// <summary>Lit best-effort le modèle par défaut dans ~/.claude.json (peut renvoyer null).</summary>
        private static string TryReadDefaultModel()
        {
            try
            {
                string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string path = Path.Combine(home, ".claude.json");
                if (!File.Exists(path)) return null;
                var obj = JObject.Parse(File.ReadAllText(path));
                // Selon les versions : "model" à la racine.
                return (string)obj["model"];
            }
            catch
            {
                return null;
            }
        }
    }
}
