using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;

namespace LoreVcs
{
    /// <summary>
    /// Result of a Lore CLI invocation.
    /// </summary>
    public struct LoreResult
    {
        public int ExitCode;
        public string StdOut;
        public string StdErr;
        public bool Success => ExitCode == 0;
        public string Combined =>
            string.IsNullOrEmpty(StdErr) ? StdOut : (StdOut + "\n" + StdErr).Trim();
    }

    /// <summary>
    /// Static wrapper around the `lore` CLI. Runs commands asynchronously
    /// with the working directory at the project root (where .lore/ lives).
    /// </summary>
    public static class LoreCli
    {
        private const string CliPathPrefKey = "LoreVcs.CliPath";

        public static string ProjectRoot =>
            Path.GetDirectoryName(UnityEngine.Application.dataPath);

        /// <summary>Manually configured path (EditorPrefs), or empty to auto-detect.</summary>
        public static string ConfiguredCliPath
        {
            get => EditorPrefs.GetString(CliPathPrefKey, string.Empty);
            set => EditorPrefs.SetString(CliPathPrefKey, value ?? string.Empty);
        }

        /// <summary>
        /// Locates the lore binary. Unity (especially on macOS) does not inherit the
        /// shell's PATH, so the typical install locations are probed.
        /// </summary>
        public static string ResolveCliPath()
        {
            var configured = ConfiguredCliPath;
            if (!string.IsNullOrEmpty(configured) && File.Exists(configured))
                return configured;

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var candidates = new List<string>();

#if UNITY_EDITOR_WIN
            candidates.Add(Path.Combine(home, "bin", "lore.exe"));
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            candidates.Add(Path.Combine(localAppData, "Programs", "lore", "lore.exe"));
            var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
            candidates.Add(Path.Combine(programFiles, "Lore", "lore.exe"));
#else
            candidates.Add(Path.Combine(home, ".local", "bin", "lore"));
            candidates.Add("/usr/local/bin/lore");
            candidates.Add("/opt/homebrew/bin/lore");
#endif
            foreach (var c in candidates)
            {
                if (File.Exists(c))
                    return c;
            }
            // Last resort: trust the PATH.
#if UNITY_EDITOR_WIN
            return "lore.exe";
#else
            return "lore";
#endif
        }

        /// <summary>Runs `lore` with the given arguments without blocking the main thread.</summary>
        public static Task<LoreResult> RunAsync(params string[] args)
        {
            var cliPath = ResolveCliPath();
            var workingDir = ProjectRoot;

            return Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = cliPath,
                    WorkingDirectory = workingDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };
                // Pagination disabled so the CLI never blocks waiting for a pager.
                psi.ArgumentList.Add("--no-pager");
                foreach (var a in args)
                    psi.ArgumentList.Add(a);

                try
                {
                    using (var proc = Process.Start(psi))
                    {
                        var stdout = proc.StandardOutput.ReadToEnd();
                        var stderr = proc.StandardError.ReadToEnd();
                        if (!proc.WaitForExit(300000))
                        {
                            try { proc.Kill(); } catch { /* already exited */ }
                            return new LoreResult
                            {
                                ExitCode = -1,
                                StdOut = stdout,
                                StdErr = "Timeout: lore took longer than 5 minutes.",
                            };
                        }
                        return new LoreResult
                        {
                            ExitCode = proc.ExitCode,
                            StdOut = stdout.Trim(),
                            StdErr = stderr.Trim(),
                        };
                    }
                }
                catch (Exception ex)
                {
                    return new LoreResult
                    {
                        ExitCode = -1,
                        StdOut = string.Empty,
                        StdErr = "Could not run '" + cliPath + "': " + ex.Message +
                                 "\nSet the CLI path in the Lore window (⚙ Settings).",
                    };
                }
            });
        }
    }
}
