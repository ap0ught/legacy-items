using System;
using System.IO;
using System.Xml;
using XRL;
using XRL.UI;

namespace LegacyItems
{
    /// <summary>
    /// Helper class for accessing legacy items mod options.
    /// All options are read from the game's options system via Options.GetOption().
    /// Options are defined in LegacyItemsOptions.xml.
    /// </summary>
    public static class LegacyItemOptions
    {
        private const int DefaultMaxPoolSize = 25;
        private const string OptionsFileName = "LegacyItemsOptions.xml";
        private static readonly Lazy<int> FixedMaxPoolSize = new Lazy<int>(LoadFixedMaxPoolSize);

        /// <summary>
        /// Whether to display the number of available legacy items before showing the selection menu.
        /// Default: true (Yes)
        /// </summary>
        public static bool ShowPoolSize => true;

        /// <summary>
        /// Whether to automatically skip the selection menu when the pool is empty.
        /// Default: true (Yes)
        /// </summary>
        public static bool AutoRefuseEmpty => false;

        /// <summary>
        /// Whether to populate new pools with 8 preseed starter items.
        /// Only affects new pools, not existing ones.
        /// Default: true (Yes)
        /// </summary>
        public static bool EnablePreseed =>
            Options.GetOption("Option_LegacyItems_EnablePreseed", "Yes") == "Yes";

        /// <summary>
        /// Maximum number of items to keep in the pool.
        /// When the pool exceeds this limit, the oldest items are removed.
        /// Default: 25
        /// </summary>
        public static int MaxPoolSize => FixedMaxPoolSize.Value;

        /// <summary>
        /// Whether to enable verbose debug logging to Player.log.
        /// Useful for troubleshooting issues with legacy item collection and granting.
        /// Default: false (No)
        /// </summary>
        public static bool DebugLogging =>
            Options.GetOption("Option_LegacyItems_DebugLogging", "No") == "Yes";

        /// <summary>
        /// Logs a debug message if debug logging is enabled.
        /// </summary>
        public static void LogDebug(string message)
        {
            try
            {
                if (DebugLogging)
                {
                    MetricsManager.LogInfo(message);
                }
            }
            catch (Exception)
            {
                // Swallow logging failures in environments without game options (e.g., test runner)
            }
        }

        /// <summary>
        /// Logs an error message (always logged regardless of debug setting).
        /// </summary>
        public static void LogError(string message)
        {
            MetricsManager.LogError(message);
        }

        private static int LoadFixedMaxPoolSize()
        {
            try
            {
                var mod = ModManager.GetMod(typeof(LegacyItemOptions).Assembly);
                var modPath = mod?.Path;
                if (string.IsNullOrEmpty(modPath))
                    return DefaultMaxPoolSize;

                var optionsPath = Path.Combine(modPath, OptionsFileName);
                if (!File.Exists(optionsPath))
                    return DefaultMaxPoolSize;

                var document = new XmlDocument();
                document.Load(optionsPath);
                var value = document.DocumentElement?.GetAttribute("maxPoolSize");
                if (int.TryParse(value, out var parsed) && parsed >= 1)
                    return parsed;

                LogDebug($"LegacyItems: Invalid maxPoolSize '{value}' in {OptionsFileName}; using default {DefaultMaxPoolSize}.");
            }
            catch (Exception ex)
            {
                LogDebug($"LegacyItems: Failed to read {OptionsFileName}; using default {DefaultMaxPoolSize}. Error: {ex.Message}");
            }

            return DefaultMaxPoolSize;
        }
    }
}
