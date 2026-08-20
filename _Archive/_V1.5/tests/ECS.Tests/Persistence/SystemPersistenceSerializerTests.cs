using System;
using System.IO;
using ECS.Tests;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class SystemPersistenceSerializerTests : IDisposable
    {
        [PersistenceId(5101)]
        private sealed class CounterSystem : SystemBase
        {
            public int Value;

            public override SystemDescriptor Descriptor =>
                new SystemDescriptor(nameof(CounterSystem), SystemStage.WorldSim, 1, 0, default, default);

            public override void Update(in SystemContext ctx) { }

            public override void Serialize(IDataWriter writer) => writer.WriteInt32(Value);
            public override void Deserialize(IDataReader reader) => Value = reader.ReadInt32();
        }

        private sealed class TransientSystem : SystemBase
        {
            public int Value;

            public override SystemDescriptor Descriptor =>
                new SystemDescriptor(nameof(TransientSystem), SystemStage.WorldSim, 1, 0, default, default);

            public override void Update(in SystemContext ctx) { }
            public override void Serialize(IDataWriter writer) => writer.WriteInt32(Value);
            public override void Deserialize(IDataReader reader) => Value = reader.ReadInt32();
        }

        public SystemPersistenceSerializerTests() => TestRuntime.ResetStatics();
        public void Dispose() => TestRuntime.ResetStatics();

        [Fact]
        public void WriteReadAll_RoundTripsRegisteredPersistentSystemsOnly()
        {
            string root = Path.Combine(Path.GetTempPath(), $"saves_{Guid.NewGuid():N}");
            try
            {
                var registry = new PersistenceTypeRegistry();
                registry.Register<CounterSystem>(PersistenceKind.System);
                registry.Seal();

                var sourceWorld = new World();
                var sourceCounter = new CounterSystem { Value = 1234 };
                var sourceTransient = new TransientSystem { Value = 88 };
                sourceWorld.RegisterSystem(sourceCounter);
                sourceWorld.RegisterSystem(sourceTransient);

                var paths = new SaveSetPaths(root);
                int written = SystemPersistenceSerializer.WriteAll(sourceWorld, paths, registry);

                Assert.Equal(1, written);
                Assert.True(File.Exists(paths.SystemFile(SystemPersistenceSerializer.GetSystemFileStem(
                    registry.GetAllSorted()[0]))));

                var loadWorld = new World();
                var loadCounter = new CounterSystem();
                var loadTransient = new TransientSystem { Value = 99 };
                loadWorld.RegisterSystem(loadCounter);
                loadWorld.RegisterSystem(loadTransient);

                SystemPersistenceLoadResult result = SystemPersistenceSerializer.ReadAll(loadWorld, paths, registry);

                Assert.Equal(1, result.LoadedSystemCount);
                Assert.Empty(result.MissingFiles);
                Assert.Equal(1234, loadCounter.Value);
                Assert.Equal(99, loadTransient.Value);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ReadAll_MissingSystemFile_IsReported()
        {
            string root = Path.Combine(Path.GetTempPath(), $"saves_missing_{Guid.NewGuid():N}");
            try
            {
                var registry = new PersistenceTypeRegistry();
                registry.Register<CounterSystem>(PersistenceKind.System);
                registry.Seal();

                var world = new World();
                world.RegisterSystem(new CounterSystem());

                var paths = new SaveSetPaths(root);
                paths.EnsureDirectoriesExist();

                SystemPersistenceLoadResult result = SystemPersistenceSerializer.ReadAll(world, paths, registry);

                Assert.Equal(0, result.LoadedSystemCount);
                Assert.Single(result.MissingFiles);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
