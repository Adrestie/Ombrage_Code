using System;
using System.Threading.Tasks;
#if UNITY_EDITOR

using UnityEditor;
#endif
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ombrage.StaticSystem
{
public class StaticSystemLoader
{
    public static StaticSystemLoaderObject LoaderObject;
    public enum LogLevel { Log, Warning, Error}

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSplashScreen)]
    public static async void Awake()
    {
        SceneManager.sceneLoaded += SceneManager_sceneLoaded;
        LoaderObject = Resources.Load("SystemLoaderList") as StaticSystemLoaderObject;

		PrintLog("-----------------Start \"OnApplicationStart\" StaticSystems Initialization---------------");

		foreach (StaticSystemBase s in LoaderObject.LoadOnApplicationStart)
        {

			PrintLog("[StaticSystemLoader] Start \"" + s.GetType().Name + "\" Initialization...");

            s.Initialize();

            while (s.status == StaticSystemBase.Status.INITIALIZING)
                await Task.Delay(100);

            if (s.status == StaticSystemBase.Status.ERROR)
            {
               PrintLog("[StaticSystemLoader] Failed to Initialize : " + s.GetType().Name + "\n" +
                    "Fatal Error, Quitting...", LogLevel.Error);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                break;
            }

        }
		PrintLog("-----------------All \"OnApplicationStart\" StaticSystem Initialized---------------");
    }

    private static async void SceneManager_sceneLoaded(Scene arg0, LoadSceneMode arg1)
    {
		PrintLog("-----------------Start \"OnSplashscreen\" StaticSystems Initialization---------------");
        SceneManager.sceneLoaded -= SceneManager_sceneLoaded;

        foreach (StaticSystemBase s in LoaderObject.LoadOnSplashscreenScene)
        {
			PrintLog("[StaticSystemLoader] Start \"" + s.GetType().Name + "\" Initialization...");

            s.Initialize();

            while (s.status == StaticSystemBase.Status.INITIALIZING)
                await Task.Delay(100);

            if (s.status == StaticSystemBase.Status.ERROR)
            {
				PrintLog("[StaticSystemLoader] Failed to Initialize : " + s.GetType().Name + "\n" +
                    "Fatal Error, Quitting...", LogLevel.Error);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                break;
            }
        }

		PrintLog("-----------------All \"OnSplashscreen\" StaticSystem Initialized---------------");
    }

    public static async void LoadSpecificSystem(StaticSystemBase _systemToLoad)
    {
        foreach (StaticSystemBase s in LoaderObject.LoadOnDemand)
        {
            if (s.GetType() != _systemToLoad.GetType())
                continue;


            s.Initialize();

            while (s.status == StaticSystemBase.Status.INITIALIZING)
                await Task.Delay(100);

            if (s.status == StaticSystemBase.Status.ERROR)
            {
				PrintLog("[StaticSystemLoader] Failed to Initialize : " + s.GetType().Name + "\n" +
                    "Fatal Error, Quitting...", LogLevel.Error);
#if UNITY_EDITOR
                UnityEditor.EditorApplication.isPlaying = false;
#endif
                break;
            }

        }
    }

    public static void PrintLog(string msg, LogLevel logLevel=LogLevel.Log)
    {
        if (LoaderObject.showLogs)
            switch (logLevel)
            {
                case LogLevel.Log: Debug.Log(msg); break;
                case LogLevel.Warning: Debug.LogWarning(msg); break;
                case LogLevel.Error: Debug.LogError(msg); break;
                default:Debug.Log(msg); break;

            }
		
    }
}
}
