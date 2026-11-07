using System;
using System.Collections.Generic;
using System.Linq;

namespace LegacyItems
{
    public static class StainSerializationHelpers
    {
        public static string EncodeComponents(IDictionary<string, int> components)
        {
            if (components == null || components.Count <= 1)
                return null;

            var parts = components
                .Where(kv => kv.Value > 0)
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{kv.Key}:{kv.Value}")
                .ToList();

            if (parts.Count <= 1)
                return null;

            return "mix:" + string.Join(",", parts);
        }

        public static bool TryParseComponents(string stainLiquid, out Dictionary<string, int> components)
        {
            components = null;
            if (string.IsNullOrEmpty(stainLiquid) || !stainLiquid.StartsWith("mix:", StringComparison.OrdinalIgnoreCase))
                return false;

            var raw = stainLiquid.Substring("mix:".Length);
            if (string.IsNullOrEmpty(raw))
                return false;

            var dict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in raw.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = part.Split(':');
                if (kv.Length != 2)
                    continue;

                var id = kv[0];
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                if (!int.TryParse(kv[1], out var proportion) || proportion <= 0)
                    continue;

                if (dict.ContainsKey(id))
                {
                    dict[id] += proportion;
                }
                else
                {
                    dict[id] = proportion;
                }
            }

            if (dict.Count == 0)
                return false;

            components = dict;
            return true;
        }
    }
}
