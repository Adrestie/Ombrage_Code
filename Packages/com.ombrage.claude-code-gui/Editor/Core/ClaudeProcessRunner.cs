using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Lance et gère le process `claude`, lit stdout ligne par ligne et stderr en fin de run.
    /// Tous les callbacks (OnStdoutLine, OnStderr, OnExited) s'exécutent sur le reader thread —
    /// c'est au consommateur de marshaler vers le main thread (voir MainThreadDispatcher).
    /// </summary>
    public class ClaudeProcessRunner
    {
        private Process _process;
        private Thread  _readerThread;
        private volatile bool _killed;
        private readonly object _stdinLock = new();
        private bool _stdinClosed;

        public Action<string>    OnStdoutLine;
        public Action<string>    OnStderr;
        public Action            OnExited;
        public Action<Exception> OnLaunchError;

        public bool IsRunning => _process is { HasExited: false };

        /// <summary>
        /// Échappe une valeur pour l'insérer dans ProcessStartInfo.Arguments (règles Windows /
        /// CommandLineToArgvW). Gère les espaces, guillemets et backslashes (y compris un backslash
        /// final, qui sinon échapperait le guillemet fermant et corromprait toute la ligne).
        /// </summary>
        public static string EscapeArgument(string arg)
        {
            arg ??= "";
            // Pas de caractère spécial : on peut laisser tel quel.
            if (arg.Length > 0 && arg.IndexOfAny(new[] { ' ', '\t', '\n', '\r', '\v', '"' }) < 0)
                return arg;

            var sb = new StringBuilder();
            sb.Append('"');
            for (int i = 0; ; i++)
            {
                int backslashes = 0;
                while (i < arg.Length && arg[i] == '\\') { backslashes++; i++; }

                if (i == arg.Length)
                {
                    // Backslashes finaux : on les double (ils précèdent le guillemet fermant).
                    sb.Append('\\', backslashes * 2);
                    break;
                }
                if (arg[i] == '"')
                {
                    // Backslashes avant un guillemet : doublés + 1 pour échapper le guillemet.
                    sb.Append('\\', backslashes * 2 + 1);
                    sb.Append('"');
                }
                else
                {
                    sb.Append('\\', backslashes);
                    sb.Append(arg[i]);
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public bool Start(string executable, string arguments, string firstStdinLine, string workingDir)
        {
            try
            {
                var startInfo = BuildStartInfo(executable, arguments, workingDir);

                _killed = false;
                _stdinClosed = false;
                _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
                _process.Exited += (_, _) => { if (!_killed) OnExited?.Invoke(); };
                _process.Start();

                if (!string.IsNullOrEmpty(firstStdinLine))
                    WriteStdinLine(firstStdinLine);

                _readerThread = new Thread(ReadOutput) { IsBackground = true };
                _readerThread.Start();
                return true;
            }
            catch (Exception ex)
            {
                OnLaunchError?.Invoke(ex);
                return false;
            }
        }

        // En --input-format stream-json, chaque message utilisateur est une ligne JSON
        // sur stdin. On garde stdin ouvert pour pouvoir répondre aux tool_use (ex: AskUserQuestion).
        //
        // ⚠ Encodage : ProcessStartInfo n'expose pas de StandardInputEncoding fiable sous
        // Mono/Unity. Le StreamWriter par défaut utilise la code page Windows (1252 sur fr-FR),
        // ce qui mutile les caractères non-ASCII et casse le parsing JSON côté Claude. On écrit
        // donc directement les bytes UTF-8 sur BaseStream.
        private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

        public void WriteStdinLine(string jsonLine)
        {
            if (string.IsNullOrEmpty(jsonLine)) return;
            lock (_stdinLock)
            {
                if (_stdinClosed) return;
                if (_process is not { HasExited: false }) return;
                try
                {
                    var bytes = Utf8NoBom.GetBytes(jsonLine + "\n");
                    var stream = _process.StandardInput.BaseStream;
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                catch (Exception ex)
                {
                    OnStderr?.Invoke($"[stdin] {ex.Message}");
                }
            }
        }

        public void CloseStdin()
        {
            lock (_stdinLock)
            {
                if (_stdinClosed) return;
                _stdinClosed = true;
                try { _process?.StandardInput?.Close(); } catch { }
            }
        }

        private void ReadOutput()
        {
            try
            {
                // Lecture du stderr ligne par ligne, en direct : les messages d'erreur et de
                // nouvelle tentative de la CLI (ex. 529 « overloaded ») remontent immédiatement.
                var stderrThread = new Thread(() =>
                {
                    try
                    {
                        string e;
                        while ((e = _process.StandardError.ReadLine()) != null)
                            if (!string.IsNullOrWhiteSpace(e)) OnStderr?.Invoke(e);
                    }
                    catch { /* flux fermé à l'arrêt */ }
                }) { IsBackground = true };
                stderrThread.Start();

                string line;
                while (!_killed && (line = _process.StandardOutput.ReadLine()) != null)
                {
                    if (_killed) break;
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    OnStdoutLine?.Invoke(line);
                }

                stderrThread.Join(3000);
            }
            catch (Exception ex)
            {
                OnStderr?.Invoke($"[Reader] {ex.Message}");
            }
        }

        public void Kill()
        {
            _killed = true;
            CloseStdin();
            try
            {
                if (_process is { HasExited: false })
                {
                    if (Application.platform == RuntimePlatform.WindowsEditor)
                    {
                        var killInfo = new ProcessStartInfo("taskkill", $"/T /F /PID {_process.Id}")
                        {
                            CreateNoWindow = true,
                            UseShellExecute = false,
                        };
                        try { Process.Start(killInfo)?.WaitForExit(3000); } catch { }
                    }
                    else
                    {
                        try { _process.Kill(); } catch { }
                    }
                }
            }
            finally
            {
                if (_readerThread is { IsAlive: true }) _readerThread.Join(1000);
                _process = null;
            }
        }

        private static ProcessStartInfo BuildStartInfo(string executable, string arguments, string workingDir)
        {
            var info = new ProcessStartInfo
            {
                FileName               = executable,
                Arguments              = arguments,
                WorkingDirectory       = workingDir ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                UseShellExecute        = false,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                RedirectStandardInput  = true,
                CreateNoWindow         = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding  = Encoding.UTF8,
            };

            if (Application.platform == RuntimePlatform.WindowsEditor)
            {
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                string localBin = Path.Combine(userProfile, ".local", "bin");
                if (!path.Contains(localBin))
                    info.EnvironmentVariables["PATH"] = $"{localBin};{path}";
            }

            return info;
        }

        /// <summary>
        /// Lance `&lt;executable&gt; --version` de manière synchrone (max 5s).
        /// Renvoie true et remplit `version` en cas de succès, sinon remplit `error`.
        /// </summary>
        public static bool TryValidate(string executable, out string version, out string error)
        {
            version = null;
            error = null;
            try
            {
                var info = BuildStartInfo(executable, "--version", null);
                using var p = Process.Start(info);
                if (p == null) { error = "Process.Start a renvoyé null"; return false; }
                if (!p.WaitForExit(5000))
                {
                    try { p.Kill(); } catch { }
                    error = "Timeout (>5s)";
                    return false;
                }
                version = p.StandardOutput.ReadToEnd().Trim();
                string stderr = p.StandardError.ReadToEnd().Trim();
                if (p.ExitCode != 0)
                {
                    error = !string.IsNullOrEmpty(stderr) ? stderr : $"exit code {p.ExitCode}";
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
