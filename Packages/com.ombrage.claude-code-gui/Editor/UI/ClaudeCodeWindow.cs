using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

using Newtonsoft.Json.Linq;

using UnityEditor;
using UnityEditor.UIElements;

using UnityEngine;
using UnityEngine.UIElements;

namespace Ombrage.ClaudeCodeGUI
{
    /// <summary>
    /// Fenêtre principale de l'interface Claude Code (UI Toolkit). Pilote le process `claude`
    /// en mode stream-json, gère l'historique de sessions et le rendu du chat.
    /// </summary>
    public partial class ClaudeCodeWindow : EditorWindow
    {
        #region Constants & prefs

        private const string WINDOW_TITLE        = "Claude Code";
        private const string PKG                 = "Packages/com.ombrage.claude-code-gui";
        private const string PREFS_EXECUTABLE    = "ClaudeCodeGUI_Executable";
        private const string PREFS_MODEL         = "ClaudeCodeGUI_Model";
        private const string PREFS_CUSTOM_MODEL  = "ClaudeCodeGUI_CustomModel";
        private const string PREFS_SYSTEM_PROMPT = "ClaudeCodeGUI_SystemPrompt";
        private const string PREFS_MAX_TURNS     = "ClaudeCodeGUI_MaxTurns";
        private const string PREFS_LIMIT_TURNS   = "ClaudeCodeGUI_LimitTurns";
        private const string PREFS_ALLOWED_TOOLS = "ClaudeCodeGUI_AllowedTools";
        private const string PREFS_PERMISSION    = "ClaudeCodeGUI_PermissionMode";
        private const string PREFS_EFFORT        = "ClaudeCodeGUI_Effort";
        private const string PREFS_INCLUDE_SEL   = "ClaudeCodeGUI_IncludeSelection";
        private const string PREFS_BYPASS        = "ClaudeCodeGUI_BypassPermissions";
        private const string PREFS_LOCK_RELOAD   = "ClaudeCodeGUI_LockReloadDuringRun";
        private const string PREFS_DEBUG         = "ClaudeCodeGUI_DebugEvents";
        private const string PREFS_SIDEBAR_WIDTH = "ClaudeCodeGUI_SidebarWidth";
        private const string PREFS_EXEC_MODE       = "ClaudeCodeGUI_ExecutionMode";
        private const string PREFS_WORKFLOW_URL    = "ClaudeCodeGUI_WorkflowUrl";
        private const string PREFS_WORKFLOW_PYTHON = "ClaudeCodeGUI_WorkflowPython";
        private const string PREFS_WORKFLOW_SCRIPT = "ClaudeCodeGUI_WorkflowScript";
        private const string PREFS_WORKFLOW_AUTOSTART = "ClaudeCodeGUI_WorkflowAutostart";
        private const string PREFS_WORKFLOW_EFFORT = "ClaudeCodeGUI_WorkflowEffort";

        // Profils d'effort du workflow : clé envoyée au démon ↔ libellé affiché.
        private static readonly string[] WORKFLOW_EFFORTS =
            { "simple", "moyen", "complexe", "tres_complexe" };
        private static readonly string[] WORKFLOW_EFFORT_LABELS =
            { "Simple", "Moyennement simple", "Compliqué", "Très compliqué" };
        private const int    MAX_PROMPT_HISTORY  = 50;

        private static readonly string[] PERMISSION_MODES =
            { "default", "plan", "acceptEdits", "bypassPermissions" };

        // "default" = ne pas passer --effort (laisse le réglage par défaut de la CLI).
        private static readonly string[] EFFORT_LEVELS =
            { "default", "low", "medium", "high", "xhigh", "max" };

        // Outils Claude Code courants proposés en cases à cocher (liste non exhaustive :
        // tout outil — y compris MCP "mcp__serveur__outil" — peut être ajouté manuellement).
        private static readonly string[] KNOWN_TOOLS =
        {
            "Read", "Write", "Edit", "MultiEdit", "Bash", "Glob", "Grep",
            "WebSearch", "WebFetch", "Task", "TodoWrite", "NotebookEdit",
            "AskUserQuestion", "Skill", "ExitPlanMode",
        };

        #endregion

        #region Settings state

        private string _executable   = "claude";
        private string _modelId      = "sonnet";
        private string _customModel  = "";
        private string _systemPrompt = "";
        private int    _maxTurns     = 10;
        private bool   _limitTurns;          // false = aucune limite (--max-turns non passé)
        private string _allowedTools = "Read,Grep,Glob";
        private string _permissionMode = "default";
        private string _effort = "default";
        private bool   _bypassPermissions;
        private bool   _includeSelection;
        private bool   _lockReloadDuringRun = true;
        private bool   _debugEvents;
        private float  _sidebarWidth = 240f;
        private string _executionMode = "direct";   // "direct" (CLI) | "workflow" (démon)
        private string _workflowUrl = "http://127.0.0.1:8787";
        // Démon workflow : chemins par défaut (ajustables dans Settings) pour l'auto-lancement.
        private string _workflowPython = @"C:\Users\Arthe\dev\agentic-workflow\.venv\Scripts\python.exe";
        private string _workflowScript = @"C:\Users\Arthe\dev\agentic-workflow\run.py";
        private bool   _workflowAutostart = true;
        private string _workflowEffort = "complexe";   // simple | moyen | complexe | tres_complexe

        private string EffectiveModel =>
            !string.IsNullOrWhiteSpace(_customModel) ? _customModel.Trim() : _modelId;

        #endregion

        #region Runtime state

        // Sessions
        private List<Session> _sessions = new();
        private Session       _activeSession;
        private string        _sessionFilter = "";
        [SerializeField] private string _activeSessionId;

        // Prompt
        [SerializeField] private List<string> _promptHistory = new();
        private int _historyIndex = -1;
        [SerializeField] private List<string> _contextPaths = new();

        // Engine
        private ClaudeProcessRunner _runner;
        private ClaudeStreamParser  _parser;
        private readonly MainThreadDispatcher _dispatcher = new();
        private string  _currentPrompt;
        private volatile bool _isRunning;
        private bool    _filesEdited; // un Edit/Write/MultiEdit a eu lieu pendant le run
        private readonly StringBuilder _currentResponse = new();

        // Renvoi automatique sur erreur réseau
        private const int MAX_NETWORK_RETRIES = 3;
        private bool _runHadNetworkError;
        private int  _retryAttempt;
        private int  _retryToken; // invalide les tentatives programmées si l'utilisateur reprend la main
        private double _runStartTime;  // pour afficher le temps écoulé
        private int    _thinkingTokens; // progression de la réflexion en cours
        private bool   _reloadLocked;  // recompilation Unity verrouillée pendant le run ?
        private bool   _suppressLog;   // true pendant la reconstruction depuis _log (ne pas ré-enregistrer)
        private bool   _newSessionAfterRun; // « Compacter » : ouvrir une nouvelle session en fin de run
        private bool   _deepResearch;       // préfixe les messages par /deep-research
        private bool   _rawSend;            // envoi interne (follow-up/retry/compact) : pas de préfixe

        private const string COMPACT_PROMPT =
            "Avant de clore cette session, mets à jour ta MÉMOIRE GLOBALE de projet (le fichier " +
            "MEMORY.md global indiqué en début de session) avec tes outils Write/Edit : ajoute un " +
            "résumé TRÈS court de cette session et les informations importantes à retenir pour la " +
            "suite (décisions, état d'avancement, points en cours), chaque entrée pointant vers l'id " +
            "de cette session. Reste concis. Confirme une fois fait.";

        // AskUserQuestion / follow-up
        private readonly HashSet<string> _handledAskIds = new();
        private string _pendingFollowUp;

        // Outils dont la permission a été refusée pendant le run courant (évite les doublons de carte).
        private readonly HashSet<string> _requestedPerms = new();

        // Streaming refs : content_block_index -> élément
        private readonly Dictionary<int, BlockRef> _streamBlocks = new();

        // Transcript sérialisé de la session AFFICHÉE : survit aux rechargements de domaine
        // (recompilation, mode Play). Référence le transcript de _activeSession (même liste),
        // lui-même gravé sur disque (Session.transcript) pour survivre à la fermeture.
        [SerializeField] private List<ChatEntry> _log = new();

        // --- Découplage run ↔ fenêtre (« continuer en arrière-plan ») ---
        // Un run reste lié à la session qui l'a lancé. Si l'utilisateur affiche une autre
        // session pendant le run, la sortie continue d'être enregistrée dans CETTE session
        // (jamais perdue, jamais attribuée à la session affichée) et gravée sur disque.
        private Session _runSession;              // session liée au run en cours (null si aucun)
        private VisualElement _displayRoot;       // racine de chat actuellement MONTÉE (affichée)
        private VisualElement _runRoot;           // racine de chat du run (détachée si arrière-plan)
        private List<ChatEntry> _recordInto;      // liste-cible de Record() (transcript concerné)
        private double _lastRunSave;              // throttle de sauvegarde disque pendant un run

        // Vrai quand un run tourne sur une session qui n'est PAS celle affichée (arrière-plan).
        private bool RunIsBackground =>
            _runSession != null && (_activeSession == null || _activeSession.id != _runSession.id);

        // Brouillon en cours de saisie : survit aux rechargements de domaine (sans ça, le
        // champ de saisie est vidé par une recompilation ou un passage en Play mode).
        [SerializeField] private string _draftPrompt = "";

        #endregion

        #region UI refs

        private VisualElement   _root;
        private VisualElement   _sidebarList;
        private VisualElement   _bulkBar;
        private Label           _bulkLabel;
        private readonly HashSet<string> _selectedIds = new();
        private ScrollView      _chat;
        private VisualElement   _chatContent;
        private TextField       _promptField;
        private Label           _statusLabel;
        private Label           _titleLabel;
        private Label           _contextLabel;
        private VisualElement   _settingsPanel;
        private VisualElement   _contextPanel;
        private VisualElement   _toolChips;
        private Button          _stopButton;
        private ToolbarButton   _copyButton;
        private AutocompletePopup _autocomplete;
        private VisualElement   _compileBanner;
        private Label           _compileBannerLabel;

        private sealed class BlockRef
        {
            public VisualElement Root;
            public Label         Content;
            public Foldout       Foldout;
            public string        Kind;
            public bool          Enriched; // tool_use déjà rempli depuis le message assistant
            public ChatEntry     Entry;    // entrée de transcript associée (persistance)
        }

        #endregion

        #region Menu & lifecycle

        [MenuItem("Window/Ombrage Tools/Claude Code (UITK) %#&k")]
        public static void ShowWindow()
        {
            var w = GetWindow<ClaudeCodeWindow>(WINDOW_TITLE);
            w.minSize = new Vector2(680, 440);
            w.Show();
        }

        private void OnEnable()
        {
            LoadPrefs();
            EditorApplication.update += OnEditorUpdate;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            _runner?.Kill();
            _wfCts?.Cancel(); // ne pas laisser une lecture SSE en arrière-plan
            UnlockReload(); // ne jamais laisser les assemblies verrouillées
            // Grave les transcripts avant fermeture/rechargement : rien n'est perdu (le run en
            // cours comme la session affichée). _log référence le transcript de _activeSession.
            FlushTranscript(_runSession);
            FlushTranscript(_activeSession);
            _activeSessionId = _activeSession?.id;
            SavePrefs();
        }

        public void CreateGUI()
        {
            BuildParser();
            _root = rootVisualElement;
            _root.Clear();
            _root.style.flexGrow = 1;       // le root doit remplir la fenêtre
            _root.style.minHeight = 0;
            _root.AddToClassList(EditorGUIUtility.isProSkin ? "cc-theme-dark" : "cc-theme-light");

            var uss = AssetDatabase.LoadAssetAtPath<StyleSheet>($"{PKG}/Editor/UI/uss/claude.uss");
            if (uss != null) _root.styleSheets.Add(uss);

            var row = new VisualElement();
            row.AddToClassList("cc-root");
            _root.Add(row);

            row.Add(BuildSidebar());
            row.Add(BuildResizer());
            row.Add(BuildMain());

            RefreshSessionList();
            if (!string.IsNullOrEmpty(_activeSessionId))
                _activeSession = _sessions.Find(s => s.id == _activeSessionId);

            // Après un rechargement de domaine (recompilation, mode Play), aucun run n'est actif
            // (la recompilation est verrouillée pendant un run). On relie _log au transcript de la
            // session affichée puis on le restaure : _log (état fenêtre, le plus frais) fait foi et
            // est regravé sur disque ; à défaut on repart du transcript disque de la session.
            if (_activeSession != null)
            {
                if (_log != null && _log.Count > 0)
                {
                    _activeSession.transcript = _log;        // l'état fenêtre (le plus frais) fait foi
                    SessionStore.LoadRunPointer(_activeSession);  // pointeur de run depuis le sidecar
                    SessionStore.SaveTranscript(_activeSession);  // regrave (préserve transcript + pointeur)
                }
                else
                {
                    SessionStore.EnsureTranscript(_activeSession); // charge le sidecar à la demande
                    _log = _activeSession.transcript;
                }
                _recordInto = _log;
                RebuildFromLog();
                MaybeShowResumeBanner(_activeSession); // run non terminé ? propose la reprise
            }
            else
            {
                _recordInto = _log;
            }
            RegisterDragAndDrop(_chat);
            RebuildContextTags();
            UpdateTitle();

            _autocomplete = new AutocompletePopup();
            _root.Add(_autocomplete); // en absolu, flotte au-dessus du reste

            // Restaure le brouillon saisi avant un rechargement de domaine (SetValueWithoutNotify
            // pour ne pas rouvrir l'autocomplétion si le texte se termine par « @… » ou « /… »).
            if (!string.IsNullOrEmpty(_draftPrompt))
                _promptField?.SetValueWithoutNotify(_draftPrompt);
        }

        private void LoadPrefs()
        {
            _executable     = EditorPrefs.GetString(PREFS_EXECUTABLE, "claude");
            _modelId        = EditorPrefs.GetString(PREFS_MODEL, "sonnet");
            _customModel    = EditorPrefs.GetString(PREFS_CUSTOM_MODEL, "");
            _systemPrompt   = EditorPrefs.GetString(PREFS_SYSTEM_PROMPT, "");
            _maxTurns       = EditorPrefs.GetInt(PREFS_MAX_TURNS, 10);
            _limitTurns     = EditorPrefs.GetBool(PREFS_LIMIT_TURNS, false);
            _allowedTools   = EditorPrefs.GetString(PREFS_ALLOWED_TOOLS, "Read,Grep,Glob");
            _permissionMode = EditorPrefs.GetString(PREFS_PERMISSION, "default");
            _effort         = EditorPrefs.GetString(PREFS_EFFORT, "default");
            _includeSelection = EditorPrefs.GetBool(PREFS_INCLUDE_SEL, false);
            _bypassPermissions = EditorPrefs.GetBool(PREFS_BYPASS, false);
            _lockReloadDuringRun = EditorPrefs.GetBool(PREFS_LOCK_RELOAD, true);
            _debugEvents    = EditorPrefs.GetBool(PREFS_DEBUG, false);
            _sidebarWidth   = EditorPrefs.GetFloat(PREFS_SIDEBAR_WIDTH, 240f);
            _executionMode  = EditorPrefs.GetString(PREFS_EXEC_MODE, "direct");
            _workflowUrl    = EditorPrefs.GetString(PREFS_WORKFLOW_URL, "http://127.0.0.1:8787");
            _workflowPython = EditorPrefs.GetString(PREFS_WORKFLOW_PYTHON, _workflowPython);
            _workflowScript = EditorPrefs.GetString(PREFS_WORKFLOW_SCRIPT, _workflowScript);
            _workflowAutostart = EditorPrefs.GetBool(PREFS_WORKFLOW_AUTOSTART, true);
            _workflowEffort = EditorPrefs.GetString(PREFS_WORKFLOW_EFFORT, "complexe");
        }

        private void SavePrefs()
        {
            EditorPrefs.SetString(PREFS_EXECUTABLE, _executable);
            EditorPrefs.SetString(PREFS_MODEL, _modelId);
            EditorPrefs.SetString(PREFS_CUSTOM_MODEL, _customModel);
            EditorPrefs.SetString(PREFS_SYSTEM_PROMPT, _systemPrompt);
            EditorPrefs.SetInt(PREFS_MAX_TURNS, _maxTurns);
            EditorPrefs.SetBool(PREFS_LIMIT_TURNS, _limitTurns);
            EditorPrefs.SetString(PREFS_ALLOWED_TOOLS, _allowedTools);
            EditorPrefs.SetString(PREFS_PERMISSION, _permissionMode);
            EditorPrefs.SetString(PREFS_EFFORT, _effort);
            EditorPrefs.SetBool(PREFS_INCLUDE_SEL, _includeSelection);
            EditorPrefs.SetBool(PREFS_BYPASS, _bypassPermissions);
            EditorPrefs.SetBool(PREFS_LOCK_RELOAD, _lockReloadDuringRun);
            EditorPrefs.SetBool(PREFS_DEBUG, _debugEvents);
            EditorPrefs.SetFloat(PREFS_SIDEBAR_WIDTH, _sidebarWidth);
            EditorPrefs.SetString(PREFS_EXEC_MODE, _executionMode);
            EditorPrefs.SetString(PREFS_WORKFLOW_URL, _workflowUrl);
            EditorPrefs.SetString(PREFS_WORKFLOW_PYTHON, _workflowPython);
            EditorPrefs.SetString(PREFS_WORKFLOW_SCRIPT, _workflowScript);
            EditorPrefs.SetBool(PREFS_WORKFLOW_AUTOSTART, _workflowAutostart);
            EditorPrefs.SetString(PREFS_WORKFLOW_EFFORT, _workflowEffort);
        }

        private void RefreshSessionList()
        {
            _sessions = SessionStore.LoadAll();
            RebuildSidebarList();
        }

        #endregion

        #region Editor update (main-thread pump)

        private void OnEditorUpdate()
        {
            _dispatcher.Drain();

            // Persistance continue du run : grave le transcript ~1×/s. Ainsi, fermer le tool ou
            // Unity en plein run ne perd au plus qu'une seconde de sortie (jamais toute la session).
            if (_isRunning && _runSession != null)
            {
                double now = EditorApplication.timeSinceStartup;
                if (now - _lastRunSave >= 1.0)
                {
                    _lastRunSave = now;
                    try { SessionStore.SaveTranscript(_runSession); } catch { } // léger : sidecar seul
                }
            }

            if (!_isRunning && !string.IsNullOrEmpty(_pendingFollowUp))
            {
                string text = _pendingFollowUp;
                _pendingFollowUp = null;
                SetPrompt(text);
                _rawSend = true;
                ExecutePrompt();
            }

            if (_statusLabel != null)
            {
                if (_isRunning)
                {
                    string[] spin = { "⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏" };
                    int idx = (int)(EditorApplication.timeSinceStartup * 10) % spin.Length;
                    int elapsed = (int)(EditorApplication.timeSinceStartup - _runStartTime);
                    string s = $"{spin[idx]} En cours… {elapsed}s";
                    if (_thinkingTokens > 0) s += $" · 💭 {_thinkingTokens} tok";
                    _statusLabel.text = s;
                }
                else if (_statusLabel.text.Contains("En cours"))
                {
                    _statusLabel.text = "";
                }
            }

            if (_compileBanner != null)
            {
                bool has = CompileErrorTracker.HasErrors();
                _compileBanner.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
                if (has)
                {
                    int count = CompileErrorTracker.GetErrors()
                        .Split('\n').Count(l => !string.IsNullOrWhiteSpace(l));
                    _compileBannerLabel.text = $"⚠ {count} erreur(s) de compilation";
                }
            }
        }

        #endregion

        #region Sidebar

        private VisualElement BuildSidebar()
        {
            var sidebar = new VisualElement();
            sidebar.AddToClassList("cc-sidebar");
            sidebar.style.width = _sidebarWidth;

            var header = new VisualElement();
            header.AddToClassList("cc-sidebar__header");
            var newBtn = new Button(CreateNewSession) { text = "+ Nouvelle session" };
            newBtn.style.flexGrow = 1;
            header.Add(newBtn);
            sidebar.Add(header);

            var search = new ToolbarSearchField();
            search.AddToClassList("cc-search");
            search.RegisterValueChangedCallback(evt =>
            {
                _sessionFilter = evt.newValue;
                RebuildSidebarList();
            });
            sidebar.Add(search);

            // Barre d'action groupée (visible uniquement quand une sélection multiple existe).
            _bulkBar = new VisualElement();
            _bulkBar.style.flexDirection = FlexDirection.Row;
            _bulkBar.style.alignItems = Align.Center;
            _bulkBar.style.paddingLeft = 6;
            _bulkBar.style.paddingRight = 6;
            _bulkBar.style.paddingTop = 2;
            _bulkBar.style.paddingBottom = 2;
            _bulkBar.style.display = DisplayStyle.None;

            _bulkLabel = new Label();
            _bulkLabel.style.flexGrow = 1;
            _bulkBar.Add(_bulkLabel);

            var bulkDelete = new Button(DeleteSelectedSessions) { text = "Supprimer" };
            _bulkBar.Add(bulkDelete);

            var bulkClear = new Button(() => { _selectedIds.Clear(); RebuildSidebarList(); }) { text = "Annuler" };
            _bulkBar.Add(bulkClear);

            sidebar.Add(_bulkBar);

            _sidebarList = new ScrollView(ScrollViewMode.Vertical);
            _sidebarList.AddToClassList("cc-sidebar__list");
            sidebar.Add(_sidebarList);

            return sidebar;
        }

        private void RebuildSidebarList()
        {
            if (_sidebarList == null) return;
            _sidebarList.Clear();

            // Purge les ids sélectionnés qui n'existent plus.
            _selectedIds.RemoveWhere(id => _sessions.All(s => s.id != id));

            foreach (var session in _sessions)
            {
                if (!session.Matches(_sessionFilter)) continue;
                _sidebarList.Add(BuildSessionRow(session));
            }

            UpdateBulkBar();
        }

        private void UpdateBulkBar()
        {
            if (_bulkBar == null) return;
            int n = _selectedIds.Count;
            _bulkBar.style.display = n > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            if (n > 0) _bulkLabel.text = $"{n} sélectionnée(s)";
        }

        private VisualElement BuildSessionRow(Session session)
        {
            var item = new VisualElement();
            item.AddToClassList("cc-session");
            if (_activeSession != null && _activeSession.id == session.id)
                item.AddToClassList("cc-session--active");
            bool selected = _selectedIds.Contains(session.id);
            if (selected)
            {
                // Surbrillance de sélection multiple (inline, indépendante de l'USS).
                item.style.backgroundColor = new Color(0.22f, 0.42f, 0.70f, 0.55f);
            }

            var row = new VisualElement();
            row.AddToClassList("cc-session__row");

            var fav = new Label(session.favorite ? "★" : "☆");
            fav.AddToClassList("cc-session__fav");
            fav.RegisterCallback<MouseDownEvent>(evt =>
            {
                session.favorite = !session.favorite;
                SessionStore.Save(session);
                RefreshSessionList();
                evt.StopPropagation();
            });
            row.Add(fav);

            var title = new Label(session.GetDisplayTitle());
            title.AddToClassList("cc-session__title");
            row.Add(title);

            // Pastille « run en cours » : repère une session qui travaille (y compris en arrière-plan).
            if (_runSession != null && _runSession.id == session.id)
            {
                var running = new Label("●");
                running.tooltip = "Run en cours dans cette session";
                running.style.color = new Color(0.36f, 0.72f, 0.36f);
                running.style.marginLeft = 4;
                running.style.unityTextAlign = TextAnchor.MiddleCenter;
                row.Add(running);
            }
            item.Add(row);

            var meta = new VisualElement();
            meta.AddToClassList("cc-session__meta");
            meta.Add(new Label($"{session.exchanges.Count} échanges"));
            meta.Add(new Label(session.GetDateDisplay()));
            item.Add(meta);

            item.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt.button == 0)
                {
                    if (evt.ctrlKey || evt.commandKey)
                    {
                        // Ctrl/Cmd+clic : (dé)sélectionne sans ouvrir la session.
                        if (!_selectedIds.Remove(session.id)) _selectedIds.Add(session.id);
                        RebuildSidebarList();
                    }
                    else
                    {
                        if (_selectedIds.Count > 0) _selectedIds.Clear();
                        LoadSession(session);
                    }
                    evt.StopPropagation();
                }
                else if (evt.button == 1) { ShowSessionContextMenu(session); evt.StopPropagation(); }
            });

            return item;
        }

        private void ShowSessionContextMenu(Session session)
        {
            var menu = new GenericMenu();
            menu.AddItem(new GUIContent("Renommer"), false, () =>
            {
                string current = session.GetDisplayTitle();
                ClaudeTextPrompt.Show("Renommer la session", current, newName =>
                {
                    session.title = newName;
                    SessionStore.Save(session);
                    RefreshSessionList();
                    UpdateTitle();
                });
            });
            menu.AddItem(new GUIContent(session.favorite ? "Retirer des favoris" : "Ajouter aux favoris"),
                false, () =>
            {
                session.favorite = !session.favorite;
                SessionStore.Save(session);
                RefreshSessionList();
            });
            menu.AddItem(new GUIContent("Exporter en Markdown"), false, () => ExportSession(session));
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Supprimer"), false, () =>
            {
                if (EditorUtility.DisplayDialog("Supprimer la session",
                        $"Supprimer « {session.GetDisplayTitle()} » ?", "Supprimer", "Annuler"))
                {
                    DeleteSessions(new List<Session> { session });
                }
            });

            // Suppression groupée si une sélection multiple est active.
            if (_selectedIds.Count > 0)
            {
                menu.AddItem(new GUIContent($"Supprimer les {_selectedIds.Count} sélectionnée(s)"),
                    false, DeleteSelectedSessions);
            }

            menu.ShowAsContext();
        }

        private void DeleteSelectedSessions()
        {
            var toDelete = _sessions.Where(s => _selectedIds.Contains(s.id)).ToList();
            if (toDelete.Count == 0) return;

            if (!EditorUtility.DisplayDialog("Supprimer les sessions",
                    $"Supprimer {toDelete.Count} session(s) ? Cette action est irréversible.",
                    "Supprimer", "Annuler"))
                return;

            DeleteSessions(toDelete);
        }

        private void DeleteSessions(List<Session> sessions)
        {
            foreach (var s in sessions)
            {
                SessionStore.Delete(s);
                _selectedIds.Remove(s.id);
                // Si on supprime la session d'un run en cours, on coupe le run SANS graver (sinon
                // FlushTranscript recréerait le fichier qu'on vient de supprimer).
                if (_runSession?.id == s.id)
                {
                    _retryToken++; _runHadNetworkError = false; _newSessionAfterRun = false;
                    _wfCts?.Cancel(); _wfAwaitingCheckpoint = false;
                    _runner?.Kill();
                    _isRunning = false; UnlockReload();
                    _runSession = null; _runRoot = null;
                    _chatContent = _displayRoot;
                }
                if (_activeSession?.id == s.id)
                {
                    _activeSession = null;
                    _log = new List<ChatEntry>();
                    _recordInto = _log;
                    ClearChat();
                }
            }
            RefreshSessionList();
            UpdateTitle();
        }

        private void ExportSession(Session session)
        {
            string path = EditorUtility.SaveFilePanel("Exporter la session",
                "", $"{session.GetDisplayTitle()}.md", "md");
            if (string.IsNullOrEmpty(path)) return;

            var sb = new StringBuilder();
            sb.AppendLine($"# {session.GetDisplayTitle()}");
            sb.AppendLine($"_Modèle : {session.model} — {session.exchanges.Count} échanges_\n");
            foreach (var ex in session.exchanges)
            {
                sb.AppendLine($"## 🧑 {ex.prompt}\n");
                sb.AppendLine(ex.response ?? "");
                sb.AppendLine("\n---\n");
            }
            File.WriteAllText(path, sb.ToString());
            EditorUtility.RevealInFinder(path);
        }

        #endregion

        #region Resizer

        private VisualElement BuildResizer()
        {
            var handle = new VisualElement();
            handle.AddToClassList("cc-resizer");
            bool dragging = false;

            handle.RegisterCallback<PointerDownEvent>(evt =>
            {
                dragging = true;
                handle.CapturePointer(evt.pointerId);
                evt.StopPropagation();
            });
            handle.RegisterCallback<PointerMoveEvent>(evt =>
            {
                if (!dragging) return;
                float w = Mathf.Clamp(evt.position.x, 160f, 460f);
                _sidebarWidth = w;
                var sidebar = _root.Q(className: "cc-sidebar");
                if (sidebar != null) sidebar.style.width = w;
            });
            handle.RegisterCallback<PointerUpEvent>(evt =>
            {
                if (!dragging) return;
                dragging = false;
                handle.ReleasePointer(evt.pointerId);
                SavePrefs();
            });
            return handle;
        }

        #endregion

        #region Main column

        private VisualElement BuildMain()
        {
            var main = new VisualElement();
            main.AddToClassList("cc-main");

            main.Add(BuildToolbar());
            main.Add(_settingsPanel = BuildSettings());
            _settingsPanel.style.display = DisplayStyle.None;
            main.Add(_contextPanel = BuildContext());

            _chat = new ScrollView(ScrollViewMode.Vertical);
            _chat.AddToClassList("cc-chat");
            // Le ScrollView contient une unique racine d'affichage, échangeable d'une session
            // à l'autre. Permet de garder vivante (détachée) la racine d'un run en arrière-plan
            // et de la re-monter telle quelle au retour, sans reconstruction ni perte de streaming.
            _displayRoot = NewChatRoot();
            _chat.Add(_displayRoot);
            _chatContent = _displayRoot;   // cible des fabriques (= affichage tant qu'aucun run)
            main.Add(_chat);

            main.Add(BuildCompileBanner());
            main.Add(BuildInput());
            return main;
        }

        // Bannière affichée quand Unity a des erreurs de compilation, pour les renvoyer à Claude.
        private VisualElement BuildCompileBanner()
        {
            _compileBanner = new VisualElement();
            _compileBanner.AddToClassList("cc-compile");
            _compileBanner.style.display = DisplayStyle.None;

            _compileBannerLabel = new Label("⚠ Erreurs de compilation");
            _compileBannerLabel.style.flexGrow = 1;
            _compileBanner.Add(_compileBannerLabel);

            _compileBanner.Add(new Button(SendCompileErrorsToClaude) { text = "Corriger avec Claude" });
            _compileBanner.Add(new Button(() => CompileErrorTracker.Clear()) { text = "Ignorer" });
            return _compileBanner;
        }

        private void SendCompileErrorsToClaude()
        {
            string errors = CompileErrorTracker.GetErrors();
            if (string.IsNullOrWhiteSpace(errors)) return;
            CompileErrorTracker.Clear();
            SetPrompt("J'ai ces erreurs de compilation Unity, corrige-les :\n\n" + errors);
            ExecutePrompt();
        }

        private VisualElement BuildToolbar()
        {
            var bar = new Toolbar();
            bar.AddToClassList("cc-toolbar");

            _titleLabel = new Label("Aucune session");
            _titleLabel.AddToClassList("cc-toolbar__title");
            bar.Add(_titleLabel);

            _contextLabel = new Label("");
            _contextLabel.style.minWidth = 64;
            _contextLabel.tooltip =
                "Taille du contexte envoyé au dernier tour (tokens). Au-delà de ~200k, la latence " +
                "et le coût montent : pensez à « Compacter → nouvelle session ».";
            bar.Add(_contextLabel);

            _statusLabel = new Label("");
            _statusLabel.style.minWidth = 200;
            _statusLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            bar.Add(_statusLabel);

            bar.Add(new ToolbarButton(() => SetAllFoldouts(false)) { text = "▶" });
            bar.Add(new ToolbarButton(() => SetAllFoldouts(true)) { text = "▼" });
            _copyButton = new ToolbarButton(CopyLastResponse) { text = "⎘ Copier" };
            bar.Add(_copyButton);
            _stopButton = new ToolbarButton(KillProcess) { text = "■ Stop" };
            bar.Add(_stopButton);
            bar.Add(new ToolbarButton(ClearChat) { text = "Clear" });
            bar.Add(new ToolbarButton(ShowMemoryMenu) { text = "🧠 Mémoire ▾" });
            bar.Add(new ToolbarButton(CompactAndNewSession) { text = "🧹 Compacter" });

            var research = new ToolbarToggle { text = "🔎 Recherche", value = _deepResearch };
            research.tooltip = 
                "Recherche approfondie : préfixe vos messages par /deep-research (skill multi-sources " +
                "avec rapport sourcé). Autorise automatiquement l'outil Skill. À désactiver pour revenir " +
                "aux réponses normales (le deep research est long et coûteux).";
            research.RegisterValueChangedCallback(e =>
            {
                _deepResearch = e.newValue;
                if (_deepResearch && !_bypassPermissions)
                {
                    AddAllowedTool("Skill"); // nécessaire pour exécuter le skill
                    RebuildToolChips();
                }
            });
            bar.Add(research);
            bar.Add(new ToolbarButton(ShowSnippetsMenu) { text = "Snippets ▾" });

            var settingsToggle = new ToolbarToggle { text = "⚙" };
            settingsToggle.RegisterValueChangedCallback(evt =>
                _settingsPanel.style.display = evt.newValue ? DisplayStyle.Flex : DisplayStyle.None);
            bar.Add(settingsToggle);

            return bar;
        }

        private VisualElement BuildSettings()
        {
            var panel = new VisualElement();
            panel.AddToClassList("cc-settings");

            var execField = new TextField("Exécutable Claude") { value = _executable };
            execField.RegisterValueChangedCallback(e => { _executable = e.newValue; SavePrefs(); });
            panel.Add(execField);

            // Mode d'exécution : Chat direct (CLI) ou Workflow (démon agentique multi-agents).
            var modeChoices = new List<string> { "Chat direct (CLI)", "Workflow (démon agentique)" };
            var modeDrop = new DropdownField("Mode d'exécution", modeChoices,
                _executionMode == "workflow" ? 1 : 0);
            modeDrop.tooltip =
                "Chat direct : pilote la CLI claude (toutes les features). " +
                "Workflow : route la demande vers le démon d'orchestration local (pipeline " +
                "multi-agents à checkpoints). Les réglages ci-dessous (modèle, effort, outils…) " +
                "ne s'appliquent qu'au Chat direct.";
            var wfUrl = new TextField("URL du démon workflow") { value = _workflowUrl };
            wfUrl.tooltip =
                "Démon local d'orchestration (ex. http://127.0.0.1:8787). Démarré automatiquement " +
                "par l'outil s'il ne tourne pas, avec AGENTIC_WORKSPACE = racine de ce projet Unity.";
            var wfAuto = new Toggle("Démarrer le démon automatiquement") { value = _workflowAutostart };
            wfAuto.tooltip = "Si le démon n'est pas joignable à l'envoi, l'outil le lance (Python + run.py ci-dessous).";
            var wfPy = new TextField("Démon : Python (.venv)") { value = _workflowPython };
            wfPy.tooltip = "Chemin de l'exécutable Python du démon, ex. …\\agentic-workflow\\.venv\\Scripts\\python.exe";
            var wfScript = new TextField("Démon : script run.py") { value = _workflowScript };
            wfScript.tooltip = "Chemin du script run.py du noyau d'orchestration.";

            int effIdx = Mathf.Max(0, Array.IndexOf(WORKFLOW_EFFORTS, _workflowEffort));
            var wfEffort = new DropdownField("Niveau d'effort", new List<string>(WORKFLOW_EFFORT_LABELS), effIdx);
            wfEffort.tooltip =
                "Allège le pipeline pour gagner en vitesse. Simple : implémentation directe (sans " +
                "validation, recherche web, conception ni documentation, aucun checkpoint). Moyennement " +
                "simple : conception légère (1 critique). Compliqué : pipeline complet (panel critique, " +
                "documentation). Très compliqué : pipeline complet, plus de révisions, effort modèle maximal.";
            wfEffort.RegisterValueChangedCallback(e =>
            {
                int i = Array.IndexOf(WORKFLOW_EFFORT_LABELS, e.newValue);
                if (i >= 0) { _workflowEffort = WORKFLOW_EFFORTS[i]; SavePrefs(); }
            });

            void EnableWorkflowFields()
            {
                bool wf = _executionMode == "workflow";
                wfUrl.SetEnabled(wf);
                wfEffort.SetEnabled(wf);
                wfAuto.SetEnabled(wf);
                wfPy.SetEnabled(wf && _workflowAutostart);
                wfScript.SetEnabled(wf && _workflowAutostart);
            }

            modeDrop.RegisterValueChangedCallback(e =>
            {
                _executionMode = modeChoices.IndexOf(e.newValue) == 1 ? "workflow" : "direct";
                EnableWorkflowFields();
                SavePrefs();
            });
            wfUrl.RegisterValueChangedCallback(e => { _workflowUrl = e.newValue; SavePrefs(); });
            wfAuto.RegisterValueChangedCallback(e => { _workflowAutostart = e.newValue; EnableWorkflowFields(); SavePrefs(); });
            wfPy.RegisterValueChangedCallback(e => { _workflowPython = e.newValue; SavePrefs(); });
            wfScript.RegisterValueChangedCallback(e => { _workflowScript = e.newValue; SavePrefs(); });

			var stopWorkflow = new Button() { text = "Arrêter le démon" };
			stopWorkflow.clicked += StopWorkflow;
			
            panel.Add(modeDrop);
            panel.Add(wfEffort);
            panel.Add(wfUrl);
            panel.Add(wfAuto);
			panel.Add(stopWorkflow);

			panel.Add(wfPy);
            panel.Add(wfScript);
			
			EnableWorkflowFields();

            var modelChoices = new List<string>();
            var modelEntries = ModelCatalog.BuildList();
            foreach (var m in modelEntries) modelChoices.Add(m.Label);
            int selIdx = modelEntries.FindIndex(m => m.Id == _modelId);
            if (selIdx < 0) selIdx = 0;
            var modelDrop = new DropdownField("Modèle", modelChoices, Mathf.Max(0, selIdx));
            modelDrop.RegisterValueChangedCallback(e =>
            {
                int i = modelChoices.IndexOf(e.newValue);
                if (i >= 0 && i < modelEntries.Count) { _modelId = modelEntries[i].Id; SavePrefs(); }
            });
            panel.Add(modelDrop);

            var customModel = new TextField("Modèle personnalisé") { value = _customModel };
            customModel.tooltip = "Si rempli, remplace le modèle ci-dessus (ID complet ou alias).";
            customModel.RegisterValueChangedCallback(e => { _customModel = e.newValue; SavePrefs(); });
            panel.Add(customModel);

            var permChoices = new List<string>(PERMISSION_MODES);
            int permIdx = Mathf.Max(0, permChoices.IndexOf(_permissionMode));
            var permDrop = new DropdownField("Mode de permission", permChoices, permIdx);
            permDrop.RegisterValueChangedCallback(e => { _permissionMode = e.newValue; SavePrefs(); });
            panel.Add(permDrop);

            var effortChoices = new List<string>(EFFORT_LEVELS);
            int effortIdx = Mathf.Max(0, effortChoices.IndexOf(_effort));
            var effortDrop = new DropdownField("Effort cible", effortChoices, effortIdx);
            effortDrop.tooltip = "Niveau d'effort du modèle (low → max). « default » laisse le réglage de la CLI.";
            effortDrop.RegisterValueChangedCallback(e => { _effort = e.newValue; SavePrefs(); });
            panel.Add(effortDrop);

			var limitTurns = new Toggle("Limiter le nombre de tours") { value = _limitTurns };
            limitTurns.tooltip =
                "Décoché (défaut) : aucune limite, Claude enchaîne les outils jusqu'à terminer. " +
                "Coché : s'arrête après « Max tours » allers-retours (--max-turns) — la fin par limite " +
                "n'est PAS une erreur ; tapez « continue » pour reprendre.";
            var turns = new SliderInt("Max tours", 1, 100) { value = _maxTurns, showInputField = true };
            turns.tooltip = "Nombre maximum d'allers-retours (appels d'outils) que Claude peut enchaîner pour une réponse, avant de s'arrêter (--max-turns). Ignoré si la limite est désactivée.";
            turns.SetEnabled(_limitTurns);
            limitTurns.RegisterValueChangedCallback(e => { _limitTurns = e.newValue; turns.SetEnabled(e.newValue); SavePrefs(); });
            turns.RegisterValueChangedCallback(e => { _maxTurns = e.newValue; SavePrefs(); });
            panel.Add(limitTurns);
            panel.Add(turns);

            var bypass = new Toggle("Autoriser tous les outils (bypass)") { value = _bypassPermissions };
            bypass.tooltip =
                "En mode -p, un outil non coché est REFUSÉ sans possibilité de l'autoriser à la volée " +
                "(WebSearch, Skill, ExitPlanMode…). Activé, passe --dangerously-skip-permissions : Claude " +
                "peut utiliser TOUS les outils sans blocage. La validation des modifications de script " +
                "repose alors sur ton system prompt (demande via AskUserQuestion). Ignore la liste ci-dessous.";
            bypass.RegisterValueChangedCallback(e =>
            {
                _bypassPermissions = e.newValue;
                if (_toolChips != null) _toolChips.SetEnabled(!_bypassPermissions);
                SavePrefs();
            });
            panel.Add(bypass);

            panel.Add(BuildToolsSection());
            if (_toolChips != null) _toolChips.SetEnabled(!_bypassPermissions);

            var sys = new TextField("System prompt") { value = _systemPrompt, multiline = true };
            sys.tooltip = "Instructions système ajoutées en tête de session (ton, contraintes, conventions du projet). Appliqué au premier message d'une nouvelle session (--system-prompt).";
            sys.style.minHeight = 44;
            sys.RegisterValueChangedCallback(e => { _systemPrompt = e.newValue; SavePrefs(); });
            panel.Add(sys);

            var lockReload = new Toggle("Empêcher la recompilation pendant un run") { value = _lockReloadDuringRun };
            lockReload.tooltip =
                "Verrouille le rechargement du domaine Unity (LockReloadAssemblies) tant que Claude " +
                "travaille : une recompilation déclenchée pendant un run le tuerait. La recompilation " +
                "est différée jusqu'à la fin de l'échange.";
            lockReload.RegisterValueChangedCallback(e => { _lockReloadDuringRun = e.newValue; SavePrefs(); });
            panel.Add(lockReload);

            var dbg = new Toggle("Debug events (Console)") { value = _debugEvents };
            dbg.tooltip = "Journalise dans la Console Unity les lignes JSON brutes reçues de la CLI Claude. À activer uniquement pour diagnostiquer un problème.";
            dbg.RegisterValueChangedCallback(e =>
            {
                _debugEvents = e.newValue;
                if (_parser != null) _parser.DebugLogging = _debugEvents;
                SavePrefs();
            });
            panel.Add(dbg);

            var btnRow = new VisualElement();
            btnRow.AddToClassList("cc-settings__row");
            btnRow.style.justifyContent = Justify.FlexEnd;
            btnRow.Add(new Button(ValidateExecutable) { text = "Tester l'exécutable" });
            panel.Add(btnRow);

            return panel;
        }

        private void ValidateExecutable()
        {
            bool ok = ClaudeProcessRunner.TryValidate(_executable, out var version, out var error);
            if (ok) EditorUtility.DisplayDialog("Claude", $"OK : {version}", "Fermer");
            else    EditorUtility.DisplayDialog("Claude", $"Introuvable : {error}", "Fermer");
        }

        private VisualElement BuildToolsSection()
        {
            var section = new VisualElement();

            var header = new Label("Outils autorisés");
            header.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.style.marginTop = 4;
            section.Add(header);

            _toolChips = new VisualElement();
            _toolChips.style.flexDirection = FlexDirection.Row;
            _toolChips.style.flexWrap = Wrap.Wrap;
            section.Add(_toolChips);
            RebuildToolChips();

            var addRow = new VisualElement();
            addRow.AddToClassList("cc-settings__row");
            var addField = new TextField
            {
                tooltip = "Nom d'un outil (ex : Bash, WebSearch, mcp__serveur__outil)",
            };
            addField.style.flexGrow = 1;
            void Commit()
            {
                string t = (addField.value ?? "").Trim();
                if (string.IsNullOrEmpty(t)) return;
                AddAllowedTool(t);
                addField.value = "";
                RebuildToolChips();
            }
            addField.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter) { Commit(); e.StopPropagation(); }
            });
            addRow.Add(addField);
            addRow.Add(new Button(Commit) { text = "+ Ajouter" });
            section.Add(addRow);

            return section;
        }

        private void RebuildToolChips()
        {
            if (_toolChips == null) return;
            _toolChips.Clear();

            var allowed = GetAllowedList();
            var set = new HashSet<string>(allowed, StringComparer.OrdinalIgnoreCase);

            // Outils connus + tout outil personnalisé déjà autorisé (pour pouvoir le décocher).
            var display = new List<string>(KNOWN_TOOLS);
            foreach (var t in allowed)
                if (!display.Any(x => string.Equals(x, t, StringComparison.OrdinalIgnoreCase)))
                    display.Add(t);

            foreach (string tool in display)
            {
                string captured = tool;
                var toggle = new Toggle(tool) { value = set.Contains(tool) };
                toggle.tooltip = ToolDescriptions.GetToolHelp(tool);
                toggle.style.marginRight = 10;
                toggle.RegisterValueChangedCallback(e =>
                {
                    if (e.newValue) AddAllowedTool(captured);
                    else            RemoveAllowedTool(captured);
                });
                _toolChips.Add(toggle);
            }
        }

        private List<string> GetAllowedList() =>
            (_allowedTools ?? "")
                .Split(',')
                .Select(s => s.Trim())
                .Where(s => s.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private void AddAllowedTool(string tool)
        {
            var list = GetAllowedList();
            if (!list.Any(x => string.Equals(x, tool, StringComparison.OrdinalIgnoreCase)))
                list.Add(tool);
            _allowedTools = string.Join(",", list);
            SavePrefs();
        }

        private void RemoveAllowedTool(string tool)
        {
            var list = GetAllowedList();
            list.RemoveAll(x => string.Equals(x, tool, StringComparison.OrdinalIgnoreCase));
            _allowedTools = string.Join(",", list);
            SavePrefs();
        }

        private VisualElement BuildContext()
        {
            var panel = new VisualElement();
            panel.AddToClassList("cc-context");

            var header = new VisualElement();
            header.AddToClassList("cc-settings__row");
            var lbl = new Label("Fichiers contexte");
            lbl.style.flexGrow = 1;
            lbl.style.unityFontStyleAndWeight = FontStyle.Bold;
            header.Add(lbl);
            header.Add(new Button(AddSelectedAssets) { text = "+ Sélection" });
            header.Add(new Button(() => { _contextPaths.Clear(); RebuildContextTags(); }) { text = "Clear" });
            panel.Add(header);

            var autoSel = new Toggle("Inclure la sélection automatiquement") { value = _includeSelection };
            autoSel.tooltip = "À chaque envoi, ajoute au contexte les assets actuellement sélectionnés (Project/Hierarchy).";
            autoSel.RegisterValueChangedCallback(e => { _includeSelection = e.newValue; SavePrefs(); });
            panel.Add(autoSel);

            var tags = new VisualElement { name = "context-tags" };
            tags.AddToClassList("cc-context__tags");
            panel.Add(tags);

            return panel;
        }

        private void RebuildContextTags()
        {
            var tags = _contextPanel?.Q("context-tags");
            if (tags == null) return;
            tags.Clear();

            // Lien vers le MEMORY.md de la session active (clic = ouvrir dans l'Explorateur).
            if (_activeSession != null)
            {
                string memPath = SessionStore.MemoryPath(_activeSession.id);
                var memLink = new Label("📄 MEMORY.md (session)");
                memLink.AddToClassList("cc-memlink");
                memLink.tooltip = "Ouvrir l'emplacement du fichier dans l'Explorateur :\n" + memPath;
                memLink.RegisterCallback<MouseDownEvent>(evt =>
                {
                    if (!File.Exists(memPath)) File.WriteAllText(memPath, "");
                    EditorUtility.RevealInFinder(memPath);
                    evt.StopPropagation();
                });
                tags.Add(memLink);
            }

            if (_contextPaths.Count == 0)
            {
                var hint = new Label("Glissez des fichiers ici ou « + Sélection »");
                hint.style.opacity = 0.6f;
                hint.style.fontSize = 10;
                tags.Add(hint);
                return;
            }

            for (int i = 0; i < _contextPaths.Count; i++)
            {
                int idx = i;
                var tag = new VisualElement();
                tag.AddToClassList("cc-tag");
                tag.Add(new Label(Path.GetFileName(_contextPaths[i])) { tooltip = _contextPaths[i] });
                var x = new Label(" ✕");
                x.RegisterCallback<MouseDownEvent>(evt =>
                {
                    _contextPaths.RemoveAt(idx);
                    RebuildContextTags();
                    evt.StopPropagation();
                });
                tag.Add(x);
                tags.Add(tag);
            }
        }

        private VisualElement BuildInput()
        {
            var rowOuter = new VisualElement();
            rowOuter.style.flexShrink = 0; // la zone de saisie reste toujours visible en bas

            var hint = new Label("Ctrl+Enter : envoyer   •   Alt+↑↓ : historique");
            hint.AddToClassList("cc-hint");
            rowOuter.Add(hint);

            var row = new VisualElement();
            row.AddToClassList("cc-input-row");

            _promptField = new TextField { multiline = true };
            _promptField.AddToClassList("cc-input");
            _promptField.RegisterCallback<KeyDownEvent>(OnPromptKeyDown, TrickleDown.TrickleDown);
            _promptField.RegisterValueChangedCallback(OnPromptChanged);
            row.Add(_promptField);

            row.Add(new Button(ExecutePrompt) { text = "Envoyer" });
            rowOuter.Add(row);
            return rowOuter;
        }

        private void OnPromptKeyDown(KeyDownEvent evt)
        {
            // Navigation dans le popup d'autocomplétion (prioritaire)
            if (_autocomplete != null && _autocomplete.IsOpen)
            {
                switch (evt.keyCode)
                {
                    case KeyCode.DownArrow:
                        _autocomplete.Move(1); evt.StopImmediatePropagation(); evt.StopPropagation(); return;
                    case KeyCode.UpArrow:
                        _autocomplete.Move(-1); evt.StopImmediatePropagation(); evt.StopPropagation(); return;
                    case KeyCode.Return:
                    case KeyCode.KeypadEnter:
                    case KeyCode.Tab:
                        _autocomplete.AcceptSelected(); evt.StopImmediatePropagation(); evt.StopPropagation(); return;
                    case KeyCode.Escape:
                        _autocomplete.Close(); evt.StopImmediatePropagation(); evt.StopPropagation(); return;
                }
            }

            // Ctrl/Cmd + Enter : envoyer
            if (evt.keyCode == KeyCode.Return && (evt.ctrlKey || evt.commandKey))
            {
                evt.StopPropagation();
                evt.StopImmediatePropagation();
                if (!_isRunning) ExecutePrompt();
                return;
            }

            // Alt + flèches : naviguer l'historique
            if (evt.altKey && _promptHistory.Count > 0)
            {
                if (evt.keyCode == KeyCode.UpArrow)
                {
                    _historyIndex = Mathf.Max(0, _historyIndex - 1);
                    SetPrompt(_promptHistory[_historyIndex]);
                    evt.StopPropagation();
                }
                else if (evt.keyCode == KeyCode.DownArrow)
                {
                    _historyIndex = Mathf.Min(_promptHistory.Count - 1, _historyIndex + 1);
                    SetPrompt(_promptHistory[_historyIndex]);
                    evt.StopPropagation();
                }
            }
        }

        private void SetPrompt(string text)
        {
            if (_promptField != null) _promptField.value = text ?? "";
        }

        // ---- Autocomplétion (/commande et @fichier) ----

        private void OnPromptChanged(ChangeEvent<string> e)
        {
            string text = e.newValue ?? "";
            _draftPrompt = text; // mémorisé pour survivre aux rechargements de domaine
            if (_autocomplete == null) return;

            // @fichier : un '@' en cours de saisie (token sans espace en fin de texte)
            var at = Regex.Match(text, @"(?:^|\s)@([^\s]*)$");
            if (at.Success)
            {
                ShowFileAutocomplete(at.Groups[1].Value, at.Groups[1].Index - 1);
                return;
            }

            // /commande : un '/' au tout début du prompt
            var slash = Regex.Match(text, @"^/([^\s]*)$");
            if (slash.Success)
            {
                ShowSlashAutocomplete(slash.Groups[1].Value);
                return;
            }

            _autocomplete.Close();
        }

        private void ShowSlashAutocomplete(string query)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            var items = SlashCommandProvider.GetCommands(root)
                .Where(c => query.Length == 0 || c.Name.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                .Take(40)
                .Select(c => new AutocompletePopup.Item(
                    "/" + c.Name + (c.IsCustom ? "  (perso)" : ""), c.Description, "/" + c.Name))
                .ToList();

            if (items.Count == 0) { _autocomplete.Close(); return; }
            OpenAutocomplete(items, item =>
            {
                SetPrompt(item.InsertValue + " ");
                _promptField.Focus();
            });
        }

        private void ShowFileAutocomplete(string query, int atIndex)
        {
            var items = AssetSearchProvider.Search(query, 30)
                .Select(p => new AutocompletePopup.Item(Path.GetFileName(p), p, p))
                .ToList();

            if (items.Count == 0) { _autocomplete.Close(); return; }
            OpenAutocomplete(items, item =>
            {
                string text = _promptField.value ?? "";
                string before = (atIndex >= 0 && atIndex <= text.Length) ? text.Substring(0, atIndex) : text;
                _promptField.value = before + "@" + item.InsertValue + " ";

                // La référence @fichier est aussi ajoutée au contexte envoyé à Claude.
                if (!_contextPaths.Contains(item.InsertValue))
                {
                    _contextPaths.Add(item.InsertValue);
                    RebuildContextTags();
                }
                _promptField.Focus();
            });
        }

        private void OpenAutocomplete(List<AutocompletePopup.Item> items, Action<AutocompletePopup.Item> onAccept)
        {
            var b = _promptField.worldBound;
            const float h = 200f;
            _autocomplete.Open(items, onAccept, b.xMin, Mathf.Max(0f, b.yMin - h - 4f), b.width);
        }

        // ---- Snippets ----

        // Demande à Claude de résumer la session dans la mémoire globale, puis ouvre une nouvelle
        // session légère (la mémoire globale est réinjectée à son 1er message).
        private void CompactAndNewSession()
        {
            if (_isRunning)
            {
                AddSystemBlock("⚠ Attendez la fin du tour en cours avant de compacter.");
                return;
            }
            if (_activeSession == null || _activeSession.exchanges.Count == 0)
            {
                CreateNewSession();
                return;
            }
            if (!EditorUtility.DisplayDialog("Compacter la session",
                    "Claude va résumer cette session dans la mémoire globale du projet, puis une " +
                    "nouvelle session (légère) sera ouverte en conservant cette mémoire.\n\n" +
                    "Nécessite Write/Edit autorisés (ou le bypass). Continuer ?",
                    "Compacter", "Annuler"))
                return;

            _newSessionAfterRun = true;
            SetPrompt(COMPACT_PROMPT);
            _rawSend = true;
            ExecutePrompt();
        }

        private void ShowMemoryMenu()
        {
            var menu = new GenericMenu();

            // Édition par blocs (suppression d'entrées au clic).
            if (_activeSession != null)
                menu.AddItem(new GUIContent("Mémoire de la session (par blocs)"), false,
                    () => MemoryEditorWindow.Show(_activeSession));
            else
                menu.AddDisabledItem(new GUIContent("Mémoire de la session (aucune session active)"));

            menu.AddItem(new GUIContent("Mémoire globale du projet (par blocs)"), false,
                () => MemoryEditorWindow.Show("Mémoire globale du projet", SessionStore.GlobalMemoryPath()));

            menu.AddSeparator("");

            // Édition texte brut.
            if (_activeSession != null)
                menu.AddItem(new GUIContent("Texte brut/Mémoire de la session"), false,
                    () => SessionMemoryWindow.Show(_activeSession));
            menu.AddItem(new GUIContent("Texte brut/Mémoire globale du projet"), false,
                () => SessionMemoryWindow.Show("Mémoire globale du projet", SessionStore.GlobalMemoryPath()));

            menu.ShowAsContext();
        }

        private void ShowSnippetsMenu()
        {
            var menu = new GenericMenu();
            var list = SnippetStore.LoadAll();
            if (list.Count == 0)
                menu.AddDisabledItem(new GUIContent("(aucun snippet)"));
            else
                foreach (var s in list)
                {
                    string title = string.IsNullOrEmpty(s.title) ? "(sans titre)" : s.title.Replace("/", "∕");
                    string text = s.text;
                    menu.AddItem(new GUIContent("Insérer/" + title), false, () => InsertSnippet(text));
                }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Enregistrer le prompt actuel…"), false, SaveCurrentPromptAsSnippet);
            menu.AddItem(new GUIContent("Gérer les snippets…"), false, () => SnippetManagerWindow.Show(() => { }));
            menu.ShowAsContext();
        }

        private void InsertSnippet(string text)
        {
            string cur = _promptField.value ?? "";
            _promptField.value = string.IsNullOrEmpty(cur) ? text : cur + "\n" + text;
            _promptField.Focus();
        }

        private void SaveCurrentPromptAsSnippet()
        {
            string cur = (_promptField.value ?? "").Trim();
            if (string.IsNullOrEmpty(cur))
            {
                EditorUtility.DisplayDialog("Snippets", "Le prompt est vide.", "OK");
                return;
            }
            ClaudeTextPrompt.Show("Titre du snippet", "", title =>
            {
                string t = string.IsNullOrWhiteSpace(title)
                    ? cur.Substring(0, Mathf.Min(30, cur.Length))
                    : title;
                SnippetStore.Add(t, cur);
            });
        }

        #endregion

        #region Drag & drop

        private void RegisterDragAndDrop(VisualElement target)
        {
            target.RegisterCallback<DragUpdatedEvent>(_ =>
                DragAndDrop.visualMode = DragAndDropVisualMode.Copy);
            target.RegisterCallback<DragPerformEvent>(_ =>
            {
                DragAndDrop.AcceptDrag();
                foreach (var obj in DragAndDrop.objectReferences)
                {
                    string path = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(path) && !_contextPaths.Contains(path))
                        _contextPaths.Add(path);
                }
                if (DragAndDrop.paths != null)
                    foreach (string p in DragAndDrop.paths)
                        if (!_contextPaths.Contains(p)) _contextPaths.Add(p);
                RebuildContextTags();
            });
        }

        private void AddSelectedAssets()
        {
            foreach (var obj in Selection.objects)
            {
                string path = AssetDatabase.GetAssetPath(obj);
                if (!string.IsNullOrEmpty(path) && !_contextPaths.Contains(path))
                    _contextPaths.Add(path);
            }
            RebuildContextTags();
        }

        #endregion

        #region Chat rendering

        // Nouvelle racine de chat (conteneur des blocs d'une session). min-height:0 pour
        // respecter la chaîne flex verticale (cf. leçon layout : déborderait sinon).
        private VisualElement NewChatRoot()
        {
            var root = new VisualElement();
            root.style.flexDirection = FlexDirection.Column;
            root.style.minHeight = 0;
            return root;
        }

        // Monte une racine d'affichage dans le ScrollView (en retire l'ancienne sans la détruire).
        private void MountDisplayRoot(VisualElement root)
        {
            if (ReferenceEquals(_displayRoot, root)) return;
            _displayRoot?.RemoveFromHierarchy();
            _displayRoot = root;
            if (root.parent == null) _chat.Add(root);
        }

        private void ClearChat()
        {
            _displayRoot?.Clear();
            // Pendant un run en arrière-plan, _currentResponse et _streamBlocks appartiennent au
            // run (autre session) : ne pas les toucher, sinon on perd le bloc en cours de stream.
            if (!RunIsBackground)
            {
                _currentResponse.Clear();
                _streamBlocks.Clear();
            }
            // _log référence le transcript de la session affichée : on NE l'efface PAS (ce serait
            // effacer des données persistées). La sélection de session le ré-aiguille à la place.
        }

        private ChatEntry Record(ChatEntry e)
        {
            if (!_suppressLog)
            {
                var target = _recordInto ?? _log;
                target.Add(e);
                const int MAX = 500; // borne la sérialisation (transcript très long)
                if (target.Count > MAX) target.RemoveRange(0, target.Count - MAX);
            }
            return e;
        }

        // Reconstruit l'affichage de la session courante depuis _log (après un rechargement de
        // domaine). Aucun run actif dans ce cas (la recompilation est verrouillée pendant un run).
        private void RebuildFromLog() => RenderTranscript(_displayRoot, _log, live: true, clearStream: true);

        // Rend un transcript dans une racine donnée. live=true autorise la réactivation d'un
        // checkpoint workflow non répondu (reprise auto). clearStream ne doit être vrai que
        // lorsqu'aucun run n'utilise _streamBlocks (sinon on casserait un stream d'arrière-plan).
        private void RenderTranscript(VisualElement root, List<ChatEntry> log, bool live, bool clearStream)
        {
            var savedChat = _chatContent;
            _chatContent = root;          // les fabriques écrivent dans CETTE racine le temps du rendu
            _suppressLog = true;          // reconstruction : ne pas ré-enregistrer
            try
            {
                root.Clear();
                if (clearStream) _streamBlocks.Clear();
                foreach (var e in log) RenderEntry(e, live);
            }
            finally { _suppressLog = false; _chatContent = savedChat; }
            ScrollToBottom();
        }

        private void RenderEntry(ChatEntry e, bool live)
        {
            switch (e.kind)
            {
                case "prompt": AddPromptBlock(e.content); break;
                case "text":   FinalizeTextMarkdown(AddTextBlock(e.content, out _)); break;
                case "system": AddSystemBlock(e.content); break;
                case "net":    AddNetworkBlock(e.content); break;
                case "error":  AddErrorBlock(e.header, e.content); break;
                case "done":   AddDoneBlock(); break;
                case "thinking":
                {
                    AddFoldoutBlock("💭 Réflexion", "cc-thinking", e.expanded, out var c);
                    c.text = e.content;
                    break;
                }
                case "result":
                {
                    AddFoldoutBlock(e.header,
                        e.isError ? "cc-error" : "cc-result__content", e.expanded, out var c);
                    c.text = e.content;
                    break;
                }
                case "tool":
                {
                    var fold = AddFoldoutBlock($"⚙ {e.header}", "cc-tool__content", e.expanded, out _);
                    JToken input = null;
                    if (!string.IsNullOrEmpty(e.inputJson))
                        try { input = JToken.Parse(e.inputJson); } catch { }
                    PopulateToolFoldout(fold, e.toolName, input, e.header, e.content);
                    break;
                }
                case "ask":
                    if (e.answered) AddSystemBlock("↩ Question traitée.");
                    else BuildAskQuestionCard(e);
                    break;
                case "wf_fold":
                    WfFold(e.header, e.cssClass, e.expanded, e.markdown, e.content, out _);
                    break;
                case "wf_checkpoint":
                    if (e.answered) AddSystemBlock("↩ Décision déjà prise.");
                    else if (live)
                    {
                        // Reprise automatique : on recrée le client pour que la carte
                        // restaurée puisse appeler /resume (le run_id est sérialisé).
                        if (_wfClient == null) _wfClient = new WorkflowClient(_workflowUrl);
                        _wfAwaitingCheckpoint = true;
                        BuildWorkflowCheckpointCard(e);
                    }
                    else AddSystemBlock("⏳ Décision en attente (session inactive).");
                    break;
                case "wf_interrupted":
                    // Ancien mécanisme (entrée enregistrée) remplacé par le bandeau dérivé du
                    // pointeur de session (MaybeShowResumeBanner). Rendu inerte pour compat.
                    break;
            }
        }

        private void LoadSession(Session session)
        {
            // Les réglages (modèle, permission, effort, outils, system prompt) sont globaux et
            // persistants : charger une session ne les modifie pas.
            // On grave d'abord la session sortante sur disque (rien n'est perdu en quittant).
            FlushTranscript(_activeSession);
            _activeSession = session;
            _contextPaths.Clear();
            _contextPaths.AddRange(session.contextPaths);
            LoadSessionIntoChat(session);
            RebuildContextTags();
            RebuildSidebarList();
            UpdateTitle();
        }

        private void LoadSessionIntoChat(Session session)
        {
            if (_contextLabel != null) _contextLabel.text = ""; // taille inconnue tant qu'aucun tour
            SessionStore.EnsureTranscript(session); // charge le sidecar (transcript) à la demande

            // Cas 1 : on revient sur la session du run en cours → re-monter sa racine VIVANTE
            // (UI et streaming intacts, aucune reconstruction). _chatContent reste _runRoot.
            if (_runSession != null && session.id == _runSession.id)
            {
                MountDisplayRoot(_runRoot);
                _log = session.transcript;
                _recordInto = session.transcript;
                RebuildContextTags();
                ScrollToBottom();
                return;
            }

            // Cas 2 : session non liée au run → reconstruire son affichage dans une racine neuve.
            var root = NewChatRoot();
            MountDisplayRoot(root);
            _log = session.transcript;
            // Tant qu'aucun run n'est lié, les fabriques visent l'affichage ; sinon le run garde
            // sa propre cible (_chatContent = _runRoot) et n'est pas perturbé par cet affichage.
            if (_runSession == null) { _chatContent = root; _recordInto = _log; }

            if (_log.Count > 0)
            {
                // Transcript riche disponible : restitution fidèle. live=false car ce n'est pas
                // la session du run actif (pas de réactivation de checkpoint).
                RenderTranscript(root, _log, live: false, clearStream: _runSession == null);
            }
            else if (session.exchanges.Count > 0)
            {
                // Session héritée (sans transcript) : reconstruction depuis les échanges, qui
                // peuple aussi le transcript pour les prochains chargements.
                var savedChat = _chatContent; var savedRecord = _recordInto;
                _chatContent = root; _recordInto = _log;
                try
                {
                    foreach (var ex in session.exchanges)
                    {
                        AddPromptBlock(ex.prompt);
                        FinalizeTextMarkdown(AddTextBlock(ex.response, out _));
                        AddDoneBlock();
                    }
                }
                finally { _chatContent = savedChat; _recordInto = savedRecord; }
                SessionStore.Save(session); // grave le transcript reconstruit
            }
            RebuildContextTags();
            ScrollToBottom();
            MaybeShowResumeBanner(session); // run non terminé sur cette session ? propose la reprise
        }

        // Grave le sidecar transcript d'une session (léger : n'écrit pas le JSON principal, dont
        // les métadonnées ne changent qu'aux actions explicites / fin de run). Best-effort.
        private void FlushTranscript(Session session)
        {
            if (session?.transcript == null) return;
            try { SessionStore.SaveTranscript(session); } catch { }
        }

        // Lie le run qui démarre à la session affichée. Sa sortie y restera attachée même si
        // l'utilisateur change de session : la racine de chat du run (_runRoot) et son transcript
        // (_recordInto) sont figés ici. Appelé AVANT d'écrire le premier bloc du run.
        private void BeginRun()
        {
            if (_activeSession == null) CreateNewSession();
            _runSession = _activeSession;
            SessionStore.EnsureTranscript(_runSession);
            _runSession.transcript ??= new List<ChatEntry>();
            _runRoot = _displayRoot;                 // le run rend là où l'utilisateur est (sa session)
            _chatContent = _runRoot;                 // cible des fabriques = racine du run
            _recordInto = _runSession.transcript;    // enregistrement = transcript du run
            _lastRunSave = EditorApplication.timeSinceStartup;
            RebuildSidebarList();                    // affiche la pastille « en cours »
        }

        // Délie le run terminé : grave une dernière fois, puis rebascule les fabriques vers la
        // session affichée. Si le run tournait en arrière-plan, sa racine détachée est libérée
        // (le transcript persistant suffit à reconstruire l'affichage au prochain accès).
        private void EndRun()
        {
            if (_runSession != null) FlushTranscript(_runSession);
            _runSession = null;
            _runRoot = null;
            _chatContent = _displayRoot;
            _recordInto = _activeSession != null ? (_activeSession.transcript ??= new List<ChatEntry>()) : _log;
            RebuildSidebarList(); // retire la pastille « en cours »
        }

        private void UpdateContextIndicator(int tokens)
        {
            if (_contextLabel == null) return;
            _contextLabel.text = $"🧠 {FormatTokens(tokens)}";
            _contextLabel.style.color =
                tokens >= 500000 ? new Color(0.85f, 0.35f, 0.30f) :   // alerte
                tokens >= 200000 ? new Color(0.90f, 0.65f, 0.25f) :   // attention
                                   new Color(0.55f, 0.55f, 0.55f);    // normal
        }

        private static string FormatTokens(int t)
        {
            if (t >= 1000000) return $"{t / 1000000.0:0.0}M";
            if (t >= 1000)    return $"{t / 1000}k";
            return t.ToString();
        }

        private void UpdateTitle()
        {
            if (_titleLabel == null) return;
            string title = _activeSession != null ? _activeSession.GetDisplayTitle() : "Aucune session";
            if (_activeSession != null && _activeSession.totalCostUsd > 0)
                title += $"   (${_activeSession.totalCostUsd:0.000})";
            _titleLabel.text = title;
        }

        private void ScrollToBottom()
        {
            _chat?.schedule.Execute(() =>
                _chat.scrollOffset = new Vector2(0, float.MaxValue)).ExecuteLater(16);
        }

        private void SetAllFoldouts(bool expanded)
        {
            if (_displayRoot == null) return;
            _displayRoot.Query<Foldout>().ForEach(f => f.value = expanded);
        }

        // ---- block factories ----

        private void AddPromptBlock(string text)
        {
            var box = new VisualElement();
            box.AddToClassList("cc-prompt");
            box.style.flexDirection = FlexDirection.Row;

            var lbl = new Label($"► {text}");
            lbl.style.flexGrow = 1;
            lbl.style.whiteSpace = WhiteSpace.Normal;
            lbl.selection.isSelectable = true;
            box.Add(lbl);

            // Rewind : reprendre ce message dans la zone de saisie pour l'éditer/renvoyer.
            var edit = new Button(() => { SetPrompt(text); _promptField?.Focus(); }) { text = "↺" };
            edit.tooltip = "Reprendre ce message dans la saisie";
            edit.AddToClassList("cc-prompt__edit");
            box.Add(edit);

            _chatContent.Add(box);
            ScrollToBottom();
            Record(new ChatEntry { kind = "prompt", content = text });
        }

        // Pendant le streaming, le texte s'accumule dans un Label brut à l'intérieur d'une bulle.
        // En fin de bloc, FinalizeTextMarkdown remplace ce Label par le rendu Markdown.
        private Label AddTextBlock(string text, out ChatEntry entry)
        {
            var box = new VisualElement();
            box.AddToClassList("cc-text");
            var lbl = new Label(text ?? "");
            lbl.AddToClassList("cc-block__content");
            lbl.selection.isSelectable = true;
            box.Add(lbl);
            _chatContent.Add(box);
            ScrollToBottom();
            entry = Record(new ChatEntry { kind = "text", content = text ?? "" });
            return lbl;
        }

        private void FinalizeTextMarkdown(Label inner)
        {
            var box = inner?.parent;
            if (box == null) return;
            string full = inner.text;
            box.Clear();
            MarkdownRenderer.Render(box, full);
        }

        private void AddSystemBlock(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList("cc-system");
            _chatContent.Add(lbl);
            ScrollToBottom();
            Record(new ChatEntry { kind = "system", content = text });
        }

        // Bloc d'avertissement réseau/serveur (surcharge, retry, timeout…).
        private void AddNetworkBlock(string text)
        {
            var lbl = new Label(text);
            lbl.AddToClassList("cc-net");
            lbl.selection.isSelectable = true;
            _chatContent.Add(lbl);
            ScrollToBottom();
            Record(new ChatEntry { kind = "net", content = text });
            if (_debugEvents && !_suppressLog) UnityEngine.Debug.LogWarning($"[ClaudeCodeGUI][réseau] {text}");
        }

        // Classe les lignes de stderr : les problèmes réseau/serveur sont mis en avant.
        private static readonly string[] NetworkKeywords =
        {
            "overloaded", "529", "503", "502", "429", "rate limit", "rate_limit",
            "retry", "retrying", "econnreset", "etimedout", "enotfound", "timeout",
            "network", "fetch failed", "socket hang up", "connection", "connexion",
            "offline", "unreachable",
        };

        private void HandleStderr(string err)
        {
            if (string.IsNullOrWhiteSpace(err)) return;
            if (IsNetworkError(err)) AddNetworkBlock($"🌐 {err}");
            else
            {
                AddSystemBlock($"[stderr] {err}");
                if (_debugEvents) UnityEngine.Debug.LogWarning($"[ClaudeCodeGUI][stderr] {err}");
            }
        }

        private static bool IsNetworkError(string text)
        {
            if (string.IsNullOrEmpty(text)) return false;
            string low = text.ToLowerInvariant();
            foreach (string k in NetworkKeywords)
                if (low.Contains(k)) return true;
            return false;
        }

        // Détecte un refus de permission d'outil et, le cas échéant, affiche une carte pour
        // l'autoriser puis réessayer. Renvoie true si la carte a été affichée (refus géré).
        private bool MaybeHandlePermissionDenial(string text)
        {
            if (_bypassPermissions || string.IsNullOrEmpty(text)) return false;
            var m = Regex.Match(text, @"permissions?\s+to\s+use\s+([A-Za-z0-9_\-]+)", RegexOptions.IgnoreCase);
            if (!m.Success) return false;

            string tool = m.Groups[1].Value;
            if (!_requestedPerms.Add(tool)) return true; // déjà proposé pour ce run
            AddPermissionRequestBlock(tool);
            return true;
        }

        private void AddPermissionRequestBlock(string tool)
        {
            var card = new VisualElement();
            card.AddToClassList("cc-ask");

            var title = new Label($"🔒 Permission demandée : outil « {tool} »");
            title.AddToClassList("cc-ask__title");
            card.Add(title);

            var info = new Label("Claude a besoin de cet outil, mais il n'est pas autorisé.");
            info.AddToClassList("cc-ask__desc");
            card.Add(info);

            var actions = new VisualElement();
            actions.AddToClassList("cc-ask__actions");
            var allow = new Button { text = $"Autoriser « {tool} » et réessayer" };
            var ignore = new Button { text = "Ignorer" };
            allow.clicked += () =>
            {
                AddAllowedTool(tool);   // persiste dans les prefs
                RebuildToolChips();     // coche la case correspondante
                allow.SetEnabled(false); ignore.SetEnabled(false);
                title.text = $"🔓 « {tool} » autorisé — relance…";
                SubmitFollowUp($"J'ai autorisé l'outil {tool}. Réessaie l'action précédente.");
            };
            ignore.clicked += () => { allow.SetEnabled(false); ignore.SetEnabled(false); };
            actions.Add(allow);
            actions.Add(ignore);
            card.Add(actions);

            _chatContent.Add(card);
            ScrollToBottom();
        }

        private void AddErrorBlock(string header, string content)
        {
            var box = new VisualElement();
            box.AddToClassList("cc-error");
            box.Add(new Label($"✗ {header}"));
            if (!string.IsNullOrEmpty(content)) box.Add(new Label(content));
            _chatContent.Add(box);
            ScrollToBottom();
            Record(new ChatEntry { kind = "error", header = header, content = content ?? "" });

            // Répliqué dans la Console Unity (copiable, horodaté) — mais PAS lors d'une
            // reconstruction du transcript (_suppressLog) : sinon recharger une session re-loggue
            // d'anciennes erreurs avec une pile trompeuse pointant vers RenderTranscript.
            if (!_suppressLog)
                UnityEngine.Debug.LogError($"[ClaudeCodeGUI] {header}" +
                    (string.IsNullOrEmpty(content) ? "" : "\n" + content));
        }

        private void AddDoneBlock()
        {
            var lbl = new Label("✔ Terminé");
            lbl.AddToClassList("cc-done");
            lbl.style.color = new Color(0.36f, 0.72f, 0.36f);
            _chatContent.Add(lbl);
            ScrollToBottom();
            Record(new ChatEntry { kind = "done" });
        }

        private Foldout AddFoldoutBlock(string title, string contentClass, bool expanded, out Label content)
        {
            var fold = new Foldout { text = title, value = expanded };
            content = new Label("");
            content.AddToClassList(contentClass);
            content.selection.isSelectable = true;
            fold.Add(content);
            _chatContent.Add(fold);
            ScrollToBottom();
            return fold;
        }

        // Remplit le contenu d'un foldout d'outil : diff visuel pour Edit/MultiEdit/Write,
        // description textuelle sinon.
        private void PopulateToolFoldout(Foldout fold, string name, JToken input, string desc, string display)
        {
            fold.text = $"⚙ {desc}";
            fold.Clear();

            if (name == "ExitPlanMode")
            {
                fold.text = "📋 Plan proposé";
                fold.value = true;
                var planBox = new VisualElement();
                MarkdownRenderer.Render(planBox, (string)input?["plan"] ?? "(plan vide)");
                fold.Add(planBox);

                var row = new VisualElement();
                row.AddToClassList("cc-plan__actions");
                var approve = new Button(() =>
                    SubmitFollowUp("J'approuve ce plan. Procède à son implémentation.")) { text = "✓ Approuver" };
                var revise = new Button(() =>
                {
                    SetPrompt("Je souhaite ajuster le plan : ");
                    _promptField?.Focus();
                }) { text = "✗ Demander des modifications" };
                row.Add(approve);
                row.Add(revise);
                fold.Add(row);
                return;
            }

            if (name is "Edit" or "MultiEdit" or "Write")
            {
                if (!_suppressLog) _filesEdited = true; // recompilation en fin de run (pas en reconstruction)
                if (input != null)
                {
                    fold.Add(DiffRenderer.Build(name, input));
                    fold.value = true; // on déplie d'office pour rendre la modification visible
                }
                else if (!string.IsNullOrEmpty(display))
                {
                    var c2 = new Label(display);
                    c2.AddToClassList("cc-tool__content");
                    c2.selection.isSelectable = true;
                    c2.style.whiteSpace = WhiteSpace.Normal;
                    fold.Add(c2);
                }
            }
            else if (!string.IsNullOrEmpty(display))
            {
                var c = new Label(display);
                c.AddToClassList("cc-tool__content");
                c.selection.isSelectable = true;
                c.style.whiteSpace = WhiteSpace.Normal;
                fold.Add(c);
            }
        }

        void StopWorkflow(int toto)
        {
            Debug.Log("coucou");
        }
        #endregion

        #region Parser wiring

        private void BuildParser()
        {
            _parser = new ClaudeStreamParser { DebugLogging = _debugEvents };

            _parser.OnSessionId = id => _dispatcher.Enqueue(() =>
            {
                var s = _runSession ?? _activeSession;
                if (s != null) s.claudeSessionId = id;
            });

            _parser.OnSessionInit = model => _dispatcher.Enqueue(() =>
                AddSystemBlock($"Session démarrée — {model ?? "?"}"));

            _parser.OnTaskStarted = desc => _dispatcher.Enqueue(() =>
                AddSystemBlock($"▶ Sous-agent : {desc ?? "tâche"}"));
            _parser.OnTaskProgress = (desc, tool) => _dispatcher.Enqueue(() =>
            {
                if (string.IsNullOrEmpty(desc)) return;
                AddSystemBlock($"  [{(string.IsNullOrEmpty(tool) ? "…" : "⚙ " + tool)}] {desc}");
            });
            _parser.OnTaskCompleted = desc => _dispatcher.Enqueue(() =>
                AddSystemBlock($"✓ Sous-agent terminé{(desc != null ? " : " + desc : "")}"));

            // Nouveau message assistant : les content blocks se ré-indexent à partir de 0.
            _parser.OnMessageStart = () => _dispatcher.Enqueue(() => _streamBlocks.Clear());

            _parser.OnContentBlockStart = (index, blockType, toolName) => _dispatcher.Enqueue(() =>
            {
                if (index < 0) return;
                switch (blockType)
                {
                    case "text":
                    {
                        var lbl = AddTextBlock("", out var entry);
                        _streamBlocks[index] = new BlockRef { Root = lbl, Content = lbl, Kind = "text", Entry = entry };
                        break;
                    }
                    case "thinking":
                    {
                        var fold = AddFoldoutBlock("💭 Réflexion", "cc-thinking", true, out var content);
                        var entry = Record(new ChatEntry { kind = "thinking", expanded = true });
                        _streamBlocks[index] = new BlockRef { Root = fold, Content = content, Foldout = fold, Kind = "thinking", Entry = entry };
                        break;
                    }
                    case "tool_use":
                    {
                        var fold = AddFoldoutBlock($"⚙ {toolName ?? "outil"}", "cc-tool__content", false, out var content);
                        var entry = Record(new ChatEntry { kind = "tool", header = toolName ?? "outil" });
                        _streamBlocks[index] = new BlockRef { Root = fold, Content = content, Foldout = fold, Kind = "tool_use", Entry = entry };
                        break;
                    }
                }
            });

            _parser.OnTextChunk = (index, chunk) => _dispatcher.Enqueue(() =>
            {
                if (_streamBlocks.TryGetValue(index, out var b))
                {
                    b.Content.text += chunk;
                    if (b.Entry != null) b.Entry.content = b.Content.text; // persistance
                }
                _currentResponse.Append(chunk);
                ScrollToBottom();
            });

            _parser.OnThinkingChunk = (index, chunk) => _dispatcher.Enqueue(() =>
            {
                if (_streamBlocks.TryGetValue(index, out var b))
                {
                    b.Content.text += chunk.Replace("\r", "");
                    if (b.Entry != null) b.Entry.content = b.Content.text;
                }
                ScrollToBottom();
            });

            _parser.OnContentBlockStop = index => _dispatcher.Enqueue(() =>
            {
                if (!_streamBlocks.TryGetValue(index, out var b)) return;
                if (b.Kind == "text" && b.Content != null)
                {
                    FinalizeTextMarkdown(b.Content); // rendu Markdown une fois le texte complet
                }
                else if (b.Kind == "thinking" && string.IsNullOrWhiteSpace(b.Content?.text))
                {
                    b.Root?.RemoveFromHierarchy(); // réflexion vide (signature seule) : on n'affiche rien
                    if (b.Entry != null) (_recordInto ?? _log).Remove(b.Entry);
                }
                else if (b.Kind == "thinking" && b.Foldout != null && b.Entry != null)
                {
                    b.Entry.expanded = b.Foldout.value;
                }
            });

            // Le texte et la réflexion sont rendus via le streaming. Le message assistant canonique
            // ne sert qu'à récupérer l'input complet des tool_use (non fourni par les deltas ici).
            // La CLI émet un événement assistant par bloc → on ne peut pas se fier à l'indice du
            // tableau ; on enrichit le 1er bloc tool_use streamé non encore rempli.
            _parser.OnAssistantContent = content => _dispatcher.Enqueue(() =>
            {
                foreach (var item in content)
                {
                    if ((string)item["type"] != "tool_use") continue;

                    string name = (string)item["name"];
                    var input = item["input"];
                    string desc = ToolDescriptions.BuildDescription(name, input);
                    string display = ToolDescriptions.BuildInputDisplay(name, input);

                    BlockRef target = null;
                    foreach (var b in _streamBlocks.Values)
                        if (b.Kind == "tool_use" && !b.Enriched) { target = b; break; }

                    ChatEntry entry;
                    Foldout fold;
                    if (target != null)
                    {
                        target.Enriched = true;
                        fold = target.Foldout;
                        entry = target.Entry;
                    }
                    else
                    {
                        fold = AddFoldoutBlock($"⚙ {desc}", "cc-tool__content", false, out _);
                        entry = Record(new ChatEntry { kind = "tool" });
                    }

                    // Persistance : on mémorise de quoi reconstruire le bloc (diff inclus).
                    if (entry != null)
                    {
                        entry.header = desc;
                        entry.content = display ?? "";
                        entry.toolName = name;
                        entry.inputJson = input?.ToString(Newtonsoft.Json.Formatting.None) ?? "";
                        entry.expanded = name is "Edit" or "MultiEdit" or "Write" or "ExitPlanMode";
                    }

                    PopulateToolFoldout(fold, name, input, desc, display);
                }
            });

            _parser.OnToolResult = (isError, output, toolUseId) => _dispatcher.Enqueue(() =>
            {
                if (!string.IsNullOrEmpty(toolUseId) && _handledAskIds.Contains(toolUseId)) return;
                if (isError && MaybeHandlePermissionDenial(output)) return;

                string truncated = output;
                if (!string.IsNullOrEmpty(truncated) && truncated.Length > 2000)
                    truncated = truncated.Substring(0, 2000) + "\n… (tronqué)";

                string header = isError ? "✗ Erreur outil" : "📋 Résultat outil";
                AddFoldoutBlock(header, isError ? "cc-error" : "cc-result__content", isError, out var c);
                c.text = truncated ?? "";
                Record(new ChatEntry { kind = "result", header = header, content = truncated ?? "", isError = isError, expanded = isError });
            });

            _parser.OnUsage = usage => _dispatcher.Enqueue(() =>
            {
                var s = _runSession ?? _activeSession;
                if (s != null) s.AddUsage(usage);
                UpdateContextIndicator(usage.InputTokens);
                string dur = usage.DurationMs > 0 ? $" — {usage.DurationMs / 1000.0:0.0}s" : "";
                string line = $"💰 ${usage.CostUsd:0.0000} — {usage.InputTokens} in / {usage.OutputTokens} out — {usage.NumTurns} tours{dur}";
                if (s != null)
                    line += $"   (session : ${s.totalCostUsd:0.000}, " +
                            $"{s.totalInputTokens + s.totalOutputTokens} tokens)";
                AddSystemBlock(line);
                UpdateTitle();
            });

            _parser.OnResult = (isError, text) => _dispatcher.Enqueue(() =>
            {
                if (isError)
                {
                    if (MaybeHandlePermissionDenial(text)) { /* carte de permission affichée */ }
                    else if (IsNetworkError(text))
                    {
                        _runHadNetworkError = true; // OnProcessExited déclenchera le renvoi
                        AddNetworkBlock($"🌐 {text}");
                    }
                    else AddErrorBlock(text ?? "Erreur inconnue", null);
                }
                else if (!string.IsNullOrEmpty(text))
                {
                    _currentResponse.Clear();
                    _currentResponse.Append(text);
                }
                _runner?.CloseStdin();
            });

            _parser.OnAskUserQuestion = (toolUseId, questions) => _dispatcher.Enqueue(() =>
            {
                _handledAskIds.Add(toolUseId);
                OpenAskQuestionDialog(toolUseId, questions);
            });

            _parser.OnRateLimit = msg => _dispatcher.Enqueue(() =>
                AddNetworkBlock($"⏳ Limite de débit : {msg}"));

            _parser.OnThinkingProgress = n => _dispatcher.Enqueue(() => _thinkingTokens = n);

            _parser.OnParseError = err => _dispatcher.Enqueue(() =>
            {
                AddSystemBlock($"[parse] {err}");
                if (_debugEvents) UnityEngine.Debug.LogWarning($"[ClaudeCodeGUI][parse] {err}");
            });
        }

        #endregion

        #region Process execution

        private void ExecutePrompt()
        {
            bool raw = _rawSend; _rawSend = false; // envoi interne (follow-up/retry/compact) ?

            string prompt = (_promptField?.value ?? "").Trim();
            if (string.IsNullOrEmpty(prompt)) return;
            if (_isRunning)
            {
                // Feedback explicite plutôt qu'un échec silencieux (cause possible : un run précédent
                // resté bloqué). « Stop » réinitialise l'état. Affiché dans la session VISIBLE
                // (sans l'enregistrer), pas dans le transcript du run en cours.
                var note = new Label("⚠ Un échange est déjà en cours. Cliquez « ■ Stop » pour l'interrompre avant d'en envoyer un autre.");
                note.AddToClassList("cc-system");
                _displayRoot.Add(note);
                ScrollToBottom();
                return;
            }

            // Mode Workflow : route vers le démon d'orchestration (le chemin CLI ci-dessous est ignoré).
            if (_executionMode == "workflow")
            {
                StartWorkflow(prompt);
                return;
            }

            // Mode recherche approfondie : préfixe les messages de l'utilisateur par /deep-research.
            if (!raw && _deepResearch && !prompt.StartsWith("/"))
                prompt = "/deep-research " + prompt;

            _retryToken++;       // un nouvel envoi annule toute tentative réseau en attente
            _retryAttempt = 0;
            SendPrompt(prompt, showPrompt: true);
        }

        // Envoi effectif. showPrompt=false pour un renvoi automatique (le bloc prompt existe déjà).
        private void SendPrompt(string prompt, bool showPrompt)
        {
            if (_activeSession == null) CreateNewSession();

            // Lie ce run à la session affichée (sauf renvoi auto/retry : _runSession déjà lié).
            if (_runSession == null) BeginRun();

            if (showPrompt) { PushPromptToHistory(prompt); AddPromptBlock(prompt); }
            _currentPrompt = prompt;
            _filesEdited = false;
            _runHadNetworkError = false;
            _requestedPerms.Clear();
            _currentResponse.Clear();
            SetPrompt("");

            var contextPaths = BuildEffectiveContext();
            var fullPrompt = new StringBuilder();

            // Mémoire (globale + session) : injectée uniquement au 1er message de la session du run
            // (ensuite --resume conserve le contexte). On vise _runSession, pas la session affichée.
            if (_runSession != null && _runSession.exchanges.Count == 0)
            {
                string globalPath = SessionStore.GlobalMemoryPath();
                string globalContent = SessionStore.ReadGlobalMemory();
                string memPath = SessionStore.MemoryPath(_runSession.id);
                string memContent = SessionStore.ReadMemory(_runSession.id);
                string sid = _runSession.id;
                string stitle = _runSession.GetDisplayTitle();

                fullPrompt.AppendLine("[Mémoire du projet — globale]");
                fullPrompt.AppendLine($"Fichier mémoire global : {globalPath}");
                fullPrompt.AppendLine(
                    "Tu y maintiens, avec tes outils Write/Edit, des résumés TRÈS courts des sessions et " +
                    "les informations importantes du projet. Chaque entrée doit pointer vers la session concernée " +
                    $"(id). La session courante est : id={sid} — « {stitle} ». Mets ce fichier à jour quand tu " +
                    "apprends une information importante et durable, ou en fin de session.");
                fullPrompt.AppendLine("Contenu actuel :");
                fullPrompt.AppendLine(string.IsNullOrWhiteSpace(globalContent) ? "(vide)" : globalContent);
                fullPrompt.AppendLine();

                fullPrompt.AppendLine("[Mémoire de cette session]");
                fullPrompt.AppendLine($"Fichier mémoire de session : {memPath}");
                fullPrompt.AppendLine("Tu peux y consigner les faits durables propres à cette session.");
                fullPrompt.AppendLine("Contenu actuel :");
                fullPrompt.AppendLine(string.IsNullOrWhiteSpace(memContent) ? "(vide)" : memContent);
                fullPrompt.AppendLine();
            }

            if (contextPaths.Count > 0)
            {
                fullPrompt.AppendLine("Voici les fichiers du projet Unity sur lesquels je travaille :");
                foreach (string path in contextPaths)
                    fullPrompt.AppendLine($"- {Path.GetFullPath(path)}");
                fullPrompt.AppendLine();
            }
            fullPrompt.Append(prompt);

            string args = BuildArguments();
            string workingDir = Path.GetDirectoryName(Application.dataPath);
            string firstLine = WrapUserMessage(fullPrompt.ToString());
            StartProcess(args, firstLine, workingDir);
        }

        // Contexte effectif = fichiers épinglés (+ sélection courante si l'option est active).
        private List<string> BuildEffectiveContext()
        {
            var list = new List<string>(_contextPaths);
            if (_includeSelection)
            {
                foreach (var obj in Selection.objects)
                {
                    string p = AssetDatabase.GetAssetPath(obj);
                    if (!string.IsNullOrEmpty(p) && !list.Contains(p)) list.Add(p);
                }
            }
            return list;
        }

        private void PushPromptToHistory(string prompt)
        {
            _promptHistory.Add(prompt);
            if (_promptHistory.Count > MAX_PROMPT_HISTORY)
                _promptHistory.RemoveRange(0, _promptHistory.Count - MAX_PROMPT_HISTORY);
            _historyIndex = _promptHistory.Count;
        }

        private string BuildArguments()
        {
            var args = new StringBuilder();
            void Flag(string f) => args.Append(f).Append(' ');
            void Opt(string f, string v) =>
                args.Append(f).Append(' ').Append(ClaudeProcessRunner.EscapeArgument(v)).Append(' ');

            Flag("-p");
            Opt("--model", EffectiveModel);
            if (_limitTurns)
                Opt("--max-turns", _maxTurns.ToString());

            if (_bypassPermissions)
            {
                // Bypass total : aucun outil n'est bloqué (indispensable en -p pour Web*/Skill/…).
                Flag("--dangerously-skip-permissions");
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(_allowedTools))
                    Opt("--allowedTools", _allowedTools);
                if (!string.IsNullOrEmpty(_permissionMode) && _permissionMode != "default")
                    Opt("--permission-mode", _permissionMode);
            }

            if (!string.IsNullOrEmpty(_effort) && _effort != "default")
                Opt("--effort", _effort);

            var runSession = _runSession ?? _activeSession;
            if (!string.IsNullOrEmpty(runSession?.claudeSessionId))
                Opt("--resume", runSession.claudeSessionId);
            else if (runSession != null && runSession.exchanges.Count > 0)
                Flag("--continue");

            if (runSession != null && runSession.exchanges.Count == 0 &&
                !string.IsNullOrWhiteSpace(_systemPrompt))
            {
                // On passe le system prompt via un FICHIER (et non en argument) : un texte
                // multi-lignes avec backslashes/guillemets corromprait la ligne de commande.
                string spPath = Path.Combine(
                    Path.GetDirectoryName(Application.dataPath), "Library", "ClaudeCodeGUI", "system-prompt.txt");
                Directory.CreateDirectory(Path.GetDirectoryName(spPath));
                File.WriteAllText(spPath, _systemPrompt);
                Opt("--system-prompt-file", Path.GetFullPath(spPath));
            }

            Flag("--input-format stream-json --output-format stream-json --verbose --include-partial-messages");
            return args.ToString();
        }

        private static string WrapUserMessage(string text)
        {
            var envelope = new JObject
            {
                ["type"] = "user",
                ["message"] = new JObject { ["role"] = "user", ["content"] = text ?? "" }
            };
            return envelope.ToString(Newtonsoft.Json.Formatting.None);
        }

        // Empêche Unity de recharger le domaine (recompilation) pendant un run, ce qui
        // détruirait le thread de lecture et l'état managé → interruption du run.
        private void LockReload()
        {
            if (_lockReloadDuringRun && !_reloadLocked)
            {
                EditorApplication.LockReloadAssemblies();
                _reloadLocked = true;
            }
        }

        private void UnlockReload()
        {
            if (_reloadLocked)
            {
                EditorApplication.UnlockReloadAssemblies();
                _reloadLocked = false;
            }
        }

        private void StartProcess(string arguments, string firstLine, string workingDir)
        {
            _runner = new ClaudeProcessRunner
            {
                OnStdoutLine = line => _parser.Parse(line),
                OnStderr = err => _dispatcher.Enqueue(() => HandleStderr(err)),
                OnExited = () => _dispatcher.Enqueue(OnProcessExited),
                OnLaunchError = ex => _dispatcher.Enqueue(() =>
                {
                    AddErrorBlock($"Erreur au lancement : {ex.Message}",
                        "Vérifiez que claude est installé et accessible (Settings → Exécutable Claude).");
                    _isRunning = false;
                    UnlockReload();
                }),
            };

            ModelCatalog.RememberUsed(EffectiveModel);
            if (_debugEvents)
                UnityEngine.Debug.Log($"[ClaudeCodeGUI] Lancement : {_executable} {arguments}\nWorkingDir: {workingDir}");

            if (_runner.Start(_executable, arguments, firstLine, workingDir))
            {
                _isRunning = true;
                _runStartTime = EditorApplication.timeSinceStartup;
                _thinkingTokens = 0;
                LockReload(); // pas de recompilation pendant le run
                AddSystemBlock($"▶ Envoi à Claude ({EffectiveModel})…");
                // Le message est envoyé ; on ferme stdin (EOF) pour que la CLI traite le tour.
                // On n'a plus besoin de stdin : les réponses/follow-ups relancent un process (--resume).
                _runner.CloseStdin();
            }
            else
            {
                AddErrorBlock("Le process Claude n'a pas pu démarrer.",
                    "Vérifiez le chemin de l'exécutable dans Settings → Exécutable Claude.");
            }
        }

        private void OnProcessExited()
        {
            _isRunning = false;

            // Échec réseau : on renvoie automatiquement le même prompt (backoff), sans
            // enregistrer d'échange ni recompiler. L'utilisateur peut annuler (nouvel envoi / Stop).
            if (_runHadNetworkError && !string.IsNullOrEmpty(_currentPrompt))
            {
                if (_retryAttempt < MAX_NETWORK_RETRIES)
                {
                    _retryAttempt++;
                    _runHadNetworkError = false;
                    int delay = _retryAttempt switch { 1 => 3, 2 => 8, _ => 20 };
                    string prompt = _currentPrompt;
                    _currentPrompt = null; // ne pas sauver l'échange échoué
                    int token = _retryToken;
                    AddNetworkBlock($"↻ Problème réseau — nouvelle tentative {_retryAttempt}/{MAX_NETWORK_RETRIES} dans {delay}s… (Stop ou un nouvel envoi annule)");
                    _root?.schedule.Execute(() =>
                    {
                        if (token != _retryToken || _isRunning) return; // annulé ou déjà reparti
                        SendPrompt(prompt, showPrompt: false);
                    }).ExecuteLater(delay * 1000);
                    return;
                }

                AddErrorBlock($"Échec réseau après {MAX_NETWORK_RETRIES} tentatives.",
                    "Le serveur semble indisponible. Réessayez plus tard (votre message est conservé dans la saisie).");
                SetPrompt(_currentPrompt);
                _currentPrompt = null;
                _runHadNetworkError = false;
                _retryAttempt = 0;
                _newSessionAfterRun = false; // échec : on annule le compactage
                UnlockReload();
                EndRun();                    // run abandonné : on délie (et on grave)
                return;
            }

            // On enregistre l'échange dans la SESSION DU RUN (pas l'affichée, qui peut différer).
            var done = _runSession ?? _activeSession;
            if (done != null && !string.IsNullOrEmpty(_currentPrompt))
            {
                string response = _currentResponse.ToString();
                done.AddExchange(_currentPrompt, response);

                if (string.IsNullOrEmpty(done.title) && done.exchanges.Count == 1)
                {
                    string first = _currentPrompt;
                    done.title = first.Length > 60 ? first[..60] + "…" : first;
                }
                SessionStore.Save(done);
                _currentPrompt = null;
            }

            AddDoneBlock();          // dans la racine du run (visible ou non)
            _retryAttempt = 0;
            UpdateTitle();

            UnlockReload(); // run terminé : on autorise de nouveau la recompilation

            // Claude a édité des fichiers : on force Unity à réimporter et recompiler
            // (sinon la recompilation n'a lieu qu'au retour de focus sur l'éditeur).
            if (_filesEdited)
            {
                _filesEdited = false;
                AddSystemBlock("↻ Recompilation Unity…");
                AssetDatabase.Refresh();
            }

            EndRun();                // run fini : grave + rebascule les fabriques vers l'affichage
            RefreshSessionList();    // titres / nb d'échanges à jour dans la liste

            // « Compacter » : la mémoire a été mise à jour, on repart sur une session légère.
            if (_newSessionAfterRun)
            {
                _newSessionAfterRun = false;
                CreateNewSession();
                AddSystemBlock("🧹 Nouvelle session démarrée — la mémoire globale est conservée.");
            }
        }

        private void KillProcess()
        {
            _retryToken++; // annule toute tentative réseau programmée
            _runHadNetworkError = false;
            _retryAttempt = 0;
            _newSessionAfterRun = false;
            _wfCts?.Cancel();           // arrête aussi un run workflow en cours
            _wfAwaitingCheckpoint = false;
            _runner?.Kill();
            AddSystemBlock("Arrêté par l'utilisateur");
            _isRunning = false;
            UnlockReload();
            EndRun(); // délie le run (grave le transcript, rebascule l'affichage)
        }

        #endregion

        #region AskUserQuestion follow-up

        // Rendu inline dans le chat (remplace l'ancienne modale). On enregistre la question
        // dans le transcript pour pouvoir reconstruire une carte active après un rechargement.
        private void OpenAskQuestionDialog(string toolUseId, JArray questions)
        {
            var entry = Record(new ChatEntry
            {
                kind = "ask",
                toolUseId = toolUseId,
                questionsJson = questions.ToString(Newtonsoft.Json.Formatting.None),
            });
            BuildAskQuestionCard(entry);
        }

        private void BuildAskQuestionCard(ChatEntry entry)
        {
            JArray questions;
            try { questions = JArray.Parse(entry.questionsJson); }
            catch { return; }

            var card = new VisualElement();
            card.AddToClassList("cc-ask");

            var title = new Label("Question de Claude");
            title.AddToClassList("cc-ask__title");
            card.Add(title);

            // Pour chaque question : ses toggles + un champ libre.
            var allToggles = new List<List<Toggle>>();
            var customFields = new List<TextField>();
            var multiFlags = new List<bool>();

            for (int qi = 0; qi < questions.Count; qi++)
            {
                var q = questions[qi];
                string header = (string)q["header"];
                string question = (string)q["question"] ?? "(question)";
                bool multi = (bool?)q["multiSelect"] ?? false;
                var options = q["options"] as JArray;
                multiFlags.Add(multi);

                var block = new VisualElement();
                block.AddToClassList("cc-ask__q");

                if (!string.IsNullOrEmpty(header))
                {
                    var h = new Label(header.ToUpperInvariant());
                    h.AddToClassList("cc-ask__header");
                    block.Add(h);
                }
                var ql = new Label(question + (multi ? "  (choix multiple)" : ""));
                ql.AddToClassList("cc-ask__question");
                block.Add(ql);

                var toggles = new List<Toggle>();
                if (options != null)
                {
                    for (int oi = 0; oi < options.Count; oi++)
                    {
                        string label = (string)options[oi]["label"] ?? "?";
                        string desc = (string)options[oi]["description"];
                        var t = new Toggle(label) { tooltip = desc };
                        int capturedIndex = oi;
                        var localToggles = toggles;
                        bool localMulti = multi;
                        t.RegisterValueChangedCallback(e =>
                        {
                            if (localMulti || !e.newValue) return;
                            // single-select : décoche les autres
                            for (int k = 0; k < localToggles.Count; k++)
                                if (k != capturedIndex) localToggles[k].SetValueWithoutNotify(false);
                        });
                        toggles.Add(t);
                        block.Add(t);
                        if (!string.IsNullOrEmpty(desc))
                        {
                            var d = new Label(desc);
                            d.AddToClassList("cc-ask__desc");
                            block.Add(d);
                        }
                    }
                }
                allToggles.Add(toggles);

                var custom = new TextField("Autre (réponse libre)");
                custom.AddToClassList("cc-ask__custom");
                customFields.Add(custom);
                block.Add(custom);

                card.Add(block);
            }

            var actions = new VisualElement();
            actions.AddToClassList("cc-ask__actions");

            var submit = new Button { text = "Répondre" };
            var cancel = new Button { text = "Annuler" };

            submit.clicked += () =>
            {
                var answers = new List<AskUserQuestionDialog.Answer>();
                for (int qi = 0; qi < questions.Count; qi++)
                {
                    string question = (string)questions[qi]["question"] ?? "";
                    var options = questions[qi]["options"] as JArray;
                    string custom = (customFields[qi].value ?? "").Trim();

                    string value;
                    if (!string.IsNullOrEmpty(custom)) value = custom;
                    else
                    {
                        var picked = new List<string>();
                        for (int k = 0; k < allToggles[qi].Count && options != null && k < options.Count; k++)
                            if (allToggles[qi][k].value) picked.Add((string)options[k]["label"] ?? "?");
                        value = picked.Count == 0 ? "(aucune réponse)" : string.Join(", ", picked);
                    }
                    answers.Add(new AskUserQuestionDialog.Answer(question, value));
                }
                submit.SetEnabled(false); cancel.SetEnabled(false);
                title.text = "Question de Claude — répondu";
                entry.answered = true;
                SubmitFollowUp(BuildFollowUpPrompt(answers));
            };
            cancel.clicked += () =>
            {
                submit.SetEnabled(false); cancel.SetEnabled(false);
                title.text = "Question de Claude — annulée";
                entry.answered = true;
                AddSystemBlock("↩ Question annulée — pas de réponse envoyée.");
            };

            actions.Add(submit);
            actions.Add(cancel);
            card.Add(actions);

            _chatContent.Add(card);
            ScrollToBottom();
        }

        private static string BuildFollowUpPrompt(List<AskUserQuestionDialog.Answer> answers)
        {
            if (answers.Count == 1)
                return $"Réponse à ta question (« {answers[0].Question} ») : {answers[0].Value}";
            var sb = new StringBuilder();
            sb.AppendLine("Mes réponses à tes questions :");
            foreach (var a in answers) sb.AppendLine($"• {a.Question} → {a.Value}");
            return sb.ToString().TrimEnd();
        }

        private void SubmitFollowUp(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            if (_isRunning)
            {
                _pendingFollowUp = text;
                AddSystemBlock("⏳ Réponse en attente de la fin du tour actuel…");
                return;
            }
            SetPrompt(text);
            _rawSend = true; // réponse/relance : ne pas re-préfixer par /deep-research
            ExecutePrompt();
        }

        #endregion

        #region Session helpers / toolbar actions

        private void CreateNewSession()
        {
            var session = Session.Create("", EffectiveModel);
            session.systemPrompt   = _systemPrompt;
            session.permissionMode = _permissionMode;
            session.effort         = _effort;
            session.allowedTools   = _allowedTools;
            session.contextPaths   = new List<string>(_contextPaths);
            SessionStore.Save(session);
            RefreshSessionList();
            LoadSession(session);
        }

        private void SaveActiveSession()
        {
            if (_activeSession == null) return;
            _activeSession.contextPaths = new List<string>(_contextPaths);
            SessionStore.Save(_activeSession);
            RefreshSessionList();
        }

        private void CopyLastResponse()
        {
            string text = _currentResponse.ToString();
            if (string.IsNullOrEmpty(text)) return;
            EditorGUIUtility.systemCopyBuffer = text;
            if (_copyButton != null)
            {
                _copyButton.text = "✓ Copié";
                _copyButton.schedule.Execute(() => _copyButton.text = "⎘ Copier").ExecuteLater(1200);
            }
        }

        #endregion
    }
}
