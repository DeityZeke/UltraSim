using System;
using ECS.Tests;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using Xunit;

namespace ECS.Tests.Persistence
{
    public sealed class PersistentTagTests : IDisposable
    {
        public PersistentTagTests() => TestRuntime.ResetStatics();
        public void Dispose()       => TestRuntime.ResetStatics();

        // ---------------------------------------------------------------------------
        // Serialization contract — zero-payload
        // ---------------------------------------------------------------------------

        [Fact]
        public void Serialize_WritesZeroBytes()
        {
            byte[] buf = new byte[64];
            var writer = new BitWriter(buf);

            PersistentTag.Serialize(ref writer, default);

            Assert.Equal(0, writer.Position);
        }

        [Fact]
        public void Deserialize_ReadsZeroBytesAndReturnsDefault()
        {
            ReadOnlySpan<byte> empty = [];
            var reader = new BitReader(empty);

            PersistentTag tag = PersistentTag.Deserialize(ref reader, version: 1);

            Assert.Equal(0, reader.Position);
            Assert.Equal(default(PersistentTag), tag);
        }

        [Fact]
        public void SerializeDeserialize_RoundTrip_LeavesReaderPositionUnchanged()
        {
            byte[] buf = new byte[16];
            var writer = new BitWriter(buf);

            PersistentTag.Serialize(ref writer, default);
            int written = writer.Position; // must be 0

            ReadOnlySpan<byte> payload = writer.WrittenSpan;
            var reader = new BitReader(payload);
            PersistentTag result = PersistentTag.Deserialize(ref reader, version: 1);

            Assert.Equal(0, written);
            Assert.Equal(0, reader.Position);
            Assert.Equal(default(PersistentTag), result);
        }

        // ---------------------------------------------------------------------------
        // PersistenceId attribute
        // ---------------------------------------------------------------------------

        [Fact]
        public void PersistentTag_HasPersistenceIdAttribute()
        {
            var attr = (PersistenceIdAttribute?)Attribute.GetCustomAttribute(
                typeof(PersistentTag), typeof(PersistenceIdAttribute));

            Assert.NotNull(attr);
            Assert.Equal(1u, attr.Id);
        }

        // ---------------------------------------------------------------------------
        // ComponentManager registration
        // ---------------------------------------------------------------------------

        [Fact]
        public void Register_WithComponentManager_ReturnsValidId()
        {
            ComponentId id = ComponentManager.Register<PersistentTag>();

            Assert.True(id.Value >= 0);
        }

        [Fact]
        public void Register_WithComponentManager_IsIdempotent()
        {
            ComponentId id1 = ComponentManager.Register<PersistentTag>();
            ComponentId id2 = ComponentManager.Register<PersistentTag>();

            Assert.Equal(id1, id2);
        }

        [Fact]
        public void GetId_AfterRegistration_MatchesRegisteredId()
        {
            ComponentId registered = ComponentManager.Register<PersistentTag>();
            ComponentId retrieved  = ComponentManager.GetId<PersistentTag>();

            Assert.Equal(registered, retrieved);
        }

        // ---------------------------------------------------------------------------
        // PersistenceTypeRegistry registration
        // ---------------------------------------------------------------------------

        [Fact]
        public void Register_WithPersistenceTypeRegistry_RecordIsCorrect()
        {
            var registry = new PersistenceTypeRegistry();

            PersistenceTypeRecord record = registry.Register<PersistentTag>(PersistenceKind.Component);

            Assert.Equal(1u, record.PersistenceId);
            Assert.Equal(PersistenceKind.Component, record.Kind);
            Assert.Equal(typeof(PersistentTag), record.ClrType);
        }

        [Fact]
        public void Register_WithPersistenceTypeRegistry_SealedSnapshotContainsPersistentTag()
        {
            var registry = new PersistenceTypeRegistry();
            registry.Register<PersistentTag>(PersistenceKind.Component);
            registry.Seal();

            ReadOnlySpan<PersistenceTypeRecord> sorted = registry.GetAllSorted();

            Assert.Equal(1, sorted.Length);
            Assert.Equal(1u, sorted[0].PersistenceId);
        }

        // ---------------------------------------------------------------------------
        // Interface constraints
        // ---------------------------------------------------------------------------

        [Fact]
        public void PersistentTag_ImplementsISerializableComponent()
        {
            // Compile-time verified by the constraint on ISerializableComponent<TSelf>,
            // but we assert the interface at runtime for documentation clarity.
            Assert.True(typeof(ISerializableComponent<PersistentTag>)
                .IsAssignableFrom(typeof(PersistentTag)));
        }

        [Fact]
        public void PersistentTag_IsValueType()
        {
            Assert.True(typeof(PersistentTag).IsValueType);
        }
    }
}
