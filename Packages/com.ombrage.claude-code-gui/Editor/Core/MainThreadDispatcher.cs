using System;
using System.Collections.Concurrent;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// File d'actions à exécuter sur le main thread de l'éditeur. Les callbacks du
    /// ClaudeProcessRunner/StreamParser arrivent sur le reader thread : on les y empile,
    /// la fenêtre les draine dans EditorApplication.update / schedule.
    /// </summary>
    public sealed class MainThreadDispatcher
    {
        private readonly ConcurrentQueue<Action> _queue = new();

        public void Enqueue(Action action)
        {
            if (action != null) _queue.Enqueue(action);
        }

        /// <summary>Exécute toutes les actions en attente. À appeler depuis le main thread.</summary>
        public void Drain()
        {
            while (_queue.TryDequeue(out var action))
            {
                try { action(); }
                catch (Exception ex) { UnityEngine.Debug.LogException(ex); }
            }
        }

        public void Clear()
        {
            while (_queue.TryDequeue(out _)) { }
        }
    }
}
