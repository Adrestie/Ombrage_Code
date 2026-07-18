namespace Ombrage.Systems.DayNight
{
	using Ombrage.StaticSystem.Editor;
	using UnityEngine;
	using UnityEditor;
	[CustomEditor(typeof(DayNightManager), true)]
    public class DayNightManagerEditor : StaticSystemBaseEditor
    {
        SerializedProperty cycleDurationInSeconds;
        SerializedProperty startTimeNormalized;
        SerializedProperty currentTimeNormalized;
        SerializedProperty timeScale;
        SerializedProperty dayAmbientColor;
        SerializedProperty nightAmbientColor;

        public override void OnEnable()
        {
            base.OnEnable();

            cycleDurationInSeconds = serializedObject.FindProperty("cycleDurationInSeconds");
            startTimeNormalized = serializedObject.FindProperty("startTimeNormalized");
            currentTimeNormalized = serializedObject.FindProperty("currentTimeNormalized");
            timeScale = serializedObject.FindProperty("timeScale");
            dayAmbientColor = serializedObject.FindProperty("dayAmbientColor");
            nightAmbientColor = serializedObject.FindProperty("nightAmbientColor");
        }

        public override void OnInspectorGUI()
        {
            BaseInspector();

            EditorGUILayout.LabelField("Temps", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(cycleDurationInSeconds);
            EditorGUILayout.PropertyField(startTimeNormalized);
            EditorGUILayout.PropertyField(currentTimeNormalized);
            EditorGUILayout.PropertyField(timeScale);

            // Affichage de l'heure en play mode
            if (Application.isPlaying && DayNightManager.Instance != null)
            {
                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    $"Heure actuelle : {DayNightManager.Instance.CurrentTimeFormatted}\n" +
                    $"Période : {(DayNightManager.Instance.IsDay ? "Jour" : "Nuit")}",
                    MessageType.Info);
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Ambient", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(dayAmbientColor);
            EditorGUILayout.PropertyField(nightAmbientColor);

            serializedObject.ApplyModifiedProperties();
        }
    }
}