using System;
using System.Collections.Generic;

namespace UltraSim.ECS.Persistence
{
    public sealed class EntityRemapTable
    {
        private readonly Dictionary<ulong, Entity> _map = new();

        public int Count => _map.Count;

        public bool TryGet(ulong oldPacked, out Entity entity)
            => _map.TryGetValue(oldPacked, out entity);

        internal void Add(ulong oldPacked, Entity entity)
        {
            if (!_map.TryAdd(oldPacked, entity))
                throw new InvalidOperationException($"Duplicate saved entity key 0x{oldPacked:X16} found while building the remap table.");
        }
    }
}
