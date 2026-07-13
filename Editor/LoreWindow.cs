using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace LoreVcs
{
    /// <summary>
    /// Panel de control de Lore VCS dentro del editor de Unity.
    /// Window → Lore. Envuelve el CLI: status, stage, commit, push, sync y branches.
    /// </summary>
    public class LoreWindow : EditorWindow
    {
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

        // Servidor
        private bool _serverHealthy;
        private bool _serverCheckDone;
        private string _serverHost = "?";
        private double _lastServerCheck;
        private string _repoName = "";
        private List<string> _localIps = new List<string>();

        // UI
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
            win.minSize = new Vector2(360, 420);
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
                EditorGUILayout.Space(4);
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

            DrawLog();

            if (_busy)
            {
                EditorGUILayout.HelpBox(_busyLabel, MessageType.Info);
            }
        }

        private void DrawHeader()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            GUILayout.Label($"Branch: {_branch}   Rev: {_revision}", EditorStyles.boldLabel);
            GUILayout.FlexibleSpace();
            if (!string.IsNullOrEmpty(_syncState))
                GUILayout.Label(_syncState, EditorStyles.miniLabel);
            if (GUILayout.Button("↻", EditorStyles.toolbarButton, GUILayout.Width(28)))
                RefreshAll();
            EditorGUILayout.EndHorizontal();
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

        private async void Sync()
        {
            if (!SaveProjectBeforeVcsOperation("sincronizar")) return;
            SetBusy("Sync en curso…");
            try
            {
                var result = await LoreCli.RunAsync("sync");
                AppendLog(result.Combined);
                AssetDatabase.Refresh();
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

                _commitMessage = "";

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

        /// <summary>
        /// Guarda escenas y assets antes de operaciones que reescriben archivos
        /// (sync, switch). Devuelve false si el usuario cancela.
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
