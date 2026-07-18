using Ombrage.StaticSystem.Editor;
using Ombrage.Systems.BatteryManager;
using UnityEngine;
using UnityEditor;

[CustomEditor(typeof(BatteryManager), true)]
public class BatteryManagerManagerEditor : StaticSystemBaseEditor 
{
	//Declare SerializedProperties here;
	//ie : SerializedProperty _sp;
	SerializedProperty baseBattery;
	SerializedProperty currentBattery;
	SerializedProperty batteryExtensionSlot;
	SerializedProperty batteryExtensionAmountPerSlot;


    public override void OnEnable() 
	{
		base.OnEnable();
		//Reference SerializedProperties here;
		//ie : _sp = serializedObject.FindProperty("PROPERTY_NAME");

		baseBattery = serializedObject.FindProperty("baseBattery");
        currentBattery = serializedObject.FindProperty("currentBattery");

        batteryExtensionSlot = serializedObject.FindProperty("batteryExtensionSlot");
        batteryExtensionAmountPerSlot = serializedObject.FindProperty("batteryExtensionAmountPerSlot");
    }

    public override void OnInspectorGUI()
	{
		BaseInspector();

		//Use custom Properties here;
		//ie : EditorGUILayout.PropertyField(_sp);

		EditorGUILayout.PropertyField(baseBattery);
		EditorGUILayout.PropertyField(currentBattery);

		EditorGUILayout.PropertyField(batteryExtensionSlot);
		EditorGUILayout.PropertyField(batteryExtensionAmountPerSlot);
        serializedObject.ApplyModifiedProperties();
	}
}