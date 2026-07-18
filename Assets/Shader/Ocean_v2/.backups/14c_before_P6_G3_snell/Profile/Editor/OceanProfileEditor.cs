// OceanProfileEditor.cs
// Inspecteur du OceanProfile façon Volume HDRP : un repli par module (toggle Enable + titre +
// menu contextuel), bouton « Add Module ». Gère les modules en SOUS-ASSETS (add/remove/move).
// Calqué sur TerrainProfileEditor, simplifié : les modules P0 sont des stubs sans paramètre,
// donc on dessine les champs sérialisés restants via PropertyField (sans système d'override).
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Ombrage.OceanFeatures
{
    [CustomEditor(typeof(OceanProfile))]
    public class OceanProfileEditor : Editor
    {
        OceanProfile profile => target as OceanProfile;

        // Clé = EntityId (remplaçant de GetInstanceID, déprécié CS0618 en Unity 6000.4).
        readonly Dictionary<EntityId, bool> m_Expanded = new Dictionary<EntityId, bool>();

        static List<Type> s_ModuleTypes;
        static List<Type> ModuleTypes
        {
            get
            {
                if (s_ModuleTypes == null)
                {
                    s_ModuleTypes = new List<Type>();
                    foreach (var t in TypeCache.GetTypesDerivedFrom<OceanFeatureModule>())
                        if (!t.IsAbstract) s_ModuleTypes.Add(t);
                }
                return s_ModuleTypes;
            }
        }

        static string MenuPath(Type t)
        {
            var a = (OceanModuleMenuAttribute)Attribute.GetCustomAttribute(t, typeof(OceanModuleMenuAttribute));
            return a != null ? a.menuPath : t.Name;
        }

        bool GetExpanded(UnityEngine.Object m)
        {
            EntityId id = m.GetEntityId();
            return !m_Expanded.TryGetValue(id, out var v) || v;   // défaut = déplié
        }
        void SetExpanded(UnityEngine.Object m, bool v) => m_Expanded[m.GetEntityId()] = v;

        public override void OnInspectorGUI()
        {
            if (profile == null) return;

            EditorGUILayout.Space(2);
            if (profile.modules.Count == 0)
                EditorGUILayout.HelpBox("Aucun module. Utilise « Add Module » pour ajouter une feature océan.", MessageType.Info);

            for (int i = 0; i < profile.modules.Count; i++)
                DrawModule(i);

            EditorGUILayout.Space(6);
            var r = EditorGUILayout.GetControlRect(false, 24f);
            const float bw = 160f;
            var br = new Rect(r.x + (r.width - bw) * 0.5f, r.y, bw, r.height);
            if (GUI.Button(br, "Add Module"))
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

            Event e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && titleRect.Contains(e.mousePosition))
            {
                expanded = !expanded;
                e.Use();
            }
            SetExpanded(module, expanded);

            if (expanded)
            {
                EditorGUILayout.Space(2);
                using (new EditorGUI.DisabledScope(!activeProp.boolValue))
                {
                    // Dessine les champs sérialisés restants (hors 'active'/'m_Script').
                    // P0 : les stubs n'en ont aucun → corps vide (info).
                    var it = mso.GetIterator();
                    it.NextVisible(true); // m_Script
                    bool any = false;
                    while (it.NextVisible(false))
                    {
                        if (it.name == "active" || it.name == "m_Script") continue;
                        EditorGUILayout.PropertyField(it.Copy(), true);
                        any = true;
                    }
                    if (!any)
                        EditorGUILayout.LabelField("Stub P0 — aucun paramètre (implémentation en phase ultérieure).", EditorStyles.miniLabel);
                }
                EditorGUILayout.Space(2);
            }

            mso.ApplyModifiedProperties();
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

        void ShowModuleMenu(int index, OceanFeatureModule m)
        {
            var menu = new GenericMenu();
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
            var m = (OceanFeatureModule)CreateInstance(type);
            m.name = type.Name;
            Undo.RegisterCreatedObjectUndo(m, "Add Ocean Module");
            if (EditorUtility.IsPersistent(profile))
                AssetDatabase.AddObjectToAsset(m, profile);
            Undo.RecordObject(profile, "Add Ocean Module");
            profile.modules.Add(m);
            EditorUtility.SetDirty(profile);
            if (EditorUtility.IsPersistent(profile)) AssetDatabase.SaveAssetIfDirty(profile);
        }

        void RemoveModule(OceanFeatureModule m)
        {
            Undo.RecordObject(profile, "Remove Ocean Module");
            profile.modules.Remove(m);
            EditorUtility.SetDirty(profile);
            Undo.DestroyObjectImmediate(m);
            if (EditorUtility.IsPersistent(profile)) AssetDatabase.SaveAssetIfDirty(profile);
        }

        void MoveModule(int from, int to)
        {
            if (to < 0 || to >= profile.modules.Count) return;
            Undo.RecordObject(profile, "Move Ocean Module");
            var m = profile.modules[from];
            profile.modules.RemoveAt(from);
            profile.modules.Insert(to, m);
            EditorUtility.SetDirty(profile);
            if (EditorUtility.IsPersistent(profile)) AssetDatabase.SaveAssetIfDirty(profile);
        }
    }
}
