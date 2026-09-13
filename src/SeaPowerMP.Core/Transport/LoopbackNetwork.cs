using System;
using System.Collections.Generic;

namespace SeaPowerMP.Core.Transport
{
    /// <summary>
    /// In-memory network for tests: one host, any number of clients. Nothing is delivered until
    /// the receiving side calls <see cref="ITransport.Poll"/>, mirroring real transports.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        private LoopbackTransport? _host;
        private ulong _nextPeerId = 1;

        /// <summary>Simulates transports that truncate disconnect reasons (Steam: 128 chars).</summary>
        public int? MaxDisconnectReasonLength { get; set; }

        public LoopbackTransport CreateHost()
        {
            if (_host != null && !_host.IsShutDown)
                throw new InvalidOperationException("A host is already running on this network.");
            _host = new LoopbackTransport(this, isHost: true, new PeerId(0));
            return _host;
        }

        public LoopbackTransport CreateClient()
        {
            var client = new LoopbackTransport(this, isHost: false, new PeerId(_nextPeerId++));
            client.ConnectTo(_host);
            return client;
        }
    }

    public sealed class LoopbackTransport : ITransport
    {
        private readonly Dictionary<PeerId, LoopbackTransport> _links = new Dictionary<PeerId, LoopbackTransport>();
        private readonly Queue<Action> _inbox = new Queue<Action>();

        private readonly LoopbackNetwork _network;

        internal LoopbackTransport(LoopbackNetwork network, bool isHost, PeerId id)
        {
            _network = network;
            IsHost = isHost;
            Id = id;
        }

        public bool IsHost { get; }
        public PeerId Id { get; }
        public bool IsShutDown { get; private set; }

        public event Action<PeerId>? PeerConnected;
        public event Action<PeerId, string>? PeerDisconnected;
        public event Action<PeerId, ArraySegment<byte>>? DataReceived;

        internal void ConnectTo(LoopbackTransport? host)
        {
            if (host == null || host.IsShutDown)
            {
                _inbox.Enqueue(() => PeerDisconnected?.Invoke(new PeerId(0), "No host is running."));
                return;
            }
            _links[host.Id] = host;
            host._links[Id] = this;
            host._inbox.Enqueue(() => host.PeerConnected?.Invoke(Id));
            _inbox.Enqueue(() => PeerConnected?.Invoke(host.Id));
        }

        public void Poll()
        {
            int count = _inbox.Count;
            for (int i = 0; i < count; i++)
                _inbox.Dequeue()();
        }

        public void Send(PeerId peer, byte[] data, int length, Delivery delivery)
        {
            if (!_links.TryGetValue(peer, out var target))
                return;
            var copy = new byte[length];
            Buffer.BlockCopy(data, 0, copy, 0, length);
            target._inbox.Enqueue(() =>
            {
                // Dropped if the link was closed while the packet was in flight.
                if (target._links.ContainsKey(Id))
                    target.DataReceived?.Invoke(Id, new ArraySegment<byte>(copy));
            });
        }

        public void Disconnect(PeerId peer, string reason)
        {
            if (!_links.TryGetValue(peer, out var target))
                return;
            int? limit = _network.MaxDisconnectReasonLength;
            if (limit != null && reason.Length > limit.Value)
                reason = reason.Substring(0, limit.Value);
            // Data already queued ahead of the disconnect still arrives, like a lingering close.
            target._inbox.Enqueue(() =>
            {
                target._links.Remove(Id);
                target.PeerDisconnected?.Invoke(Id, reason);
            });
            _links.Remove(peer);
            _inbox.Enqueue(() => PeerDisconnected?.Invoke(peer, reason));
        }

        public void Shutdown(string reason)
        {
            if (IsShutDown)
                return;
            IsShutDown = true;
            foreach (var peer in new List<PeerId>(_links.Keys))
                Disconnect(peer, reason);
        }

        public int GetRttMs(PeerId peer) => 0;
    }
}
