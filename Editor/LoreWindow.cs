using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LoreVcs
{
    /// <summary>Una entrada del historial de revisiones.</summary>
    internal class LoreHistoryEntry
    {
        public string Revision = "";
        public string Signature = "";
        public string Date = "";
        public string Committer = "";
        public string Message = "";
    }

    /// <summary>
    /// Panel de control de Lore VCS dentro del editor de Unity.
    /// Window → Lore. Pestañas: Trabajo (status/commit/sync/servidor),
    /// Historial (timeline de revisiones) y Merge (con manejo de conflictos).
    /// </summary>
    public class LoreWindow : EditorWindow
    {
        private static readonly string[] TabNames = { "Trabajo", "Historial", "Merge" };

        // Estado del repo
        private string _branch = "?";
        private string _revision = "?";
        private string _syncState = "";
        private readonly List<string> _changes = new List<string>();

        // Branches
        private string[] _branches = Array.Empty<string>();
        private int _branchIndex = -1;
        private string _newBranchName = "";

        // Commit
        private string _commitMessage = "";

        // Historial
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

        // Servidor
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
            // Re-chequear la salud del servidor cada 30 s mientras la ventana esté abierta.
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

        // ------------------------------------------------------ Pestaña Trabajo

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
            var newIndex = EditorGUILayout.Popup(_branchIndex, _branches);
            if (newIndex != _branchIndex && newIndex >= 0 && newIndex < _branches.Length)
            {
                var target = _branches[newIndex];
                if (target != _branch)
                    SwitchBranch(target);
                _branchIndex = newIndex;
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _newBranchName = EditorGUILayout.TextField(_newBranchName);
            using (new EditorGUI.DisabledScope(string.IsNullOrWhiteSpace(_newBranchName)))
            {
                if (GUILayout.Button("Crear y cambiar", GUILayout.Width(120)))
                    CreateBranch(_newBranchName.Trim());
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawChanges()
        {
            EditorGUILayout.LabelField($"Cambios ({_changes.Count})", EditorStyles.boldLabel);
            _changesScroll = EditorGUILayout.BeginScrollView(
                _changesScroll, GUILayout.MinHeight(90), GUILayout.MaxHeight(160));
            if (_changes.Count == 0)
            {
                EditorGUILayout.LabelField("Sin cambios locales.", EditorStyles.miniLabel);
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
            EditorGUILayout.LabelField("Servidor", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            var dot = !_serverCheckDone ? "…" : (_serverHealthy ? "●" : "○");
            var color = GUI.color;
            GUI.color = !_serverCheckDone ? Color.gray : (_serverHealthy ? Color.green : Color.red);
            GUILayout.Label(dot, GUILayout.Width(14));
            GUI.color = color;
            var stateText = !_serverCheckDone ? "comprobando…"
                : (_serverHealthy ? "online" : "sin respuesta");
            GUILayout.Label($"{_serverHost}:41337 — {stateText}", EditorStyles.miniLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Comprobar", EditorStyles.miniButton, GUILayout.Width(80)))
                RefreshServerStatus();
            EditorGUILayout.EndHorizontal();

            var serverInstalled = LoreServerController.ResolveServerPath() != null;
            if (serverInstalled)
            {
                var running = LoreServerController.IsProcessRunningLocally();
                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(running))
                {
                    if (GUILayout.Button("Iniciar servidor"))
                    {
                        AppendLog(LoreServerController.StartServer());
                        RefreshServerStatus(delaySeconds: 2);
                    }
                }
                using (new EditorGUI.DisabledScope(!running))
                {
                    if (GUILayout.Button("Detener servidor"))
                    {
                        if (EditorUtility.DisplayDialog("Detener loreserver",
                            "¿Detener el servidor de Lore de esta máquina?\n" +
                            "Los demás equipos no podrán hacer sync/push hasta reiniciarlo.",
                            "Detener", "Cancelar"))
                        {
                            AppendLog(LoreServerController.StopServer());
                            RefreshServerStatus(delaySeconds: 1);
                        }
                    }
                }
                EditorGUILayout.EndHorizontal();

                // Direcciones en las que el servidor queda expuesto (escucha en 0.0.0.0),
                // listas para compartir con los demás equipos.
                if (running && _localIps.Count > 0)
                {
                    EditorGUILayout.LabelField("Direcciones para compartir:", EditorStyles.miniBoldLabel);
                    foreach (var ip in _localIps)
                    {
                        var url = $"lore://{ip}:41337/{_repoName}";
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.SelectableLabel(url, EditorStyles.miniLabel,
                            GUILayout.Height(16));
                        if (GUILayout.Button("Copiar", EditorStyles.miniButton, GUILayout.Width(60)))
                        {
                            EditorGUIUtility.systemCopyBuffer = url;
                            AppendLog($"Copiado al portapapeles: {url}");
                        }
                        EditorGUILayout.EndHorizontal();
                    }
                }
            }
            else if (LoreServerController.RepoServerIsLocal())
            {
                EditorGUILayout.HelpBox(
                    "El repo apunta a un servidor local pero loreserver no está instalado " +
                    "en esta máquina. Configura su ruta en ⚙ Ajustes.",
                    MessageType.Warning);
            }
            // Si el servidor es remoto y no hay binario local, solo se muestra el estado.
        }

        private void DrawSettings()
        {
            _showSettings = EditorGUILayout.Foldout(_showSettings, "⚙ Ajustes");
            if (!_showSettings) return;

            EditorGUILayout.BeginHorizontal();
            _cliPathField = EditorGUILayout.TextField("Ruta del CLI", _cliPathField);
            if (GUILayout.Button("Guardar", GUILayout.Width(70)))
            {
                LoreCli.ConfiguredCliPath = _cliPathField;
                AppendLog($"CLI configurado: {(string.IsNullOrEmpty(_cliPathField) ? "(autodetectar)" : _cliPathField)}");
            }
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.LabelField($"En uso: {LoreCli.ResolveCliPath()}", EditorStyles.miniLabel);

            EditorGUILayout.BeginHorizontal();
            _serverPathField = EditorGUILayout.TextField("Ruta loreserver", _serverPathField);
            if (GUILayout.Button("Guardar", GUILayout.Width(70)))
            {
                LoreServerController.ConfiguredServerPath = _serverPathField;
                AppendLog("Ruta de loreserver guardada.");
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();
            _serverConfigField = EditorGUILayout.TextField("Config del server", _serverConfigField);
            if (GUILayout.Button("Guardar", GUILayout.Width(70)))
            {
                LoreServerController.ConfiguredServerConfigDir = _serverConfigField;
                AppendLog("Directorio de config del servidor guardado.");
            }
            EditorGUILayout.EndHorizontal();

            var resolvedServer = LoreServerController.ResolveServerPath() ?? "(no instalado en esta máquina)";
            EditorGUILayout.LabelField($"loreserver: {resolvedServer}", EditorStyles.miniLabel);
        }

        // ---------------------------------------------------- Pestaña Historial

        private void DrawHistoryTab()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Historial ({_history.Count} revisiones)", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Recargar", EditorStyles.miniButton, GUILayout.Width(70)))
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
                    if (GUILayout.Button("Copiar firma", EditorStyles.miniButton, GUILayout.Width(90)))
                    {
                        EditorGUIUtility.systemCopyBuffer = entry.Signature;
                        AppendLog($"Firma de #{entry.Revision} copiada.");
                    }
                    EditorGUILayout.EndHorizontal();
                }

                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            if (_history.Count == 0 && _historyLoaded)
                EditorGUILayout.LabelField("Sin revisiones.", EditorStyles.miniLabel);

            EditorGUILayout.EndScrollView();
        }

        // -------------------------------------------------------- Pestaña Merge

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
                    "No hay otras branches para mergear. Crea una en la pestaña Trabajo.",
                    MessageType.Info);
                EditorGUILayout.EndScrollView();
                return;
            }

            EditorGUILayout.LabelField("Merge", EditorStyles.boldLabel);

            _mergeSourceIndex = Mathf.Clamp(_mergeSourceIndex, 0, otherBranches.Length - 1);
            _mergeSourceIndex = EditorGUILayout.Popup("Branch origen", _mergeSourceIndex, otherBranches);
            var source = otherBranches[_mergeSourceIndex];

            EditorGUILayout.HelpBox(
                $"Se mergeará  {source}  →  {_branch}  (tu branch actual).\n" +
                "Los cambios de la branch origen se traen a la actual; la origen no se modifica.",
                MessageType.Info);

            if (string.IsNullOrEmpty(_mergeMessage))
                _mergeMessage = $"Merge {source} into {_branch}";
            _mergeMessage = EditorGUILayout.TextField("Mensaje", _mergeMessage);

            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Ver diferencias"))
                ShowBranchDiff(source);
            if (GUILayout.Button("Simular (dry-run)"))
                MergeDryRun(source);
            EditorGUILayout.EndHorizontal();

            if (GUILayout.Button($"Merge {source} → {_branch}", GUILayout.Height(28)))
                StartMerge(source);

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField(
                "Si el merge encuentra conflictos, esta pestaña cambiará al modo de " +
                "resolución donde eliges archivo por archivo entre tu versión (local) " +
                "o la de la otra branch (remota).",
                EditorStyles.wordWrappedMiniLabel);

            EditorGUILayout.EndScrollView();
        }

        private void DrawMergeConflicts()
        {
            EditorGUILayout.HelpBox(
                "Merge con conflictos en curso. Resuelve cada archivo eligiendo qué " +
                "versión conservar, y después finaliza o aborta el merge.",
                MessageType.Warning);

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Conflictos ({_conflicts.Count})", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Recargar", EditorStyles.miniButton, GUILayout.Width(70)))
                RefreshConflicts();
            EditorGUILayout.EndHorizontal();

            foreach (var path in _conflicts.ToList())
            {
                EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
                EditorGUILayout.LabelField(path, EditorStyles.miniLabel);
                if (GUILayout.Button("Local (mío)", EditorStyles.miniButton, GUILayout.Width(85)))
                    ResolveConflict(path, mine: true);
                if (GUILayout.Button("Remoto (suyo)", EditorStyles.miniButton, GUILayout.Width(95)))
                    ResolveConflict(path, mine: false);
                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Resolución masiva:", EditorStyles.miniBoldLabel);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Todo local (mío)"))
            {
                if (EditorUtility.DisplayDialog("Resolver todo con lo local",
                    "¿Conservar TU versión en todos los archivos en conflicto?",
                    "Sí, todo local", "Cancelar"))
                    ResolveConflict(".", mine: true);
            }
            if (GUILayout.Button("Todo remoto (suyo)"))
            {
                if (EditorUtility.DisplayDialog("Resolver todo con lo remoto",
                    "¿Conservar la versión de la OTRA branch en todos los archivos en conflicto?",
                    "Sí, todo remoto", "Cancelar"))
                    ResolveConflict(".", mine: false);
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(6);
            _mergeMessage = EditorGUILayout.TextField("Mensaje del merge", _mergeMessage);
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Finalizar merge (commit)", GUILayout.Height(26)))
                FinishMerge();
            if (GUILayout.Button("Abortar merge", GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog("Abortar merge",
                    "¿Descartar el merge en curso y volver al estado anterior?",
                    "Abortar", "Cancelar"))
                    AbortMerge();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawLog()
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField("Salida", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Limpiar", EditorStyles.miniButton, GUILayout.Width(60)))
                _log = "";
            EditorGUILayout.EndHorizontal();

            // Ocupa todo el alto restante de la ventana.
            _logScroll = EditorGUILayout.BeginScrollView(
                _logScroll, GUILayout.MinHeight(150), GUILayout.ExpandHeight(true));
            EditorGUILayout.TextArea(_log, EditorStyles.wordWrappedMiniLabel,
                GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
        }

        // ------------------------------------------------------------ Acciones

        private async void RefreshAll()
        {
            if (_busy) return;
            SetBusy("Consultando estado…");
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
            SetBusy("Cargando historial…");
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
            if (!SaveProjectBeforeVcsOperation("sincronizar")) return;
            SetBusy("Sync en curso…");
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
            SetBusy("Push en curso…");
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
            if (!SaveProjectBeforeVcsOperation("commitear")) return;
            SetBusy("Stage + commit…");
            try
            {
                // El .loreignore del repo filtra Library/, Temp/, etc.
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
            if (!SaveProjectBeforeVcsOperation($"cambiar a la branch '{branch}'")) return;
            SetBusy($"Cambiando a {branch}…");
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

        private async void CreateBranch(string branch)
        {
            SetBusy($"Creando branch {branch}…");
            try
            {
                var create = await LoreCli.RunAsync("branch", "create", branch);
                AppendLog(create.Combined);
                if (create.Success)
                {
                    var sw = await LoreCli.RunAsync("branch", "switch", branch);
                    AppendLog(sw.Combined);
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

        // ------------------------------------------------------- Acciones merge

        private async void ShowBranchDiff(string source)
        {
            SetBusy($"Diff {source} → {_branch}…");
            try
            {
                var result = await LoreCli.RunAsync("branch", "diff", source);
                AppendLog(result.Success && string.IsNullOrWhiteSpace(result.StdOut)
                    ? "Sin diferencias entre las branches."
                    : result.Combined);
            }
            finally
            {
                ClearBusy();
            }
        }

        private async void MergeDryRun(string source)
        {
            SetBusy("Simulando merge…");
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
            if (!SaveProjectBeforeVcsOperation($"mergear {source}")) return;
            SetBusy($"Merge {source} → {_branch}…");
            try
            {
                var result = await LoreCli.RunAsync(
                    "branch", "merge", source, "--message", _mergeMessage);
                AppendLog(result.Combined);
                AssetDatabase.Refresh();

                if (result.Success && !LooksLikeConflict(result.Combined))
                {
                    AppendLog("Merge completado. Recuerda hacer Push para publicarlo.");
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
            SetBusy($"Resolviendo {path}…");
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
            SetBusy("Finalizando merge…");
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
                    AppendLog("Merge finalizado. Recuerda hacer Push para publicarlo.");
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
            SetBusy("Abortando merge…");
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
                // Conflictos: líneas tipo "C ruta" o que mencionan conflicto con una ruta.
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

        // ------------------------------------------------------------- Parsing

        private void ParseStatus(LoreResult result)
        {
            _changes.Clear();
            if (!result.Success)
            {
                _branch = "?";
                _revision = "?";
                _syncState = "sin conexión";
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
                    _syncState = "✓ en sync";
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
                    // Texto antes de la primera revisión: ignorar.
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
            // Primera línea de `lore repository info`: "crysp-development (019f5893...)"
            if (result.Success && !string.IsNullOrWhiteSpace(result.StdOut))
            {
                var firstLine = result.StdOut.Split('\n')[0].Trim();
                var idx = firstLine.IndexOf(" (", StringComparison.Ordinal);
                if (idx > 0)
                {
                    _repoName = firstLine.Substring(0, idx);
                    return;
                }
            }
            // Sin conexión: usamos el nombre de la carpeta como aproximación.
            _repoName = System.IO.Path.GetFileName(LoreCli.ProjectRoot);
        }

        private void ParseBranches(LoreResult result)
        {
            if (!result.Success) return;

            var names = new List<string>();
            foreach (var raw in result.StdOut.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.EndsWith(":")) continue;
                var name = line.TrimStart('*', ' ');
                if (!names.Contains(name)) names.Add(name);
                if (line.StartsWith("*")) _branch = name;
            }
            _branches = names.ToArray();
            _branchIndex = Array.IndexOf(_branches, _branch);
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
        /// Limpia el campo de mensaje. IMGUI cachea el texto del control con foco,
        /// así que hay que soltar el foco antes para que el TextArea se vacíe de verdad.
        /// </summary>
        private void ClearCommitMessageField()
        {
            GUI.FocusControl(null);
            EditorGUIUtility.editingTextField = false;
            _commitMessage = "";
            Repaint();
        }

        /// <summary>
        /// Guarda escenas y assets antes de operaciones que reescriben archivos
        /// (sync, switch, merge). Devuelve false si el usuario cancela.
        /// </summary>
        private bool SaveProjectBeforeVcsOperation(string operation)
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                AppendLog($"Operación cancelada: hay escenas sin guardar (ibas a {operation}).");
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
