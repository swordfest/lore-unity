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
        private static bool IsTransientConnectionError(LoreResult r) =>
            !r.Success && LoreParse.IsTransientConnectionError(r.StdErr);

        /// <summary>
        /// Runs `lore`, retrying transient connection errors (the CLI's QUIC/gRPC
        /// transport is occasionally flaky). Retried commands here — status, list,
        /// stage, commit, push, sync — are all safe to run again on failure.
        /// </summary>
        public static async Task<LoreResult> RunAsync(params string[] args)
        {
            const int maxAttempts = 3;
            LoreResult result = default;
            for (var attempt = 1; attempt <= maxAttempts; attempt++)
            {
                result = await RunOnceAsync(300000, args);
                if (!IsTransientConnectionError(result) || attempt == maxAttempts)
                    return result;
                await Task.Delay(700 * attempt); // brief backoff before retrying
            }
            return result;
        }

        /// <summary>
        /// Single run with a custom process timeout and NO transient-error retry.
        /// Used by the reachability check, where a hung connection must be capped
        /// quickly rather than retried (the CLI already retries internally).
        /// </summary>
        public static Task<LoreResult> RunOnceWithTimeoutAsync(int timeoutMs, params string[] args) =>
            RunOnceAsync(timeoutMs, args);

        private static Task<LoreResult> RunOnceAsync(int timeoutMs, string[] args)
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
                        // Read both streams asynchronously so the timeout actually
                        // works — a blocking ReadToEnd() would wait for the process
                        // to close its pipes, defeating WaitForExit(timeout) on a
                        // hung connection.
                        var outTask = proc.StandardOutput.ReadToEndAsync();
                        var errTask = proc.StandardError.ReadToEndAsync();

                        if (!proc.WaitForExit(timeoutMs))
                        {
                            try { proc.Kill(); } catch { /* already exited */ }
                            try { proc.WaitForExit(1500); } catch { /* ignore */ }
                            return new LoreResult
                            {
                                ExitCode = -1,
                                StdOut = SafeRead(outTask),
                                StdErr = $"Timeout: lore did not respond within {timeoutMs / 1000}s.",
                            };
                        }

                        return new LoreResult
                        {
                            ExitCode = proc.ExitCode,
                            StdOut = SafeRead(outTask).Trim(),
                            StdErr = SafeRead(errTask).Trim(),
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

        private static string SafeRead(Task<string> readTask)
        {
            try { return readTask.GetAwaiter().GetResult() ?? ""; }
            catch { return ""; }
        }
    }
}
