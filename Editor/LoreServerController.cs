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
        private const int HealthPort = 41339;

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

        /// <summary>Host of the repo's server, read from .lore/config.toml (remote_url).</summary>
        public static string RepoServerHost()
        {
            try
            {
                var configPath = Path.Combine(LoreCli.ProjectRoot, ".lore", "config.toml");
                if (!File.Exists(configPath))
                    return "127.0.0.1";
                var text = File.ReadAllText(configPath);
                var match = Regex.Match(text,
                    "remote_url\\s*=\\s*\"lores?://([^:/\"]+)");
                return match.Success ? match.Groups[1].Value : "127.0.0.1";
            }
            catch
            {
                return "127.0.0.1";
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

        /// <summary>HTTP health check against the repo's server (local or remote).</summary>
        public static async Task<bool> CheckHealthAsync()
        {
            var host = RepoServerHost();
            try
            {
                var response = await Http.GetAsync(
                    $"http://{host}:{HealthPort}/health_check");
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
