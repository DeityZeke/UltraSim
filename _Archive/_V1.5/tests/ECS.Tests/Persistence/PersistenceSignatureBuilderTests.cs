using System;
using System.Collections.Generic;
using ECS.Tests;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class PersistenceSignatureBuilderTests : IDisposable
    {
        [PersistenceId(7001)]
        private struct PositionC : ISerializableComponent<PositionC>
        {
            public float X;
            public static void Serialize(ref BitWriter w, in PositionC v) => w.WriteSingle(v.X);
            public static PositionC Deserialize(ref BitReader r, ushort _) => new PositionC { X = r.ReadSingle() };
        }

        [PersistenceId(7002)]
        private struct HealthC : ISerializableComponent<HealthC>
        {
            public int Hp;
            public static void Serialize(ref BitWriter w, in HealthC v) => w.WriteInt32(v.Hp);
            public static HealthC Deserialize(ref BitReader r, ushort _) => new HealthC { Hp = r.ReadInt32() };
        }

        private struct TransientC : IComponent<TransientC> { }

        [PersistenceId(7003)]
        private struct VisualStateC : ISerializableComponent<VisualStateC>
        {
            public byte State;
            public static void Serialize(ref BitWriter w, in VisualStateC v) => w.WriteByte(v.State);
            public static VisualStateC Deserialize(ref BitReader r, ushort _) => new VisualStateC { State = r.ReadByte() };
        }

        public PersistenceSignatureBuilderTests() => TestRuntime.ResetStatics();
        public void Dispose() => TestRuntime.ResetStatics();

        private static World BuildWorld(Action<World> setup)
        {
            var world = new World();
            world.Initialize();
            setup(world);
            world.ProcessStructuralQueues();
            return world;
        }

        private static ArchetypeSignature Sig(params ComponentId[] ids) => new ArchetypeSignature(ids);

        private static PersistenceTypeRegistry BuildRegistry(params Type[] types)
        {
            var registry = new PersistenceTypeRegistry();
            for (int i = 0; i < types.Length; i++)
                registry.Register(types[i], PersistenceKind.Component);
            registry.Seal();
            return registry;
        }

        [Fact]
        public void TryBuild_SortsDescriptorsByPersistenceId()
        {
            ComponentId tagId = ComponentManager.Register<PersistentTag>();
            ComponentId healthId = ComponentManager.Register<HealthC>();
            ComponentId posId = ComponentManager.Register<PositionC>();

            var world = BuildWorld(w => w.EnqueueCreateDefault(Sig(tagId, healthId, posId)));
            var view = new WorldPersistenceView(world);
            var archetype = view.GetPersistentArchetypes()[0];
            var registry = BuildRegistry(typeof(PersistentTag), typeof(HealthC), typeof(PositionC));

            bool built = PersistenceSignatureBuilder.TryBuild(archetype, registry, out PersistenceSignature? signature, out PersistenceComponentDescriptor[] descriptors);

            Assert.True(built);
            Assert.NotNull(signature);
            Assert.Equal([1u, 7001u, 7002u], signature!.PersistenceIds.ToArray());
            Assert.Equal(3, descriptors.Length);
            Assert.Equal(1u, descriptors[0].PersistenceId);
            Assert.Equal(7001u, descriptors[1].PersistenceId);
            Assert.Equal(7002u, descriptors[2].PersistenceId);
        }

        [Fact]
        public void TryBuild_SkipsNonSerializableComponents()
        {
            ComponentId tagId = ComponentManager.Register<PersistentTag>();
            ComponentId posId = ComponentManager.Register<PositionC>();
            ComponentId transientId = ComponentManager.Register<TransientC>();

            var world = BuildWorld(w => w.EnqueueCreateDefault(Sig(tagId, posId, transientId)));
            var view = new WorldPersistenceView(world);
            var archetype = view.GetPersistentArchetypes()[0];
            var registry = BuildRegistry(typeof(PersistentTag), typeof(PositionC));

            bool built = PersistenceSignatureBuilder.TryBuild(archetype, registry, out PersistenceSignature? signature, out PersistenceComponentDescriptor[] descriptors);

            Assert.True(built);
            Assert.NotNull(signature);
            Assert.Equal([1u, 7001u], signature!.PersistenceIds.ToArray());
            Assert.Equal(2, descriptors.Length);
        }

        [Fact]
        public void TryBuild_SamePersistentSubsetAcrossRuntimeArchetypes_YieldsEqualSignatures()
        {
            ComponentId tagId = ComponentManager.Register<PersistentTag>();
            ComponentId posId = ComponentManager.Register<PositionC>();
            ComponentId transientId = ComponentManager.Register<TransientC>();

            var world = BuildWorld(w =>
            {
                w.EnqueueCreateDefault(Sig(tagId, posId));
                w.EnqueueCreateDefault(Sig(tagId, posId, transientId));
            });

            var view = new WorldPersistenceView(world);
            IReadOnlyList<PersistenceArchetypeView> archetypes = view.GetPersistentArchetypes();
            var registry = BuildRegistry(typeof(PersistentTag), typeof(PositionC));

            bool builtA = PersistenceSignatureBuilder.TryBuild(archetypes[0], registry, out PersistenceSignature? sigA, out _);
            bool builtB = PersistenceSignatureBuilder.TryBuild(archetypes[1], registry, out PersistenceSignature? sigB, out _);

            Assert.True(builtA);
            Assert.True(builtB);
            Assert.Equal(sigA, sigB);
        }

        [Fact]
        public void TryBuild_MissingPersistentRegistrationForSerializableComponent_Throws()
        {
            ComponentId tagId = ComponentManager.Register<PersistentTag>();
            ComponentId posId = ComponentManager.Register<PositionC>();

            var world = BuildWorld(w => w.EnqueueCreateDefault(Sig(tagId, posId)));
            var view = new WorldPersistenceView(world);
            var archetype = view.GetPersistentArchetypes()[0];
            var registry = BuildRegistry(typeof(PersistentTag));

            Assert.Throws<InvalidOperationException>(() =>
                PersistenceSignatureBuilder.TryBuild(archetype, registry, out _, out _));
        }

        [Fact]
        public void EntityBlockIndexRecord_HoldsExpectedValues()
        {
            var signature = new PersistenceSignature([1u, 7001u, 7003u]);
            var record = new EntityBlockIndexRecord(signature, EntityCount: 5, BinOffset: 128, BinLength: 512);

            Assert.Equal(signature, record.Signature);
            Assert.Equal(5, record.EntityCount);
            Assert.Equal(128L, record.BinOffset);
            Assert.Equal(512, record.BinLength);
        }
    }
}
