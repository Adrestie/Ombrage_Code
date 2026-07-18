// OceanEditorIO.cs  (banc de validation P2 — outillage éditeur)
// Petit utilitaire partagé : création récursive de dossier d'assets (les builders de gate écrivent
// sous Assets/Shader/Ocean_v2/Tests/… qui peut ne pas exister au premier passage).
using UnityEditor;

namespace Ombrage.OceanFeatures.GateTools
{
    public static class OceanEditorIO
    {
        /// Crée le dossier d'assets (et ses parents) s'il n'existe pas. Chemin type "Assets/A/B/C".
        public static void EnsureFolder(string assetFolder)
        {
            if (string.IsNullOrEmpty(assetFolder) || AssetDatabase.IsValidFolder(assetFolder))
                return;

            var parts = assetFolder.Split('/');
            string current = parts[0]; // "Assets"
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
