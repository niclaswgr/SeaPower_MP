using System;
using System.Text;

namespace SeaPowerMP.Core.Serialization
{
    /// <summary>Growable little-endian byte writer. Reuse one instance per send path and call <see cref="Reset"/>.</summary>
    public sealed class PacketWriter
    {
        private byte[] _buffer;
        private int _length;

        public PacketWriter(int initialCapacity = 256)
        {
            _buffer = new byte[Math.Max(16, initialCapacity)];
        }

        public int Length => _length;

        public byte[] Buffer => _buffer;

        public void Reset() => _length = 0;

        public byte[] ToArray()
        {
            var copy = new byte[_length];
            System.Buffer.BlockCopy(_buffer, 0, copy, 0, _length);
            return copy;
        }

        public void WriteByte(byte value)
        {
            Ensure(1);
            _buffer[_length++] = value;
        }

        public void WriteBool(bool value) => WriteByte(value ? (byte)1 : (byte)0);

        public void WriteUInt16(ushort value)
        {
            Ensure(2);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
        }

        public void WriteUInt32(uint value)
        {
            Ensure(4);
            _buffer[_length++] = (byte)value;
            _buffer[_length++] = (byte)(value >> 8);
            _buffer[_length++] = (byte)(value >> 16);
            _buffer[_length++] = (byte)(value >> 24);
        }

        public void WriteUInt64(ulong value)
        {
            WriteUInt32((uint)value);
            WriteUInt32((uint)(value >> 32));
        }

        /// <summary>LEB128: 1 byte for values below 128, at most 5 bytes.</summary>
        public void WriteVarUInt(uint value)
        {
            Ensure(5);
            while (value >= 0x80)
            {
                _buffer[_length++] = (byte)(value | 0x80);
                value >>= 7;
            }
            _buffer[_length++] = (byte)value;
        }

        /// <summary>Zigzag-encoded so small negative numbers stay small on the wire.</summary>
        public void WriteVarInt(int value) => WriteVarUInt((uint)((value << 1) ^ (value >> 31)));

        public unsafe void WriteFloat(float value) => WriteUInt32(*(uint*)&value);

        public unsafe void WriteDouble(double value) => WriteUInt64(*(ulong*)&value);

        public void WriteString(string? value)
        {
            if (value == null)
            {
                WriteVarUInt(0);
                return;
            }
            int byteCount = Encoding.UTF8.GetByteCount(value);
            // Length is stored +1 so that 0 can mean null.
            WriteVarUInt((uint)byteCount + 1);
            Ensure(byteCount);
            _length += Encoding.UTF8.GetBytes(value, 0, value.Length, _buffer, _length);
        }

        public void WriteBytes(byte[] data, int offset, int count)
        {
            WriteVarUInt((uint)count);
            Ensure(count);
            System.Buffer.BlockCopy(data, offset, _buffer, _length, count);
            _length += count;
        }

        private void Ensure(int extra)
        {
            int needed = _length + extra;
            if (needed <= _buffer.Length)
                return;
            int size = _buffer.Length * 2;
            while (size < needed)
                size *= 2;
            Array.Resize(ref _buffer, size);
        }
    }
}
