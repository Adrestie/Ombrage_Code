using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Mode « Workflow » : pilote le démon d'orchestration agentique (pipeline
    /// multi-agents) et mappe son flux SSE vers les fabriques de blocs existantes
    /// (mêmes diffs, Markdown, persistance que le mode CLI). Additif : le chemin
    /// CLI n'est pas modifié.
    ///
    /// Persistance : chaque bloc est Record() dans _log et reconstruit par
    /// RebuildFromLog (kinds wf_fold / wf_checkpoint en plus de tool/result/text/system).
    /// _wfRunId est sérialisé : si une recompilation survient pendant l'attente d'une
    /// décision, la carte de checkpoint est restaurée et la reprise (/resume) reste
    /// possible automatiquement.
    /// </summary>
    public partial class ClaudeCodeWindow
    {
        [SerializeField] private string _wfRunId; // sérialisé : survit au domain reload
        [SerializeField] private int    _wfLastSeq = -1; // dernier seq reçu (pour la ré-attache)

        private const int MAX_WF_RECONNECT = 5; // tentatives de ré-attache avant abandon

        private WorkflowClient _wfClient;
        private CancellationTokenSource _wfCts;
        private double _wfCost;
        private bool   _wfAwaitingCheckpoint;
        private bool   _wfDone;
        private bool   _wfErrored;   // le run s'est terminé sur une erreur (reprise possible)
        private bool   _wfSpawnFailure;   // le démon n'a pas pu LANCER un agent (env défaillant)
        private string _wfRepairedRunId;  // garde : une seule auto-réparation par run
        private string _wfCurAgent;
        private Label  _wfTextLabel;  private ChatEntry _wfTextEntry;
        private Label  _wfThinkLabel; private ChatEntry _wfThinkEntry;
        private System.Diagnostics.Process _wfDaemonProcess; // démon lancé par l'outil (le cas échéant)

        // ---- Cycle de vie d'un run workflow ----

        private void StartWorkflow(string prompt)
        {
            if (_activeSession == null) CreateNewSession();
            BeginRun(); // lie ce run workflow à la session affichée AVANT d'écrire le 1er bloc

            PushPromptToHistory(prompt);
            AddPromptBlock(prompt);
            SetPrompt("");

            _wfRunId = null; _wfLastSeq = -1; _wfCost = 0; _wfAwaitingCheckpoint = false; _wfDone = false; _wfErrored = false;
            _wfSpawnFailure = false;
            _filesEdited = false;
            _wfCurAgent = null; _wfTextLabel = null; _wfTextEntry = null; _wfThinkLabel = null; _wfThinkEntry = null;

            _wfClient = new WorkflowClient(_workflowUrl);
            _wfCts?.Cancel();
            _wfCts = new CancellationTokenSource();

            _isRunning = true;
            _runStartTime = EditorApplication.timeSinceStartup;
            _thinkingTokens = 0;
            LockReload();
            AddSystemBlock($"▶ Workflow agentique — {_workflowUrl}");

            var ct = _wfCts.Token;
            string p = prompt, eff = _workflowEffort;
            var mem = BuildWorkflowMemory(_runSession);
            // Nouveau run : rien à perdre côté démon → on vérifie aussi que le démon en place
            // n'est pas périmé (code plus récent que le processus), et on le redémarre si besoin.
            _ = DriveWorkflowAsync(c => _wfClient.StartAsync(p, eff, mem, OnWfEvent, c), ct, checkFreshness: true);
        }

        // Construit l'objet « memory » transmis au démon : les agents du workflow s'appuient sur
        // les MÊMES fichiers MEMORY.md que le mode CLI (global projet + mémoire de la session).
        private JObject BuildWorkflowMemory(Session s)
        {
            if (s == null) return null;
            // Garantit que les DEUX mémoires existent : la globale, et celle de la session (créée
            // si nouvelle). Le système a ainsi toujours ses deux mémoires, lisibles dès le départ.
            return new JObject
            {
                ["global_path"] = SessionStore.EnsureGlobalMemory(),
                ["session_path"] = SessionStore.EnsureSessionMemory(s.id),
                ["session_id"] = s.id,
                ["session_title"] = s.GetDisplayTitle(),
            };
        }

        private void ResumeWorkflow(JObject decision)
        {
            if (string.IsNullOrEmpty(_wfRunId)) return;
            if (_wfClient == null) _wfClient = new WorkflowClient(_workflowUrl);

            // Après un rechargement de domaine, _runSession est perdu (non sérialisé) : on relie
            // la reprise à la session affichée, qui détient le transcript du checkpoint restauré.
            if (_runSession == null) BeginRun();

            _wfAwaitingCheckpoint = false; _wfDone = false; _wfErrored = false; _wfSpawnFailure = false;
            _wfCurAgent = null; _wfTextLabel = null; _wfTextEntry = null; _wfThinkLabel = null; _wfThinkEntry = null;

            _wfCts?.Cancel();
            _wfCts = new CancellationTokenSource();

            _isRunning = true;
            _runStartTime = EditorApplication.timeSinceStartup;
            LockReload();

            var ct = _wfCts.Token;
            string rid = _wfRunId; int after = _wfLastSeq; JObject dec = decision;
            _ = DriveWorkflowAsync(c => _wfClient.ResumeAsync(rid, dec, after, OnWfEvent, c), ct);
        }

        // « Reprendre le run » : re-pilote un run INTERROMPU depuis sa phase courante, sans
        // décision. L'état (phase, brief, plan, implémentation…) est rechargé côté démon : la
        // dernière phase atteinte est relancée, les acquis des phases terminées sont conservés.
        private void RetryWorkflow()
        {
            if (string.IsNullOrEmpty(_wfRunId)) return;
            if (_isRunning)
            {
                // Un run est déjà en cours (éventuellement en arrière-plan sur une autre session).
                var note = new Label("⚠ Un run est déjà en cours. Arrêtez-le (■ Stop) avant d'en reprendre un autre.");
                note.AddToClassList("cc-system");
                _displayRoot.Add(note);
                ScrollToBottom();
                return;
            }
            if (_wfClient == null) _wfClient = new WorkflowClient(_workflowUrl);

            // Après fermeture/recompilation, _runSession est perdu : on relie la reprise à la
            // session affichée (qui détient le transcript du run interrompu).
            if (_runSession == null) BeginRun();

            _wfAwaitingCheckpoint = false; _wfDone = false; _wfErrored = false; _wfSpawnFailure = false;
            _wfCurAgent = null; _wfTextLabel = null; _wfTextEntry = null; _wfThinkLabel = null; _wfThinkEntry = null;

            _wfCts?.Cancel();
            _wfCts = new CancellationTokenSource();

            _isRunning = true;
            _runStartTime = EditorApplication.timeSinceStartup;
            LockReload();
            AddSystemBlock("↻ Reprise du run interrompu…");

            var ct = _wfCts.Token;
            string rid = _wfRunId; int after = _wfLastSeq;
            _ = DriveWorkflowAsync(c => _wfClient.RetryAsync(rid, after, OnWfEvent, c), ct);
        }

        // Callback d'événement SSE (thread d'arrière-plan → remis sur le thread principal).
        private void OnWfEvent(JObject ev) => _dispatcher.Enqueue(() => HandleWorkflowEvent(ev));

        // Pilote un run workflow avec ré-attache automatique en cas de coupure transport.
        // L'« étape » (start/resume) renvoie normalement à sa fin propre (event: done) ou lève
        // sur coupure → on tente /attach (depuis _wfLastSeq) avec backoff, tant que le run n'est
        // pas terminé. Le run continue côté démon (tâche de fond) même pendant la déconnexion.
        private async Task DriveWorkflowAsync(Func<CancellationToken, Task> firstLeg, CancellationToken ct, bool checkFreshness = false)
        {
            // checkFreshness : uniquement pour un NOUVEAU run (rien à perdre côté démon). Jamais
            // sur resume/retry/ré-attache : redémarrer le démon y détruirait le canal du run.
            if (!await EnsureDaemonReady(ct, checkFreshness)) return; // erreurs affichées par EnsureDaemonReady

            Func<CancellationToken, Task> leg = firstLeg;
            int attempt = 0;
            while (true)
            {
                try
                {
                    await leg(ct);
                }
                catch (OperationCanceledException) { return; }
                catch (Exception ex)
                {
                    // Run déjà terminé/au checkpoint, ou arrêt volontaire : pas de ré-attache.
                    if (ct.IsCancellationRequested || _wfDone || _wfAwaitingCheckpoint
                        || string.IsNullOrEmpty(_wfRunId))
                    {
                        _dispatcher.Enqueue(() => OnWorkflowError(ex));
                        return;
                    }
                    attempt++;
                    if (attempt > MAX_WF_RECONNECT)
                    {
                        _dispatcher.Enqueue(() => OnWorkflowError(new Exception(
                            $"Connexion au démon perdue et ré-attache échouée après {MAX_WF_RECONNECT} tentatives. "
                            + "Dernière erreur : " + ex.Message)));
                        return;
                    }
                    int delay = attempt == 1 ? 2 : attempt == 2 ? 5 : 10;
                    _dispatcher.Enqueue(() => AddNetworkBlock(
                        $"↻ Connexion au démon perdue — ré-attache {attempt}/{MAX_WF_RECONNECT} dans {delay}s… (le run continue côté démon)"));
                    try { await Task.Delay(delay * 1000, ct); } catch { return; }
                    if (ct.IsCancellationRequested) return;
                    if (!await EnsureDaemonReady(ct)) return;

                    string rid = _wfRunId; int after = _wfLastSeq;
                    leg = c => _wfClient.AttachAsync(rid, after, OnWfEvent, c);
                    continue;
                }
                break; // étape terminée proprement
            }
            _dispatcher.Enqueue(OnWorkflowStreamEnd);
        }

        // S'assure que le démon est joignable (le lance si besoin). Renvoie false (et affiche
        // l'erreur) si indisponible. checkFreshness : si le démon répond mais que son code sur
        // disque a changé depuis son lancement (démon périmé — il survit volontairement aux
        // fermetures d'Unity), il est arrêté (/shutdown) et relancé automatiquement.
        private async Task<bool> EnsureDaemonReady(CancellationToken ct, bool checkFreshness = false)
        {
            if (await IsDaemonReachable(ct))
            {
                if (!checkFreshness || !_workflowAutostart) return true;
                if (!await DaemonIsStale(ct)) return true;
                _dispatcher.Enqueue(() => AddSystemBlock("♻ Le code du démon a changé depuis son lancement — redémarrage automatique…"));
                if (await RestartDaemonAsync(ct))
                {
                    _dispatcher.Enqueue(() => AddSystemBlock("✓ Démon relancé sur le code à jour."));
                    return true;
                }
                // Redémarrage impossible (ex. démon d'ancienne génération sans /shutdown) : on
                // poursuit avec le démon en place plutôt que de bloquer le run.
                _dispatcher.Enqueue(() => AddSystemBlock("⚠ Redémarrage automatique impossible — poursuite avec le démon en place."));
                return await IsDaemonReachable(ct);
            }

            if (!_workflowAutostart)
            {
                _dispatcher.Enqueue(() => OnWorkflowError(new Exception(
                    "Démon non joignable et démarrage automatique désactivé. " +
                    "Lancez le démon ou cochez « Démarrer le démon automatiquement ».")));
                return false;
            }

            _dispatcher.Enqueue(() => AddSystemBlock("⏳ Démon workflow non joignable — démarrage automatique…"));
            if (!TryLaunchDaemon(out string err))
            {
                _dispatcher.Enqueue(() => OnWorkflowError(new Exception(err)));
                return false;
            }
            if (!await WaitForDaemon(ct, 25))
            {
                _dispatcher.Enqueue(() => OnWorkflowError(new Exception(
                    "Le démon a été lancé mais n'a pas répondu (timeout). " +
                    "Vérifiez les chemins Python / run.py dans Settings.")));
                return false;
            }
            _dispatcher.Enqueue(() => AddSystemBlock("✓ Démon prêt."));
            return true;
        }

        private async Task<bool> IsDaemonReachable(CancellationToken ct)
        {
            try { await new WorkflowClient(_workflowUrl).HealthAsync(ct); return true; }
            catch { return false; }
        }

        // Démon « périmé » : son processus a démarré AVANT la dernière modification du code du
        // démon sur disque. Un /health sans started_at (démon d'ancienne génération) est réputé
        // périmé. En cas de doute (health illisible), on ne redémarre pas.
        private async Task<bool> DaemonIsStale(CancellationToken ct)
        {
            try
            {
                var health = await new WorkflowClient(_workflowUrl).HealthAsync(ct);
                var sa = health["started_at"];
                if (sa == null || sa.Type == JTokenType.Null) return true;
                var started = DateTimeOffset.FromUnixTimeMilliseconds((long)((double)sa * 1000));
                // Marge de 2 s : évite un redémarrage sur simple arrondi d'horloge.
                return DaemonCodeLastWriteUtc() > started.AddSeconds(2);
            }
            catch { return false; }
        }

        // Dernière modification (UTC) du code du démon : run.py + core/*.py + prompts/*.md,
        // localisés à partir du chemin de run.py renseigné dans Settings.
        private DateTimeOffset DaemonCodeLastWriteUtc()
        {
            var newest = DateTimeOffset.MinValue;
            void Consider(string f)
            {
                try
                {
                    var t = new DateTimeOffset(File.GetLastWriteTimeUtc(f), TimeSpan.Zero);
                    if (t > newest) newest = t;
                }
                catch { /* fichier illisible : ignoré */ }
            }

            string script = (_workflowScript ?? "").Trim();
            if (string.IsNullOrEmpty(script) || !File.Exists(script)) return newest;
            Consider(script);
            string root = Path.GetDirectoryName(script);
            foreach (var (sub, pattern) in new[] { ("core", "*.py"), ("prompts", "*.md") })
            {
                string dir = Path.Combine(root, sub);
                if (!Directory.Exists(dir)) continue;
                try { foreach (var f in Directory.GetFiles(dir, pattern)) Consider(f); }
                catch { /* dossier illisible : ignoré */ }
            }
            return newest;
        }

        // Arrête le démon en place (/shutdown) puis le relance et attend qu'il réponde.
        private async Task<bool> RestartDaemonAsync(CancellationToken ct)
        {
            var client = new WorkflowClient(_workflowUrl);
            await client.ShutdownAsync(ct);
            // Attend la libération du port (≤ 5 s) ; un démon encore joignable après ce délai
            // ne connaît probablement pas /shutdown → on renonce (l'appelant décide).
            for (int i = 0; i < 10 && await IsDaemonReachable(ct); i++)
            {
                try { await Task.Delay(500, ct); } catch { return false; }
            }
            if (await IsDaemonReachable(ct)) return false;
            if (!TryLaunchDaemon(out _)) return false;
            return await WaitForDaemon(ct, 25);
        }

        // Auto-réparation : le démon répond mais n'arrive plus à LANCER ses agents
        // (CLINotFoundError = échec de spawn du CLI : environnement de lancement défaillant).
        // On le redémarre avec un environnement sain puis on reprend le run (/retry) — sans
        // intervention de l'utilisateur. Garde : une seule tentative par run (_wfRepairedRunId).
        private void RepairDaemonAndRetry()
        {
            AddSystemBlock("♻ Le démon ne parvient plus à lancer ses agents — redémarrage automatique puis reprise du run…");
            _ = Task.Run(async () =>
            {
                bool ok = await RestartDaemonAsync(CancellationToken.None);
                _dispatcher.Enqueue(() =>
                {
                    if (ok)
                    {
                        AddSystemBlock("✓ Démon relancé — reprise du run.");
                        RetryWorkflow();
                    }
                    else
                    {
                        AddErrorBlock("Workflow",
                            "Redémarrage automatique du démon impossible (chemins Python/run.py dans Settings ?). " +
                            "Le run peut être repris via le bandeau « Reprendre » une fois le démon relancé.");
                        EndRunAndOfferResume();
                        RefreshSessionList();
                    }
                });
            });
        }

        private async Task<bool> WaitForDaemon(CancellationToken ct, int maxSeconds)
        {
            var client = new WorkflowClient(_workflowUrl);
            for (int i = 0; i < maxSeconds * 2; i++)
            {
                if (ct.IsCancellationRequested) return false;
                try { await client.HealthAsync(ct); return true; }
                catch { /* pas encore prêt */ }
                try { await Task.Delay(500, ct); } catch { return false; }
            }
            return false;
        }

        private bool TryLaunchDaemon(out string error)
        {
            error = null;
            string py = (_workflowPython ?? "").Trim();
            string script = (_workflowScript ?? "").Trim();

            if (string.IsNullOrEmpty(py) || !File.Exists(py))
            {
                error = "Exécutable Python du démon introuvable. Renseignez Settings → " +
                        "« Démon : Python (.venv) » (ex. …\\agentic-workflow\\.venv\\Scripts\\python.exe).";
                return false;
            }
            if (string.IsNullOrEmpty(script) || !File.Exists(script))
            {
                error = "Script run.py introuvable. Renseignez Settings → « Démon : script run.py ».";
                return false;
            }

            try
            {
                string projectRoot = Path.GetDirectoryName(Application.dataPath);
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = py,
                    Arguments = "\"" + script + "\"",
                    WorkingDirectory = Path.GetDirectoryName(script),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                psi.EnvironmentVariables["AGENTIC_WORKSPACE"] = projectRoot;
                try
                {
                    var uri = new Uri(_workflowUrl);
                    if (uri.Port > 0) psi.EnvironmentVariables["AGENTIC_PORT"] = uri.Port.ToString();
                }
                catch { /* URL sans port : on garde le port par défaut du démon */ }

                var p = new System.Diagnostics.Process { StartInfo = psi };
                p.OutputDataReceived += (s, e) =>
                {
                    if (_debugEvents && e.Data != null) UnityEngine.Debug.Log("[workflow-daemon] " + e.Data);
                };
                p.ErrorDataReceived += (s, e) =>
                {
                    if (_debugEvents && e.Data != null) UnityEngine.Debug.LogWarning("[workflow-daemon] " + e.Data);
                };
                p.Start();
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                _wfDaemonProcess = p;
                return true;
            }
            catch (Exception ex)
            {
                error = "Échec du lancement du démon : " + ex.Message;
                return false;
            }
        }

        private void StopWorkflow()
        {
            _wfCts?.Cancel();
            _wfAwaitingCheckpoint = false;
            _isRunning = false;
            UnlockReload();
            EndRun(); // run workflow arrêté : on délie (grave le transcript, rebascule l'affichage)
        }

        private void OnWorkflowError(Exception ex)
        {
            AddErrorBlock("Erreur workflow",
                ex.Message + "\n\nVérifiez que le démon tourne (python run.py, AGENTIC_WORKSPACE = ce projet) " +
                "et l'URL dans Settings → URL du démon workflow.");
            _isRunning = false;
            UnlockReload();
            EndRunAndOfferResume();
        }

        // Délie le run arrêté/échoué puis, si la session du run est affichée, montre le bandeau
        // « Reprendre ». Le pointeur (wfRunId/wfLastSeq) reste persisté sur la session → le bandeau
        // est de toute façon régénéré à la prochaine ouverture/affichage de cette session.
        private void EndRunAndOfferResume()
        {
            var s = _runSession;
            EndRun();
            if (s != null && !string.IsNullOrEmpty(s.wfRunId) && _activeSession?.id == s.id)
                ShowResumeBanner(s);
        }

        private void OnWorkflowStreamEnd()
        {
            _isRunning = false;
            UnlockReload();

            if (_wfAwaitingCheckpoint)
            {
                // En pause sur une décision : le run reste lié à sa session (reprise possible),
                // mais on grave le transcript pour ne rien perdre en attendant la décision.
                FlushTranscript(_runSession);
                return;
            }

            // Fin sur erreur (event "error" du démon, leg terminé proprement) : proposer la reprise.
            if (_wfErrored && !_wfDone)
            {
                // Échec de SPAWN d'agent : le démon est vivant mais son environnement ne permet
                // plus de lancer le CLI. Auto-réparation (une fois par run) : redémarrer le démon
                // puis reprendre le run — l'utilisateur n'a rien à faire.
                if (_wfSpawnFailure && _workflowAutostart
                    && !string.IsNullOrEmpty(_wfRunId) && _wfRepairedRunId != _wfRunId)
                {
                    _wfSpawnFailure = false;
                    _wfRepairedRunId = _wfRunId;
                    RepairDaemonAndRetry(); // le run reste lié à sa session (pas de EndRun)
                    return;
                }
                EndRunAndOfferResume();
                RefreshSessionList();
                return;
            }

            if (_wfDone)
            {
                if (_wfCost > 0) AddSystemBlock($"💰 Coût cumulé du run : ${_wfCost:0.000}");
                AddDoneBlock();

                // Échange synthétique pour l'historique de la session du run (titre, --resume futur).
                var done = _runSession ?? _activeSession;
                if (done != null && string.IsNullOrEmpty(done.title) && done.exchanges.Count == 0)
                {
                    var firstPrompt = _promptHistory.Count > 0 ? _promptHistory[_promptHistory.Count - 1] : "";
                    if (!string.IsNullOrEmpty(firstPrompt))
                        done.title = firstPrompt.Length > 60 ? firstPrompt[..60] + "…" : firstPrompt;
                }

                if (_filesEdited)
                {
                    _filesEdited = false;
                    AddSystemBlock("↻ Recompilation Unity…");
                    AssetDatabase.Refresh();
                }
                ClearRunPointer(); // run terminé : plus rien à reprendre
            }

            EndRun();             // run workflow fini : grave + rebascule l'affichage
            RefreshSessionList();
        }

        // Vide le pointeur de run non terminé (et le grave) sur la session du run.
        private void ClearRunPointer()
        {
            var s = _runSession ?? _activeSession;
            if (s == null || string.IsNullOrEmpty(s.wfRunId)) return;
            s.wfRunId = null;
            s.wfLastSeq = -1;
            if (s.transcript != null) SessionStore.SaveTranscript(s);
        }

        // ---- Mapping des événements du pipeline ----

        private void HandleWorkflowEvent(JObject ev)
        {
            // Suit le dernier seq reçu (pour une éventuelle ré-attache). Pas de dédup cliente :
            // le démon filtre déjà par after_seq, et dédupliquer ici masquerait des events après
            // un redémarrage du démon (où le seq peut être réémis).
            var seqTok = ev["seq"];
            if (seqTok != null && seqTok.Type == JTokenType.Integer)
            {
                int seq = (int)seqTok;
                if (seq > _wfLastSeq) _wfLastSeq = seq;
                if (_runSession != null) _runSession.wfLastSeq = _wfLastSeq; // persisté (sidecar)
            }

            switch ((string)ev["type"])
            {
                case "run":
                    _wfRunId = (string)ev["run_id"] ?? _wfRunId;
                    // Pointeur persistant : permet de proposer la reprise après fermeture/réouverture.
                    if (_runSession != null && !string.IsNullOrEmpty(_wfRunId))
                    {
                        _runSession.wfRunId = _wfRunId;
                        _runSession.wfLastSeq = _wfLastSeq;
                        SessionStore.SaveTranscript(_runSession);
                    }
                    break;

                case "phase":
                    _wfCurAgent = null;
                    _wfTextLabel = null; _wfTextEntry = null; _wfThinkLabel = null; _wfThinkEntry = null;
                    AddSystemBlock($"══ Phase {(int?)ev["phase"]} — {(string)ev["label"]}");
                    break;

                case "phase_done":
                    break;

                case "agent_start":
                    _wfCurAgent = (string)ev["agent"];
                    _wfTextLabel = null; _wfTextEntry = null; _wfThinkLabel = null; _wfThinkEntry = null;
                    AddSystemBlock($"▶ {_wfCurAgent} ({(string)ev["role"]})");
                    break;

                case "text":
                    EnsureWfText((string)ev["agent"]);
                    _wfTextLabel.text += (string)ev["content"];
                    if (_wfTextEntry != null) _wfTextEntry.content = _wfTextLabel.text;
                    ScrollToBottom();
                    break;

                case "thinking":
                    EnsureWfThink((string)ev["agent"]);
                    _wfThinkLabel.text += ((string)ev["content"] ?? "").Replace("\r", "");
                    if (_wfThinkEntry != null) _wfThinkEntry.content = _wfThinkLabel.text;
                    ScrollToBottom();
                    break;

                case "tool_use":
                    AddWfTool((string)ev["tool"], ev["input"]);
                    break;

                case "tool_result":
                    AddWfResult((bool?)ev["is_error"] ?? false, (string)ev["content"] ?? "");
                    break;

                case "usage":
                {
                    var cost = ev["cost_usd"];
                    if (cost != null && cost.Type != JTokenType.Null) _wfCost += cost.Value<double>();
                    break;
                }

                case "brief":
                    AddWfMarkdownFold("🧠 Brief", BriefToMarkdown(ev["content"] as JObject), false);
                    break;

                case "verdict":
                    AddSystemBlock($"   Validation : {(string)(ev["content"]?["verdict"]) ?? "?"}");
                    break;

                case "contexte":
                {
                    int files = (ev["exploration"]?["fichiers_pertinents"] as JArray)?.Count ?? 0;
                    int refs  = (ev["sources"]?["references"] as JArray)?.Count ?? 0;
                    AddSystemBlock($"   Contexte : {files} fichier(s), {refs} référence(s)");
                    break;
                }

                case "plan":
                    AddWfMarkdownFold($"📋 Plan v{(int?)ev["version"]}", PlanToMarkdown(ev["content"] as JObject), true);
                    break;

                case "critiques":
                    AddSystemBlock("   Critiques : " + SummarizeCritiques(ev["content"] as JObject));
                    break;

                case "implementation":
                    AddSystemBlock("   Implémentation : " + SummarizeFiles(ev["content"]?["fichiers_modifies"] as JArray));
                    break;

                case "revue":
                {
                    var rc = ev["content"];
                    string verdict = (string)(rc?["verdict"]) ?? "?";
                    var tests = rc?["tests"];
                    string ts = tests != null
                        ? $" — tests.succes={(bool?)tests["succes"]} ({(string)tests["commande"]})"
                        : "";
                    AddSystemBlock($"   Revue : {verdict}{ts}");
                    break;
                }

                case "documentation":
                    AddSystemBlock("   Documentation : " + SummarizeFiles(ev["content"]?["fichiers_doc"] as JArray));
                    break;

                case "synthese":
                {
                    _wfDone = true;
                    var box = AddTextBlock(SyntheseToMarkdown(ev["content"] as JObject), out _);
                    FinalizeTextMarkdown(box);
                    break;
                }

                case "checkpoint":
                {
                    _wfAwaitingCheckpoint = true;
                    var cpEntry = Record(new ChatEntry
                    {
                        kind = "wf_checkpoint",
                        dataJson = ev.ToString(Newtonsoft.Json.Formatting.None),
                    });
                    BuildWorkflowCheckpointCard(cpEntry);
                    break;
                }

                case "info":
                    AddSystemBlock("ℹ " + (string)ev["message"]);
                    break;

                case "error":
                    // resumable:false (run inconnu / déjà terminé / en attente d'un checkpoint) :
                    // erreur définitive → on ne propose pas de reprise et on vide le pointeur.
                    if ((bool?)ev["resumable"] == false)
                    {
                        ClearRunPointer();
                        AddErrorBlock("Workflow", (string)ev["message"]);
                    }
                    else
                    {
                        _wfErrored = true; // OnWorkflowStreamEnd proposera la reprise
                        // CLINotFoundError = le démon n'a pas pu LANCER l'agent (spawn du CLI
                        // impossible : environnement du processus démon défaillant) → candidat à
                        // l'auto-réparation (redémarrage du démon + /retry) en fin de flux.
                        string emsg = (string)ev["message"] ?? "";
                        if (emsg.Contains("CLINotFoundError")) _wfSpawnFailure = true;
                        AddErrorBlock("Erreur agent", emsg);
                    }
                    break;
            }
        }

        // ---- Fabriques de blocs workflow (créent ET persistent) ----

        // Foldout générique réutilisé par RebuildFromLog (kind wf_fold).
        private Foldout WfFold(string header, string cssClass, bool expanded, bool markdown, string content, out Label lbl)
        {
            lbl = null;
            string cls = string.IsNullOrEmpty(cssClass) ? "cc-tool__content" : cssClass;
            var fold = AddFoldoutBlock(header, cls, expanded, out var defaultLbl);
            if (markdown)
            {
                fold.Clear();
                var box = new VisualElement();
                MarkdownRenderer.Render(box, string.IsNullOrEmpty(content) ? "_(vide)_" : content);
                fold.Add(box);
            }
            else
            {
                defaultLbl.text = content ?? "";
                lbl = defaultLbl;
            }
            return fold;
        }

        private void EnsureWfText(string agent)
        {
            if (_wfTextLabel != null) return;
            _wfTextEntry = Record(new ChatEntry { kind = "wf_fold", header = $"🗎 {agent} — sortie", cssClass = "cc-tool__content", content = "" });
            WfFold(_wfTextEntry.header, _wfTextEntry.cssClass, false, false, "", out _wfTextLabel);
        }

        private void EnsureWfThink(string agent)
        {
            if (_wfThinkLabel != null) return;
            _wfThinkEntry = Record(new ChatEntry { kind = "wf_fold", header = $"💭 {agent} — réflexion", cssClass = "cc-thinking", content = "" });
            WfFold(_wfThinkEntry.header, _wfThinkEntry.cssClass, false, false, "", out _wfThinkLabel);
        }

        private void AddWfMarkdownFold(string header, string md, bool expanded)
        {
            Record(new ChatEntry { kind = "wf_fold", header = header, cssClass = "cc-tool__content", expanded = expanded, markdown = true, content = md ?? "" });
            WfFold(header, "cc-tool__content", expanded, true, md, out _);
        }

        private void AddWfTool(string name, JToken input)
        {
            string desc = ToolDescriptions.BuildDescription(name, input);
            string display = ToolDescriptions.BuildInputDisplay(name, input);
            bool expand = name is "Edit" or "MultiEdit" or "Write";
            var fold = AddFoldoutBlock($"⚙ {desc}", "cc-tool__content", expand, out _);
            Record(new ChatEntry
            {
                kind = "tool",
                header = desc,
                content = display ?? "",
                toolName = name,
                inputJson = input?.ToString(Newtonsoft.Json.Formatting.None) ?? "",
                expanded = expand,
            });
            PopulateToolFoldout(fold, name, input, desc, display);
        }

        private void AddWfResult(bool isErr, string outp)
        {
            if (!string.IsNullOrEmpty(outp) && outp.Length > 2000) outp = outp.Substring(0, 2000) + "\n… (tronqué)";
            AddFoldoutBlock(isErr ? "✗ Erreur outil" : "📋 Résultat outil",
                isErr ? "cc-error" : "cc-result__content", isErr, out var c);
            c.text = outp ?? "";
            Record(new ChatEntry { kind = "result", header = isErr ? "✗ Erreur outil" : "📋 Résultat outil", content = outp ?? "", isError = isErr, expanded = isErr });
        }

        // ---- Carte de checkpoint (réutilise le style cc-ask ; persistée via wf_checkpoint) ----

        private void BuildWorkflowCheckpointCard(ChatEntry entry)
        {
            JObject ev;
            try { ev = JObject.Parse(entry.dataJson); }
            catch { return; }

            string kind = (string)ev["kind"];
            var card = new VisualElement();
            card.AddToClassList("cc-ask");

            var title = new Label(CheckpointTitle(kind, (string)ev["agent"]));
            title.AddToClassList("cc-ask__title");
            card.Add(title);

            string msg = (string)ev["message"];
            if (!string.IsNullOrEmpty(msg))
            {
                var d = new Label(msg);
                d.AddToClassList("cc-ask__desc");
                card.Add(d);
            }

            // Matière à décision : ce qui bloque (escalades), les remarques du panel et
            // hypothèses d'arbitrage (approbation du plan), le protocole de test à dérouler
            // (validation). Rendu en Markdown directement DANS la carte — l'utilisateur n'a
            // pas à reconstituer le contexte depuis les blocs précédents du fil.
            string details = CheckpointDetailsMarkdown(kind, ev);
            if (!string.IsNullOrEmpty(details))
            {
                var box = new VisualElement();
                box.style.marginTop = 4;
                box.style.marginBottom = 4;
                MarkdownRenderer.Render(box, details);
                card.Add(box);
            }

            if (kind == "clarification" || kind == "ask_user" || kind == "tri")
            {
                var questions = ev["questions"] as JArray;
                var options   = ev["options"] as JArray;
                var fields = new List<TextField>();
                var optionToggles = new List<List<Toggle>>();

                for (int i = 0; questions != null && i < questions.Count; i++)
                {
                    var block = new VisualElement();
                    block.AddToClassList("cc-ask__q");
                    block.Add(new Label((string)questions[i] ?? "(question)"));

                    var toggles = new List<Toggle>();
                    var opts = (options != null && i < options.Count) ? options[i] as JArray : null;
                    if (opts != null)
                    {
                        foreach (var op in opts)
                        {
                            string opText = (string)op ?? "";
                            var t = new Toggle(opText);
                            t.AddToClassList("cc-ask__option");
                            // Les options sont descriptives (avantages/inconvénients) : on laisse
                            // le libellé passer à la ligne plutôt que de déborder.
                            var lbl = t.Q<Label>();
                            if (lbl != null)
                            {
                                lbl.style.whiteSpace = WhiteSpace.Normal;
                                lbl.style.flexGrow = 1;
                            }
                            var local = toggles;
                            int idx = toggles.Count;
                            t.RegisterValueChangedCallback(e =>
                            {
                                if (!e.newValue) return;
                                for (int k = 0; k < local.Count; k++)
                                    if (k != idx) local[k].SetValueWithoutNotify(false);
                            });
                            // Présélection de l'option recommandée (« (recommandé) … »), sans
                            // notifier : l'utilisateur peut toujours en choisir une autre.
                            if (opText.IndexOf("recommand", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                foreach (var prev in toggles) prev.SetValueWithoutNotify(false);
                                t.SetValueWithoutNotify(true);
                            }
                            toggles.Add(t);
                            block.Add(t);
                        }
                    }
                    optionToggles.Add(toggles);

                    var custom = new TextField("Réponse libre");
                    custom.AddToClassList("cc-ask__custom");
                    fields.Add(custom);
                    block.Add(custom);
                    card.Add(block);
                }

                var actions = new VisualElement();
                actions.AddToClassList("cc-ask__actions");
                var submit = new Button { text = "Répondre" };
                submit.clicked += () =>
                {
                    var reponses = new JArray();
                    for (int i = 0; questions != null && i < questions.Count; i++)
                    {
                        string custom = (fields[i].value ?? "").Trim();
                        string val = custom;
                        if (string.IsNullOrEmpty(val))
                        {
                            var picked = new List<string>();
                            var tg = optionToggles[i];
                            var opts = (options != null && i < options.Count) ? options[i] as JArray : null;
                            for (int k = 0; k < tg.Count && opts != null && k < opts.Count; k++)
                                if (tg[k].value) picked.Add((string)opts[k]);
                            val = picked.Count > 0 ? string.Join(", ", picked) : "(aucune réponse)";
                        }
                        reponses.Add(val);
                    }
                    entry.answered = true;
                    DisableCard(submit, title, "répondu");
                    ResumeWorkflow(new JObject { ["reponses"] = reponses });
                };
                actions.Add(submit);
                card.Add(actions);
            }
            else if (kind == "approbation_plan" || kind == "escalade" || kind == "escalade_revue")
            {
                var actions = new VisualElement();
                actions.AddToClassList("cc-ask__actions");
                var approve = new Button { text = "✓ Approuver" };
                var adjust  = new Button { text = "✗ Ajuster…" };
                var field = new TextField("Commentaire d'ajustement");
                field.AddToClassList("cc-ask__custom");
                field.style.display = DisplayStyle.None;
                bool fieldShown = false;

                approve.clicked += () =>
                {
                    entry.answered = true;
                    DisableCard(approve, title, "approuvé");
                    adjust.SetEnabled(false);
                    ResumeWorkflow(new JObject { ["action"] = "approuver" });
                };
                adjust.clicked += () =>
                {
                    if (!fieldShown)
                    {
                        fieldShown = true;
                        field.style.display = DisplayStyle.Flex;
                        field.Focus();
                        return;
                    }
                    string c = (field.value ?? "").Trim();
                    if (string.IsNullOrEmpty(c)) { field.Focus(); return; }
                    entry.answered = true;
                    DisableCard(approve, title, "ajustement demandé");
                    adjust.SetEnabled(false);
                    ResumeWorkflow(new JObject { ["action"] = "ajuster", ["commentaire"] = c });
                };
                actions.Add(approve);
                actions.Add(adjust);
                card.Add(actions);
                card.Add(field);
            }
            else if (kind == "validation_avant_doc")
            {
                var actions = new VisualElement();
                actions.AddToClassList("cc-ask__actions");
                var approve = new Button { text = "✓ Documenter" };
                var skip    = new Button { text = "Sans documentation" };
                approve.clicked += () =>
                {
                    entry.answered = true;
                    DisableCard(approve, title, "validé"); skip.SetEnabled(false);
                    ResumeWorkflow(new JObject { ["action"] = "approuver" });
                };
                skip.clicked += () =>
                {
                    entry.answered = true;
                    DisableCard(approve, title, "sans doc"); skip.SetEnabled(false);
                    ResumeWorkflow(new JObject { ["action"] = "sans_doc" });
                };
                actions.Add(approve);
                actions.Add(skip);
                card.Add(actions);
            }

            _chatContent.Add(card);
            ScrollToBottom();
        }

        private static void DisableCard(Button b, Label title, string suffix)
        {
            b.SetEnabled(false);
            title.text += " — " + suffix;
        }

        // Régénère le bandeau « Reprendre » SI la session affichée porte un run non terminé et
        // qu'aucun run n'est actif dessus. Appelé à l'ouverture de l'outil et au retour sur une
        // session. Le bandeau est TRANSITOIRE (non enregistré dans le transcript) : il est dérivé
        // du pointeur persisté (session.wfRunId) → pas de doublon, et il disparaît dès que le run
        // est repris-et-terminé (le pointeur est alors vidé).
        private void MaybeShowResumeBanner(Session s)
        {
            if (s == null || string.IsNullOrEmpty(s.wfRunId)) return;
            if (_runSession != null && _runSession.id == s.id) return; // un run tourne déjà dessus
            // Si un checkpoint est en attente, c'est SA carte qui gère la reprise → pas de bandeau.
            if (s.transcript != null)
                foreach (var e in s.transcript)
                    if (e.kind == "wf_checkpoint" && !e.answered) return;
            ShowResumeBanner(s);
        }

        // Bandeau « Run interrompu » → bouton de reprise (RetryWorkflow lit le run_id/seq de la
        // session). Ajouté à l'affichage courant, non enregistré (régénéré à chaque affichage).
        private void ShowResumeBanner(Session s)
        {
            if (s == null || string.IsNullOrEmpty(s.wfRunId)) return;

            var card = new VisualElement();
            card.AddToClassList("cc-ask");

            var title = new Label("Run interrompu");
            title.AddToClassList("cc-ask__title");
            card.Add(title);

            var desc = new Label(
                "Un run n'a pas été terminé (erreur, coupure, ou fermeture de l'outil). Son état est "
                + "conservé côté démon : la reprise relance exactement la dernière phase atteinte, "
                + "sans reposer de questions.");
            desc.AddToClassList("cc-ask__desc");
            card.Add(desc);

            var actions = new VisualElement();
            actions.AddToClassList("cc-ask__actions");
            var resume = new Button { text = "↻ Reprendre où il en était" };
            var ignore = new Button { text = "Ignorer" };
            string runId = s.wfRunId;
            int lastSeq = s.wfLastSeq;
            resume.clicked += () =>
            {
                resume.SetEnabled(false); ignore.SetEnabled(false);
                title.text += " — reprise";
                _wfRunId = runId; _wfLastSeq = lastSeq; // restaure le contexte de reprise
                RetryWorkflow();
            };
            ignore.clicked += () =>
            {
                resume.SetEnabled(false); ignore.SetEnabled(false);
                title.text += " — ignoré";
                s.wfRunId = null; s.wfLastSeq = -1;
                if (s.transcript != null) SessionStore.SaveTranscript(s);
            };
            actions.Add(resume);
            actions.Add(ignore);
            card.Add(actions);

            // Toujours dans la racine AFFICHÉE (pas _chatContent, qui peut viser la racine d'un
            // run d'arrière-plan sur une autre session).
            _displayRoot.Add(card);
            ScrollToBottom();
        }

        // Contenu détaillé d'une carte de checkpoint, par kind. Retourne null si rien à montrer.
        private static string CheckpointDetailsMarkdown(string kind, JObject ev)
        {
            var sb = new StringBuilder();
            switch (kind)
            {
                case "approbation_plan":
                {
                    var hyps = ev["hypotheses"] as JArray;
                    if (hyps != null && hyps.Count > 0)
                    {
                        sb.AppendLine("**Hypothèses tranchées d'office** (si l'une est fausse, corrigez-la via « Ajuster ») :");
                        foreach (var h in hyps)
                        {
                            string src = (string)h?["source"];
                            sb.AppendLine($"- {(string)h?["point"]} → **{(string)h?["decision"]}**" +
                                          (string.IsNullOrEmpty(src) ? "" : $" _(source : {src})_"));
                        }
                    }
                    var notes = ev["notes"] as JArray;
                    if (notes != null && notes.Count > 0)
                    {
                        if (sb.Length > 0) sb.AppendLine();
                        sb.AppendLine("**Remarques non bloquantes du panel** (suivront l'implémentation ; motifs d'ajustement possibles) :");
                        foreach (var n in notes)
                        {
                            string reco = (string)n?["recommandation"];
                            sb.AppendLine($"- `{(string)n?["critique"]}` : {(string)n?["point"]}" +
                                          (string.IsNullOrEmpty(reco) ? "" : $" — {reco}"));
                        }
                    }
                    break;
                }
                case "escalade":
                {
                    if (ev["problemes_restants"] is JObject probs && probs.Count > 0)
                    {
                        sb.AppendLine("**Ce qui bloque** (à traiter dans votre ajustement, ou à assumer en approuvant tel quel) :");
                        foreach (var prop in probs.Properties())
                            if (prop.Value is JArray list)
                                foreach (var p in list)
                                {
                                    string reco = (string)p?["recommandation"];
                                    sb.AppendLine($"- `{prop.Name}` : {(string)p?["point"]}" +
                                                  (string.IsNullOrEmpty(reco) ? "" : $" — correctif attendu : {reco}"));
                                }
                    }
                    break;
                }
                case "escalade_revue":
                {
                    if (ev["review"] is JObject rev)
                    {
                        var tests = rev["tests"];
                        if (tests != null)
                            sb.AppendLine($"**Tests** : {((bool?)tests["succes"] == true ? "succès" : "échec")} — `{(string)tests["commande"]}`" +
                                          (string.IsNullOrEmpty((string)tests["sortie_resume"]) ? "" : $" — {(string)tests["sortie_resume"]}"));
                        var probs = rev["problemes"] as JArray;
                        if (probs != null && probs.Count > 0)
                        {
                            sb.AppendLine();
                            sb.AppendLine("**Ce qui bloque** (à traiter dans votre ajustement, ou à assumer en approuvant tel quel) :");
                            foreach (var p in probs)
                            {
                                if ((string)p?["gravite"] != "bloquant") continue;
                                string reco = (string)p?["recommandation"];
                                sb.AppendLine($"- {(string)p?["point"]}" +
                                              (string.IsNullOrEmpty(reco) ? "" : $" — correctif attendu : {reco}"));
                            }
                        }
                    }
                    break;
                }
                case "validation_avant_doc":
                {
                    var fichiers = (ev["implementation"] as JObject)?["fichiers_modifies"] as JArray;
                    if (fichiers != null && fichiers.Count > 0)
                        AppendList(sb, "Fichiers modifiés", fichiers);
                    if (ev["review"] is JObject rev)
                    {
                        var tests = rev["tests"];
                        if (tests != null && !string.IsNullOrEmpty((string)tests["commande"]))
                        {
                            sb.AppendLine();
                            sb.AppendLine($"**Tests automatisés** : {((bool?)tests["succes"] == true ? "succès" : "échec")} — `{(string)tests["commande"]}`");
                        }
                        var proto = rev["protocole_test"] as JArray;
                        if (proto != null && proto.Count > 0)
                        {
                            sb.AppendLine();
                            sb.AppendLine("**Protocole de test à dérouler avant de valider** :");
                            int i = 1;
                            foreach (var p in proto)
                            {
                                if (p is JObject po)
                                {
                                    string attendu = (string)po["attendu"];
                                    sb.AppendLine($"{i}. {(string)po["etape"]}" +
                                                  (string.IsNullOrEmpty(attendu) ? "" : $"\n   - _Attendu : {attendu}_"));
                                }
                                else
                                    sb.AppendLine($"{i}. {p}");
                                i++;
                            }
                        }
                    }
                    break;
                }
            }
            return sb.Length == 0 ? null : sb.ToString();
        }

        private static string CheckpointTitle(string kind, string agent)
        {
            switch (kind)
            {
                case "clarification":        return "Clarifications requises";
                case "tri":                  return "Nature de la demande";
                case "ask_user":             return $"Question de l'agent « {agent} »";
                case "approbation_plan":     return "Approbation du plan";
                case "validation_avant_doc": return "Validation avant documentation";
                case "escalade":             return "Escalade — plan non stabilisé";
                case "escalade_revue":       return "Escalade — implémentation non validée";
                default:                     return "Décision requise";
            }
        }

        // ---- Conversions JSON → Markdown ----

        private static string BriefToMarkdown(JObject b)
        {
            if (b == null) return null;
            var sb = new StringBuilder();
            sb.AppendLine($"**Objectif** : {(string)b["objectif"]}");
            AppendList(sb, "Livrables", b["livrables"] as JArray);
            AppendList(sb, "Contraintes", b["contraintes"] as JArray);
            AppendList(sb, "Hors scope", b["hors_scope"] as JArray);
            AppendList(sb, "Hypothèses", b["hypotheses"] as JArray);
            AppendList(sb, "Questions ouvertes", b["questions_ouvertes"] as JArray);
            return sb.ToString();
        }

        private static string PlanToMarkdown(JObject p)
        {
            if (p == null) return null;
            var sb = new StringBuilder();
            sb.AppendLine($"**Approche** : {(string)p["approche"]}");
            AppendList(sb, "Étapes", p["etapes"] as JArray);
            AppendList(sb, "Décisions", p["decisions"] as JArray);
            AppendList(sb, "Risques", p["risques"] as JArray);
            AppendList(sb, "Critères de réussite", p["criteres_de_reussite"] as JArray);
            return sb.ToString();
        }

        private static string SyntheseToMarkdown(JObject s)
        {
            if (s == null) return "_(synthèse indisponible)_";
            var sb = new StringBuilder();
            // « rapport » = compte rendu complet destiné à la fenêtre (jamais tronqué,
            // indépendant de ce qui part en mémoire) ; « resume » n'est que le repli
            // (anciens runs, mode conversation).
            string rapport = (string)s["rapport"];
            string corps = !string.IsNullOrEmpty(rapport) ? rapport : (string)s["resume"];
            sb.AppendLine($"### Synthèse\n\n{corps}");
            AppendList(sb, "Livrables produits", s["livrables_produits"] as JArray);
            AppendList(sb, "Réserves", s["reserves"] as JArray);
            AppendList(sb, "Étapes suivantes", s["etapes_suivantes"] as JArray);
            return sb.ToString();
        }

        private static void AppendList(StringBuilder sb, string title, JArray arr)
        {
            if (arr == null || arr.Count == 0) return;
            sb.AppendLine();
            sb.AppendLine($"**{title}** :");
            foreach (var item in arr) sb.AppendLine($"- {JTokenToLine(item)}");
        }

        private static string JTokenToLine(JToken t)
        {
            if (t == null) return "";
            if (t.Type == JTokenType.String) return (string)t;
            if (t is JObject o)
            {
                string head = (string)(o["titre"] ?? o["choix"] ?? o["point"]) ?? "";
                string detail = (string)(o["details"] ?? o["justification"] ?? o["recommandation"] ?? o["resume_changement"]) ?? "";
                if (!string.IsNullOrEmpty(head))
                    return string.IsNullOrEmpty(detail) ? head : $"{head} — {detail}";
                return o.ToString(Newtonsoft.Json.Formatting.None);
            }
            return t.ToString();
        }

        private static string SummarizeCritiques(JObject content)
        {
            if (content == null) return "(aucune)";
            var parts = new List<string>();
            foreach (var prop in content.Properties())
                parts.Add($"{prop.Name}={(string)(prop.Value?["verdict"]) ?? "?"}");
            return string.Join(", ", parts);
        }

        private static string SummarizeFiles(JArray arr)
        {
            if (arr == null || arr.Count == 0) return "(aucun fichier)";
            var parts = new List<string>();
            foreach (var f in arr)
            {
                string ch = (string)(f?["chemin"]) ?? f?.ToString();
                if (!string.IsNullOrEmpty(ch)) parts.Add(ch);
            }
            return string.Join(", ", parts);
        }
    }
}
