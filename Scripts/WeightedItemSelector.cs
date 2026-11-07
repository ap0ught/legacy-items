using System;
using System.Collections.Generic;
using System.Linq;
using XRL;
using XRL.Core;
using XRL.World;
using XRL.World.Parts;

namespace LegacyItems
{
    /// <summary>
    /// Handles weighted random selection of items based on value.
    /// </summary>
    public static class WeightedItemSelector
    {
        private const double ProcContinuationChance = 0.835;

        /// <summary>
        /// Represents an item with its calculated weight.
        /// </summary>
        public class WeightedItem
        {
            public GameObject Item { get; set; }
            public int BaseWeight { get; set; }
            public int Weight { get; set; }
            public int StackCount { get; set; }
            public string LiquidSuffix { get; set; }
            public string Source { get; set; }
            public int WeightMultiplier { get; set; }
            public bool IsEquipped { get; set; }
        }

        /// <summary>
        /// Calculates the weight from tier, complexity, and commerce value.
        /// Formula: Max(1, (Tier² × 10) + (Complexity² × 10) + Value)
        /// This is the pure calculation, separated for testability.
        /// </summary>
        public static int CalculateWeight(int tier, int complexity, int commerceValue)
        {
            return Math.Max(1, (tier * tier * 10) + (complexity * complexity * 10) + commerceValue);
        }

        /// <summary>
        /// Calculates the weight for a stack of items by applying a stack multiplier.
        /// </summary>
        public static int CalculateStackedWeight(int tier, int complexity, int commerceValue, int stackCount)
        {
            int normalizedStack = Math.Max(1, stackCount);
            int adjustedCommerce = commerceValue * normalizedStack;
            return CalculateWeight(tier, complexity, adjustedCommerce);
        }

        /// <summary>
        /// Gets the stack count for an item, clamped to at least 1.
        /// </summary>
        private static int GetStackCount(GameObject item)
        {
            if (item == null)
                return 1;

            if (item.HasPart("Stacker"))
            {
                var stacker = item.GetPart<Stacker>();
                if (stacker != null)
                    return Math.Max(1, stacker.Number);
            }

            return 1;
        }

        /// <summary>
        /// Gets commerce value contributed by contained liquid (primary liquid only).
        /// </summary>
        private static int GetLiquidCommerceValue(GameObject item)
        {
            if (item == null || !item.HasPart("LiquidVolume"))
                return 0;

            try
            {
                var volume = item.GetPart<LiquidVolume>();
                if (volume != null && volume.Volume > 0)
                {
                    // Handles mixtures and dilution via component proportions
                    double valuePerDram = volume.GetLiquidExtrinsicValuePerDram();
                    if (valuePerDram <= 0)
                    {
                        var primary = volume.GetPrimaryLiquid();
                        if (primary != null)
                            valuePerDram = primary.GetValuePerDram();
                    }
                    return (int)Math.Round(valuePerDram * volume.Volume);
                }
            }
            catch (Exception)
            {
                // Best-effort; ignore failures and fall back to base commerce value
            }

            return 0;
        }

        /// <summary>
        /// Gets a display suffix describing liquid volume for logging (e.g., " (32dr water)" or " (1dr water,1dr honey)").
        /// </summary>
        private static string GetLiquidSuffix(GameObject item)
        {
            if (item == null || !item.HasPart("LiquidVolume"))
                return "";

            try
            {
                var volume = item.GetPart<LiquidVolume>();
                if (volume != null && volume.Volume > 0)
                {
                    // If multiple component liquids exist, use the game's liquid naming to reflect mixtures.
                    if (volume.ComponentLiquids != null && volume.ComponentLiquids.Count > 0)
                    {
                        if (volume.ComponentLiquids.Count > 1)
                        {
                            return $" ({volume.Volume}dr {volume.GetLiquidName()})";
                        }
                    }

                    var primaryLiquid = volume.GetPrimaryLiquid();
                    if (primaryLiquid != null)
                    {
                        return $" ({volume.Volume}dr {primaryLiquid.ID})";
                    }
                }
            }
            catch (Exception)
            {
                // Logging best-effort; ignore failures
            }

            return "";
        }

        /// <summary>
        /// Calculates the weight for an item based on tier, complexity, and commerce value.
        /// Formula: Max(1, (Tier² × 10) + (Complexity² × 10) + Value)
        /// </summary>
        public static int CalculateItemWeight(GameObject item, bool logDetails = false, int weightMultiplierForLog = 1, string sourceForLog = null)
        {
            if (item == null)
                return 0;

            int tier = item.GetTier();
            int complexity = item.GetComplexity();
            int stackCount = GetStackCount(item);

            // Get commerce value from Commerce part
            int commerceValue = 0;
            if (item.HasPart("Commerce"))
            {
                var commerce = item.GetPart<Commerce>();
                if (commerce != null)
                    commerceValue = (int)commerce.Value;
            }

            // Add value of contained liquid (primary liquid only)
            int liquidCommerce = GetLiquidCommerceValue(item);
            commerceValue += liquidCommerce;

            int weight = CalculateStackedWeight(tier, complexity, commerceValue, stackCount);
            int adjustedCommerceValue = commerceValue * stackCount;

            // Liquid context for logging only
            string liquidSuffix = GetLiquidSuffix(item);

            if (logDetails)
            {
                var nameWithLiquid = $"{item.DisplayNameOnlyDirect}{liquidSuffix}";
                var displayLabel = stackCount > 1 ? $"{nameWithLiquid} x{stackCount}" : nameWithLiquid;
                int finalWeight = Math.Max(1, weight * weightMultiplierForLog);
                var sourceLabel = string.IsNullOrEmpty(sourceForLog) ? "" : $" source={sourceForLog}";
                LegacyItemOptions.LogDebug($"    Weight calc for '{displayLabel}': Tier:{tier}²×10={tier * tier * 10}, Complexity:{complexity}²×10={complexity * complexity * 10}, Value:{commerceValue}×{stackCount}={adjustedCommerceValue}, Stack:{stackCount}× → base {weight}, mult={weightMultiplierForLog} → final {finalWeight}{sourceLabel}");
            }

            return weight;
        }

        /// <summary>
        /// Determines if an item is eligible for the legacy pool.
        /// Excludes: non-removable items (natural weapons, mutation-granted gear) and cursed items that are currently equipped.
        /// </summary>
        public static bool IsItemEligible(GameObject item, bool logDetails = false)
        {
            if (item == null)
                return false;

            string itemName = item.DisplayNameOnlyDirect ?? "Unknown";

            // Exclude natural weapons and body parts (non-removable)
            if (item.HasPart("NaturalWeapon") || item.HasPart("BodyPart"))
            {
                if (logDetails)
                    LegacyItemOptions.LogDebug($"    EXCLUDED '{itemName}': natural weapon/body part");
                return false;
            }

            // Exclude mutation-granted items (e.g., quills from Quills mutation)
            if (item.HasPart("NaturalEquipment"))
            {
                if (logDetails)
                    LegacyItemOptions.LogDebug($"    EXCLUDED '{itemName}': mutation-granted natural equipment");
                return false;
            }

            // Exclude cursed items (equipped and cannot be removed)
            if (item.HasPart("Cursed") && item.Equipped != null)
            {
                if (logDetails)
                    LegacyItemOptions.LogDebug($"    EXCLUDED '{itemName}': cursed and equipped");
                return false;
            }

            return true;
        }

        private static List<WeightedItem> BuildCandidates(List<GameObject> items, string source, bool isEquipped)
        {
            var weightedItems = new List<WeightedItem>();

            if (items == null || items.Count == 0)
                return weightedItems;

            foreach (var item in items)
            {
                if (IsItemEligible(item, logDetails: true))
                {
                    int stackCount = GetStackCount(item);
                    string liquidSuffix = GetLiquidSuffix(item);
                    var baseWeight = CalculateItemWeight(item, logDetails: true, weightMultiplierForLog: isEquipped ? 2 : 1, sourceForLog: source);
                    if (baseWeight > 0)
                    {
                        int multiplier = isEquipped ? 2 : 1;
                        int finalWeight = Math.Max(1, baseWeight * multiplier);
                        weightedItems.Add(new WeightedItem
                        {
                            Item = item,
                            BaseWeight = baseWeight,
                            Weight = finalWeight,
                            StackCount = stackCount,
                            LiquidSuffix = liquidSuffix,
                            Source = source,
                            WeightMultiplier = multiplier,
                            IsEquipped = isEquipped
                        });
                    }
                }
            }

            return weightedItems;
        }

        /// <summary>
        /// Selects a single weighted item from the provided list, applying the equipped multiplier when specified.
        /// </summary>
        public static GameObject SelectSingleWeightedItem(List<GameObject> items, string source, bool isEquipped)
        {
            var candidates = BuildCandidates(items, source, isEquipped);
            var picked = PickWeightedItem(candidates, source);
            return picked?.Item;
        }

        /// <summary>
        /// Selects one item from weighted candidates.
        /// </summary>
        private static WeightedItem PickWeightedItem(List<WeightedItem> weightedItems, string category, Func<int, int, int> randomFunc = null)
        {
            if (weightedItems == null || weightedItems.Count == 0)
            {
                LegacyItemOptions.LogDebug($"  PickWeightedItem({category}): no candidates provided");
                return null;
            }

            if (weightedItems.Count == 0)
            {
                LegacyItemOptions.LogDebug($"  PickWeightedItem({category}): no eligible items after filtering");
                return null;
            }

            // Calculate total weight
            int totalWeight = weightedItems.Sum(wi => wi.Weight);

            LegacyItemOptions.LogDebug($"  PickWeightedItem({category}): {weightedItems.Count} eligible items, total weight = {totalWeight}");

            // Log all candidates with their weights and selection probabilities
            foreach (var wi in weightedItems)
            {
                double probability = (double)wi.Weight / totalWeight * 100;
                var nameWithLiquid = $"{wi.Item?.DisplayNameOnlyDirect ?? "<null>"}{wi.LiquidSuffix}";
                var nameWithStack = wi.StackCount > 1 ? $"{nameWithLiquid} x{wi.StackCount}" : nameWithLiquid;
                LegacyItemOptions.LogDebug($"    Candidate: '{nameWithStack}' source={wi.Source} base={wi.BaseWeight}×{wi.WeightMultiplier}=final {wi.Weight} (stack x{wi.StackCount}, {probability:F1}% chance)");
            }

            var rng = randomFunc ?? XRL.Rules.Stat.Random;

            // Select random value in range [0, totalWeight)
            int randomValue = rng(0, totalWeight - 1);

            // Find the item corresponding to this random value
            int cumulativeWeight = 0;
            foreach (var weightedItem in weightedItems)
            {
                cumulativeWeight += weightedItem.Weight;
                if (randomValue < cumulativeWeight)
                {
                    var nameWithLiquid = $"{weightedItem.Item?.DisplayNameOnlyDirect ?? "<null>"}{weightedItem.LiquidSuffix}";
                    LegacyItemOptions.LogDebug($"  PickWeightedItem({category}): rolled {randomValue}/{totalWeight - 1}, selected '{nameWithLiquid}'");
                    return weightedItem;
                }
            }

            // Fallback (should never reach here, but return last item)
            var fallback = weightedItems.Last();
            var fallbackName = $"{fallback.Item?.DisplayNameOnlyDirect ?? "<null>"}{fallback.LiquidSuffix}";
            LegacyItemOptions.LogDebug($"  PickWeightedItem({category}): fallback to '{fallbackName}'");
            return fallback;
        }

        /// <summary>
        /// Gets all equipped items (excluding natural weapons and body parts),
        /// including installed cybernetic implants.
        /// </summary>
        public static List<GameObject> GetEquippedItems(GameObject character)
        {
            var equippedItems = new List<GameObject>();
            bool AddUnique(GameObject obj)
            {
                if (obj == null)
                    return false;
                if (equippedItems.Any(existing => ReferenceEquals(existing, obj)))
                    return false;
                equippedItems.Add(obj);
                return true;
            }

            if (character == null || !character.HasPart("Body"))
                return equippedItems;

            var body = character.GetPart<Body>();
            if (body == null)
                return equippedItems;

            // Iterate through all body parts and get equipped items
            foreach (var bodyPart in body.GetParts())
            {
                AddUnique(bodyPart.Equipped);
            }

            // Also include installed cybernetic implants
            var cybernetics = body.GetInstalledCybernetics();
            if (cybernetics != null)
            {
                foreach (var implant in cybernetics)
                {
                    AddUnique(implant);
                }
            }

            return equippedItems;
        }

        /// <summary>
        /// Gets all inventory items (not equipped).
        /// </summary>
        public static List<GameObject> GetInventoryItems(GameObject character)
        {
            var inventoryItems = new List<GameObject>();

            if (character == null || !character.HasPart("Inventory"))
                return inventoryItems;

            var inventory = character.GetPart<Inventory>();
            if (inventory == null)
                return inventoryItems;

            var body = character.GetPart<Body>();
            var equippedObjects = new List<GameObject>();

            // Build list of currently equipped items
            if (body != null)
            {
                foreach (var bodyPart in body.GetParts())
                {
                    if (bodyPart.Equipped != null)
                    {
                        equippedObjects.Add(bodyPart.Equipped);
                    }
                }
            }

            // Get all items that are not equipped
            foreach (var item in inventory.Objects)
            {
                if (item != null && !equippedObjects.Contains(item))
                {
                    inventoryItems.Add(item);
                }
            }

            return inventoryItems;
        }

        /// <summary>
        /// Represents a selected item with its source.
        /// </summary>
        public class SelectedItem
        {
            public GameObject Item { get; set; }
            public string Source { get; set; }
        }

        internal static List<WeightedItem> SelectChainedWeightedItems(List<WeightedItem> weightedItems, Func<int, int, int> randomFunc = null, Func<double> procRollFunc = null)
        {
            var results = new List<WeightedItem>();

            if (weightedItems == null || weightedItems.Count == 0)
            {
                return results;
            }

            var rng = randomFunc ?? XRL.Rules.Stat.Random;
            var procRoll = procRollFunc ?? (() => XRL.Rules.Stat.Random(0, 1_000_000) / 1_000_000.0);

            int attempt = 1;
            var remaining = new List<WeightedItem>(weightedItems);

            while (remaining.Count > 0)
            {
                double chance = Math.Pow(ProcContinuationChance, attempt - 1);
                double roll = procRoll();
                LegacyItemOptions.LogDebug($"  Proc {attempt}: chance={chance * 100:F2}% roll={roll * 100:F4}%");

                if (roll > chance)
                {
                    LegacyItemOptions.LogDebug($"  Proc {attempt}: failed (roll {roll * 100:F4}% > {chance * 100:F2}%), stopping chain");
                    break;
                }

                var picked = PickWeightedItem(remaining, $"CHAIN-{attempt}", rng);
                if (picked == null)
                {
                    LegacyItemOptions.LogDebug($"  Proc {attempt}: no eligible items remain, stopping chain");
                    break;
                }

                var pickedName = $"{picked.Item?.DisplayNameOnlyDirect ?? "<null>"}{picked.LiquidSuffix}";
                LegacyItemOptions.LogDebug($"  Proc {attempt}: success -> '{pickedName}' (source={picked.Source})");
                results.Add(picked);

                remaining = remaining.Where(w => !ReferenceEquals(w.Item, picked.Item)).ToList();
                attempt++;
            }

            if (remaining.Count == 0)
            {
                LegacyItemOptions.LogDebug("  Chain ended because the pool is empty.");
            }

            LegacyItemOptions.LogDebug($"  Chain produced {results.Count} item(s).");
            return results;
        }

        /// <summary>
        /// Selects items from a character from a merged pool of equipped + inventory.
        /// Equipped items have their weight doubled. Selection chains with diminishing proc chance
        /// and is without replacement. The chain stops on the first failed proc.
        /// </summary>
        public static List<SelectedItem> SelectLegacyItems(GameObject character)
        {
            LegacyItemOptions.LogDebug("=== LEGACY ITEM SELECTION ===");

            var equippedItems = GetEquippedItems(character);
            var inventoryItems = GetInventoryItems(character);

            LegacyItemOptions.LogDebug($"Found {equippedItems.Count} equipped items, {inventoryItems.Count} inventory items");

            var candidates = new List<WeightedItem>();
            candidates.AddRange(BuildCandidates(equippedItems, "EQUIPPED", isEquipped: true));
            candidates.AddRange(BuildCandidates(inventoryItems, "INVENTORY", isEquipped: false));

            if (candidates.Count == 0)
            {
                LegacyItemOptions.LogDebug("No eligible items found for selection");
                return new List<SelectedItem>();
            }

            LegacyItemOptions.LogDebug($"Merged candidate pool: {candidates.Count} items");

            var picks = SelectChainedWeightedItems(candidates);

            var summary = picks.Count == 0
                ? "none"
                : string.Join(", ", picks.Select((p, i) => $"{i + 1}. '{p.Item?.DisplayNameOnlyDirect ?? "null"}' ({p.Source})"));
            LegacyItemOptions.LogDebug($"=== SELECTION RESULT: {picks.Count} item(s) -> {summary} ===");

            SelectedItem Convert(WeightedItem wi) => wi == null ? null : new SelectedItem { Item = wi.Item, Source = wi.Source };

            return picks.Select(Convert).Where(p => p != null).ToList();
        }
    }
}
