using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Recherche de fichiers du projet pour l'autocomplétion @fichier. Les chemins (sous
    /// Assets/) sont mis en cache et rafraîchis périodiquement.
    /// </summary>
    public static class AssetSearchProvider
    {
        private static string[] _cache;
        private static double   _cacheTime = -100;

        public static IEnumerable<string> Search(string query, int max = 30)
        {
            EnsureCache();
            if (string.IsNullOrEmpty(query))
                return _cache.Take(max);

            return _cache
                .Where(p => p.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .OrderBy(p => Path.GetFileName(p).Length)
                .Take(max);
        }

        public static void Invalidate() => _cacheTime = -100;

        private static void EnsureCache()
        {
            if (_cache != null && EditorApplication.timeSinceStartup - _cacheTime < 30) return;
            _cache = AssetDatabase.GetAllAssetPaths()
                .Where(p => p.StartsWith("Assets/", StringComparison.Ordinal) && Path.HasExtension(p))
                .ToArray();
            _cacheTime = EditorApplication.timeSinceStartup;
        }
    }
}
