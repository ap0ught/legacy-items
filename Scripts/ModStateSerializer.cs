using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using XRL.World;
using XRL.World.Parts;

namespace LegacyItems.Scripts
{
    /// <summary>
    /// Helper class for detecting, serializing, and restoring mod part state.
    /// Implements broader mod detection heuristics and state preservation.
    /// </summary>
    public static class ModStateSerializer
    {
        // Known third-party mod prefixes
        private static readonly string[] ThirdPartyPrefixes = { "MAFT", "QBM", "MW" };

        /// <summary>
        /// Detects if a part is a modification part using multiple heuristics.
        /// </summary>
        /// <param name="part">The part to check</param>
        /// <returns>True if the part is a modification part</returns>
        public static bool IsModificationPart(IPart part)
        {
            if (part == null)
                return false;

            Type partType = part.GetType();
            string partName = partType.Name;

            // Strategy 1: Check if inherits from IModification
            if (IsIModification(part))
            {
                LegacyItemOptions.LogDebug($"[DETECT] Part {partName}: IsIModification=true");
                return true;
            }

            // Strategy 2: Check for "Mod" prefix (original behavior)
            if (partName.StartsWith("Mod"))
            {
                LegacyItemOptions.LogDebug($"[DETECT] Part {partName}: HasModPrefix=true");
                return true;
            }

            // Strategy 3: Check for known third-party prefixes
            foreach (var prefix in ThirdPartyPrefixes)
            {
                if (partName.StartsWith(prefix))
                {
                    LegacyItemOptions.LogDebug($"[DETECT] Part {partName}: ThirdPartyPrefix={prefix}");
                    return true;
                }
            }

            LegacyItemOptions.LogDebug($"[DETECT] Part {partName}: NotAMod");
            return false;
        }

        /// <summary>
        /// Checks if a part inherits from IModification.
        /// </summary>
        private static bool IsIModification(IPart part)
        {
            Type current = part.GetType();
            while (current != null && current != typeof(object))
            {
                if (current.Name == "IModification")
                    return true;
                current = current.BaseType;
            }
            return false;
        }

        /// <summary>
        /// Checks if a part was added to the item (not intrinsic to the blueprint).
        /// </summary>
        /// <param name="item">The item to check</param>
        /// <param name="part">The part to check</param>
        /// <returns>True if the part was added, false if it's intrinsic to the blueprint</returns>
        public static bool IsAddedPart(GameObject item, IPart part)
        {
            if (item == null || part == null)
                return false;

            try
            {
                var blueprint = GameObjectFactory.Factory.Blueprints.GetValue(item.Blueprint);
                if (blueprint == null)
                {
                    LegacyItemOptions.LogDebug($"[DETECT] No blueprint found for {item.Blueprint}, assuming part is added");
                    return true; // Assume added if no blueprint found
                }

                string partName = part.GetType().Name;
                bool hasPartInBlueprint = blueprint.HasPart(partName);

                LegacyItemOptions.LogDebug($"[DETECT] Part {partName} in blueprint {item.Blueprint}: {hasPartInBlueprint}");

                return !hasPartInBlueprint;
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[DETECT] Error checking blueprint for part: {ex.Message}");
                return true; // Assume added on error
            }
        }

        /// <summary>
        /// Extracts serializable state from a modification part.
        /// </summary>
        /// <param name="part">The part to serialize</param>
        /// <returns>A ModStateEntry containing the part's state</returns>
        public static ModStateEntry SerializeModState(IPart part)
        {
            if (part == null)
                return null;

            var entry = new ModStateEntry
            {
                PartName = part.GetType().Name,
                Tier = GetModTier(part),
                StateData = ExtractModSpecificState(part)
            };

            LegacyItemOptions.LogDebug($"[SERIALIZE] Mod {entry.PartName}: Tier={entry.Tier}, StateFields={entry.StateData.Count}");

            return entry;
        }

        /// <summary>
        /// Restores state to a modification part after creation.
        /// IMPORTANT: Call this BEFORE adding the part to the item.
        /// </summary>
        /// <param name="part">The part to restore</param>
        /// <param name="entry">The state entry containing the saved state</param>
        public static void RestoreModState(IPart part, ModStateEntry entry)
        {
            if (part == null || entry == null)
                return;

            // Restore Tier field
            SetModTier(part, entry.Tier);

            // Restore any mod-specific state
            RestoreModSpecificState(part, entry.StateData);

            LegacyItemOptions.LogDebug($"[DESERIALIZE] Restored mod {entry.PartName}: Tier={entry.Tier}");
        }

        /// <summary>
        /// Gets the Tier field value from a modification part.
        /// </summary>
        private static int GetModTier(IPart part)
        {
            try
            {
                // Special-case: some mods (e.g., ModExtradimensional) default to 0 even when applied.
                // Treat 0 as tier 1 when state is present so we don't downgrade on round-trip.
                if (part.GetType().Name == "ModExtradimensional")
                {
                    var trainingField = part.GetType().GetField("Training");
                    var dimensionField = part.GetType().GetField("DimensionName");
                    var hasState = (trainingField?.GetValue(part) != null) || (dimensionField?.GetValue(part) != null);

                    if (hasState)
                        return 1;
                }

                var tierField = part.GetType().GetField("Tier");
                if (tierField != null)
                {
                    object value = tierField.GetValue(part);
                    if (value != null)
                        return (int)value;
                }
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[SERIALIZE] Failed to get Tier from {part.GetType().Name}: {ex.Message}");
            }
            return 0;
        }

        /// <summary>
        /// Sets the Tier field value on a modification part.
        /// </summary>
        public static void SetModTier(IPart part, int tier)
        {
            try
            {
                var tierField = part.GetType().GetField("Tier");
                if (tierField != null)
                {
                    tierField.SetValue(part, tier);
                    LegacyItemOptions.LogDebug($"[DESERIALIZE] Set Tier={tier} on {part.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Failed to set Tier on {part.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Extracts mod-specific state that needs to be preserved.
        /// </summary>
        private static Dictionary<string, string> ExtractModSpecificState(IPart part)
        {
            var state = new Dictionary<string, string>();
            var partName = part.GetType().Name;

            try
            {
                // ModExtradimensional: Preserve indices and metadata
                if (partName == "ModExtradimensional")
                {
                    // Numeric indices
                    var weaponIndexField = part.GetType().GetField("WeaponModIndex");
                    var missileIndexField = part.GetType().GetField("MissileWeaponModIndex");
                    var armorIndexField = part.GetType().GetField("ArmorModIndex");
                    var shieldIndexField = part.GetType().GetField("ShieldModIndex");
                    var miscIndexField = part.GetType().GetField("MiscModIndex");

                    if (weaponIndexField != null)
                        state["WeaponModIndex"] = weaponIndexField.GetValue(part).ToString();

                    if (missileIndexField != null)
                        state["MissileWeaponModIndex"] = missileIndexField.GetValue(part).ToString();

                    if (armorIndexField != null)
                        state["ArmorModIndex"] = armorIndexField.GetValue(part).ToString();

                    if (shieldIndexField != null)
                        state["ShieldModIndex"] = shieldIndexField.GetValue(part).ToString();

                    if (miscIndexField != null)
                        state["MiscModIndex"] = miscIndexField.GetValue(part).ToString();

                    var trainingField = part.GetType().GetField("Training");
                    var dimensionField = part.GetType().GetField("DimensionName");
                    var secretField = part.GetType().GetField("SecretID");
                    var lookedAtField = part.GetType().GetField("LookedAt");

                    if (trainingField != null)
                    {
                        var value = trainingField.GetValue(part);
                        if (value != null)
                            state["Training"] = value.ToString();
                    }

                    if (dimensionField != null)
                    {
                        var value = dimensionField.GetValue(part);
                        if (value != null)
                            state["DimensionName"] = value.ToString();
                    }

                    if (secretField != null)
                    {
                        var value = secretField.GetValue(part);
                        if (value != null)
                            state["SecretID"] = value.ToString();
                    }

                    if (lookedAtField != null)
                    {
                        var value = lookedAtField.GetValue(part);
                        if (value != null)
                            state["LookedAt"] = value.ToString();
                    }
                }
                // ModJewelEncrusted: Preserve adjColored and bNamed (random color sequence)
                else if (partName == "ModJewelEncrusted")
                {
                    var adjColoredField = part.GetType().GetField("adjColored");
                    var bNamedField = part.GetType().GetField("bNamed");
                    var amountField = part.GetType().GetField("Amount");

                    if (adjColoredField != null)
                    {
                        var value = adjColoredField.GetValue(part);
                        if (value != null)
                            state["adjColored"] = value.ToString();
                    }

                    if (bNamedField != null)
                    {
                        var value = bNamedField.GetValue(part);
                        if (value != null)
                            state["bNamed"] = value.ToString();
                    }

                    if (amountField != null)
                    {
                        var value = amountField.GetValue(part);
                        if (value != null)
                            state["Amount"] = value.ToString();
                    }
                }

                // Add additional mod-specific extractions here as needed
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[SERIALIZE] Error extracting state from {partName}: {ex.Message}");
            }

            return state;
        }

        /// <summary>
        /// Restores mod-specific state to a part.
        /// </summary>
        private static void RestoreModSpecificState(IPart part, Dictionary<string, string> stateData)
        {
            if (stateData == null || stateData.Count == 0)
                return;

            var partName = part.GetType().Name;

            try
            {
                // ModExtradimensional: Restore indices and metadata
                if (partName == "ModExtradimensional")
                {
                    if (stateData.TryGetValue("WeaponModIndex", out var weaponIndexStr) && int.TryParse(weaponIndexStr, out var weaponIndex))
                    {
                        var weaponIndexField = part.GetType().GetField("WeaponModIndex");
                        weaponIndexField?.SetValue(part, weaponIndex);
                    }

                    if (stateData.TryGetValue("MissileWeaponModIndex", out var missileIndexStr) && int.TryParse(missileIndexStr, out var missileIndex))
                    {
                        var missileIndexField = part.GetType().GetField("MissileWeaponModIndex");
                        missileIndexField?.SetValue(part, missileIndex);
                    }

                    if (stateData.TryGetValue("ArmorModIndex", out var armorIndexStr) && int.TryParse(armorIndexStr, out var armorIndex))
                    {
                        var armorIndexField = part.GetType().GetField("ArmorModIndex");
                        armorIndexField?.SetValue(part, armorIndex);
                    }

                    if (stateData.TryGetValue("ShieldModIndex", out var shieldIndexStr) && int.TryParse(shieldIndexStr, out var shieldIndex))
                    {
                        var shieldIndexField = part.GetType().GetField("ShieldModIndex");
                        shieldIndexField?.SetValue(part, shieldIndex);
                    }

                    if (stateData.TryGetValue("MiscModIndex", out var miscIndexStr) && int.TryParse(miscIndexStr, out var miscIndex))
                    {
                        var miscIndexField = part.GetType().GetField("MiscModIndex");
                        miscIndexField?.SetValue(part, miscIndex);
                    }

                    if (stateData.TryGetValue("Training", out var training))
                    {
                        var trainingField = part.GetType().GetField("Training");
                        if (trainingField != null)
                            trainingField.SetValue(part, training);
                    }

                    if (stateData.TryGetValue("DimensionName", out var dimensionName))
                    {
                        var dimensionField = part.GetType().GetField("DimensionName");
                        if (dimensionField != null)
                            dimensionField.SetValue(part, dimensionName);
                    }

                    if (stateData.TryGetValue("SecretID", out var secretId))
                    {
                        var secretField = part.GetType().GetField("SecretID");
                        secretField?.SetValue(part, secretId);
                    }

                    if (stateData.TryGetValue("LookedAt", out var lookedAtStr) && bool.TryParse(lookedAtStr, out var lookedAt))
                    {
                        var lookedAtField = part.GetType().GetField("LookedAt");
                        lookedAtField?.SetValue(part, lookedAt);
                    }
                }
                // ModJewelEncrusted: Restore adjColored and bNamed (random color sequence)
                else if (partName == "ModJewelEncrusted")
                {
                    if (stateData.TryGetValue("adjColored", out var adjColored))
                    {
                        var adjColoredField = part.GetType().GetField("adjColored");
                        if (adjColoredField != null)
                        {
                            adjColoredField.SetValue(part, adjColored);
                            LegacyItemOptions.LogDebug($"[DESERIALIZE] Set adjColored='{adjColored}' on ModJewelEncrusted");
                        }
                    }

                    if (stateData.TryGetValue("bNamed", out var bNamedStr))
                    {
                        if (bool.TryParse(bNamedStr, out bool bNamed))
                        {
                            var bNamedField = part.GetType().GetField("bNamed");
                            if (bNamedField != null)
                            {
                                bNamedField.SetValue(part, bNamed);
                                LegacyItemOptions.LogDebug($"[DESERIALIZE] Set bNamed={bNamed} on ModJewelEncrusted");
                            }
                        }
                    }

                    if (stateData.TryGetValue("Amount", out var amountStr))
                    {
                        if (int.TryParse(amountStr, out int amount))
                        {
                            var amountField = part.GetType().GetField("Amount");
                            if (amountField != null)
                            {
                                amountField.SetValue(part, amount);
                                LegacyItemOptions.LogDebug($"[DESERIALIZE] Set Amount={amount} on ModJewelEncrusted");
                            }
                        }
                    }
                }

                // Add additional mod-specific restorations here as needed
            }
            catch (Exception ex)
            {
                LegacyItemOptions.LogDebug($"[DESERIALIZE] Error restoring state to {partName}: {ex.Message}");
            }
        }

        /// <summary>
        /// Serializes a ModStateEntry to a string format.
        /// Format: "PartName:Tier:key1=value1,key2=value2"
        /// </summary>
        public static string SerializeModStateEntry(ModStateEntry entry)
        {
            if (entry == null)
                return "";

            var statePairs = entry.StateData
                .Select(kvp => $"{EscapeDelimiter(kvp.Key)}={EscapeDelimiter(kvp.Value)}")
                .ToList();

            var stateString = string.Join(",", statePairs);

            return $"{entry.PartName}:{entry.Tier}:{stateString}";
        }

        /// <summary>
        /// Deserializes a string to a ModStateEntry.
        /// </summary>
        public static ModStateEntry DeserializeModStateEntry(string entryString)
        {
            if (string.IsNullOrWhiteSpace(entryString))
                return null;

            var parts = entryString.Split(':');
            if (parts.Length < 2)
                return null;

            var entry = new ModStateEntry
            {
                PartName = parts[0],
                Tier = 0,
                StateData = new Dictionary<string, string>()
            };

            int tier = 0;
            int.TryParse(parts[1], out tier);
            entry.Tier = tier;

            // Parse state data if present
            if (parts.Length >= 3 && !string.IsNullOrEmpty(parts[2]))
            {
                var statePairs = parts[2].Split(',');
                foreach (var pair in statePairs)
                {
                    var keyValue = pair.Split('=');
                    if (keyValue.Length == 2)
                    {
                        entry.StateData[UnescapeDelimiter(keyValue[0])] = UnescapeDelimiter(keyValue[1]);
                    }
                }
            }

            return entry;
        }

        /// <summary>
        /// Escapes delimiter characters in string values.
        /// </summary>
        private static string EscapeDelimiter(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace(":", "{{COLON}}")
                       .Replace(";", "{{SEMICOLON}}")
                       .Replace(",", "{{COMMA}}")
                       .Replace("=", "{{EQUALS}}");
        }

        /// <summary>
        /// Unescapes delimiter characters in string values.
        /// </summary>
        private static string UnescapeDelimiter(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "";
            return value.Replace("{{COLON}}", ":")
                       .Replace("{{SEMICOLON}}", ";")
                       .Replace("{{COMMA}}", ",")
                       .Replace("{{EQUALS}}", "=");
        }
    }

    /// <summary>
    /// Represents the serialized state of a modification part.
    /// </summary>
    public class ModStateEntry
    {
        public string PartName { get; set; }
        public int Tier { get; set; }
        public Dictionary<string, string> StateData { get; set; }

        public ModStateEntry()
        {
            StateData = new Dictionary<string, string>();
        }
    }
}
