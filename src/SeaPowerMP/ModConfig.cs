using BepInEx.Configuration;
using SeaPowerMP.Core;
using UnityEngine;

namespace SeaPowerMP
{
    internal sealed class ModConfig
    {
        public ModConfig(ConfigFile file)
        {
            PlayerName = file.Bind("Player", "Name", "",
                "Name shown to other players. Empty = your Steam name.");
            ToggleWindow = file.Bind("Interface", "ToggleWindow", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftControl),
                "Opens and closes the multiplayer window.");
            MaxPlayers = file.Bind("Host", "MaxPlayers", Protocol.MaxPlayers,
                new ConfigDescription("Players allowed in a session you host, including you.",
                    new AcceptableValueRange<int>(2, Protocol.MaxPlayers)));
            StrictModCheck = file.Bind("Host", "StrictModCheck", true,
                "Refuse players whose enabled mods differ from yours. Unit, weapon or sensor mods on one side only cause desyncs.");
            Port = file.Bind("DirectIP", "Port", 7777,
                new ConfigDescription("UDP port for direct IP sessions.", new AcceptableValueRange<int>(1024, 65535)));
            LastAddress = file.Bind("DirectIP", "LastAddress", "127.0.0.1",
                "Last address you joined.");
        }

        public ConfigEntry<string> PlayerName { get; }
        public ConfigEntry<KeyboardShortcut> ToggleWindow { get; }
        public ConfigEntry<int> MaxPlayers { get; }
        public ConfigEntry<bool> StrictModCheck { get; }
        public ConfigEntry<int> Port { get; }
        public ConfigEntry<string> LastAddress { get; }
    }
}
