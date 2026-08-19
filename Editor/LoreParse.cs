using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace LoreVcs
{
    /// <summary>A single revision entry parsed from `lore history`.</summary>
    internal class LoreHistoryEntry
    {
        public string Revision = "";
        public string Signature = "";
        public string Date = "";
        public string Committer = "";
        public string Message = "";
    }

    /// <summary>
    /// Pure parsing and config-rewriting helpers with NO Unity dependencies, so
    /// they can be unit-tested outside the editor. All the fiddly, bug-prone
    /// string handling lives here.
    /// </summary>
    internal static class LoreParse
    {
        // remote_url = "lore://host:port/path" — group 1 scheme, 2 host, 3 port,
        // 4 path (with leading '/'). The trailing " is consumed so a rewrite does
        // not leave a dangling quote.
        public static readonly Regex RemoteUrlRegex = new Regex(
            "remote_url\\s*=\\s*\"(lores?)://([^:/\"]+)(?::(\\d+))?(/[^\"]*)?\"");

        /// <summary>Host from a config.toml's remote_url, or the fallback.</summary>
        public static string RemoteHost(string configText, string fallback = "127.0.0.1")
        {
            if (string.IsNullOrEmpty(configText)) return fallback;
            var m = RemoteUrlRegex.Match(configText);
            return m.Success ? m.Groups[2].Value : fallback;
        }

        /// <summary>Port from a config.toml's remote_url, or the fallback.</summary>
        public static int RemotePort(string configText, int fallback = 41337)
        {
            if (string.IsNullOrEmpty(configText)) return fallback;
            var m = RemoteUrlRegex.Match(configText);
            if (m.Success && m.Groups[3].Success && int.TryParse(m.Groups[3].Value, out var p))
                return p;
            return fallback;
        }

        /// <summary>Full remote_url value, or empty string.</summary>
        public static string FullRemoteUrl(string configText)
        {
            if (string.IsNullOrEmpty(configText)) return "";
            var m = Regex.Match(configText, "remote_url\\s*=\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }

        /// <summary>
        /// Rewrites the host and port of remote_url, preserving scheme and path.
        /// Returns the new text, or null on failure (message explains why).
        /// </summary>
        public static string RewriteRemoteHostPort(
            string configText, string host, int port, out string message)
        {
            host = (host ?? "").Trim();
            if (host.Length == 0) { message = "Empty address; nothing changed."; return null; }
            if (port <= 0 || port > 65535) { message = $"Invalid port {port}; nothing changed."; return null; }

            var m = RemoteUrlRegex.Match(configText ?? "");
            if (!m.Success) { message = "Could not find remote_url in config.toml."; return null; }

            var scheme = m.Groups[1].Value;
            var path = m.Groups[4].Success ? m.Groups[4].Value : "";
            var replacement = $"remote_url = \"{scheme}://{host}:{port}{path}\"";
            var updated = configText.Substring(0, m.Index) + replacement +
                          configText.Substring(m.Index + m.Length);
            message = $"Server address set to {scheme}://{host}:{port}{path}";
            return updated;
        }

        /// <summary>
        /// Branch names from `lore branch list`, ignoring section headers and the
        /// connection notices/warnings the CLI mixes into the output.
        /// </summary>
        public static List<string> Branches(string stdout, out string currentBranch)
        {
            currentBranch = null;
            var names = new List<string>();
            var inSection = false;
            foreach (var raw in (stdout ?? "").Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;

                if (line.EndsWith("branches:")) { inSection = true; continue; }
                if (!inSection) continue;

                // Branch names never contain spaces or URLs; that filters out
                // "Reconnecting to http://…" and "Warning: Could not query …".
                var name = line.TrimStart('*', ' ').Trim();
                if (name.Length == 0 || name.Contains(" ") || name.Contains("://"))
                    continue;

                if (!names.Contains(name)) names.Add(name);
                if (line.StartsWith("*")) currentBranch = name;
            }
            return names;
        }

        /// <summary>Repository name from `lore repository info`, or the fallback.</summary>
        public static string RepoName(string stdout, string fallback)
        {
            foreach (var raw in (stdout ?? "").Split('\n'))
            {
                var m = Regex.Match(raw.Trim(), @"^(\S+) \([0-9a-f]{16,}\)$");
                if (m.Success) return m.Groups[1].Value;
            }
            return fallback;
        }

        /// <summary>Revision entries from `lore history` output.</summary>
        public static List<LoreHistoryEntry> History(string stdout)
        {
            var list = new List<LoreHistoryEntry>();
            LoreHistoryEntry current = null;

            foreach (var raw in (stdout ?? "").Split('\n'))
            {
                if (raw.StartsWith("Revision"))
                {
                    if (current != null) list.Add(current);
                    current = new LoreHistoryEntry { Revision = AfterColon(raw) };
                }
                else if (current == null)
                {
                    // Text before the first revision block: ignore.
                }
                else if (raw.StartsWith("Signature")) current.Signature = AfterColon(raw);
                else if (raw.StartsWith("Date")) current.Date = AfterColon(raw);
                else if (raw.StartsWith("Committer")) current.Committer = AfterColon(raw);
                else if (raw.StartsWith("    "))
                {
                    var msgLine = raw.Trim();
                    current.Message = string.IsNullOrEmpty(current.Message)
                        ? msgLine
                        : current.Message + "\n" + msgLine;
                }
            }
            if (current != null) list.Add(current);
            return list;
        }

        /// <summary>Branch, revision and sync flag from `lore status`.</summary>
        public static void Status(string stdout, out string branch, out string revision,
            out bool inSync, out List<string> changes)
        {
            branch = "?";
            revision = "?";
            inSync = false;
            changes = new List<string>();

            foreach (var raw in (stdout ?? "").Split('\n'))
            {
                var trimmed = raw.Trim();
                if (trimmed.StartsWith("On branch "))
                {
                    var parts = trimmed.Split(' ');
                    if (parts.Length >= 3) branch = parts[2];
                    var revIdx = Array.IndexOf(parts, "revision");
                    if (revIdx >= 0 && revIdx + 1 < parts.Length)
                        revision = parts[revIdx + 1];
                }
                else if (trimmed.Contains("in sync with remote"))
                {
                    inSync = true;
                }
                else if (trimmed.Length > 2 && trimmed[1] == ' ' &&
                         (trimmed[0] == 'A' || trimmed[0] == 'M' || trimmed[0] == 'D'))
                {
                    changes.Add(trimmed);
                }
            }
        }

        /// <summary>
        /// True when stderr looks like a transient QUIC/gRPC transport hiccup that
        /// is worth retrying rather than surfacing as a hard failure.
        /// </summary>
        public static bool IsTransientConnectionError(string stderr)
        {
            if (string.IsNullOrEmpty(stderr)) return false;
            foreach (var marker in TransientErrorMarkers)
                if (stderr.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static readonly string[] TransientErrorMarkers =
        {
            "transport error",
            "Not connected to remote",
            "connection was not ready",
            "acquiring remote",
            "operation was canceled",
        };

        private static string AfterColon(string line)
        {
            var idx = line.IndexOf(':');
            return idx >= 0 ? line.Substring(idx + 1).Trim() : line.Trim();
        }
    }
}
