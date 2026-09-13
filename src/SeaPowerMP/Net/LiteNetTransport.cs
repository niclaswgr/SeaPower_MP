using System;
using System.Collections.Generic;
using System.Text;
using LiteNetLib;
using SeaPowerMP.Core.Transport;

namespace SeaPowerMP.Net
{
    /// <summary>Direct UDP connection (LAN, port forwarding, VPN) and the transport used for local two-instance testing.</summary>
    internal sealed class LiteNetTransport : ITransport
    {
        // Versions are checked in the session handshake; the key only filters stray traffic.
        private const string ConnectionKey = "SeaPowerMP";
        private const int MaxReasonBytes = 400;

        private readonly EventBasedNetListener _listener = new EventBasedNetListener();
        private readonly NetManager _manager;
        private readonly Dictionary<int, NetPeer> _peers = new Dictionary<int, NetPeer>();
        private readonly string _target;
        private bool _shutDown;

        private LiteNetTransport(bool isHost, string target)
        {
            IsHost = isHost;
            _target = target;
            _manager = new NetManager(_listener)
            {
                AutoRecycle = true,
                // Mission loads block the main thread for many seconds; LiteNetLib keeps the link
                // alive on its own thread, so a generous timeout only matters for real outages.
                DisconnectTimeout = 20000,
                IPv6Enabled = false,
            };
            _listener.ConnectionRequestEvent += OnConnectionRequest;
            _listener.PeerConnectedEvent += OnPeerConnected;
            _listener.PeerDisconnectedEvent += OnPeerDisconnected;
            _listener.NetworkReceiveEvent += OnNetworkReceive;
        }

        public bool IsHost { get; }

        public event Action<PeerId>? PeerConnected;
        public event Action<PeerId, string>? PeerDisconnected;
        public event Action<PeerId, ArraySegment<byte>>? DataReceived;

        public static LiteNetTransport Host(int port)
        {
            var transport = new LiteNetTransport(isHost: true, "port " + port);
            if (!transport._manager.Start(port))
                throw new InvalidOperationException($"Could not open UDP port {port} - is another program using it?");
            return transport;
        }

        public static LiteNetTransport Connect(string address, int port)
        {
            var transport = new LiteNetTransport(isHost: false, address + ":" + port);
            if (!transport._manager.Start())
                throw new InvalidOperationException("Could not open a local UDP socket.");
            if (transport._manager.Connect(address, port, ConnectionKey) == null)
            {
                transport._manager.Stop();
                throw new InvalidOperationException($"Could not resolve address '{address}'.");
            }
            return transport;
        }

        public void Poll()
        {
            if (!_shutDown)
                _manager.PollEvents();
        }

        public void Send(PeerId peer, byte[] data, int length, Delivery delivery)
        {
            if (!_peers.TryGetValue((int)peer.Value, out var netPeer))
                return;
            netPeer.Send(data, 0, length, delivery == Delivery.Reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
        }

        public void Disconnect(PeerId peer, string reason)
        {
            if (_peers.TryGetValue((int)peer.Value, out var netPeer))
                _manager.DisconnectPeer(netPeer, EncodeReason(reason));
        }

        public void Shutdown(string reason)
        {
            if (_shutDown)
                return;
            _shutDown = true;
            byte[] data = EncodeReason(reason);
            _manager.DisconnectAll(data, 0, data.Length);
            _manager.Stop(true);
            _peers.Clear();
        }

        public int GetRttMs(PeerId peer) =>
            _peers.TryGetValue((int)peer.Value, out var netPeer) ? netPeer.RoundTripTime : 0;

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (IsHost)
                request.AcceptIfKey(ConnectionKey);
            else
                request.Reject();
        }

        private void OnPeerConnected(NetPeer peer)
        {
            _peers[peer.Id] = peer;
            PeerConnected?.Invoke(new PeerId((ulong)peer.Id));
        }

        private void OnPeerDisconnected(NetPeer peer, DisconnectInfo info)
        {
            _peers.Remove(peer.Id);
            string reason = DescribeDisconnect(info);
            PeerDisconnected?.Invoke(new PeerId((ulong)peer.Id), reason);
        }

        private void OnNetworkReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod deliveryMethod)
        {
            DataReceived?.Invoke(new PeerId((ulong)peer.Id), reader.GetRemainingBytesSegment());
        }

        private string DescribeDisconnect(DisconnectInfo info)
        {
            var extra = info.AdditionalData;
            if (extra != null && extra.AvailableBytes > 0)
            {
                ArraySegment<byte> bytes = extra.GetRemainingBytesSegment();
                if (bytes.Array != null)
                    return Encoding.UTF8.GetString(bytes.Array, bytes.Offset, bytes.Count);
            }
            switch (info.Reason)
            {
                case DisconnectReason.ConnectionFailed:
                    return $"Could not connect to {_target}. Check the address, the host's firewall and port forwarding.";
                case DisconnectReason.Timeout:
                    return "Connection timed out.";
                case DisconnectReason.RemoteConnectionClose:
                    return "The other side closed the connection.";
                case DisconnectReason.ConnectionRejected:
                    return "The host rejected the connection.";
                case DisconnectReason.DisconnectPeerCalled:
                    return "Disconnected.";
                default:
                    return "Connection lost (" + info.Reason + ").";
            }
        }

        private static byte[] EncodeReason(string reason)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(reason ?? "");
            if (bytes.Length <= MaxReasonBytes)
                return bytes;
            // Cut on a character boundary; the full text also travels in the session's Goodbye message.
            return Encoding.UTF8.GetBytes(reason!.Substring(0, MaxReasonBytes / 4));
        }
    }
}
