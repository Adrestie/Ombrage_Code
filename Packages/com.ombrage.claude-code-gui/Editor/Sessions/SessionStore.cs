using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Persistence des sessions en JSON dans Library/ClaudeCodeGUI/sessions/.
    /// Le dossier Library/ est ignoré par le VCS mais persiste localement.
    /// Les sessions de l'ancien outil (Library/ClaudeCode/sessions) sont importées
    /// automatiquement en lecture si elles ne sont pas déjà présentes.
    /// </summary>
    public static class SessionStore
    {
        private static string Root => Path.Combine(Application.dataPath, "..", "Library", "ClaudeCodeGUI");

        private static string SessionDir
        {
            get
            {
                string dir = Path.Combine(Root, "sessions");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string LegacyDir =>
            Path.Combine(Application.dataPath, "..", "Library", "ClaudeCode", "sessions");

        public static void Save(Session session)
        {
            string path = Path.Combine(SessionDir, $"{session.id}.json");
            string json = JsonUtility.ToJson(session, true);
            File.WriteAllText(path, json);

            // Le transcript (lourd) vit dans un sidecar séparé. On ne le grave QUE s'il est
            // chargé (non null) : une session issue de LoadAll a transcript=null → on ne
            // risque pas d'écraser le sidecar existant avec du vide (ex. bascule de favori).
            if (session.transcript != null) SaveTranscript(session);
        }

        // ---- Transcript riche (sidecar par session, chargé à la demande) ----

        [Serializable]
        private class TranscriptFile
        {
            public List<ChatEntry> entries = new();
            public string wfRunId;          // run Workflow non terminé (null si aucun)
            public int    wfLastSeq = -1;   // dernier seq reçu pour ce run
        }

        // Sous-dossier dédié : les sidecars ne doivent PAS être ramassés par le glob *.json
        // de LoadFrom (qui scanne SessionDir sans récursion).
        private static string TranscriptDir
        {
            get
            {
                string dir = Path.Combine(SessionDir, "transcripts");
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                return dir;
            }
        }

        private static string TranscriptPath(string id) => Path.Combine(TranscriptDir, $"{id}.json");

        /// <summary>Grave le seul transcript (sidecar) — léger, appelé en continu pendant un run.</summary>
        public static void SaveTranscript(Session session)
        {
            if (session?.transcript == null) return;
            var wrap = new TranscriptFile
            {
                entries = session.transcript,
                wfRunId = session.wfRunId,
                wfLastSeq = session.wfLastSeq,
            };
            File.WriteAllText(TranscriptPath(session.id), JsonUtility.ToJson(wrap, true));
        }

        /// <summary>Charge UNIQUEMENT le pointeur de run Workflow (wfRunId / wfLastSeq) depuis le
        /// sidecar, sans toucher au transcript. Utile quand le transcript provient déjà d'ailleurs
        /// (ex. champ sérialisé de la fenêtre après une recompilation).</summary>
        public static void LoadRunPointer(Session session)
        {
            if (session == null) return;
            string path = TranscriptPath(session.id);
            if (!File.Exists(path)) return;
            try
            {
                var wrap = JsonUtility.FromJson<TranscriptFile>(File.ReadAllText(path));
                session.wfRunId = wrap?.wfRunId;
                session.wfLastSeq = wrap?.wfLastSeq ?? -1;
            }
            catch { /* best-effort */ }
        }

        /// <summary>Charge le transcript dans la session s'il ne l'est pas déjà (null = non chargé),
        /// ainsi que le pointeur de run Workflow non terminé (wfRunId / wfLastSeq).</summary>
        public static void EnsureTranscript(Session session)
        {
            if (session == null || session.transcript != null) return;
            string path = TranscriptPath(session.id);
            if (!File.Exists(path))
            {
                session.transcript = new List<ChatEntry>();
                return;
            }
            try
            {
                var wrap = JsonUtility.FromJson<TranscriptFile>(File.ReadAllText(path));
                session.transcript = wrap?.entries ?? new List<ChatEntry>();
                session.wfRunId = wrap?.wfRunId;
                session.wfLastSeq = wrap?.wfLastSeq ?? -1;
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[ClaudeCodeGUI] Transcript illisible {path}: {ex.Message}");
                session.transcript = new List<ChatEntry>();
            }
        }

        // ---- Mémoire de session (MEMORY.md dédié, par session) ----

        /// <summary>Chemin absolu du MEMORY.md de la session (le dossier est créé au besoin).</summary>
        public static string MemoryPath(string sessionId)
        {
            string dir = Path.Combine(Root, "memory", sessionId);
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.GetFullPath(Path.Combine(dir, "MEMORY.md"));
        }

        public static string ReadMemory(string sessionId)
        {
            string path = MemoryPath(sessionId);
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        public static void WriteMemory(string sessionId, string content)
        {
            File.WriteAllText(MemoryPath(sessionId), content ?? "");
        }

        /// <summary>Garantit l'existence du MEMORY.md de la session (le crée avec un en-tête si
        /// absent — cas d'une NOUVELLE session). Renvoie le chemin absolu.</summary>
        public static string EnsureSessionMemory(string sessionId)
        {
            string path = MemoryPath(sessionId);
            if (!File.Exists(path))
                File.WriteAllText(path,
                    $"# Mémoire de session ({sessionId})\n\n" +
                    "_Faits durables propres à cette session (décisions, état, points en cours)._\n");
            return path;
        }

        // ---- Mémoire globale du projet (partagée par toutes les sessions) ----

        /// <summary>Chemin absolu du MEMORY.md global du projet.</summary>
        public static string GlobalMemoryPath()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, "..", "MEMORY.md"));
        }

        public static string ReadGlobalMemory()
        {
            string path = GlobalMemoryPath();
            return File.Exists(path) ? File.ReadAllText(path) : "";
        }

        /// <summary>Garantit l'existence du MEMORY.md global (le crée avec un en-tête si absent).
        /// Renvoie le chemin absolu.</summary>
        public static string EnsureGlobalMemory()
        {
            string path = GlobalMemoryPath();
            if (!File.Exists(path))
                File.WriteAllText(path,
                    "# Mémoire globale du projet\n\n" +
                    "_Résumés TRÈS courts des sessions + informations durables du projet. " +
                    "Chaque entrée pointe vers l'id de la session concernée._\n");
            return path;
        }

        public static void Delete(Session session)
        {
            string path = Path.Combine(SessionDir, $"{session.id}.json");
            if (File.Exists(path)) File.Delete(path);

            // Sidecar transcript de cette session.
            try { string tp = TranscriptPath(session.id); if (File.Exists(tp)) File.Delete(tp); } catch { }

            // Supprimer aussi la copie de l'ancien outil, sinon LoadAll la ré-importe
            // depuis LegacyDir et la session « supprimée » réapparaît aussitôt.
            string legacyPath = Path.Combine(LegacyDir, $"{session.id}.json");
            try { if (File.Exists(legacyPath)) File.Delete(legacyPath); } catch { }

            string memDir = Path.Combine(Root, "memory", session.id);
            try { if (Directory.Exists(memDir)) Directory.Delete(memDir, true); } catch { }
        }

        public static Session Load(string id)
        {
            string path = Path.Combine(SessionDir, $"{id}.json");
            if (!File.Exists(path)) return null;
            var s = TryDeserialize(File.ReadAllText(path));
            EnsureTranscript(s); // chargement explicite d'une session unique (≠ LoadAll)
            return s;
        }

        /// <summary>Charge toutes les sessions, triées par date de mise à jour descendante.</summary>
        public static List<Session> LoadAll()
        {
            var byId = new Dictionary<string, Session>();

            LoadFrom(SessionDir, byId);

            // Import best-effort des sessions de l'ancien outil (sans écraser les nouvelles).
            if (Directory.Exists(LegacyDir))
                LoadFrom(LegacyDir, byId, skipExisting: true);

            var sessions = byId.Values.ToList();
            sessions.Sort((a, b) => b.updatedAt.CompareTo(a.updatedAt));
            return sessions;
        }

        private static void LoadFrom(string dir, Dictionary<string, Session> byId, bool skipExisting = false)
        {
            if (!Directory.Exists(dir)) return;
            foreach (string file in Directory.GetFiles(dir, "*.json"))
            {
                try
                {
                    var session = TryDeserialize(File.ReadAllText(file));
                    if (session == null || string.IsNullOrEmpty(session.id)) continue;
                    if (skipExisting && byId.ContainsKey(session.id)) continue;
                    byId[session.id] = session;
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[ClaudeCodeGUI] Impossible de charger {file}: {ex.Message}");
                }
            }
        }

        private static Session TryDeserialize(string json)
        {
            var s = JsonUtility.FromJson<Session>(json);
            // JsonUtility laisse les listes null si absentes du JSON legacy.
            if (s != null)
            {
                s.contextPaths ??= new List<string>();
                s.exchanges    ??= new List<Exchange>();
                // Sentinelle « non chargé » : le transcript vit dans un sidecar, chargé à la
                // demande via EnsureTranscript (jamais ici, pour garder LoadAll léger).
                s.transcript   = null;
                s.tags         ??= new List<string>();
                s.notes        ??= "";
                s.permissionMode ??= "";
                s.effort       ??= "";
                s.allowedTools ??= "";
            }
            return s;
        }
    }
}
