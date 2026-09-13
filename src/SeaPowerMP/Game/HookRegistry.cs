using System;
using System.Collections.Generic;
using HarmonyLib;
using SeaPower;

namespace SeaPowerMP.Game
{
    /// <summary>
    /// Every game member the mod depends on is listed here and checked at startup. When a game
    /// update renames or removes one, the mod refuses to start instead of running half-patched
    /// and silently desyncing.
    /// </summary>
    internal static class HookRegistry
    {
        private static readonly (Type Type, string Method)[] RequiredMethods =
        {
            // Central simulation clocks - the thin client gates the simulation here.
            (typeof(GameMain), "PerformFixedUpdate"),
            (typeof(GameMain), "Update"),
            (typeof(GameFixedUpdater), nameof(GameFixedUpdater.fixedUpdate)),
            (typeof(GameUpdater), nameof(GameUpdater.update)),
        };

        private static readonly (Type Type, string Field)[] RequiredFields =
        {
            (typeof(GameMain), "_objects"),
        };

        public static List<string> FindMissingTargets()
        {
            var missing = new List<string>();
            foreach (var (type, method) in RequiredMethods)
            {
                if (AccessTools.Method(type, method) == null)
                    missing.Add(type.Name + "." + method + "()");
            }
            foreach (var (type, field) in RequiredFields)
            {
                if (AccessTools.Field(type, field) == null)
                    missing.Add(type.Name + "." + field);
            }
            return missing;
        }
    }
}
