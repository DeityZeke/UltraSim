using System;

namespace UltraSim.ECS
{
    public interface IResolvedPrefab<TSelf> : IPrefab<TSelf>
        where TSelf : unmanaged, IResolvedPrefab<TSelf>
    {
        static abstract ComponentId[] ResolvedComponentIds { get; }
        void ApplyResolvedTo(Archetype archetype, int slot, ReadOnlySpan<int> columns);
    }
}
