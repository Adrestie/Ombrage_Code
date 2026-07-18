using UnityEngine;
using System;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Ombrage.StaticSystem
{
public class StaticSystemBase : ScriptableObject
{
    public enum SerializationMethode { LOCAL, CLOUD, NONE }
    public SerializationMethode _serializationMethode; //Accessible uniquement en inspector

    public enum Status { NOT_INITIALIZED, INITIALIZING, READY, ERROR }
    [SerializeField] private Status _status; //Accessible uniquement en inspector
    public Status status
    {
        get { return _status; } //on ne veut pas qu'un script puisse modifier ça. 
        set { _status = value; }
    }

    public bool _canBeCleared;
    public bool _InitializedInEditor;

    [SerializeField] [TextArea] public string _description;

    public virtual void Initialize() { }

    /// <summary>
    /// Premier lancement du système. Permet de définir son état
    /// </summary>
    public virtual void Init<T>(T _data)
    {
        #region EDITOR
#if UNITY_EDITOR
        EditorApplication.playModeStateChanged += RevertOnQuit;
        if (!EditorPrefs.HasKey("Show StaticSystems Logs"))
            EditorPrefs.SetBool("Show StaticSystems Logs", true);
#endif
        #endregion
        _status = Status.INITIALIZING;
        try
        {
            //Chargement du système, on ne veut pas qu'il soit async
            Load(_data);
            Debug.Log(_data.GetType().Name + " is READY");
            _status = Status.READY;
        }
        catch (Exception e)
        {
            Debug.LogError("===|INITIALISATION ERROR|=== " + _data.GetType().Name + " : " + e.Message);

            _data = default(T);
            _status = Status.ERROR;
        }

        if (!Application.isPlaying)
        {
            _data = default(T);
            _status = Status.NOT_INITIALIZED;

        }
    }

    //DO GENERIC STUFF HERE
    /// <summary>
    /// Static Systems aren't in a scene, this method is called each frame by the StaticUpdaterManager to emulate the Monobehaviour's Update method.
    /// </summary>
    public virtual void StaticUpdate() { }

    public virtual Tuple<bool, string> Import(){ return new Tuple<bool, string>(false, "virtual"); }
    
    public virtual Tuple<bool, string> Export() { return new Tuple<bool, string>(false, "virtual"); }

    public Tuple<bool,string> Load<T>(T _data)
    {
        switch (_serializationMethode)
        {
            case SerializationMethode.LOCAL:

                if (PathManager.instance == null)
                {
                    #if UNITY_EDITOR
                    if (EditorPrefs.GetBool("Show StaticSystems Logs"))
                        Debug.Log("[StaticSystemBase] PathManager.instance is null, that's not normal at all !");
#else
                        Debug.Log("[StaticSystemBase] PathManager.instance is null, that's not normal at all !");
#endif

                    return new Tuple<bool, string>(false, "Fatal error, PathManager is missing");
                }

                if (PathManager.instance.ModulePath.ContainsKey(_data.GetType().Name))
                {
                    var result = JSONTools<T>.Deserialize(PathManager.instance.ModulePath[_data.GetType().Name] + "/" + _data.GetType().Name + ".json", _data, null);
                   
                    //ok, Initialization success
                    if (result.Item1 == 0)
                    {
                        Debug.Log("JSON found for " + _data.GetType().Name + " !");
                        return new Tuple<bool,string>(true, "Ok");
                    }
                    //missing JSON, Initialization success
                    else if (result.Item1 == -1)
                    {
                        Debug.Log("No JSON Found at path " + PathManager.instance.ModulePath[_data.GetType().Name]);
                        return new Tuple<bool, string>(true, "No JSON Found at path " + PathManager.instance.ModulePath[_data.GetType().Name]);
                    }
                    //error
                    else
                    {
                        Debug.LogError(_data.GetType().Name + "JSON operation failed with " + result.Item2);
                        return new Tuple<bool, string>(false, _data.GetType().Name + "JSON operation failed with " + result.Item2);
                    }
                }
                else
                {
                    Debug.Log("System " + _data.GetType().Name + " is missing from the PathManager. Abort the local deserialization procedure.");
                    return new Tuple<bool, string>(false, "No path to JSON, Please check PathManager");
                }

            case SerializationMethode.CLOUD: return new Tuple<bool, string>(false, "Serialization mode doesn't need any JSON"); //REQUETE SERVEUR
            case SerializationMethode.NONE: return new Tuple<bool, string>(true, "No serialization needed"); //Charge le scriptable par défaut (comme si on venait de le créer)
            default: return new Tuple<bool, string>(true, "Where am i ? Who am i ? is this what we call the void ?");
        }
    }

    public Tuple<bool, string> Save<T>(T _data, Action _callback = null, List<string> ExcludeField = null)
    {
        switch (_serializationMethode)
        {
            case SerializationMethode.LOCAL:

                if (PathManager.instance == null)
                {
                    Debug.LogError("Fatal error, PathManager is missing");
                    return new Tuple<bool, string>(false, "Fatal error, PathManager is missing");
                }

                if(ExcludeField.IsNullOrEmpty())
                    ExcludeField = new List<string>();

                ExcludeField.Add("_serializationMethode");
                ExcludeField.Add("_status");
             
#if UNITY_EDITOR
                //Permet d'initialiser le path manager en mode Editeur
                if (!Application.isPlaying && PathManager.instance == null)
                {
                    AssetHandler.FindAsset("PathManager", out PathManager _pm, ".asset");
                    PathManager.instance = _pm;
                }
#endif
                if (_data == null)
                {
                    Debug.LogError("Fatal error, No input data");
                    return new Tuple<bool, string>(false, "Fatal error, No input data");
                }

                if (PathManager.instance.ModulePath.ContainsKey(_data.GetType().Name))
                {
                    JSONTools<T>.Serialize(_data, PathManager.instance.ModulePath[_data.GetType().Name] + "/" + _data.GetType().Name + ".json", ExcludeField, null);
                    return new Tuple<bool, string>(true, "Ok");
                }
                else
                {
                    Debug.LogWarning("No path for " + _data.GetType().Name + " JSON, Please check PathManager if this is not intended.");
                    return new Tuple<bool, string>(false, "No path for " + _data.GetType().Name + " JSON, Please check PathManager if this is not intended.");
                }
            case SerializationMethode.CLOUD: return new Tuple<bool, string>(false, "Serialization mode doesn't need any JSON"); //REQUETE SERVEUR
            case SerializationMethode.NONE: return new Tuple<bool, string>(true, "No serialization needed"); //ne sauvegarde pas les données
            default: return new Tuple<bool, string>(true, "Where am i ? Who am i ? is this what we call the void ?");
        }
    }

#region EDITOR
    //EDITOR ONLY : permet de revert certaines valeurs quand on quitte le mode play
#if UNITY_EDITOR
    private void RevertOnQuit(PlayModeStateChange _gameState)
    {
        if (_gameState == PlayModeStateChange.ExitingPlayMode)
        {
            _status = Status.NOT_INITIALIZED;
            Debug.Log("System status reseted");
        }
    }

    public void ResetData(string _fullPath)
    {
        UnityEditor.Presets.Preset _preset = null;
        if(AssetHandler.FindAsset(this.name, out _preset, ".preset"))
        {
            if (!_preset.DataEquals(this))
            {
                Debug.Log("[Data Clear] " + this.name + " reseted with preseted values");
                _preset.ApplyTo(this);
            }
            else
            {
                Debug.Log("[Data Clear] " + this.name + " hasn't been modified");
            }
        }
        else
        {
            Debug.Log("[Data Clear] No preset found for " + this.name + " creating a new one with current values");
            string _path = AssetDatabase.GetAssetPath(this);
            _path = _path.Remove(_path.Length - 6) + "preset";
            _preset = new UnityEditor.Presets.Preset(this);
            AssetDatabase.CreateAsset(_preset, _path);
        }
    }
#endif
#endregion
}
}
