// GitSyncToolbar.cs
// À placer dans un dossier Editor : Assets/Editor/GitSyncToolbar/GitSyncToolbar.cs
//
// Surveille une branche distante (origin/<branche>) et signale/récupère les nouveaux commits.
// - Dropdown + libellé d'état + bouton "Pull" dans la barre d'outils principale d'Unity.
// - Ne vérifie que hors Play mode, toutes les X secondes (réglable dans Preferences > Git Sync).
// - Dès que "en retard" est détecté, la vérification récurrente s'arrête.
// - En retard + éditeur au premier plan  -> popup + bouton Pull disponible.
// - En retard + éditeur en arrière-plan  -> pull automatique quand tu reviens sur l'éditeur.
//
// Prérequis : Unity 6.3 (6000.3) ou plus récent pour l'intégration barre d'outils.
//             Un dépôt git à la racine du projet (le .git à côté du dossier Assets).

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
#if UNITY_6000_3_OR_NEWER
using UnityEditor.Toolbars;
#endif

[InitializeOnLoad]
public static class GitSyncToolbar
{
    enum Status { Disabled, UpToDate, Checking, Behind, Pulling, Error }

    const string kBranchPath = "Git Sync/Branch";
    const string kStatusPath = "Git Sync/Status";
    const string kPullPath   = "Git Sync/Pull";

    static string Key(string k) => $"GitSync.{PlayerSettings.productName}.{k}";

    // État — muté uniquement sur le thread principal (via la file s_Main).
    static Status s_Status;
    static string s_Branch;
    static double s_Interval;
    static bool   s_AutoPullOnFocus;
    static bool   s_Busy;
    static bool   s_PendingAutoPull;
    static bool   s_EditorFocused = true;
    static double s_LastCheck;
    static string s_LastError = "";
    static readonly List<string> s_Branches = new List<string>();
    static string s_RepoRoot;
    static bool   s_RepoAvailable;

    static readonly ConcurrentQueue<Action> s_Main = new ConcurrentQueue<Action>();

    static GitSyncToolbar()
    {
        s_RepoRoot = Directory.GetParent(Application.dataPath)?.FullName;
        s_RepoAvailable = s_RepoRoot != null &&
            (Directory.Exists(Path.Combine(s_RepoRoot, ".git")) ||
             File.Exists(Path.Combine(s_RepoRoot, ".git")));

        s_Branch          = EditorPrefs.GetString(Key("branch"), "main");
        s_Interval        = EditorPrefs.GetFloat(Key("interval"), 30f);
        s_AutoPullOnFocus = EditorPrefs.GetBool(Key("autopull"), true);
        s_Status          = s_RepoAvailable ? Status.UpToDate : Status.Disabled;

        EditorApplication.update       += OnUpdate;
        EditorApplication.focusChanged += OnFocusChanged;

        if (s_RepoAvailable) RefreshBranchList();
    }

    // ---------------- Boucle principale ----------------
    static void OnUpdate()
    {
        while (s_Main.TryDequeue(out var act)) act();

        if (!s_RepoAvailable || s_Busy) return;
        if (s_Status != Status.UpToDate) return;                              // arrêt du poll dès Behind/Error
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isPlaying) return; // gate Play
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (EditorApplication.timeSinceStartup - s_LastCheck < s_Interval) return;

        s_LastCheck = EditorApplication.timeSinceStartup;
        StartCheck();
    }

    static void OnFocusChanged(bool focused)
    {
        s_EditorFocused = focused;
        if (focused && s_Status == Status.Behind && s_PendingAutoPull && s_AutoPullOnFocus)
        {
            s_PendingAutoPull = false;
            DoPull();                                                         // éditeur ramené au premier plan
        }
    }

    // ---------------- Vérification ----------------
    static void StartCheck()
    {
        s_Busy = true;
        s_Status = Status.Checking;
        RefreshToolbar();

        string branch = s_Branch;
        Task.Run(() =>
        {
            try
            {
                var fetch = RunGit($"fetch origin {branch} --quiet");
                if (fetch.Failed) { Post(() => OnCheckDone(false, fetch.Error)); return; }

                var rev = RunGit($"rev-list --count HEAD..origin/{branch}");
                if (rev.Failed) { Post(() => OnCheckDone(false, rev.Error)); return; }

                int.TryParse(rev.Out.Trim(), out int count);
                Post(() => OnCheckDone(count > 0, null));
            }
            catch (Exception ex) { Post(() => OnCheckDone(false, ex.Message)); }
        });
    }

    static void OnCheckDone(bool behind, string error)
    {
        s_Busy = false;

        if (error != null) { s_Status = Status.Error; s_LastError = error; RefreshToolbar(); return; }

        if (behind)
        {
            s_Status = Status.Behind;
            RefreshToolbar();

            if (s_EditorFocused)
            {
                s_PendingAutoPull = false;
                bool pull = EditorUtility.DisplayDialog(
                    "Code en retard",
                    $"La branche « {s_Branch} » a de nouveaux commits sur le dépôt distant.\n\n" +
                    "Récupérer ces commits maintenant ?",
                    "Pull",
                    "Cancel");
                if (pull) DoPull();
            }
            else
            {
                s_PendingAutoPull = true;                                     // pull au retour du focus
            }
        }
        else
        {
            s_Status = Status.UpToDate;                                       // reste en veille, poll continue
            RefreshToolbar();
        }
    }

    // ---------------- Pull ----------------
    static void DoPull()
    {
        if (s_Busy || !s_RepoAvailable) return;
        s_Busy = true;
        s_Status = Status.Pulling;
        RefreshToolbar();

        string branch = s_Branch;
        Task.Run(() =>
        {
            try
            {
                RunGit($"fetch origin {branch} --quiet");
                var merge = RunGit($"merge --ff-only origin/{branch}");
                Post(() => OnPullDone(merge));
            }
            catch (Exception ex)
            {
                Post(() => OnPullDone(new GitResult { Threw = true, Err = ex.Message }));
            }
        });
    }

    static void OnPullDone(GitResult merge)
    {
        s_Busy = false;

        if (merge.Failed)
        {
            s_Status = Status.Error;
            s_LastError = merge.Error;
            RefreshToolbar();
            EditorUtility.DisplayDialog(
                "Pull impossible",
                "Le fast-forward a échoué (l'historique local a peut-être divergé) :\n\n" +
                merge.Error + "\n\nRésous-le manuellement en ligne de commande.",
                "OK");
            return;
        }

        s_PendingAutoPull = false;
        s_Status = Status.UpToDate;
        s_LastCheck = EditorApplication.timeSinceStartup;
        RefreshToolbar();
        AssetDatabase.Refresh();                                             // réimport + recompilation des scripts récupérés
    }

    // ---------------- Liste des branches ----------------
    static void RefreshBranchList()
    {
        Task.Run(() =>
        {
            var res = RunGit("for-each-ref --format=%(refname:short) refs/remotes/origin");
            var list = new List<string>();
            if (!res.Failed)
            {
                foreach (var raw in res.Out.Split('\n'))
                {
                    var b = raw.Trim();
                    if (b.Length == 0 || b == "origin/HEAD") continue;
                    if (b.StartsWith("origin/")) b = b.Substring("origin/".Length);
                    if (b == "HEAD" || list.Contains(b)) continue;
                    list.Add(b);
                }
            }
            Post(() => { s_Branches.Clear(); s_Branches.AddRange(list); RefreshToolbar(); });
        });
    }

    static void SelectBranch(string b)
    {
        s_Branch = b;
        EditorPrefs.SetString(Key("branch"), b);
        s_PendingAutoPull = false;
        s_Status = s_RepoAvailable ? Status.UpToDate : Status.Disabled;      // réarme le poll
        s_LastCheck = 0;                                                     // vérifie bientôt
        RefreshToolbar();
    }

    // ---------------- Processus git ----------------
    struct GitResult
    {
        public int Code; public string Out; public string Err; public bool Threw;
        public bool Failed => Threw || Code != 0;
        public string Error => Threw ? Err : (Err.Length > 0 ? Err : $"git a renvoyé le code {Code}");
    }

    static GitResult RunGit(string args)
    {
        try
        {
            var psi = new ProcessStartInfo("git", args)
            {
                WorkingDirectory       = s_RepoRoot,
                RedirectStandardOutput = true,
                RedirectStandardError  = true,
                UseShellExecute        = false,
                CreateNoWindow         = true,
            };
            using (var p = Process.Start(psi))
            {
                string o = p.StandardOutput.ReadToEnd();
                string e = p.StandardError.ReadToEnd();
                p.WaitForExit();
                return new GitResult { Code = p.ExitCode, Out = o, Err = e.Trim(), Threw = false };
            }
        }
        catch (Exception ex)
        {
            return new GitResult { Threw = true, Err = "git introuvable ou illisible : " + ex.Message };
        }
    }

    static void Post(Action a) => s_Main.Enqueue(a);

    // ---------------- Barre d'outils (Unity 6.3+) ----------------
    static void RefreshToolbar()
    {
#if UNITY_6000_3_OR_NEWER
        MainToolbar.Refresh(kBranchPath);
        MainToolbar.Refresh(kStatusPath);
        MainToolbar.Refresh(kPullPath);
#endif
    }

#if UNITY_6000_3_OR_NEWER
    static string StatusText()
    {
        switch (s_Status)
        {
            case Status.Disabled: return "pas de dépôt git";
            case Status.UpToDate: return "à jour";
            case Status.Checking: return "vérification…";
            case Status.Behind:   return "en retard";
            case Status.Pulling:  return "pull…";
            case Status.Error:    return "erreur";
            default:              return "";
        }
    }

    static Texture2D StatusIcon()
    {
        string n = s_Status == Status.Behind ? "console.warnicon"
                 : s_Status == Status.Error  ? "console.erroricon"
                 : "console.infoicon";
        return EditorGUIUtility.IconContent(n).image as Texture2D;
    }

    [MainToolbarElement(kBranchPath, defaultDockPosition = MainToolbarDockPosition.Right)]
    static MainToolbarElement BranchDropdown()
    {
        string label = string.IsNullOrEmpty(s_Branch) ? "(branche)" : s_Branch;
        return new MainToolbarDropdown(new MainToolbarContent(label), ShowBranchMenu);
    }

    static void ShowBranchMenu(Rect rect)
    {
        var menu = new GenericMenu();
        if (s_Branches.Count == 0)
            menu.AddDisabledItem(new GUIContent("Aucune branche distante trouvée"));
        foreach (var b in s_Branches)
        {
            string captured = b;
            menu.AddItem(new GUIContent(b), b == s_Branch, () => SelectBranch(captured));
        }
        menu.AddSeparator("");
        menu.AddItem(new GUIContent("Rafraîchir la liste"), false, RefreshBranchList);
        menu.DropDown(rect);
    }

    [MainToolbarElement(kStatusPath, defaultDockPosition = MainToolbarDockPosition.Right)]
    static MainToolbarElement StatusLabel()
    {
        var icon = StatusIcon();
        var content = icon != null
            ? new MainToolbarContent(StatusText(), icon, s_LastError)
            : new MainToolbarContent(StatusText());
        return new MainToolbarLabel(content);
    }

    [MainToolbarElement(kPullPath, defaultDockPosition = MainToolbarDockPosition.Right)]
    static MainToolbarElement PullButton()
    {
        return new MainToolbarButton(new MainToolbarContent("Pull"), DoPull);
    }
#endif

    // ---------------- Préférences ----------------
    [SettingsProvider]
    static SettingsProvider CreateSettings()
    {
        return new SettingsProvider("Preferences/Git Sync", SettingsScope.User)
        {
            label = "Git Sync",
            guiHandler = _ =>
            {
                EditorGUILayout.LabelField("Dépôt", s_RepoAvailable ? s_RepoRoot : "aucun dépôt git détecté");
                EditorGUI.BeginChangeCheck();
                float interval = EditorGUILayout.FloatField("Intervalle (s)", (float)s_Interval);
                bool auto = EditorGUILayout.Toggle("Pull auto au focus", s_AutoPullOnFocus);
                if (EditorGUI.EndChangeCheck())
                {
                    s_Interval = Mathf.Max(1f, interval);
                    s_AutoPullOnFocus = auto;
                    EditorPrefs.SetFloat(Key("interval"), (float)s_Interval);
                    EditorPrefs.SetBool(Key("autopull"), auto);
                }
#if !UNITY_6000_3_OR_NEWER
                EditorGUILayout.HelpBox(
                    "Les contrôles dans la barre d'outils nécessitent Unity 6.3 (6000.3) ou plus récent. " +
                    "La logique de synchro fonctionne, mais sans UI de toolbar sur cette version.",
                    MessageType.Warning);
#endif
            }
        };
    }
}
