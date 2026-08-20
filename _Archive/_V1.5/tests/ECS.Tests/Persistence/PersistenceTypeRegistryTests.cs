using System;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class PersistenceTypeRegistryTests
    {
        // ---------------------------------------------------------------------------
        // Test types
        // ---------------------------------------------------------------------------

        [PersistenceId(1001)]
        private struct TypeA { }

        [PersistenceId(1002)]
        private struct TypeB { }

        [PersistenceId(2000)]
        private struct TypeC { }

        // Intentionally shares id 1001 with TypeA — for duplicate detection tests.
        [PersistenceId(1001)]
        private struct TypeDuplicateId { }

        // Missing [PersistenceId] — for missing-attribute tests.
        private struct TypeNoAttribute { }

        // ---------------------------------------------------------------------------
        // Basic registration
        // ---------------------------------------------------------------------------

        [Fact]
        public void Register_ValidType_ReturnsCorrectRecord()
        {
            var registry = new PersistenceTypeRegistry();

            PersistenceTypeRecord record = registry.Register<TypeA>(PersistenceKind.Component);

            Assert.Equal(1001u, record.PersistenceId);
            Assert.Equal(PersistenceKind.Component, record.Kind);
            Assert.Equal(typeof(TypeA), record.ClrType);
        }

        [Fact]
        public void Register_SameTypeTwice_IsIdempotent()
        {
            var registry = new PersistenceTypeRegistry();

            PersistenceTypeRecord first  = registry.Register<TypeA>(PersistenceKind.Component);
            PersistenceTypeRecord second = registry.Register<TypeA>(PersistenceKind.Component);

            Assert.Equal(first, second);
            Assert.Equal(1, registry.Count);
        }

        [Fact]
        public void Register_SameTypeWithDifferentKind_Throws()
        {
            var registry = new PersistenceTypeRegistry();
            registry.Register<TypeA>(PersistenceKind.Component);

            var ex = Assert.Throws<InvalidOperationException>(
                () => registry.Register<TypeA>(PersistenceKind.System));

            Assert.Contains("already registered as persistence kind", ex.Message);
        }

        [Fact]
        public void Register_MultipleTypes_IncrementsCount()
        {
            var registry = new PersistenceTypeRegistry();

            registry.Register<TypeA>(PersistenceKind.Component);
            registry.Register<TypeB>(PersistenceKind.Component);
            registry.Register<TypeC>(PersistenceKind.System);

            Assert.Equal(3, registry.Count);
        }

        // ---------------------------------------------------------------------------
        // Missing [PersistenceId] attribute
        // ---------------------------------------------------------------------------

        [Fact]
        public void Register_TypeMissingAttribute_Throws()
        {
            var registry = new PersistenceTypeRegistry();

            var ex = Assert.Throws<InvalidOperationException>(
                () => registry.Register<TypeNoAttribute>(PersistenceKind.Component));

            Assert.Contains("PersistenceId", ex.Message);
        }

        // ---------------------------------------------------------------------------
        // Duplicate persistence id
        // ---------------------------------------------------------------------------

        [Fact]
        public void Register_DuplicatePersistenceId_Throws()
        {
            var registry = new PersistenceTypeRegistry();
            registry.Register<TypeA>(PersistenceKind.Component);

            var ex = Assert.Throws<InvalidOperationException>(
                () => registry.Register<TypeDuplicateId>(PersistenceKind.Component));

            Assert.Contains("1001", ex.Message);
        }

        // ---------------------------------------------------------------------------
        // Lookup
        // ---------------------------------------------------------------------------

        [Fact]
        public void TryGetById_ExistingId_ReturnsTrue()
        {
            var registry = new PersistenceTypeRegistry();
            registry.Register<TypeA>(PersistenceKind.Component);

            bool found = registry.TryGetById(1001u, out PersistenceTypeRecord record);

            Assert.True(found);
            Assert.Equal(typeof(TypeA), record.ClrType);
        }

        [Fact]
        public void TryGetById_MissingId_ReturnsFalse()
        {
            var registry = new PersistenceTypeRegistry();

            bool found = registry.TryGetById(9999u, out _);

            Assert.False(found);
        }

        [Fact]
        public void TryGetByType_ExistingType_ReturnsTrue()
        {
            var registry = new PersistenceTypeRegistry();
            registry.Register<TypeB>(PersistenceKind.System);

            bool found = registry.TryGetByType<TypeB>(out PersistenceTypeRecord record);

            Assert.True(found);
            Assert.Equal(1002u, record.PersistenceId);
            Assert.Equal(PersistenceKind.System, record.Kind);
        }

        // ---------------------------------------------------------------------------
        // Seal behaviour
        // ---------------------------------------------------------------------------

        [Fact]
        public void Seal_PreventsRegistration()
        {
            var registry = new PersistenceTypeRegistry();
            registry.Register<TypeA>(PersistenceKind.Component);
            registry.Seal();

            Assert.True(registry.IsSealed);
            Assert.Throws<InvalidOperationException>(
                () => registry.Register<TypeB>(PersistenceKind.Component));
        }

        [Fact]
        public void Seal_IsIdempotent()
        {
            var registry = new PersistenceTypeRegistry();
            registry.Seal();
            registry.Seal(); // must not throw
            Assert.True(registry.IsSealed);
        }

        [Fact]
        public void GetAllSorted_BeforeSeal_Throws()
        {
            var registry = new PersistenceTypeRegistry();
            registry.Register<TypeA>(PersistenceKind.Component);

            Assert.Throws<InvalidOperationException>(() => { _ = registry.GetAllSorted(); });
        }

        [Fact]
        public void GetAllSorted_AfterSeal_ReturnsSortedById()
        {
            var registry = new PersistenceTypeRegistry();
            registry.Register<TypeC>(PersistenceKind.System);    // id 2000
            registry.Register<TypeA>(PersistenceKind.Component); // id 1001
            registry.Register<TypeB>(PersistenceKind.Component); // id 1002
            registry.Seal();

            ReadOnlySpan<PersistenceTypeRecord> sorted = registry.GetAllSorted();

            Assert.Equal(3, sorted.Length);
            Assert.Equal(1001u, sorted[0].PersistenceId);
            Assert.Equal(1002u, sorted[1].PersistenceId);
            Assert.Equal(2000u, sorted[2].PersistenceId);
        }

        [Fact]
        public void GetAllSorted_DeterministicAcrossCallsAndRegistrationOrder()
        {
            // Register in reverse id order — sorted output must still be ascending.
            var r1 = new PersistenceTypeRegistry();
            r1.Register<TypeC>(PersistenceKind.Component); // 2000
            r1.Register<TypeB>(PersistenceKind.Component); // 1002
            r1.Register<TypeA>(PersistenceKind.Component); // 1001
            r1.Seal();

            var r2 = new PersistenceTypeRegistry();
            r2.Register<TypeA>(PersistenceKind.Component); // 1001
            r2.Register<TypeB>(PersistenceKind.Component); // 1002
            r2.Register<TypeC>(PersistenceKind.Component); // 2000
            r2.Seal();

            ReadOnlySpan<PersistenceTypeRecord> s1 = r1.GetAllSorted();
            ReadOnlySpan<PersistenceTypeRecord> s2 = r2.GetAllSorted();

            Assert.Equal(s1.Length, s2.Length);
            for (int i = 0; i < s1.Length; i++)
                Assert.Equal(s1[i], s2[i]);
        }
    }
}
