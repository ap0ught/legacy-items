using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using XRL;
using XRL.CharacterBuilds;
using XRL.CharacterBuilds.UI;
using XRL.UI;
using XRL.UI.Framework;
using XRL.World;
using ConsoleColorUtility = ConsoleLib.Console.ColorUtility;

namespace LegacyItems
{
    /// <summary>
    /// Embark builder window for legacy item selection.
    /// Displays items in a horizontal scroller matching the game's character creation UI.
    /// </summary>
    [UIView("LegacyItems:Selection", NavCategory = "Menu", UICanvas = "Chargen/LegacyItems", UICanvasHost = 1)]
    public class LegacyItemEmbarkWindow : EmbarkBuilderModuleWindowPrefabBase<LegacyItemEmbarkModule, HorizontalScroller>
    {
        private const string LegacyItemsViewId = "Chargen/LegacyItems";
        private bool _hasLoggedSelections;

        private void OnEnable()
        {
            _hasLoggedSelections = false;
        }

        /// <summary>
        /// Always has a valid selection - "Start Fresh" is the default.
        /// </summary>
        public override bool HasSelection()
        {
            // Always has a selection - either an item or "Start Fresh" (RefuseAll)
            return true;
        }

        /// <summary>
        /// Gets the breadcrumb for this window in the character creation flow.
        /// </summary>
        public override UIBreadcrumb GetBreadcrumb()
        {
            var crumb = new UIBreadcrumb
            {
                Id = "LegacyItems",
                Title = "Inheritance",
                IconPath = UIBreadcrumb.DEFAULT_ICON,
                IconForegroundColor = ConsoleColorUtility.ColorMap['M'],
                IconDetailColor = Color.clear,
            };

            // If an item is selected, show its info in the breadcrumb
            var selectedId = module?.data?.SelectedItemId;
            if (!string.IsNullOrEmpty(selectedId))
            {
                var entry = module.Pool.FirstOrDefault(e => e.ID == selectedId);
                if (entry != null)
                {
                    crumb.Title = FormatItemTitle(entry);
                    crumb.IconPath = GetItemIcon(entry);
                    crumb.IconForegroundColor = GetItemForegroundColor(entry);
                    crumb.IconDetailColor = GetItemDetailColor(entry);
                }
            }

            return crumb;
        }

        /// <summary>
        /// Handle random selection - pick a random item from pool.
        /// </summary>
        public override void RandomSelection()
        {
            if (module.Pool.Count > 0)
            {
                // Select a random item from the pool directly via module
                int randomIndex = XRL.Rules.Stat.Random(0, module.Pool.Count - 1);
                var randomItem = module.Pool[randomIndex];
                module.SelectItem(randomItem.ID);
                LegacyItemOptions.LogDebug($"LegacyItems: RandomSelection -> {randomItem.DisplayName}");

                // Update the visual selection to match
                // Items are displayed in reverse order (most recent first), so convert the index
                int visualIndex = module.Pool.Count - 1 - randomIndex;

                // Use scrollContext.SelectIndex to properly update the UI selection
                prefabComponent.scrollContext.SelectIndex(visualIndex);
                prefabComponent.ScrollSelectedIntoView();
            }
            else
            {
                module.RefuseAll();
                // Visual selection should be "RefuseAll" which is the only item when pool is empty
                prefabComponent.scrollContext.SelectIndex(0);
            }
        }

        /// <summary>
        /// Handle quickstart - pick a random item from pool.
        /// </summary>
        public override void DebugQuickstart(string type)
        {
            // Quickstart should select a random item
            RandomSelection();
        }

        /// <summary>
        /// Get the selection choices to display.
        /// </summary>
        public IEnumerable<ChoiceWithColorIcon> GetSelections()
        {
            var pool = module.Pool;

            if (pool.Count == 0)
            {
                // Empty pool - show a message option
                yield return new ChoiceWithColorIcon
                {
                    Id = "Empty",
                    Title = "No inheritance",
                    IconPath = "Items/sw_grave.bmp",
                    IconForegroundColor = ConsoleColorUtility.ColorMap['K'],
                    IconDetailColor = ConsoleColorUtility.ColorMap['k'],
                    Description = "The vaults of your line lie empty.\nRelics are gathered from the deaths of your line.",
                    Chosen = x => false
                };
            }
            else
            {
                // Add each item in the pool (reversed so most recent items are first/left)
                for (int i = pool.Count - 1; i >= 0; i--)
                {
                    var entry = pool[i];
                    yield return new ChoiceWithColorIcon
                    {
                        Id = entry.ID,
                        Title = FormatItemTitle(entry),
                        IconPath = GetItemIcon(entry),
                        IconForegroundColor = GetItemForegroundColor(entry),
                        IconDetailColor = GetItemDetailColor(entry),
                        Description = FormatItemDescription(entry),
                        Chosen = x => x.Id == module.data?.SelectedItemId
                    };
                }
            }

            // Always add "refuse all" option - this is the default selection
            yield return new ChoiceWithColorIcon
            {
                Id = "RefuseAll",
                Title = "Inherit nothing",
                IconPath = "creatures/sw_sowerseed.bmp",
                IconForegroundColor = ConsoleColorUtility.ColorMap['W'],
                IconDetailColor = ConsoleColorUtility.ColorMap['w'],
                Description = "Begin your journey without a keepsake from the past.\nRelics you leave behind will await the scrutiny of your successors.",
                Chosen = x => module.data == null || string.IsNullOrEmpty(module.data.SelectedItemId)
            };
        }

        /// <summary>
        /// Show vault capacity in the key menu bar so it is always visible.
        /// </summary>
        public override IEnumerable<MenuOption> GetKeyMenuBar()
        {
            var current = module?.Pool?.Count ?? 0;
            var maximum = LegacyItemOptions.MaxPoolSize;

            yield return new MenuOption
            {
                Id = null,
                InputCommand = "",
                KeyDescription = null,
                Description = $"{{{{y|Vault currently holds {current} (of up to {maximum}) relics}}}}"
            };

            foreach (var item in base.GetKeyMenuBar())
            {
                yield return item;
            }
        }

        /// <summary>
        /// Handle item selection.
        /// </summary>
        public void OnItemSelected(FrameworkDataElement choice)
        {
            if (choice.Id == "Empty")
            {
                // Empty pool - go back
                module.builder.back();
                return;
            }

            if (choice.Id == "RefuseAll")
            {
                module.RefuseAll();
            }
            else
            {
                module.SelectItem(choice.Id);
            }

            // Advance to next screen
            module.builder.advance();
        }

        /// <summary>
        /// Called before the window is shown.
        /// </summary>
        public override void BeforeShow(EmbarkBuilderModuleWindowDescriptor descriptor)
        {
            prefabComponent.autoHotkey = true;
            prefabComponent.onSelected.RemoveAllListeners();
            prefabComponent.onSelected.AddListener(OnItemSelected);
            var selections = GetSelections().ToList();
            LogMenuSelectionsOnce(selections);
            prefabComponent.BeforeShow(descriptor, selections);
            base.BeforeShow(descriptor);
        }

        /// <summary>
        /// Handle key input for discarding items.
        /// </summary>
        public void Update()
        {
            // Only process when this window is active and visible
            if (!gameObject.activeInHierarchy)
                return;

            // Only handle input when this window is the active builder screen
            if (!IsActiveLegacySelection())
                return;

            // Check for Delete or Backspace key
            if (UnityEngine.Input.GetKeyDown(KeyCode.Delete) || UnityEngine.Input.GetKeyDown(KeyCode.Backspace))
            {
                TryDiscardHighlightedItem();
            }
        }

        /// <summary>
        /// Attempts to discard the currently highlighted item.
        /// </summary>
        private async void TryDiscardHighlightedItem()
        {
            // Get the currently highlighted item from the scroller
            if (prefabComponent == null || prefabComponent.choices == null || prefabComponent.choices.Count == 0)
                return;

            var selectedIndex = prefabComponent.selectedPosition;
            if (selectedIndex < 0 || selectedIndex >= prefabComponent.choices.Count)
                return;

            var highlightedChoice = prefabComponent.choices[selectedIndex];
            if (highlightedChoice == null)
                return;

            // Can't discard special options
            if (highlightedChoice.Id == "Empty" || highlightedChoice.Id == "RefuseAll")
                return;

            // Find the entry in the pool
            var entry = module.Pool.FirstOrDefault(e => e.ID == highlightedChoice.Id);
            if (entry == null)
                return;

            // Show lore-friendly confirmation dialog
            var itemName = FormatItemTitle(entry);
            var result = await Popup.ShowYesNoAsync(
                $"Discard {{{{M|{itemName}}}}}?\n\n" +
                "{{r|This item will be erased from the legacy of your line.}}\n" +
                "{{K|It will become permanently unavailable as an option again in this or future runs.}}");

            if (result == DialogResult.Yes)
            {
                LegacyItemOptions.LogDebug($"LegacyItems: Consigned '{entry.DisplayName}' to oblivion");

                // Remove from pool
                if (module.ConsignToOblivion(highlightedChoice.Id))
                {
                    // Refresh the UI
                    prefabComponent.BeforeShow(null, GetSelections());

                    // Show brief confirmation
                    await Popup.ShowAsync($"{{{{M|{itemName}}}}} {{{{K|is unmade.}}}}");
                }
            }
        }

        private void LogMenuSelectionsOnce(List<ChoiceWithColorIcon> selections)
        {
            if (_hasLoggedSelections)
                return;

            if (selections == null || selections.Count == 0)
            {
                _hasLoggedSelections = true;
                return;
            }

            var labeledSelections = selections
                .Select((choice, index) => $"{index + 1}. {choice?.Title ?? "<null>"}")
                .ToList();

            LegacyItemOptions.LogDebug($"LegacyItems: Selection menu entries (left-to-right): {string.Join(" | ", labeledSelections)}");
            _hasLoggedSelections = true;
        }

        /// <summary>
        /// Get the icon path for an item.
        /// </summary>
        private string GetItemIcon(LegacyPoolManager.LegacyItemEntry entry)
        {
            // Use stored tile if available
            if (!string.IsNullOrEmpty(entry.Tile))
                return entry.Tile;

            // Try to get tile from the blueprint
            try
            {
                if (GameObjectFactory.Factory.Blueprints.TryGetValue(entry.Blueprint, out var bp))
                {
                    var tile = bp.GetPartParameter<string>("Render", "Tile");
                    if (!string.IsNullOrEmpty(tile))
                        return tile;
                }
            }
            catch { }

            // Fallback icons based on item type
            if (entry.IsArtifact)
                return "Items/sw_artifact.bmp";
            if (entry.Blueprint.Contains("Melee") || entry.Blueprint.Contains("Sword") || entry.Blueprint.Contains("Axe"))
                return "Items/sw_sword.bmp";
            if (entry.Blueprint.Contains("Missile") || entry.Blueprint.Contains("Gun") || entry.Blueprint.Contains("Rifle"))
                return "Items/sw_gun.bmp";

            return "Items/sw_chest.bmp";
        }

        /// <summary>
        /// Get the foreground color for an item's tile icon.
        /// </summary>
        private Color GetItemForegroundColor(LegacyPoolManager.LegacyItemEntry entry)
        {
            // Use stored TileColor if available
            // TileColor format is like "&C" or "&C^k" - extract the foreground char after &
            var colorCode = ExtractColorChar(entry.TileColor);
            if (colorCode.HasValue && ConsoleColorUtility.ColorMap.ContainsKey(colorCode.Value))
                return ConsoleColorUtility.ColorMap[colorCode.Value];

            // Try to get from blueprint if not stored
            try
            {
                if (GameObjectFactory.Factory.Blueprints.TryGetValue(entry.Blueprint, out var bp))
                {
                    var tileColor = bp.GetPartParameter<string>("Render", "TileColor")
                                    ?? bp.GetPartParameter<string>("Render", "ColorString");
                    colorCode = ExtractColorChar(tileColor);
                    if (colorCode.HasValue && ConsoleColorUtility.ColorMap.ContainsKey(colorCode.Value))
                        return ConsoleColorUtility.ColorMap[colorCode.Value];
                }
            }
            catch { }

            // Fallback: magenta for artifacts, grey for regular items
            return entry.IsArtifact ? ConsoleColorUtility.ColorMap['M'] : ConsoleColorUtility.ColorMap['y'];
        }

        /// <summary>
        /// Get the detail color for an item's tile icon.
        /// </summary>
        private Color GetItemDetailColor(LegacyPoolManager.LegacyItemEntry entry)
        {
            // DetailColor is just a single char like "g" or "r"
            if (!string.IsNullOrEmpty(entry.DetailColor) && entry.DetailColor.Length >= 1)
            {
                var colorCode = entry.DetailColor[0];
                if (ConsoleColorUtility.ColorMap.ContainsKey(colorCode))
                    return ConsoleColorUtility.ColorMap[colorCode];
            }

            // Try to get from blueprint if not stored
            try
            {
                if (GameObjectFactory.Factory.Blueprints.TryGetValue(entry.Blueprint, out var bp))
                {
                    var detailColor = bp.GetPartParameter<string>("Render", "DetailColor");
                    if (!string.IsNullOrEmpty(detailColor) && detailColor.Length >= 1)
                    {
                        var colorCode = detailColor[0];
                        if (ConsoleColorUtility.ColorMap.ContainsKey(colorCode))
                            return ConsoleColorUtility.ColorMap[colorCode];
                    }
                }
            }
            catch { }

            // Fallback to dark background color
            return ConsoleColorUtility.ColorMap['k'];
        }

        /// <summary>
        /// Extract the foreground color character from a color string like "&C" or "&C^k".
        /// </summary>
        private char? ExtractColorChar(string colorString)
        {
            if (string.IsNullOrEmpty(colorString))
                return null;

            // Look for & followed by a color char
            int idx = colorString.IndexOf('&');
            if (idx >= 0 && idx + 1 < colorString.Length)
                return colorString[idx + 1];

            // If no &, maybe it's just a single char
            if (colorString.Length == 1)
                return colorString[0];

            return null;
        }

        /// <summary>
        /// Format the title for an item entry, including container contents.
        /// </summary>
        private string FormatItemTitle(LegacyPoolManager.LegacyItemEntry entry)
        {
            var baseName = entry?.DisplayName;

            // Fall back to base display name or blueprint if needed
            if (string.IsNullOrEmpty(baseName))
            {
                baseName = !string.IsNullOrEmpty(entry?.BaseDisplayName)
                    ? entry.BaseDisplayName
                    : entry?.Blueprint ?? "Unknown item";
            }

            return $"†{baseName}";
        }

        /// <summary>
        /// Format the description for an item entry.
        /// </summary>
        private string FormatItemDescription(LegacyPoolManager.LegacyItemEntry entry)
        {
            var itemName = FormatItemTitle(entry);
            string description;

            // Preseed items have a different description - they're starter items, not from a character
            if (entry.Source == "PRESEED" || string.IsNullOrEmpty(entry.CharacterName))
            {
                description = $"{itemName} {{{{y|left behind by a nameless predecessor}}}}.";
            }
            else
            {
                // Format: "<Full item name> once carried/equipped by <Name>, who <death reason>."
                var actionText = entry.Source == "EQUIPPED" ? "once equipped by" : "once carried by";
                description = $"{itemName} {{{{y|{actionText}}}}} {{{{C|{entry.CharacterName}}}}}";

                // Add death reason if available and meaningful
                var formattedReason = LegacyItemText.FormatDeathReason(entry.DeathReason);
                if (!string.IsNullOrEmpty(formattedReason))
                {
                    description += $", who {{{{r|{formattedReason}}}}}";
                }

                description += ".";
            }

            // Add hint for permanent removal
            description += "\n{{K|[Del] Discard}}";

            return description;
        }

        /// <summary>
        /// Returns true if the embark builder is currently showing the legacy item selection view.
        /// </summary>
        private bool IsActiveLegacySelection()
        {
            var activeWindow = module?.builder?.activeWindow;
            if (activeWindow == null)
                return false;

            // Check by direct window reference or view ID match
            return activeWindow.window == this || activeWindow.viewID == LegacyItemsViewId;
        }
    }
}
