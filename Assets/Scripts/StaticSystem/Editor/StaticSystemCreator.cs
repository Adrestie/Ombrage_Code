using System.IO;
using UnityEngine;
using UnityEditor;
using System;
using Ombrage.StaticSystem;

namespace Ombrage.StaticSystem.Editor
{
    public class StaticSystemCreator : EditorWindow
    {
        static StaticSystemCreator instance;

        string SystemName;
        string SelectedPath;
        string Namespace;

        static string _AssetSelectedPath;
        static string _AssetFolderPath;
        static string _AssetEditorPath;
        static string _AssetObjectPath;
        static string _assetClassPath;
        static string _runtimeAsmdefPath;
        static string _editorAsmdefPath;

        //Templates
        string _systemTemplate;
        string _runtimeAssemblyTemplate;
        string _editorTemplate;
        string _EditorAssemblyTemplate;
        string _runtimeAssemblyGUID;


        [MenuItem("Window/Ombrage Tools/System/System Creator")]
        static void Init()
        {
            // Get existing open window or if none, make a new one:
            StaticSystemCreator window = (StaticSystemCreator)EditorWindow.GetWindow(typeof(StaticSystemCreator));
            instance = window;
            window.Show();
        }

        public void OnGUI()
        {
            SystemName = EditorGUILayout.TextField(new GUIContent("System name : "), SystemName);//nom du système
            GUI.enabled = false;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.TextField(new GUIContent(" "), SystemName + "Manager");
            EditorGUILayout.EndHorizontal();
            GUI.enabled = true;
            EditorGUI.BeginChangeCheck();
            EditorGUILayout.BeginHorizontal();
            SelectedPath = EditorGUILayout.TextField(new GUIContent("System location: "), SelectedPath);//Chemin global du système



            if (GUILayout.Button(EditorGUIUtility.IconContent("d_Project@2x"), GUILayout.Width(35), GUILayout.Height(35)))
            {
                if (string.IsNullOrEmpty(SelectedPath) || string.IsNullOrWhiteSpace(SelectedPath))
                    SelectedPath = Application.dataPath;

                SelectedPath = EditorUtility.OpenFolderPanel("Select a destination folder", SelectedPath, "");
            }
            EditorGUILayout.EndHorizontal();


            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("Namespace : ", GUILayout.Width(85));
            GUI.enabled = false;
            Namespace = EditorGUILayout.TextField("Ombrage.Systems." + SystemName);
            GUI.enabled = true;
            //Namespace = EditorGUILayout.TextField(Namespace);//Chemin global du système
            EditorGUILayout.EndHorizontal();

            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }

            if (string.IsNullOrWhiteSpace(SystemName) || string.IsNullOrWhiteSpace(Namespace) || string.IsNullOrWhiteSpace(SelectedPath))
                GUI.enabled = false;
            else
            {
                if (Namespace == SystemName)
                {
                    EditorGUILayout.HelpBox("Namespace and System name cannot be the same", MessageType.Warning);
                    GUI.enabled = false;
                }
            }

            if (GUILayout.Button("Create new System"))
            {
                //Check if script exist
                Type _t = Type.GetType(SystemName + ", Assembly-CSharp");

                if (_t != null)
                {
                    if (EditorUtility.DisplayDialog("Conflict", "A class with this name already exist", "Cancel"))
                    {
                        return;
                    }
                }
                Create();
            }
        }

        private void Create()
        {
            _AssetSelectedPath = SelectedPath.Substring(SelectedPath.IndexOf("Assets"));     // Assets/FolderB/SelectedFolder
            _AssetFolderPath = _AssetSelectedPath + "/" + SystemName;                      // Assets/FolderB/SelectedFolder/SystemName
            _AssetEditorPath = _AssetFolderPath + "/Editor/" + SystemName + "Editor.cs";   // Assets/FolderB/SelectedFolder/SystemName/Editor/SystemNameEditor.cs
            _AssetObjectPath = _AssetFolderPath + "/" + SystemName + "Manager.asset";             // Assets/FolderB/SelectedFolder/SystemName/SystemName.asset
            _assetClassPath = _AssetFolderPath + "/" + SystemName + "Manager.cs";                // Assets/FolderB/SelectedFolder/SystemName/SystemName.cs
            _runtimeAsmdefPath = _AssetFolderPath + "/Ombrage.Systems." + SystemName + ".asmdef";                // Assets/FolderB/SelectedFolder/SystemName/Ombrage.SystemName.asmdef
            _editorAsmdefPath = _AssetFolderPath + "/Editor/Ombrage.Systems." + SystemName + ".EditorClass" + ".asmdef";                // Assets/FolderB/SelectedFolder/SystemName/Ombrage.SystemName.asmdef

            //Set the template
            DefineAssemblyRuntime();
            DefineRuntimeTemplate();
            DefineEditorTemplate();

            _systemTemplate = _systemTemplate.Replace("SYSTEM_TEMPLATE", SystemName);

            //Create directory if non existant
            if (!AssetDatabase.IsValidFolder(_AssetFolderPath))
            {
                AssetDatabase.CreateFolder(_AssetSelectedPath, SystemName);
            }


            //Write the script
            File.WriteAllText(_runtimeAsmdefPath, _runtimeAssemblyTemplate);
            File.WriteAllText(_assetClassPath, _systemTemplate);



            if (!AssetDatabase.IsValidFolder(_AssetFolderPath + "Editor/"))
            {
                AssetDatabase.CreateFolder(_AssetFolderPath, "Editor");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _runtimeAssemblyGUID = AssetDatabase.AssetPathToGUID(_runtimeAsmdefPath);
            DefineAssemblyEditor();

            File.WriteAllText(_editorAsmdefPath, _EditorAssemblyTemplate);
            File.WriteAllText(_AssetEditorPath, _editorTemplate);

            EditorPrefs.SetBool("SystemCreator.MustCreate", true);
            EditorPrefs.SetString("SystemCreator.SystemName", SystemName + "Manager");
            EditorPrefs.SetString("SystemCreator._AssetObjectPath", _AssetObjectPath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

        }


        [UnityEditor.Callbacks.DidReloadScripts]
        private static void EditorApplication_projectChanged()
        {
            bool _mustCreate = EditorPrefs.GetBool("SystemCreator.MustCreate");

            if (!_mustCreate)
                return;

            if (StaticSystemCreator.instance == null)
            {
                instance = (StaticSystemCreator)EditorWindow.GetWindow(typeof(StaticSystemCreator));
            }

            EditorPrefs.SetBool("SystemCreator.MustCreate", false);

            string _systemName = EditorPrefs.GetString("SystemCreator.SystemName");

            _AssetObjectPath = EditorPrefs.GetString("SystemCreator._AssetObjectPath");

            ScriptableObject _newscriptable = ScriptableObject.CreateInstance(_systemName);

            AssetHandler.Create(_newscriptable, _AssetObjectPath);

            EditorUtility.FocusProjectWindow();
            UnityEngine.Object _obj = AssetDatabase.LoadAssetAtPath(_AssetObjectPath, typeof(UnityEngine.Object));

            Selection.activeObject = _obj;

            EditorGUIUtility.PingObject(_obj);
            EditorPrefs.SetString("SystemCreator.SystemName", "");
            EditorPrefs.SetString("SystemCreator._AssetObjectPath", "");

            StaticSystemCreator.instance.Close();

        }

        private void DefineAssemblyRuntime()
        {
            _runtimeAssemblyTemplate =
                "{" + "\n" +
                    "\"name\": \"Ombrage.Systems." + SystemName + "\"," + "\n" +
                    "\"rootNamespace\": \"" + Namespace + "\"," + "\n" +
                    "\"references\": [" + "\n" +
                    "\"GUID:58e294fe8954234449fe7798270c6f39\"," + "\n" +
                    "\"GUID:0ad57d508c782f04e86ebf4955b6e134\"," + "\n" +
                    "\"GUID:0036dd76c1889b6408c499dbb2211f84\"" + "\n" +
                    "]," + "\n" +
                    "\"includePlatforms\": [" + "\n" +
                    "]," + "\n" +
                    "\"excludePlatforms\": []," + "\n" +
                    "\"allowUnsafeCode\": false," + "\n" +
                    "\"overrideReferences\": false," + "\n" +
                    "\"precompiledReferences\": []," + "\n" +
                    "\"autoReferenced\": true," + "\n" +
                    "\"defineConstraints\": []," + "\n" +
                    "\"versionDefines\": []," + "\n" +
                    "\"noEngineReferences\": false" + "\n" +
                    "}";
        }

        private void DefineRuntimeTemplate()
        {
            _systemTemplate =
                                "using System;" + "\n" +
                                "using Ombrage.StaticSystem;" + "\n" +
                                "using UnityEngine;" + "\n" +
                "\n" +
                                "namespace " + Namespace + "\n" +
                                "{" + "\n" +
                                "[CreateAssetMenu(fileName = \"" + SystemName + "Manager" + "\", menuName = \"Ombrage/StaticTools/Create New " + SystemName + "Manager" + "\", order = 20)]" + "\n" +
                                "public class " + SystemName + "Manager" + " : StaticSystemBase" + "\n" +
                                "{" + "\n" +
                "\t" + "public static " + SystemName + "Manager Instance" + "\n" +
                "\t" + "{" + "\n" +
                "\t\t" + "get" + "\n" +
                "\t\t" + "{" + "\n" +
                "\t\t\t" + "#if UNITY_EDITOR" + "\n" +
                "\t\t\t" + " if (!Application.isPlaying)" + "\n" +
                "\t\t\t" + "{" + "\n" +
                "\t\t\t\t" + "AssetHandler.FindAsset(\"" + SystemName + "Manager" + "\", out _instance, \".asset\");" + "\n" +
                "\t\t\t" + "}" + "\n" +
                "\t\t\t" + "#endif" + "\n" +
                "\t\t\t" + "return _instance;" + "\n" +
                "\t\t" + "}" + "\n" +
                "\t\t" + "internal set" + "\n" +
                "\t\t" + "{" + "\n" +
                "\t\t\t" + "_instance = value;" + "\n" +
                "\t\t" + "}" + "\n" +
                "\t" + "}" + "\n" +
                "\n" +
                "\t" + "private static " + SystemName + "Manager" + " _instance;" + "\n" +
                "\t" + "//Specifics variables here" + "\n" +
                "\n" +
                "\t" + "public override void Initialize()" + "\n" +
                "\t" + "{" + "\n" +
                "\t\t" + "_instance = this;" + "\n" +
                "\t\t" + "base.Init(_instance);" + "\n" +
                "\n" +
                "\t\t" + "//Do specific Initialization here" + "\n" +
                "\n" +
                "\t\t" + "Save(_instance);" + "\n" +
                "\t" + "}" + "\n" +
                "\n" +
                "\t" + "public override Tuple<bool, string> Import()" + "\n" +
                "\t" + "{" + "\n" +
                "\t\t" + "return Load(this);" + "\n" +
                "\t" + "}" + "\n" +
                "\n" +
                "\t" + "public override Tuple<bool, string> Export()" + "\n" +
                "\t" + "{" + "\n" +
                "\t\t" + "return Save(this);" + "\n" +
                "\t" + "}" + "\n" +
                                "}" + "\n" +
                                "}";
        }

        private void DefineAssemblyEditor()
        {
            _EditorAssemblyTemplate =
                "{" + "\n" +
                    "\"name\": \"Ombrage." + SystemName + ".EditorClass" + "\"," + "\n" +
                    "\"rootNamespace\": \"Ombrage." + SystemName + ".EditorClass" + "\"," + "\n" +
                    "\"references\": [" + "\n" +
                    "\"GUID:f5baedacd00672743b09deb636f2d050\"," + "\n" +
                     "\"GUID:" + _runtimeAssemblyGUID + "\"" + "\n" +
                    "]," + "\n" +
                    "\"includePlatforms\": [" +
                    "\"Editor\"" + "\n" +
                    "]," + "\n" +
                    "\"excludePlatforms\": []," + "\n" +
                    "\"allowUnsafeCode\": false," + "\n" +
                    "\"overrideReferences\": false," + "\n" +
                    "\"precompiledReferences\": []," + "\n" +
                    "\"autoReferenced\": true," + "\n" +
                    "\"defineConstraints\": []," + "\n" +
                    "\"versionDefines\": []," + "\n" +
                    "\"noEngineReferences\": false" + "\n" +
                    "}";
        }
        private void DefineEditorTemplate()
        {
            _editorTemplate =
                "using Ombrage.StaticSystem.Editor;" + "\n" +
                "using " + Namespace + ";" + "\n" +
                "using UnityEngine;" + "\n" +
                "using UnityEditor;" + "\n" +
                "\n" +
                "[CustomEditor(typeof(" + SystemName + "Manager" + "), true)] \n" +
                "public class " + SystemName + "Manager" + "Editor : StaticSystemBaseEditor \n" +
                "{\n" +
                "\t//Declare SerializedProperties here;\n" +
                "\t//ie : SerializedProperty _sp;\n" +
                "\n" +
                "\t" + "public override void OnEnable() \n" +
                "\t{\n" +
                "\t\tbase.OnEnable();\n" +
                "\t\t//Reference SerializedProperties here;\n" +
                "\t\t//ie : _sp = serializedObject.FindProperty(\"PROPERTY_NAME\");\n" +
                "\n" +
                "\t}\n" +
                "\n" +
                "\tpublic override void OnInspectorGUI()\n" +
                "\t{\n" +
                "\t\tBaseInspector();\n" +
                "\n" +
                "\t\t//Use custom Properties here;\n" +
                "\t\t//ie : EditorGUILayout.PropertyField(_sp);\n" +
                "\n" +
                "\t\tserializedObject.ApplyModifiedProperties();\n" +
                "\t}\n" +
                "}";
        }
    }
}
