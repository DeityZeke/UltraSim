using System;
using System.IO;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class SaveSetManifestSerializerTests
    {
        [Fact]
        public void WriteRead_RoundTripsManifest()
        {
            string path = Path.Combine(Path.GetTempPath(), $"manifest_{Guid.NewGuid():N}.bin");
            try
            {
                var manifest = new SaveSetManifest(
                    DateTime.UtcNow.Ticks,
                    TypeRecordCount: 3,
                    SystemFileCount: 2,
                    EntityBlockCount: 7,
                    PersistentEntityCount: 42);

                SaveSetManifestSerializer.Write(path, manifest);
                SaveSetManifest loaded = SaveSetManifestSerializer.Read(path);

                Assert.Equal(manifest, loaded);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void Read_NegativeCounts_Throws()
        {
            string path = Path.Combine(Path.GetTempPath(), $"manifest_bad_{Guid.NewGuid():N}.bin");
            try
            {
                using (var writer = new BinaryDataWriter(path))
                {
                    SaveFileHeaders.WriteHeader(writer, SaveFileHeaders.MetaMagic, SaveFileHeaders.MetaVersion);
                    writer.WriteInt64(DateTime.UtcNow.Ticks);
                    writer.WriteInt32(-1);
                    writer.WriteInt32(0);
                    writer.WriteInt32(0);
                    writer.WriteInt32(0);
                }

                Assert.Throws<InvalidDataException>(() => SaveSetManifestSerializer.Read(path));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
