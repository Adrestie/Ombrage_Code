// Drawers for the per-layer (0-7) inspector widgets used by the grass system.
//
//  - LayerToggleParameter : a row of 0-7 mini toggle buttons (same look as the module's "Layers").
//  - LayerDensity (multi-species) : a row of 0-7 toggle buttons that decide whether the species grows
//    on each Terrain layer; for every ENABLED layer a "Layer Density N" slider appears below it.
//    Raising one species' density on a layer past the layer's budget of 1 scales the OTHER species'
//    densities on that layer down proportionally (the species share the budget).
using UnityEditor;
using UnityEngine;
using Ombrage.TerrainFeatures;

[CustomPropertyDrawer(typeof(LayerToggleParameter))]
public class LayerToggleParameterDrawer : PropertyDrawer
{
    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var arr = property.FindPropertyRelative("value");
        if (arr == null || !arr.isArray) { EditorGUI.PropertyField(position, property, label, true); return; }

        if (arr.arraySize != 8) arr.arraySize = 8;
        EditorGUI.BeginProperty(position, label, property);
        position = EditorGUI.PrefixLabel(position, label);
        float w = position.width / 8f;
        for (int i = 0; i < 8; i++)
        {
            var er = new Rect(position.x + i * w, position.y, w, position.height);
            var el = arr.GetArrayElementAtIndex(i);
            el.boolValue = GUI.Toggle(er, el.boolValue, i.ToString(), EditorStyles.miniButton);
        }
        EditorGUI.EndProperty();
    }

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        => EditorGUIUtility.singleLineHeight;
}

[CustomPropertyDrawer(typeof(LayerDensity))]
public class LayerDensityDrawer : PropertyDrawer
{
    const int N = 8;

    public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
    {
        float line = EditorGUIUtility.singleLineHeight, pad = 2f;
        var en = property.FindPropertyRelative("enabled");
        if (en == null || !en.isArray || en.arraySize != N) return line;
        int enabled = 0;
        for (int i = 0; i < N; i++) if (en.GetArrayElementAtIndex(i).boolValue) enabled++;
        return line + pad + enabled * (line + pad); // toggles row + one slider per ENABLED layer
    }

    public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
    {
        var en  = property.FindPropertyRelative("enabled");
        var val = property.FindPropertyRelative("value");
        if (en == null || val == null || !en.isArray) { EditorGUI.PropertyField(position, property, label, true); return; }
        if (en.arraySize != N)  en.arraySize = N;
        if (val.arraySize != N) val.arraySize = N;

        float line = EditorGUIUtility.singleLineHeight, pad = 2f;
        EditorGUI.BeginProperty(position, label, property);

        // --- Row of layer toggles (decide whether the species grows on each layer) ---
        var togRow = new Rect(position.x, position.y, position.width, line);
        var cells = EditorGUI.PrefixLabel(togRow, new GUIContent(label.text + " (couches)"));
        float w = cells.width / N;
        for (int i = 0; i < N; i++)
        {
            var er = new Rect(cells.x + i * w, cells.y, w, line);
            bool on = en.GetArrayElementAtIndex(i).boolValue;
            bool now = GUI.Toggle(er, on, i.ToString(), EditorStyles.miniButton);
            if (now != on)
            {
                en.GetArrayElementAtIndex(i).boolValue = now;
                if (now) SetDensity(property, i, DefaultOnValue(property, i)); // enable -> remaining budget
                // disable: keep the slider value as-is (the enabled flag alone hides/zeroes it).
            }
        }

        // --- One "Layer Density N" slider per ENABLED layer, below (a 0 slider does NOT disable it) ---
        float y = position.y + line + pad;
        for (int i = 0; i < N; i++)
        {
            if (!en.GetArrayElementAtIndex(i).boolValue) continue;
            var sr = new Rect(position.x, y, position.width, line);
            EditorGUI.BeginChangeCheck();
            float nv = EditorGUI.Slider(sr, "Layer Density " + i, val.GetArrayElementAtIndex(i).floatValue, 0f, 1f);
            if (EditorGUI.EndChangeCheck()) SetDensity(property, i, nv);
            y += line + pad;
        }

        EditorGUI.EndProperty();
    }

    static float Round3(float x) => Mathf.Round(x * 1000f) / 1000f; // 3-digit precision

    // Set this species' density on `layer`, then scale the OTHER ENABLED species on that layer down
    // proportionally so the layer total stays <= 1 (shared budget). All values quantized to 0.001.
    static void SetDensity(SerializedProperty myLayerDensity, int layer, float val)
    {
        val = Round3(Mathf.Clamp01(val));
        myLayerDensity.FindPropertyRelative("value").GetArrayElementAtIndex(layer).floatValue = val;

        int myIndex = ParseSpeciesIndex(myLayerDensity.propertyPath);
        var speciesArr = myLayerDensity.serializedObject.FindProperty("species");
        if (speciesArr == null || !speciesArr.isArray || myIndex < 0) return;

        float others = 0f;
        for (int s = 0; s < speciesArr.arraySize; s++)
        {
            if (s == myIndex) continue;
            var ov = SiblingLayerValue(speciesArr, s, layer);
            if (ov != null) others += ov.floatValue;
        }
        if (val + others > 1f + 1e-4f && others > 1e-5f)
        {
            float scale = Mathf.Max(0f, 1f - val) / others;
            for (int s = 0; s < speciesArr.arraySize; s++)
            {
                if (s == myIndex) continue;
                var ov = SiblingLayerValue(speciesArr, s, layer);
                if (ov != null) ov.floatValue = Round3(ov.floatValue * scale);
            }
        }
    }

    // Toggling a layer ON gives the species the layer's remaining budget (fair share).
    static float DefaultOnValue(SerializedProperty myLayerDensity, int layer)
    {
        int myIndex = ParseSpeciesIndex(myLayerDensity.propertyPath);
        var speciesArr = myLayerDensity.serializedObject.FindProperty("species");
        if (speciesArr == null || myIndex < 0) return 1f;
        float others = 0f;
        for (int s = 0; s < speciesArr.arraySize; s++)
        {
            if (s == myIndex) continue;
            var ov = SiblingLayerValue(speciesArr, s, layer);
            if (ov != null) others += ov.floatValue;
        }
        float rem = 1f - others;
        return Round3(rem > 0.05f ? rem : 0.5f);
    }

    // The density of sibling species `s` on `layer` — only if that layer is ENABLED on the sibling.
    static SerializedProperty SiblingLayerValue(SerializedProperty speciesArr, int s, int layer)
    {
        var ld = speciesArr.GetArrayElementAtIndex(s).FindPropertyRelative("layerDensity");
        if (ld == null) return null;
        var en = ld.FindPropertyRelative("enabled");
        var val = ld.FindPropertyRelative("value");
        if (en == null || val == null || !en.isArray || layer >= en.arraySize || layer >= val.arraySize) return null;
        if (!en.GetArrayElementAtIndex(layer).boolValue) return null; // disabled -> not part of the budget
        return val.GetArrayElementAtIndex(layer);
    }

    // "species.Array.data[3].layerDensity" -> 3
    static int ParseSpeciesIndex(string path)
    {
        int a = path.IndexOf("data[");
        if (a < 0) return -1;
        a += 5;
        int b = path.IndexOf(']', a);
        return (b > a && int.TryParse(path.Substring(a, b - a), out int idx)) ? idx : -1;
    }
}
