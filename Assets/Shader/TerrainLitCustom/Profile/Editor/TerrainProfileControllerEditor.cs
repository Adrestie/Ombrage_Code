// TerrainProfileControllerEditor.cs
// Inspecteur du contrôleur : champ profil + refs de scène + matériau résolu (lecture seule),
// puis embarque l'éditeur du profil (comme l'inspecteur Volume embarque son VolumeProfile).
using UnityEditor;
using UnityEngine;

namespace Ombrage.TerrainFeatures
{
    [CustomEditor(typeof(TerrainProfileController))]
    public class TerrainProfileControllerEditor : Editor
    {
        Editor m_ProfileEditor;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("profile"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("materialOverride"));

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Références de scène (déformation)", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("vehicleBody"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("wheels"), true);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("groundLayer"));

            serializedObject.ApplyModifiedProperties();

            var ctrl = (TerrainProfileController)target;

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(true))
                EditorGUILayout.ObjectField("Matériau résolu", ctrl.ResolvedMaterial, typeof(Material), false);

            EditorGUILayout.Space();
            if (ctrl.profile != null)
            {
                EditorGUILayout.LabelField("Profil", EditorStyles.boldLabel);
                CreateCachedEditor(ctrl.profile, null, ref m_ProfileEditor);
                if (m_ProfileEditor != null) m_ProfileEditor.OnInspectorGUI();
            }
            else
            {
                EditorGUILayout.HelpBox("Assigne un Terrain Profile pour éditer les modules.", MessageType.Info);
                if (GUILayout.Button("Nouveau profil…"))
                    CreateProfile(ctrl);
            }
        }

        void CreateProfile(TerrainProfileController ctrl)
        {
            string path = EditorUtility.SaveFilePanelInProject("Nouveau Terrain Profile", "TerrainProfile", "asset", "");
            if (string.IsNullOrEmpty(path)) return;
            var p = CreateInstance<TerrainProfile>();
            AssetDatabase.CreateAsset(p, path);
            AssetDatabase.SaveAssets();
            Undo.RecordObject(ctrl, "Assign Terrain Profile");
            ctrl.profile = p;
            EditorUtility.SetDirty(ctrl);
        }

        void OnDisable()
        {
            if (m_ProfileEditor != null) DestroyImmediate(m_ProfileEditor);
        }
    }
}
