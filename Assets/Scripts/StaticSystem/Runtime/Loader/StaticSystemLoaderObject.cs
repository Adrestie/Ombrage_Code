using System.Collections.Generic;
using UnityEngine;

namespace Ombrage.StaticSystem
{
[CreateAssetMenu(fileName = "SystemLoaderList", menuName = "Ombrage/StaticTools/Create New SystemLoaderList", order = 0)]
public class StaticSystemLoaderObject : ScriptableObject
{
    public bool showLogs = false;
    public List<StaticSystemBase> LoadOnApplicationStart;
    public List<StaticSystemBase> LoadOnSplashscreenScene;
    public List<StaticSystemBase> LoadOnDemand;
}
}
