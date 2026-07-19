// OceanHdrpPath.cs  (outillage éditeur de test)
// Résolution DYNAMIQUE du chemin disque + de la version du package HDRP (aucun chemin codé en dur,
// aucun glob « à la main » en primaire).
//
// Primaire : UnityEditor.PackageManager.PackageInfo.FindForPackageName(...) → resolvedPath + version
//            (une ligne, robuste, pointe indifféremment vers Library/PackageCache/…@17.4.0 OU Packages/).
// Repli    : glob Library/PackageCache/com.unity.render-pipelines.high-definition@* puis Packages/…,
//            avec lecture de la version dans package.json (au cas où l'API renverrait null).
//
// Les SHADERS gardent leurs includes 'Packages/com.unity.render-pipelines.high-definition/…' auto-résolus
// par Unity ; ce helper couvre UNIQUEMENT le besoin C#/outillage (logs de test, vérif de version).
using System.IO;
using UnityEditor;
using UnityEngine;
// Lève l'ambiguïté CS0104 : 'PackageInfo' existe dans UnityEditor ET UnityEditor.PackageManager.
// On ne veut QUE celui du PackageManager (FindForPackageName) → alias explicite, pas de using global.
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace Ombrage.OceanFeatures.EditorTools
{
    public static class OceanHdrpPath
    {
        public const string PackageName = "com.unity.render-pipelines.high-definition";
        public const string ExpectedVersion = "17.4.0";

        /// Chemin disque absolu du package HDRP résolu, ou null si introuvable. Renseigne aussi la version.
        public static string Resolve(out string version)
        {
            version = null;

            // --- Primaire : API PackageManager (Editor-only, une ligne) ---
            var info = PackageInfo.FindForPackageName(PackageName);
            if (info != null)
            {
                version = info.version;
                return info.resolvedPath;
            }

            // --- Repli 1 : glob Library/PackageCache/…@* ---
            string cacheRoot = Path.Combine(Directory.GetCurrentDirectory(), "Library", "PackageCache");
            if (Directory.Exists(cacheRoot))
            {
                var dirs = Directory.GetDirectories(cacheRoot, PackageName + "@*");
                if (dirs.Length > 0)
                {
                    version = ReadPackageJsonVersion(dirs[0]);
                    return dirs[0];
                }
            }

            // --- Repli 2 : Packages/… (package embarqué) ---
            string embedded = Path.Combine(Directory.GetCurrentDirectory(), "Packages", PackageName);
            if (Directory.Exists(embedded))
            {
                version = ReadPackageJsonVersion(embedded);
                return embedded;
            }

            return null;
        }

        static string ReadPackageJsonVersion(string packageDir)
        {
            try
            {
                string pj = Path.Combine(packageDir, "package.json");
                if (!File.Exists(pj)) return null;
                foreach (var line in File.ReadAllLines(pj))
                {
                    // Ligne type : "version": "17.4.0",
                    int i = line.IndexOf("\"version\"", System.StringComparison.Ordinal);
                    if (i < 0) continue;
                    int firstQuote = line.IndexOf('"', i + 9);
                    if (firstQuote < 0) continue;
                    int secondQuote = line.IndexOf('"', firstQuote + 1);
                    if (secondQuote < 0) continue;
                    return line.Substring(firstQuote + 1, secondQuote - firstQuote - 1);
                }
            }
            catch { /* lecture best-effort : le repli ne doit jamais jeter */ }
            return null;
        }

        [MenuItem("Ombrage/Ocean/Log HDRP Package Path + Version")]
        public static void LogResolvedPath()
        {
            string version;
            string path = Resolve(out version);

            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError($"[Ocean] HDRP introuvable ({PackageName}) — ni PackageManager, ni PackageCache, ni Packages/.");
                return;
            }

            if (string.IsNullOrEmpty(version))
                Debug.LogWarning($"[Ocean] HDRP résolu : {path}\nVersion NON déterminée (package.json illisible).");
            else if (version != ExpectedVersion)
                Debug.LogWarning($"[Ocean] HDRP résolu : {path}\nVersion INSTALLÉE = {version} ≠ attendue {ExpectedVersion} — vérifier la compatibilité du test.");
            else
                Debug.Log($"[Ocean] HDRP OK : {path}\nVersion = {version} (attendue {ExpectedVersion}).");
        }
    }
}
