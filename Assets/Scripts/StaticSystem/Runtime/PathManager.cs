using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ombrage.StaticSystem
{
[CreateAssetMenu(fileName = "PathManager", menuName = "Ombrage/StaticTools/Create New PathManager", order = 20)]
public class PathManager : StaticSystemBase
{
    public static PathManager instance
    {
        get
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                AssetHandler.FindAsset("PathManager", out _instance, ".asset");
            }
#endif
            return _instance;
        }
        internal set
        {
            _instance = value;
        }
    }

    private static PathManager _instance;

    //Specifics variables here 
    public string MainPath;
    public SerializableDictionary<string, string> ModulePath = new SerializableDictionary<string, string>();
    [SerializeField] private SerializableDictionary<string, string> INTERNAL_PATH = new SerializableDictionary<string, string>();

    public static void ForceInit()
    {
        instance = ScriptableObject.CreateInstance<PathManager>();
        instance.Initialize();
    }
    public override void Initialize()
    {
        instance = this;

        base.Init(instance);


        //Do specific Initialization here
        MainPath = SetMainPath();

        CheckFolderExist();
    }

    private string SetMainPath()
    {
        string _platformPath = Application.dataPath + "/StreamingAssets/";

        if (!Directory.Exists(_platformPath))
            Directory.CreateDirectory(_platformPath);


        return _platformPath;
    }

    private void CheckFolderExist()
    {
        foreach (SerializableDictionary<string, string>.Pair _module in INTERNAL_PATH)
        {
            if (!ModulePath.ContainsKey(_module.Key))
            {
                SerializableDictionary<string, string>.Pair _newKey = new SerializableDictionary<string, string>.Pair();
                _newKey.Key = _module.Key;
                _newKey.Value = MainPath + _module.Value;

                ModulePath.Add(_newKey);
            }
            else
            {
                if (ModulePath[_module.Key] != MainPath + _module.Value)
                {
                    SerializableDictionary<string, string>.Pair _editedKey = new SerializableDictionary<string, string>.Pair();
                    _editedKey.Key = _module.Key;
                    _editedKey.Value = MainPath + _module.Value;
                    ModulePath.Set(_editedKey);
                }
            }

        }

        List<string> keysToRemove = new List<string>();
        foreach (SerializableDictionary<string, string>.Pair _module in ModulePath)
        {
            if (!INTERNAL_PATH.ContainsKey(_module.Key))
            {
                keysToRemove.Add(_module.Key);
            }
        }
        foreach (string key in keysToRemove)
        {
            ModulePath.Remove(key);
        }

        foreach (SerializableDictionary<string, string>.Pair _module in ModulePath)
        {
            if (!Directory.Exists(ModulePath[_module.Key]))
                Directory.CreateDirectory(ModulePath[_module.Key]);
        }
    }
}
}
