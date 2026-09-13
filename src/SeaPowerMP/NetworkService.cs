using System;
using System.IO;
using SeaPowerMP.Core.Session;
using SeaPowerMP.Core.Transport;
using SeaPowerMP.Game;
using SeaPowerMP.Net;
using Steamworks;
using UnityEngine;

namespace SeaPowerMP
{
    internal enum NetRole
    {
        Offline,
        Host,
        Client,
    }

    /// <summary>Owns the active transport and session and exposes the actions the lobby window offers.</summary>
    internal sealed class NetworkService
    {
        private readonly ModConfig _config;
        private readonly string _modDirectory;
        private ITransport? _transport;

        public NetworkService(ModConfig config, string modDirectory)
        {
            _config = config;
            _modDirectory = modDirectory;
            if (SteamAvailable)
            {
                Lobby = new SteamLobby();
                Lobby.HostFound += JoinSteam;
                Lobby.Failed += ReportProblem;
            }
        }

        public HostSession? Host { get; private set; }
        public ClientSession? Client { get; private set; }
        public SteamLobby? Lobby { get; }

        /// <summary>Last error or disconnect reason, shown in the window until the next action.</summary>
        public string? LastProblem { get; private set; }

        public bool SteamAvailable => global::SteamManager.Initialized;

        public NetRole Role => Host != null ? NetRole.Host : Client != null ? NetRole.Client : NetRole.Offline;

        public void HostSteam() => Start(() =>
        {
            StartHost(SteamTransport.Host());
            Lobby!.Create(_config.MaxPlayers.Value);
        });

        public void HostDirect(int port) => Start(() => StartHost(LiteNetTransport.Host(port)));

        public void JoinDirect(string address, int port) => Start(() => StartClient(LiteNetTransport.Connect(address, port)));

        public void JoinSteam(ulong hostSteamId) => Start(() => StartClient(SteamTransport.Connect(hostSteamId)));

        public void Leave()
        {
            Host?.Stop();
            Client?.Leave();
            Lobby?.Leave();
            Host = null;
            Client = null;
            _transport = null;
        }

        public void Tick()
        {
            try
            {
                Host?.Tick();
                if (Client != null)
                {
                    Client.Tick();
                    if (Client.State == ClientState.Disconnected)
                    {
                        ReportProblem(Client.DisconnectReason ?? "Disconnected.");
                        Client = null;
                        _transport = null;
                        Lobby?.Leave();
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Network tick failed, leaving the session: " + ex);
                ReportProblem("Internal error - the session was closed. See BepInEx/LogOutput.log.");
                Leave();
            }
        }

        /// <summary>Steam starts the game with "+connect_lobby &lt;id&gt;" when an invite is accepted while it is closed.</summary>
        public void HandleLaunchArguments(string[] args)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "+connect_lobby" && ulong.TryParse(args[i + 1], out ulong lobbyId) && Lobby != null)
                {
                    Plugin.Log.LogInfo("[Steam] Joining lobby from launch argument.");
                    Lobby.Join(new CSteamID(lobbyId));
                    return;
                }
            }
        }

        /// <summary>
        /// Lets two game instances on one PC connect without clicking through the window:
        /// SEAPOWERMP_ROLE=host|client, SEAPOWERMP_ADDRESS, SEAPOWERMP_PORT, SEAPOWERMP_NAME.
        /// </summary>
        public void ApplyEnvironmentAutoStart()
        {
            string? role = Environment.GetEnvironmentVariable("SEAPOWERMP_ROLE");
            if (string.IsNullOrEmpty(role))
                return;
            string? name = Environment.GetEnvironmentVariable("SEAPOWERMP_NAME");
            if (!string.IsNullOrEmpty(name))
                _overrideName = name;
            int port = int.TryParse(Environment.GetEnvironmentVariable("SEAPOWERMP_PORT"), out int p) ? p : _config.Port.Value;
            string address = Environment.GetEnvironmentVariable("SEAPOWERMP_ADDRESS") ?? "127.0.0.1";

            Plugin.Log.LogWarning($"[AutoStart] SEAPOWERMP_ROLE={role}, address={address}, port={port}");
            if (role.Equals("host", StringComparison.OrdinalIgnoreCase))
                HostDirect(port);
            else
                JoinDirect(address, port);
        }

        private string? _overrideName;

        private void Start(Action start)
        {
            Leave();
            LastProblem = null;
            try
            {
                start();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("Could not start the session: " + ex);
                ReportProblem(ex.Message);
                Leave();
            }
        }

        private void StartHost(ITransport transport)
        {
            _transport = transport;
            var settings = new HostSettings
            {
                MaxPlayers = _config.MaxPlayers.Value,
                StrictModCheck = _config.StrictModCheck.Value,
            };
            Host = new HostSession(transport, BuildIdentity(), settings, Clock);
            Host.Log += message => Plugin.Log.LogInfo("[Session] " + message);
            Plugin.Log.LogInfo($"[Session] Hosting via {transport.GetType().Name} for up to {settings.MaxPlayers} players.");
        }

        private void StartClient(ITransport transport)
        {
            _transport = transport;
            Client = new ClientSession(transport, BuildIdentity(), Clock);
            Client.StateChanged += state => Plugin.Log.LogInfo($"[Session] Client state: {state}");
            Plugin.Log.LogInfo($"[Session] Joining via {transport.GetType().Name}.");
        }

        private SessionIdentity BuildIdentity()
        {
            bool steam = SteamAvailable;
            string name = _overrideName ?? _config.PlayerName.Value;
            if (string.IsNullOrWhiteSpace(name) && steam)
                name = SteamFriends.GetPersonaName();
            return new SessionIdentity
            {
                PlayerName = name,
                SteamId = steam ? SteamUser.GetSteamID().m_SteamID : 0,
                GameVersion = Application.version,
                ModVersion = PluginInfo.Version,
                Mods = EnabledMods.List(_modDirectory),
            };
        }

        private void ReportProblem(string message)
        {
            LastProblem = message;
            Plugin.Log.LogWarning("[Session] " + message);
        }

        private static double Clock() => Time.realtimeSinceStartupAsDouble;
    }
}
