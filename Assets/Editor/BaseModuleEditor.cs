using System.Reflection;
using UnityEditor;
using UnityEngine;
using Ombrage.Modules;

[CustomEditor(typeof(BaseModule), true)]
public class BaseModuleEditor : Editor
{
	private static readonly string[] drainModeFields = { "energyCost", "consumptionInterval", "consumptionCurve" };

	public override void OnInspectorGUI()
	{
		serializedObject.Update();

		DrawPropertiesExcluding(serializedObject, drainModeFields);

		var drainMode = (BaseModule.DrainMode)serializedObject.FindProperty("drainMode").enumValueIndex;

		switch (drainMode)
		{
			case BaseModule.DrainMode.Spontaneous:
			case BaseModule.DrainMode.Continuous:
				EditorGUILayout.PropertyField(serializedObject.FindProperty("energyCost"));
				break;
			case BaseModule.DrainMode.Intermittent:
				EditorGUILayout.PropertyField(serializedObject.FindProperty("energyCost"));
				EditorGUILayout.PropertyField(serializedObject.FindProperty("consumptionInterval"));
				break;
			case BaseModule.DrainMode.Curve:
				EditorGUILayout.PropertyField(serializedObject.FindProperty("consumptionCurve"));
				break;
		}

		serializedObject.ApplyModifiedProperties();
	}
}
