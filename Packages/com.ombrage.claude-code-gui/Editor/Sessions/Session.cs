using System;
using System.Collections.Generic;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Représente une session de conversation avec Claude Code.
    /// Sérialisée en JSON dans Library/ClaudeCodeGUI/sessions/.
    /// Les champs ajoutés (favorite, tags, usage, notes…) sont rétro-compatibles : une
    /// session écrite par l'ancien outil se charge sans erreur (valeurs par défaut).
    /// </summary>
    [Serializable]
    public class Session
    {
        public string id;
        public string title;
        public string model;
        public string systemPrompt;
        public string permissionMode = "";          // "", "plan", "acceptEdits", "bypassPermissions"
        public string effort = "";                   // "", "low", "medium", "high", "xhigh", "max"
        public string allowedTools = "";             // CSV des outils autorisés
        public List<string> contextPaths = new();
        public List<Exchange> exchanges = new();

        /// <summary>
        /// Transcript riche et complet du chat (texte streamé, réflexion, outils, blocs
        /// workflow…), persisté pour survivre au changement de session, à la fermeture du tool
        /// ou d'Unity. <see cref="exchanges"/> ne garde que les couples prompt/réponse ;
        /// <c>transcript</c> grave TOUT ce qui s'affiche (source de vérité de RebuildFromLog).
        ///
        /// <para><b>[NonSerialized]</b> : EXCLU du JSON principal de session et stocké dans un
        /// fichier sidecar dédié (<c>sessions/&lt;id&gt;.transcript.json</c>), chargé À LA DEMANDE
        /// (jamais par <see cref="SessionStore.LoadAll"/>, qui n'alimente que la barre latérale).
        /// Sans ça, une session de plusieurs centaines d'échanges alourdirait chaque rafraîchissement
        /// de liste. <c>null</c> = « pas encore chargé » (sentinelle pour le chargement paresseux).</para>
        /// </summary>
        [NonSerialized]
        public List<ChatEntry> transcript = new();

        /// <summary>
        /// Pointeur vers un run Workflow NON terminé sur cette session (id côté démon + dernier
        /// seq reçu). Persisté dans le sidecar transcript. Permet, à la réouverture de l'outil
        /// (ou au retour sur la session), de proposer « Reprendre où il en était » même après
        /// une fermeture propre. Vidé (null) quand le run se termine.
        /// </summary>
        [NonSerialized] public string wfRunId;
        [NonSerialized] public int    wfLastSeq = -1;

        public long createdAt;
        public long updatedAt;

        // Organisation
        public bool favorite;
        public List<string> tags = new();

        /// <summary>Notes Markdown attachées à la session (scratchpad / mémoire locale).</summary>
        public string notes = "";

        // Usage cumulé sur la session
        public double totalCostUsd;
        public int    totalInputTokens;
        public int    totalOutputTokens;

        /// <summary>ID de session Claude Code (récupéré via --output-format json) pour --resume.</summary>
        public string claudeSessionId;

        public static Session Create(string title, string model)
        {
            var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            return new Session
            {
                id = Guid.NewGuid().ToString("N")[..12],
                title = title,
                model = model,
                createdAt = now,
                updatedAt = now,
            };
        }

        public void AddExchange(string prompt, string response)
        {
            exchanges.Add(new Exchange
            {
                prompt = prompt,
                response = response,
                timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            });
            updatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public void AddUsage(ResultUsage usage)
        {
            totalCostUsd      += usage.CostUsd;
            totalInputTokens  += usage.InputTokens;
            totalOutputTokens += usage.OutputTokens;
            updatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public string GetDisplayTitle()
        {
            if (!string.IsNullOrEmpty(title)) return title;
            if (exchanges != null && exchanges.Count > 0 && !string.IsNullOrEmpty(exchanges[0].prompt))
            {
                string first = exchanges[0].prompt;
                return first.Length > 50 ? first.Substring(0, 50) + "…" : first;
            }
            // id court/vide possible (legacy/import) : on ne tronque jamais au-delà de la longueur.
            if (!string.IsNullOrEmpty(id))
                return "Session " + (id.Length > 6 ? id.Substring(0, 6) : id);
            return "Session";
        }

        public string GetDateDisplay()
        {
            var dt = DateTimeOffset.FromUnixTimeSeconds(updatedAt).LocalDateTime;
            var now = DateTime.Now;
            if (dt.Date == now.Date) return dt.ToString("HH:mm");
            if (dt.Date == now.Date.AddDays(-1)) return "Hier " + dt.ToString("HH:mm");
            return dt.ToString("dd/MM HH:mm");
        }

        /// <summary>Texte agrégé pour la recherche (titre + tags + prompts).</summary>
        public bool Matches(string filter)
        {
            if (string.IsNullOrWhiteSpace(filter)) return true;
            if (GetDisplayTitle().Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (string tag in tags)
                if (tag != null && tag.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
            foreach (var ex in exchanges)
                if (ex.prompt != null && ex.prompt.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }

    [Serializable]
    public class Exchange
    {
        public string prompt;
        public string response;
        public long timestamp;
    }
}
