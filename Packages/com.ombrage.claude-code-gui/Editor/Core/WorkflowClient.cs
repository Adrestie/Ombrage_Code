using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Client du démon d'orchestration agentique (interface locale loopback).
    /// Parle le protocole SSE du workflow : POST /chat (démarre un run), POST /resume
    /// (reprend après checkpoint) et POST /attach (ré-attache après une coupure transport),
    /// chaque réponse étant un flux d'événements JSON numérotés (« data: {...,"seq":n} »).
    /// 100 % local — jamais réseau.
    ///
    /// Les callbacks d'événement sont invoqués depuis un thread d'arrière-plan : l'appelant
    /// (la fenêtre) les remet sur le thread principal via son MainThreadDispatcher. Les
    /// erreurs de transport NE sont PAS avalées : elles remontent via l'exception de la Task,
    /// pour que l'appelant puisse tenter une ré-attache (voir DriveWorkflowAsync).
    /// </summary>
    public sealed class WorkflowClient
    {
        // Timeout infini : un run peut durer plusieurs minutes (streaming continu).
        private static readonly HttpClient Http = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };

        private readonly string _baseUrl;

        public WorkflowClient(string baseUrl)
        {
            _baseUrl = string.IsNullOrWhiteSpace(baseUrl)
                ? "http://127.0.0.1:8787"
                : baseUrl.Trim().TrimEnd('/');
        }

        /// <summary>Vérifie que le démon répond (GET /health).</summary>
        public async Task<JObject> HealthAsync(CancellationToken ct)
        {
            using (var resp = await Http.GetAsync(_baseUrl + "/health", ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                var json = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
                return JObject.Parse(json);
            }
        }

        /// <summary>Demande l'arrêt du démon (POST /shutdown), avant relance par l'outil.
        /// Renvoie false si le démon ne répond pas (déjà mort) ou ne connaît pas la route
        /// (démon d'ancienne génération) — dans les deux cas l'appelant décide de la suite.</summary>
        public async Task<bool> ShutdownAsync(CancellationToken ct)
        {
            try
            {
                using (var resp = await Http.PostAsync(_baseUrl + "/shutdown",
                    new StringContent("{}", Encoding.UTF8, "application/json"), ct).ConfigureAwait(false))
                    return resp.IsSuccessStatusCode;
            }
            catch { return false; } // démon déjà mort, ou connexion coupée pendant l'arrêt
        }

        /// <summary>Démarre un run. <paramref name="effort"/> = profil d'effort
        /// (simple | moyen | complexe | tres_complexe). <paramref name="memory"/> = chemins
        /// MEMORY.md de l'outil (global + session) que les agents utilisent comme mémoire.
        /// Le premier événement porte le run_id.</summary>
        public Task StartAsync(string text, string effort, JObject memory, Action<JObject> onEvent, CancellationToken ct)
        {
            var obj = new JObject { ["text"] = text ?? "" };
            if (!string.IsNullOrEmpty(effort)) obj["effort"] = effort;
            if (memory != null) obj["memory"] = memory;
            return StreamAsync("/chat", obj.ToString(Newtonsoft.Json.Formatting.None), onEvent, ct);
        }

        /// <summary>Reprend un run après un checkpoint. decision : objet JSON
        /// ({reponses:[...]}, {action:"approuver"|"ajuster"|"sans_doc", commentaire?}).
        /// <paramref name="afterSeq"/> = dernier seq déjà reçu (évite de rejouer l'étape précédente).</summary>
        public Task ResumeAsync(string runId, JObject decision, int afterSeq, Action<JObject> onEvent, CancellationToken ct)
        {
            var body = new JObject
            {
                ["run_id"] = runId,
                ["decision"] = decision ?? new JObject(),
                ["after_seq"] = afterSeq,
            }.ToString(Newtonsoft.Json.Formatting.None);
            return StreamAsync("/resume", body, onEvent, ct);
        }

        /// <summary>Ré-attache à un run en cours après une coupure transport. Le démon rejoue
        /// les événements de seq &gt; <paramref name="afterSeq"/> puis suit le direct.</summary>
        public Task AttachAsync(string runId, int afterSeq, Action<JObject> onEvent, CancellationToken ct)
        {
            var body = new JObject { ["run_id"] = runId, ["after_seq"] = afterSeq }
                .ToString(Newtonsoft.Json.Formatting.None);
            return StreamAsync("/attach", body, onEvent, ct);
        }

        /// <summary>Reprend un run INTERROMPU (erreur / coupure définitive / démon redémarré)
        /// depuis sa phase courante, sans décision. L'état est rechargé depuis le disque du démon.
        /// <paramref name="afterSeq"/> n'est utilisé que si le run tournait encore (ré-attache) :
        /// évite de re-rendre ce qui a déjà été reçu ; ignoré pour une vraie reprise (nouveaux seq).</summary>
        public Task RetryAsync(string runId, int afterSeq, Action<JObject> onEvent, CancellationToken ct)
        {
            var body = new JObject { ["run_id"] = runId, ["after_seq"] = afterSeq }
                .ToString(Newtonsoft.Json.Formatting.None);
            return StreamAsync("/retry", body, onEvent, ct);
        }

        // Lit le flux SSE. Renvoie normalement à la fin propre du flux (« event: done » puis EOF).
        // Une annulation volontaire est avalée ; toute autre erreur (coupure transport) REMONTE
        // pour permettre une ré-attache côté appelant.
        private async Task StreamAsync(string path, string body, Action<JObject> onEvent, CancellationToken ct)
        {
            try
            {
                using (var req = new HttpRequestMessage(HttpMethod.Post, _baseUrl + path))
                {
                    req.Content = new StringContent(body, Encoding.UTF8, "application/json");
                    using (var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
                    {
                        resp.EnsureSuccessStatusCode();
                        using (var stream = await resp.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var reader = new StreamReader(stream, Encoding.UTF8))
                        {
                            string line;
                            while ((line = await reader.ReadLineAsync().ConfigureAwait(false)) != null)
                            {
                                if (ct.IsCancellationRequested) break;
                                if (!line.StartsWith("data:", StringComparison.Ordinal)) continue;

                                var payload = line.Substring(5).Trim();
                                if (payload.Length == 0 || payload == "{}") continue;

                                JObject ev;
                                try { ev = JObject.Parse(payload); }
                                catch { continue; }
                                onEvent?.Invoke(ev);
                            }
                        }
                    }
                }
            }
            catch (OperationCanceledException) { /* arrêt volontaire : pas une erreur */ }
        }
    }
}
