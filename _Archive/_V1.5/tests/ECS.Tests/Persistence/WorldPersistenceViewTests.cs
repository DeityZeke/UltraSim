using System;
using System.Collections.Generic;
using ECS.Tests;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    /// <summary>
    /// Tests for <see cref="WorldPersistenceView"/> and <see cref="PersistenceArchetypeView"/>.
    ///
    /// Each test builds its own World with a fresh component registration so that
    /// archetype ids and ComponentIds are predictable.
    /// </summary>
    public sealed class WorldPersistenceViewTests : IDisposable
    {
        public WorldPersistenceViewTests() => TestRuntime.ResetStatics();
        public void Dispose()             => TestRuntime.ResetStatics();

        // ---------------------------------------------------------------------------
        // Test components
        // ---------------------------------------------------------------------------

        [PersistenceId(9001)]
        private struct PositionC : ISerializableComponent<PositionC>
        {
            public float X, Y;
            public static void Serialize(ref BitWriter w, in PositionC v) { w.WriteSingle(v.X); w.WriteSingle(v.Y); }
            public static PositionC Deserialize(ref BitReader r, ushort _) => new PositionC { X = r.ReadSingle(), Y = r.ReadSingle() };
        }

        [PersistenceId(9002)]
        private struct HealthC : ISerializableComponent<HealthC>
        {
            public int Hp;
            public static void Serialize(ref BitWriter w, in HealthC v) => w.WriteInt32(v.Hp);
            public static HealthC Deserialize(ref BitReader r, ushort _) => new HealthC { Hp = r.ReadInt32() };
        }

        // Non-serializable component — must never appear in persistence output.
        private struct TransientC : IComponent<TransientC> { }

        // ---------------------------------------------------------------------------
        // Helpers
        // ---------------------------------------------------------------------------

        /// <summary>
        /// Builds and initializes a World, then spawns entities.
        /// <paramref name="setup"/> receives the world AFTER initialization so it can
        /// enqueue entities; after it returns the barrier is flushed.
        /// </summary>
        private static World BuildWorld(Action<World> setup)
        {
            var world = new World();
            world.Initialize();
            setup(world);
            world.ProcessStructuralQueues();
            return world;
        }

        private static ArchetypeSignature Sig(params ComponentId[] ids)
            => new ArchetypeSignature(ids);

        // ---------------------------------------------------------------------------
        // Empty world
        // ---------------------------------------------------------------------------

        [Fact]
        public void EmptyWorld_NoPersistentEntities_ReturnsEmptyList()
        {
            ComponentManager.Register<PersistentTag>();

            var world = BuildWorld(_ => { });
            var view = new WorldPersistenceView(world);

            Assert.Equal(0, view.PersistentEntityCount);
            Assert.Empty(view.GetPersistentArchetypes());
        }

        // ---------------------------------------------------------------------------
        // Only non-persistent entities
        // ---------------------------------------------------------------------------

        [Fact]
        public void WorldWithOnlyNonPersistentEntities_ReturnsEmpty()
        {
            ComponentManager.Register<PersistentTag>();
            var posId = ComponentManager.Register<PositionC>();

            var world = BuildWorld(w =>
            {
                // Spawn 3 entities WITHOUT PersistentTag
                w.EnqueueCreateDefault(Sig(posId));
                w.EnqueueCreateDefault(Sig(posId));
                w.EnqueueCreateDefault(Sig(posId));
            });

            var view = new WorldPersistenceView(world);

            Assert.Equal(0, view.PersistentEntityCount);
            Assert.Empty(view.GetPersistentArchetypes());
        }

        // ---------------------------------------------------------------------------
        // Mixed: persistent and non-persistent
        // ---------------------------------------------------------------------------

        [Fact]
        public void MixedWorld_OnlyPersistentArchetypesReturned()
        {
            var tagId = ComponentManager.Register<PersistentTag>();
            var posId = ComponentManager.Register<PositionC>();

            var world = BuildWorld(w =>
            {
                // Persistent: PersistentTag + PositionC
                w.EnqueueCreateDefault(Sig(tagId, posId));
                w.EnqueueCreateDefault(Sig(tagId, posId));
                // Non-persistent: PositionC only
                w.EnqueueCreateDefault(Sig(posId));
            });

            var view = new WorldPersistenceView(world);
            IReadOnlyList<PersistenceArchetypeView> archetypes = view.GetPersistentArchetypes();

            Assert.Equal(2, view.PersistentEntityCount);
            Assert.Single(archetypes);
            Assert.True(archetypes[0].HasComponent(tagId));
            Assert.Equal(2, archetypes[0].EntityCount);
        }

        // ---------------------------------------------------------------------------
        // Multiple persistent archetypes (different component sets)
        // ---------------------------------------------------------------------------

        [Fact]
        public void MultipleDistinctPersistentArchetypes_AllReturned()
        {
            var tagId = ComponentManager.Register<PersistentTag>();
            var posId = ComponentManager.Register<PositionC>();
            var hpId  = ComponentManager.Register<HealthC>();

            var world = BuildWorld(w =>
            {
                // Archetype A: Tag + Position (2 entities)
                w.EnqueueCreateDefault(Sig(tagId, posId));
                w.EnqueueCreateDefault(Sig(tagId, posId));
                // Archetype B: Tag + Health (3 entities)
                w.EnqueueCreateDefault(Sig(tagId, hpId));
                w.EnqueueCreateDefault(Sig(tagId, hpId));
                w.EnqueueCreateDefault(Sig(tagId, hpId));
            });

            var view = new WorldPersistenceView(world);
            IReadOnlyList<PersistenceArchetypeView> archetypes = view.GetPersistentArchetypes();

            Assert.Equal(5, view.PersistentEntityCount);
            Assert.Equal(2, archetypes.Count);

            int totalEntities = 0;
            foreach (var av in archetypes)
            {
                Assert.True(av.HasComponent(tagId));
                totalEntities += av.EntityCount;
            }
            Assert.Equal(5, totalEntities);
        }

        // ---------------------------------------------------------------------------
        // Entity span correctness
        // ---------------------------------------------------------------------------

        [Fact]
        public void ArchetypeView_Entities_CountMatchesEntityCount()
        {
            var tagId = ComponentManager.Register<PersistentTag>();
            var posId = ComponentManager.Register<PositionC>();

            var world = BuildWorld(w =>
            {
                w.EnqueueCreateDefault(Sig(tagId, posId));
                w.EnqueueCreateDefault(Sig(tagId, posId));
                w.EnqueueCreateDefault(Sig(tagId, posId));
            });

            var view = new WorldPersistenceView(world);
            IReadOnlyList<PersistenceArchetypeView> archetypes = view.GetPersistentArchetypes();

            Assert.Single(archetypes);
            var av = archetypes[0];
            Assert.Equal(av.EntityCount, av.Entities.Length);
            Assert.Equal(3, av.Entities.Length);
        }

        [Fact]
        public void ArchetypeView_Entities_AreAllValid()
        {
            var tagId = ComponentManager.Register<PersistentTag>();

            var world = BuildWorld(w =>
            {
                w.EnqueueCreateDefault(Sig(tagId));
                w.EnqueueCreateDefault(Sig(tagId));
            });

            var view = new WorldPersistenceView(world);
            var av = view.GetPersistentArchetypes()[0];

            foreach (Entity e in av.Entities)
                Assert.True(e.IsValid(), $"Entity {e} should be valid");
        }

        // ---------------------------------------------------------------------------
        // ComponentIds surface
        // ---------------------------------------------------------------------------

        [Fact]
        public void ArchetypeView_ComponentIds_ContainsPersistentTag()
        {
            var tagId = ComponentManager.Register<PersistentTag>();
            var posId = ComponentManager.Register<PositionC>();

            var world = BuildWorld(w => w.EnqueueCreateDefault(Sig(tagId, posId)));

            var view = new WorldPersistenceView(world);
            var av = view.GetPersistentArchetypes()[0];

            bool foundTag = false;
            bool foundPos = false;
            foreach (ComponentId cid in av.ComponentIds)
            {
                if (cid == tagId) foundTag = true;
                if (cid == posId) foundPos = true;
            }

            Assert.True(foundTag, "ComponentIds must include PersistentTag");
            Assert.True(foundPos, "ComponentIds must include PositionC");
        }

        [Fact]
        public void ArchetypeView_HasComponent_CorrectForMemberAndNonMember()
        {
            var tagId     = ComponentManager.Register<PersistentTag>();
            var posId     = ComponentManager.Register<PositionC>();
            var transId   = ComponentManager.Register<TransientC>();

            // Archetype: Tag + Position (no TransientC)
            var world = BuildWorld(w => w.EnqueueCreateDefault(Sig(tagId, posId)));

            var view = new WorldPersistenceView(world);
            var av = view.GetPersistentArchetypes()[0];

            Assert.True(av.HasComponent(tagId));
            Assert.True(av.HasComponent(posId));
            Assert.False(av.HasComponent(transId));
        }

        // ---------------------------------------------------------------------------
        // Stability: calling GetPersistentArchetypes twice returns same data
        // ---------------------------------------------------------------------------

        [Fact]
        public void GetPersistentArchetypes_CalledTwice_ReturnsSameCount()
        {
            var tagId = ComponentManager.Register<PersistentTag>();
            var posId = ComponentManager.Register<PositionC>();

            var world = BuildWorld(w =>
            {
                w.EnqueueCreateDefault(Sig(tagId, posId));
                w.EnqueueCreateDefault(Sig(tagId, posId));
            });

            var view = new WorldPersistenceView(world);
            var first  = view.GetPersistentArchetypes();
            var second = view.GetPersistentArchetypes();

            Assert.Equal(first.Count, second.Count);
            for (int i = 0; i < first.Count; i++)
                Assert.Equal(first[i].EntityCount, second[i].EntityCount);
        }

        // ---------------------------------------------------------------------------
        // Zero-entity archetypes are not included
        // ---------------------------------------------------------------------------

        [Fact]
        public void ArchetypesWith_ZeroEntities_AreExcluded()
        {
            var tagId = ComponentManager.Register<PersistentTag>();
            var posId = ComponentManager.Register<PositionC>();

            // Create a world where a persistent archetype exists but was never populated.
            // We'll create the archetype by registering its signature, then never enqueue to it.
            // Instead spawn one entity in a different persistent archetype.
            var world = BuildWorld(w =>
            {
                // Only spawn into Tag+Position — Tag alone is never populated.
                w.EnqueueCreateDefault(Sig(tagId, posId));
            });

            var view = new WorldPersistenceView(world);
            IReadOnlyList<PersistenceArchetypeView> archetypes = view.GetPersistentArchetypes();

            // Only the populated archetype should appear; empty ones are excluded.
            Assert.All(archetypes, av => Assert.True(av.EntityCount > 0));
        }

        // ---------------------------------------------------------------------------
        // Traversal does not require server/runtime types (compile-time check)
        // This test documents that WorldPersistenceView lives entirely in UltraSim.ECS.Persistence.
        // ---------------------------------------------------------------------------

        [Fact]
        public void WorldPersistenceView_TypeIsInPersistenceNamespace()
        {
            Assert.Equal("UltraSim.ECS.Persistence", typeof(WorldPersistenceView).Namespace);
            Assert.Equal("UltraSim.ECS.Persistence", typeof(PersistenceArchetypeView).Namespace);
        }
    }
}
