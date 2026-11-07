using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XRL;
using XRL.UI;
using XRL.Wish;
using XRL.World;
using XRL.World.Parts;

namespace LegacyItems
{
    /// <summary>
    /// Wish commands for testing and debugging the legacy items system.
    /// </summary>
    [HasWishCommand]
    public class LegacyItemWishCommands
    {
        /// <summary>
        /// Shows the current legacy pool contents.
        /// Usage: wish:legacypool
        /// </summary>
        [WishCommand("legacypool")]
        public static bool ShowLegacyPool()
        {
            try
            {
                var pool = LegacyPoolManager.LoadPool();
                var summary = LegacyItemSelectionUI.GetPoolSummary(pool);

                // Debug log each pool item
                LegacyItemOptions.LogDebug($"LegacyItems: Displaying pool with {pool.Count} items:");
                for (int i = 0; i < pool.Count; i++)
                {
                    var entry = pool[i];
                    LegacyItemOptions.LogDebug($"  [{i}] {entry.DisplayName} (Blueprint: {entry.Blueprint}, Tier: {entry.Tier}, Source: {entry.Source}, Character: {entry.CharacterName ?? "N/A"})");
                    if (!string.IsNullOrEmpty(entry.ModData))
                        LegacyItemOptions.LogDebug($"      ModData: {entry.ModData}");
                    if (!string.IsNullOrEmpty(entry.ProjectileData))
                        LegacyItemOptions.LogDebug($"      ProjectileData: {entry.ProjectileData}");
                }

                Popup.Show(summary);
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error loading legacy pool: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Clears the entire legacy pool.
        /// Usage: wish:legacyclear
        /// </summary>
        [WishCommand("legacyclear")]
        public static bool ClearLegacyPool()
        {
            try
            {
                LegacyPoolManager.SavePool(new List<LegacyPoolManager.LegacyItemEntry>());
                Popup.Show("Legacy pool cleared!");
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error clearing legacy pool: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Adds an item to the legacy pool for testing.
        /// Usage: wish:legacyadd:Blueprint
        /// Example: wish:legacyadd:dagger
        /// </summary>
        [WishCommand("legacyadd")]
        public static bool AddItemToPool(string blueprintName)
        {
            try
            {
                if (string.IsNullOrEmpty(blueprintName))
                {
                    Popup.Show("Usage: wish:legacyadd:Blueprint\nExample: wish:legacyadd:Dagger");
                    return false;
                }

                // Try to create the item
                var item = GameObjectFactory.Factory.CreateObject(blueprintName);
                if (item == null)
                {
                    Popup.Show($"Failed to create item from blueprint: {blueprintName}");
                    return false;
                }

                // Serialize to pool entry
                var entry = LegacyItemSerializer.SerializeItem(item, "WISHTEST", "Wish Command");
                if (entry == null)
                {
                    Popup.Show($"Failed to serialize item: {blueprintName}");
                    return false;
                }

                // Add to pool
                LegacyPoolManager.AddToPool(entry, null);

                Popup.Show($"Added {entry.DisplayName} to legacy pool!");
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error adding item to pool: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Removes a specific item from the legacy pool by index.
        /// Usage: wish:legacyremove:index
        /// Example: wish:legacyremove:0
        /// </summary>
        [WishCommand("legacyremove")]
        public static bool RemoveItemFromPool(string indexStr)
        {
            try
            {
                if (!int.TryParse(indexStr, out int index))
                {
                    Popup.Show("Usage: wish:legacyremove:index\nExample: wish:legacyremove:0");
                    return false;
                }

                var pool = LegacyPoolManager.LoadPool();
                if (index < 0 || index >= pool.Count)
                {
                    Popup.Show($"Invalid index {index}. Pool has {pool.Count} items (0-{pool.Count - 1}).");
                    return false;
                }

                var removed = pool[index];
                pool.RemoveAt(index);
                LegacyPoolManager.SavePool(pool);

                Popup.Show($"Removed {removed.DisplayName} from pool!");
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error removing item: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Grants a legacy item from the pool directly to the player.
        /// Usage: wish:legacygrant:index
        /// Example: wish:legacygrant:0
        /// </summary>
        [WishCommand("legacygrant")]
        public static bool GrantLegacyItem(string indexStr)
        {
            try
            {
                if (!int.TryParse(indexStr, out int index))
                {
                    Popup.Show("Usage: wish:legacygrant:index\nExample: wish:legacygrant:0");
                    return false;
                }

                var pool = LegacyPoolManager.LoadPool();
                if (index < 0 || index >= pool.Count)
                {
                    Popup.Show($"Invalid index {index}. Pool has {pool.Count} items (0-{pool.Count - 1}).");
                    return false;
                }

                var entry = pool[index];

                // Validate blueprint
                if (!GameObjectFactory.Factory.Blueprints.ContainsKey(entry.Blueprint))
                {
                    Popup.Show($"Blueprint {entry.Blueprint} no longer exists!");
                    return false;
                }

                // Deserialize item
                var item = LegacyItemSerializer.DeserializeItem(entry);
                if (item == null)
                {
                    Popup.Show($"Failed to deserialize {entry.DisplayName}");
                    return false;
                }

                // Apply legacy prefix and notes to match grant flow
                string prefixedName = null;
                var renderPart = item.GetPart<Render>();
                if (renderPart != null)
                {
                    var baseName = !string.IsNullOrEmpty(entry.BaseDisplayName)
                        ? entry.BaseDisplayName
                        : renderPart.DisplayName;

                    prefixedName = $"†{baseName}";
                    renderPart.DisplayName = prefixedName;
                    item.SetStringProperty("LegacyItemName", prefixedName);
                }

                var noteText = LegacyItemText.BuildLegacyNote(entry.CharacterName, entry.Source, entry.DeathReason);
                if (!string.IsNullOrEmpty(noteText))
                {
                    LegacyItemText.ApplyLegacyNote(item, entry.CharacterName, entry.Source, entry.DeathReason);
                }

                // Grant to player
                var player = The.Player;
                if (player == null)
                {
                    Popup.Show("No player found!");
                    return false;
                }

                player.Inventory.AddObject(item);

                var displayNameForPopup = prefixedName ?? item.DisplayName ?? entry.DisplayName;
                Popup.Show($"Granted {{M|{displayNameForPopup}}} to player!");
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error granting item: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Reseeds the legacy pool with default items.
        /// Usage: wish:legacyreseed
        /// </summary>
        [WishCommand("legacyreseed")]
        public static bool ReseedLegacyPool()
        {
            try
            {
                // Delete existing pool file
                if (System.IO.File.Exists(LegacyPoolManager.PoolFilePath))
                {
                    System.IO.File.Delete(LegacyPoolManager.PoolFilePath);
                }

                // Reinitialize with preseed items
                LegacyPoolManager.InitializePoolIfNeeded();

                Popup.Show("Legacy pool reseeded with 9 starter items!\n\n" +
                          "Includes test items with mod tier preservation.");
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error reseeding pool: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Shows the legacy pool file path.
        /// Usage: wish:legacypath
        /// </summary>
        [WishCommand("legacypath")]
        public static bool ShowLegacyPath()
        {
            try
            {
                var path = LegacyPoolManager.PoolFilePath;
                var exists = System.IO.File.Exists(path);

                Popup.Show($"Legacy pool path:\n{path}\n\nExists: {exists}");
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error getting path: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Simulates character death and serialization to pool using weighted item selection.
        /// Usage: wish:legacysimulatedeath
        /// </summary>
        [WishCommand("legacysimulatedeath")]
        public static bool SimulateDeath()
        {
            try
            {
                var player = The.Player;
                if (player == null)
                {
                    Popup.Show("No player found!");
                    return false;
                }

                // Select items using weighted selection (like real death)
                var selections = WeightedItemSelector.SelectLegacyItems(player);

                if (selections.Count == 0)
                {
                    Popup.Show("No eligible items found!\n\nMake sure you have equipped or inventory items.");
                    return false;
                }

                // Get character info
                var characterName = The.Game?.PlayerName ?? "Test Character";
                var deathReason = "died during wish command testing";

                // Serialize items
                LegacyPoolManager.LegacyItemEntry Serialize(WeightedItemSelector.SelectedItem sel)
                {
                    if (sel?.Item == null)
                        return null;
                    var source = string.IsNullOrEmpty(sel.Source) ? "INVENTORY" : sel.Source;
                    return LegacyItemSerializer.SerializeItem(sel.Item, source, characterName, deathReason);
                }

                var entries = selections
                    .Select(Serialize)
                    .Where(e => e != null)
                    .ToList();

                // Add to pool (LegacyPoolManager.AddToPool expects two args; reuse it for first two, then append)
                if (entries.Count == 1)
                {
                    LegacyPoolManager.AddToPool(entries[0], null);
                }
                else if (entries.Count >= 2)
                {
                    LegacyPoolManager.AddToPool(entries[0], entries[1]);
                    for (int i = 2; i < entries.Count; i++)
                    {
                        LegacyPoolManager.AddToPool(entries[i], null);
                    }
                }

                // Build result message
                var message = $"Simulated death for {{{{M|{characterName}}}}}\n\nAdded to legacy pool:\n";
                foreach (var entry in entries)
                {
                    message += $"- [{entry.Source}] {entry.DisplayName}\n";
                }
                message += "\nUse wish:legacypool to view pool.\nUse wish:legacydeserialize to test inheritance.";

                Popup.Show(message);
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error simulating death: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Deserializes an item from the pool and spawns it at player's feet.
        /// Shows a selection menu to choose which item to deserialize.
        /// Usage: wish:legacydeserialize
        /// </summary>
        [WishCommand("legacydeserialize")]
        public static bool DeserializeFromPool()
        {
            try
            {
                var player = The.Player;
                if (player == null)
                {
                    Popup.Show("No player found!");
                    return false;
                }

                var cell = player.CurrentCell;
                if (cell == null)
                {
                    Popup.Show("Player is not in a valid cell!");
                    return false;
                }

                var pool = LegacyPoolManager.LoadPool();
                if (pool.Count == 0)
                {
                    Popup.Show("Pool is empty!\n\nUse wish:legacyreseed or wish:legacysimulatedeath first.");
                    return false;
                }

                // Create selection menu from pool items
                var options = new List<string>();
                foreach (var poolEntry in pool)
                {
                    var optionText = poolEntry.DisplayName;
                    if (!string.IsNullOrEmpty(poolEntry.CharacterName))
                        optionText += $" ({{{{M|{poolEntry.CharacterName}}}}})";
                    options.Add(optionText);
                }

                // Show popup menu (returns selected index or -1 if cancelled)
                int selectedIndex = Popup.PickOption(
                    Title: "Select Item to Deserialize",
                    Intro: "Select an item from the legacy pool to spawn at your feet.",
                    Options: options,
                    AllowEscape: true
                );

                if (selectedIndex < 0 || selectedIndex >= pool.Count)
                {
                    Popup.Show("No item selected.");
                    return false;
                }

                var entry = pool[selectedIndex];
                if (entry == null)
                {
                    Popup.Show("Failed to find item in pool!");
                    return false;
                }

                // Deserialize item
                var item = LegacyItemSerializer.DeserializeItem(entry);
                if (item == null)
                {
                    Popup.Show($"Failed to deserialize: {entry.DisplayName}");
                    return false;
                }

                // Drop at player's feet
                cell.AddObject(item);

                // Build result message
                var message = $"Deserialized: {{{{C|{entry.DisplayName}}}}}\n";
                if (!string.IsNullOrEmpty(entry.CharacterName))
                    message += $"From: {{{{M|{entry.CharacterName}}}}}\n";
                if (!string.IsNullOrEmpty(entry.DeathReason))
                    message += $"Death: {entry.DeathReason}\n";
                if (!string.IsNullOrEmpty(entry.ModData))
                    message += $"\nMod Data: {{{{y|{entry.ModData}}}}}\n";
                message += "\n{{{{g|Item dropped at your feet.}}}}\nInspect to verify mods and properties.";

                Popup.Show(message);
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error deserializing item: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Validates all preseed and test item blueprints against the actual game blueprint database.
        /// This is a NO-MOCK test that runs in-game using real GameObjectFactory.Factory.Blueprints.
        /// Usage: wish:legacytestblueprints
        /// </summary>
        [WishCommand("legacytestblueprints")]
        public static bool TestBlueprints()
        {
            try
            {
                var errors = new List<string>();
                var warnings = new List<string>();

                // Test 1: Validate preseed blueprints
                var preseedMethod = typeof(LegacyPoolManager).GetMethod("GetPreseedItems",
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
                if (preseedMethod != null)
                {
                    var preseedItems = preseedMethod.Invoke(null, null) as List<LegacyPoolManager.LegacyItemEntry>;
                    if (preseedItems != null)
                    {
                        foreach (var item in preseedItems)
                        {
                            if (!GameObjectFactory.Factory.Blueprints.ContainsKey(item.Blueprint))
                            {
                                errors.Add($"PRESEED: \"{item.DisplayName}\" uses invalid blueprint \"{item.Blueprint}\"");
                            }
                        }
                    }
                }

                // Test 2: Validate test item blueprints (hardcoded from AddTestItems)
                var testBlueprints = new[]
                {
                    ("Chain Pistol", "chain pistol test item"),
                    ("Long Sword2", "iron long sword test item"),
                    ("Desert Rifle", "Issachar rifle multi-slot test item"),
                    ("FluxPhial", "expensive liquid container regression (phial with neutronflux)"),
                    ("FullCanteen", "mixed liquid container regression (honey + wine + water)"),
                    ("EmptyWaterskin", "mixed liquid waterskin regression (acid + oil + slime)")
                };

                // Regression: ensure we can create a mixed-liquid canteen (honey + wine + water)
                bool mixedCanteenOk = true;
                GameObject mixedCanteen = null;
                try
                {
                    mixedCanteen = GameObjectFactory.Factory.CreateObject("EmptyCanteen");
                    var liquid = mixedCanteen?.GetPart<LiquidVolume>();
                    if (liquid == null)
                    {
                        mixedCanteenOk = false;
                    }
                    else
                    {
                        mixedCanteenOk &= liquid.AddDrams("honey", 1);
                        mixedCanteenOk &= liquid.AddDrams("wine", 1);
                        mixedCanteenOk &= liquid.AddDrams("water", 1);
                    }
                }
                catch (Exception ex)
                {
                    mixedCanteenOk = false;
                    warnings.Add($"Mixed liquid canteen regression failed: {ex.Message}");
                }

                // Regression: ensure a waterskin can hold three different liquids simultaneously (different from canteen liquids)
                bool mixedWaterskinOk = true;
                GameObject mixedWaterskin = null;
                try
                {
                    mixedWaterskin = GameObjectFactory.Factory.CreateObject("EmptyWaterskin");
                    var liquid = mixedWaterskin?.GetPart<LiquidVolume>();
                    if (liquid == null)
                    {
                        mixedWaterskinOk = false;
                    }
                    else
                    {
                        mixedWaterskinOk &= liquid.AddDrams("acid", 1);
                        mixedWaterskinOk &= liquid.AddDrams("oil", 1);
                        mixedWaterskinOk &= liquid.AddDrams("slime", 1);
                    }
                }
                catch (Exception ex)
                {
                    mixedWaterskinOk = false;
                    warnings.Add($"Mixed liquid waterskin regression failed: {ex.Message}");
                }

                foreach (var (blueprint, description) in testBlueprints)
                {
                    if (!GameObjectFactory.Factory.Blueprints.ContainsKey(blueprint))
                    {
                        errors.Add($"TEST ITEM: {description} uses invalid blueprint \"{blueprint}\"");
                    }
                }

                // Test 3: Check for common mistakes (display names used as blueprints)
                var displayNameMistakes = new Dictionary<string, string>
                {
                    { "{{b|carbide}} dagger", "Dagger3" },
                    { "iron long sword", "Long Sword2" },
                    { "chain pistol", "Chain Pistol" },
                    { "carbide dagger", "Dagger3" }
                };

                foreach (var (displayName, correctBlueprint) in displayNameMistakes)
                {
                    if (GameObjectFactory.Factory.Blueprints.ContainsKey(displayName))
                    {
                        warnings.Add($"WARNING: \"{displayName}\" exists as a blueprint (expected it to be display-only)");
                    }
                }

                // Build result message
                var message = "{{C|Blueprint Validation Test}}\n\n";

                if (errors.Count > 0)
                {
                    LegacyItemOptions.LogError("LegacyItems: Blueprint validation failed:");
                    foreach (var error in errors)
                    {
                        LegacyItemOptions.LogError($"  {error}");
                    }
                }

                if (errors.Count == 0)
                {
                    message += "{{g|PASS: All blueprints valid!}}\n\n";
                    message += "Preseed items: {{g|PASS}}\n";
                    message += "Test items: {{g|PASS}}\n";
                }
                else
                {
                    message += "{{R|FAIL: ERRORS FOUND:}}\n\n";
                    foreach (var error in errors)
                    {
                        message += $"{{R|- {error}}}\n";
                    }
                    message += "\n{{y|Fix these in LegacyPoolManager.cs or LegacyItemWishCommands.cs}}";
                }

                if (warnings.Count > 0)
                {
                    message += "\n\n{{y|Warnings:}}\n";
                    foreach (var warning in warnings)
                    {
                        message += $"{{y|- {warning}}}\n";
                    }
                }
                else if (mixedCanteenOk)
                {
                    message += "\n{{g|Mixed liquid canteen regression: PASS (honey + wine + water)}}";
                    // Drop the mixed canteen for manual inspection
                    var player = The.Player;
                    if (player?.CurrentCell != null && mixedCanteen != null)
                    {
                        player.CurrentCell.AddObject(mixedCanteen);
                        message += "\n{{g|Spawned mixed canteen at your feet for inspection.}}";
                        LegacyItemOptions.LogDebug("LegacyItems: Spawned mixed canteen (honey+wine+water) at player's feet");
                    }
                }
                else
                {
                    message += "\n{{y|Mixed liquid canteen regression: FAILED}}";
                }

                if (mixedWaterskinOk)
                {
                    message += "\n{{g|Mixed liquid waterskin regression: PASS (acid + oil + slime)}}";
                    var player = The.Player;
                    if (player?.CurrentCell != null && mixedWaterskin != null)
                    {
                        player.CurrentCell.AddObject(mixedWaterskin);
                        message += "\n{{g|Spawned mixed waterskin at your feet for inspection.}}";
                        LegacyItemOptions.LogDebug("LegacyItems: Spawned mixed waterskin (honey+wine+water) at player's feet");
                    }
                }
                else
                {
                    message += "\n{{y|Mixed liquid waterskin regression: FAILED}}";
                }

                Popup.Show(message);
                return errors.Count == 0;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error testing blueprints: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Tests serialization round-trip for all player inventory items.
        /// Serializes each item, deserializes it, spawns it on ground, re-serializes it,
        /// and compares all fields using reflection to verify nothing changed.
        /// Usage: wish:legacytest
        /// </summary>
        [WishCommand("legacytest")]
        public static bool TestInventoryRoundTrip()
        {
            try
            {
                // Get player
                var player = The.Player;
                if (player == null)
                {
                    Popup.Show("No player found!");
                    return false;
                }

                var cell = player.CurrentCell;
                if (cell == null)
                {
                    Popup.Show("Player is not in a valid cell!");
                    return false;
                }

                // Get all items
                var equippedItems = WeightedItemSelector.GetEquippedItems(player) ?? new List<GameObject>();
                var inventoryItems = WeightedItemSelector.GetInventoryItems(player) ?? new List<GameObject>();
                var allItems = new List<GameObject>();
                allItems.AddRange(equippedItems);
                allItems.AddRange(inventoryItems);

                int stackItemCount = 0;
                int stackFailureCount = 0;

                if (allItems.Count == 0)
                {
                    Popup.Show("No items found in inventory!");
                    return true;
                }

                // Log start
                LegacyItemOptions.LogDebug("[TEST] ================================================");
                LegacyItemOptions.LogDebug("[TEST] Starting inventory round-trip test");
                LegacyItemOptions.LogDebug($"[TEST] Found {equippedItems.Count} equipped items, {inventoryItems.Count} inventory items");
                LegacyItemOptions.LogDebug($"[TEST] Testing {allItems.Count} items total");

                // Track results
                int passCount = 0;
                int failCount = 0;
                var failedItems = new List<(string itemName, List<string> differences)>();

                // Test each item
                for (int i = 0; i < allItems.Count; i++)
                {
                    var item = allItems[i];
                    if (item == null)
                        continue;

                    int stackCount = 1;
                    if (item.HasPart("Stacker"))
                    {
                        var stacker = item.GetPart<Stacker>();
                        if (stacker != null)
                            stackCount = Math.Max(1, stacker.Number);
                    }

                    bool isStacked = stackCount > 1;
                    if (isStacked)
                        stackItemCount++;

                    try
                    {
                        LegacyItemOptions.LogDebug("[TEST] ------------------------------------------------");
                        LegacyItemOptions.LogDebug($"[TEST] Item {i + 1}/{allItems.Count}: {item.DisplayName} (Blueprint: {item.Blueprint}, Stack: x{stackCount})");

                        // Serialize original
                        LegacyItemOptions.LogDebug("[TEST] Serializing original...");
                        var entry1 = LegacyItemSerializer.SerializeItem(item, "TEST", "RoundTripTest");
                        if (entry1 == null)
                        {
                            LegacyItemOptions.LogError($"[TEST] ERROR: Serialization returned null for {item.DisplayName}");
                            failCount++;
                            failedItems.Add(($"{item.DisplayName} (stack x{stackCount})", new List<string> { "Serialization failed" }));
                            continue;
                        }

                        // Log all entry1 fields using reflection
                        LogEntryFields(entry1, "Entry1");

                        // Deserialize
                        LegacyItemOptions.LogDebug("[TEST] Deserializing...");
                        var newItem = LegacyItemSerializer.DeserializeItem(entry1);
                        if (newItem == null)
                        {
                            LegacyItemOptions.LogError("[TEST] ERROR: Deserialization returned null");
                            failCount++;
                            failedItems.Add(($"{item.DisplayName} (stack x{stackCount})", new List<string> { "Deserialization failed" }));
                            continue;
                        }

                        LegacyItemOptions.LogDebug($"[TEST]   Created new item: {newItem.DisplayName}");

                        // Apply legacy notes for comparison/visual inspection
                        LegacyItemText.ApplyLegacyNote(newItem, entry1.CharacterName, entry1.Source, entry1.DeathReason);

                        // Spawn on ground
                        LegacyItemOptions.LogDebug("[TEST] Spawning deserialized item on ground at player location...");
                        cell.AddObject(newItem);

                        // Re-serialize
                        LegacyItemOptions.LogDebug("[TEST] Re-serializing...");
                        var entry2 = LegacyItemSerializer.SerializeItem(newItem, "TEST", "RoundTripTest");
                        if (entry2 == null)
                        {
                            LegacyItemOptions.LogError("[TEST] ERROR: Re-serialization returned null");
                            failCount++;
                            failedItems.Add(($"{item.DisplayName} (stack x{stackCount})", new List<string> { "Re-serialization failed" }));
                            continue;
                        }

                        // Log all entry2 fields using reflection
                        LogEntryFields(entry2, "Entry2");

                        // Compare entries
                        LegacyItemOptions.LogDebug("[TEST] Comparing entries...");
                        var differences = CompareEntries(entry1, entry2);

                        if (differences.Count == 0)
                        {
                            LegacyItemOptions.LogDebug("[TEST]   PASS: Item unchanged");
                            passCount++;
                        }
                        else
                        {
                            LegacyItemOptions.LogError($"[TEST] FAIL: Differences found in fields: {string.Join(", ", differences)}");
                            failCount++;
                            failedItems.Add(($"{item.DisplayName} (stack x{stackCount})", differences));

                            if (isStacked && differences.Any(d => d.StartsWith("StackSize", StringComparison.OrdinalIgnoreCase)))
                            {
                                stackFailureCount++;
                                LegacyItemOptions.LogError("[TEST]   Stack-specific regression detected (StackSize mismatch)");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LegacyItemOptions.LogError($"[TEST] ERROR testing item {i + 1}: {ex.Message}");
                        LegacyItemOptions.LogError($"[TEST] Stack trace: {ex.StackTrace}");
                        failCount++;
                        failedItems.Add(($"{item.DisplayName} (stack x{stackCount})", new List<string> { $"Exception: {ex.Message}" }));
                    }
                }

                // Log summary
                LegacyItemOptions.LogDebug("[TEST] ================================================");
                LegacyItemOptions.LogDebug("[TEST] Round-trip test complete");
                LegacyItemOptions.LogDebug($"[TEST] Results: {passCount}/{allItems.Count} items PASSED");
                LegacyItemOptions.LogDebug($"[TEST] {failCount} items FAILED");
                LegacyItemOptions.LogDebug($"[TEST] Stack coverage: {stackItemCount} stacked items tested, {stackFailureCount} stack-specific failures");
                LegacyItemOptions.LogDebug("[TEST] All deserialized items spawned on ground at player location");

                // Build popup summary
                var message = "{{C|Inventory Round-Trip Test}}\n\n";
                message += $"Total items: {allItems.Count}\n";
                message += $"{{g|PASS}}: {passCount}\n";
                message += $"{{R|FAIL}}: {failCount}\n";
                if (stackItemCount > 0)
                {
                    int stackPass = stackItemCount - stackFailureCount;
                    message += $"Stacked items tested: {stackItemCount} (pass {stackPass}, fail {stackFailureCount})\n";
                }

                if (failedItems.Count > 0)
                {
                    message += "\n{{y|Failed items:}}\n";
                    foreach (var (itemName, diffs) in failedItems)
                    {
                        message += $"- {itemName}: {string.Join(", ", diffs)}\n";
                    }
                }

                message += "\n{{y|Note: Enable debug logging to see detailed logs in Player.log}}\n";
                message += "{{y|All deserialized items have been spawned on the ground.}}";

                Popup.Show(message);
                return true;
            }
            catch (Exception ex)
            {
                Popup.Show($"Error in round-trip test: {ex.Message}");
                LegacyItemOptions.LogError($"Round-trip test failed: {ex.Message}");
                LegacyItemOptions.LogError($"Stack trace: {ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Logs all fields of a LegacyItemEntry using reflection.
        /// </summary>
        private static void LogEntryFields(LegacyPoolManager.LegacyItemEntry entry, string prefix)
        {
            var entryType = typeof(LegacyPoolManager.LegacyItemEntry);
            var properties = entryType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var value = prop.GetValue(entry);
                var valueStr = value == null ? "NULL" : value.ToString();
                LegacyItemOptions.LogDebug($"[TEST]   {prefix}.{prop.Name} = {valueStr}");
            }
        }

        /// <summary>
        /// Compares two LegacyItemEntry objects using reflection.
        /// Returns a list of property names that differ.
        /// Skips: ID (always regenerated), Source/CharacterName/DeathReason (test context).
        /// </summary>
        private static List<string> CompareEntries(
            LegacyPoolManager.LegacyItemEntry entry1,
            LegacyPoolManager.LegacyItemEntry entry2)
        {
            var differences = new List<string>();

            if (entry1 == null || entry2 == null)
            {
                if (entry1 == null && entry2 != null)
                    differences.Add("entry1 is null");
                if (entry2 == null && entry1 != null)
                    differences.Add("entry2 is null");
                return differences;
            }

            var entryType = typeof(LegacyPoolManager.LegacyItemEntry);
            var properties = entryType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            // Properties to skip (context fields that are expected to differ or be ignored)
            var skipProperties = new HashSet<string> { "ID", "Source", "CharacterName", "DeathReason" };

            foreach (var prop in properties)
            {
                if (skipProperties.Contains(prop.Name))
                    continue;

                var value1 = prop.GetValue(entry1);
                var value2 = prop.GetValue(entry2);

                // Use exact equality - null and "" are DIFFERENT
                bool areEqual;
                if (value1 == null && value2 == null)
                {
                    areEqual = true;
                }
                else if (value1 == null || value2 == null)
                {
                    areEqual = false;
                }
                else
                {
                    areEqual = value1.Equals(value2);
                }

                if (!areEqual)
                {
                    var v1Str = value1 == null ? "NULL" : $"\"{value1}\"";
                    var v2Str = value2 == null ? "NULL" : $"\"{value2}\"";
                    differences.Add($"{prop.Name}({v1Str}→{v2Str})");
                }
            }

            return differences;
        }
    }
}
