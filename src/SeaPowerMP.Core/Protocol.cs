namespace SeaPowerMP.Core
{
    public static class Protocol
    {
        /// <summary>Bump on every wire-format change. Peers with different versions are refused at handshake.</summary>
        public const ushort Version = 1;

        public const int MaxPlayers = 8;

        /// <summary>Slot 0 is always the host.</summary>
        public const byte HostSlot = 0;

        /// <summary>Keeps unreliable packets under typical path MTU so they are never fragmented by IP.</summary>
        public const int MaxUnreliablePayload = 1100;
    }
}
