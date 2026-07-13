# Changelog

All notable changes to this package will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
