using System.IO;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class SaveFileHeadersTests
    {
        // ---------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------

        private static (MemoryStream ms, BinaryWriter bw) MakeWriter()
        {
            var ms = new MemoryStream();
            return (ms, new BinaryWriter(ms));
        }

        private static BinaryReader RewindAndWrap(MemoryStream ms)
        {
            ms.Position = 0;
            return new BinaryReader(ms);
        }

        // ---------------------------------------------------------------------------
        // Header size constant
        // ---------------------------------------------------------------------------

        [Fact]
        public void HeaderBytes_Is6()
        {
            Assert.Equal(6, SaveFileHeaders.HeaderBytes);
        }

        // ---------------------------------------------------------------------------
        // Write / validate round-trips for every file type
        // ---------------------------------------------------------------------------

        [Theory]
        [InlineData(SaveFileHeaders.MetaMagic,      SaveFileHeaders.MetaVersion)]
        [InlineData(SaveFileHeaders.TypeDbMagic,    SaveFileHeaders.TypeDbVersion)]
        [InlineData(SaveFileHeaders.EntityIdxMagic, SaveFileHeaders.EntityIdxVersion)]
        [InlineData(SaveFileHeaders.EntityBinMagic, SaveFileHeaders.EntityBinVersion)]
        [InlineData(SaveFileHeaders.SystemMagic,    SaveFileHeaders.SystemVersion)]
        public void WriteHeader_ThenValidate_Succeeds(uint magic, ushort version)
        {
            var (ms, bw) = MakeWriter();
            SaveFileHeaders.WriteHeader(bw, magic, version);
            bw.Flush();

            Assert.Equal(SaveFileHeaders.HeaderBytes, ms.Position);

            using var br = RewindAndWrap(ms);
            // Must not throw:
            SaveFileHeaders.ReadAndValidateHeader(br, magic, version);
        }

        // ---------------------------------------------------------------------------
        // Wrong magic throws
        // ---------------------------------------------------------------------------

        [Fact]
        public void ReadAndValidateHeader_WrongMagic_Throws()
        {
            var (ms, bw) = MakeWriter();
            SaveFileHeaders.WriteHeader(bw, 0xDEADBEEFu, 1);
            bw.Flush();

            using var br = RewindAndWrap(ms);
            Assert.Throws<InvalidDataException>(
                () => SaveFileHeaders.ReadAndValidateHeader(br, SaveFileHeaders.MetaMagic, SaveFileHeaders.MetaVersion));
        }

        // ---------------------------------------------------------------------------
        // Wrong version throws
        // ---------------------------------------------------------------------------

        [Fact]
        public void ReadAndValidateHeader_WrongVersion_Throws()
        {
            var (ms, bw) = MakeWriter();
            SaveFileHeaders.WriteHeader(bw, SaveFileHeaders.MetaMagic, version: 99);
            bw.Flush();

            using var br = RewindAndWrap(ms);
            Assert.Throws<InvalidDataException>(
                () => SaveFileHeaders.ReadAndValidateHeader(br, SaveFileHeaders.MetaMagic, SaveFileHeaders.MetaVersion));
        }

        // ---------------------------------------------------------------------------
        // Header is exactly at the start of the stream (no preceding bytes)
        // ---------------------------------------------------------------------------

        [Fact]
        public void WriteHeader_WritesExactly6Bytes_AtStartOfStream()
        {
            var (ms, bw) = MakeWriter();
            SaveFileHeaders.WriteHeader(bw, SaveFileHeaders.EntityIdxMagic, SaveFileHeaders.EntityIdxVersion);
            bw.Flush();

            Assert.Equal(6L, ms.Length);
        }
    }
}
