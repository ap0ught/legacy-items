using System;
using System.Collections.Generic;

namespace LegacyItems
{
    /// <summary>
    /// Shared text helpers for legacy item formatting.
    /// </summary>
    public static class LegacyItemText
    {
        /// <summary>
        /// Formats a death reason string for use in UI strings.
        /// Removes trailing punctuation and normalizes player-centric phrasing.
        /// </summary>
        public static string FormatDeathReason(string reason)
        {
            if (string.IsNullOrEmpty(reason))
                return null;

            var text = reason.Trim();

            // Remove trailing period
            if (text.EndsWith("."))
                text = text.Substring(0, text.Length - 1);

            // Remove bracketed annotations like "[swimming]"
            var bracketIdx = text.IndexOf('[');
            if (bracketIdx > 0)
                text = text.Substring(0, bracketIdx).Trim();

            // Skip non-meaningful death reasons
            if (text.Equals("You quit", StringComparison.OrdinalIgnoreCase))
                return null;

            // Remove leading "You "
            if (text.StartsWith("You ", StringComparison.OrdinalIgnoreCase))
                text = text.Substring(4);

            // "were killed by X" → "was killed by X"
            if (text.StartsWith("were ", StringComparison.OrdinalIgnoreCase))
                text = "was " + text.Substring(5);

            // "Killed by X" → "was killed by X" (when reason starts directly with "Killed")
            if (text.StartsWith("Killed ", StringComparison.OrdinalIgnoreCase))
                text = "was k" + text.Substring(1);

            return text;
        }

        /// <summary>
        /// Builds the legacy note text to attach to a granted item.
        /// Format: "Once equipped by {{C|Name}}, who was killed by X."
        /// </summary>
        public static string BuildLegacyNote(string characterName, string source, string deathReason)
        {
            if (string.IsNullOrEmpty(characterName) || characterName == "Preseed")
                return null;

            var action = source == "EQUIPPED" ? "used" : "carried";
            var formattedDeath = FormatDeathReason(deathReason);

            if (!string.IsNullOrEmpty(formattedDeath))
                return $"Once {action} by {{{{C|{characterName}}}}}, who {formattedDeath}.";

            return $"Once {action} by {{{{C|{characterName}}}}}, now yours to bear.";
        }

        /// <summary>
        /// Attaches the legacy note to an item using the Notes part.
        /// </summary>
        public static void ApplyLegacyNote(XRL.World.GameObject item, string characterName, string source, string deathReason)
        {
            if (item == null)
                return;

            var noteText = BuildLegacyNote(characterName, source, deathReason);
            if (string.IsNullOrEmpty(noteText))
                return;

            var notes = item.RequirePart<XRL.World.Parts.Notes>();
            if (notes == null)
                return;

            if (string.IsNullOrEmpty(notes.Text))
                notes.Text = noteText;
            else
                notes.Text = notes.Text + "\n" + noteText;
        }
    }
}
