using System;
using System.Collections.Generic;
using SeaPowerMP.Core.Serialization;
using SeaPowerMP.Core.Transport;

namespace SeaPowerMP.Core.Session
{
    public sealed class HostSettings
    {
        public int MaxPlayers { get; set; } = Protocol.MaxPlayers;

        /// <summary>Team assigned to newly joined players; they can switch in the lobby.</summary>
        public Team JoinTeam { get; set; } = Team.Blue;

        /// <summary>Refuse players whose enabled mods differ. Mismatched unit/weapon mods are a desync source.</summary>
        public bool StrictModCheck { get; set; } = true;

        public double HandshakeTimeoutSec { get; set; } = 10;
    }

    /// <summary>
    /// Host side of a session: accepts connections, runs the handshake, assigns slots and owns the roster.
    /// The host itself is always slot 0.
    /// </summary>
    public sealed class HostSession
    {
        private readonly ITransport _transport;
        private readonly SessionIdentity _identity;
        private readonly HostSettings _settings;
        private readonly Func<double> _clock;

        private readonly Dictionary<PeerId, double> _pendingDeadlines = new Dictionary<PeerId, double>();
        private readonly Dictionary<PeerId, PlayerInfo> _byPeer = new Dictionary<PeerId, PlayerInfo>();
        private readonly List<PlayerInfo> _players = new List<PlayerInfo>();
        private readonly PacketWriter _writer = new PacketWriter();
        private readonly PacketReader _reader = new PacketReader(new byte[0]);

        public HostSession(ITransport transport, SessionIdentity identity, HostSettings settings, Func<double> clock)
        {
            if (!transport.IsHost)
                throw new ArgumentException("HostSession needs a hosting transport.", nameof(transport));
            _transport = transport;
            _identity = identity;
            _settings = settings;
            _clock = clock;

            _players.Add(new PlayerInfo
            {
                Slot = Protocol.HostSlot,
                Name = SanitizeName(identity.PlayerName, Protocol.HostSlot),
                Team = settings.JoinTeam,
                SteamId = identity.SteamId,
            });

            _transport.PeerConnected += OnPeerConnected;
            _transport.PeerDisconnected += OnPeerDisconnected;
            _transport.DataReceived += OnDataReceived;
        }

        /// <summary>Sorted by slot. Index 0 is the host.</summary>
        public IReadOnlyList<PlayerInfo> Players => _players;

        public PlayerInfo LocalPlayer => _players[0];

        /// <summary>Once set (mission running), players can no longer switch teams.</summary>
        public bool TeamsLocked { get; set; }

        public event Action? RosterChanged;
        public event Action<string>? Log;
        public event Action<PlayerInfo, MessageId, PacketReader>? MessageReceived;

        public void Tick()
        {
            _transport.Poll();

            if (_pendingDeadlines.Count == 0)
                return;
            double now = _clock();
            List<PeerId>? expired = null;
            foreach (var pair in _pendingDeadlines)
            {
                if (now >= pair.Value)
                    (expired ??= new List<PeerId>()).Add(pair.Key);
            }
            if (expired == null)
                return;
            foreach (var peer in expired)
            {
                _pendingDeadlines.Remove(peer);
                Refuse(peer, "Handshake timed out - is the mod installed and enabled on your side?");
            }
        }

        public void SetTeam(byte slot, Team team)
        {
            var player = FindSlot(slot);
            if (player == null || player.Team == team)
                return;
            player.Team = team;
            Log?.Invoke($"{player.Name} switched to {team}.");
            BroadcastRoster();
        }

        public void Kick(byte slot, string reason)
        {
            foreach (var pair in _byPeer)
            {
                if (pair.Value.Slot == slot)
                {
                    Refuse(pair.Key, "Kicked by host: " + reason);
                    return;
                }
            }
        }

        public void Stop(string reason = "The host closed the session.")
        {
            var goodbye = BeginMessage(MessageId.Goodbye);
            goodbye.WriteString(reason);
            foreach (var peer in _pendingDeadlines.Keys)
                _transport.Send(peer, goodbye.Buffer, goodbye.Length, Delivery.Reliable);
            Broadcast(goodbye, Delivery.Reliable);
            _transport.Shutdown(reason);
        }

        public PacketWriter BeginMessage(MessageId id)
        {
            _writer.Reset();
            _writer.WriteByte((byte)id);
            return _writer;
        }

        public void SendTo(byte slot, PacketWriter message, Delivery delivery)
        {
            foreach (var pair in _byPeer)
            {
                if (pair.Value.Slot == slot)
                {
                    _transport.Send(pair.Key, message.Buffer, message.Length, delivery);
                    return;
                }
            }
        }

        public void Broadcast(PacketWriter message, Delivery delivery)
        {
            foreach (var peer in _byPeer.Keys)
                _transport.Send(peer, message.Buffer, message.Length, delivery);
        }

        private void OnPeerConnected(PeerId peer)
        {
            _pendingDeadlines[peer] = _clock() + _settings.HandshakeTimeoutSec;
        }

        private void OnPeerDisconnected(PeerId peer, string reason)
        {
            _pendingDeadlines.Remove(peer);
            if (!_byPeer.TryGetValue(peer, out var player))
                return;
            _byPeer.Remove(peer);
            _players.Remove(player);
            Log?.Invoke($"{player.Name} left ({reason}).");
            BroadcastRoster();
        }

        private void OnDataReceived(PeerId peer, ArraySegment<byte> data)
        {
            if (data.Array == null || data.Count == 0)
                return;
            _reader.Reset(data.Array, data.Offset, data.Count);
            try
            {
                var id = (MessageId)_reader.ReadByte();
                if (_pendingDeadlines.ContainsKey(peer))
                {
                    if (id == MessageId.Hello)
                        HandleHello(peer);
                    else
                        Refuse(peer, "Protocol error: expected hello.");
                    return;
                }

                if (!_byPeer.TryGetValue(peer, out var player))
                    return;

                if (id == MessageId.SetTeam)
                {
                    Team team = RosterMessage.ReadTeam(_reader);
                    if (!TeamsLocked)
                        SetTeam(player.Slot, team);
                }
                else if (id >= MessageId.FirstGameMessage)
                {
                    MessageReceived?.Invoke(player, id, _reader);
                }
            }
            catch (MalformedPacketException ex)
            {
                _pendingDeadlines.Remove(peer);
                Refuse(peer, "Protocol error: " + ex.Message);
            }
        }

        private void HandleHello(PeerId peer)
        {
            _pendingDeadlines.Remove(peer);

            ushort protocol = _reader.ReadUInt16();
            if (protocol != Protocol.Version)
            {
                Refuse(peer, $"Mod protocol mismatch - host has {Protocol.Version}, you have {protocol}. Update SeaPower MP on both sides.");
                return;
            }

            SessionIdentity remote = HelloMessage.ReadBody(_reader);
            if (remote.GameVersion != _identity.GameVersion)
            {
                Refuse(peer, $"Sea Power version mismatch - host runs {_identity.GameVersion}, you run {remote.GameVersion}.");
                return;
            }
            if (remote.ModVersion != _identity.ModVersion)
            {
                Refuse(peer, $"SeaPower MP version mismatch - host has {_identity.ModVersion}, you have {remote.ModVersion}.");
                return;
            }
            if (_settings.StrictModCheck)
            {
                string? modDifference = ModListCheck.Describe(_identity.Mods, remote.Mods);
                if (modDifference != null)
                {
                    Refuse(peer, modDifference);
                    return;
                }
            }

            byte? slot = FindFreeSlot();
            if (slot == null)
            {
                Refuse(peer, $"The session is full ({_settings.MaxPlayers} players).");
                return;
            }

            var player = new PlayerInfo
            {
                Slot = slot.Value,
                Name = SanitizeName(remote.PlayerName, slot.Value),
                Team = _settings.JoinTeam,
                SteamId = remote.SteamId,
            };
            _byPeer[peer] = player;
            int index = _players.FindIndex(p => p.Slot > player.Slot);
            _players.Insert(index < 0 ? _players.Count : index, player);

            var welcome = BeginMessage(MessageId.Welcome);
            welcome.WriteByte(player.Slot);
            _transport.Send(peer, welcome.Buffer, welcome.Length, Delivery.Reliable);

            Log?.Invoke($"{player.Name} joined as slot {player.Slot}.");
            BroadcastRoster();
        }

        private void Refuse(PeerId peer, string reason)
        {
            Log?.Invoke($"Refused {peer}: {reason}");
            var goodbye = BeginMessage(MessageId.Goodbye);
            goodbye.WriteString(reason);
            _transport.Send(peer, goodbye.Buffer, goodbye.Length, Delivery.Reliable);
            _transport.Disconnect(peer, reason);
        }

        private void BroadcastRoster()
        {
            var message = BeginMessage(MessageId.Roster);
            RosterMessage.Write(message, _players);
            Broadcast(message, Delivery.Reliable);
            RosterChanged?.Invoke();
        }

        private byte? FindFreeSlot()
        {
            int limit = Math.Min(_settings.MaxPlayers, Protocol.MaxPlayers);
            for (byte slot = 1; slot < limit; slot++)
            {
                if (FindSlot(slot) == null)
                    return slot;
            }
            return null;
        }

        private PlayerInfo? FindSlot(byte slot) => _players.Find(p => p.Slot == slot);

        internal static string SanitizeName(string? name, byte slot)
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length > 32)
                trimmed = trimmed.Substring(0, 32);
            return trimmed.Length == 0 ? "Player " + (slot + 1) : trimmed;
        }
    }
}
