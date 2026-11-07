using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XRL;
using XRL.Core;
using XRL.World;
using XRL.World.Effects;
using XRL.World.Parts;
using XRL.World.Parts.Mutation;

namespace LegacyItems
{
    /// <summary>
    /// Handles serialization and deserialization of game objects to/from pool entries.
    /// Preserves all relevant item properties including mods, durability, charges, liquids, etc.
    /// </summary>
    public static partial class LegacyItemSerializer
    {
        /// <summary>
        /// Serializes a GameObject to a LegacyItemEntry.
        /// </summary>
        /// <param name="item">The game object to serialize</param>
        /// <param name="source">Source type: "EQUIPPED" or "INVENTORY"</param>
        /// <param name="characterName">Name of the character who owned the item</param>
        /// <param name="deathReason">Optional death reason, e.g., "frozen to death by Warden Yrame"</param>
        public static LegacyPoolManager.LegacyItemEntry SerializeItem(GameObject item, string source, string characterName, string deathReason = null)
        {
            if (item == null)
                return null;

            return BuildEntry(item, source, characterName, deathReason);
        }

        /// <summary>
        /// Deserializes a LegacyItemEntry to a GameObject.
        /// </summary>
        public static GameObject DeserializeItem(LegacyPoolManager.LegacyItemEntry entry)
        {
            LegacyItemOptions.LogDebug($"LegacyItemSerializer.DeserializeItem: Starting deserialization");
            LegacyItemOptions.LogDebug($"  Entry: ID='{entry?.ID}' Blueprint='{entry?.Blueprint}' DisplayName='{entry?.DisplayName}'");
            LegacyItemOptions.LogDebug($"  Entry: Mods='{entry?.Mods}' IsArtifact={entry?.IsArtifact} Tier={entry?.Tier}");

            if (entry == null || string.IsNullOrEmpty(entry.Blueprint))
            {
                LegacyItemOptions.LogError($"LegacyItemSerializer: Entry is null or has no blueprint!");
                return null;
            }

            // Create base object from blueprint
            // Use BonusModChance:-999 to completely bypass mod generation (prevents ModAirfoil, ModSlender, etc from being added randomly)
            LegacyItemOptions.LogDebug($"LegacyItemSerializer: Calling CreateObject('{entry.Blueprint}') with BonusModChance:-999...");
            var item = GameObjectFactory.Factory.CreateObject(
                ObjectBlueprint: entry.Blueprint,
                BonusModChance: -999  // <= -999 completely bypasses modification logic per ModificationFactory.cs
            );
            if (item == null)
            {
                LegacyItemOptions.LogError($"LegacyItemSerializer: CreateObject returned null for '{entry.Blueprint}'!");
                return null;
            }

            LegacyItemOptions.LogDebug($"LegacyItemSerializer: CreateObject returned - Blueprint='{item.Blueprint}' DisplayName='{item.DisplayName}' (no random mods)");

            // Log all parts on the newly created item
            var partNames = string.Join(", ", item.PartsList.Select(p => p.GetType().Name));
            LegacyItemOptions.LogDebug($"LegacyItemSerializer: Item parts after CreateObject: [{partNames}]");

            // Strip temporary effects
            StripTemporaryEffects(item);

            // Restore mods first (before setting DisplayName)
            // Mods like ModJewelEncrusted will add prefixes dynamically
            // Prefer ModData if available, fall back to legacy Mods format
            if (!string.IsNullOrEmpty(entry.ModData) || !string.IsNullOrEmpty(entry.Mods))
            {
                LegacyItemOptions.LogDebug($"LegacyItemSerializer: Restoring mods (ModData='{entry.ModData}', Mods='{entry.Mods}')");
                DeserializeMods(item, entry.Mods, entry.ModData);
            }
            else
            {
                LegacyItemOptions.LogDebug($"LegacyItemSerializer: No mods to restore");
            }

            // Restore the base custom name (without mod prefixes) from BaseDisplayName if available
            // This preserves custom item names while letting mods add their prefixes dynamically
            var renderPart = item.GetPart<Render>();
            if (renderPart != null)
            {
                // Use BaseDisplayName if available (custom name without mod prefixes)
                // Otherwise fall back to DisplayName (for items without mods)
                string nameToRestore = !string.IsNullOrEmpty(entry.BaseDisplayName)
                    ? entry.BaseDisplayName
                    : entry.DisplayName;

                if (!string.IsNullOrEmpty(nameToRestore))
                {
                    renderPart.DisplayName = nameToRestore;
                }

                // Persist both the base name and full visible name for stable round-trips.
                var baseNameToPersist = !string.IsNullOrEmpty(entry.BaseDisplayName)
                    ? entry.BaseDisplayName
                    : nameToRestore ?? "";
                var displayNameToPersist = !string.IsNullOrEmpty(entry.DisplayName)
                    ? entry.DisplayName
                    : nameToRestore ?? "";
                item.SetStringProperty("LegacyBaseDisplayName", baseNameToPersist);
                item.SetStringProperty("LegacyDisplayName", displayNameToPersist);

                // Restore tile and colors from saved entry to keep visual identity (books, scrolls, etc.)
                RestoreRenderVisuals(renderPart, entry);
            }

            // Restore stack size
            if (!string.IsNullOrEmpty(entry.StackSize))
            {
                DeserializeStackSize(item, entry.StackSize);
            }

            // Restore charges
            if (!string.IsNullOrEmpty(entry.Charges))
            {
                DeserializeCharges(item, entry.Charges);
            }

            // Restore liquid contents (always call to handle empty containers too)
            // This clears auto-filled liquid from ObjectCreatedEvent
            DeserializeLiquid(item, entry.LiquidType, entry.LiquidDrams);
            RestoreStain(item, entry.StainName, entry.StainColor);
            RestoreStainLiquid(item, entry.StainLiquid, entry.StainLiquidDrams, entry.StainName, entry.StainColor);

            // Restore durability
            if (!string.IsNullOrEmpty(entry.Durability))
            {
                DeserializeDurability(item, entry.Durability);
            }

            // Restore custom colors
            if (!string.IsNullOrEmpty(entry.CustomColors))
            {
                DeserializeCustomColors(item, entry.CustomColors);
            }

            // Restore projectile data (piercing shots, etc.)
            if (!string.IsNullOrEmpty(entry.ProjectileData))
            {
                DeserializeProjectileData(item, entry.ProjectileData);
            }

            // Restore Examiner state (identification)
            if (!string.IsNullOrEmpty(entry.ExaminerState))
            {
                DeserializeExaminerState(item, entry.ExaminerState);
            }

            // Restore LightSource state (lit/unlit for torches)
            if (!string.IsNullOrEmpty(entry.LightSourceLit))
            {
                DeserializeLightSource(item, entry.LightSourceLit);
            }

            // Restore DataDisk recipe (prevent random recipe selection)
            if (!string.IsNullOrEmpty(entry.DataDiskBlueprint))
            {
                DeserializeDataDisk(item, entry.DataDiskBlueprint);
            }

            // Note: Material is no longer deserialized separately - it's now part of DisplayName
            // (e.g., "{{g|iron}} dagger" instead of storing "Iron" in Material field)

            // Log final state
            var finalPartNames = string.Join(", ", item.PartsList.Select(p => p.GetType().Name));
            LegacyItemOptions.LogDebug($"LegacyItemSerializer: Final item - Blueprint='{item.Blueprint}' DisplayName='{item.DisplayName}'");
            LegacyItemOptions.LogDebug($"LegacyItemSerializer: Final parts: [{finalPartNames}]");

            return item;
        }

        private static LegacyPoolManager.LegacyItemEntry BuildEntry(GameObject item, string source, string characterName, string deathReason)
        {
            var renderPart = item.GetPart<Render>();

            var (displayName, baseDisplayName) = ResolveDisplayNames(item, renderPart);
            var (tile, tileColor, detailColor) = ResolveRenderVisuals(renderPart);

            return new LegacyPoolManager.LegacyItemEntry
            {
                ID = LegacyPoolManager.GenerateID(),
                Source = source,
                Blueprint = item.Blueprint,
                DisplayName = displayName,
                Tier = item.GetTier(),
                TechTier = item.GetTechTier(),
                Mods = SerializeMods(item),
                ModData = SerializeModData(item),
                IsArtifact = IsArtifact(item),
                CharacterName = characterName,
                StackSize = SerializeStackSize(item),
                Charges = SerializeCharges(item),
                LiquidType = SerializeLiquidType(item),
                LiquidDrams = SerializeLiquidDrams(item),
                Durability = SerializeDurability(item),
                CustomColors = SerializeCustomColors(item),
                BaseDisplayName = baseDisplayName,
                Tile = tile,
                TileColor = tileColor,
                DetailColor = detailColor,
                DeathReason = deathReason ?? "",
                ProjectileData = SerializeProjectileData(item),
                ExaminerState = SerializeExaminerState(item),
                LightSourceLit = SerializeLightSource(item),
                DataDiskBlueprint = SerializeDataDisk(item),
                StainName = SerializeStainName(item),
                StainColor = SerializeStainColor(item),
                StainLiquid = SerializeStainLiquid(item),
                StainLiquidDrams = SerializeStainLiquidDrams(item)
            };
        }

        private static (string displayName, string baseDisplayName) ResolveDisplayNames(GameObject item, Render renderPart)
        {
            var persistedDisplayName = item.GetStringProperty("LegacyDisplayName");
            var displayName = !string.IsNullOrEmpty(persistedDisplayName)
                ? persistedDisplayName
                : item.DisplayName ?? renderPart?.DisplayName ?? item.Blueprint;

            var persistedBaseDisplayName = item.GetStringProperty("LegacyBaseDisplayName");
            var baseDisplayName = !string.IsNullOrEmpty(persistedBaseDisplayName)
                ? persistedBaseDisplayName
                : renderPart?.DisplayName ?? item.Blueprint;

            return (displayName, baseDisplayName);
        }

        private static (string tile, string tileColor, string detailColor) ResolveRenderVisuals(Render renderPart)
        {
            if (renderPart == null)
                return ("", "", "");

            var tile = renderPart.Tile ?? "";
            var tileColor = renderPart.TileColor ?? renderPart.ColorString ?? "";
            var detailColor = renderPart.DetailColor ?? "";
            return (tile, tileColor, detailColor);
        }

    }
}
