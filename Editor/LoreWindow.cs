using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LoreVcs
{
    /// <summary>A single revision entry from the history.</summary>
    internal class LoreHistoryEntry
    {
        public string Revision = "";
        public string Signature = "";
        public string Date = "";
        public string Committer = "";
        public string Message = "";
    }

    /// <summary>
    /// Lore VCS control panel inside the Unity editor.
    /// Window → Lore. Tabs: Work (status/commit/sync/server),
    /// History (revision timeline) and Merge (with conflict resolution).
    /// </summary>
    public class LoreWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "Work", "History", "Merge" };

        // Repo state
        private string _branch = "?";
        private string _revision = "?";
        private string _syncState = "";
        private readonly List<string> _changes = new List<string>();

        // Branches
        private string[] _branches = Array.Empty<string>();
        private int _branchIndex = -1;
        private string _newBranchName = "";
        private int _newBranchSourceIndex = -1;

        // Commit
        private string _commitMessage = "";

        // History
        private readonly List<LoreHistoryEntry> _history = new List<LoreHistoryEntry>();
        private bool _historyLoaded;
        private int _selectedHistory = -1;
        private Vector2 _historyScroll;

        // Merge
        private int _mergeSourceIndex;
        private string _mergeMessage = "";
        private bool _mergeInProgress;
        private readonly List<string> _conflicts = new List<string>();
        private Vector2 _mergeScroll;

        // Server
        private bool _serverHealthy;
        private bool _serverCheckDone;
        private string _serverHost = "?";
        private double _lastServerCheck;
        private string _repoName = "";
        private List<string> _localIps = new List<string>();

        // UI
        private int _tab;
        private bool _busy;
        private string _busyLabel = "";
        private string _log = "";
        private Vector2 _changesScroll;
        private Vector2 _logScroll;
        private bool _showSettings;
        private string _cliPathField;
        private string _serverPathField;
        private string _serverConfigField;

        [MenuItem("Window/Lore %#l")]
        public static void Open()
        {
            var win = GetWindow<LoreWindow>("Lore");
            win.minSize = new Vector2(380, 460);
            win.RefreshAll();
        }

        private void OnEnable()
        {
            _cliPathField = LoreCli.ConfiguredCliPath;
            _serverPathField = LoreServerController.ConfiguredServerPath;
            _serverConfigField = LoreServerController.ConfiguredServerConfigDir;
            RefreshAll();
            RefreshServerStatus();
        }

        private void OnFocus()
        {
            if (!_busy) RefreshAll();
            RefreshServerStatus();
        }

        private void Update()
        {
            // Re-check server health every 30 s while the window is open.
            if (EditorApplication.timeSinceStartup - _lastServerCheck > 30)
                RefreshServerStatus();
        }

        // ---------------------------------------------------------------- UI

        private void OnGUI()
        {
            using (new EditorGUI.DisabledScope(_busy))
            {
                DrawHeader();
                var newTab = GUILayout.Toolbar(_tab, TabNames);
                if (newTab != _tab)
                {
                    _tab = newTab;
                    if (_tab == 1 && !_historyLoaded) RefreshHistory();
                    if (_tab == 2) RefreshConflicts();
                }
                EditorGUILayout.Space(4);

                switch (_tab)
                {
                    case 0: DrawWorkTab(); break;
                    case 1: DrawHistoryTab(); break;
                    case 2: DrawMergeTab(); break;
                }
            }

            DrawLog();

            if (_busy)
                EditorGUILayout.HelpBox(_busyLabel, MessageType.Info);
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label($"Branch: {_branch}   Rev: {_revision}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_syncState))
                GUILayout.Label(_syncState, EditorStyles.miniLabel);
            if (GUILayout.Button("↻", EditorStyles.toolbarButton, GUILayout.Width(28)))
            {
                RefreshAll();
                if (_tab == 1) RefreshHistory();
                if (_tab == 2) RefreshConflicts();
            }
            EditorGUILayout.EndHorizontal();
        }

        // ----------------------------------------------------------- Work tab

        private void DrawWorkTab()
        {
            DrawBranches();
            EditorGUILayout.Space(4);
            DrawChanges();
            EditorGUILayout.Space(4);
            DrawCommitAndSync();
            EditorGUILayout.Space(4);
            DrawServer();
            EditorGUILayout.Space(4);
            DrawSettings();
        }

        private void DrawBranches()
        {
            EditorGUILayout.LabelField("Branches", EditorStyles.boldLabel);
            EditorGUILayout.BeginHorizontal();
            var newIndex = EditorGUILayout.Popup(_branchIndex, ToPopupLabels(_branches));
            if (newIndex != _branchIndex && newIndex >= 0 && newIndex < _branches.Length)
            {
                var target = _branches[newIndex];
                if (target != _branch)
                    SwitchBranch(target);
                _branchIndex = newIndex;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("New branch", EditorStyles.miniBoldLabel);

            if (_newBranchSourceIndex < 0 || _newBranchSourceIndex >= _branches.Length)
                _newBranchSourceIndex = _branchIndex;
            _newBranchSourceIndex = EditorGUILayout.Popup(
                "From branch", _newBranchSourceIndex, ToPopupLabels(_branches));

            EditorGUILayout.BeginHorizontal();
            _newBranchName = EditorGUILayout.TextField(_newBranchName);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newBranchName)))
            {
                if (GUILayout.Button("Create & switch", GUILayout.Width(110)))
                {
                    var sourceOk = _newBranchSourceIndex >= 0 &&
                                   _newBranchSourceIndex < _branches.Length;
                    var source = sourceOk ? _branches[_newBranchSourceIndex] : _branch;
                    CreateBranch(_newBranchName.Trim(), source);
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawChanges()
        {
            EditorGUILayout.LabelField($"Changes ({_changes.Count})", EditorStyles.boldLabel);
            _changesScroll = EditorGUILayout.BeginScrollView(
                _changesScroll, GUILayout.MinHeight(90), GUILayout.MaxHeight(160));
            if (_changes.Count == 0)
            {
                EditorGUILayout.LabelField("No local changes.", EditorStyles.miniLabel);
            }
            else
            {
                foreach (var change in _changes)
                    EditorGUILayout.LabelField(change, EditorStyles.miniLabel);
            }
            EditorGUILayout.EndScrollView();
        }

        private void DrawCommitAndSync()
        {
            EditorGUILayout.LabelField("Commit", EditorStyles.boldLabel);
            GUI.SetNextControlName("LoreCommitMessage");
            _commitMessage = EditorGUILayout.TextArea(_commitMessage, GUILayout.MinHeight(36));

            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_commitMessage)))
            {
                if (GUILayout.Button("Stage + Commit"))
                    StageAndCommit();
                if (GUILayout.Button("Stage + Commit + Push"))
                    StageAndCommit(pushAfter: true);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Sync (pull)"))
                Sync();
            if (GUILayout.Button("Push"))
                Push();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawServer()
        {
            EditorGUILayout.LabelField("Server", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            var dot = !_serverCheckDone ? "…" : (_serverHealthy ? "●" : "○");
            var color = GUI.color;
            GUI.color = !_serverCheckDone ? Color.gray : (_serverHealthy ? Color.green : Color.red);
            GUILayout.Label(dot, GUILayout.Width(14));
            GUI.color = color;
            var stateText = !_serverCheckDone ? "checking…"
                : (_serverHealthy ? "online" : "no response");
            GUILayout.Label($"{_serverHost}:41337 — {stateText}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Check", EditorStyles.miniButton, GUILayout.Width(60)))
                RefreshServerStatus();
            EditorGUILayout.EndHorizontal();

            var serverInstalled = LoreServerController.ResolveServerPath() != null;
            if (serverInstalled)
            {
                var running = LoreServerController.IsProcessRunningLocally();
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(running))
                {
                    if (GUILayout.Button("Start server"))
                    {
                        AppendLog(LoreServerController.StartServer());
                        RefreshServerStatus(delaySeconds: 2);
                    }
                }
                using (new EditorGUI.DisabledScope(!running))
                {
                    if (GUILayout.Button("Stop server"))
                    {
                        if (EditorUtility.DisplayDialog("Stop loreserver",
                            "Stop the Lore server on this machine?\n" +
                            "Other machines won't be able to sync/push until it restarts.",
                            "Stop", "Cancel"))
                        {
                            AppendLog(LoreServerController.StopServer());
                            RefreshServerStatus(delaySeconds: 1);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                // Addresses the server is exposed on (it listens on 0.0.0.0),
                // ready to share with teammates.
                if (running && _localIps.Count > 0)
                {
                    EditorGUILayout.LabelField("Shareable addresses:", EditorStyles.miniBoldLabel);
                    foreach (var ip in _localIps)
                    {
                        var url = $"lore://{ip}:41337/{_repoName}";
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.SelectableLabel(url, EditorStyles.miniLabel,
                            GUILayout.Height(16));
                        if (GUILayout.Button("Copy", EditorStyles.miniButton, GUILayout.Width(50)))
                        {
                            EditorGUIUtility.systemCopyBuffer = url;
                            AppendLog($"Copied to clipboard: {url}");
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            else if (LoreServerController.RepoServerIsLocal())
            {
                EditorGUILayout.HelpBox(
                    "This repo points at a local server but loreserver is not installed " +
                    "on this machine. Configure its path under ⚙ Settings.",
                    MessageType.Warning);
            }
            // Remote server with no local binary: only the status row is shown.
        }

        private void DrawSettings()
        {
            _showSettings = EditorGUILayout.Foldout(_showSettings, "⚙ Settings");
            if (!_showSettings) return;

            EditorGUILayout.BeginHorizontal();
            _cliPathField = EditorGUILayout.TextField("CLI path", _cliPathField);
            if (GUILayout.Button("Save", GUILayout.Width(60)))
            {
                LoreCli.ConfiguredCliPath = _cliPathField;
                AppendLog($"CLI path set: {(string.IsNullOrEmpty(_cliPathField) ? "(auto-detect)" : _cliPathField)}");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField($"In use: {LoreCli.ResolveCliPath()}", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            _serverPathField = EditorGUILayout.TextField("loreserver path", _serverPathField);
            if (GUILayout.Button("Save", GUILayout.Width(60)))
            {
                LoreServerController.ConfiguredServerPath = _serverPathField;
                AppendLog("loreserver path saved.");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _serverConfigField = EditorGUILayout.TextField("Server config dir", _serverConfigField);
            if (GUILayout.Button("Save", GUILayout.Width(60)))
            {
                LoreServerController.ConfiguredServerConfigDir = _serverConfigField;
                AppendLog("Server config directory saved.");
            }
            EditorGUILayout.EndHorizontal();

            var resolvedServer = LoreServerController.ResolveServerPath() ?? "(not installed on this machine)";
            EditorGUILayout.LabelField($"loreserver: {resolvedServer}", EditorStyles.miniLabel);
        }

        // -------------------------------------------------------- History tab

        private void DrawHistoryTab()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"History ({_history.Count} revisions)", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload", EditorStyles.miniButton, GUILayout.Width(60)))
                RefreshHistory();
            EditorGUILayout.EndHorizontal();

            _historyScroll = EditorGUILayout.BeginScrollView(
                _historyScroll, GUILayout.ExpandHeight(true));

            for (var i = 0; i < _history.Count; i++)
            {
                var entry = _history[i];
                var selected = i == _selectedHistory;

                EditorGUILayout.BeginVertical(selected ? EditorStyles.helpBox : GUIStyle.none);

                EditorGUILayout.BeginHorizontal();
                GUILayout.Label($"#{entry.Revision}", EditorStyles.boldLabel, GUILayout.Width(40));
                var firstLine = entry.Message.Split('\n')[0];
                if (GUILayout.Button(firstLine, EditorStyles.label))
                    _selectedHistory = selected ? -1 : i;
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(44);
                GUILayout.Label($"{entry.Date}  ·  {entry.Committer}", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();

                if (selected)
                {
                    if (entry.Message.Contains("\n"))
                        EditorGUILayout.LabelField(entry.Message, EditorStyles.wordWrappedMiniLabel);
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.SelectableLabel(entry.Signature, EditorStyles.miniLabel,
                        GUILayout.Height(16));
                    if (GUILayout.Button("Copy signature", EditorStyles.miniButton, GUILayout.Width(100)))
                    {
                        EditorGUIUtility.systemCopyBuffer = entry.Signature;
                        AppendLog($"Signature of #{entry.Revision} copied.");
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            if (_history.Count == 0 && _historyLoaded)
                EditorGUILayout.LabelField("No revisions.", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
        }

        // ---------------------------------------------------------- Merge tab

        private void DrawMergeTab()
        {
            _mergeScroll = EditorGUILayout.BeginScrollView(_mergeScroll, GUILayout.ExpandHeight(true));

            if (_mergeInProgress)
            {
                DrawMergeConflicts();
                EditorGUILayout.EndScrollView();
                return;
            }

            var otherBranches = _branches.Where(b => b != _branch).ToArray();
            if (otherBranches.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No other branches to merge. Create one in the Work tab.",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.LabelField("Merge", EditorStyles.boldLabel);

            _mergeSourceIndex = Mathf.Clamp(_mergeSourceIndex, 0, otherBranches.Length - 1);
            _mergeSourceIndex = EditorGUILayout.Popup(
                "Source branch", _mergeSourceIndex, ToPopupLabels(otherBranches));
            var source = otherBranches[_mergeSourceIndex];

            EditorGUILayout.HelpBox(
                $"This will merge  {source}  →  {_branch}  (your current branch).\n" +
                "Changes from the source branch are brought into the current one; " +
                "the source branch is not modified.",
                MessageType.Info);

            if (string.IsNullOrEmpty(_mergeMessage))
                _mergeMessage = $"Merge {source} into {_branch}";
            _mergeMessage = EditorGUILayout.TextField("Message", _mergeMessage);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("View differences"))
                ShowBranchDiff(source);
            if (GUILayout.Button("Simulate (dry-run)"))
                MergeDryRun(source);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button($"Merge {source} → {_branch}", GUILayout.Height(28)))
                StartMerge(source);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(
                "If the merge hits conflicts, this tab switches to resolution mode " +
                "where you choose file by file between your version (local) or the " +
                "other branch's version (remote).",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawMergeConflicts()
        {
            EditorGUILayout.HelpBox(
                "Merge with conflicts in progress. Resolve each file by choosing " +
                "which version to keep, then finish or abort the merge.",
                MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Conflicts ({_conflicts.Count})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Reload", EditorStyles.miniButton, GUILayout.Width(60)))
                RefreshConflicts();
            EditorGUILayout.EndHorizontal();

            foreach (var path in _conflicts.ToList())
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                if (GUILayout.Button("Local (mine)", EditorStyles.miniButton, GUILayout.Width(85)))
                    ResolveConflict(path, mine: true);
                if (GUILayout.Button("Remote (theirs)", EditorStyles.miniButton, GUILayout.Width(100)))
                    ResolveConflict(path, mine: false);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Bulk resolution:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("All local (mine)"))
            {
                if (EditorUtility.DisplayDialog("Resolve all with local",
                    "Keep YOUR version for every conflicted file?",
                    "Yes, all local", "Cancel"))
                    ResolveConflict(".", mine: true);
            }
            if (GUILayout.Button("All remote (theirs)"))
            {
                if (EditorUtility.DisplayDialog("Resolve all with remote",
                    "Keep the OTHER branch's version for every conflicted file?",
                    "Yes, all remote", "Cancel"))
                    ResolveConflict(".", mine: false);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            _mergeMessage = EditorGUILayout.TextField("Merge message", _mergeMessage);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Finish merge (commit)", GUILayout.Height(26)))
                FinishMerge();
            if (GUILayout.Button("Abort merge", GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog("Abort merge",
                    "Discard the merge in progress and return to the previous state?",
                    "Abort", "Cancel"))
                    AbortMerge();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLog()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Output", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Clear", EditorStyles.miniButton, GUILayout.Width(50)))
                _log = "";
            EditorGUILayout.EndHorizontal();

            // Fills all remaining window height.
            _logScroll = EditorGUILayout.BeginScrollView(
                _logScroll, GUILayout.MinHeight(150), GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_log, EditorStyles.wordWrappedMiniLabel,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------ Actions

        private async void RefreshAll()
        {
            if (_busy) return;
            SetBusy("Querying status…");
            try
            {
                var status = await LoreCli.RunAsync("status");
                ParseStatus(status);

                var branches = await LoreCli.RunAsync("branch", "list");
                ParseBranches(branches);

                if (string.IsNullOrEmpty(_repoName))
                {
                    var info = await LoreCli.RunAsync("repository", "info");
                    ParseRepoName(info);
                }
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void RefreshHistory()
        {
            SetBusy("Loading history…");
            try
            {
                var result = await LoreCli.RunAsync("history");
                if (result.Success)
                    ParseHistory(result.StdOut);
                else
                    AppendLog(result.Combined);
                _historyLoaded = true;
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void Sync()
        {
            if (!SaveProjectBeforeVcsOperation("sync")) return;
            SetBusy("Sync in progress…");
            try
            {
                var result = await LoreCli.RunAsync("sync");
                AppendLog(result.Combined);
                AssetDatabase.Refresh();
                _historyLoaded = false;
            }
            finally
            {
                ClearBusy();
                RefreshAll();
            }
        }

        private async void Push()
        {
            SetBusy("Push in progress…");
            try
            {
                var result = await LoreCli.RunAsync("push");
                AppendLog(result.Combined);
            }
            finally
            {
                ClearBusy();
                RefreshAll();
            }
        }

        private async void StageAndCommit(bool pushAfter = false)
        {
            if (!SaveProjectBeforeVcsOperation("commit")) return;
            SetBusy("Stage + commit…");
            try
            {
                // The repo's .loreignore filters Library/, Temp/, etc.
                var stage = await LoreCli.RunAsync("stage", "--scan", ".");
                AppendLog(stage.Combined);
                if (!stage.Success) return;

                var commit = await LoreCli.RunAsync("commit", _commitMessage.Trim());
                AppendLog(commit.Combined);
                if (!commit.Success) return;

                ClearCommitMessageField();
                _historyLoaded = false;

                if (pushAfter)
                {
                    var push = await LoreCli.RunAsync("push");
                    AppendLog(push.Combined);
                }
            }
            finally
            {
                ClearBusy();
                RefreshAll();
            }
        }

        private async void SwitchBranch(string branch)
        {
            if (!SaveProjectBeforeVcsOperation($"switch to branch '{branch}'")) return;
            SetBusy($"Switching to {branch}…");
            try
            {
                var result = await LoreCli.RunAsync("branch", "switch", branch);
                AppendLog(result.Combined);
                AssetDatabase.Refresh();
                _historyLoaded = false;
            }
            finally
            {
                ClearBusy();
                RefreshAll();
            }
        }

        /// <summary>
        /// Creates a branch starting from <paramref name="source"/>. Lore always
        /// branches off the current branch, so if the source differs from the
        /// current one we switch there first, then create and switch to the new branch.
        /// </summary>
        private async void CreateBranch(string branch, string source)
        {
            if (!SaveProjectBeforeVcsOperation($"create branch '{branch}' from '{source}'"))
                return;

            SetBusy($"Creating branch {branch} from {source}…");
            try
            {
                if (source != _branch)
                {
                    var sw = await LoreCli.RunAsync("branch", "switch", source);
                    AppendLog(sw.Combined);
                    if (!sw.Success)
                    {
                        AppendLog($"Could not switch to source branch '{source}'; branch not created.");
                        return;
                    }
                    AssetDatabase.Refresh();
                }

                var create = await LoreCli.RunAsync("branch", "create", branch);
                AppendLog(create.Combined);
                if (create.Success)
                {
                    var sw = await LoreCli.RunAsync("branch", "switch", branch);
                    AppendLog(sw.Combined);
                    AppendLog($"Branch '{branch}' created from '{source}'.");
                    _newBranchName = "";
                    AssetDatabase.Refresh();
                }
            }
            finally
            {
                ClearBusy();
                RefreshAll();
            }
        }

        // ------------------------------------------------------ Merge actions

        private async void ShowBranchDiff(string source)
        {
            SetBusy($"Diff {source} → {_branch}…");
            try
            {
                var result = await LoreCli.RunAsync("branch", "diff", source);
                AppendLog(result.Success && string.IsNullOrWhiteSpace(result.StdOut)
                    ? "No differences between the branches."
                    : result.Combined);
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void MergeDryRun(string source)
        {
            SetBusy("Simulating merge…");
            try
            {
                var result = await LoreCli.RunAsync("branch", "merge", "start", source, "--dry-run");
                AppendLog(result.Combined);
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void StartMerge(string source)
        {
            if (!SaveProjectBeforeVcsOperation($"merge {source}")) return;
            SetBusy($"Merging {source} → {_branch}…");
            try
            {
                var result = await LoreCli.RunAsync(
                    "branch", "merge", source, "--message", _mergeMessage);
                AppendLog(result.Combined);
                AssetDatabase.Refresh();

                if (result.Success && !LooksLikeConflict(result.Combined))
                {
                    AppendLog("Merge completed. Remember to Push to publish it.");
                    _mergeMessage = "";
                    _historyLoaded = false;
                }
                else
                {
                    _mergeInProgress = true;
                    RefreshConflicts();
                }
            }
            finally
            {
                ClearBusy();
                RefreshAll();
            }
        }

        private async void ResolveConflict(string path, bool mine)
        {
            SetBusy($"Resolving {path}…");
            try
            {
                var result = await LoreCli.RunAsync(
                    "branch", "merge", "resolve", mine ? "mine" : "theirs", path);
                AppendLog(result.Combined);
                AssetDatabase.Refresh();
                RefreshConflicts();
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void FinishMerge()
        {
            SetBusy("Finishing merge…");
            try
            {
                var resolve = await LoreCli.RunAsync("branch", "merge", "resolve");
                AppendLog(resolve.Combined);

                var message = string.IsNullOrWhiteSpace(_mergeMessage)
                    ? $"Merge into {_branch}"
                    : _mergeMessage;
                var commit = await LoreCli.RunAsync("commit", message);
                AppendLog(commit.Combined);

                if (commit.Success)
                {
                    _mergeInProgress = false;
                    _mergeMessage = "";
                    _conflicts.Clear();
                    _historyLoaded = false;
                    AppendLog("Merge finished. Remember to Push to publish it.");
                }
            }
            finally
            {
                ClearBusy();
                RefreshAll();
            }
        }

        private async void AbortMerge()
        {
            SetBusy("Aborting merge…");
            try
            {
                var result = await LoreCli.RunAsync("branch", "merge", "abort");
                AppendLog(result.Combined);
                if (result.Success)
                {
                    _mergeInProgress = false;
                    _conflicts.Clear();
                }
                AssetDatabase.Refresh();
            }
            finally
            {
                ClearBusy();
                RefreshAll();
            }
        }

        private async void RefreshConflicts()
        {
            var result = await LoreCli.RunAsync("status");
            _conflicts.Clear();
            if (!result.Success) return;

            var inMerge = false;
            foreach (var raw in result.StdOut.Split('\n'))
            {
                var line = raw.Trim();
                if (line.IndexOf("merge", StringComparison.OrdinalIgnoreCase) >= 0)
                    inMerge = true;
                // Conflicts: lines like "C path" or mentioning a conflict with a path.
                if (line.Length > 2 && line[0] == 'C' && line[1] == ' ')
                    _conflicts.Add(line.Substring(2).Trim());
                else if (line.IndexOf("conflict", StringComparison.OrdinalIgnoreCase) >= 0 &&
                         line.Contains("/"))
                    _conflicts.Add(line);
            }

            if (_conflicts.Count > 0 || inMerge)
                _mergeInProgress = true;
            Repaint();
        }

        // ------------------------------------------------------------ Parsing

        private void ParseStatus(LoreResult result)
        {
            _changes.Clear();
            if (!result.Success)
            {
                _branch = "?";
                _revision = "?";
                _syncState = "offline";
                AppendLog(result.Combined);
                return;
            }

            _syncState = "";
            foreach (var raw in result.StdOut.Split('\n'))
            {
                var line = raw.TrimEnd();
                var trimmed = line.Trim();

                // "On branch main revision 3 -> 70d99113..."
                if (trimmed.StartsWith("On branch "))
                {
                    var parts = trimmed.Split(' ');
                    if (parts.Length >= 3) _branch = parts[2];
                    var revIdx = Array.IndexOf(parts, "revision");
                    if (revIdx >= 0 && revIdx + 1 < parts.Length)
                        _revision = parts[revIdx + 1];
                }
                else if (trimmed.Contains("in sync with remote"))
                {
                    _syncState = "✓ in sync";
                }
                else if (trimmed.Length > 2 && trimmed[1] == ' ' &&
                         (trimmed[0] == 'A' || trimmed[0] == 'M' || trimmed[0] == 'D'))
                {
                    _changes.Add(trimmed);
                }
            }
        }

        private void ParseHistory(string stdout)
        {
            _history.Clear();
            _selectedHistory = -1;
            LoreHistoryEntry current = null;

            foreach (var raw in stdout.Split('\n'))
            {
                if (raw.StartsWith("Revision"))
                {
                    if (current != null) _history.Add(current);
                    current = new LoreHistoryEntry
                    {
                        Revision = AfterColon(raw),
                    };
                }
                else if (current == null)
                {
                    // Text before the first revision block: ignore.
                }
                else if (raw.StartsWith("Signature"))
                {
                    current.Signature = AfterColon(raw);
                }
                else if (raw.StartsWith("Date"))
                {
                    current.Date = AfterColon(raw);
                }
                else if (raw.StartsWith("Committer"))
                {
                    current.Committer = AfterColon(raw);
                }
                else if (raw.StartsWith("    "))
                {
                    var msgLine = raw.Trim();
                    current.Message = string.IsNullOrEmpty(current.Message)
                        ? msgLine
                        : current.Message + "\n" + msgLine;
                }
            }
            if (current != null) _history.Add(current);
        }

        private static string AfterColon(string line)
        {
            var idx = line.IndexOf(':');
            return idx >= 0 ? line.Substring(idx + 1).Trim() : line.Trim();
        }

        private void ParseRepoName(LoreResult result)
        {
            // `lore repository info` contains a line like
            // "crysp-development (019f5893…)", but connection notices may precede
            // it, so scan for the first line matching that shape.
            if (result.Success && !string.IsNullOrWhiteSpace(result.StdOut))
            {
                foreach (var raw in result.StdOut.Split('\n'))
                {
                    var match = System.Text.RegularExpressions.Regex.Match(
                        raw.Trim(), @"^(\S+) \([0-9a-f]{16,}\)$");
                    if (match.Success)
                    {
                        _repoName = match.Groups[1].Value;
                        return;
                    }
                }
            }
            // Offline: fall back to the folder name as an approximation.
            _repoName = System.IO.Path.GetFileName(LoreCli.ProjectRoot);
        }

        private void ParseBranches(LoreResult result)
        {
            if (!result.Success) return;

            var names = new List<string>();
            var inSection = false;
            foreach (var raw in result.StdOut.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                // Section headers: "Local branches:" / "Remote branches:".
                if (line.EndsWith("branches:"))
                {
                    inSection = true;
                    continue;
                }
                if (!inSection) continue;

                // The CLI mixes connection notices and warnings into the output
                // ("Reconnecting to http://…", "Warning: Could not query remote
                // branch list"). Branch names never contain spaces or URLs.
                var name = line.TrimStart('*', ' ').Trim();
                if (name.Length == 0 || name.Contains(" ") || name.Contains("://"))
                    continue;

                if (!names.Contains(name)) names.Add(name);
                if (line.StartsWith("*")) _branch = name;
            }
            _branches = names.ToArray();
            _branchIndex = Array.IndexOf(_branches, _branch);
        }

        /// <summary>
        /// Popup labels: Unity treats "/" as a submenu separator, so branch names
        /// like "feature/x" are displayed with a lookalike slash instead.
        /// </summary>
        private static string[] ToPopupLabels(string[] names)
        {
            var labels = new string[names.Length];
            for (var i = 0; i < names.Length; i++)
                labels[i] = names[i].Replace("/", " ∕ ");
            return labels;
        }

        private async void RefreshServerStatus(int delaySeconds = 0)
        {
            _lastServerCheck = EditorApplication.timeSinceStartup;
            if (delaySeconds > 0)
                await System.Threading.Tasks.Task.Delay(delaySeconds * 1000);

            _serverHost = LoreServerController.RepoServerHost();
            _serverHealthy = await LoreServerController.CheckHealthAsync();
            _localIps = LoreServerController.GetLocalIpAddresses();
            _serverCheckDone = true;
            Repaint();
        }

        // ------------------------------------------------------------ Helpers

        private static bool LooksLikeConflict(string output) =>
            output.IndexOf("conflict", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Clears the commit message field. IMGUI caches the focused control's
        /// text, so focus must be released first for the TextArea to actually empty.
        /// </summary>
        private void ClearCommitMessageField()
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
            _commitMessage = "";
            Repaint();
        }

        /// <summary>
        /// Saves scenes and assets before operations that rewrite files
        /// (sync, switch, merge). Returns false if the user cancels.
        /// </summary>
        private bool SaveProjectBeforeVcsOperation(string operation)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                AppendLog($"Operation cancelled: there are unsaved scenes (you were about to {operation}).");
                return false;
            }
            AssetDatabase.SaveAssets();
            return true;
        }

        private void SetBusy(string label)
        {
            _busy = true;
            _busyLabel = label;
            Repaint();
        }

        private void ClearBusy()
        {
            _busy = false;
            _busyLabel = "";
            Repaint();
        }

        private void AppendLog(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            var stamp = DateTime.Now.ToString("HH:mm:ss");
            _log = $"[{stamp}] {text.Trim()}\n{_log}";
            if (_log.Length > 20000)
                _log = _log.Substring(0, 20000);
            Repaint();
        }
    }
}
