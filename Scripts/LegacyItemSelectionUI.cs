using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using XRL;
using XRL.UI;
using XRL.World;

namespace LegacyItems
{
    /// <summary>
    /// Handles the UI display for legacy item selection during character creation.
    /// Presents items with color-coded details and chronological ordering.
    /// </summary>
    public static class LegacyItemSelectionUI
    {
        // Color codes for UI display
        private const string COLOR_ARTIFACT = "&M";  // Magenta for artifacts
        private const string COLOR_REGULAR = "&y";   // White for regular items
        private const string COLOR_TIER = "&K";      // Gray for tier
        private const string COLOR_RESET = "&y";     // Reset to default

        /// <summary>
        /// Shows the legacy item selection menu and returns the selected item ID.
        /// </summary>
        /// <param name="pool">The pool of legacy items to display</param>
        /// <returns>The selected item ID, or null if player refuses all</returns>
        public static string ShowSelectionMenu(List<LegacyPoolManager.LegacyItemEntry> pool)
        {
            if (pool == null || pool.Count == 0)
            {
                return null;
            }

            // Build menu options
            var menuOptions = new List<string>();
            var itemMap = new Dictionary<int, string>();

            // Add items in chronological order (oldest to newest)
            for (int i = 0; i < pool.Count; i++)
            {
                var entry = pool[i];
                menuOptions.Add(FormatItemForMenu(entry));
                itemMap[i] = entry.ID;
            }

            // Add "refuse all" option
            menuOptions.Add("{{rules|Choose nothing}}");
            int refuseIndex = menuOptions.Count - 1;

            // Determine subtitle based on pool size
            string subtitle = $"Vault currently holds {pool.Count} (of up to {LegacyItemOptions.MaxPoolSize}) relics. Select a relic from a past life to bring with you on your journey:";

            // Build intro lines
            var introLines = new List<string> { subtitle };

            // Add pool size if option is enabled
            if (LegacyItemOptions.ShowPoolSize)
            {
                string poolSizeText = pool.Count == 1
                    ? "&K1 legacy item available&y"
                    : $"&K{pool.Count} legacy items available&y";
                introLines.Add(poolSizeText);
            }

            introLines.Add("");
            introLines.Add("&KUnchosen items await your return.&y");

            // Show menu
            int selection = Popup.PickOption(
                Title: "Legacy Items",
                Options: menuOptions.ToArray(),
                Intro: string.Join("\n", introLines),
                AllowEscape: false
            );

            // Handle selection
            if (selection < 0 || selection == refuseIndex)
            {
                return null; // Player refused all
            }

            return itemMap.TryGetValue(selection, out string itemId) ? itemId : null;
        }

        /// <summary>
        /// Formats a legacy item entry for menu display with color coding.
        /// </summary>
        private static string FormatItemForMenu(LegacyPoolManager.LegacyItemEntry entry)
        {
            var sb = new StringBuilder();

            // Item name with artifact coloring
            string nameColor = entry.IsArtifact ? COLOR_ARTIFACT : COLOR_REGULAR;
            sb.Append($"{nameColor}{entry.DisplayName}{COLOR_RESET}");

            // Tier (if > 0) - important for item quality
            if (entry.Tier > 0)
            {
                sb.Append($" {COLOR_TIER}(T{entry.Tier}){COLOR_RESET}");
            }

            // Additional properties (always shown as they affect item functionality)
            var properties = new List<string>();

            if (!string.IsNullOrEmpty(entry.StackSize) && entry.StackSize != "1")
            {
                properties.Add($"x{entry.StackSize}");
            }

            if (!string.IsNullOrEmpty(entry.Charges))
            {
                properties.Add($"{entry.Charges} charges");
            }

            if (!string.IsNullOrEmpty(entry.LiquidType))
            {
                properties.Add($"{entry.LiquidDrams} drams of {entry.LiquidType}");
            }

            if (properties.Count > 0)
            {
                sb.Append($" &K({string.Join(", ", properties)})&y");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Gets a formatted display of all items in the pool for debugging.
        /// </summary>
        public static string GetPoolSummary(List<LegacyPoolManager.LegacyItemEntry> pool)
        {
            if (pool == null || pool.Count == 0)
            {
                return "No legacy items in pool.";
            }

            var sb = new StringBuilder();
            sb.AppendLine($"Legacy Pool ({pool.Count} items):");
            sb.AppendLine();

            for (int i = 0; i < pool.Count; i++)
            {
                sb.AppendLine($"{i + 1}. {FormatItemForMenu(pool[i])}");
            }

            return sb.ToString();
        }
    }
}
