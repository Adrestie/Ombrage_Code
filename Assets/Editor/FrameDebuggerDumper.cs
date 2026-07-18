// FrameDebuggerDumper.cs
// Dumps the currently-captured Frame Debugger event list to a text file.
// Unity has no native export; this reads the internal UnityEditorInternal.FrameDebuggerUtility
// API by reflection (version-tolerant: it logs whatever fields the FrameDebuggerEventData class
// exposes in this Unity build).
//
// USAGE:
//   1) Window > Analysis > Frame Debugger > Enable (capture the GAME view you care about —
//      in Play mode if the grass window follows the game camera).
//   2) Tools > Grass > Dump Frame Debugger.
//   3) The dump path is logged to the Console (project root: framedebugger_dump.txt).
using System;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

public static class FrameDebuggerDumper
{
    const BindingFlags STATIC = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
    const BindingFlags INST   = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

    [MenuItem("Tools/Grass/Probe Frame Debugger API")]
    public static void Probe()
    {
        var sb = new StringBuilder();
        sb.AppendLine("# Types contenant 'FrameDebugger' / 'FrameEvent' :");
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type[] types;
            try { types = a.GetTypes(); } catch { continue; }
            foreach (var t in types)
            {
                if (t.FullName == null) continue;
                if (!t.FullName.Contains("FrameDebugger") && !t.FullName.Contains("FrameEvent")) continue;
                sb.AppendLine($"\n== {t.FullName}   [{a.GetName().Name}]");
                foreach (var m in t.GetMembers(BindingFlags.Public | BindingFlags.NonPublic |
                                               BindingFlags.Static | BindingFlags.Instance |
                                               BindingFlags.DeclaredOnly))
                {
                    if (m is MethodInfo mi)
                    {
                        var ps = mi.GetParameters();
                        var args = string.Join(", ", Array.ConvertAll(ps, p => p.ParameterType.Name + " " + p.Name));
                        sb.AppendLine($"    method {mi.ReturnType.Name} {mi.Name}({args})");
                    }
                    else if (m is PropertyInfo pi) sb.AppendLine($"    prop   {pi.PropertyType.Name} {pi.Name}");
                    else if (m is FieldInfo fi)     sb.AppendLine($"    field  {fi.FieldType.Name} {fi.Name}");
                }
            }
        }
        string path = Path.Combine(Directory.GetCurrentDirectory(), "framedebugger_api.txt");
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[FDDump] API sondée -> {path}");
        EditorUtility.RevealInFinder(path);
    }

    [MenuItem("Tools/Grass/Dump Frame Debugger")]
    public static void Dump()
    {
        var util = FindType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerUtility")
                ?? FindType("UnityEditorInternal.FrameDebuggerUtility");
        if (util == null) { Debug.LogError("[FDDump] FrameDebuggerUtility introuvable — lance d'abord 'Tools/Grass/Probe Frame Debugger API'."); return; }

        int count = ReadInt(util, "count");
        if (count <= 0)
        {
            Debug.LogError("[FDDump] count = 0. Active le Frame Debugger (Window > Analysis > Frame Debugger > Enable) " +
                           "et capture une frame AVANT de lancer le dump.");
            return;
        }

        // Make sure all captured events are loaded (limit = the currently-viewed event).
        var limitProp = util.GetProperty("limit", STATIC);
        if (limitProp != null && limitProp.CanWrite) limitProp.SetValue(null, count);

        // FrameDebuggerEventData target instance + GetFrameEventData(int, target).
        var dataType = FindType("UnityEditorInternal.FrameDebuggerInternal.FrameDebuggerEventData")
                    ?? FindType("UnityEditorInternal.FrameDebuggerEventData");
        object data   = dataType != null ? Activator.CreateInstance(dataType) : null;
        MethodInfo getData = FindGetData(util, dataType);
        MethodInfo getName = util.GetMethod("GetFrameEventInfoName", STATIC);

        // Real field names in Unity 6 (UnityEditorInternal.FrameDebuggerInternal).
        string[] priority = { "m_RealShaderName", "m_OriginalShaderName", "m_PassName", "m_PassLightMode",
                              "m_InstanceCount", "m_DrawCallCount", "m_VertexCount", "m_IndexCount",
                              "m_ComputeShaderName", "m_ComputeShaderKernelName",
                              "m_RenderTargetName" };

        var sb = new StringBuilder();
        sb.AppendLine($"# Frame Debugger dump — {count} events  (Unity {Application.unityVersion})");
        sb.AppendLine($"# {DateTime.Now}");
        sb.AppendLine("# Cherche 'GrassBRG' pour les brins.");
        sb.AppendLine();

        // Also collect a grass-only digest at the end.
        var grass = new StringBuilder();

        for (int i = 0; i < count; i++)
        {
            string evName = getName != null ? (string)getName.Invoke(null, new object[] { i }) : null;
            sb.Append($"[{i:000}] {evName}  | ");

            bool ok = getData != null && data != null &&
                      (bool)getData.Invoke(null, new object[] { i, data });
            string line = "";
            if (ok)
            {
                foreach (var name in priority)
                {
                    var f = dataType.GetField(name, INST);
                    if (f == null) continue;
                    object v = f.GetValue(data);
                    if (v == null) continue;
                    string s = v.ToString();
                    if (string.IsNullOrEmpty(s) || s == "0") continue;
                    line += $"{name.Substring(2)}={s}  ";
                }
            }
            else line = "(pas de data)";
            sb.AppendLine(line);

            string blob = (evName + " " + line);
            if (blob.IndexOf("Grass", StringComparison.OrdinalIgnoreCase) >= 0)
                grass.AppendLine($"[{i:000}] {evName}  | {line}");
        }

        sb.AppendLine();
        sb.AppendLine("==================== DIGEST HERBE (events 'Grass') ====================");
        sb.Append(grass.Length > 0 ? grass.ToString() : "(aucun event contenant 'Grass' — l'herbe n'est pas rendue dans la frame capturée)\n");

        string path = Path.Combine(Directory.GetCurrentDirectory(), "framedebugger_dump.txt");
        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[FDDump] {count} events écrits dans :\n{path}");
        EditorUtility.RevealInFinder(path);
    }

    static Type FindType(string fullName)
    {
        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
        {
            var t = a.GetType(fullName);
            if (t != null) return t;
        }
        return null;
    }

    static int ReadInt(Type t, string member)
    {
        var p = t.GetProperty(member, STATIC);
        if (p != null) return Convert.ToInt32(p.GetValue(null));
        var f = t.GetField(member, STATIC);
        if (f != null) return Convert.ToInt32(f.GetValue(null));
        return 0;
    }

    static MethodInfo FindGetData(Type util, Type dataType)
    {
        foreach (var m in util.GetMethods(STATIC))
        {
            if (m.Name != "GetFrameEventData") continue;
            var ps = m.GetParameters();
            if (ps.Length == 2 && ps[0].ParameterType == typeof(int)) return m;
        }
        return null;
    }
}
