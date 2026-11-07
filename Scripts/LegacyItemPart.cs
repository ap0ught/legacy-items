using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Qud.API;
using XRL;
using XRL.Core;
using XRL.World;
using XRL.World.Parts;

namespace LegacyItems
{
    /// <summary>
    /// Manages pending legacy items that are collected on death but not yet confirmed.
    /// Uses GameID + Scoreboard checking to determine if a character was truly retired:
    /// 1. On BeforeDieEvent, items are saved to a "pending" file along with the Game's GameID
    /// 2. On any game interaction (OnGameLoaded, PlayerMutator), check the Scoreboard for that GameID
    ///    - If GameID found in Scoreboard: character retired → commit pending items to pool
    ///    - If GameID NOT in Scoreboard: character might restore from checkpoint → keep pending
    /// 3. If current GameID matches pending GameID: we're in the same game session → clear pending (checkpoint restore)
    /// </summary>
    public static class PendingLegacyItems
    {
        private static string PendingFilePath => Path.Combine(LegacyPoolManager.ModSaveDirectory, "pending_items.txt");
        private static string PendingGameIdPath => Path.Combine(LegacyPoolManager.ModSaveDirectory, "pending_game_id.txt");

        /// <summary>
        /// Saves items to the pending file along with the Game's GameID. Called on BeforeDieEvent.
        /// </summary>
        public static void SavePendingItems(List<LegacyPoolManager.LegacyItemEntry> entries, string gameId)
        {
            try
            {
                LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Saving {entries.Count} pending items with gameId='{gameId}'");

                // Ensure directory exists
                Directory.CreateDirectory(Path.GetDirectoryName(PendingFilePath));

                // Save items
                var lines = new List<string>();
                foreach (var entry in entries)
                {
                    if (entry != null)
                    {
                        lines.Add(entry.ToFileFormat());
                    }
                }
                File.WriteAllLines(PendingFilePath, lines);

                // Save GameID
                File.WriteAllText(PendingGameIdPath, gameId);

                LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Saved {lines.Count} pending items for gameId '{gameId}'");
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"LegacyItems: [PENDING] Error saving pending items - {ex.Message}");
            }
        }

        /// <summary>
        /// Gets the GameID from the pending file.
        /// </summary>
        public static string GetPendingGameId()
        {
            try
            {
                if (File.Exists(PendingGameIdPath))
                {
                    return File.ReadAllText(PendingGameIdPath).Trim();
                }
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"LegacyItems: [PENDING] Error reading pending GameID - {ex.Message}");
            }
            return null;
        }

        /// <summary>
        /// Clears the pending files.
        /// </summary>
        public static void ClearPendingItems()
        {
            try
            {
                bool hadItems = File.Exists(PendingFilePath);
                if (File.Exists(PendingFilePath))
                {
                    File.Delete(PendingFilePath);
                }
                if (File.Exists(PendingGameIdPath))
                {
                    File.Delete(PendingGameIdPath);
                }
                if (hadItems)
                {
                    LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Pending items cleared");
                }
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"LegacyItems: [PENDING] Error clearing pending items - {ex.Message}");
            }
        }

        /// <summary>
        /// Commits pending items to the main pool.
        /// </summary>
        private static void CommitPendingItems()
        {
            try
            {
                if (!File.Exists(PendingFilePath))
                {
                    return;
                }

                LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Committing pending items from previous death");

                var lines = File.ReadAllLines(PendingFilePath);
                var entries = new List<LegacyPoolManager.LegacyItemEntry>();

                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var entry = LegacyPoolManager.LegacyItemEntry.FromFileFormat(line);
                        if (entry != null)
                        {
                            entries.Add(entry);
                        }
                    }
                }

                if (entries.Count > 0)
                {
                    LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Adding {entries.Count} pending items to pool");
                    LegacyPoolManager.AddEntriesToPool(entries);
                    LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Successfully committed {entries.Count} items to pool");
                }

                // Clear the pending files after committing
                ClearPendingItems();
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"LegacyItems: [PENDING] Error committing pending items - {ex.Message}");
            }
        }

        /// <summary>
        /// Checks if there are pending items waiting to be processed.
        /// </summary>
        public static bool HasPendingItems()
        {
            return File.Exists(PendingFilePath);
        }

        /// <summary>
        /// Checks if a save file with the given GameID still exists.
        /// If no save exists, the character has been permanently retired.
        /// </summary>
        private static bool DoesSaveExist(string gameId)
        {
            try
            {
                if (string.IsNullOrEmpty(gameId))
                    return false;

                // GetSavedGameInfo() returns Task<List<SaveGameInfo>> (async); unwrap it synchronously.
                var saves = SavesAPI.GetSavedGameInfo().GetAwaiter().GetResult();
                if (saves == null)
                    return false;

                foreach (var save in saves)
                {
                    if (save.ID == gameId)
                    {
                        LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Save exists for GameID '{gameId}' - character still alive");
                        return true;
                    }
                }

                LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] No save found for GameID '{gameId}' - character has been retired");
                return false;
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"LegacyItems: [PENDING] Error checking saves - {ex.Message}");
                // On error, assume save exists (don't commit items incorrectly)
                return true;
            }
        }

        /// <summary>
        /// Processes pending items. Called on game interactions (OnGameLoaded, PlayerMutator).
        /// Checks if the pending GameID's save still exists to determine if character retired.
        /// </summary>
        public static void ProcessPendingItems(string currentGameId)
        {
            if (!HasPendingItems())
            {
                LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] ProcessPendingItems - no pending items");
                return;
            }

            var pendingGameId = GetPendingGameId();
            LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] ProcessPendingItems - currentGameId='{currentGameId}', pendingGameId='{pendingGameId}'");

            // If we're back in the same game (checkpoint restore), clear pending items
            if (!string.IsNullOrEmpty(currentGameId) && currentGameId == pendingGameId)
            {
                LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Same GameID - checkpoint restore detected, clearing pending items");
                ClearPendingItems();
                return;
            }

            // Check if the pending GameID's save still exists
            if (!DoesSaveExist(pendingGameId))
            {
                // Save no longer exists = character was retired = commit items
                LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Save for GameID '{pendingGameId}' no longer exists - committing items");
                CommitPendingItems();
            }
            else
            {
                // Save still exists - character might restore from checkpoint later
                LegacyItemOptions.LogDebug($"LegacyItems: [PENDING] Save for GameID '{pendingGameId}' still exists - keeping pending items");
            }
        }
    }

    /// <summary>
    /// Static class that handles the actual item collection logic.
    /// </summary>
    public static class LegacyItemCollector
    {
        /// <summary>
        /// Collects legacy items from the player character and saves them to pending.
        /// Called by LegacyItemPart.HandleEvent(BeforeDieEvent).
        /// Items are saved to a pending file, NOT directly to the pool.
        /// </summary>
        /// <param name="character">The player character object</param>
        /// <param name="characterName">The player's name</param>
        /// <param name="deathReason">The death message/reason</param>
        public static void CollectLegacyItemsToPending(GameObject character, string characterName, string deathReason)
        {
            // Use GameID to track which game/save these items came from
            var gameId = The.Game?.GameID ?? "unknown";
            LegacyItemOptions.LogDebug($"LegacyItems: CollectLegacyItemsToPending called, character={character?.DisplayName ?? "null"}, gameId='{gameId}', characterName='{characterName}', deathReason='{deathReason ?? "null"}'");

            // Check if this is a temporal fugue (precognition) death
            if (IsTemporalFugueDeath(character))
            {
                LegacyItemOptions.LogDebug("LegacyItems: Temporal fugue death detected, skipping item collection");
                return;
            }

            LegacyItemOptions.LogDebug($"LegacyItems: Using character name: '{characterName}'");

            // Initialize pool if needed (first run)
            LegacyPoolManager.InitializePoolIfNeeded();

            // Select items using chained weighted selection
            LegacyItemOptions.LogDebug("LegacyItems: Calling WeightedItemSelector.SelectLegacyItems...");
            var selections = WeightedItemSelector.SelectLegacyItems(character);
            LegacyItemOptions.LogDebug($"LegacyItems: Selected {selections.Count} item(s): {string.Join(", ", selections.Select(s => $"{s.Item?.DisplayName ?? "null"} [{s.Source ?? "-"}]"))}");

            // Serialize selected items
            var entries = new List<LegacyPoolManager.LegacyItemEntry>();

            void SerializeSelection(WeightedItemSelector.SelectedItem selection)
            {
                if (selection?.Item == null)
                    return;

                var source = string.IsNullOrEmpty(selection.Source) ? "INVENTORY" : selection.Source;
                LegacyItemOptions.LogDebug($"LegacyItems: Serializing selected item '{selection.Item.DisplayName}' (Blueprint={selection.Item.Blueprint}, Source={source})");
                var entry = LegacyItemSerializer.SerializeItem(selection.Item, source, characterName, deathReason);
                if (entry != null)
                {
                    entries.Add(entry);
                    LegacyItemOptions.LogDebug($"LegacyItems: Serialized entry ID={entry.ID}, Blueprint={entry.Blueprint}, Source={source}");
                }
            }

            foreach (var selection in selections)
            {
                SerializeSelection(selection);
            }

            // Save to pending with GameID (NOT directly to pool)
            if (entries.Count > 0)
            {
                LegacyItemOptions.LogDebug($"LegacyItems: Saving {entries.Count} items to pending file with gameId='{gameId}'");
                PendingLegacyItems.SavePendingItems(entries, gameId);
                LegacyItemOptions.LogDebug($"LegacyItems: SUCCESS - Saved {entries.Count} items to pending from {characterName}. Items: {string.Join(", ", entries.Select(e => e.DisplayName))}");
            }
            else
            {
                LegacyItemOptions.LogDebug($"LegacyItems: WARNING - No items to save from {characterName}");
            }
        }

        /// <summary>
        /// Checks if the current death is from temporal fugue (precognition).
        /// </summary>
        private static bool IsTemporalFugueDeath(GameObject character)
        {
            try
            {
                if (character == null)
                    return false;

                // Check if character has active temporal fugue effect
                if (character.HasEffect("TemporalFugue"))
                    return true;

                // Check for precognition-related properties
                if (character.GetIntProperty("Precognition") > 0)
                    return true;

                return false;
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"LegacyItems: Error checking temporal fugue - {ex.Message}");
                return false;
            }
        }
    }

    /// <summary>
    /// Handles death event detection and legacy item collection.
    /// Attached to the player character to monitor death events.
    /// </summary>
    [Serializable]
    public class LegacyItemPart : IPart
    {
        public override bool SameAs(IPart p)
        {
            return false;
        }

        public override bool WantEvent(int ID, int cascade)
        {
            return base.WantEvent(ID, cascade)
                || ID == BeforeDieEvent.ID;
        }

        public override bool HandleEvent(BeforeDieEvent E)
        {
            LegacyItemOptions.LogDebug($"LegacyItems: [EVENT] BeforeDieEvent fired for {ParentObject?.DisplayName ?? "null"}");
            LegacyItemOptions.LogDebug($"LegacyItems:   IsPlayer={ParentObject?.IsPlayer()}, Killer={E.Killer?.DisplayName ?? "null"}, Reason={E.Reason ?? "null"}");

            // Collect items to pending file (not directly to pool).
            // OnGameLoaded will later decide whether to commit (different game) or clear (checkpoint restore).
            if (ParentObject != null && ParentObject.IsPlayer())
            {
                string characterName = The.Game?.PlayerName ?? ParentObject.DisplayNameOnlyDirect ?? "Unknown Hero";
                string deathReason = E.Reason ?? $"Killed by {E.Killer?.DisplayName ?? "unknown"}";
                LegacyItemCollector.CollectLegacyItemsToPending(ParentObject, characterName, deathReason);
            }

            return base.HandleEvent(E);
        }
    }
}

namespace XRL
{
    /// <summary>
    /// Bootstrapper to attach LegacyItemPart to the player on new game start.
    /// Must be in XRL namespace for the game's reflection system to find it.
    /// </summary>
    [PlayerMutator]
    public class LegacyItemPlayerMutator : IPlayerMutator
    {
        public void mutate(XRL.World.GameObject player)
        {
            LegacyItems.LegacyItemOptions.LogDebug("LegacyItems: PlayerMutator.mutate called");
            // Note: Don't check IsPlayer() here - at this point in the boot sequence,
            // the player object hasn't been fully assigned as "the player" yet.
            // The object passed to mutate() IS the player by definition.
            if (player != null)
            {
                if (!player.HasPart<LegacyItems.LegacyItemPart>())
                {
                    player.AddPart<LegacyItems.LegacyItemPart>();
                    LegacyItems.LegacyItemOptions.LogDebug($"LegacyItems: Attached LegacyItemPart to player '{player.DisplayName}'");
                }
                else
                {
                    LegacyItems.LegacyItemOptions.LogDebug("LegacyItems: Player already has LegacyItemPart");
                }
                // Note: ProcessPendingItems is called in LegacyItemEmbarkModule.handleBootEvent
                // at BOOTEVENT_GAMESTARTING, which happens later when save deletion is complete.
            }
            else
            {
                LegacyItems.LegacyItemOptions.LogDebug("LegacyItems: PlayerMutator - player is null");
            }
        }
    }

    /// <summary>
    /// Bootstrapper to attach LegacyItemPart to the player on save load.
    /// Must be in XRL namespace for the game's reflection system to find it.
    /// </summary>
    [HasCallAfterGameLoadedAttribute]
    public class LegacyItemLoadHandler
    {
        [CallAfterGameLoadedAttribute]
        public static void OnGameLoaded()
        {
            LegacyItems.LegacyItemOptions.LogDebug("LegacyItems: OnGameLoaded called");
            XRL.World.GameObject player = XRL.Core.XRLCore.Core?.Game?.Player?.Body;
            if (player != null)
            {
                player.RequirePart<LegacyItems.LegacyItemPart>();

                // Initialize pool if needed
                LegacyItems.LegacyPoolManager.InitializePoolIfNeeded();

                // Check if there are pending items from a previous character that retired
                var currentGameId = XRL.Core.XRLCore.Core?.Game?.GameID;
                LegacyItems.PendingLegacyItems.ProcessPendingItems(currentGameId);

                LegacyItems.LegacyItemOptions.LogDebug("LegacyItems: Mod loaded, LegacyItemPart ensured on player");
            }
            else
            {
                LegacyItems.LegacyItemOptions.LogDebug("LegacyItems: OnGameLoaded - player is null");
            }
        }
    }
}
