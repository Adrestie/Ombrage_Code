using System;
using System.Text;
using Newtonsoft.Json.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>Métadonnées d'un événement `result` : coût, tokens, durée, tours.</summary>
    public readonly struct ResultUsage
    {
        public readonly double CostUsd;
        public readonly int    InputTokens;
        public readonly int    OutputTokens;
        public readonly int    NumTurns;
        public readonly long   DurationMs;

        public ResultUsage(double cost, int input, int output, int turns, long durationMs)
        {
            CostUsd = cost; InputTokens = input; OutputTokens = output;
            NumTurns = turns; DurationMs = durationMs;
        }
    }

    /// <summary>
    /// Parse les lignes JSON émises par `claude -p --output-format stream-json --include-partial-messages`
    /// et déclenche des callbacks typés. Tout content_block est référencé par son index pour permettre
    /// au consommateur de dédupliquer (stream_event = streaming, assistant = vue canonique en fin de message).
    /// Callbacks fired sur le thread appelant (généralement le reader thread).
    /// </summary>
    public class ClaudeStreamParser
    {
        // Session / status
        public Action<string /*sessionId*/>           OnSessionId;
        public Action<string /*model*/>               OnSessionInit;
        public Action<string /*description*/>         OnTaskStarted;
        public Action<string /*description*/,
                       string /*lastTool*/>            OnTaskProgress;
        public Action<string /*description*/>         OnTaskCompleted;

        // Début d'un message assistant (réinitialise l'indexation des content blocks).
        public Action                                 OnMessageStart;

        // Content blocks (stream_event) — indexés par content_block_index
        public Action<int    /*index*/,
                       string /*blockType*/,
                       string /*toolName*/>            OnContentBlockStart;
        public Action<int    /*index*/,
                       string /*chunk*/>               OnTextChunk;
        public Action<int    /*index*/,
                       string /*chunk*/>               OnThinkingChunk;
        public Action<int    /*index*/>                OnContentBlockStop;

        // Assistant message complete — pour finaliser tool_use avec leur input canonique
        public Action<JArray /*content*/>             OnAssistantContent;

        // Tool result (user event) / final result
        public Action<bool   /*isError*/,
                       string /*output*/,
                       string /*toolUseId*/>           OnToolResult;
        public Action<bool   /*isError*/,
                       string /*text*/>                OnResult;

        // Usage / coût (émis sur l'événement `result`)
        public Action<ResultUsage>                    OnUsage;

        // Question interactive : le harness n'a pas pu y répondre (mode -p), à nous de gérer.
        public Action<string /*toolUseId*/,
                       JArray /*questions*/>            OnAskUserQuestion;

        // Limite de débit signalée par la CLI.
        public Action<string /*message*/>             OnRateLimit;

        // Progression de la réflexion (nombre estimé de tokens) pendant que le modèle « pense ».
        public Action<int /*estimatedTokens*/>        OnThinkingProgress;

        public Action<string /*error*/>               OnParseError;

        public bool DebugLogging;

        public void Parse(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;

            // Log RAW AVANT le parsing : sinon une ligne non-JSON (qui fait échouer JObject.Parse)
            // ne serait jamais tracée, même en mode debug.
            if (DebugLogging)
            {
                string preview = line.Length > 500 ? line.Substring(0, 500) + "…" : line;
                Debug.Log($"[ClaudeCode] RAW: {preview}");
            }

            try
            {
                var obj = JObject.Parse(line);
                string type = (string)obj["type"];

                switch (type)
                {
                    case "system":       HandleSystem(obj);      break;
                    case "assistant":    HandleAssistant(obj);   break;
                    case "user":         HandleUser(obj);        break;
                    case "result":       HandleResult(obj);      break;
                    case "stream_event": HandleStreamEvent(obj); break;
                    case "rate_limit_event":
                    {
                        var info = obj["rate_limit_info"] ?? obj["rate_limit"];
                        string status = (string)info?["status"] ?? (string)obj["status"];
                        // "allowed" = requête dans les limites : rien à signaler.
                        if (string.IsNullOrEmpty(status) ||
                            string.Equals(status, "allowed", StringComparison.OrdinalIgnoreCase))
                            break;
                        OnRateLimit?.Invoke($"statut « {status} »");
                        break;
                    }
                    default:
                        if (DebugLogging)
                            Debug.Log($"[ClaudeCode] Unknown event type='{type}'");
                        break;
                }
            }
            catch (Exception ex)
            {
                string preview = line.Length > 200 ? line.Substring(0, 200) + "…" : line;
                OnParseError?.Invoke($"{ex.Message} | ligne : {preview}");
            }
        }

        private void HandleSystem(JObject obj)
        {
            string sessionId = (string)obj["session_id"];
            if (!string.IsNullOrEmpty(sessionId)) OnSessionId?.Invoke(sessionId);

            switch ((string)obj["subtype"])
            {
                case "init":           OnSessionInit?.Invoke((string)obj["model"]); break;
                case "thinking_tokens": OnThinkingProgress?.Invoke((int?)obj["estimated_tokens"] ?? 0); break;
                case "task_started":   OnTaskStarted?.Invoke((string)obj["description"]); break;
                case "task_progress":  OnTaskProgress?.Invoke((string)obj["description"],
                                                              (string)obj["last_tool_name"]); break;
                case "task_completed": OnTaskCompleted?.Invoke((string)obj["description"]); break;
            }
        }

        private static JArray GetContentArray(JObject obj)
        {
            return obj["content"] as JArray ?? obj["message"]?["content"] as JArray;
        }

        private void HandleAssistant(JObject obj)
        {
            var content = GetContentArray(obj);
            if (content == null) return;

            // Repère les AskUserQuestion en amont pour déclencher le popup natif.
            foreach (var item in content)
            {
                if ((string)item["type"] != "tool_use") continue;
                if ((string)item["name"] != "AskUserQuestion") continue;

                string toolUseId = (string)item["id"];
                var questions = item["input"]?["questions"] as JArray;
                if (!string.IsNullOrEmpty(toolUseId) && questions != null)
                    OnAskUserQuestion?.Invoke(toolUseId, questions);
            }

            OnAssistantContent?.Invoke(content);
        }

        private void HandleUser(JObject obj)
        {
            var content = GetContentArray(obj);
            if (content == null) return;

            foreach (var item in content)
            {
                if ((string)item["type"] != "tool_result") continue;

                bool isError = (bool?)item["is_error"] ?? false;
                string output = ExtractToolResultContent(item);
                string toolUseId = (string)item["tool_use_id"];
                OnToolResult?.Invoke(isError, output, toolUseId);
            }
        }

        private static string ExtractToolResultContent(JToken item)
        {
            var contentField = item["content"];
            if (contentField is JArray arr)
            {
                var sb = new StringBuilder();
                foreach (var c in arr)
                {
                    string t = (string)c["text"];
                    if (t != null) sb.AppendLine(t);
                }
                return sb.ToString().TrimEnd();
            }
            if (contentField is JValue v)
                return (string)v;
            return (string)item["text"];
        }

        private void HandleResult(JObject obj)
        {
            string sessionId = (string)obj["session_id"];
            if (!string.IsNullOrEmpty(sessionId)) OnSessionId?.Invoke(sessionId);

            // Usage / coût
            var usage = obj["usage"];
            double cost = (double?)obj["total_cost_usd"] ?? 0d;
            int input  = (int?)usage?["input_tokens"] ?? 0;
            int output = (int?)usage?["output_tokens"] ?? 0;
            int cacheRead = (int?)usage?["cache_read_input_tokens"] ?? 0;
            int cacheCreate = (int?)usage?["cache_creation_input_tokens"] ?? 0;
            int turns  = (int?)obj["num_turns"] ?? 0;
            long durationMs = (long?)obj["duration_ms"] ?? 0L;
            if (cost > 0 || input > 0 || output > 0)
                OnUsage?.Invoke(new ResultUsage(cost, input + cacheRead + cacheCreate, output, turns, durationMs));

            bool isError = (bool?)obj["is_error"] ?? false;
            string text;
            if (isError)
            {
                // La CLI ne fournit pas toujours un champ `error` : sur une fin par limite de
                // tours, seul `subtype` est renseigné (« error_max_turns »). On en tire un
                // message explicite plutôt que le repli « Erreur inconnue » trompeur.
                text = (string)obj["error"];
                if (string.IsNullOrEmpty(text))
                {
                    string subtype = (string)obj["subtype"];
                    text = subtype switch
                    {
                        "error_max_turns"        => "Limite de tours atteinte (--max-turns). Tapez « continue » pour reprendre, ou désactivez la limite dans les réglages.",
                        "error_during_execution" => "Erreur pendant l'exécution.",
                        _ => string.IsNullOrEmpty(subtype) ? "Erreur inconnue" : $"Erreur : {subtype}",
                    };
                }
            }
            else
            {
                text = (string)obj["result"];
            }
            OnResult?.Invoke(isError, text);
        }

        private void HandleStreamEvent(JObject obj)
        {
            var ev = obj["event"] as JObject;
            if (ev == null) return;

            switch ((string)ev["type"])
            {
                case "message_start":
                    OnMessageStart?.Invoke();
                    break;
                case "content_block_start":
                {
                    int index = (int?)ev["index"] ?? -1;
                    var block = ev["content_block"];
                    string blockType = (string)block?["type"];
                    string toolName = blockType == "tool_use" ? (string)block?["name"] : null;
                    OnContentBlockStart?.Invoke(index, blockType, toolName);
                    break;
                }
                case "content_block_delta":
                {
                    int index = (int?)ev["index"] ?? -1;
                    var delta = ev["delta"];
                    string deltaType = (string)delta?["type"];
                    if (deltaType == "thinking_delta")
                    {
                        string chunk = (string)delta["thinking"];
                        if (!string.IsNullOrEmpty(chunk)) OnThinkingChunk?.Invoke(index, chunk);
                    }
                    else if (deltaType == "text_delta")
                    {
                        string chunk = (string)delta["text"];
                        if (!string.IsNullOrEmpty(chunk)) OnTextChunk?.Invoke(index, chunk);
                    }
                    break;
                }
                case "content_block_stop":
                {
                    int index = (int?)ev["index"] ?? -1;
                    OnContentBlockStop?.Invoke(index);
                    break;
                }
            }
        }
    }
}
