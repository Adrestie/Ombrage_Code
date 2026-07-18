// TerrainMaskMapPacker.cs
// Editor utility: packs individual channels (or CSNOH textures) into
// the HDRP Terrain Layer Mask Map format (R=Metallic, G=AO, B=Height, A=Smoothness).
//
// Usage: Window → Terrain Tools → Mask Map Packer
// ---------------------------------------------------------------------------------

#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;
using System.IO;

public class TerrainMaskMapPacker : EditorWindow
{
    // Input textures — assign individual channels
    Texture2D smoothnessMap;
    Texture2D occlusionMap;
    Texture2D heightMap;
    Texture2D metallicMap;

    // Channel source selection for packed CSNOH textures
    enum ChannelSource { R, G, B, A }

    ChannelSource smoothnessChannel = ChannelSource.A;
    ChannelSource occlusionChannel  = ChannelSource.R;
    ChannelSource heightChannel     = ChannelSource.R;
    ChannelSource metallicChannel   = ChannelSource.R;

    // Default values when no texture is assigned
    float defaultMetallic   = 0f;
    float defaultSmoothness = 0.5f;
    float defaultAO         = 1f;
    float defaultHeight     = 0.5f;

    int outputResolution = 1024;
    string outputName    = "MaskMap";

    Vector2 scrollPos;

    [MenuItem("Window/Terrain Tools/Mask Map Packer")]
    static void ShowWindow()
    {
        var window = GetWindow<TerrainMaskMapPacker>("Mask Map Packer");
        window.minSize = new Vector2(350, 500);
    }

    void OnGUI()
    {
        scrollPos = EditorGUILayout.BeginScrollView(scrollPos);

        EditorGUILayout.LabelField("HDRP Terrain Mask Map Packer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Pack your CSNOH textures into the HDRP Mask Map format:\n" +
            "  R = Metallic\n" +
            "  G = AO (Occlusion)\n" +
            "  B = Height\n" +
            "  A = Smoothness",
            MessageType.Info);

        EditorGUILayout.Space(10);

        // --- Metallic ---
        EditorGUILayout.LabelField("Metallic → Mask Map R", EditorStyles.boldLabel);
        metallicMap = (Texture2D)EditorGUILayout.ObjectField("Metallic Texture", metallicMap, typeof(Texture2D), false);
        if (metallicMap != null)
            metallicChannel = (ChannelSource)EditorGUILayout.EnumPopup("Source Channel", metallicChannel);
        else
            defaultMetallic = EditorGUILayout.Slider("Default Value", defaultMetallic, 0f, 1f);

        EditorGUILayout.Space(5);

        // --- AO / Occlusion ---
        EditorGUILayout.LabelField("Occlusion (AO) → Mask Map G", EditorStyles.boldLabel);
        occlusionMap = (Texture2D)EditorGUILayout.ObjectField("Occlusion Texture", occlusionMap, typeof(Texture2D), false);
        if (occlusionMap != null)
            occlusionChannel = (ChannelSource)EditorGUILayout.EnumPopup("Source Channel", occlusionChannel);
        else
            defaultAO = EditorGUILayout.Slider("Default Value", defaultAO, 0f, 1f);

        EditorGUILayout.Space(5);

        // --- Height ---
        EditorGUILayout.LabelField("Height → Mask Map B", EditorStyles.boldLabel);
        heightMap = (Texture2D)EditorGUILayout.ObjectField("Height Texture", heightMap, typeof(Texture2D), false);
        if (heightMap != null)
            heightChannel = (ChannelSource)EditorGUILayout.EnumPopup("Source Channel", heightChannel);
        else
            defaultHeight = EditorGUILayout.Slider("Default Value", defaultHeight, 0f, 1f);

        EditorGUILayout.Space(5);

        // --- Smoothness ---
        EditorGUILayout.LabelField("Smoothness → Mask Map A", EditorStyles.boldLabel);
        smoothnessMap = (Texture2D)EditorGUILayout.ObjectField("Smoothness Texture", smoothnessMap, typeof(Texture2D), false);
        if (smoothnessMap != null)
            smoothnessChannel = (ChannelSource)EditorGUILayout.EnumPopup("Source Channel", smoothnessChannel);
        else
            defaultSmoothness = EditorGUILayout.Slider("Default Value", defaultSmoothness, 0f, 1f);

        EditorGUILayout.Space(15);

        // --- Output ---
        EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
        outputResolution = EditorGUILayout.IntPopup("Resolution",
            outputResolution,
            new string[] { "256", "512", "1024", "2048", "4096" },
            new int[] { 256, 512, 1024, 2048, 4096 });
        outputName = EditorGUILayout.TextField("File Name", outputName);

        EditorGUILayout.Space(10);

        if (GUILayout.Button("Pack Mask Map", GUILayout.Height(35)))
        {
            PackMaskMap();
        }

        EditorGUILayout.EndScrollView();
    }

    void PackMaskMap()
    {
        int res = outputResolution;
        Texture2D output = new Texture2D(res, res, TextureFormat.RGBA32, true, true); // linear

        // Make input textures readable
        Texture2D readMetallic   = MakeReadable(metallicMap, res);
        Texture2D readOcclusion  = MakeReadable(occlusionMap, res);
        Texture2D readHeight     = MakeReadable(heightMap, res);
        Texture2D readSmoothness = MakeReadable(smoothnessMap, res);

        Color[] pixels = new Color[res * res];

        for (int y = 0; y < res; y++)
        {
            for (int x = 0; x < res; x++)
            {
                float u = (float)x / res;
                float v = (float)y / res;

                float metallic   = readMetallic   != null ? GetChannel(readMetallic.GetPixelBilinear(u, v), metallicChannel)   : defaultMetallic;
                float ao         = readOcclusion  != null ? GetChannel(readOcclusion.GetPixelBilinear(u, v), occlusionChannel)  : defaultAO;
                float height     = readHeight     != null ? GetChannel(readHeight.GetPixelBilinear(u, v), heightChannel)        : defaultHeight;
                float smoothness = readSmoothness != null ? GetChannel(readSmoothness.GetPixelBilinear(u, v), smoothnessChannel) : defaultSmoothness;

                pixels[y * res + x] = new Color(metallic, ao, height, smoothness);
            }
        }

        output.SetPixels(pixels);
        output.Apply();

        // Save
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Mask Map", outputName, "png", "Choose where to save the packed Mask Map");

        if (!string.IsNullOrEmpty(path))
        {
            byte[] pngData = output.EncodeToPNG();
            File.WriteAllBytes(path, pngData);
            AssetDatabase.Refresh();

            // Set import settings: linear, no sRGB, no compression for accuracy
            TextureImporter importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer != null)
            {
                importer.sRGBTexture = false; // Mask map must be linear
                importer.textureCompression = TextureImporterCompression.CompressedHQ;
                importer.SaveAndReimport();
            }

            Debug.Log($"Mask Map saved to {path} ({res}x{res}, linear)");
            EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Texture2D>(path));
        }

        // Cleanup temp textures
        if (readMetallic   != metallicMap   && readMetallic   != null) DestroyImmediate(readMetallic);
        if (readOcclusion  != occlusionMap  && readOcclusion  != null) DestroyImmediate(readOcclusion);
        if (readHeight     != heightMap     && readHeight     != null) DestroyImmediate(readHeight);
        if (readSmoothness != smoothnessMap && readSmoothness != null) DestroyImmediate(readSmoothness);
        DestroyImmediate(output);
    }

    float GetChannel(Color color, ChannelSource channel)
    {
        switch (channel)
        {
            case ChannelSource.R: return color.r;
            case ChannelSource.G: return color.g;
            case ChannelSource.B: return color.b;
            case ChannelSource.A: return color.a;
            default: return 0;
        }
    }

    Texture2D MakeReadable(Texture2D source, int targetRes)
    {
        if (source == null) return null;

        RenderTexture rt = RenderTexture.GetTemporary(targetRes, targetRes, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        Graphics.Blit(source, rt);

        RenderTexture prev = RenderTexture.active;
        RenderTexture.active = rt;

        Texture2D readable = new Texture2D(targetRes, targetRes, TextureFormat.RGBA32, false, true);
        readable.ReadPixels(new Rect(0, 0, targetRes, targetRes), 0, 0);
        readable.Apply();

        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        return readable;
    }
}
#endif
