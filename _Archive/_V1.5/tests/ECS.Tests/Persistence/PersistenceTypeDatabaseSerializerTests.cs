using System;
using System.IO;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class PersistenceTypeDatabaseSerializerTests
    {
        [PersistenceId(3101)]
        private struct TestComponentA { }

        [PersistenceId(3102)]
        private struct TestComponentB { }

        [PersistenceId(4101)]
        private sealed class TestSystemA { }

        [Fact]
        public void WriteReadAndValidate_RoundTripsRegistrySnapshot()
        {
            string path = Path.Combine(Path.GetTempPath(), $"types_{Guid.NewGuid():N}.tdb");
            try
            {
                var registry = new PersistenceTypeRegistry();
                registry.Register<TestComponentB>(PersistenceKind.Component);
                registry.Register<TestSystemA>(PersistenceKind.System);
                registry.Register<TestComponentA>(PersistenceKind.Component);
                registry.Seal();

                int written = PersistenceTypeDatabaseSerializer.Write(path, registry);
                PersistenceTypeRecord[] loaded = PersistenceTypeDatabaseSerializer.ReadAndValidate(path, registry);

                Assert.Equal(3, written);
                Assert.Equal(3, loaded.Length);
                Assert.Equal(3101u, loaded[0].PersistenceId);
                Assert.Equal(3102u, loaded[1].PersistenceId);
                Assert.Equal(4101u, loaded[2].PersistenceId);
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }

        [Fact]
        public void ReadAndValidate_UnknownPersistenceId_Throws()
        {
            string path = Path.Combine(Path.GetTempPath(), $"types_bad_{Guid.NewGuid():N}.tdb");
            try
            {
                var registry = new PersistenceTypeRegistry();
                registry.Register<TestComponentA>(PersistenceKind.Component);
                registry.Seal();

                using (var writer = new BinaryDataWriter(path))
                {
                    SaveFileHeaders.WriteHeader(writer, SaveFileHeaders.TypeDbMagic, SaveFileHeaders.TypeDbVersion);
                    writer.WriteInt32(1);
                    writer.WriteUInt32(9999u);
                    writer.WriteByte((byte)PersistenceKind.Component);
                    writer.WriteString(typeof(TestComponentA).FullName ?? nameof(TestComponentA));
                }

                Assert.Throws<InvalidDataException>(() => PersistenceTypeDatabaseSerializer.ReadAndValidate(path, registry));
            }
            finally
            {
                if (File.Exists(path))
                    File.Delete(path);
            }
        }
    }
}
