using Ombrage.StaticSystem.Editor;
using Ombrage.Systems.Inputs;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(InputsManager), true)]
public class InputsManagerEditor : StaticSystemBaseEditor 
{
	//Declare SerializedProperties here;
	//ie : SerializedProperty _sp;

	public override void OnEnable() 
	{
		base.OnEnable();
		//Reference SerializedProperties here;
		//ie : _sp = serializedObject.FindProperty("PROPERTY_NAME");

	}

	public override void OnInspectorGUI()
	{
		BaseInspector();

		//Use custom Properties here;
		//ie : EditorGUILayout.PropertyField(_sp);

		serializedObject.ApplyModifiedProperties();
	}
}