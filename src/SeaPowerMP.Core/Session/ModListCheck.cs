using System;
using System.Collections.Generic;
using System.Linq;

namespace SeaPowerMP.Core.Session
{
    public static class ModListCheck
    {
        /// <summary>Returns null when both lists contain the same mods, otherwise a readable difference.</summary>
        public static string? Describe(IEnumerable<string> hostMods, IEnumerable<string> clientMods)
        {
            var host = new HashSet<string>(hostMods, StringComparer.OrdinalIgnoreCase);
            var client = new HashSet<string>(clientMods, StringComparer.OrdinalIgnoreCase);
            if (host.SetEquals(client))
                return null;

            var onlyHost = host.Where(m => !client.Contains(m)).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            var onlyClient = client.Where(m => !host.Contains(m)).OrderBy(m => m, StringComparer.OrdinalIgnoreCase).ToList();
            var parts = new List<string>();
            if (onlyHost.Count > 0)
                parts.Add("missing on your side: " + string.Join(", ", onlyHost));
            if (onlyClient.Count > 0)
                parts.Add("not enabled on the host: " + string.Join(", ", onlyClient));
            return "Enabled mods differ - " + string.Join("; ", parts) + ".";
        }
    }
}
