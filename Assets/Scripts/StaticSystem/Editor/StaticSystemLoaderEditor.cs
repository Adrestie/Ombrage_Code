using Ombrage.StaticSystem;
using UnityEditor;
using UnityEngine;

namespace Ombrage.StaticSystem.Editor
{
public class StaticSystemLoaderEditor : EditorWindow
{
    static UnityEditor.Editor staticLoaderEditor;
    static StaticSystemLoaderObject staticLoader;

    [MenuItem("Window/Ombrage Tools/System/System Loader")]
    static void Init()
    {
        // Get existing open window or if none, make a new one:
        StaticSystemLoaderEditor window = (StaticSystemLoaderEditor)EditorWindow.GetWindow(typeof(StaticSystemLoaderEditor));
        staticLoader = Resources.Load("SystemLoaderList") as StaticSystemLoaderObject;
        staticLoaderEditor = UnityEditor.Editor.CreateEditor(staticLoader);
        window.Show();
    }

    public void OnGUI()
    {
        if (staticLoader == null || staticLoaderEditor == null)
        {
            staticLoader = Resources.Load("SystemLoaderList") as StaticSystemLoaderObject;
            staticLoaderEditor = UnityEditor.Editor.CreateEditor(staticLoader);
        }

        EditorGUILayout.LabelField("Gère le chargement des différents système dans cet ordre spécifique", EditorStyles.helpBox);
        staticLoaderEditor.OnInspectorGUI();
    }
}
}
