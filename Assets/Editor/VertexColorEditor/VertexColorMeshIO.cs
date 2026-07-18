using System;
using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace Ombrage.Tools.VertexColorEditor
{
    /// <summary>
    /// Entrées / sorties disque : lecture des couleurs existantes,
    /// sauvegarde du mesh en .asset et backup versionné de l'ancien .asset.
    /// </summary>
    public static class VertexColorMeshIO
    {
        /// <summary>
        /// Dossier de backup, relatif à la racine du projet (à côté de "Assets/").
        /// Ex : [ProjectRoot]/Backups/VertexColorEditor/...
        /// </summary>
        public const string BackupRootFolder = "Backups/VertexColorEditor";

        /// <summary>
        /// Retourne les vertex colors du mesh, ou un tableau blanc opaque
        /// si le mesh n'en possède pas encore.
        /// </summary>
        public static Color[] LoadColors(Mesh mesh)
        {
            if (mesh == null)
                return Array.Empty<Color>();

            Color[] existing = mesh.colors;
            if (existing != null && existing.Length == mesh.vertexCount)
                return (Color[])existing.Clone();

            var colors = new Color[mesh.vertexCount];
            for (int i = 0; i < colors.Length; i++)
                colors[i] = Color.white;
            return colors;
        }

        public class SaveResult
        {
            public bool success;
            public string message;
            public Mesh savedAsset;
            public string assetPath;
            public string backupPath;
        }

        /// <summary>
        /// Sauvegarde le mesh de travail en .asset, à côté du mesh source.
        /// Si un .asset de même nom existe déjà, il est sauvegardé (backup versionné)
        /// puis écrasé en conservant son identité (les références restent valides).
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

                    // On reconstruit le contenu de 'existing' canal par canal via les
                    // API Set* plutôt que CopySerialized : CopySerialized recopiait
                    // un état où le canal couleur n'était pas intégré au VertexData
                    // sérialisé (m_Colors absent du .asset). SetColors garantit son
                    // intégration. On écrit dans 'existing' : l'identité de l'asset
                    // (GUID, fileID) est conservée, les MeshFilter restent valides.
                    CopyMeshInto(workingMesh, existing);
                    existing.name = meshName;
                    existing.hideFlags = HideFlags.None;
                    existing.UploadMeshData(false);

                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssets();
                    // Invalide le cache de l'AssetDatabase pour ce fichier.
                    AssetDatabase.ImportAsset(targetAssetPath,
                        ImportAssetOptions.ForceUpdate);
                    result.savedAsset = existing;
                }
                else
                {
                    var newMesh = new Mesh { name = meshName };
                    CopyMeshInto(workingMesh, newMesh);
                    newMesh.hideFlags = HideFlags.None;
                    AssetDatabase.CreateAsset(newMesh, targetAssetPath);
                    AssetDatabase.SaveAssets();
                    result.savedAsset = newMesh;
                }

                // Rafraîchit les vues : les SceneView / Game affichent le mesh à jour.
                SceneView.RepaintAll();
                UnityEditorInternal.InternalEditorUtility.RepaintAllViews();

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
        /// Copie le contenu de 'src' dans 'dst' canal par canal, via les API Set*.
        /// Contrairement à CopySerialized, cela reconstruit proprement le VertexData
        /// de 'dst' : tous les canaux présents — dont la couleur via SetColors — sont
        /// intégrés et seront correctement sérialisés dans le .asset.
        /// 'dst' conserve son identité d'asset (on ne le recrée pas).
        /// </summary>
        static void CopyMeshInto(Mesh src, Mesh dst)
        {
            dst.Clear();
            dst.indexFormat = src.indexFormat;

            // Géométrie de base.
            dst.SetVertices(GetList(src.vertices));

            var normals = src.normals;
            if (normals != null && normals.Length == src.vertexCount)
                dst.SetNormals(GetList(normals));

            var tangents = src.tangents;
            if (tangents != null && tangents.Length == src.vertexCount)
                dst.SetTangents(GetList(tangents));

            // Couleur : SetColors force l'intégration du canal dans le VertexData.
            var colors = src.colors;
            if (colors != null && colors.Length == src.vertexCount)
                dst.SetColors(GetList(colors));

            // UV (jusqu'à 8 canaux).
            var uv = new System.Collections.Generic.List<Vector2>();
            for (int channel = 0; channel < 8; channel++)
            {
                src.GetUVs(channel, uv);
                if (uv.Count == src.vertexCount)
                    dst.SetUVs(channel, uv);
            }

            // Sous-maillages et indices.
            dst.subMeshCount = src.subMeshCount;
            for (int sm = 0; sm < src.subMeshCount; sm++)
            {
                dst.SetTriangles(src.GetTriangles(sm), sm, false);
                dst.SetSubMesh(sm, src.GetSubMesh(sm));
            }

            // BindPoses / bones : préservés si présents (mesh skinné).
            var bindposes = src.bindposes;
            if (bindposes != null && bindposes.Length > 0)
                dst.bindposes = bindposes;
            var boneWeights = src.boneWeights;
            if (boneWeights != null && boneWeights.Length == src.vertexCount)
                dst.boneWeights = boneWeights;

            dst.RecalculateBounds();
        }

        static System.Collections.Generic.List<T> GetList<T>(T[] array)
        {
            return new System.Collections.Generic.List<T>(array);
        }

        /// <summary>
        /// Copie le .asset existant vers le dossier de backup avec un numéro de version.
        /// Ex : Assets/Mesh/Rock/rock_01.asset
        ///   -> [ProjectRoot]/Backups/VertexColorEditor/Mesh/Rock/rock_01_v1.asset
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
