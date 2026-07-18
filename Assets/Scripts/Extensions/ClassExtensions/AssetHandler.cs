using System.Collections;
using System.IO;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;

#if UNITY_EDITOR
using UnityEditor;
#endif

public static class AssetHandler
{
    /// <summary>
    /// Get All assets of type "T" in a specific folder.
    /// 
    /// Runtime : DONT
    /// Editor : OK
    /// 
    /// </summary>
    /// <typeparam name="T">Object Type to search</typeparam>
    /// <param name="path"></param>
    /// <returns>
    /// "True"  if something is found.
    /// "False" if nothing where found or something went wrong.
    /// </returns>
    public static bool EditorLoadAll<T>(string path, out T[] result)
    {
#if UNITY_EDITOR
        ArrayList al = new ArrayList();

        string _globalPath = Application.dataPath;

        int _part = _globalPath.IndexOf("Assets");
        _globalPath = _globalPath.Substring(0, _part);

        UnityEngine.Debug.Log(_globalPath);

        string[] fileEntries = Directory.GetFiles(_globalPath + "/" + path);
        foreach (string fileName in fileEntries)
        {
            int index = fileName.LastIndexOf("/");
            string localPath = path;

            if (index > 0)
                localPath += fileName.Substring(index);

            Object t = null;

            if (!Application.isPlaying)
                t = AssetDatabase.LoadAssetAtPath(localPath, typeof(T));

            if (t != null)
                al.Add(t);
        }

        T[] itemFound = new T[al.Count];
        for (int i = 0; i < al.Count; i++)
            itemFound[i] = (T)al[i];

        result = itemFound;

        if (result == null)
            return false;
        else if (result.Length == 0)
            return false;
        else
            return true;
#else
        Debug.LogError("[AssetHandler - EditorLoadAll] ne peut pas être utilisé en runtime");
        result = null;
        return false;
#endif
    }

    /// <summary>
    /// Get All assets of type "T" in a specific folder.
    /// 
    /// Runtime : DONT
    /// Editor : OK
    /// 
    /// </summary>
    /// <typeparam name="T">Object Type to search</typeparam>
    /// <param name="path"></param>
    /// <returns>
    /// "True"  if something is found.
    /// "False" if nothing where found or something went wrong.
    /// </returns>
    public static bool EditorLoadAll<T>(string path, out List<T> result)
    {
#if UNITY_EDITOR
        ArrayList al = new ArrayList();

        string _globalPath = Application.dataPath;

        int _part = _globalPath.IndexOf("Assets");
        _globalPath = _globalPath.Substring(0, _part);
        UnityEngine.Debug.Log(_globalPath);

        string[] fileEntries = Directory.GetFiles(_globalPath + "/" + path);
        foreach (string fileName in fileEntries)
        {
            int index = fileName.LastIndexOf("/");
            string localPath = path;

            if (index > 0)
                localPath += fileName.Substring(index);

            Object t = null;

            if (!Application.isPlaying)
                t = AssetDatabase.LoadAssetAtPath(localPath, typeof(T));
            if (t != null)
                al.Add(t);
        }

        T[] itemFound = new T[al.Count];
        for (int i = 0; i < al.Count; i++)
            itemFound[i] = (T)al[i];

        result = itemFound.ToList();

        if (result == null)
            return false;
        else if (result.Count == 0)
            return false;
        else
            return true;
#else
        Debug.LogError("[AssetHandler - EditorLoadAll] ne peut pas être utilisé en runtime");
        result = null;
        return false;
#endif

    }

    /// <summary>
    /// Find All Assets of type "T" in the Resources folder
    /// 
    /// Runtime : OK
    /// Editor : OK
    /// 
    /// </summary>
    /// <typeparam name="_path">Object Type to search</typeparam>
    /// <returns>
    /// "True"  if something is found.
    /// "False" if nothing where found or something went wrong.
    /// </returns>
    public static bool LoadResourcesAll<T>(string _path, out T[] result) where T : Object
    {
        result = Resources.LoadAll<T>(_path);

        if (result == null)
            return false;
        else if (result.Length == 0)
            return false;
        else
            return true;
    }

    /// <summary>
    /// Find All Assets of type "T" in the Resources folder
    /// 
    /// Runtime : OK
    /// Editor : OK
    /// 
    /// </summary>
    /// <typeparam name="_path">Object Type to search</typeparam>
    /// <returns>
    /// "True"  if something is found.
    /// "False" if nothing where found or something went wrong.
    /// </returns>
    public static bool LoadResourcesAll<T>(string _path, out List<T> result) where T : Object
    {
        result = Resources.LoadAll<T>(_path).ToList();

        if (result == null)
            return false;
        else if (result.Count == 0)
            return false;
        else
            return true;

    }

    /// <summary>
    /// Find All Assets of type "T" in the entire project
    /// 
    /// Runtime : DONT USE
    /// Editor : OK
    /// 
    /// </summary>
    /// <typeparam name="T">Object Type to search</typeparam>
    /// <returns>
    /// "True"  if something is found.
    /// "False" if nothing where found or something went wrong.
    /// </returns>
    public static bool FindAllAssetsOfType<T>(out T[] result) where T : Object
    {
#if UNITY_EDITOR
        List<T> assets = new List<T>();

        string[] parts = typeof(T).ToString().Split('.');
        UnityEngine.Debug.Log(parts[parts.Length - 1]);
        string[] guids = AssetDatabase.FindAssets("t:" + typeof(T).FullName);

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                assets.Add(asset);
            }
        }

        result = assets.ToArray();

        if (result == null)
            return false;
        else if (result.Length == 0)
            return false;
        else
            return true;
#else
        Debug.LogError("[AssetHandler - FindAllAssetsOfType] ne peut pas être utilisé en runtime");
        result = null;
        return false;
#endif
    }

    /// <summary>
    /// Find All Assets of type "T" in the entire project
    /// 
    /// Runtime : DONT USE
    /// Editor : OK
    /// 
    /// </summary>
    /// <typeparam name="T">Object Type to search</typeparam>
    /// <returns>
    /// "True"  if something is found.
    /// "False" if nothing where found or something went wrong.
    /// </returns>
    public static bool FindAllAssetsOfType<T>(out List<T> result) where T : UnityEngine.Object
    {
#if UNITY_EDITOR
        List<T> assets = new List<T>();
        string[] splits = typeof(T).ToString().Split('.');


        string[] guids = AssetDatabase.FindAssets("t:" + splits[splits.Length - 1]);

        for (int i = 0; i < guids.Length; i++)
        {
            string assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset != null)
            {
                assets.Add(asset);
            }
        }

        result = assets;

        if (result == null)
            return false;
        else if (result.Count == 0)
            return false;
        else
            return true;
#else
        Debug.LogError("[AssetHandler - FindAllAssetsOfType] ne peut pas être utilisé en runtime");
        result = null;
        return false;
#endif
    }

    public static bool FindAsset<T>(string _assetName, out T result, string extension = null, bool _showDebug = false) where T : UnityEngine.Object
    {
#if UNITY_EDITOR
        if (string.IsNullOrEmpty(_assetName))
        {
            if (_showDebug) UnityEngine.Debug.Log("No name provided, Abort operation");
            result = default;
            return false;
        }



        string[] guids = AssetDatabase.FindAssets(_assetName, new[] { "Assets" });
        string assetPath = "";
        T asset = null;
        if (guids.Length <= 0)
        {
            if (_showDebug) UnityEngine.Debug.Log("No asset found");
            result = default;
            return false;
        }
        else
        {
            if (_showDebug) UnityEngine.Debug.Log(guids.Length + " Assets found");
            for (int i = 0; i < guids.Length; ++i)
            {
                string[] _splits = AssetDatabase.GUIDToAssetPath(guids[i]).Split("/");

                string _splitAssetName = _splits[_splits.Length - 1];
                if (_splitAssetName.Contains(extension))
                {
                    string removeExt = extension.Contains(".") ? extension : "." + extension;
                    if (_splitAssetName.Replace(removeExt, "") == _assetName)
                    {
                        if (_showDebug) UnityEngine.Debug.Log("[" + i + "] " + AssetDatabase.GUIDToAssetPath(guids[i]));
                        assetPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                        asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
                        if (asset != null)
                            break;
                    }
                }
            }
        }


        result = asset;
        if (result == null)
            return false;
        else
            return true;
#else
        Debug.LogError("[AssetHandler - FindAllAssetsOfType] ne peut pas être utilisé en runtime");
        result = null;
        return false;
#endif
    }

    /// <summary>
    /// Select specific Asset at Path
    /// </summary>
    /// <typeparam name="T"></typeparam>
    /// <param name="Path"></param>
    /// <param name="result"></param>
    /// <returns></returns>
    public static bool SelectAsset<T>(string Path, out T result)
    {
#if UNITY_EDITOR
        try
        {
            Object t = AssetDatabase.LoadAssetAtPath(Path, typeof(T));
            result = (T)System.Convert.ChangeType(t, typeof(T));
            return true;
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.Log(e);
            result = default;
            return false;
        }
#else
        Debug.LogError("[AssetHandler - Create] ne peut pas être utilisé en runtime");
        result = default(T);
        return false;
#endif
    }

    /// <summary>
    /// Create a new asset at path
    /// 
    /// Runtime : DONT USE
    /// Editor : OK
    /// 
    /// </summary>
    /// <param name="_object">Object to create</param>
    /// <param name="_path">Destination</param>
    /// <returns>
    /// "True"  if object correctly created
    /// "False" if something went wrong.
    /// </returns>
    public static bool Create(Object _object, string _path)
    {
#if UNITY_EDITOR
        try
        {
            CheckPath(_path);

            AssetDatabase.CreateAsset(_object, _path);

            Save();
            return true;
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError(e);
            return false;
        }
#else
        Debug.LogError("[AssetHandler - Create] ne peut pas être utilisé en runtime");
        return false;
#endif
    }

    /// <summary>
    /// Duplicate an Object
    /// </summary>
    /// <param name="_originalObject">Original Object</param>
    /// <param name="_newName">New name Object</param>
    /// <param name="_destination">New Destination</param>
    /// <returns>
    /// Return the new object or null if something went wrong
    /// </returns>
    public static Object Duplicate(string _originalPath, string _newName, string _destination = "")
    {
#if UNITY_EDITOR
        try
        {
            CheckPath(_destination);
            string pathExt = Path.GetExtension(_originalPath);
            string pathNoFile = "";

            if (string.IsNullOrEmpty(_destination) || string.IsNullOrWhiteSpace(_destination))
            {
                _destination = _originalPath;
            }

            string[] spliPath = _destination.Split('/');

            for (int i = 0; i < spliPath.Length - 1; ++i)
            {
                pathNoFile += spliPath[i] + "/";
            }

            string newPath = pathNoFile + _newName + pathExt;


            if (File.Exists(newPath))
            {
                if (EditorUtility.DisplayDialog("Warning", "The object you want to duplicate have the same name as its original, Operation Cancelled", "Ok"))
                {
                    return null;
                }
            }

            AssetDatabase.CopyAsset(_originalPath, newPath);

            Save();

            System.Type t = AssetDatabase.GetMainAssetTypeAtPath(_originalPath);
            return AssetDatabase.LoadAssetAtPath(newPath, t);
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("[AssetHandler - Duplicate] " + e);
            return null;
        }
#else
        Debug.LogError("[AssetHandler - Duplicate] ne peut pas être utilisé en runtime");
        return null;
#endif
    }

    /// <summary>
    /// Duplicate an Object
    /// </summary>
    /// <param name="_originalObject">Original Object</param>
    /// <param name="_newName">New name Object</param>
    /// <param name="_destination">New Destination</param>
    /// <returns>
    /// Return the new object or null if something went wrong
    /// </returns>
    public static Object Duplicate(Object _originalObject, string _newName, string _destination = "")
    {
#if UNITY_EDITOR
        try
        {
            CheckPath(_destination);
            string _originalPath = AssetDatabase.GetAssetPath(_originalObject);

            string pathExt = Path.GetExtension(_originalPath);
            string pathNoFile = "";

            if (string.IsNullOrEmpty(_destination) || string.IsNullOrWhiteSpace(_destination))
            {
                _destination = AssetDatabase.GetAssetPath(_originalObject);
            }

            string[] spliPath = _destination.Split('/');

            for (int i = 0; i < spliPath.Length - 1; ++i)
            {
                pathNoFile += spliPath[i] + "/";
            }



            string newPath = pathNoFile + _newName + pathExt;
            UnityEngine.Debug.Log(newPath);
            if (File.Exists(newPath))
            {
                if (EditorUtility.DisplayDialog("Warning", "The object you want to duplicate have the same name as its original, Operation Cancelled", "Ok"))
                {
                    return null;
                }
            }

            Save();
            AssetDatabase.CopyAsset(_originalPath, newPath);
            return AssetDatabase.LoadAssetAtPath(newPath, _originalObject.GetType());
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError("[AssetHandler - Duplicate] " + e);
            return null;
        }
#else
        Debug.LogError("[AssetHandler - Duplicate] ne peut pas être utilisé en runtime");
        return null;
#endif
    }

    /// <summary>
    /// Delete specific Asset
    /// 
    /// Runtime : DONT USE
    /// Editor : OK
    /// 
    /// </summary>
    /// <param name="_object">Object to delete</param>
    /// <returns>
    /// "True"  if object correctly created
    /// "False" if something went wrong.
    /// </returns>
    public static bool Delete(Object _object)
    {
#if UNITY_EDITOR
        try
        {
            string _path = AssetDatabase.GetAssetPath(_object);
            AssetDatabase.DeleteAsset(_path);
            Save();
            return true;
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError(e);
            return false;
        }
#else
        Debug.LogError("[AssetHandler - Delete] ne peut pas être utilisé en runtime");
        return false;
#endif
    }

    /// <summary>
    /// Delete specific Asset
    /// 
    /// Runtime : DONT USE
    /// Editor : OK
    /// 
    /// </summary>
    /// <param name="_object">Object to delete</param>
    /// <returns>
    /// "True"  if object correctly created
    /// "False" if something went wrong.
    /// </returns>
    public static bool Delete(string _path)
    {
#if UNITY_EDITOR
        try
        {
            AssetDatabase.DeleteAsset(_path);
            Save();
            return true;
        }
        catch (System.Exception e)
        {
            UnityEngine.Debug.LogError(e);
            return false;
        }
#else
        Debug.LogError("[AssetHandler - Delete] ne peut pas être utilisé en runtime");
        return false;
#endif
    }

    /// <summary>
    /// Save and Refresh the Asset Database
    /// </summary>
    public static void Save()
    {
#if UNITY_EDITOR
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
#endif
    }

    //Tools
    private static void CheckPath(string _path)
    {
        string[] _pathParts = _path.Split('/');
        string absolutePath = Application.dataPath + '/';

        for (int i = 1; i < _pathParts.Length - 1; ++i) //création récursive du dossier qui va acceuillir le nouvel asset
        {
            absolutePath += _pathParts[i] + '/';
            if (!Directory.Exists(absolutePath))
            {
                Directory.CreateDirectory(absolutePath);
            }
        }
    }

    public static bool GetAssetNameAndExtentionFromPath(string path, out string name, out string extention)
    {
        string[] split = path.Split("/");
        if (split[0] != "Assets")
        {
            name = "";
            extention = "";
            return false;
        }

        string assetName = split[split.Length - 1];
        name = assetName.Split(".")[0];
        extention = assetName.Split(".")[1];
        return true;
    }

    public static string FormatAssetPath(string path)
    {
        string _correctedPath = path;

        if (path.Contains(Application.dataPath))
        {
            _correctedPath = "Assets" + _correctedPath.Replace(Application.dataPath, "");
        }

        _correctedPath = _correctedPath.Replace('\\', '/');
        return _correctedPath;
    }
}
