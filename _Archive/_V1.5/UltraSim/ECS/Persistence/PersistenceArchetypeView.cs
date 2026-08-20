using System;

namespace UltraSim.ECS.Persistence
{
    /// <summary>
    /// Read-only view of a single runtime archetype through the persistence lens.
    /// Exposes only the surface needed to enumerate entities and their persistent
    /// component ids during a save barrier.
    ///
    /// The view is valid for the duration of the save barrier that produced it.
    /// Do not retain it across structural changes.
    /// </summary>
    public sealed class PersistenceArchetypeView
    {
        /// <summary>
        /// The underlying archetype. Accessible to persistence infrastructure in
        /// <c>UltraSim.ECS.Persistence</c> (same assembly) for column access in later slices.
        /// </summary>
        internal Archetype Archetype { get; }

        /// <summary>Number of entities currently in this archetype.</summary>
        public int EntityCount => Archetype.Count;

        /// <summary>
        /// Live entity span for this archetype.
        /// Valid only during the save barrier — do not cache across structural changes.
        /// </summary>
        public ReadOnlySpan<Entity> Entities => Archetype.GetEntitySpan();

        /// <summary>All component ids present in this archetype (in id-ascending order).</summary>
        public ReadOnlySpan<ComponentId> ComponentIds => Archetype.ComponentIds;

        /// <summary>Returns true if this archetype contains the given component.</summary>
        public bool HasComponent(ComponentId id) => Archetype.HasComponent(id);

        internal PersistenceArchetypeView(Archetype archetype)
        {
            Archetype = archetype;
        }
    }
}
