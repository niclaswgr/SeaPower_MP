using System;
using System.Text;

namespace SeaPowerMP.Core.Serialization
{
    /// <summary>Counterpart to <see cref="PacketWriter"/>. Throws <see cref="MalformedPacketException"/> on truncated input.</summary>
    public sealed class PacketReader
    {
        private byte[] _buffer;
        private int _position;
        private int _end;

        public PacketReader(byte[] buffer) : this(buffer, 0, buffer.Length) { }

        public PacketReader(byte[] buffer, int offset, int count)
        {
            _buffer = buffer;
            _position = offset;
            _end = offset + count;
        }

        public int Remaining => _end - _position;

        public void Reset(byte[] buffer, int offset, int count)
        {
            _buffer = buffer;
            _position = offset;
            _end = offset + count;
        }

        public byte ReadByte()
        {
            Require(1);
            return _buffer[_position++];
        }

        public bool ReadBool() => ReadByte() != 0;

        public ushort ReadUInt16()
        {
            Require(2);
            ushort value = (ushort)(_buffer[_position] | (_buffer[_position + 1] << 8));
            _position += 2;
            return value;
        }

        public uint ReadUInt32()
        {
            Require(4);
            uint value = (uint)(_buffer[_position]
                | (_buffer[_position + 1] << 8)
                | (_buffer[_position + 2] << 16)
                | (_buffer[_position + 3] << 24));
            _position += 4;
            return value;
        }

        public ulong ReadUInt64()
        {
            ulong low = ReadUInt32();
            ulong high = ReadUInt32();
            return low | (high << 32);
        }

        public uint ReadVarUInt()
        {
            uint result = 0;
            for (int shift = 0; shift < 35; shift += 7)
            {
                byte b = ReadByte();
                result |= (uint)(b & 0x7F) << shift;
                if ((b & 0x80) == 0)
                    return result;
            }
            throw new MalformedPacketException("VarUInt longer than 5 bytes");
        }

        public int ReadVarInt()
        {
            uint raw = ReadVarUInt();
            return (int)(raw >> 1) ^ -(int)(raw & 1);
        }

        public unsafe float ReadFloat()
        {
            uint raw = ReadUInt32();
            return *(float*)&raw;
        }

        public unsafe double ReadDouble()
        {
            ulong raw = ReadUInt64();
            return *(double*)&raw;
        }

        public string? ReadString()
        {
            uint lengthPlusOne = ReadVarUInt();
            if (lengthPlusOne == 0)
                return null;
            int length = checked((int)(lengthPlusOne - 1));
            Require(length);
            string value = Encoding.UTF8.GetString(_buffer, _position, length);
            _position += length;
            return value;
        }

        public byte[] ReadBytes()
        {
            int count = checked((int)ReadVarUInt());
            Require(count);
            var data = new byte[count];
            Buffer.BlockCopy(_buffer, _position, data, 0, count);
            _position += count;
            return data;
        }

        private void Require(int count)
        {
            if (count < 0 || _end - _position < count)
                throw new MalformedPacketException($"Needed {count} bytes, {_end - _position} left");
        }
    }

    public sealed class MalformedPacketException : Exception
    {
        public MalformedPacketException(string message) : base(message) { }
    }
}
