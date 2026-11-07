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
using XRL.World.Tinkering;

namespace LegacyItems
{
    public static partial class LegacyItemSerializer
    {
        private static void DeserializeMods(GameObject item, string modsString, string modDataString = null)
        {
            // Prefer new ModData format if available
            if (!string.IsNullOrEmpty(modDataString))
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Using ModData format: '{modDataString}'");
                DeserializeModData(item, modDataString);
                return;
            }

            // Fall back to legacy format for backward compatibility
            if (string.IsNullOrEmpty(modsString))
                return;

            LegacyItemOptions.LogDebug($"[DESERIALIZE] Using legacy Mods format: '{modsString}'");
            var mods = modsString.Split(',');
            foreach (var mod in mods)
            {
                if (string.IsNullOrWhiteSpace(mod))
                    continue;

                var partName = ResolveModPartName(mod.Trim());
                if (!item.HasPart(partName))
                {
                    AddModPart(item, partName, null, null);
                }
            }
        }

        private static void DeserializeModData(GameObject item, string modDataString)
        {
            var entries = modDataString.Split(';');
            foreach (var entryString in entries)
            {
                if (string.IsNullOrWhiteSpace(entryString))
                    continue;

                var entry = LegacyItems.Scripts.ModStateSerializer.DeserializeModStateEntry(entryString);
                if (entry == null)
                    continue;

                // Skip if part already exists in the item
                if (item.HasPart(entry.PartName))
                {
                    LegacyItemOptions.LogDebug($"[DESERIALIZE] Skipping {entry.PartName} - already present in blueprint");
                    continue;
                }

                AddModPart(item, entry.PartName, entry.Tier, entry.StateData);
            }
        }

        private static string ResolveModPartName(string modName)
        {
            // Try with "Mod" prefix first (standard mods)
            string withMod = "Mod" + modName;
            var type = Type.GetType($"XRL.World.Parts.{withMod}, Assembly-CSharp");
            if (type != null)
                return withMod;

            // Try with "XRL.World.Parts.Mutation" namespace
            type = Type.GetType($"XRL.World.Parts.Mutation.{withMod}, Assembly-CSharp");
            if (type != null)
                return withMod;

            // Try exact name (for third-party mods like MAFT)
            type = Type.GetType($"XRL.World.Parts.{modName}, Assembly-CSharp");
            if (type != null)
                return modName;

            // Default to Mod prefix
            return withMod;
        }

        private static void AddModPart(GameObject item, string partName, int? tier, Dictionary<string, string> stateData)
        {
            try
            {
                var partType = Type.GetType($"XRL.World.Parts.{partName}, Assembly-CSharp");
                if (partType == null)
                {
                    partType = Type.GetType($"XRL.World.Parts.Mutation.{partName}, Assembly-CSharp");
                }

                if (partType != null)
                {
                    var part = Activator.CreateInstance(partType) as IPart;
                    if (part != null)
                    {
                        // CRITICAL: Restore tier and state BEFORE adding the part
                        // This prevents ApplyModification() from recalculating based on item tier
                        if (tier.HasValue)
                        {
                            LegacyItems.Scripts.ModStateSerializer.SetModTier(part, tier.Value);
                            LegacyItemOptions.LogDebug($"[DESERIALIZE] Restored Tier={tier.Value} for {partName}");
                        }

                        // Restore mod-specific state
                        if (stateData != null && stateData.Count > 0)
                        {
                            var entry = new LegacyItems.Scripts.ModStateEntry
                            {
                                PartName = partName,
                                Tier = tier ?? 0,
                                StateData = stateData
                            };
                            LegacyItems.Scripts.ModStateSerializer.RestoreModState(part, entry);
                        }

                        // Now add the part to the item
                        item.AddPart(part);
                        LegacyItemOptions.LogDebug($"[DESERIALIZE] Added mod {partName} to item");
                    }
                }
                else
                {
                    LegacyItemOptions.LogDebug($"[DESERIALIZE] Part type not found: {partName}");
                }
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Failed to add mod {partName}: {ex.Message}");
            }
        }

        private static void DeserializeStackSize(GameObject item, string stackSize)
        {
            if (int.TryParse(stackSize, out int count) && count > 1)
            {
                if (item.HasPart("Stacker"))
                {
                    var stacker = item.GetPart<Stacker>();
                    if (stacker != null)
                    {
                        // Set the stack count using StackCount property
                        stacker.StackCount = count;
                    }
                }
            }
        }

        private static void DeserializeCharges(GameObject item, string charges)
        {
            if (string.IsNullOrEmpty(charges))
                return;

            // Check if this is an energy cell charge value (numeric)
            if (int.TryParse(charges, out int chargeValue))
            {
                if (item.HasPart("EnergyCell"))
                {
                    var cell = item.GetPart<EnergyCell>();
                    if (cell != null)
                        cell.Charge = chargeValue;
                }
                else
                {
                    item.SetIntProperty("Charge", chargeValue);
                }
            }
            // Otherwise it might be a blueprint for magazine ammo
            else if (item.HasPart("MagazineAmmoLoader"))
            {
                var loader = item.GetPart<MagazineAmmoLoader>();
                if (loader != null)
                {
                    try
                    {
                        // Create the ammo GameObject from blueprint
                        var ammoObj = GameObjectFactory.Factory.CreateObject(charges);
                        if (ammoObj != null)
                        {
                            loader.Ammo = ammoObj;
                        }
                    }
                    catch (Exception ex)
                    {
                        LegacyItemOptions.LogError($"LegacyItems: Failed to create ammo from blueprint {charges} - {ex.Message}");
                    }
                }
            }
        }

        private static void DeserializeLiquid(GameObject item, string liquidType, string liquidDrams)
        {
            if (!item.HasPart("LiquidVolume"))
                return;

            var volume = item.GetPart<LiquidVolume>();
            if (volume == null)
                return;

            // Always empty first to clear any auto-filled liquid from ObjectCreatedEvent
            volume.Empty(WillCheckImage: false, NoDestroy: true);

            // Only fill if there's actually liquid to restore
            if (int.TryParse(liquidDrams, out int drams) && drams > 0 && !string.IsNullOrEmpty(liquidType))
            {
                volume.Fill(liquidType, drams);
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Filled LiquidVolume with {drams} drams of {liquidType}");
            }
            else
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Left LiquidVolume empty (was {liquidType}/{liquidDrams})");
            }
        }

        private static void RestoreStain(GameObject item, string stainName, string stainColor)
        {
            if (string.IsNullOrEmpty(stainName) && string.IsNullOrEmpty(stainColor))
                return;

            var volume = item.GetPart<LiquidVolume>();
            if (volume == null)
                return;

            try
            {
                var lvType = typeof(LiquidVolume);
                var nameField = lvType.GetField("_StainedName", BindingFlags.NonPublic | BindingFlags.Instance);
                var colorField = lvType.GetField("_StainedColor", BindingFlags.NonPublic | BindingFlags.Instance);

                nameField?.SetValue(volume, stainName);
                colorField?.SetValue(volume, stainColor);
                item.SetStringProperty("LegacyStainName", stainName ?? "");
                item.SetStringProperty("LegacyStainColor", stainColor ?? "");

                LegacyItemOptions.LogDebug($"[DESERIALIZE] Restored stain '{stainName}' color '{stainColor}'");
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Failed to restore stain: {ex.Message}");
            }
        }

        private static void RestoreStainLiquid(GameObject item, string stainLiquid, string stainDrams, string stainName, string stainColor)
        {
            if (string.IsNullOrEmpty(stainLiquid))
                return;

            int drams = 1;
            int.TryParse(stainDrams, out drams);
            if (drams <= 0) drams = 1;

            try
            {
                LiquidVolume lv;
                if (StainSerializationHelpers.TryParseComponents(stainLiquid, out var components))
                {
                    lv = new LiquidVolume
                    {
                        Volume = drams
                    };
                    foreach (var kv in components)
                    {
                        lv.ComponentLiquids[kv.Key] = kv.Value;
                    }
                    lv.Update();
                }
                else
                {
                    lv = new LiquidVolume(stainLiquid, drams);
                }

                if (!string.IsNullOrEmpty(stainName))
                    lv._StainedName = stainName;
                if (!string.IsNullOrEmpty(stainColor))
                    lv._StainedColor = stainColor;

                var existing = item.GetEffect<LiquidStained>();
                if (existing != null)
                {
                    existing.Liquid = lv;
                    existing.Liquid.Update();
                }
                else
                {
                    item.ForceApplyEffect(new LiquidStained(lv));
                }
                item.SetStringProperty("LegacyStainLiquid", stainLiquid);
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Applied LiquidStained effect: {stainLiquid} ({drams} drams)");
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Failed to apply LiquidStained: {ex.Message}");
            }
        }

        private static void DeserializeDurability(GameObject item, string durability)
        {
            if (int.TryParse(durability, out int durabilityValue))
            {
                if (item.HasPart("Armor"))
                {
                    var armor = item.GetPart<Armor>();
                    if (armor != null)
                        armor.AV = durabilityValue;
                }
                else
                {
                    item.SetIntProperty("Durability", durabilityValue);
                }
            }
        }

        private static void DeserializeCustomColors(GameObject item, string customColors)
        {
            if (string.IsNullOrWhiteSpace(customColors))
                return;

            var colorParts = customColors.Split(';');
            var render = item.GetPart("Render");
            if (render == null)
                return;

            var renderType = render.GetType();

            foreach (var colorPart in colorParts)
            {
                var parts = colorPart.Split(':');
                if (parts.Length != 2)
                    continue;

                var key = parts[0];
                var value = parts[1];

                if (key == "color")
                {
                    renderType.GetProperty("ColorString")?.SetValue(render, value);
                }
                else if (key == "detail")
                {
                    renderType.GetProperty("DetailColor")?.SetValue(render, value);
                }
            }
        }

        // Note: DeserializeMaterial removed - material info is now part of Render.DisplayName

        /// <summary>
        /// Restores Render tile/TileColor/DetailColor from a saved entry.
        /// </summary>
        private static void RestoreRenderVisuals(Render renderPart, LegacyPoolManager.LegacyItemEntry entry)
        {
            if (renderPart == null || entry == null)
                return;

            if (!string.IsNullOrEmpty(entry.Tile))
                renderPart.Tile = entry.Tile;

            if (!string.IsNullOrEmpty(entry.TileColor))
                renderPart.TileColor = entry.TileColor;

            if (!string.IsNullOrEmpty(entry.DetailColor))
                renderPart.DetailColor = entry.DetailColor;
        }

        private static void DeserializeProjectileData(GameObject item, string projectileDataString)
        {
            if (string.IsNullOrWhiteSpace(projectileDataString))
                return;

            LegacyItemOptions.LogDebug($"[DESERIALIZE] ProjectileData: '{projectileDataString}'");

            var entries = projectileDataString.Split(';');
            var data = new Dictionary<string, string>();

            foreach (var entry in entries)
            {
                var parts = entry.Split(':');
                if (parts.Length >= 2)
                {
                    data[parts[0]] = parts[1];
                }
            }

            // Restore Projectile part properties
            if (item.HasPart("Projectile"))
            {
                var projectile = item.GetPart("Projectile");
                if (projectile != null)
                {
                    var projectileType = projectile.GetType();

                    // Restore PenetrateCreatures
                    if (data.TryGetValue("PenetrateCreatures", out var penetrateCreatures))
                    {
                        if (bool.TryParse(penetrateCreatures, out bool value))
                        {
                            var field = projectileType.GetField("PenetrateCreatures");
                            if (field != null)
                            {
                                field.SetValue(projectile, value);
                                LegacyItemOptions.LogDebug($"[DESERIALIZE] Set PenetrateCreatures={value}");
                            }
                        }
                    }

                    // Restore PenetrateWalls
                    if (data.TryGetValue("PenetrateWalls", out var penetrateWalls))
                    {
                        if (bool.TryParse(penetrateWalls, out bool value))
                        {
                            var field = projectileType.GetField("PenetrateWalls");
                            if (field != null)
                            {
                                field.SetValue(projectile, value);
                                LegacyItemOptions.LogDebug($"[DESERIALIZE] Set PenetrateWalls={value}");
                            }
                        }
                    }

                    // Restore Attributes
                    if (data.TryGetValue("Attributes", out var attributes))
                    {
                        var field = projectileType.GetField("Attributes");
                        if (field != null)
                        {
                            field.SetValue(projectile, attributes);
                            LegacyItemOptions.LogDebug($"[DESERIALIZE] Set Attributes={attributes}");
                        }
                    }
                }
            }

            // Add OmniphaseProjectile if it was present
            if (data.TryGetValue("OmniphaseProjectile", out var omniphase) && omniphase == "true")
            {
                if (!item.HasPart("OmniphaseProjectile"))
                {
                    try
                    {
                        var omniphaseType = Type.GetType("XRL.World.Parts.OmniphaseProjectile, Assembly-CSharp");
                        if (omniphaseType != null)
                        {
                            var omniphasePart = Activator.CreateInstance(omniphaseType) as IPart;
                            if (omniphasePart != null)
                            {
                                item.AddPart(omniphasePart);
                                LegacyItemOptions.LogDebug($"[DESERIALIZE] Added OmniphaseProjectile part");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LegacyItemOptions.LogDebug($"[DESERIALIZE] Failed to add OmniphaseProjectile: {ex.Message}");
                    }
                }
            }
        }

        private static void DeserializeExaminerState(GameObject item, string examinerState)
        {
            if (string.IsNullOrEmpty(examinerState) || !item.HasPart("Examiner"))
                return;

            try
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] ExaminerState: '{examinerState}'");

                var parts = examinerState.Split(':');
                if (parts.Length != 2)
                {
                    LegacyItemOptions.LogError($"[DESERIALIZE] Invalid ExaminerState format: '{examinerState}'");
                    return;
                }

                if (!int.TryParse(parts[0], out int epistemicStatus))
                {
                    LegacyItemOptions.LogError($"[DESERIALIZE] Invalid EpistemicStatus: '{parts[0]}'");
                    return;
                }

                if (!int.TryParse(parts[1], out int understanding))
                {
                    LegacyItemOptions.LogError($"[DESERIALIZE] Invalid Understanding: '{parts[1]}'");
                    return;
                }

                // Restore epistemic status
                var examiner = item.GetPart<Examiner>();
                if (examiner != null)
                {
                    examiner.SetEpistemicStatus(epistemicStatus);
                    LegacyItemOptions.LogDebug($"[DESERIALIZE] Set EpistemicStatus to {epistemicStatus}");
                }

                // Restore understanding (global per-blueprint)
                Examiner.SetUnderstanding(item.Blueprint, understanding);
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Set Understanding for {item.Blueprint} to {understanding}");
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"[DESERIALIZE] Error deserializing Examiner state: {ex.Message}");
            }
        }

        private static void DeserializeLightSource(GameObject item, string lightSourceLit)
        {
            if (string.IsNullOrEmpty(lightSourceLit) || !item.HasPart("LightSource"))
                return;

            try
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] LightSourceLit: '{lightSourceLit}'");

                if (!bool.TryParse(lightSourceLit, out bool lit))
                {
                    LegacyItemOptions.LogError($"[DESERIALIZE] Invalid LightSourceLit value: '{lightSourceLit}'");
                    return;
                }

                var lightSource = item.GetPart<LightSource>();
                if (lightSource != null)
                {
                    lightSource.Lit = lit;
                    LegacyItemOptions.LogDebug($"[DESERIALIZE] Set LightSource.Lit to {lit}");
                }
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"[DESERIALIZE] Error deserializing LightSource state: {ex.Message}");
            }
        }

        private static void DeserializeDataDisk(GameObject item, string dataDiskBlueprint)
        {
            if (string.IsNullOrEmpty(dataDiskBlueprint) || !item.HasPart("DataDisk"))
                return;

            try
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] DataDiskBlueprint: '{dataDiskBlueprint}'");

                var dataDisk = item.GetPart<DataDisk>();
                if (dataDisk == null)
                    return;

                var recipe = ResolveDataDiskRecipe(dataDiskBlueprint);
                if (recipe != null)
                {
                    dataDisk.Data = recipe;
                    dataDisk.TargetBlueprint = recipe.Blueprint;
                    LegacyItemOptions.LogDebug($"[DESERIALIZE] Restored DataDisk.Data for '{recipe.Blueprint}'");
                    return;
                }

                LegacyItemOptions.LogError($"[DESERIALIZE] Failed to resolve DataDisk recipe for blueprint '{dataDiskBlueprint}'");
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"[DESERIALIZE] Error deserializing DataDisk blueprint: {ex.Message}");
            }
        }

        private static TinkerData ResolveDataDiskRecipe(string blueprint)
        {
            if (string.IsNullOrEmpty(blueprint))
                return null;

            // Handle "mod:" wish-form blueprints
            if (blueprint.StartsWith("mod:", StringComparison.OrdinalIgnoreCase))
            {
                blueprint = "[mod]" + blueprint.Substring(4);
            }

            if (blueprint.StartsWith("[mod]", StringComparison.OrdinalIgnoreCase))
            {
                return TinkerData.TinkerRecipes.FirstOrDefault(t =>
                    t.Type == "Mod" && t.Blueprint.Equals(blueprint, StringComparison.OrdinalIgnoreCase));
            }

            return TinkerData.TinkerRecipes.FirstOrDefault(t =>
                t.Type == "Build" && t.Blueprint.Equals(blueprint, StringComparison.OrdinalIgnoreCase));
        }

        private static void StripTemporaryEffects(GameObject item)
        {
            // Remove temporary status effects
            var effectsToRemove = new[]
            {
                "Stuck", "Frozen", "Confused", "Bleeding",
                "Poisoned", "Ill", "Burning", "Asleep", "Paralyzed"
            };

            foreach (var effectName in effectsToRemove)
            {
                // Get the effect type from XRL.World.Effects namespace
                var effectType = Type.GetType($"XRL.World.Effects.{effectName}, Assembly-CSharp");
                if (effectType != null)
                {
                    while (item.HasEffect(effectType))
                    {
                        // Use the Type-based RemoveEffect method
                        item.RemoveEffect(effectType);
                    }
                }
            }
        }
    }
}
