using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Ombrage.Tools.Core.Meshing;
using Ombrage.Tools.Core.Serialization;
using Ombrage.Tools.Core.Settings;

namespace Ombrage.Tools.Editor
{
    /// <summary>Outcome of an export attempt, surfaced back to the editor window.</summary>
    public readonly struct ExportResult
    {
        /// <summary>False only when the user cancelled the save dialog.</summary>
        public readonly bool Completed;
        public readonly string Message;

        public ExportResult(bool completed, string message)
        {
            Completed = completed;
            Message = message;
        }
    }

    /// <summary>
    /// Exports a generated mesh as an FBX model plus its JSON preset, written side by side.
    ///
    /// The JSON preset is always written (it has no external dependency). The FBX is written
    /// through the official <c>com.unity.formats.fbx</c> package, invoked by reflection so
    /// the tool still compiles and runs when that package is not installed — in that case
    /// the export degrades gracefully to "JSON only".
    /// </summary>
    public static class RockExporter
    {
        const string FbxExporterType =
            "UnityEditor.Formats.Fbx.Exporter.ModelExporter, Unity.Formats.Fbx.Editor";

        /// <summary>
        /// Prompts for a save location, then writes <c>name.fbx</c> and <c>name.json</c>.
        /// The mesh is exported exactly as supplied (the caller is expected to pass the final
        /// result, rebuilt cleanly at the origin).
        /// </summary>
        public static ExportResult Export(RockGenerationSettings settings, MeshData meshData)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (meshData == null) throw new ArgumentNullException(nameof(meshData));

            string suggestedName = string.IsNullOrEmpty(settings.presetName)
                ? "Rock" : settings.presetName;

            string fbxPath = EditorUtility.SaveFilePanel(
                "Export (FBX + JSON)", Application.dataPath, suggestedName, "fbx");

            if (string.IsNullOrEmpty(fbxPath))
                return new ExportResult(false, "Export cancelled.");

            string directory = Path.GetDirectoryName(fbxPath);
            string baseName = Path.GetFileNameWithoutExtension(fbxPath);
            string jsonPath = Path.Combine(directory ?? string.Empty, baseName + ".json");

            try
            {
                File.WriteAllText(jsonPath, new JsonSettingsSerializer().Serialize(settings));
            }
            catch (Exception e)
            {
                return new ExportResult(true, "Failed to write JSON preset: " + e.Message);
            }

            string fbxMessage = ExportFbx(meshData, baseName, fbxPath);

            if (IsInsideAssets(fbxPath) || IsInsideAssets(jsonPath))
                AssetDatabase.Refresh();

            string summary = fbxMessage == null
                ? "Exported FBX + JSON to: " + directory
                : "JSON written to: " + directory + "\n" + fbxMessage;
            return new ExportResult(true, summary);
        }

        /// <summary>Returns null on success, or a human-readable reason on failure.</summary>
        static string ExportFbx(MeshData meshData, string name, string fbxPath)
        {
            Type exporterType = Type.GetType(FbxExporterType);
            if (exporterType == null)
            {
                return "FBX skipped: the 'com.unity.formats.fbx' package is not installed. "
                       + "Install it via the Package Manager to enable FBX export.";
            }

            MethodInfo exportMethod = exporterType.GetMethod(
                "ExportObject",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(UnityEngine.Object) },
                null);

            if (exportMethod == null)
                return "FBX skipped: the FBX exporter API (ModelExporter.ExportObject) was not found.";

            GameObject temp = null;
            Mesh mesh = null;
            try
            {
                mesh = meshData.ToMesh(name);
                temp = new GameObject(name);
                temp.transform.position = Vector3.zero;
                temp.AddComponent<MeshFilter>().sharedMesh = mesh;
                temp.AddComponent<MeshRenderer>();

                exportMethod.Invoke(null, new object[] { fbxPath, temp });
                return null;
            }
            catch (Exception e)
            {
                return "FBX export failed: " + (e.InnerException?.Message ?? e.Message);
            }
            finally
            {
                if (temp != null) UnityEngine.Object.DestroyImmediate(temp);
                if (mesh != null) UnityEngine.Object.DestroyImmediate(mesh);
            }
        }

        static bool IsInsideAssets(string absolutePath)
        {
            if (string.IsNullOrEmpty(absolutePath))
                return false;
            string full = Path.GetFullPath(absolutePath).Replace('\\', '/');
            string assets = Path.GetFullPath(Application.dataPath).Replace('\\', '/');
            return full.StartsWith(assets, StringComparison.OrdinalIgnoreCase);
        }
    }
}
