using System;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using SeaPowerMP.Core;
using SeaPowerMP.Game;
using SeaPowerMP.UI;
using UnityEngine;

namespace SeaPowerMP
{
    internal sealed class Plugin : MonoBehaviour
    {
        private const float AutoStartDelaySec = 2f;

        internal static ManualLogSource Log = null!;
        internal static Plugin? Instance { get; private set; }

        /// <summary>Set when a hook target is missing; multiplayer stays off for the whole session.</summary>
        public static string? DisabledReason { get; private set; }

        private static bool _booted;
        private Harmony? _harmony;
        private NetworkService? _network;
        private bool _autoStartDone;

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
            var config = new ModConfig(new ConfigFile(Path.Combine(Paths.ConfigPath, PluginInfo.Guid + ".cfg"), saveOnInit: true));
            var window = gameObject.AddComponent<LobbyOverlay>();

            if (!TryInstallPatches())
            {
                window.Init(null, config);
                return;
            }

            string modDirectory = Path.GetDirectoryName(typeof(Plugin).Assembly.Location) ?? "";
            _network = new NetworkService(config, modDirectory);
            window.Init(_network, config);
            _network.HandleLaunchArguments(System.Environment.GetCommandLineArgs());

            Log.LogInfo($"{PluginInfo.Name} v{PluginInfo.Version} loaded (protocol {Protocol.Version}, game {Application.version}). " +
                        $"{config.ToggleWindow.Value} opens the multiplayer window.");
        }

        private bool TryInstallPatches()
        {
            var missing = HookRegistry.FindMissingTargets();
            if (missing.Count > 0)
            {
                DisabledReason = "Game version not supported - missing: " + string.Join(", ", missing);
                Log.LogError(DisabledReason + ". Multiplayer is disabled for this session.");
                return false;
            }

            try
            {
                _harmony = new Harmony(PluginInfo.Guid);
                _harmony.PatchAll(typeof(Plugin).Assembly);
                return true;
            }
            catch (Exception ex)
            {
                DisabledReason = "Patching failed: " + ex.Message;
                Log.LogError(DisabledReason + "\n" + ex);
                _harmony?.UnpatchSelf();
                return false;
            }
        }

        private void Update()
        {
            if (_network == null)
                return;
            if (!_autoStartDone && Time.realtimeSinceStartup > AutoStartDelaySec)
            {
                _autoStartDone = true;
                _network.ApplyEnvironmentAutoStart();
            }
            _network.Tick();
        }

        private void OnDestroy()
        {
            _network?.Leave();
            _harmony?.UnpatchSelf();
        }
    }
}
