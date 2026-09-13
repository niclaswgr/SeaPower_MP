using SeaPowerMP.Core.Serialization;
using Xunit;

namespace SeaPowerMP.Core.Tests;

public class PacketSerializationTests
{
    [Fact]
    public void RoundTripsAllPrimitiveTypes()
    {
        var writer = new PacketWriter(4);
        writer.WriteByte(0xAB);
        writer.WriteBool(true);
        writer.WriteUInt16(0xBEEF);
        writer.WriteUInt32(0xDEADBEEF);
        writer.WriteUInt64(0x0123456789ABCDEF);
        writer.WriteVarUInt(300);
        writer.WriteVarInt(-5);
        writer.WriteFloat(3.25f);
        writer.WriteDouble(-12345.678);
        writer.WriteString("Kiel-Klasse äöü");
        writer.WriteString(null);
        writer.WriteString("");
        writer.WriteBytes(new byte[] { 1, 2, 3 }, 0, 3);

        var reader = new PacketReader(writer.ToArray());
        Assert.Equal(0xAB, reader.ReadByte());
        Assert.True(reader.ReadBool());
        Assert.Equal(0xBEEF, reader.ReadUInt16());
        Assert.Equal(0xDEADBEEFu, reader.ReadUInt32());
        Assert.Equal(0x0123456789ABCDEFul, reader.ReadUInt64());
        Assert.Equal(300u, reader.ReadVarUInt());
        Assert.Equal(-5, reader.ReadVarInt());
        Assert.Equal(3.25f, reader.ReadFloat());
        Assert.Equal(-12345.678, reader.ReadDouble());
        Assert.Equal("Kiel-Klasse äöü", reader.ReadString());
        Assert.Null(reader.ReadString());
        Assert.Equal("", reader.ReadString());
        Assert.Equal(new byte[] { 1, 2, 3 }, reader.ReadBytes());
        Assert.Equal(0, reader.Remaining);
    }

    [Theory]
    [InlineData(0u, 1)]
    [InlineData(127u, 1)]
    [InlineData(128u, 2)]
    [InlineData(uint.MaxValue, 5)]
    public void VarUIntUsesMinimalBytes(uint value, int expectedBytes)
    {
        var writer = new PacketWriter();
        writer.WriteVarUInt(value);
        Assert.Equal(expectedBytes, writer.Length);
        Assert.Equal(value, new PacketReader(writer.ToArray()).ReadVarUInt());
    }

    [Theory]
    [InlineData(int.MinValue)]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public void VarIntRoundTripsExtremes(int value)
    {
        var writer = new PacketWriter();
        writer.WriteVarInt(value);
        Assert.Equal(value, new PacketReader(writer.ToArray()).ReadVarInt());
    }

    [Fact]
    public void TruncatedInputThrowsMalformedPacket()
    {
        var writer = new PacketWriter();
        writer.WriteUInt32(42);
        var reader = new PacketReader(writer.Buffer, 0, 3);
        Assert.Throws<MalformedPacketException>(() => reader.ReadUInt32());
    }

    [Fact]
    public void OversizedStringLengthThrowsInsteadOfAllocating()
    {
        var writer = new PacketWriter();
        writer.WriteVarUInt(1_000_000);
        var reader = new PacketReader(writer.ToArray());
        Assert.Throws<MalformedPacketException>(() => reader.ReadString());
    }
}
