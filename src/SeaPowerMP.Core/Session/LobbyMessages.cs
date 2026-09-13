using System.Collections.Generic;
using SeaPowerMP.Core.Serialization;

namespace SeaPowerMP.Core.Session
{
    public enum MessageId : byte
    {
        Hello = 1,
        Welcome = 2,
        Roster = 3,
        SetTeam = 4,

        /// <summary>
        /// Reason for an upcoming disconnect. Transports may truncate their own disconnect reason
        /// (Steam: 128 chars), so the full text travels as a reliable message first.
        /// Layout (id + string) must never change: it is also sent to peers with other protocol versions.
        /// </summary>
        Goodbye = 5,

        /// <summary>Ids from here on are not handled by the session and are forwarded to subscribers.</summary>
        FirstGameMessage = 32,
    }

    public enum Team : byte
    {
        Blue = 0,
        Red = 1,
    }

    public sealed class PlayerInfo
    {
        public byte Slot { get; internal set; }
        public string Name { get; internal set; } = "";
        public Team Team { get; internal set; }
        public ulong SteamId { get; internal set; }

        public bool IsHost => Slot == Protocol.HostSlot;

        public override string ToString() => $"#{Slot} {Name} ({Team})";
    }

    /// <summary>Everything a peer tells the other side about itself during the handshake.</summary>
    public sealed class SessionIdentity
    {
        public string PlayerName { get; set; } = "";
        public ulong SteamId { get; set; }
        public string GameVersion { get; set; } = "";
        public string ModVersion { get; set; } = "";

        /// <summary>Folder names of all enabled mods. Order does not matter.</summary>
        public IReadOnlyList<string> Mods { get; set; } = new string[0];
    }

    internal static class HelloMessage
    {
        // The protocol version is always the first field so any future layout can still be rejected cleanly.
        public static void Write(PacketWriter w, SessionIdentity id)
        {
            w.WriteUInt16(Protocol.Version);
            w.WriteString(id.GameVersion);
            w.WriteString(id.ModVersion);
            w.WriteString(id.PlayerName);
            w.WriteUInt64(id.SteamId);
            w.WriteVarUInt((uint)id.Mods.Count);
            foreach (string mod in id.Mods)
                w.WriteString(mod);
        }

        /// <summary>Reads everything after the protocol version.</summary>
        public static SessionIdentity ReadBody(PacketReader r)
        {
            var id = new SessionIdentity
            {
                GameVersion = r.ReadString() ?? "",
                ModVersion = r.ReadString() ?? "",
                PlayerName = r.ReadString() ?? "",
                SteamId = r.ReadUInt64(),
            };
            uint count = r.ReadVarUInt();
            if (count > 1024)
                throw new MalformedPacketException("Too many mods in hello");
            var mods = new List<string>((int)count);
            for (int i = 0; i < count; i++)
                mods.Add(r.ReadString() ?? "");
            id.Mods = mods;
            return id;
        }
    }

    internal static class RosterMessage
    {
        public static void Write(PacketWriter w, IReadOnlyList<PlayerInfo> players)
        {
            w.WriteByte((byte)players.Count);
            foreach (var p in players)
            {
                w.WriteByte(p.Slot);
                w.WriteString(p.Name);
                w.WriteByte((byte)p.Team);
                w.WriteUInt64(p.SteamId);
            }
        }

        public static List<PlayerInfo> Read(PacketReader r)
        {
            int count = r.ReadByte();
            if (count > Protocol.MaxPlayers)
                throw new MalformedPacketException("Roster larger than MaxPlayers");
            var players = new List<PlayerInfo>(count);
            for (int i = 0; i < count; i++)
            {
                players.Add(new PlayerInfo
                {
                    Slot = r.ReadByte(),
                    Name = r.ReadString() ?? "",
                    Team = ReadTeam(r),
                    SteamId = r.ReadUInt64(),
                });
            }
            return players;
        }

        public static Team ReadTeam(PacketReader r)
        {
            byte raw = r.ReadByte();
            if (raw > (byte)Team.Red)
                throw new MalformedPacketException("Unknown team " + raw);
            return (Team)raw;
        }
    }
}
