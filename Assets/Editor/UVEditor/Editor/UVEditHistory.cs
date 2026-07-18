using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Historique d'annulation interne à l'UV Editor.
    ///
    /// L'outil ne confie PAS sa géométrie au système Undo d'Unity :
    /// <see cref="UnityEditor.Undo"/> est conçu pour des objets sérialisables
    /// stables, pas pour un mesh transitoire reconstruit en continu. Le mélanger
    /// à l'Undo global provoquait des annulations manquées et des annulations
    /// qui débordaient sur la scène.
    ///
    /// À la place, chaque "commit" empile ici un instantané complet (géométrie
    /// du mesh + réglages de projection + sélection de faces). Ctrl+Z / Ctrl+Y
    /// naviguent dans cette pile, uniquement quand la fenêtre de l'outil a le
    /// focus, sans jamais toucher l'Undo de la scène.
    ///
    /// Le modèle est un curseur dans une liste : empiler après une annulation
    /// tronque les états "refaire" devenus obsolètes.
    /// </summary>
    public class UVEditHistory
    {
        /// <summary>Instantané complet d'un état committé.</summary>
        public class Snapshot
        {
            // Géométrie du mesh de travail.
            public Vector3[] positions;
            public Vector3[] normals;
            public Vector4[] tangents;
            public Color[] colors;
            public Vector2[] uv0;
            public Vector2[] uv1;
            public Vector2[] uv2;
            public int[] triangles;
            public bool hasNormals;
            public bool hasTangents;
            public bool hasColors;
            public bool hasUV0;
            public bool hasUV1;
            public bool hasUV2;

            // Réglages de projection à cet instant.
            public UVProjectionSettings projection;

            // Sélection (indices de vertices) à cet instant.
            public int[] selectedVertices;
            public int vertexCount;

            // Vrai si une projection avait déjà été appliquée.
            public bool projectionStarted;
        }

        readonly List<Snapshot> _states = new List<Snapshot>();
        int _cursor = -1;          // index de l'état courant dans _states
        readonly int _maxDepth;

        public UVEditHistory(int maxDepth = 50)
        {
            _maxDepth = Mathf.Max(1, maxDepth);
        }

        public bool CanUndo => _cursor > 0;
        public bool CanRedo => _cursor >= 0 && _cursor < _states.Count - 1;
        public int Count => _states.Count;
        public bool IsEmpty => _states.Count == 0;

        /// <summary>Vide entièrement l'historique.</summary>
        public void Clear()
        {
            _states.Clear();
            _cursor = -1;
        }

        /// <summary>
        /// Empile un nouvel état committé. Tronque d'abord les états "refaire"
        /// situés après le curseur, puis applique la limite de profondeur.
        /// </summary>
        public void Push(Snapshot snapshot)
        {
            if (snapshot == null)
                return;

            // Tout ce qui suit le curseur (états "refaire") devient obsolète.
            if (_cursor < _states.Count - 1)
                _states.RemoveRange(_cursor + 1, _states.Count - _cursor - 1);

            _states.Add(snapshot);

            // Limite de profondeur : on retire les plus anciens.
            while (_states.Count > _maxDepth)
                _states.RemoveAt(0);

            _cursor = _states.Count - 1;
        }

        /// <summary>
        /// Recule d'un cran et retourne l'état à restaurer, ou null si aucune
        /// annulation n'est possible.
        /// </summary>
        public Snapshot Undo()
        {
            if (!CanUndo)
                return null;
            _cursor--;
            return _states[_cursor];
        }

        /// <summary>
        /// Avance d'un cran et retourne l'état à restaurer, ou null si aucun
        /// rétablissement n'est possible.
        /// </summary>
        public Snapshot Redo()
        {
            if (!CanRedo)
                return null;
            _cursor++;
            return _states[_cursor];
        }

        /// <summary>État courant sous le curseur, ou null si l'historique est vide.</summary>
        public Snapshot Current =>
            (_cursor >= 0 && _cursor < _states.Count) ? _states[_cursor] : null;
    }
}
