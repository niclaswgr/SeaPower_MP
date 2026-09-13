using System;
using BepInEx.Logging;
using HarmonyLib;
using SeaPowerMP.Core;
using SeaPowerMP.Game;
using UnityEngine;

namespace SeaPowerMP
{
    public sealed class Plugin : MonoBehaviour
    {
        internal static ManualLogSource Log = null!;
        internal static Plugin? Instance { get; private set; }

        /// <summary>Set when a hook target is missing; multiplayer stays off for the whole session.</summary>
        public static string? DisabledReason { get; private set; }

        private static bool _booted;
        private Harmony? _harmony;

        internal static void Boot()
        {
            if (_booted)
                return;
            _booted = true;

            Log = BepInEx.Logging.Logger.CreateLogSource("SeaPowerMP");
            if (!ModMenu.IsEnabled(typeof(Plugin).Assembly.Location, Log))
            {
                Log.LogInfo("Disabled in the game's mod menu - not loading.");
                return;
            }

            var host = new GameObject("SeaPowerMP");
            DontDestroyOnLoad(host);
            host.AddComponent<Plugin>();
        }

        private void Awake()
        {
            Instance = this;

            var missing = HookRegistry.FindMissingTargets();
            if (missing.Count > 0)
            {
                DisabledReason = "Game version not supported - missing: " + string.Join(", ", missing);
                Log.LogError(DisabledReason + ". Multiplayer is disabled for this session.");
                return;
            }

            try
            {
                _harmony = new Harmony(PluginInfo.Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
            }
            catch (Exception ex)
            {
                DisabledReason = "Patching failed: " + ex.Message;
                Log.LogError(DisabledReason + "\n" + ex);
                _harmony?.UnpatchSelf();
                return;
            }

            Log.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded (protocol {Protocol.Version}, game {Application.version}).");
        }

        private void OnDestroy()
        {
            _harmony?.UnpatchSelf();
        }
    }
}
