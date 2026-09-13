using System;

namespace SeaPowerMP.Core.Transport
{
    public enum Delivery : byte
    {
        /// <summary>Reliable and ordered. Session control, orders, events.</summary>
        Reliable,

        /// <summary>May be dropped; only the newest matters. State snapshots.</summary>
        Unreliable,
    }

    /// <summary>Opaque per-transport peer handle (Steam connection handle, LiteNetLib peer id, ...).</summary>
    public readonly struct PeerId : IEquatable<PeerId>
    {
        public readonly ulong Value;

        public PeerId(ulong value) => Value = value;

        public bool Equals(PeerId other) => Value == other.Value;
        public override bool Equals(object? obj) => obj is PeerId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => "peer#" + Value;

        public static bool operator ==(PeerId a, PeerId b) => a.Value == b.Value;
        public static bool operator !=(PeerId a, PeerId b) => a.Value != b.Value;
    }

    /// <summary>
    /// A started transport, either hosting or connected/connecting to a host. How it was started
    /// (Steam lobby, IP address, ...) is transport-specific and not part of this interface.
    /// All events are raised from inside <see cref="Poll"/> on the calling thread.
    /// </summary>
    public interface ITransport
    {
        bool IsHost { get; }

        /// <summary>Host: a client connected. Client: the connection to the host is established.</summary>
        event Action<PeerId>? PeerConnected;

        /// <summary>Raised for remote and local disconnects. The reason is shown to the player.</summary>
        event Action<PeerId, string>? PeerDisconnected;

        /// <summary>The segment is only valid during the callback.</summary>
        event Action<PeerId, ArraySegment<byte>>? DataReceived;

        void Poll();

        void Send(PeerId peer, byte[] data, int length, Delivery delivery);

        /// <summary>Closes one connection. The reason reaches the remote side (after pending reliable data).</summary>
        void Disconnect(PeerId peer, string reason);

        /// <summary>Closes everything and stops the transport. Safe to call more than once.</summary>
        void Shutdown(string reason);

        int GetRttMs(PeerId peer);
    }
}
