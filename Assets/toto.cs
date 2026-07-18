using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public class Toto
{
    static float lastLogTime = 0f;

    static Toto()
    {
        EditorApplication.update += OnEditorUpdate;
    }

    static void OnEditorUpdate()
    {
        if (Time.realtimeSinceStartup - lastLogTime >= 1f)
        {
            Debug.Log("coucou");
            lastLogTime = Time.realtimeSinceStartup;
        }
    }
}
