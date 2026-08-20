using System;

namespace UltraSim.ECS
{
    public enum StructuralBarrierPhase : byte
    {
        PreSystems = 0,
        PostSystems = 1,
        Manual = 2,
    }

    public readonly struct StructuralBarrierEvent
    {
        public readonly uint WorldTick;
        public readonly StructuralBarrierPhase Phase;
        public readonly int CreatesApplied;
        public readonly int SpawnsApplied;
        public readonly int DestroysApplied;
        public readonly int RemovesApplied;
        public readonly int AddsApplied;

        public StructuralBarrierEvent(
            uint worldTick,
            StructuralBarrierPhase phase,
            int createsApplied,
            int spawnsApplied,
            int destroysApplied,
            int removesApplied,
            int addsApplied)
        {
            WorldTick = worldTick;
            Phase = phase;
            CreatesApplied = createsApplied;
            SpawnsApplied = spawnsApplied;
            DestroysApplied = destroysApplied;
            RemovesApplied = removesApplied;
            AddsApplied = addsApplied;
        }
    }

    public interface IWorldEventSink
    {
        bool WantsEntityCreatedBatches { get; }
        void OnEntitiesCreated(StructuralBarrierPhase phase, ReadOnlySpan<Entity> entities);
        void OnStructuralBarrierApplied(in StructuralBarrierEvent value);
    }

    public sealed class NoOpWorldEventSink : IWorldEventSink
    {
        public static readonly NoOpWorldEventSink Instance = new NoOpWorldEventSink();

        private NoOpWorldEventSink()
        {
        }

        public bool WantsEntityCreatedBatches => false;

        public void OnEntitiesCreated(StructuralBarrierPhase phase, ReadOnlySpan<Entity> entities)
        {
        }

        public void OnStructuralBarrierApplied(in StructuralBarrierEvent value)
        {
        }
    }
}
