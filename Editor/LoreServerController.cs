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
    /// Controla el proceso local de `loreserver` (si está instalado en esta máquina)
    /// y consulta la salud del servidor del repositorio, sea local o remoto.
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

        /// <summary>Ruta del binario loreserver en esta máquina, o null si no está instalado.</summary>
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

        /// <summary>Directorio de configuración del servidor (--config), o null.</summary>
        public static string ResolveServerConfigDir()
        {
            var configured = ConfiguredServerConfigDir;
            if (!string.IsNullOrEmpty(configured) && Directory.Exists(configured))
                return configured;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var fallback = Path.Combine(home, "loreserver", "config");
            return Directory.Exists(fallback) ? fallback : null;
        }

        /// <summary>Host del servidor del repo, leído de .lore/config.toml (remote_url).</summary>
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

        /// <summary>True si el host del repo apunta a esta misma máquina.</summary>
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

        /// <summary>Health check HTTP contra el servidor del repo (local o remoto).</summary>
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
        /// IPs v4 de las interfaces de red activas de esta máquina, donde el servidor
        /// queda expuesto (loreserver escucha en 0.0.0.0). Excluye loopback y link-local.
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
                // Sin permisos de red o plataforma rara: lista vacía.
            }
            return ips;
        }

        /// <summary>
        /// Lanza loreserver desacoplado del editor (sigue vivo si Unity se cierra).
        /// Devuelve mensaje de resultado.
        /// </summary>
        public static string StartServer()
        {
            var serverPath = ResolveServerPath();
            if (serverPath == null)
                return "loreserver no está instalado en esta máquina " +
                       "(configura la ruta en ⚙ Ajustes si está en otro lugar).";

            if (IsProcessRunningLocally())
                return "loreserver ya está corriendo.";

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
                return $"loreserver iniciado (PID {proc.Id})" +
                       (configDir != null ? $" con config {configDir}" : " con config por defecto");
            }
            catch (Exception ex)
            {
                return $"No se pudo iniciar loreserver: {ex.Message}";
            }
        }

        /// <summary>Detiene todos los procesos loreserver locales. Devuelve mensaje.</summary>
        public static string StopServer()
        {
            try
            {
                var procs = Process.GetProcessesByName("loreserver");
                if (procs.Length == 0)
                    return "No hay ningún loreserver corriendo en esta máquina.";

                foreach (var proc in procs)
                {
                    try
                    {
                        proc.Kill();
                        proc.WaitForExit(5000);
                    }
                    catch (Exception ex)
                    {
                        return $"No se pudo detener el PID {proc.Id}: {ex.Message}";
                    }
                }
                return $"loreserver detenido ({procs.Length} proceso(s)).";
            }
            catch (Exception ex)
            {
                return $"Error deteniendo loreserver: {ex.Message}";
            }
        }
    }
}
