using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ECS.Tests;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class EntityPersistenceReaderTests : IDisposable
    {
        [PersistenceId(8101)]
        private struct PositionC : ISerializableComponent<PositionC>
        {
            public const ushort SchemaVersion = 2;
            public float X;
            public static void Serialize(ref BitWriter w, in PositionC v) => w.WriteSingle(v.X);
            public static PositionC Deserialize(ref BitReader r, ushort _) => new PositionC { X = r.ReadSingle() };
        }

        [PersistenceId(8102)]
        private struct HealthC : ISerializableComponent<HealthC>
        {
            public const ushort SchemaVersion = 3;
            public int Hp;
            public static void Serialize(ref BitWriter w, in HealthC v) => w.WriteInt32(v.Hp);
            public static HealthC Deserialize(ref BitReader r, ushort _) => new HealthC { Hp = r.ReadInt32() };
        }

        [PersistenceId(8103)]
        private struct LinkC : ISerializableComponent<LinkC>, IEntityReferenceFixup<LinkC>
        {
            public Entity Target;

            public static void Serialize(ref BitWriter w, in LinkC v) => w.WriteUInt64(v.Target.Packed);

            public static LinkC Deserialize(ref BitReader r, ushort _)
                => new LinkC { Target = new Entity(r.ReadUInt64()) };

            public static void FixupEntityReferences(ref LinkC value, EntityRemapTable remapTable)
            {
                if (!value.Target.IsValid())
                    return;

                if (!remapTable.TryGet(value.Target.Packed, out Entity remapped))
                {
                    throw new InvalidOperationException(
                        $"Could not remap saved entity reference 0x{value.Target.Packed:X16}.");
                }

                value.Target = remapped;
            }
        }

        private struct TransientC : IComponent<TransientC> { }

        public EntityPersistenceReaderTests() => TestRuntime.ResetStatics();
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
        public void ReadCreatePhase_CreatesEntitiesAndBuildsRemapTable()
        {
            string root = Path.Combine(Path.GetTempPath(), $"entityload_{Guid.NewGuid():N}");
            try
            {
                ComponentId tagId = ComponentManager.Register<PersistentTag>();
                ComponentId posId = ComponentManager.Register<PositionC>();
                ComponentId healthId = ComponentManager.Register<HealthC>();
                ComponentId transientId = ComponentManager.Register<TransientC>();

                var saveWorld = BuildWorld(w =>
                {
                    w.EnqueueCreateDefault(Sig(tagId, posId));
                    w.EnqueueCreateDefault(Sig(tagId, posId, transientId));
                    w.EnqueueCreateDefault(Sig(tagId, healthId));
                    w.EnqueueCreateDefault(Sig(posId));
                });

                var registry = BuildRegistry(typeof(PersistentTag), typeof(PositionC), typeof(HealthC));
                var paths = new SaveSetPaths(root);

                EntityPersistenceWriter.Write(saveWorld, paths, registry);

                var savedPersistentPacked = new List<ulong>();
                var saveView = new WorldPersistenceView(saveWorld);
                foreach (PersistenceArchetypeView archetype in saveView.GetPersistentArchetypes())
                {
                    foreach (Entity entity in archetype.Entities)
                        savedPersistentPacked.Add(entity.Packed);
                }

                var loadWorld = BuildWorld(w => w.EnqueueCreateDefault(Sig(posId)));
                Entity bootstrapEntity = default;
                loadWorld.Query<PositionC>(CaptureBootstrap);

                EntityPersistenceLoadResult result = EntityPersistenceReader.ReadCreatePhase(loadWorld, paths, registry);

                Assert.Empty(result.MissingFiles);
                Assert.Equal(2, result.BlockCount);
                Assert.Equal(3, result.LoadedEntityCount);
                Assert.Equal(3, result.RemapTable.Count);
                Assert.Equal(4, loadWorld.AliveEntityCount);

                var loadView = new WorldPersistenceView(loadWorld);
                IReadOnlyList<PersistenceArchetypeView> loadedArchetypes = loadView.GetPersistentArchetypes();
                Assert.Equal(2, loadedArchetypes.Count);
                Assert.Equal(3, loadedArchetypes.Sum(static a => a.EntityCount));

                bool foundRemappedEntityWithDifferentPacked = false;
                for (int i = 0; i < savedPersistentPacked.Count; i++)
                {
                    ulong oldPacked = savedPersistentPacked[i];
                    Assert.True(result.RemapTable.TryGet(oldPacked, out Entity remapped));
                    Assert.True(loadWorld.IsAlive(remapped));
                    Assert.NotEqual(bootstrapEntity, remapped);
                    if (remapped.Packed != oldPacked)
                        foundRemappedEntityWithDifferentPacked = true;
                }

                Assert.True(foundRemappedEntityWithDifferentPacked);

                void CaptureBootstrap(Entity entity, ref PositionC component) => bootstrapEntity = entity;
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Hydrate_RestoresPersistentComponentValues()
        {
            string root = Path.Combine(Path.GetTempPath(), $"entityhydrate_{Guid.NewGuid():N}");
            try
            {
                ComponentId tagId = ComponentManager.Register<PersistentTag>();
                ComponentId posId = ComponentManager.Register<PositionC>();
                ComponentId healthId = ComponentManager.Register<HealthC>();
                ComponentId transientId = ComponentManager.Register<TransientC>();

                var saveWorld = BuildWorld(w =>
                {
                    w.EnqueueCreateDefault(Sig(tagId, posId));
                    w.EnqueueCreateDefault(Sig(tagId, posId, transientId));
                    w.EnqueueCreateDefault(Sig(tagId, healthId));
                    w.EnqueueCreateDefault(Sig(posId));
                });

                int posIndex = 0;
                saveWorld.Query<PersistentTag, PositionC>((Entity entity, ref PersistentTag tag, ref PositionC pos) =>
                {
                    pos.X = posIndex == 0 ? 10.5f : 22.25f;
                    posIndex++;
                });

                saveWorld.Query<PersistentTag, HealthC>((Entity entity, ref PersistentTag tag, ref HealthC health) =>
                {
                    health.Hp = 77;
                });

                saveWorld.Query<PositionC>((Entity entity, ref PositionC pos) =>
                {
                    if (!saveWorld.HasComponent<PersistentTag>(entity))
                        pos.X = 999f;
                });

                var registry = BuildRegistry(typeof(PersistentTag), typeof(PositionC), typeof(HealthC));
                var paths = new SaveSetPaths(root);
                EntityPersistenceWriter.Write(saveWorld, paths, registry);

                var loadWorld = BuildWorld(_ => { });
                EntityPersistenceLoadResult loadResult = EntityPersistenceReader.ReadCreatePhase(loadWorld, paths, registry);
                EntityPersistenceReader.Hydrate(loadResult, paths);

                var persistentPosValues = new List<float>();
                loadWorld.Query<PersistentTag, PositionC>((Entity entity, ref PersistentTag tag, ref PositionC pos) =>
                {
                    persistentPosValues.Add(pos.X);
                });

                var healthValues = new List<int>();
                loadWorld.Query<PersistentTag, HealthC>((Entity entity, ref PersistentTag tag, ref HealthC health) =>
                {
                    healthValues.Add(health.Hp);
                });

                Assert.Equal([10.5f, 22.25f], persistentPosValues.OrderBy(static v => v).ToArray());
                Assert.Equal([77], healthValues.ToArray());
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void FixupEntityReferences_RemapsSavedEntityTargets()
        {
            string root = Path.Combine(Path.GetTempPath(), $"entityfixup_{Guid.NewGuid():N}");
            try
            {
                ComponentId tagId = ComponentManager.Register<PersistentTag>();
                ComponentId linkId = ComponentManager.Register<LinkC>();
                ComponentId transientId = ComponentManager.Register<TransientC>();

                var saveWorld = BuildWorld(w =>
                {
                    w.EnqueueCreateDefault(Sig(tagId, linkId));
                    w.EnqueueCreateDefault(Sig(tagId, linkId));
                });

                var savedEntities = new List<Entity>();
                saveWorld.Query<PersistentTag, LinkC>((Entity entity, ref PersistentTag tag, ref LinkC link) =>
                {
                    savedEntities.Add(entity);
                });

                Assert.Equal(2, savedEntities.Count);
                saveWorld.TrySetComponent(savedEntities[0], new LinkC { Target = savedEntities[1] });
                saveWorld.TrySetComponent(savedEntities[1], new LinkC { Target = savedEntities[0] });

                ulong oldPackedA = savedEntities[0].Packed;
                ulong oldPackedB = savedEntities[1].Packed;

                var registry = BuildRegistry(typeof(PersistentTag), typeof(LinkC));
                var paths = new SaveSetPaths(root);
                EntityPersistenceWriter.Write(saveWorld, paths, registry);

                var loadWorld = BuildWorld(w => w.EnqueueCreateDefault(Sig(transientId)));
                EntityPersistenceLoadResult loadResult = EntityPersistenceReader.ReadCreatePhase(loadWorld, paths, registry);
                EntityPersistenceReader.Hydrate(loadResult, paths);
                EntityPersistenceReader.FixupEntityReferences(loadResult);

                Assert.True(loadResult.RemapTable.TryGet(oldPackedA, out Entity newA));
                Assert.True(loadResult.RemapTable.TryGet(oldPackedB, out Entity newB));

                LinkC loadedA = loadWorld.GetComponent<LinkC>(newA);
                LinkC loadedB = loadWorld.GetComponent<LinkC>(newB);

                Assert.Equal(newB, loadedA.Target);
                Assert.Equal(newA, loadedB.Target);
                Assert.True(loadWorld.IsAlive(loadedA.Target));
                Assert.True(loadWorld.IsAlive(loadedB.Target));
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void ReadCreatePhase_MissingEntityFiles_ReturnsMissingFilesWithoutLoading()
        {
            string root = Path.Combine(Path.GetTempPath(), $"entityload_missing_{Guid.NewGuid():N}");
            try
            {
                ComponentManager.Register<PersistentTag>();
                ComponentManager.Register<PositionC>();
                var registry = BuildRegistry(typeof(PersistentTag), typeof(PositionC));
                var loadWorld = BuildWorld(_ => { });
                var paths = new SaveSetPaths(root);

                EntityPersistenceLoadResult result = EntityPersistenceReader.ReadCreatePhase(loadWorld, paths, registry);

                Assert.Equal(2, result.MissingFiles.Count);
                Assert.Contains(paths.EntitiesIdx, result.MissingFiles);
                Assert.Contains(paths.EntitiesBin, result.MissingFiles);
                Assert.Equal(0, result.BlockCount);
                Assert.Equal(0, result.LoadedEntityCount);
                Assert.Equal(0, result.RemapTable.Count);
                Assert.Equal(0, loadWorld.AliveEntityCount);
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
