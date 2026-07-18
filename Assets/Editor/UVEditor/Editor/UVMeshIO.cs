using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ombrage.Tools.UVEditor
{
    /// <summary>
    /// Entrées / sorties disque de l'UV Editor : sauvegarde du mesh de travail
    /// en <c>.asset</c> et backup versionné de l'ancien <c>.asset</c>.
    ///
    /// La logique est calquée sur celle du Vertex Color Editor afin que les deux
    /// outils se comportent de façon identique :
    ///  - le <c>.asset</c> est écrit dans le dossier du mesh source ;
    ///  - le FBX source n'est JAMAIS modifié ;
    ///  - si un <c>.asset</c> de même nom existe, il est d'abord copié dans
    ///    <c>[RacineProjet]/Backups/UVEditor/...</c> puis écrasé en conservant
    ///    son identité (les MeshFilter / prefabs qui le référencent restent
    ///    valides).
    /// </summary>
    public static class UVMeshIO
    {
        /// <summary>
        /// Dossier de backup, relatif à la racine du projet (à côté de "Assets/").
        /// </summary>
        public const string BackupRootFolder = "Backups/UVEditor";

        public class SaveResult
        {
            public bool success;
            public string message;
            public Mesh savedAsset;
            public string assetPath;
            public string backupPath;
        }

        /// <summary>
        /// Sauvegarde le mesh de travail en <c>.asset</c>, à côté du mesh source.
        /// Si un <c>.asset</c> de même nom existe déjà, il est sauvegardé (backup
        /// versionné) puis écrasé en conservant son identité.
        /// </summary>
        public static SaveResult Save(Mesh workingMesh, Mesh sourceAsset)
        {
            var result = new SaveResult();

            if (workingMesh == null || sourceAsset == null)
            {
                result.message = "Mesh de travail ou mesh source manquant.";
                return result;
            }

            string sourcePath = AssetDatabase.GetAssetPath(sourceAsset);
            if (string.IsNullOrEmpty(sourcePath))
            {
                result.message = "Impossible de retrouver le chemin de l'asset source.";
                return result;
            }

            string dir = Path.GetDirectoryName(sourcePath).Replace('\\', '/');
            string meshName = SanitizeName(sourceAsset.name);
            string targetAssetPath = $"{dir}/{meshName}.asset";

            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(targetAssetPath);

            try
            {
                if (existing != null)
                {
                    // Backup de l'ancien .asset avant écrasement.
                    result.backupPath = BackupExistingAsset(targetAssetPath);

                    // Reconstruction canal par canal DANS l'objet existant :
                    // garde le GUID et le fileID de l'asset (les MeshFilter /
                    // prefabs qui le référencent restent valides). On n'utilise
                    // pas EditorUtility.CopySerialized : il recopie l'état
                    // sérialisé tel quel, ce qui peut omettre un canal qui n'a
                    // pas été intégré au VertexData. CopyMeshInto réécrit chaque
                    // canal via les API Set*, donc tout est sérialisé.
                    CopyMeshInto(workingMesh, existing);
                    existing.name = meshName;
                    existing.hideFlags = HideFlags.None;
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                    result.savedAsset = existing;
                }
                else
                {
                    // Nouveau .asset : même reconstruction propre dans un mesh
                    // neuf, puis création de l'asset.
                    var newMesh = new Mesh { name = meshName };
                    CopyMeshInto(workingMesh, newMesh);
                    newMesh.name = meshName;
                    newMesh.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(newMesh, targetAssetPath);
                    AssetDatabase.SaveAssets();
                    result.savedAsset = newMesh;
                }

                result.success = true;
                result.assetPath = targetAssetPath;
                result.message = existing != null
                    ? $"Mesh écrasé : {targetAssetPath}"
                    : $"Mesh créé : {targetAssetPath}";
            }
            catch (Exception e)
            {
                result.success = false;
                result.message = $"Échec de la sauvegarde : {e.Message}";
            }

            return result;
        }

        /// <summary>
        /// Reconstruit <paramref name="destination"/> à l'identique de
        /// <paramref name="source"/>, canal par canal, via les API <c>Set*</c>.
        ///
        /// Contrairement à <see cref="EditorUtility.CopySerialized"/>, qui
        /// recopie le blob sérialisé du mesh source — au risque d'omettre un
        /// canal non intégré au <c>VertexData</c> —, cette méthode réécrit
        /// explicitement chaque canal présent : positions, normales, tangentes,
        /// couleurs, UV0/1/2, sous-maillages et bindposes. Tout canal présent
        /// est ainsi proprement matérialisé et sérialisé sur le disque.
        ///
        /// L'écriture se fait DANS l'objet destination existant : son identité
        /// (GUID + fileID quand c'est un asset) est préservée.
        /// </summary>
        public static void CopyMeshInto(Mesh source, Mesh destination)
        {
            if (source == null || destination == null)
                return;

            var positions = new List<Vector3>();
            var normals = new List<Vector3>();
            var tangents = new List<Vector4>();
            var colors = new List<Color>();
            var uv0 = new List<Vector2>();
            var uv1 = new List<Vector2>();
            var uv2 = new List<Vector2>();

            source.GetVertices(positions);
            source.GetNormals(normals);
            source.GetTangents(tangents);
            source.GetColors(colors);
            source.GetUVs(0, uv0);
            source.GetUVs(1, uv1);
            source.GetUVs(2, uv2);

            int vCount = positions.Count;

            destination.Clear();
            destination.indexFormat = vCount > 65535
                ? IndexFormat.UInt32
                : IndexFormat.UInt16;

            destination.SetVertices(positions);
            if (normals.Count == vCount)
                destination.SetNormals(normals);
            if (tangents.Count == vCount)
                destination.SetTangents(tangents);
            if (colors.Count == vCount)
                destination.SetColors(colors);
            if (uv0.Count == vCount)
                destination.SetUVs(0, uv0);
            if (uv1.Count == vCount)
                destination.SetUVs(1, uv1);
            if (uv2.Count == vCount)
                destination.SetUVs(2, uv2);

            // Sous-maillages (phase 1 : un seul, mais on copie ce qui existe).
            destination.subMeshCount = source.subMeshCount;
            for (int sm = 0; sm < source.subMeshCount; sm++)
            {
                var tris = new List<int>();
                source.GetTriangles(tris, sm);
                destination.SetTriangles(tris, sm);
            }

            destination.bindposes = source.bindposes;

            if (normals.Count != vCount)
                destination.RecalculateNormals();
            if (tangents.Count != vCount)
                destination.RecalculateTangents();
            destination.RecalculateBounds();
        }

        /// <summary>
        /// Copie le <c>.asset</c> existant vers le dossier de backup avec un
        /// numéro de version.
        /// Ex : <c>Assets/Mesh/Rock/rock_01.asset</c>
        ///   -> <c>[ProjectRoot]/Backups/UVEditor/Mesh/Rock/rock_01_v1.asset</c>
        /// </summary>
        static string BackupExistingAsset(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath)
                .FullName.Replace('\\', '/');
            string fullAssetPath = $"{projectRoot}/{assetPath}";
            if (!File.Exists(fullAssetPath))
                return null;

            string relative = assetPath.StartsWith("Assets/")
                ? assetPath.Substring("Assets/".Length)
                : assetPath;
            string relativeDir = Path.GetDirectoryName(relative).Replace('\\', '/');
            string meshName = Path.GetFileNameWithoutExtension(relative);

            string backupDir = $"{projectRoot}/{BackupRootFolder}";
            if (!string.IsNullOrEmpty(relativeDir))
                backupDir = $"{backupDir}/{relativeDir}";
            Directory.CreateDirectory(backupDir);

            int version = GetNextVersion(backupDir, meshName);
            string backupPath = $"{backupDir}/{meshName}_v{version}.asset";
            File.Copy(fullAssetPath, backupPath, false);
            return backupPath;
        }

        static int GetNextVersion(string backupDir, string meshName)
        {
            int max = 0;
            var regex = new Regex($@"^{Regex.Escape(meshName)}_v(\d+)\.asset$",
                RegexOptions.IgnoreCase);

            foreach (var file in Directory.GetFiles(backupDir, $"{meshName}_v*.asset"))
            {
                var m = regex.Match(Path.GetFileName(file));
                if (m.Success && int.TryParse(m.Groups[1].Value, out int v))
                    max = Mathf.Max(max, v);
            }

            return max + 1;
        }

        static string SanitizeName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Mesh";

            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }
    }
}
