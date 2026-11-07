using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security;
using XRL;
using XRL.Core;
using XRL.World;
using XRL.World.Parts;
using XRL.World.Tinkering;

namespace LegacyItems
{
    /// <summary>
    /// Manages the persistent pool of legacy items from character deaths.
    /// Handles file I/O, FIFO queue management, and blueprint validation.
    /// </summary>
    public static class LegacyPoolManager
    {
        private static string _poolFilePath;
        private static readonly string PoolRelativePath = Path.Combine("LegacyItems", "legacy_pool.txt");

        /// <summary>
        /// Gets the platform-specific path for the legacy pool file.
        /// </summary>
        public static string PoolFilePath
        {
            get
            {
                if (_poolFilePath != null)
                    return _poolFilePath;

                string syncedPath = null;
                try
                {
                    syncedPath = DataManager.SyncedPath(PoolRelativePath);
                    EnsureDirectoryExists(Path.GetDirectoryName(syncedPath));
                }
                catch (SecurityException)
                {
                    LegacyItemOptions.LogDebug("LegacyItems: DataManager.SyncedPath unavailable in this environment; using legacy path.");
                }

                var legacyPath = GetLegacyFallbackPath();

                if (!string.IsNullOrEmpty(syncedPath))
                {
                    TryMigrateLegacyPool(legacyPath, syncedPath);

                    // Prefer synced path unless migration failed and legacy exists
                    _poolFilePath = File.Exists(syncedPath)
                        ? syncedPath
                        : File.Exists(legacyPath) ? legacyPath : syncedPath;
                }
                else
                {
                    EnsureDirectoryExists(Path.GetDirectoryName(legacyPath));
                    _poolFilePath = legacyPath;
                }

                return _poolFilePath;
            }
        }

        /// <summary>
        /// Represents a single legacy item entry in the pool.
        /// </summary>
        public class LegacyItemEntry
        {
            public string ID { get; set; }
            public string Source { get; set; }
            public string Blueprint { get; set; }
            public string DisplayName { get; set; }
            public int Tier { get; set; }
            public int TechTier { get; set; }
            public string Mods { get; set; }
            public bool IsArtifact { get; set; }
            public string CharacterName { get; set; }
            public string StackSize { get; set; }
            public string Charges { get; set; }
            public string LiquidType { get; set; }
            public string LiquidDrams { get; set; }
            public string Durability { get; set; }
            public string CustomColors { get; set; }
            /// <summary>
            /// The base Render.DisplayName without mod prefixes.
            /// Used to restore custom item names (artifacts, material names, etc.)
            /// while letting mods add their prefixes dynamically.
            /// </summary>
            public string BaseDisplayName { get; set; }
            public string Tile { get; set; } // Tile path for icon display
            public string TileColor { get; set; } // Foreground color for tile
            public string DetailColor { get; set; } // Detail color for tile
            public string DeathReason { get; set; } // e.g., "frozen to death by Warden Yrame"

            /// <summary>
            /// Extended mod data with tier and state information.
            /// Format: "ModName:Tier:StateJSON;ModName2:Tier2:StateJSON2"
            /// For backward compatibility, if ModData is empty, falls back to Mods field.
            /// </summary>
            public string ModData { get; set; }

            /// <summary>
            /// Projectile-specific properties that need preservation.
            /// Format: "PenetrateCreatures:true;PenetrateWalls:false;Attributes:attr1"
            /// </summary>
            public string ProjectileData { get; set; }

            /// <summary>
            /// Examiner part identification state for artifacts.
            /// Format: "EpistemicStatus:Understanding" e.g., "3:100" (KNOWN with understanding=100)
            /// Empty string = no Examiner part or default state.
            /// </summary>
            public string ExaminerState { get; set; } = "";

            /// <summary>
            /// LightSource Lit state for torches and other light sources.
            /// Values: "true", "false", or "" (no LightSource part)
            /// </summary>
            public string LightSourceLit { get; set; } = "";

            /// <summary>
            /// DataDisk blueprint (recipe) for data disks.
            /// Stores Data.Blueprint to prevent random recipe selection on deserialization.
            /// Empty string = no DataDisk part
            /// </summary>
            public string DataDiskBlueprint { get; set; } = "";

            /// <summary>
            /// Liquid stain name/color (e.g., "{{r|blood}}-stained") from LiquidVolume.
            /// </summary>
            public string StainName { get; set; } = "";
            public string StainColor { get; set; } = "";

            /// <summary>
            /// Liquid stain data (liquid ID and drams) for restoring LiquidStained effect.
            /// </summary>
            public string StainLiquid { get; set; } = "";
            public string StainLiquidDrams { get; set; } = "";

            /// <summary>
            /// Converts this entry to pipe-delimited format.
            /// </summary>
            // Escape/unescape pipe characters to prevent delimiter corruption
            private const string PIPE_ESCAPE = "{{PIPE}}";

            private static string EscapePipe(string value)
            {
                return value?.Replace("|", PIPE_ESCAPE) ?? "";
            }

            private static string UnescapePipe(string value)
            {
                return value?.Replace(PIPE_ESCAPE, "|") ?? "";
            }

            public string ToFileFormat()
            {
                return string.Join("|", new[]
                {
                    EscapePipe(ID),
                    EscapePipe(Source),
                    EscapePipe(Blueprint),
                    EscapePipe(DisplayName),
                    Tier.ToString(),
                    TechTier.ToString(),
                    EscapePipe(Mods),
                    IsArtifact ? "1" : "0",
                    EscapePipe(CharacterName),
                    EscapePipe(StackSize),
                    EscapePipe(Charges),
                    EscapePipe(LiquidType),
                    EscapePipe(LiquidDrams),
                    EscapePipe(Durability),
                    EscapePipe(CustomColors),
                    EscapePipe(BaseDisplayName),
                    EscapePipe(Tile),
                    EscapePipe(TileColor),
                    EscapePipe(DetailColor),
                    EscapePipe(DeathReason),
                    EscapePipe(ModData ?? ""),           // Field 21
                    EscapePipe(ProjectileData ?? ""),    // Field 22
                    EscapePipe(ExaminerState ?? ""),     // Field 23
                    EscapePipe(LightSourceLit ?? ""),    // Field 24
                    EscapePipe(DataDiskBlueprint ?? ""), // Field 25
                    EscapePipe(StainName ?? ""),         // Field 26
                    EscapePipe(StainColor ?? ""),        // Field 27
                    EscapePipe(StainLiquid ?? ""),       // Field 28
                    EscapePipe(StainLiquidDrams ?? "")   // Field 29
                });
            }

            /// <summary>
            /// Parses an entry from pipe-delimited format.
            /// Supports: 16-field (original), 19-field (tile info), 20-field (with death reason), 22-field (with mod/projectile data), 23-field (with examiner state), 24-field (with light source state), 25-field (with data disk blueprint), 27-field (with stain info), 29-field (with stain liquid)
            /// </summary>
            public static LegacyItemEntry FromFileFormat(string line)
            {
                var parts = line.Split('|');
                // Support old and new formats
                if (parts.Length != 16 && parts.Length != 19 && parts.Length != 20 && parts.Length != 22 && parts.Length != 23 && parts.Length != 24 && parts.Length != 25 && parts.Length != 27 && parts.Length != 29)
                {
                    LegacyItemOptions.LogError($"LegacyItems: Failed to parse pool entry - invalid field count: {parts.Length} (expected 16, 19, 20, 22, 23, 24, 25, 27, or 29). Line: {line.Substring(0, Math.Min(100, line.Length))}...");
                    return null;
                }

                var entry = new LegacyItemEntry
                {
                    ID = UnescapePipe(parts[0]),
                    Source = UnescapePipe(parts[1]),
                    Blueprint = UnescapePipe(parts[2]),
                    DisplayName = UnescapePipe(parts[3]),
                    Tier = int.TryParse(parts[4], out int tier) ? tier : 0,
                    TechTier = int.TryParse(parts[5], out int techTier) ? techTier : 0,
                    Mods = UnescapePipe(parts[6]),
                    IsArtifact = parts[7] == "1",
                    CharacterName = UnescapePipe(parts[8]),
                    StackSize = UnescapePipe(parts[9]),
                    Charges = UnescapePipe(parts[10]),
                    LiquidType = UnescapePipe(parts[11]),
                    LiquidDrams = UnescapePipe(parts[12]),
                    Durability = UnescapePipe(parts[13]),
                    CustomColors = UnescapePipe(parts[14]),
                    BaseDisplayName = UnescapePipe(parts[15])
                };

                // Parse tile fields if present (19+ fields)
                if (parts.Length >= 19)
                {
                    entry.Tile = UnescapePipe(parts[16]);
                    entry.TileColor = UnescapePipe(parts[17]);
                    entry.DetailColor = UnescapePipe(parts[18]);
                }

                // Parse death reason if present (20+ fields)
                if (parts.Length >= 20)
                {
                    entry.DeathReason = UnescapePipe(parts[19]);
                }

                // Parse mod data and projectile data if present (22 fields)
                if (parts.Length >= 22)
                {
                    entry.ModData = UnescapePipe(parts[20]);
                    entry.ProjectileData = UnescapePipe(parts[21]);
                }

                // Parse examiner state if present (23 fields)
                if (parts.Length >= 23)
                {
                    entry.ExaminerState = UnescapePipe(parts[22]);
                }

                // Parse light source state if present (24 fields)
                if (parts.Length >= 24)
                {
                    entry.LightSourceLit = UnescapePipe(parts[23]);
                }

                // Parse data disk blueprint if present (25 fields)
                if (parts.Length >= 25)
                {
                    entry.DataDiskBlueprint = UnescapePipe(parts[24]);
                }

                // Parse stain info if present (27 fields)
                if (parts.Length >= 27)
                {
                    entry.StainName = UnescapePipe(parts[25]);
                    entry.StainColor = UnescapePipe(parts[26]);
                }

                // Parse stain liquid if present (29 fields)
                if (parts.Length >= 29)
                {
                    entry.StainLiquid = UnescapePipe(parts[27]);
                    entry.StainLiquidDrams = UnescapePipe(parts[28]);
                }

                return entry;
            }
        }

        /// <summary>
        /// Loads the pool from disk, validates blueprints, and returns entries.
        /// </summary>
        public static List<LegacyItemEntry> LoadPool()
        {
            return LoadPoolInternal(trimToMax: false);
        }

        /// <summary>
        /// Loads the pool from disk without trimming to the current max size.
        /// Useful for option change handling where we need the raw count.
        /// </summary>
        public static List<LegacyItemEntry> LoadPoolUntrimmed()
        {
            return LoadPoolInternal(trimToMax: false);
        }

        private static List<LegacyItemEntry> LoadPoolInternal(bool trimToMax)
        {
            LegacyItemOptions.LogDebug($"LegacyItems: LoadPool - PoolFilePath='{PoolFilePath}'");
            LegacyItemOptions.LogDebug($"LegacyItems: LoadPool - File.Exists={File.Exists(PoolFilePath)}");

            if (!File.Exists(PoolFilePath))
                return new List<LegacyItemEntry>();

            List<LegacyItemEntry> entries = new List<LegacyItemEntry>();
            try
            {
                var lines = File.ReadAllLines(PoolFilePath);
                entries = ParsePoolLines(lines)
                    .Where(EntryHasValidBlueprint)
                    .ToList();
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError("LegacyItems: Error loading pool - " + ex.Message);
            }

            if (trimToMax)
            {
                var truncated = TruncateToMax(entries, LegacyItemOptions.MaxPoolSize);
                if (truncated.Count != entries.Count)
                {
                    SavePool(truncated);
                    entries = truncated;
                }
            }

            return entries;
        }

        /// <summary>
        /// Saves the pool to disk.
        /// </summary>
        public static void SavePool(List<LegacyItemEntry> entries)
        {
            try
            {
                var trimmed = TruncateToMax(entries, LegacyItemOptions.MaxPoolSize);
                if (trimmed.Count != entries.Count)
                {
                    LegacyItemOptions.LogDebug($"LegacyItems: SavePool - Trimmed pool from {entries.Count} to {trimmed.Count} (max {LegacyItemOptions.MaxPoolSize})");
                }

                LegacyItemOptions.LogDebug($"LegacyItems: SavePool - Saving {trimmed.Count} entries to '{PoolFilePath}'");
                File.WriteAllLines(PoolFilePath, trimmed.Select(e => e.ToFileFormat()));
                LegacyItemOptions.LogDebug($"LegacyItems: SavePool - Successfully saved pool");
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError("LegacyItems: Error saving pool - " + ex.Message);
            }
        }

        private static IEnumerable<LegacyItemEntry> ParsePoolLines(IEnumerable<string> lines)
        {
            foreach (var line in lines.Where(l => !string.IsNullOrWhiteSpace(l)))
            {
                var entry = LegacyItemEntry.FromFileFormat(line);
                if (entry == null)
                {
                    LegacyItemOptions.LogError("LegacyItems: Skipping unparseable pool entry (see error above)");
                    continue;
                }

                yield return entry;
            }
        }

        private static bool EntryHasValidBlueprint(LegacyItemEntry entry)
        {
            if (entry == null)
                return false;

            if (GameObjectFactory.Factory.Blueprints.ContainsKey(entry.Blueprint))
                return true;

            LegacyItemOptions.LogError($"LegacyItems: Skipping pool entry '{entry.DisplayName}' (ID: {entry.ID}) - blueprint '{entry.Blueprint}' does not exist in the game. This may be from an uninstalled mod or old game version.");
            return false;
        }

        private static List<LegacyItemEntry> TruncateToMax(List<LegacyItemEntry> entries, int maxPoolSize)
        {
            if (entries == null || entries.Count <= maxPoolSize)
                return entries ?? new List<LegacyItemEntry>();

            return entries.Skip(entries.Count - maxPoolSize).ToList();
        }

        /// <summary>
        /// Adds items to the pool, enforcing FIFO and size limits.
        /// </summary>
        public static void AddToPool(LegacyItemEntry equippedItem, LegacyItemEntry inventoryItem)
        {
            var additions = new[] { equippedItem, inventoryItem }.Where(e => e != null);
            var updated = AppendAndTrim(LoadPool(), additions, LegacyItemOptions.MaxPoolSize);
            SavePool(updated);
        }

        /// <summary>
        /// Generates a unique ID for a new pool entry.
        /// </summary>
        public static string GenerateID()
        {
            var timestamp = DateTime.UtcNow.Ticks;
            var random = XRL.Rules.Stat.Random(1, 999999);
            return $"{timestamp}_{random}";
        }

        /// <summary>
        /// Checks if the pool exists and is initialized.
        /// </summary>
        public static bool PoolExists()
        {
            return File.Exists(PoolFilePath);
        }

        /// <summary>
        /// Clears all items from the pool.
        /// </summary>
        public static void ClearPool()
        {
            SavePool(new List<LegacyItemEntry>());
            LegacyItemOptions.LogDebug("LegacyItems: Pool cleared");
        }

        /// <summary>
        /// Initializes the pool with preseed items if it doesn't exist and preseed option is enabled.
        /// </summary>
        public static void InitializePoolIfNeeded()
        {
            if (!PoolExists())
            {
                // Check if preseed items should be added based on option
                if (LegacyItemOptions.EnablePreseed)
                {
                    var preseedEntries = GetPreseedItems();
                    SavePool(preseedEntries);
                    LegacyItemOptions.LogDebug("LegacyItems: Initialized pool with preseed items");
                }
                else
                {
                    // Create empty pool
                    SavePool(new List<LegacyItemEntry>());
                    LegacyItemOptions.LogDebug("LegacyItems: Initialized empty pool (preseed disabled)");
                }
            }
        }

        /// <summary>
        /// Adds preseed items into the pool. Existing items are kept; oldest items are dropped if over max size.
        /// </summary>
        public static void RestockPreseedItems()
        {
            var updated = AppendAndTrim(LoadPool(), GetPreseedItems(), LegacyItemOptions.MaxPoolSize);
            SavePool(updated);
            LegacyItemOptions.LogDebug($"LegacyItems: Restocked preseed items; pool size now {updated.Count}");
        }

        /// <summary>
        /// Returns the starter preseed items for initial pool creation.
        /// Includes test items demonstrating the fixed bugs.
        /// </summary>
        private static List<LegacyItemEntry> GetPreseedItems()
        {
            var starterBlueprints = new (string blueprint, string mods, string liquidType, string liquidDrams, string stackSize, string modData)[]
            {
                ("Waterskin", "", "honey", "32", "", ""),
                ("Floating Glowsphere", "", "", "", "", ""),
                ("Bandage", "", "", "", "10", ""),
                ("Long Sword2", "Flaming", "", "", "", "ModFlaming:2:"),  // ModFlaming adds {{R|flaming}} prefix dynamically
                ("Gold Nugget", "", "", "", "", ""),
                ("Pickaxe", "", "", "", "", ""),
                // Test item for tier preservation - ModFreezing adds {{M|freezing}} prefix dynamically
                ("Dagger3", "Freezing", "", "", "", "ModFreezing:3:")
            };

            return starterBlueprints
                .Select(BuildPreseedEntry)
                .Where(entry => entry != null)
                .ToList();
        }

        private static LegacyItemEntry BuildPreseedEntry((string blueprint, string mods, string liquidType, string liquidDrams, string stackSize, string modData) definition)
        {
            GameObject tempItem = null;
            try
            {
                tempItem = GameObjectFactory.Factory.CreateObject(definition.blueprint, BonusModChance: -999);
                if (tempItem == null)
                    return null;

                ApplyPreseedMods(tempItem, definition.modData);

                var entry = LegacyItemSerializer.SerializeItem(tempItem, "PRESEED", "");
                entry.LiquidType = definition.liquidType;
                entry.LiquidDrams = definition.liquidDrams;
                entry.StackSize = definition.stackSize;
                entry.CharacterName = "";  // Empty for preseed items
                return entry;
            }
            finally
            {
                tempItem?.Obliterate();
            }
        }

        private static void ApplyPreseedMods(GameObject tempItem, string modData)
        {
            if (string.IsNullOrEmpty(modData))
                return;

            var modEntries = modData.Split(';');
            foreach (var modEntry in modEntries)
            {
                if (string.IsNullOrEmpty(modEntry))
                    continue;

                var parts = modEntry.Split(':');
                if (parts.Length >= 2 && int.TryParse(parts[1], out int tier))
                {
                    ItemModding.ApplyModification(tempItem, parts[0], tier);
                }
            }
        }

        /// <summary>
        /// Gets the directory where mod save files are stored.
        /// </summary>
        public static string ModSaveDirectory
        {
            get
            {
                return Path.GetDirectoryName(PoolFilePath);
            }
        }

        /// <summary>
        /// Ensures the directory for the given path exists.
        /// </summary>
        private static void EnsureDirectoryExists(string directory)
        {
            if (string.IsNullOrEmpty(directory))
                return;

            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);
        }

        /// <summary>
        /// Legacy path used before switching to DataManager.SyncedPath().
        /// </summary>
        private static string GetLegacyFallbackPath()
        {
            string basePath;
            var platform = Environment.OSVersion.Platform;

            if (platform == PlatformID.Win32NT || platform == PlatformID.Win32S || platform == PlatformID.Win32Windows || platform == PlatformID.WinCE)
            {
                basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "AppData", "LocalLow", "Freehold Games", "CavesOfQud", "LegacyItems"
                );
            }
            else if (platform == PlatformID.MacOSX)
            {
                basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    "Library", "Application Support", "com.FreeholdGames.CavesOfQud", "LegacyItems"
                );
            }
            else
            {
                basePath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.Personal),
                    ".config", "unity3d", "Freehold Games", "CavesOfQud", "LegacyItems"
                );
            }

            return Path.Combine(basePath, "legacy_pool.txt");
        }

        /// <summary>
        /// Copies an existing legacy pool file into the synced path to avoid data loss.
        /// </summary>
        private static void TryMigrateLegacyPool(string legacyPath, string syncedPath)
        {
            try
            {
                if (string.IsNullOrEmpty(legacyPath) || string.IsNullOrEmpty(syncedPath))
                    return;

                if (File.Exists(syncedPath) || !File.Exists(legacyPath))
                    return;

                EnsureDirectoryExists(Path.GetDirectoryName(syncedPath));
                File.Copy(legacyPath, syncedPath, overwrite: false);
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"LegacyItems: Failed to migrate legacy pool file: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds a list of entries to the pool.
        /// </summary>
        public static void AddEntriesToPool(List<LegacyItemEntry> entries)
        {
            var updated = AppendAndTrim(LoadPool(), entries, LegacyItemOptions.MaxPoolSize);
            SavePool(updated);
        }

        /// <summary>
        /// Trims the pool to the specified maximum size (FIFO: keep most recent).
        /// </summary>
        public static void TrimPoolToMax(int maxSize)
        {
            if (maxSize < 1)
                return;

            var truncated = TruncateToMax(LoadPool(), maxSize);
            if (truncated.Count > 0)
            {
                SavePool(truncated);
            }
        }

        private static List<LegacyItemEntry> AppendAndTrim(IEnumerable<LegacyItemEntry> pool, IEnumerable<LegacyItemEntry> additions, int maxSize)
        {
            var combined = (pool ?? Enumerable.Empty<LegacyItemEntry>()).Where(e => e != null).ToList();
            if (additions != null)
            {
                combined.AddRange(additions.Where(e => e != null));
            }

            return TruncateToMax(combined, maxSize);
        }
    }
}
