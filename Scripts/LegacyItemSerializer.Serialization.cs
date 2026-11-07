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
    public static partial class LegacyItemSerializer
    {
        #region Serialization Helpers

        private static string SerializeMods(GameObject item)
        {
            var mods = new List<string>();

            // Check for common mod parts
            if (item.HasPart("ModFlaming"))
                mods.Add("Flaming");
            if (item.HasPart("ModFreezing"))
                mods.Add("Freezing");
            if (item.HasPart("ModElectrified"))
                mods.Add("Electrified");
            if (item.HasPart("ModSharp"))
                mods.Add("Sharp");
            if (item.HasPart("ModReinforced"))
                mods.Add("Reinforced");

            // Get all parts and check for mods
            foreach (var part in item.PartsList)
            {
                var partName = part.GetType().Name;
                LegacyItemOptions.LogDebug($"LegacyItemSerializer.SerializeMods: partName='{partName}'");
                if (partName.StartsWith("Mod") && !mods.Contains(partName.Substring(3)))
                {
                    mods.Add(partName.Substring(3));
                }
            }

            return string.Join(",", mods);
        }

        private static bool IsArtifact(GameObject item)
        {
            return item.HasPart("Artifact") || item.GetIntProperty("Artifact") > 0;
        }

        private static string SerializeStackSize(GameObject item)
        {
            if (item.HasPart("Stacker"))
            {
                var stacker = item.GetPart<Stacker>();
                if (stacker != null)
                    return stacker.Number.ToString();
            }
            return "";
        }

        private static string SerializeCharges(GameObject item)
        {
            // Check for energy cell
            if (item.HasPart("EnergyCell"))
            {
                var cell = item.GetPart<EnergyCell>();
                if (cell != null)
                    return cell.Charge.ToString();
            }

            // Check for magazine-loaded weapons
            // Note: MagazineAmmoLoader.Ammo is a GameObject field, not an int
            // We store the ammo count (MaxAmmo) for preservation
            if (item.HasPart("MagazineAmmoLoader"))
            {
                var loader = item.GetPart<MagazineAmmoLoader>();
                if (loader != null && loader.Ammo != null)
                {
                    // Store the blueprint of loaded ammo for restoration
                    return loader.Ammo.Blueprint;
                }
            }

            // Check for other charge-based items
            var chargeProperty = item.GetIntProperty("Charge");
            if (chargeProperty > 0)
                return chargeProperty.ToString();

            return "";
        }

        private static string SerializeStainName(GameObject item)
        {
            // Prefer persisted stain name if we set one during deserialization
            var persisted = item.GetStringProperty("LegacyStainName");
            if (!string.IsNullOrEmpty(persisted))
                return persisted;

            try
            {
                var stain = item.GetEffect<LiquidStained>();
                if (stain?.Liquid != null && !string.IsNullOrEmpty(stain.Liquid.StainedName))
                    return stain.Liquid.StainedName;

                var volume = item.GetPart<LiquidVolume>();
                if (volume != null)
                {
                    var temp = new LiquidVolume
                    {
                        Volume = volume.Volume,
                        Primary = volume.Primary,
                        Secondary = volume.Secondary
                    };
                    foreach (var kv in volume.ComponentLiquids)
                    {
                        temp.ComponentLiquids[kv.Key] = kv.Value;
                    }
                    temp.Update();
                    if (!string.IsNullOrEmpty(temp.StainedName))
                    {
                        item.SetStringProperty("LegacyStainName", temp.StainedName);
                        return temp.StainedName;
                    }
                }
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[SERIALIZE] Failed to read StainName: {ex.Message}");
            }

            var fallbackStain = item.GetEffect<LiquidStained>();
            return fallbackStain?.Liquid?.StainedName ?? "";
        }

        private static string SerializeStainColor(GameObject item)
        {
            var persisted = item.GetStringProperty("LegacyStainColor");
            if (!string.IsNullOrEmpty(persisted))
                return persisted;

            try
            {
                var volume = item.GetPart<LiquidVolume>();
                if (volume != null && !string.IsNullOrEmpty(volume.StainedColor))
                {
                    item.SetStringProperty("LegacyStainColor", volume.StainedColor);
                    return volume.StainedColor;
                }
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[SERIALIZE] Failed to read StainColor: {ex.Message}");
            }

            var stain = item.GetEffect<LiquidStained>();
            var color = stain?.Liquid?.StainedColor;
            if (!string.IsNullOrEmpty(color))
            {
                item.SetStringProperty("LegacyStainColor", color);
            }
            return color ?? "";
        }

        private static string SerializeStainLiquid(GameObject item)
        {
            var persisted = item.GetStringProperty("LegacyStainLiquid");
            if (!string.IsNullOrEmpty(persisted))
                return persisted;

            var stainEffect = item.GetEffect<LiquidStained>();
            if (stainEffect?.Liquid != null)
            {
                var components = stainEffect.Liquid.GetComponentLiquids()
                    ?.ToDictionary(liquid => liquid.ID, liquid => stainEffect.Liquid.Proportion(liquid.ID));

                var encoded = StainSerializationHelpers.EncodeComponents(components);
                if (!string.IsNullOrEmpty(encoded))
                    return encoded;

                var id = stainEffect.Liquid.GetPrimaryLiquidID();
                if (!string.IsNullOrEmpty(id))
                    return id;
            }
            return "";
        }

        private static string SerializeStainLiquidDrams(GameObject item)
        {
            return "";
        }


        private static string SerializeLiquidType(GameObject item)
        {
            if (item.HasPart("LiquidVolume"))
            {
                var volume = item.GetPart<LiquidVolume>();
                if (volume != null && volume.Volume > 0)
                {
                    var primaryLiquid = volume.GetPrimaryLiquid();
                    if (primaryLiquid != null)
                        return primaryLiquid.ID;
                }
            }
            return "";
        }

        private static string SerializeLiquidDrams(GameObject item)
        {
            if (item.HasPart("LiquidVolume"))
            {
                var volume = item.GetPart<LiquidVolume>();
                if (volume != null)
                    return volume.Volume.ToString();
            }
            return "";
        }

        private static string SerializeDurability(GameObject item)
        {
            if (item.HasPart("Armor"))
            {
                var armor = item.GetPart<Armor>();
                if (armor != null)
                    return armor.AV.ToString();
            }

            if (item.HasPart("MeleeWeapon"))
            {
                var weapon = item.GetPart<MeleeWeapon>();
                if (weapon != null && weapon.GetType().GetProperty("Durability") != null)
                {
                    var durability = item.GetIntProperty("Durability");
                    if (durability > 0)
                        return durability.ToString();
                }
            }

            return "";
        }

        private static string SerializeCustomColors(GameObject item)
        {
            var colorData = new List<string>();

            // Check for render layer colors
            var render = item.GetPart("Render");
            if (render != null)
            {
                var renderType = render.GetType();
                var colorString = renderType.GetProperty("ColorString")?.GetValue(render) as string;
                var detailColor = renderType.GetProperty("DetailColor")?.GetValue(render) as string;

                if (!string.IsNullOrEmpty(colorString))
                    colorData.Add("color:" + colorString);
                if (!string.IsNullOrEmpty(detailColor))
                    colorData.Add("detail:" + detailColor);
            }

            return string.Join(";", colorData);
        }

        // Note: SerializeMaterial removed - material info is now part of Render.DisplayName
        // (e.g., "{{g|iron}} dagger" contains both the material name and color code)

        private static string SerializeModData(GameObject item)
        {
            var modEntries = new List<string>();

            foreach (var part in item.PartsList)
            {
                // Use broader mod detection from ModStateSerializer
                if (LegacyItems.Scripts.ModStateSerializer.IsModificationPart(part) &&
                    LegacyItems.Scripts.ModStateSerializer.IsAddedPart(item, part))
                {
                    var state = LegacyItems.Scripts.ModStateSerializer.SerializeModState(part);
                    if (state != null)
                    {
                        var serialized = LegacyItems.Scripts.ModStateSerializer.SerializeModStateEntry(state);
                        modEntries.Add(serialized);
                        LegacyItemOptions.LogDebug($"[SERIALIZE] Mod {state.PartName}: Tier={state.Tier}, StateFields={state.StateData.Count}");
                    }
                }
            }

            var result = string.Join(";", modEntries);
            LegacyItemOptions.LogDebug($"[SERIALIZE] ModData result: '{result}'");
            return result;
        }

        private static string SerializeProjectileData(GameObject item)
        {
            var projectileData = new List<string>();

            // Check for Projectile part
            if (item.HasPart("Projectile"))
            {
                var projectile = item.GetPart("Projectile");
                if (projectile != null)
                {
                    var projectileType = projectile.GetType();

                    // Get PenetrateCreatures field
                    var penetrateCreaturesField = projectileType.GetField("PenetrateCreatures");
                    if (penetrateCreaturesField != null)
                    {
                        var value = penetrateCreaturesField.GetValue(projectile);
                        if (value != null)
                            projectileData.Add($"PenetrateCreatures:{value}");
                    }

                    // Get PenetrateWalls field
                    var penetrateWallsField = projectileType.GetField("PenetrateWalls");
                    if (penetrateWallsField != null)
                    {
                        var value = penetrateWallsField.GetValue(projectile);
                        if (value != null)
                            projectileData.Add($"PenetrateWalls:{value}");
                    }

                    // Get Attributes field
                    var attributesField = projectileType.GetField("Attributes");
                    if (attributesField != null)
                    {
                        var value = attributesField.GetValue(projectile) as string;
                        if (!string.IsNullOrEmpty(value))
                            projectileData.Add($"Attributes:{value}");
                    }
                }
            }

            // Check for OmniphaseProjectile part
            if (item.HasPart("OmniphaseProjectile"))
            {
                projectileData.Add("OmniphaseProjectile:true");
            }

            var result = string.Join(";", projectileData);
            if (!string.IsNullOrEmpty(result))
            {
                LegacyItemOptions.LogDebug($"[SERIALIZE] ProjectileData result: '{result}'");
            }
            return result;
        }

        private static string SerializeExaminerState(GameObject item)
        {
            if (!item.HasPart("Examiner"))
                return "";

            try
            {
                var examiner = item.GetPart<Examiner>();
                if (examiner == null)
                    return "";

                int epistemicStatus = examiner.GetEpistemicStatus();
                int understanding = Examiner.GetUnderstanding(item.Blueprint);

                string result = $"{epistemicStatus}:{understanding}";
                LegacyItemOptions.LogDebug($"[SERIALIZE] ExaminerState: {result}");
                return result;
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"[SERIALIZE] Error serializing Examiner state: {ex.Message}");
                return "";
            }
        }

        private static string SerializeLightSource(GameObject item)
        {
            if (!item.HasPart("LightSource"))
                return "";

            try
            {
                var lightSource = item.GetPart<LightSource>();
                if (lightSource == null)
                    return "";

                string result = lightSource.Lit.ToString().ToLower();
                LegacyItemOptions.LogDebug($"[SERIALIZE] LightSourceLit: {result}");
                return result;
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"[SERIALIZE] Error serializing LightSource state: {ex.Message}");
                return "";
            }
        }

        private static string SerializeDataDisk(GameObject item)
        {
            if (!item.HasPart("DataDisk"))
                return "";

            try
            {
                var dataDisk = item.GetPart<DataDisk>();
                if (dataDisk == null)
                    return "";

                // Prefer any stored legacy blueprint override
                var stored = item.GetStringProperty("LegacyDataDiskBlueprint");
                if (!string.IsNullOrEmpty(stored))
                    return stored;

                // Use Data.Blueprint if available
                if (dataDisk.Data != null && !string.IsNullOrEmpty(dataDisk.Data.Blueprint))
                {
                    LegacyItemOptions.LogDebug($"[SERIALIZE] DataDiskBlueprint (Data): {dataDisk.Data.Blueprint}");
                    return dataDisk.Data.Blueprint;
                }

                // Fallback to TargetBlueprint if set
                if (!string.IsNullOrEmpty(dataDisk.TargetBlueprint))
                {
                    LegacyItemOptions.LogDebug($"[SERIALIZE] DataDiskBlueprint (Target): {dataDisk.TargetBlueprint}");
                    return dataDisk.TargetBlueprint;
                }

                return "";
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogError($"[SERIALIZE] Error serializing DataDisk blueprint: {ex.Message}");
                return "";
            }
        }

        #endregion

    }
}
