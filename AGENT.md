# AGENT.md — Legacy Items (maintenance fork)

> *A practical note for humans and well-meaning automatons tending a rescued mod.*

This repo is a **maintenance fork** of **Legacy Items / “Inheritance”** (original author: **Kraut Kontrol**, Steam Workshop `3619655640`). The workshop copy stopped compiling against current Caves of Qud; this fork keeps it building and working. **Original authorship and credit remain with Kraut Kontrol** (see `README.md` → License/attribution). Do not claim original authorship.

## Layout

| Path | What it is |
|---|---|
| `manifest.json` | mod identity (`id: LegacyItems`) + `preloadScripts` |
| `Scripts/*.cs` | mod source (16 files) |
| `EmbarkModules.xml`, `LegacyItemsOptions.xml` | mod data/config |
| `README.md` | what the mod does, the compile fix, and **human install instructions** |

## The compile fix (why this fork exists)

`SavesAPI.GetSavedGameInfo()` changed to return `Task<List<SaveGameInfo>>`. The only functional change in this fork is in `Scripts/LegacyItemPart.cs` (`DoesSaveExist`): unwrap with `.GetAwaiter().GetResult()` instead of iterating the task. Keep changes minimal and focused; preserve the mod’s behavior.

## Build / test (Caves of Qud compiles script mods at startup)

- **Install (local):** copy this directory to `~/.config/unity3d/Freehold Games/CavesOfQud/Mods/LegacyItems/` (or rsync, excluding `.git`/`bin`/`obj`). Enable *Options → Modding → Allow scripting mods*.
- **Restart the game** to recompile, then check the oracle:
  ```bash
  grep -A6 'LEGACY ITEMS\|LegacyItems' ~/.config/unity3d/Freehold\ Games/CavesOfQud/build_log.txt | head -8
  ```
  Expect `Compiling 16 files...` / `Success :)` and a `LegacyItems.dll` in `ModAssemblies/`. Runtime issues: `Player.log`.
- **This machine:** source repo at `~/code/games/qud/legacy-items`; an installed copy may live at `Mods/LegacyItems/` (re-sync from the repo after changes).

## How to work

1. Keep fixes **minimal, evidence-based, and compatible** with current Qud APIs; don’t refactor broadly unless necessary.
2. After script changes, re-sync to `Mods/LegacyItems/` (if used) and **restart** to verify a clean compile.
3. Preserve attribution to Kraut Kontrol in docs/commits; follow any takedown/request noted in `README.md`.

*Install instructions for humans live in `README.md` → “Installing”.*