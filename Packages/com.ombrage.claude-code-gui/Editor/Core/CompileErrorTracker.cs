using System.Text;
using UnityEditor;
using UnityEditor.Compilation;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Capture les erreurs de compilation Unity et les conserve à travers les rechargements
    /// de domaine (via SessionState), pour permettre de les renvoyer à Claude en un clic.
    /// </summary>
    [InitializeOnLoad]
    public static class CompileErrorTracker
    {
        private const string KEY = "ClaudeCodeGUI_CompileErrors";

        static CompileErrorTracker()
        {
            CompilationPipeline.compilationStarted += _ => Clear();
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyCompiled;
        }

        private static void OnAssemblyCompiled(string assemblyPath, CompilerMessage[] messages)
        {
            if (messages == null || messages.Length == 0) return;

            var sb = new StringBuilder(SessionState.GetString(KEY, ""));
            foreach (var m in messages)
            {
                if (m.type != CompilerMessageType.Error) continue;
                sb.AppendLine($"{m.file}({m.line},{m.column}): {m.message}");
            }
            SessionState.SetString(KEY, sb.ToString());
        }

        public static string GetErrors() => SessionState.GetString(KEY, "");
        public static bool   HasErrors() => !string.IsNullOrEmpty(GetErrors());
        public static void   Clear()     => SessionState.SetString(KEY, "");
    }
}
