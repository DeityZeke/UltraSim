using System;
using System.IO;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class BinaryDataWriterReaderTests
    {
        // ---------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------

        private static (BinaryDataWriter writer, MemoryStream stream) MakeWriter()
        {
            var ms = new MemoryStream();
            return (new BinaryDataWriter(ms, leaveOpen: true), ms);
        }

        private static BinaryDataReader RewindAndWrap(MemoryStream ms)
        {
            ms.Position = 0;
            return new BinaryDataReader(ms, leaveOpen: true);
        }

        // ---------------------------------------------------------------------------
        // Primitive round-trips
        // ---------------------------------------------------------------------------

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Bool_RoundTrip(bool value)
        {
            var (writer, ms) = MakeWriter();
            writer.WriteBool(value);
            using var reader = RewindAndWrap(ms);
            Assert.Equal(value, reader.ReadBool());
        }

        [Theory]
        [InlineData((byte)0)]
        [InlineData((byte)127)]
        [InlineData((byte)255)]
        public void Byte_RoundTrip(byte value)
        {
            var (writer, ms) = MakeWriter();
            writer.WriteByte(value);
            using var reader = RewindAndWrap(ms);
            Assert.Equal(value, reader.ReadByte());
        }

        [Theory]
        [InlineData(0)]
        [InlineData(-1)]
        [InlineData(int.MinValue)]
        [InlineData(int.MaxValue)]
        [InlineData(42)]
        public void Int32_RoundTrip(int value)
        {
            var (writer, ms) = MakeWriter();
            writer.WriteInt32(value);
            using var reader = RewindAndWrap(ms);
            Assert.Equal(value, reader.ReadInt32());
        }

        [Theory]
        [InlineData(0u)]
        [InlineData(uint.MaxValue)]
        [InlineData(0xDEADBEEFu)]
        public void UInt32_RoundTrip(uint value)
        {
            var (writer, ms) = MakeWriter();
            writer.WriteUInt32(value);
            using var reader = RewindAndWrap(ms);
            Assert.Equal(value, reader.ReadUInt32());
        }

        [Theory]
        [InlineData((ushort)0)]
        [InlineData(ushort.MaxValue)]
        [InlineData((ushort)0xBEEF)]
        public void UInt16_RoundTrip(ushort value)
        {
            var (writer, ms) = MakeWriter();
            writer.WriteUInt16(value);
            using var reader = RewindAndWrap(ms);
            Assert.Equal(value, reader.ReadUInt16());
        }

        [Theory]
        [InlineData(0L)]
        [InlineData(-1L)]
        [InlineData(long.MinValue)]
        [InlineData(long.MaxValue)]
        public void Int64_RoundTrip(long value)
        {
            var (writer, ms) = MakeWriter();
            writer.WriteInt64(value);
            using var reader = RewindAndWrap(ms);
            Assert.Equal(value, reader.ReadInt64());
        }

        [Theory]
        [InlineData(0UL)]
        [InlineData(ulong.MaxValue)]
        [InlineData(0xCAFEBABE_DEADBEEFul)]
        public void UInt64_RoundTrip(ulong value)
        {
            var (writer, ms) = MakeWriter();
            writer.WriteUInt64(value);
            using var reader = RewindAndWrap(ms);
            Assert.Equal(value, reader.ReadUInt64());
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(1f)]
        [InlineData(-1f)]
        [InlineData(float.MaxValue)]
        [InlineData(float.NegativeInfinity)]
        public void Float_RoundTrip(float value)
        {
            var (writer, ms) = MakeWriter();
            writer.WriteFloat(value);
            using var reader = RewindAndWrap(ms);
            Assert.Equal(value, reader.ReadFloat());
        }

        // ---------------------------------------------------------------------------
        // String round-trips
        // ---------------------------------------------------------------------------

        [Theory]
        [InlineData("")]
        [InlineData("hello")]
        [InlineData("ASCII 123 !@#")]
        [InlineData("Unicode: \u00e9\u00e0\u0636\u4e2d\u6587")]
        public void String_RoundTrip(string value)
        {
            var (writer, ms) = MakeWriter();
            writer.WriteString(value);
            using var reader = RewindAndWrap(ms);
            Assert.Equal(value, reader.ReadString());
        }

        // ---------------------------------------------------------------------------
        // WriteBytes (array overload) round-trip
        // ---------------------------------------------------------------------------

        [Fact]
        public void WriteBytes_Array_RoundTrip()
        {
            byte[] source = [0x01, 0x02, 0x03, 0x04, 0x05];
            var (writer, ms) = MakeWriter();
            writer.WriteBytes(source, 1, 3); // write bytes 0x02, 0x03, 0x04

            using var reader = RewindAndWrap(ms);
            byte[] dest = new byte[3];
            int read = reader.ReadBytes(dest, 0, 3);

            Assert.Equal(3, read);
            Assert.Equal([0x02, 0x03, 0x04], dest);
        }

        // ---------------------------------------------------------------------------
        // WriteBytes (span overload) round-trip
        // ---------------------------------------------------------------------------

        [Fact]
        public void WriteBytes_Span_RoundTrip()
        {
            ReadOnlySpan<byte> source = [0xAA, 0xBB, 0xCC];
            var (writer, ms) = MakeWriter();
            writer.WriteBytes(source);

            ms.Position = 0;
            using var reader = new BinaryDataReader(ms, leaveOpen: true);
            Span<byte> dest = stackalloc byte[3];
            int read = reader.ReadBytes(dest);

            Assert.Equal(3, read);
            Assert.Equal(0xAA, dest[0]);
            Assert.Equal(0xBB, dest[1]);
            Assert.Equal(0xCC, dest[2]);
        }

        // ---------------------------------------------------------------------------
        // Mixed sequence round-trip
        // ---------------------------------------------------------------------------

        [Fact]
        public void MixedSequence_RoundTrip()
        {
            var (writer, ms) = MakeWriter();
            writer.WriteBool(true);
            writer.WriteInt32(-42);
            writer.WriteString("test");
            writer.WriteUInt64(ulong.MaxValue);
            writer.WriteFloat(3.14f);
            writer.WriteByte(0xFF);

            using var reader = RewindAndWrap(ms);
            Assert.True(reader.ReadBool());
            Assert.Equal(-42, reader.ReadInt32());
            Assert.Equal("test", reader.ReadString());
            Assert.Equal(ulong.MaxValue, reader.ReadUInt64());
            Assert.Equal(3.14f, reader.ReadFloat());
            Assert.Equal((byte)0xFF, reader.ReadByte());
        }

        // ---------------------------------------------------------------------------
        // Position tracking
        // ---------------------------------------------------------------------------

        [Fact]
        public void Position_AdvancesCorrectly()
        {
            var (writer, ms) = MakeWriter();
            Assert.Equal(0L, writer.Position);
            writer.WriteInt32(1);
            Assert.Equal(4L, writer.Position);
            writer.WriteBool(true);
            Assert.Equal(5L, writer.Position);
        }

        // ---------------------------------------------------------------------------
        // File-path constructors (integration)
        // ---------------------------------------------------------------------------

        [Fact]
        public void FilePath_WriteAndRead_RoundTrip()
        {
            string path = Path.Combine(Path.GetTempPath(), $"ecs_test_{Guid.NewGuid():N}.bin");
            try
            {
                using (var writer = new BinaryDataWriter(path))
                {
                    writer.WriteInt32(12345);
                    writer.WriteString("file-path test");
                }

                using var reader = new BinaryDataReader(path);
                Assert.Equal(12345, reader.ReadInt32());
                Assert.Equal("file-path test", reader.ReadString());
            }
            finally
            {
                if (File.Exists(path)) File.Delete(path);
            }
        }
    }
}
