using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using XRL;
using XRL.CharacterBuilds;
using XRL.CharacterBuilds.Qud;
using XRL.UI;
using XRL.World;
using XRL.World.Parts;

namespace LegacyItems
{
    /// <summary>
    /// Data class for storing legacy item selection in character builder.
    /// </summary>
    [Serializable]
    [JsonObject(MemberSerialization.Fields)]
    public class LegacyItemModuleData : AbstractEmbarkBuilderModuleData
    {
        /// <summary>
        /// The selected legacy item ID, or null if player refused all.
        /// </summary>
        public string SelectedItemId;

        public LegacyItemModuleData() { }

        public LegacyItemModuleData(string selectedItemId)
        {
            SelectedItemId = selectedItemId;
        }
    }

    /// <summary>
    /// Embark builder module for legacy item selection during character creation.
    /// Inserts a selection step after game mode, before genotype selection.
    /// Uses handleBootEvent with BOOTEVENT_GAMESTARTING to grant items.
    /// </summary>
    public class LegacyItemEmbarkModule : EmbarkBuilderModule<LegacyItemModuleData>
    {
        private List<LegacyPoolManager.LegacyItemEntry> _pool;

        /// <summary>
        /// Gets the current legacy item pool.
        /// </summary>
        public List<LegacyPoolManager.LegacyItemEntry> Pool
        {
            get
            {
                if (_pool == null)
                {
                    // Process any pending items from a previous character that retired
                    // This must happen BEFORE loading the pool so newly committed items are included
                    PendingLegacyItems.ProcessPendingItems(null);

                    // Ensure pool is initialized with preseed items if it doesn't exist
                    LegacyPoolManager.InitializePoolIfNeeded();
                    _pool = LegacyPoolManager.LoadPool();
                }
                return _pool;
            }
        }

        /// <summary>
        /// Do not include legacy item selection in build codes.
        /// </summary>
        public override bool IncludeInBuildCodes() => false;

        /// <summary>
        /// Module is enabled when the pool has items or AutoRefuseEmpty is disabled.
        /// </summary>
        public override bool shouldBeEnabled()
        {
            // If pool is empty and AutoRefuseEmpty is set, skip the module
            if (Pool.Count == 0 && LegacyItemOptions.AutoRefuseEmpty)
                return false;

            return true;
        }

        /// <summary>
        /// Module is always editable when enabled.
        /// </summary>
        public override bool shouldBeEditable() => true;

        /// <summary>
        /// Insert the legacy item window after game mode selection, before genotype.
        /// </summary>
        public override void assembleWindowDescriptors(List<EmbarkBuilderModuleWindowDescriptor> windows)
        {
            // Find the index of the CharType (game mode) window
            int charTypeIndex = windows.FindIndex(w => w.viewID == "Chargen/CharType");
            if (charTypeIndex >= 0 && this.windows.ContainsKey("Chargen/LegacyItems"))
            {
                // Insert legacy item window after CharType
                windows.Insert(charTypeIndex + 1, this.windows["Chargen/LegacyItems"]);
            }
        }

        /// <summary>
        /// Called when the module is enabled - reload pool.
        /// </summary>
        public override void OnEnabled()
        {
            LegacyItemOptions.LogDebug("LegacyItems: EmbarkModule.OnEnabled called");
            _pool = null; // Force reload (ProcessPendingItems called in Pool getter)
        }

        /// <summary>
        /// Called when the module is disabled - clear pool.
        /// </summary>
        public override void OnDisabled()
        {
            _pool = null;
        }

        /// <summary>
        /// Handle random selection - pick a random item from pool.
        /// </summary>
        public override void RandomSelection()
        {
            if (Pool.Count > 0)
            {
                var randomItem = Pool[XRL.Rules.Stat.Random(0, Pool.Count - 1)];
                setData(new LegacyItemModuleData(randomItem.ID));
            }
            else
            {
                RefuseAll();
            }
        }

        /// <summary>
        /// Initialize from seed - for quickstart, auto-select a random item.
        /// </summary>
        public override void InitFromSeed(string seed)
        {
            LegacyItemOptions.LogDebug($"LegacyItems: InitFromSeed called with seed='{seed}', Pool.Count={Pool.Count}");
            if (Pool.Count > 0)
            {
                // Quickstart: auto-select random item
                var randomItem = Pool[XRL.Rules.Stat.Random(0, Pool.Count - 1)];
                setData(new LegacyItemModuleData(randomItem.ID));
                LegacyItemOptions.LogDebug($"LegacyItems: InitFromSeed auto-selected {randomItem.DisplayName}");
            }
            else
            {
                // Empty pool - refuse all (no item selected)
                RefuseAll();
            }
        }

        /// <summary>
        /// Initialize the module.
        /// </summary>
        public override void Init()
        {
            LegacyItemOptions.LogDebug("LegacyItems: Init called");
            base.Init();
        }

        /// <summary>
        /// Data is always valid - this module is optional, so null data is fine.
        /// </summary>
        public override bool IsDataValid()
        {
            LegacyItemOptions.LogDebug($"LegacyItems: IsDataValid called, data={data}, SelectedItemId={data?.SelectedItemId}");
            // Always valid - selection is optional (user can refuse all)
            return true;
        }

        /// <summary>
        /// Handle boot events - grant selected item when game starts.
        /// Selection is done via the window UI, not during boot.
        /// </summary>
        public override object handleBootEvent(string id, XRLGame game, EmbarkInfo info, object element = null)
        {
            LegacyItemOptions.LogDebug($"LegacyItems: handleBootEvent called with id='{id}'");

            // Grant item when game is starting (first tick)
            if (id == QudGameBootModule.BOOTEVENT_GAMESTARTING)
            {
                var selectedItemId = data?.SelectedItemId;
                LegacyItemOptions.LogDebug($"LegacyItems: BOOTEVENT_GAMESTARTING - data.SelectedItemId='{selectedItemId}'");

                if (!string.IsNullOrEmpty(selectedItemId))
                {
                    GrantLegacyItem(game, selectedItemId);
                }
                else
                {
                    LegacyItemOptions.LogDebug("LegacyItems: No item selected (SelectedItemId is null or empty)");
                }
            }

            return base.handleBootEvent(id, game, info, element);
        }

        /// <summary>
        /// Select an item from the pool.
        /// </summary>
        public void SelectItem(string itemId)
        {
            setData(new LegacyItemModuleData(itemId));
        }

        /// <summary>
        /// Refuse all legacy items.
        /// </summary>
        public void RefuseAll()
        {
            LegacyItemOptions.LogDebug("LegacyItems: Player refused all relics");
            setData(new LegacyItemModuleData(null));
        }

        /// <summary>
        /// Permanently removes an item from the pool ("consign to oblivion").
        /// Returns true if the item was found and removed.
        /// </summary>
        public bool ConsignToOblivion(string itemId)
        {
            if (string.IsNullOrEmpty(itemId))
                return false;

            var pool = LegacyPoolManager.LoadPool();
            var entry = pool.Find(e => e.ID == itemId);
            if (entry == null)
            {
                LegacyItemOptions.LogDebug($"LegacyItems: ConsignToOblivion - item '{itemId}' not found in pool");
                return false;
            }

            pool.RemoveAll(e => e.ID == itemId);
            LegacyPoolManager.SavePool(pool);
            LegacyItemOptions.LogDebug($"LegacyItems: ConsignToOblivion - removed '{entry.DisplayName}' from pool");

            // Clear cached pool so it reloads
            _pool = null;

            // If this was the selected item, clear selection
            if (data?.SelectedItemId == itemId)
            {
                setData(new LegacyItemModuleData(null));
            }

            return true;
        }

        /// <summary>
        /// Reloads the pool from disk. Called after items are removed.
        /// </summary>
        public void ReloadPool()
        {
            _pool = null;
        }

        private void GrantLegacyItem(XRLGame game, string selectedItemId)
        {
            LegacyItemOptions.LogDebug($"LegacyItems: GrantLegacyItem called with selectedItemId='{selectedItemId}'");

            try
            {
                var pool = LegacyPoolManager.LoadPool();
                LegacyItemOptions.LogDebug($"LegacyItems: Loaded pool with {pool.Count} items");

                // Log all pool items for debugging
                for (int i = 0; i < pool.Count; i++)
                {
                    var e = pool[i];
                    LegacyItemOptions.LogDebug($"LegacyItems: Pool[{i}] ID='{e.ID}' Blueprint='{e.Blueprint}' DisplayName='{e.DisplayName}' Mods='{e.Mods}'");
                }

                var selectedEntry = pool.Find(e => e.ID == selectedItemId);

                if (selectedEntry == null)
                {
                    LegacyItemOptions.LogError($"LegacyItems: Selected item '{selectedItemId}' not found in pool!");
                    return;
                }

                LegacyItemOptions.LogDebug($"LegacyItems: Found entry - Blueprint='{selectedEntry.Blueprint}' DisplayName='{selectedEntry.DisplayName}' Mods='{selectedEntry.Mods}' IsArtifact={selectedEntry.IsArtifact}");

                // Validate blueprint exists
                if (!GameObjectFactory.Factory.Blueprints.ContainsKey(selectedEntry.Blueprint))
                {
                    LegacyItemOptions.LogError($"LegacyItems: Blueprint '{selectedEntry.Blueprint}' no longer exists in game!");
                    pool.RemoveAll(e => e.ID == selectedItemId);
                    LegacyPoolManager.SavePool(pool);
                    return;
                }

                LegacyItemOptions.LogDebug($"LegacyItems: Blueprint '{selectedEntry.Blueprint}' validated, calling DeserializeItem...");

                // Deserialize and grant item
                var item = LegacyItemSerializer.DeserializeItem(selectedEntry);
                if (item == null)
                {
                    LegacyItemOptions.LogError($"LegacyItems: DeserializeItem returned null for '{selectedEntry.DisplayName}'");
                    return;
                }

                LegacyItemOptions.LogDebug($"LegacyItems: DeserializeItem returned - Blueprint='{item.Blueprint}' DisplayName='{item.DisplayName}' ShortDisplayName='{item.ShortDisplayName}'");

                // Apply dagger prefix
                ApplyLegacyPrefix(item, selectedEntry);
                LegacyItemOptions.LogDebug($"LegacyItems: After ApplyLegacyPrefix - DisplayName='{item.DisplayName}'");

                // Get player object
                var player = The.Player;
                if (player?.Inventory != null)
                {
                    player.Inventory.AddObject(item);
                    LegacyItemOptions.LogDebug($"LegacyItems: Successfully added to player inventory - Final DisplayName='{item.DisplayName}'");
                }
                else
                {
                    LegacyItemOptions.LogError($"LegacyItems: Player or Inventory is null!");
                }

                // Remove from pool based on game mode
                if (ShouldRemoveFromPool(game))
                {
                    pool.RemoveAll(e => e.ID == selectedItemId);
                    LegacyPoolManager.SavePool(pool);
                    LegacyItemOptions.LogDebug($"LegacyItems: Removed '{selectedEntry.DisplayName}' from pool");
                }
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"LegacyItems: Error granting item - {ex.Message}\n{ex.StackTrace}");
            }
        }

        private void ApplyLegacyPrefix(GameObject item, LegacyPoolManager.LegacyItemEntry entry)
        {
            if (item == null || entry == null) return;

            var renderPart = item.GetPart<Render>();
            if (renderPart == null) return;

            // Use the saved BaseDisplayName (custom name without mod prefixes).
            // This preserves custom item names (artifacts, material names like "carbide", etc.)
            // while letting mods add their prefixes dynamically.
            // Fall back to the fresh item's Render.DisplayName if BaseDisplayName wasn't saved.
            string baseName = !string.IsNullOrEmpty(entry.BaseDisplayName)
                ? entry.BaseDisplayName
                : renderPart.DisplayName;

            // Always prefix with dagger without adding ancestor name to the display name itself
            string prefixedName = $"†{baseName}";

            // Set on Render.DisplayName directly
            // Mods like ModJewelEncrusted will add their prefix dynamically to this base name
            renderPart.DisplayName = prefixedName;
            LegacyItemOptions.LogDebug($"LegacyItems: ApplyLegacyPrefix - Set Render.DisplayName to '{prefixedName}'");

            // Also set as a property for potential other uses
            item.SetStringProperty("LegacyItemName", prefixedName);

            // Attach ancestor context to item notes instead of the display name
            LegacyItemText.ApplyLegacyNote(item, entry.CharacterName, entry.Source, entry.DeathReason);
        }

        private bool ShouldRemoveFromPool(XRLGame game)
        {
            if (game == null) return true;

            var gameMode = GetGameMode(game);
            switch (gameMode)
            {
                case "Roleplay":
                    return !game.GetBooleanGameState("RoleplayCheckpoints");
                default:
                    return true;
            }
        }

        private string GetGameMode(XRLGame game)
        {
            var gameMode = game?.GetStringGameState("GameMode");
            if (!string.IsNullOrEmpty(gameMode)) return gameMode;
            if (game?.GetBooleanGameState("Roleplay") == true) return "Roleplay";
            return "Classic";
        }
    }
}
