# Legacy Items (Hand-me-downs)

**A maintenance fork of [Steam Workshop #3619655640](https://steamcommunity.com/sharedfiles/filedetails/?id=3619655640).**

> **Original mod by Kraut Kontrol.** This repository is not the original. It exists because the
> workshop copy stopped compiling against a current Caves of Qud build and, as far as I can tell,
> is no longer being maintained. I use the mod, so I fixed it and I'm keeping it building.
> Original authorship and credit remain with Kraut Kontrol — see *License* below.

---

## What the mod does

Passes items down from character deaths into a pool of **legacy items**, from which you choose one
at the start of your next run.

- **During character creation** — pick one item from the legacy pool before choosing a genotype;
  it's removed from the pool when you start.
- **On death** — one or more items (equipped and carried) are added to the pool, biased toward
  higher value (tier, artifact complexity, commerce value) and toward equipped items. Mods, charge,
  ammo, liquids, custom names and stack sizes are preserved. Natural weapons (claws) are excluded.
- **Roleplay runs** — items are only banked when a character is retired permanently, not on a
  checkpoint restore.
- **Pool management** — 25 item cap, oldest evicted first, discardable during character creation,
  pre-seeded with a few low-value items on first install so you can see it work immediately.

---

## Why this fork exists: the compile fix

After a game update the workshop copy failed to build:

```
LegacyItemPart.cs(175,38): error CS1579: foreach statement cannot operate on variables of type
'Task<List<SaveGameInfo>>' because 'Task<List<SaveGameInfo>>' does not contain a public instance
or extension definition for 'GetEnumerator'
```

The mod produced a **0-byte `LegacyItems.dll`**, so it silently did nothing.

**Cause:** `SavesAPI.GetSavedGameInfo()` changed from returning `List<SaveGameInfo>` to
`Task<List<SaveGameInfo>>`, but the mod still `foreach`-ed the raw task.

**Fix** — `Scripts/LegacyItemPart.cs`, in `DoesSaveExist(string gameId)`:

```diff
-                var saves = SavesAPI.GetSavedGameInfo();
+                // GetSavedGameInfo() returns Task<List<SaveGameInfo>> (async); unwrap it synchronously.
+                var saves = SavesAPI.GetSavedGameInfo().GetAwaiter().GetResult();
```

That is the **only** change made to the mod. All 16 scripts are otherwise byte-for-byte the
workshop version, and no other call site in the mod misuses the async API.

> ⚠️ The Steam workshop folder is re-downloaded whenever the workshop item updates or files are
> verified, so this patch does not survive there. **This repository is where the fix lives.**

---

## Installing

1. Make sure **Options → Modding → Allow scripting mods** is enabled
   (`Local/PlayerOptions.json` → `"OptionAllowCSMods": "Yes"`).
2. Install/update with the helper script (or copy manually):

   ```bash
   ./install.sh   # from this repo; uses QUD_MODS_DIR or the default Linux Mods path
   ```

   Manual copy equivalent:

   ```
   ~/.config/unity3d/Freehold Games/CavesOfQud/Mods/LegacyItems/
   ```

   (Any folder name works — the mod's identity comes from `manifest.json` → `"id": "LegacyItems"`.)
3. Launch the game. Mods are enabled by default.
4. Confirm it built:

   ```bash
   grep -A4 "LEGACY ITEMS" ~/.config/unity3d/Freehold\ Games/CavesOfQud/build_log.txt
   ```

   Expect `Compiling 16 files...`, `Success :)`, and
   `Location: …/ModAssemblies/LegacyItems.dll`.

`obj/` in this repository is a local .NET restore cache and is not needed to run the mod.

---

## License

The original author has not declared a license for this mod. Absent one, all rights remain with
**Kraut Kontrol**; this fork redistributes it only so that it can be maintained and kept working,
with attribution. If you are Kraut Kontrol and would like this repository changed or removed, open
an issue and I'll do it.

The compile fix itself is trivial and documented above, so it can be re-applied to the workshop
copy in one line if you'd rather not run a fork.
