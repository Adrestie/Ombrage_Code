using Newtonsoft.Json.Linq;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using UnityEngine;

public static class JSONTools<T>
{
    /// <summary>
    /// Set Datas to json Files
    /// </summary>
    /// <param name="filePath"> path to the json file</param>
    /// <param name="_object"> Object who will contains datas</param>
    /// <param name="CallBack"></param>
    /// <returns>
    /// Item 1 : int -1 : File Not Found | 0 : Ok | 1: Error
    /// Item 2 : string status
    /// </returns>
    public static Tuple<int, string> Serialize(T data, string filePath, List<string> excludeFields = null, Action CallBack = null)
    {
        string _status = "";
        string _fileDirectory = Path.GetDirectoryName(filePath);

        try
        {
            if (!Directory.Exists(_fileDirectory))
            {
                Directory.CreateDirectory(_fileDirectory);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("Cannot create directory at : " + _fileDirectory + ".\n Error : " + e.Message);
            _status = "Cannot create directory at : " + _fileDirectory + ".\n Error : " + e.Message;
            return new Tuple<int, string>(1, _status);
        }

        string json = JsonUtility.ToJson(data, true);

        //Remove read only from the JSON target file

        if (File.Exists(filePath))
        {
            FileInfo _noReadOnly = new FileInfo(filePath);
            _noReadOnly.IsReadOnly = false;
        }

        if (!excludeFields.IsNullOrEmpty())
        {
            JObject jo = JObject.Parse(json);
            for (int e = 0; e < excludeFields.Count; ++e)
            {


                if (jo.ContainsKey(excludeFields[e]))
                    jo.Property(excludeFields[e]).Remove();

            }
            json = jo.ToString();
            try
            {
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError("Cannot write JSON at : " + filePath + ".\n Error : " + e.Message);
                _status = "Cannot write JSON at : " + filePath + ".\n Error : " + e.Message;
                return new Tuple<int, string>(1, _status);
            }
        }
        else
        {
            try
            {
                File.WriteAllText(filePath, json);
            }
            catch (Exception e)
            {
                Debug.LogError("Cannot write JSON at : " + filePath + ".\n Error : " + e.Message);
                _status = "Cannot write JSON at : " + filePath + ".\n Error : " + e.Message;
                return new Tuple<int, string>(1, _status);
            }
        }

        _status = "Ok";
        CallBack?.Invoke();

        return new Tuple<int, string>(0, _status);
    }

    /// <summary>
    /// Get Datas from json Files
    /// </summary>
    /// <param name="filePath"> path to the json file</param>
    /// <param name="_object"> Object who will contains datas</param>
    /// <param name="CallBack"></param>
    /// <returns>
    /// Item 1 : int -1 : File Not Found | 0 : Ok | 1: Error
    /// Item 2 : string status
    /// </returns>
    public static Tuple<int, string> Deserialize(string filePath, T _object, Action CallBack = null)
    {
        int _success = -1;
        string status = "";

        if (File.Exists(filePath))
        {
            string json = "";
            try
            {
                json = File.ReadAllText(filePath);
            }
            catch (Exception e)
            {
                Debug.LogError("Cannot read JSON file : " + e.Message);
                status = "Error : " + e.Message;
            }
            finally
            {
                if (!string.IsNullOrEmpty(json))
                {
                    try
                    {
                        JsonUtility.FromJsonOverwrite(json, _object);
                        _success = 0;
                        status = "Ok";
                        CallBack?.Invoke();
                    }
                    catch (Exception e)
                    {
                        Debug.LogError(e.Message);
                        _success = 1;
                        status = "Error : " + e.Message;
                    }
                }
            }
        }
        else
        {
            _success = -1;
            status = "File Not Found";
        }

        return new Tuple<int, string>(_success, status);
    }

    public static Tuple<bool, string> Clear(string filePath, bool _safeClear = true, Action CallBack = null)
    {

        if (Directory.Exists(filePath))
        {
            string[] _files = Directory.GetFiles(filePath);

            if (_files.Length == 0)
            {
                return new Tuple<bool, string>(false, "No Data to clear");
            }

            foreach (string _file in _files)
            {
                FileInfo _f = new FileInfo(_file);

                if (_safeClear)
                {
                    if (_f.IsReadOnly)
                    {
                        UnityEngine.Debug.LogError("[JSON TOOLS] The target JSON is Read Only, aborting clear operation.");
                        return new Tuple<bool, string>(false, "Safe Clear detected a Read Only file and aborted the operation");
                    }
                }
            }

            foreach (string _file in _files)
            {
                File.Delete(_file);
            }

            return new Tuple<bool, string>(true, "Data Cleared");
        }

        return new Tuple<bool, string>(false, "Directory not found");
    }
}