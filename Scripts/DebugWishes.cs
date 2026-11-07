using System;
using System.Linq;
using System.Text;
using XRL;
using XRL.Core;
using XRL.UI;
using XRL.Wish;
using XRL.World;
using XRL.World.Parts;

namespace LegacyItems
{
    /// <summary>
    /// Debug wish commands for testing Legacy Items functionality.
    /// These commands help verify behavior without needing to die/restart.
    /// </summary>
    [HasWishCommand]
    public class DebugWishes
    {
        #region Pool Management Wishes

        [WishCommand("legacypool", null)]
        public static bool WishLegacyPool()
        {
            try
            {
                var pool = LegacyPoolManager.LoadPool();

                if (pool.Count == 0)
                {
                    Popup.Show("{{rules|Legacy item pool is empty.}}");
                    return true;
                }

                var summary = LegacyItemSelectionUI.GetPoolSummary(pool);
                Popup.ShowBlock(summary);
                LegacyItemOptions.LogDebug("LegacyItems: Pool summary displayed");
                return true;
            }
            catch (Exception ex)
            {
                Popup.ShowFail("Error loading pool: " + ex.Message);
                LegacyItemOptions.LogDebug("LegacyItems: Error in legacypool - " + ex);
                return false;
            }
        }

        [WishCommand("legacystats", null)]
        public static bool WishLegacyStats()
        {
            try
            {
                var pool = LegacyPoolManager.LoadPool();

                var sb = new StringBuilder();
                sb.AppendLine("{{W|Legacy Items Statistics}}");
                sb.AppendLine();
                sb.AppendLine($"Total items in pool: {{C|{pool.Count}/10}}");
                sb.AppendLine();

                if (pool.Count > 0)
                {
                    int equipped = pool.Count(e => e.Source == "EQUIPPED");
                    int inventory = pool.Count(e => e.Source == "INVENTORY");
                    int preseed = pool.Count(e => e.CharacterName == "Preseed");
                    int artifacts = pool.Count(e => e.IsArtifact);

                    sb.AppendLine("{{y|Breakdown:}}");
                    sb.AppendLine($"  Equipped: {equipped}");
                    sb.AppendLine($"  Inventory: {inventory}");
                    sb.AppendLine($"  Preseed items: {preseed}");
                    sb.AppendLine($"  Artifacts: {artifacts}");
                    sb.AppendLine();

                    // Show oldest and newest
                    sb.AppendLine($"Oldest: {{K|{pool[0].DisplayName}}} from {pool[0].CharacterName}");
                    sb.AppendLine($"Newest: {{G|{pool[pool.Count - 1].DisplayName}}} from {pool[pool.Count - 1].CharacterName}");
                }

                Popup.ShowBlock(sb.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Popup.ShowFail("Error getting stats: " + ex.Message);
                LegacyItemOptions.LogDebug("LegacyItems: Error in legacystats - " + ex);
                return false;
            }
        }

        [WishCommand("clearlegacy", null)]
        public static bool WishClearLegacy()
        {
            try
            {
                if (Popup.ShowYesNo("Clear all legacy items from the pool?") == DialogResult.Yes)
                {
                    LegacyPoolManager.SavePool(new System.Collections.Generic.List<LegacyPoolManager.LegacyItemEntry>());
                    Popup.Show("{{rules|Legacy pool cleared.}}");
                    LegacyItemOptions.LogDebug("LegacyItems: Pool cleared via wish");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Popup.ShowFail("Error clearing pool: " + ex.Message);
                LegacyItemOptions.LogDebug("LegacyItems: Error in clearlegacy - " + ex);
                return false;
            }
        }

        [WishCommand("resetlegacy", null)]
        public static bool WishResetLegacy()
        {
            try
            {
                if (Popup.ShowYesNo("Reset pool to 8 preseed items?") == DialogResult.Yes)
                {
                    // Delete existing pool file to trigger preseed recreation
                    if (System.IO.File.Exists(LegacyPoolManager.PoolFilePath))
                    {
                        System.IO.File.Delete(LegacyPoolManager.PoolFilePath);
                    }

                    LegacyPoolManager.InitializePoolIfNeeded();
                    Popup.Show("{{rules|Legacy pool reset to preseed items.}}");
                    LegacyItemOptions.LogDebug("LegacyItems: Pool reset to preseed");
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Popup.ShowFail("Error resetting pool: " + ex.Message);
                LegacyItemOptions.LogDebug("LegacyItems: Error in resetlegacy - " + ex);
                return false;
            }
        }

        #endregion

        #region Item Addition Wishes

        [WishCommand("addlegacy", null)]
        public static bool WishAddLegacy(string args)
        {
            try
            {
                var player = The.Player;
                if (player == null)
                {
                    Popup.ShowFail("No player character found");
                    return false;
                }

                // Parse arguments: equipped, inventory, or both
                string source = args?.ToLower().Trim() ?? "both";

                LegacyPoolManager.LegacyItemEntry equippedEntry = null;
                LegacyPoolManager.LegacyItemEntry inventoryEntry = null;

                if (source == "equipped" || source == "both")
                {
                    var equipped = WeightedItemSelector.GetEquippedItems(player);
                    var selectedEquipped = WeightedItemSelector.SelectSingleWeightedItem(equipped, "EQUIPPED", isEquipped: true);

                    if (selectedEquipped != null)
                    {
                        equippedEntry = LegacyItemSerializer.SerializeItem(
                            selectedEquipped,
                            "EQUIPPED",
                            "DEBUG"
                        );

                        LegacyItemOptions.LogDebug($"LegacyItems: Added equipped item: {selectedEquipped.DisplayName}");
                    }
                }

                if (source == "inventory" || source == "both")
                {
                    var inventory = WeightedItemSelector.GetInventoryItems(player);
                    var selectedInventory = WeightedItemSelector.SelectSingleWeightedItem(inventory, "INVENTORY", isEquipped: false);

                    if (selectedInventory != null)
                    {
                        inventoryEntry = LegacyItemSerializer.SerializeItem(
                            selectedInventory,
                            "INVENTORY",
                            "DEBUG"
                        );

                        LegacyItemOptions.LogDebug($"LegacyItems: Added inventory item: {selectedInventory.DisplayName}");
                    }
                }

                if (equippedEntry == null && inventoryEntry == null)
                {
                    Popup.ShowFail("No eligible items found");
                    return false;
                }

                LegacyPoolManager.AddToPool(equippedEntry, inventoryEntry);

                var msg = new StringBuilder("{{rules|Added to legacy pool:}}\\n");
                if (equippedEntry != null)
                    msg.Append($"  {{W|{equippedEntry.DisplayName}}} (equipped)\\n");
                if (inventoryEntry != null)
                    msg.Append($"  {{C|{inventoryEntry.DisplayName}}} (inventory)");

                Popup.Show(msg.ToString());
                return true;
            }
            catch (Exception ex)
            {
                Popup.ShowFail("Error adding items: " + ex.Message);
                LegacyItemOptions.LogDebug("LegacyItems: Error in addlegacy - " + ex);
                return false;
            }
        }

        #endregion

        #region Item Testing Wishes

        [WishCommand("testserialize", null)]
        public static bool WishTestSerialize()
        {
            try
            {
                var player = The.Player;
                if (player == null || !player.HasPart("Body"))
                {
                    Popup.ShowFail("No player character found");
                    return false;
                }

                // Get any equipped item
                GameObject testItem = null;
                var equipped = WeightedItemSelector.GetEquippedItems(player);
                if (equipped.Count > 0)
                    testItem = equipped[0];

                if (testItem == null)
                {
                    Popup.ShowFail("No equipped items to serialize");
                    return false;
                }

                // Serialize the item
                var entry = LegacyItemSerializer.SerializeItem(testItem, "TEST", "TestChar");

                // Build detailed output
                var sb = new StringBuilder();
                sb.AppendLine("{{W|Item Serialization Test}}");
                sb.AppendLine();
                sb.AppendLine($"Original: {{C|{testItem.DisplayName}}}");
                sb.AppendLine();
                sb.AppendLine("{{y|Serialized Properties:}}");
                sb.AppendLine($"  Blueprint: {entry.Blueprint}");
                sb.AppendLine($"  Display Name: {entry.DisplayName}");
                sb.AppendLine($"  Tier: {entry.Tier}");
                sb.AppendLine($"  Tech Tier: {entry.TechTier}");
                sb.AppendLine($"  Mods: {(string.IsNullOrEmpty(entry.Mods) ? "(none)" : entry.Mods)}");
                sb.AppendLine($"  Artifact: {entry.IsArtifact}");
                sb.AppendLine($"  Stack Size: {(string.IsNullOrEmpty(entry.StackSize) ? "(none)" : entry.StackSize)}");
                sb.AppendLine($"  Charges: {(string.IsNullOrEmpty(entry.Charges) ? "(none)" : entry.Charges)}");
                sb.AppendLine($"  Liquid: {(string.IsNullOrEmpty(entry.LiquidType) ? "(none)" : entry.LiquidType + " (" + entry.LiquidDrams + " drams)")}");
                sb.AppendLine($"  Durability: {(string.IsNullOrEmpty(entry.Durability) ? "(none)" : entry.Durability)}");
                sb.AppendLine();
                sb.AppendLine("{{K|File Format:}}");
                sb.AppendLine(entry.ToFileFormat());

                Popup.ShowBlock(sb.ToString());
                LegacyItemOptions.LogDebug($"LegacyItems: Serialized {testItem.DisplayName}");
                LegacyItemOptions.LogDebug($"  Format: {entry.ToFileFormat()}");

                return true;
            }
            catch (Exception ex)
            {
                Popup.ShowFail("Error serializing item: " + ex.Message);
                LegacyItemOptions.LogDebug("LegacyItems: Error in testserialize - " + ex);
                return false;
            }
        }

        [WishCommand("testdeserialize", null)]
        public static bool WishTestDeserialize(string args)
        {
            try
            {
                var pool = LegacyPoolManager.LoadPool();

                if (pool.Count == 0)
                {
                    Popup.ShowFail("Pool is empty");
                    return false;
                }

                // Parse index argument (0-based)
                int index = 0;
                if (!string.IsNullOrEmpty(args))
                {
                    if (!int.TryParse(args, out index))
                    {
                        Popup.ShowFail("Invalid index. Usage: testdeserialize [0-" + (pool.Count - 1) + "]");
                        return false;
                    }
                }

                if (index < 0 || index >= pool.Count)
                {
                    Popup.ShowFail($"Index out of range. Pool has {pool.Count} items (0-{pool.Count - 1})");
                    return false;
                }

                var entry = pool[index];
                var item = LegacyItemSerializer.DeserializeItem(entry);

                if (item == null)
                {
                    Popup.ShowFail("Failed to deserialize item");
                    return false;
                }

                // Apply legacy prefix to test full granting flow
                var prefixedName = $"†{entry.DisplayName}†";
                item.SetStringProperty("LegacyItemName", prefixedName);
                item.SetStringProperty("CustomName", prefixedName);

                // Add to player inventory
                var player = The.Player;
                if (player != null && player.HasPart("Inventory"))
                {
                    player.ReceiveObject(item);
                    Popup.Show($"{{rules|Created and added:}} {{C|{item.DisplayName}}}");
                    LegacyItemOptions.LogDebug($"LegacyItems: Deserialized {entry.DisplayName} from pool index {index} with legacy marking");
                }
                else
                {
                    Popup.ShowFail("Cannot add item to inventory");
                }

                return true;
            }
            catch (Exception ex)
            {
                Popup.ShowFail("Error deserializing item: " + ex.Message);
                LegacyItemOptions.LogDebug("LegacyItems: Error in testdeserialize - " + ex);
                return false;
            }
        }

        [WishCommand("showweights", null)]
        public static bool WishShowWeights()
        {
            try
            {
                var player = The.Player;
                if (player == null)
                {
                    Popup.ShowFail("No player character found");
                    return false;
                }

                var sb = new StringBuilder();
                sb.AppendLine("{{W|Item Weight Calculation}}");
                sb.AppendLine();

                // Show equipped items
                var equipped = WeightedItemSelector.GetEquippedItems(player);
                if (equipped.Count > 0)
                {
                    sb.AppendLine("{{y|Equipped Items:}}");
                    foreach (var item in equipped)
                    {
                        if (WeightedItemSelector.IsItemEligible(item))
                        {
                            int weight = WeightedItemSelector.CalculateItemWeight(item);
                            int tier = item.GetTier();
                            int stackCount = 1;

                            // Get commerce value from Commerce part
                            int value = 0;
                            if (item.HasPart("Commerce"))
                            {
                                var commerce = item.GetPart<Commerce>();
                                if (commerce != null)
                                    value = (int)commerce.Value;
                            }

                            if (item.HasPart("Stacker"))
                            {
                                var stacker = item.GetPart<Stacker>();
                                if (stacker != null)
                                    stackCount = Math.Max(1, stacker.Number);
                            }

                            sb.AppendLine($"  {{C|{item.DisplayName}}}");
                            sb.AppendLine($"    Tier({tier}) + Value({value}/100) [Stack x{stackCount}] = Weight({{G|{weight}}})");
                        }
                        else
                        {
                            sb.AppendLine($"  {{K|{item.DisplayName}}} - INELIGIBLE");
                        }
                    }
                    sb.AppendLine();
                }

                // Show inventory items
                var inventory = WeightedItemSelector.GetInventoryItems(player);
                if (inventory.Count > 0)
                {
                    sb.AppendLine("{{y|Inventory Items:}}");
                    int shown = 0;
                    foreach (var item in inventory)
                    {
                        if (shown >= 20)
                        {
                            sb.AppendLine($"  ... and {inventory.Count - shown} more items");
                            break;
                        }

                        if (WeightedItemSelector.IsItemEligible(item))
                        {
                            int weight = WeightedItemSelector.CalculateItemWeight(item);
                            int tier = item.GetTier();
                            int stackCount = 1;

                            // Get commerce value from Commerce part
                            int value = 0;
                            if (item.HasPart("Commerce"))
                            {
                                var commerce = item.GetPart<Commerce>();
                                if (commerce != null)
                                    value = (int)commerce.Value;
                            }

                            if (item.HasPart("Stacker"))
                            {
                                var stacker = item.GetPart<Stacker>();
                                if (stacker != null)
                                    stackCount = Math.Max(1, stacker.Number);
                            }

                            sb.AppendLine($"  {{C|{item.DisplayName}}}");
                            sb.AppendLine($"    Tier({tier}) + Value({value}/100) [Stack x{stackCount}] = Weight({{G|{weight}}})");
                            shown++;
                        }
                    }
                }

                if (equipped.Count == 0 && inventory.Count == 0)
                {
                    sb.AppendLine("{{K|No items found}}");
                }

                LegacyItemOptions.LogDebug("LegacyItems: showweights output:\n" + sb.ToString());
                Popup.ShowBlock(sb.ToString());
                LegacyItemOptions.LogDebug("LegacyItems: Displayed weight calculations");
                return true;
            }
            catch (Exception ex)
            {
                Popup.ShowFail("Error calculating weights: " + ex.Message);
                LegacyItemOptions.LogDebug("LegacyItems: Error in showweights - " + ex);
                return false;
            }
        }

        #endregion

        #region Utility Wishes

        [WishCommand("legacypath", null)]
        public static bool WishLegacyPath()
        {
            try
            {
                string path = LegacyPoolManager.PoolFilePath;
                bool exists = System.IO.File.Exists(path);

                var sb = new StringBuilder();
                sb.AppendLine("{{W|Legacy Pool File Location}}");
                sb.AppendLine();
                sb.AppendLine($"{{y|Path:}}");
                sb.AppendLine(path);
                sb.AppendLine();
                sb.AppendLine($"{{y|Status:}} {(exists ? "{{G|EXISTS}}" : "{{K|NOT FOUND}}")}");

                if (exists)
                {
                    var info = new System.IO.FileInfo(path);
                    sb.AppendLine();
                    sb.AppendLine($"{{y|Size:}} {info.Length} bytes");
                    sb.AppendLine($"{{y|Modified:}} {info.LastWriteTime}");
                }

                Popup.ShowBlock(sb.ToString());
                LegacyItemOptions.LogDebug($"LegacyItems: Pool path: {path}");
                return true;
            }
            catch (Exception ex)
            {
                Popup.ShowFail("Error getting path: " + ex.Message);
                LegacyItemOptions.LogDebug("LegacyItems: Error in legacypath - " + ex);
                return false;
            }
        }

        #endregion
    }
}
