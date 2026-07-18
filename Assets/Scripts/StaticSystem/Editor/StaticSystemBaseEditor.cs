using System;
using System.Collections;
using System.Collections.Generic;
using Ombrage.StaticSystem;
using UnityEngine;
using UnityEditor;

namespace Ombrage.StaticSystem.Editor
{
public class StaticSystemBaseEditor : UnityEditor.Editor
{
    public SerializedProperty _status;
    public SerializedProperty _serializationMethode;
    public SerializedProperty _canBeCleared;
    public SerializedProperty _description;


    public virtual void OnEnable()
    {
        _serializationMethode = serializedObject.FindProperty("_serializationMethode");
        _status = serializedObject.FindProperty("_status");
        _canBeCleared = serializedObject.FindProperty("_canBeCleared");
        _description = serializedObject.FindProperty("_description");
    }

    public void BaseInspector()
    {
        using (var hb = new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
        {
            EditorGUILayout.LabelField("Base Settings");
            GUILayout.Space(10);
            EditorGUILayout.PropertyField(_serializationMethode);
            if (_serializationMethode.intValue == 0)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Import from JSON"))
                {
                    Tuple<bool, string> _r = ((StaticSystemBase)serializedObject.targetObject).Import();
                    if (_r.Item1)
                    {
                        Debug.Log("JSON imported for : " + serializedObject.targetObject);
                        serializedObject.Update();
                    }
                    else
                    {
                        Debug.Log("JSON import error for : " + serializedObject.targetObject + ", " + _r.Item2);
                    }
                }

                if (GUILayout.Button("Save to JSON"))
                {
                    Tuple<bool, string> _r = ((StaticSystemBase)serializedObject.targetObject).Export();

                    if (_r.Item1)
                        Debug.Log("JSON exported for : " + serializedObject.targetObject);
                    else
                        Debug.Log("JSON export error for : " + serializedObject.targetObject + ", " + _r.Item2);
                }

                EditorGUILayout.EndHorizontal();
                GUILayout.Space(10);
            }

            GUI.enabled = false;
            EditorGUILayout.PropertyField(_status);
            GUI.enabled = true;
            EditorGUILayout.PropertyField(_canBeCleared);
            EditorGUILayout.PropertyField(_description);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
}
