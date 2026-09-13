using System;
using System.Collections.Generic;
using SeaPowerMP.Core.Serialization;
using SeaPowerMP.Core.Transport;

namespace SeaPowerMP.Core.Session
{
    public enum ClientState : byte
    {
        Connecting,
        Handshaking,
        Connected,
        Disconnected,
    }

    /// <summary>Client side of a session: performs the handshake and mirrors the host's roster.</summary>
    public sealed class ClientSession
    {
        public const byte NoSlot = byte.MaxValue;

        private readonly ITransport _transport;
        private readonly SessionIdentity _identity;
        private readonly Func<double> _clock;
        private readonly double _deadline;
        private readonly PacketWriter _writer = new PacketWriter();
        private readonly PacketReader _reader = new PacketReader(new byte[0]);
        private List<PlayerInfo> _players = new List<PlayerInfo>();
        private PeerId _host;
        private string? _goodbyeReason;

        public ClientSession(ITransport transport, SessionIdentity identity, Func<double> clock, double connectTimeoutSec = 20)
        {
            if (transport.IsHost)
                throw new ArgumentException("ClientSession needs a client transport.", nameof(transport));
            _transport = transport;
            _identity = identity;
            _clock = clock;
            _deadline = clock() + connectTimeoutSec;

            _transport.PeerConnected += OnConnected;
            _transport.PeerDisconnected += OnDisconnected;
            _transport.DataReceived += OnDataReceived;
        }

        public ClientState State { get; private set; } = ClientState.Connecting;

        /// <summary>Why the session ended, in words for the player. Null while connected.</summary>
        public string? DisconnectReason { get; private set; }

        public byte LocalSlot { get; private set; } = NoSlot;

        public IReadOnlyList<PlayerInfo> Players => _players;

        public PlayerInfo? LocalPlayer => _players.Find(p => p.Slot == LocalSlot);

        public event Action<ClientState>? StateChanged;
        public event Action? RosterChanged;
        public event Action<MessageId, PacketReader>? MessageReceived;

        public void Tick()
        {
            _transport.Poll();
            if (State < ClientState.Connected && _clock() >= _deadline)
            {
                // Set the reason first so the transport's own disconnect event does not overwrite it.
                EnterDisconnected("Could not reach the host (timed out).");
                _transport.Shutdown("Timed out");
            }
        }

        public void RequestTeam(Team team)
        {
            if (State != ClientState.Connected)
                return;
            var message = BeginMessage(MessageId.SetTeam);
            message.WriteByte((byte)team);
            Send(message, Delivery.Reliable);
        }

        public void Leave()
        {
            EnterDisconnected("You left the session.");
            _transport.Shutdown("Player left");
        }

        public PacketWriter BeginMessage(MessageId id)
        {
            _writer.Reset();
            _writer.WriteByte((byte)id);
            return _writer;
        }

        public void Send(PacketWriter message, Delivery delivery)
        {
            if (State == ClientState.Handshaking || State == ClientState.Connected)
                _transport.Send(_host, message.Buffer, message.Length, delivery);
        }

        private void OnConnected(PeerId host)
        {
            if (State != ClientState.Connecting)
                return;
            _host = host;
            SetState(ClientState.Handshaking);
            var hello = BeginMessage(MessageId.Hello);
            HelloMessage.Write(hello, _identity);
            _transport.Send(_host, hello.Buffer, hello.Length, Delivery.Reliable);
        }

        private void OnDisconnected(PeerId peer, string reason)
        {
            EnterDisconnected(_goodbyeReason ?? reason);
        }

        private void OnDataReceived(PeerId peer, ArraySegment<byte> data)
        {
            if (data.Array == null || data.Count == 0 || State == ClientState.Disconnected)
                return;
            _reader.Reset(data.Array, data.Offset, data.Count);
            try
            {
                var id = (MessageId)_reader.ReadByte();
                switch (id)
                {
                    case MessageId.Welcome:
                        LocalSlot = _reader.ReadByte();
                        SetState(ClientState.Connected);
                        break;
                    case MessageId.Roster:
                        _players = RosterMessage.Read(_reader);
                        RosterChanged?.Invoke();
                        break;
                    case MessageId.Goodbye:
                        _goodbyeReason = _reader.ReadString();
                        break;
                    default:
                        if (id >= MessageId.FirstGameMessage && State == ClientState.Connected)
                            MessageReceived?.Invoke(id, _reader);
                        break;
                }
            }
            catch (MalformedPacketException ex)
            {
                EnterDisconnected("Protocol error from host: " + ex.Message);
                _transport.Shutdown("Protocol error");
            }
        }

        private void EnterDisconnected(string reason)
        {
            if (State == ClientState.Disconnected)
                return;
            DisconnectReason = reason;
            _players = new List<PlayerInfo>();
            LocalSlot = NoSlot;
            SetState(ClientState.Disconnected);
            RosterChanged?.Invoke();
        }

        private void SetState(ClientState state)
        {
            if (State == state)
                return;
            State = state;
            StateChanged?.Invoke(state);
        }
    }
}
