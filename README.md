# Lore VCS for Unity

Unity Editor integration for [Lore](https://github.com/EpicGames/lore) — Epic
Games' next-generation, open source version control system built for projects
that mix code with large binary assets.

This package wraps the `lore` CLI in a dockable editor panel, so you can manage
your whole VCS workflow — status, commits, pushes, syncs, branches, and even the
Lore server itself — without leaving Unity.

## Features

- **Repo status at a glance** — current branch, revision, and remote sync state.
- **Changes list** — added / modified / deleted files, honoring your `.loreignore`.
- **Stage + Commit (+ Push)** — one click, powered by `lore stage --scan`.
- **Sync (pull) and Push** — with scene-save prompts before file-rewriting
  operations and an automatic asset database refresh afterwards.
- **Branch management** — switch branches or create-and-switch from a dropdown.
- **Server module** — health indicator for the repo's server (local or remote,
  re-checked every 30 s). If the `loreserver` binary is installed on the machine,
  you also get Start/Stop buttons and a list of shareable `lore://` addresses
  for every active network interface, each with a copy button.
- **Cross-platform CLI detection** — finds `lore` in the usual install locations
  on macOS, Linux, and Windows; paths are configurable in the panel settings.

## Requirements

- Unity **6000.0+** (may work on earlier versions; untested).
- The [`lore` CLI](https://github.com/EpicGames/lore/releases):
  - macOS/Linux: `curl -fsSL https://raw.githubusercontent.com/EpicGames/lore/main/scripts/install.sh | bash`
  - Windows: MSI installer, or `irm https://raw.githubusercontent.com/EpicGames/lore/main/scripts/install.ps1 | iex`
- Your Unity project must be a Lore working tree (a `.lore/` folder at the
  project root, next to `Assets/`), created via `lore repository create` or
  `lore clone`.

## Installation

**Git URL** — Package Manager → `+` → *Install package from git URL*:

```
https://github.com/swordfest/lore-unity.git#1.2.0
```

**Tarball** — Package Manager → `+` → *Install package from tarball*.

**Embedded** — copy this folder into your project's `Packages/` directory.

## Usage

Open **Window → Lore** (`Cmd/Ctrl+Shift+L`).

Daily flow between machines:

1. **Sync** before you start working (the panel prompts to save open scenes).
2. Work in Unity as usual.
3. Write a commit message → **Stage + Commit + Push**.

The stage step scans the whole working tree, so your `.loreignore` decides what
gets versioned. A typical Unity ignore file:

```gitignore
Library
Temp
Logs
UserSettings
obj
Build
Builds
*.csproj
*.sln
```

### Recommended Unity settings

In `Edit → Project Settings → Editor`, enable **Asset Serialization: Force Text**
and **Visible Meta Files** so scenes and prefabs are diffable. For shared binary
assets, lock before editing: `lore lock Assets/Scenes/MyScene.unity`.

## Documentation

See [`Documentation~/lore-unity.md`](Documentation~/lore-unity.md) for the full
manual.

## License

[MIT](LICENSE.md) — © 2026 Alex Navarro.

Lore is a trademark of Epic Games, Inc. This package is an independent,
community-made integration and is not affiliated with or endorsed by Epic Games.
