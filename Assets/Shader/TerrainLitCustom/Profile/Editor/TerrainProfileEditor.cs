// TerrainProfileEditor.cs
// Inspecteur du TerrainProfile façon Volume HDRP : un repli par module (toggle Enable + titre +
// menu contextuel), cases « override » par paramètre, bouton « Add Override ».
// Gère les modules en SOUS-ASSETS du profil (add/remove/move).
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    [CustomEditor(typeof(TerrainProfile))]
    public class TerrainProfileEditor : Editor
    {
        TerrainProfile profile => target as TerrainProfile;

        readonly Dictionary<int, bool> m_Expanded = new Dictionary<int, bool>();

        static List<Type> s_ModuleTypes;
        static List<Type> ModuleTypes
        {
            get
            {
                if (s_ModuleTypes == null)
                {
                    s_ModuleTypes = new List<Type>();
                    foreach (var t in TypeCache.GetTypesDerivedFrom<TerrainFeatureModule>())
                        if (!t.IsAbstract) s_ModuleTypes.Add(t);
                }
                return s_ModuleTypes;
            }
        }

        static string MenuPath(Type t)
        {
            var a = (TerrainModuleMenuAttribute)Attribute.GetCustomAttribute(t, typeof(TerrainModuleMenuAttribute));
            return a != null ? a.menuPath : t.Name;
        }

        bool GetExpanded(UnityEngine.Object m)
        {
            int id = m.GetInstanceID();
            return !m_Expanded.TryGetValue(id, out var v) || v;   // défaut = déplié
        }
        void SetExpanded(UnityEngine.Object m, bool v) => m_Expanded[m.GetInstanceID()] = v;

        public override void OnInspectorGUI()
        {
            if (profile == null) return;

            EditorGUILayout.Space(2);
            if (profile.modules.Count == 0)
                EditorGUILayout.HelpBox("Aucun module. Utilise « Add Override » pour ajouter une feature terrain.", MessageType.Info);

            for (int i = 0; i < profile.modules.Count; i++)
                DrawModule(i);

            EditorGUILayout.Space(6);
            var r = EditorGUILayout.GetControlRect(false, 24f);
            const float bw = 160f;
            var br = new Rect(r.x + (r.width - bw) * 0.5f, r.y, bw, r.height);
            if (GUI.Button(br, "Add Override"))
                ShowAddMenu();
        }

        void DrawModule(int index)
        {
            var module = profile.modules[index];
            if (module == null)
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField("(module manquant — script supprimé ?)");
                if (GUILayout.Button("Remove", GUILayout.Width(70)))
                {
                    Undo.RecordObject(profile, "Remove Module");
                    profile.modules.RemoveAt(index);
                    EditorUtility.SetDirty(profile);
                }
                EditorGUILayout.EndHorizontal();
                return;
            }

            var mso = new SerializedObject(module);
            mso.Update();

            // --- Header (rects manuels : évite le chevauchement flèche foldout / toggle Enable) ---
            var activeProp = mso.FindProperty("active");
            bool expanded = GetExpanded(module);

            Rect hr = EditorGUILayout.GetControlRect(false, 20f);
            EditorGUI.DrawRect(hr, new Color(0f, 0f, 0f, 0.12f));

            Rect foldRect = new Rect(hr.x + 2f, hr.y + 2f, 13f, hr.height);
            Rect togRect = new Rect(hr.x + 18f, hr.y + 2f, 16f, 16f);
            Rect titleRect = new Rect(hr.x + 38f, hr.y, hr.width - 38f - 26f, hr.height);
            Rect menuRect = new Rect(hr.xMax - 24f, hr.y + 1f, 22f, 18f);

            expanded = EditorGUI.Foldout(foldRect, expanded, GUIContent.none, true);
            activeProp.boolValue = EditorGUI.Toggle(togRect, activeProp.boolValue);
            EditorGUI.LabelField(titleRect, module.DisplayName, EditorStyles.boldLabel);
            if (GUI.Button(menuRect, "⋮", EditorStyles.miniButton))
                ShowModuleMenu(index, module);

            // Clic sur le titre = plier/déplier.
            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && titleRect.Contains(e.mousePosition))
            {
                expanded = !expanded;
                e.Use();
            }
            SetExpanded(module, expanded);

            // --- Body ---
            if (expanded)
            {
                EditorGUILayout.Space(2);
                // Module inactif → tout le corps grisé (params + cases override). L'en-tête reste actif.
                using (new EditorGUI.DisabledScope(!activeProp.boolValue))
                {
                    var it = mso.GetIterator();
                    it.NextVisible(true); // m_Script
                    while (it.NextVisible(false))
                    {
                        if (it.name == "active" || it.name == "m_Script") continue;
                        DrawParameter(it.Copy());
                    }
                }
                EditorGUILayout.Space(2);
            }

            mso.ApplyModifiedProperties();
        }

        void DrawParameter(SerializedProperty paramProp)
        {
            var ovr = paramProp.FindPropertyRelative("overrideState");
            var val = paramProp.FindPropertyRelative("value");

            if (ovr == null || val == null)
            {
                // Champ non standard : indenté, sans case override.
                var rNo = EditorGUILayout.GetControlRect(true, EditorGUI.GetPropertyHeight(paramProp, true));
                rNo.xMin += 22f;
                EditorGUI.PropertyField(rNo, paramProp, true);
                return;
            }

            var min = paramProp.FindPropertyRelative("min");
            var max = paramProp.FindPropertyRelative("max");
            var label = new GUIContent(ObjectNames.NicifyVariableName(paramProp.name));

            Rect row = EditorGUILayout.GetControlRect(true, EditorGUIUtility.singleLineHeight);

            // Case override : marge gauche, TOUJOURS active (hors DisabledScope) → cliquable.
            var ovrRect = new Rect(row.x + 2f, row.y + 1f, 14f, 14f);
            ovr.boolValue = EditorGUI.Toggle(ovrRect, ovr.boolValue);

            // Champ valeur : décalé à droite de la case.
            var fieldRect = new Rect(row.x + 22f, row.y, row.width - 22f, row.height);
            using (new EditorGUI.DisabledScope(!ovr.boolValue))
            {
                if (val.isArray && val.arrayElementType == "bool")
                {
                    DrawLayerToggles(fieldRect, val, label);
                }
                else if (min != null && max != null)
                {
                    if (val.propertyType == SerializedPropertyType.Integer)
                        val.intValue = EditorGUI.IntSlider(fieldRect, label, val.intValue, min.intValue, max.intValue);
                    else
                        val.floatValue = EditorGUI.Slider(fieldRect, label, val.floatValue, min.floatValue, max.floatValue);
                }
                else if (min != null && val.propertyType == SerializedPropertyType.Float)
                {
                    float v = EditorGUI.FloatField(fieldRect, label, val.floatValue);
                    val.floatValue = Mathf.Max(v, min.floatValue);
                }
                else
                {
                    EditorGUI.PropertyField(fieldRect, val, label, true);
                }
            }
        }

        static void DrawLayerToggles(Rect rect, SerializedProperty arr, GUIContent label)
        {
            if (arr.arraySize != 8) arr.arraySize = 8;
            rect = EditorGUI.PrefixLabel(rect, label);
            float w = rect.width / 8f;
            for (int i = 0; i < 8; i++)
            {
                var er = new Rect(rect.x + i * w, rect.y, w, rect.height);
                var el = arr.GetArrayElementAtIndex(i);
                el.boolValue = GUI.Toggle(er, el.boolValue, i.ToString(), EditorStyles.miniButton);
            }
        }

        // --- Menus ---
        void ShowAddMenu()
        {
            var menu = new GenericMenu();
            if (ModuleTypes.Count == 0)
                menu.AddDisabledItem(new GUIContent("Aucun module disponible"));
            foreach (var t in ModuleTypes)
            {
                var path = new GUIContent(MenuPath(t));
                if (profile.Has(t)) menu.AddDisabledItem(path);
                else { var captured = t; menu.AddItem(path, false, () => AddModule(captured)); }
            }
            menu.ShowAsContext();
        }

        void ShowModuleMenu(int index, TerrainFeatureModule m)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Override All"), false, () => { Undo.RecordObject(m, "Override All"); m.SetAllOverrides(true); EditorUtility.SetDirty(m); });
            menu.AddItem(new GUIContent("Override None"), false, () => { Undo.RecordObject(m, "Override None"); m.SetAllOverrides(false); EditorUtility.SetDirty(m); });
            menu.AddSeparator("");
            if (index > 0) menu.AddItem(new GUIContent("Move Up"), false, () => MoveModule(index, index - 1));
            else menu.AddDisabledItem(new GUIContent("Move Up"));
            if (index < profile.modules.Count - 1) menu.AddItem(new GUIContent("Move Down"), false, () => MoveModule(index, index + 1));
            else menu.AddDisabledItem(new GUIContent("Move Down"));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Remove"), false, () => RemoveModule(m));
            menu.ShowAsContext();
        }

        // --- Sous-assets ---
        void AddModule(Type type)
        {
            var m = (TerrainFeatureModule)CreateInstance(type);
            m.name = type.Name;
            Undo.RegisterCreatedObjectUndo(m, "Add Terrain Module");
            if (EditorUtility.IsPersistent(profile))
                AssetDatabase.AddObjectToAsset(m, profile);
            Undo.RecordObject(profile, "Add Terrain Module");
            profile.modules.Add(m);
            EditorUtility.SetDirty(profile);
            if (EditorUtility.IsPersistent(profile)) AssetDatabase.SaveAssetIfDirty(profile);
        }

        void RemoveModule(TerrainFeatureModule m)
        {
            Undo.RecordObject(profile, "Remove Terrain Module");
            profile.modules.Remove(m);
            EditorUtility.SetDirty(profile);
            Undo.DestroyObjectImmediate(m);
            if (EditorUtility.IsPersistent(profile)) AssetDatabase.SaveAssetIfDirty(profile);
        }

        void MoveModule(int from, int to)
        {
            if (to < 0 || to >= profile.modules.Count) return;
            Undo.RecordObject(profile, "Move Terrain Module");
            var m = profile.modules[from];
            profile.modules.RemoveAt(from);
            profile.modules.Insert(to, m);
            EditorUtility.SetDirty(profile);
            if (EditorUtility.IsPersistent(profile)) AssetDatabase.SaveAssetIfDirty(profile);
        }
    }
}
