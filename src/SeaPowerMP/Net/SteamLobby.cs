using System;
using SeaPowerMP.Core;
using Steamworks;

namespace SeaPowerMP.Net
{
    /// <summary>
    /// Friends-only Steam lobby used for invites. It carries the host's Steam id; the actual game
    /// traffic runs over <see cref="SteamTransport"/>.
    /// </summary>
    internal sealed class SteamLobby
    {
        private const string HostKey = "seapowermp_host";
        private const string ProtocolKey = "seapowermp_protocol";

        private readonly Callback<GameLobbyJoinRequested_t> _joinRequested;
        private readonly Callback<LobbyEnter_t> _lobbyEntered;
        private readonly CallResult<LobbyCreated_t> _lobbyCreated;

        public SteamLobby()
        {
            // Fires when the player accepts an invite or clicks "Join game" while Sea Power is running.
            _joinRequested = Callback<GameLobbyJoinRequested_t>.Create(request => Join(request.m_steamIDLobby));
            _lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            _lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
        }

        public CSteamID LobbyId { get; private set; } = CSteamID.Nil;
        public bool InLobby => LobbyId != CSteamID.Nil;
        public bool IsCreating { get; private set; }

        /// <summary>We entered someone else's lobby: connect to this host.</summary>
        public event Action<ulong>? HostFound;

        public event Action<string>? Failed;

        public void Create(int maxMembers)
        {
            Leave();
            IsCreating = true;
            _lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxMembers));
        }

        public void Join(CSteamID lobby)
        {
            Leave();
            SteamMatchmaking.JoinLobby(lobby);
        }

        public void OpenInviteDialog()
        {
            if (InLobby)
                SteamFriends.ActivateGameOverlayInviteDialog(LobbyId);
        }

        public void Leave()
        {
            IsCreating = false;
            if (InLobby)
                SteamMatchmaking.LeaveLobby(LobbyId);
            LobbyId = CSteamID.Nil;
        }

        private void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            IsCreating = false;
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                Failed?.Invoke($"Steam could not create a lobby ({(ioFailure ? "I/O failure" : result.m_eResult.ToString())}). Direct IP still works.");
                return;
            }
            LobbyId = new CSteamID(result.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(LobbyId, HostKey, SteamUser.GetSteamID().m_SteamID.ToString());
            SteamMatchmaking.SetLobbyData(LobbyId, ProtocolKey, Protocol.Version.ToString());
        }

        private void OnLobbyEntered(LobbyEnter_t entered)
        {
            var lobby = new CSteamID(entered.m_ulSteamIDLobby);
            if (entered.m_EChatRoomEnterResponse != (uint)EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Failed?.Invoke("Could not join the Steam lobby - it may be full or closed.");
                return;
            }
            LobbyId = lobby;

            // The creator receives this callback too.
            if (SteamMatchmaking.GetLobbyOwner(lobby) == SteamUser.GetSteamID())
                return;

            string protocol = SteamMatchmaking.GetLobbyData(lobby, ProtocolKey);
            if (protocol != Protocol.Version.ToString())
            {
                Leave();
                Failed?.Invoke(string.IsNullOrEmpty(protocol)
                    ? "That Steam lobby is not a SeaPower MP session."
                    : $"The host runs a different SeaPower MP version (protocol {protocol}, yours {Protocol.Version}).");
                return;
            }
            if (!ulong.TryParse(SteamMatchmaking.GetLobbyData(lobby, HostKey), out ulong hostId))
            {
                Leave();
                Failed?.Invoke("That Steam lobby has no host.");
                return;
            }
            HostFound?.Invoke(hostId);
        }
    }
}
