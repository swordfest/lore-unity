# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.7.0] - 2026-07-13

### Added
- Automatic retry (up to 3 attempts, with backoff) when the Lore CLI reports a
  transient QUIC/gRPC transport error, which the 0.8.4 transport hits often on
  flaky connections. Smooths over intermittent "transport error" / "Not
  connected to remote" failures during status, stage, commit, push and sync.
- EditMode unit test suite (`Tests/Editor`) covering the parsing and config
  rewrite logic, including regressions for the doubled-quote and
  notices-as-branches bugs.

### Changed
- Reachability check (Test button and status dot) now probes the protocol port
  directly via TCP — the same port the CLI uses — instead of deriving a health
  port. The port you type is the port that gets tested. Fixes false "offline"
  results caused by the previous HttpClient + port-derivation approach.
- Parsing and config-rewrite logic extracted to a Unity-independent `LoreParse`
  class so it can be unit-tested.

## [1.6.2] - 2026-07-13

### Fixed
- Server reachability check (health dot and Test button) now uses a raw TCP
  connect instead of HttpClient, which is unreliable inside the Unity editor
  (Mono's System.Net.Http stack) and reported reachable servers as offline.

## [1.6.1] - 2026-07-13

### Fixed
- Apply no longer corrupts `.lore/config.toml` with a doubled closing quote
  (`...41337""`), which made the CLI fail with a TOML parse error. The
  remote_url regex now consumes the closing quote and preserves any path.

## [1.6.0] - 2026-07-13

### Added
- Server **Port** field next to Address (defaults to 41337, the Lore protocol
  port). Test and Apply now use it; the HTTP health check port is derived as
  port + 2 (matching loreserver's default). Shareable addresses and the status
  row reflect the configured port instead of a hardcoded 41337.

## [1.5.0] - 2026-07-13

### Added
- Server **Address** field: type the Lore server host/IP, **Test** it (health
  check without saving) and **Apply** it. Apply rewrites the host of
  `remote_url` in `.lore/config.toml` (preserving scheme and port), so both the
  CLI and the health check start using the new address. Fixes the client
  staying stuck on an old IP after the server's address changes.

## [1.4.2] - 2026-07-13

### Changed
- The window no longer re-queries repo state every time it gains focus
  (every click used to trigger a refresh and block the UI). State refreshes
  on open, after each action, and manually via the ↻ button.

## [1.4.1] - 2026-07-13

### Fixed
- Branch dropdowns (Branches, From branch, Source branch) no longer show CLI
  connection notices and warnings ("Reconnecting to http://…", "Warning: Could
  not query remote branch list") as if they were branches — parsing is now
  scoped to the Local/Remote branches sections and rejects non-branch lines.
- Branch names containing "/" no longer turn into popup submenus.
- Repository name parsing now skips connection notices as well.

## [1.4.0] - 2026-07-13

### Added
- Branch creation now lets you pick the **source branch**: the plugin switches
  to it, creates the new branch there, and switches to the new branch.

### Changed
- Entire plugin UI translated to English (tabs, buttons, dialogs, log messages,
  and code documentation).

## [1.3.0] - 2026-07-13

### Added
- Window tabs: Work / History / Merge.
- History: revision timeline (number, message, date, author); selecting an
  entry reveals the full message and the revision signature with a Copy button.
- Merge: source branch selection with a clear direction indicator
  (source → current branch), preview via "View differences" and
  "Simulate (dry-run)", and execution with a custom message.
- Conflict resolution: list of conflicted files with per-file
  "Local (mine)" / "Remote (theirs)" buttons, bulk resolution, finish merge
  (commit) and abort merge, all guarded by confirmation dialogs.

### Fixed
- The commit message field now actually clears after Stage + Commit (+ Push) —
  IMGUI focus used to retain the text.

## [1.2.0] - 2026-07-12

### Added
- "Shareable addresses": list of `lore://` URLs for every active network
  interface when the server runs locally, each with a Copy button.
- "Clear" button in the output panel.

### Changed
- The output (log) panel now expands to fill the remaining window height
  (minimum 150 px).

## [1.1.0] - 2026-07-12

### Added
- Server module: health indicator for the repo's server (HTTP health check
  every 30 s, local or remote) and Start/Stop buttons when the `loreserver`
  binary is installed on the machine.
- Settings to configure the `loreserver` path and its config directory.

## [1.0.0] - 2026-07-12

### Added
- `Window → Lore` panel (Cmd/Ctrl+Shift+L) wrapping the Lore CLI.
- Repo status: branch, revision, remote sync state and A/M/D change list.
- Stage + Commit (+ Push), Sync (pull) and Push.
- Create and switch branches, saving scenes before and refreshing assets after.
- Cross-platform auto-detection of the `lore` CLI with a configurable path.
