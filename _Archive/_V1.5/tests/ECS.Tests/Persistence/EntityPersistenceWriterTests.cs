using System;
using System.Collections.Generic;
using System.IO;
using ECS.Tests;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class EntityPersistenceWriterTests : IDisposable
    {
        [PersistenceId(8001)]
        private struct PositionC : ISerializableComponent<PositionC>
        {
            public const ushort SchemaVersion = 2;
            public float X;
            public static void Serialize(ref BitWriter w, in PositionC v) => w.WriteSingle(v.X);
            public static PositionC Deserialize(ref BitReader r, ushort _) => new PositionC { X = r.ReadSingle() };
        }

        [PersistenceId(8002)]
        private struct HealthC : ISerializableComponent<HealthC>
        {
            public const ushort SchemaVersion = 7;
            public int Hp;
            public static void Serialize(ref BitWriter w, in HealthC v) => w.WriteInt32(v.Hp);
            public static HealthC Deserialize(ref BitReader r, ushort _) => new HealthC { Hp = r.ReadInt32() };
        }

        private struct TransientC : IComponent<TransientC> { }
        private struct ItemTagC : IComponent<ItemTagC> { }
        private struct MobileTagC : IComponent<MobileTagC> { }

        public EntityPersistenceWriterTests() => TestRuntime.ResetStatics();
        public void Dispose() => TestRuntime.ResetStatics();

        private static PersistenceTypeRegistry BuildRegistry(params Type[] types)
        {
            var registry = new PersistenceTypeRegistry();
            for (int i = 0; i < types.Length; i++)
                registry.Register(types[i], PersistenceKind.Component);
            registry.Seal();
            return registry;
        }

        private static World BuildWorld(Action<World> setup)
        {
            var world = new World();
            world.Initialize();
            setup(world);
            world.ProcessStructuralQueues();
            return world;
        }

        private static ArchetypeSignature Sig(params ComponentId[] ids) => new ArchetypeSignature(ids);

        [Fact]
        public void Write_GroupsByPersistenceSignature_AndEmitsIndexAndBin()
        {
            string root = Path.Combine(Path.GetTempPath(), $"entitysave_{Guid.NewGuid():N}");
            try
            {
                ComponentId tagId = ComponentManager.Register<PersistentTag>();
                ComponentId posId = ComponentManager.Register<PositionC>();
                ComponentId healthId = ComponentManager.Register<HealthC>();
                ComponentId transientId = ComponentManager.Register<TransientC>();

                var world = BuildWorld(w =>
                {
                    w.EnqueueCreateDefault(Sig(tagId, posId));
                    w.EnqueueCreateDefault(Sig(tagId, posId, transientId));
                    w.EnqueueCreateDefault(Sig(tagId, healthId));
                    w.EnqueueCreateDefault(Sig(posId)); // non-persistent entity
                });

                var registry = BuildRegistry(typeof(PersistentTag), typeof(PositionC), typeof(HealthC));
                var paths = new SaveSetPaths(root);

                EntityPersistenceWriteResult result = EntityPersistenceWriter.Write(world, paths, registry);

                Assert.Equal(2, result.BlockCount);
                Assert.Equal(3, result.PersistentEntityCount);
                Assert.Equal(2, result.Blocks.Length);

                using var idxReader = new BinaryDataReader(paths.EntitiesIdx);
                SaveFileHeaders.ReadAndValidateHeader(idxReader, SaveFileHeaders.EntityIdxMagic, SaveFileHeaders.EntityIdxVersion);
                int blockCount = idxReader.ReadInt32();
                Assert.Equal(2, blockCount);

                int componentCountA = idxReader.ReadInt32();
                Assert.Equal(2, componentCountA);
                Assert.Equal(1u, idxReader.ReadUInt32());
                Assert.Equal(8001u, idxReader.ReadUInt32());
                Assert.Equal(2, idxReader.ReadInt32());
                long blockAOffset = idxReader.ReadInt64();
                int blockALength = idxReader.ReadInt32();
                Assert.True(blockALength > 0);

                int componentCountB = idxReader.ReadInt32();
                Assert.Equal(2, componentCountB);
                Assert.Equal(1u, idxReader.ReadUInt32());
                Assert.Equal(8002u, idxReader.ReadUInt32());
                Assert.Equal(1, idxReader.ReadInt32());
                long blockBOffset = idxReader.ReadInt64();
                int blockBLength = idxReader.ReadInt32();
                Assert.True(blockBLength > 0);
                Assert.True(blockBOffset > blockAOffset);

                using var binReader = new BinaryDataReader(paths.EntitiesBin);
                SaveFileHeaders.ReadAndValidateHeader(binReader, SaveFileHeaders.EntityBinMagic, SaveFileHeaders.EntityBinVersion);

                Assert.Equal(blockAOffset, binReader.Position);
                ulong firstPacked = binReader.ReadUInt64();
                ulong secondPacked = binReader.ReadUInt64();
                Assert.NotEqual(0UL, firstPacked);
                Assert.NotEqual(0UL, secondPacked);

                Assert.Equal(1u, binReader.ReadUInt32());
                Assert.Equal((ushort)1, binReader.ReadUInt16());
                int tagPayloadBytes = binReader.ReadInt32();
                Assert.Equal(8, tagPayloadBytes); // 2 zero-length payload prefixes
                Assert.Equal(2, binReader.ReadInt32());
                Assert.Equal(0, binReader.ReadInt32());
                Assert.Equal(0, binReader.ReadInt32());

                Assert.Equal(8001u, binReader.ReadUInt32());
                Assert.Equal((ushort)2, binReader.ReadUInt16());
                int posPayloadBytes = binReader.ReadInt32();
                Assert.True(posPayloadBytes > 0);
                Assert.Equal(2, binReader.ReadInt32());
                Assert.Equal(4, binReader.ReadInt32());
                byte[] payloadA = new byte[4];
                Assert.Equal(4, binReader.ReadBytes(payloadA, 0, 4));
                Assert.Equal(4, binReader.ReadInt32());
                byte[] payloadB = new byte[4];
                Assert.Equal(4, binReader.ReadBytes(payloadB, 0, 4));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void WriteArchetypes_WritesOnlySelectedTaggedSubset()
        {
            string root = Path.Combine(Path.GetTempPath(), $"entitysave_subset_{Guid.NewGuid():N}");
            try
            {
                ComponentId tagId = ComponentManager.Register<PersistentTag>();
                ComponentId posId = ComponentManager.Register<PositionC>();
                ComponentId itemTagId = ComponentManager.Register<ItemTagC>();
                ComponentId mobileTagId = ComponentManager.Register<MobileTagC>();

                var world = BuildWorld(w =>
                {
                    w.EnqueueCreateDefault(Sig(tagId, posId, itemTagId));
                    w.EnqueueCreateDefault(Sig(tagId, posId, itemTagId));
                    w.EnqueueCreateDefault(Sig(tagId, posId, mobileTagId));
                });

                var registry = BuildRegistry(typeof(PersistentTag), typeof(PositionC));
                var view = new WorldPersistenceView(world);
                IReadOnlyList<PersistenceArchetypeView> persistent = view.GetPersistentArchetypes();
                var itemsOnly = new System.Collections.Generic.List<PersistenceArchetypeView>(1);

                for (int i = 0; i < persistent.Count; i++)
                {
                    if (persistent[i].HasComponent(itemTagId))
                        itemsOnly.Add(persistent[i]);
                }

                EntityPersistenceWriteResult result = EntityPersistenceWriter.WriteArchetypes(
                    itemsOnly,
                    new SaveSetPaths(root),
                    registry);

                Assert.Equal(1, result.BlockCount);
                Assert.Equal(2, result.PersistentEntityCount);

                using var idxReader = new BinaryDataReader(Path.Combine(root, "Entities", "entities.idx"));
                SaveFileHeaders.ReadAndValidateHeader(idxReader, SaveFileHeaders.EntityIdxMagic, SaveFileHeaders.EntityIdxVersion);
                Assert.Equal(1, idxReader.ReadInt32());
                Assert.Equal(2, idxReader.ReadInt32());
                Assert.Equal(1u, idxReader.ReadUInt32());
                Assert.Equal(8001u, idxReader.ReadUInt32());
                Assert.Equal(2, idxReader.ReadInt32());
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
