using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;

namespace LoreVcs
{
    /// <summary>
    /// Controls the local `loreserver` process (when installed on this machine)
    /// and checks the health of the repository's server, local or remote.
    /// </summary>
    public static class LoreServerController
    {
        private const string ServerPathPrefKey = "LoreVcs.ServerPath";
        private const string ServerConfigPrefKey = "LoreVcs.ServerConfigDir";

        public const int DefaultProtocolPort = 41337;
        // loreserver's default config exposes the HTTP health check at
        // protocol port + 2 (41337 → 41339).
        private const int HealthPortOffset = 2;

        private static int HealthPortFor(int protocolPort) => protocolPort + HealthPortOffset;

        private static readonly HttpClient Http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(3),
        };

        public static string ConfiguredServerPath
        {
            get => EditorPrefs.GetString(ServerPathPrefKey, string.Empty);
            set => EditorPrefs.SetString(ServerPathPrefKey, value ?? string.Empty);
        }

        public static string ConfiguredServerConfigDir
        {
            get => EditorPrefs.GetString(ServerConfigPrefKey, string.Empty);
            set => EditorPrefs.SetString(ServerConfigPrefKey, value ?? string.Empty);
        }

        /// <summary>Path of the loreserver binary on this machine, or null if not installed.</summary>
        public static string ResolveServerPath()
        {
            var configured = ConfiguredServerPath;
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return configured;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
#if UNITY_EDITOR_WIN
            var candidates = new[]
            {
                Path.Combine(home, "bin", "loreserver.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "lore", "loreserver.exe"),
            };
#else
            var candidates = new[]
            {
                Path.Combine(home, ".local", "bin", "loreserver"),
                "/usr/local/bin/loreserver",
                "/opt/homebrew/bin/loreserver",
            };
#endif
            foreach (var c in candidates)
                if (File.Exists(c))
                    return c;
            return null;
        }

        /// <summary>Server configuration directory (--config), or null.</summary>
        public static string ResolveServerConfigDir()
        {
            var configured = ConfiguredServerConfigDir;
            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
                return configured;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var fallback = Path.Combine(home, "loreserver", "config");
            return Directory.Exists(fallback) ? fallback : null;
        }

        private static string ConfigPath =>
            Path.Combine(LoreCli.ProjectRoot, ".lore", "config.toml");

        // remote_url = "lore://host:port" — captures scheme, host and optional port.
        private static readonly Regex RemoteUrlRegex = new Regex(
            "remote_url\\s*=\\s*\"(lores?)://([^:/\"]+)(?::(\\d+))?");

        /// <summary>Full remote_url value from .lore/config.toml, or empty if none.</summary>
        public static string RepoRemoteUrl()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return "";
                var match = Regex.Match(File.ReadAllText(ConfigPath),
                    "remote_url\\s*=\\s*\"([^\"]*)\"");
                return match.Success ? match.Groups[1].Value : "";
            }
            catch { return ""; }
        }

        /// <summary>Host of the repo's server, read from .lore/config.toml (remote_url).</summary>
        public static string RepoServerHost()
        {
            try
            {
                if (!File.Exists(ConfigPath)) return "127.0.0.1";
                var match = RemoteUrlRegex.Match(File.ReadAllText(ConfigPath));
                return match.Success ? match.Groups[2].Value : "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
            }
        }

        /// <summary>Port of the repo's server (defaults to 41337 if unspecified).</summary>
        public static int RepoServerPort()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var match = RemoteUrlRegex.Match(File.ReadAllText(ConfigPath));
                    if (match.Success && match.Groups[3].Success &&
                        int.TryParse(match.Groups[3].Value, out var p))
                        return p;
                }
            }
            catch { /* fall through to default */ }
            return DefaultProtocolPort;
        }

        /// <summary>
        /// Rewrites the host and port of remote_url in .lore/config.toml,
        /// preserving the scheme. Returns a result message. The CLI and the
        /// health check both start using the new address.
        /// </summary>
        public static string SetRepoServerAddress(string newHost, int newPort)
        {
            newHost = (newHost ?? "").Trim();
            if (newHost.Length == 0)
                return "Empty address; nothing changed.";
            if (newPort <= 0 || newPort > 65535)
                return $"Invalid port {newPort}; nothing changed.";

            try
            {
                if (!File.Exists(ConfigPath))
                    return "No .lore/config.toml found — is this a Lore working tree?";

                var text = File.ReadAllText(ConfigPath);
                var match = RemoteUrlRegex.Match(text);
                if (!match.Success)
                    return "Could not find remote_url in config.toml.";

                var scheme = match.Groups[1].Value;
                var replacement = $"remote_url = \"{scheme}://{newHost}:{newPort}\"";
                var updated = text.Substring(0, match.Index) + replacement +
                              text.Substring(match.Index + match.Length);
                File.WriteAllText(ConfigPath, updated);
                return $"Server address set to {scheme}://{newHost}:{newPort}";
            }
            catch (Exception ex)
            {
                return $"Could not update config.toml: {ex.Message}";
            }
        }

        /// <summary>True when the repo's host points at this very machine.</summary>
        public static bool RepoServerIsLocal()
        {
            var host = RepoServerHost();
            return host == "127.0.0.1" || host == "localhost";
        }

        public static bool IsProcessRunningLocally()
        {
            try
            {
                return Process.GetProcessesByName("loreserver").Length > 0;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>HTTP health check against the repo's configured server.</summary>
        public static Task<bool> CheckHealthAsync() =>
            CheckHealthAsync(RepoServerHost(), RepoServerPort());

        /// <summary>
        /// HTTP health check against an explicit host and protocol port (used by
        /// "Test"). The health endpoint lives at protocol port + 2.
        /// </summary>
        public static async Task<bool> CheckHealthAsync(string host, int protocolPort)
        {
            host = (host ?? "").Trim();
            if (host.Length == 0) return false;
            try
            {
                var response = await Http.GetAsync(
                    $"http://{host}:{HealthPortFor(protocolPort)}/health_check");
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// IPv4 addresses of this machine's active network interfaces, where the server
        /// is exposed (loreserver listens on 0.0.0.0). Excludes loopback and link-local.
        /// </summary>
        public static List<string> GetLocalIpAddresses()
        {
            var ips = new List<string>();
            try
            {
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up)
                        continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                        continue;

                    foreach (var addr in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (addr.Address.AddressFamily != AddressFamily.InterNetwork)
                            continue;
                        var ip = addr.Address.ToString();
                        if (ip.StartsWith("169.254.") || ips.Contains(ip))
                            continue;
                        ips.Add(ip);
                    }
                }
            }
            catch
            {
                // No network permissions or exotic platform: empty list.
            }
            return ips;
        }

        /// <summary>
        /// Launches loreserver detached from the editor (survives Unity closing).
        /// Returns a result message.
        /// </summary>
        public static string StartServer()
        {
            var serverPath = ResolveServerPath();
            if (serverPath == null)
                return "loreserver is not installed on this machine " +
                       "(set its path under ⚙ Settings if it lives elsewhere).";

            if (IsProcessRunningLocally())
                return "loreserver is already running.";

            var psi = new ProcessStartInfo
            {
                FileName = serverPath,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            var configDir = ResolveServerConfigDir();
            if (configDir != null)
            {
                psi.ArgumentList.Add("--config");
                psi.ArgumentList.Add(configDir);
            }

            try
            {
                var proc = Process.Start(psi);
                return $"loreserver started (PID {proc.Id})" +
                       (configDir != null ? $" with config {configDir}" : " with default config");
            }
            catch (Exception ex)
            {
                return $"Could not start loreserver: {ex.Message}";
            }
        }

        /// <summary>Stops every local loreserver process. Returns a message.</summary>
        public static string StopServer()
        {
            try
            {
                var procs = Process.GetProcessesByName("loreserver");
                if (procs.Length == 0)
                    return "No loreserver process is running on this machine.";

                foreach (var proc in procs)
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        return $"Could not stop PID {proc.Id}: {ex.Message}";
                    }
                }
                return $"loreserver stopped ({procs.Length} process(es)).";
            }
            catch (Exception ex)
            {
                return $"Error stopping loreserver: {ex.Message}";
            }
        }
    }
}
