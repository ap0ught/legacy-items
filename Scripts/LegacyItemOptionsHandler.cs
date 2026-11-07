using System;
using System.Threading.Tasks;
using XRL.UI;

namespace LegacyItems
{
    /// <summary>
    /// Handles option actions for the Legacy Items mod.
    /// </summary>
    public static class LegacyItemOptionsHandler
    {
        /// <summary>
        /// Called when the "Clear Legacy Pool" button is clicked in options.
        /// Shows a confirmation dialog and clears the pool if confirmed.
        /// </summary>
        public static async Task<bool> ClearPoolClick()
        {
            var pool = LegacyPoolManager.LoadPool();
            int count = pool.Count;

            if (count == 0)
            {
                await Popup.ShowAsync("The legacy pool is already empty.");
                return true;
            }

            var result = await Popup.ShowYesNoAsync(
                $"Are you sure you want to clear all {count} items from the legacy pool?\n\nThis cannot be undone.");

            if (result == DialogResult.Yes)
            {
                LegacyPoolManager.ClearPool();
                await Popup.ShowAsync("Legacy item pool is now empty.");
            }

            return true;
        }

        /// <summary>
        /// Called when the "Restock preseed items" button is clicked in options.
        /// Adds the preseed items back into the pool, respecting the max size policy.
        /// </summary>
        public static async Task<bool> RestockPreseedClick()
        {
            try
            {
                LegacyPoolManager.RestockPreseedItems();
                await Popup.ShowAsync("Preseed items have been restocked.");
            }
            catch (Exception ex)
            {
                await Popup.ShowAsync($"Failed to restock preseed items: {ex.Message}");
            }

            return true;
        }
    }
}
