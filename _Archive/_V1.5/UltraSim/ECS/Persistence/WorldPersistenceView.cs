using System;
using System.Collections.Generic;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Read-only view of a <see cref="World"/> through the persistence lens.
    /// Filters the archetype set to only those containing <see cref="PersistentTag"/>.
    ///
    /// Intended to be created once at the start of a save barrier and discarded after.
    /// All returned views are valid for the duration of the barrier; do not retain them
    /// across structural changes.
    /// </summary>
    public sealed class WorldPersistenceView
    {
        private readonly World _world;
        private readonly ComponentId _persistentTagId;

        /// <param name="world">The world to traverse. Must be initialized and sealed.</param>
        public WorldPersistenceView(World world)
        {
            ArgumentNullException.ThrowIfNull(world);
            _world = world;
            _persistentTagId = ComponentManager.GetId<PersistentTag>();
        }

        /// <summary>
        /// Total live entity count across all archetypes that contain
        /// <see cref="PersistentTag"/>. Useful for pre-allocation and diagnostics.
        /// </summary>
        public int PersistentEntityCount
        {
            get
            {
                int total = 0;
                ReadOnlySpan<Archetype> archetypes = _world.Archetypes.AsSpan();
                for (int i = 0; i < archetypes.Length; i++)
                {
                    var arch = archetypes[i];
                    if (arch.HasComponent(_persistentTagId))
                        total += arch.Count;
                }
                return total;
            }
        }

        /// <summary>
        /// Returns views for all archetypes that contain <see cref="PersistentTag"/>
        /// and have at least one entity.
        ///
        /// Order is the archetype creation order within <see cref="ArchetypeManager"/>.
        /// For a given component registration sequence this order is deterministic.
        /// The deterministic sort by persistence signature (Slice 5) will replace this
        /// ordering once persistence signatures are available.
        /// </summary>
        public IReadOnlyList<PersistenceArchetypeView> GetPersistentArchetypes()
        {
            ReadOnlySpan<Archetype> archetypes = _world.Archetypes.AsSpan();
            var result = new List<PersistenceArchetypeView>(archetypes.Length);

            for (int i = 0; i < archetypes.Length; i++)
            {
                var arch = archetypes[i];
                if (arch.HasComponent(_persistentTagId) && arch.Count > 0)
                    result.Add(new PersistenceArchetypeView(arch));
            }

            return result;
        }
    }
}
