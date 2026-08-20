using System.Buffers;
using UltraSim.ECS.Persistence;

namespace UltraSim.ECS;

public sealed partial class World
{
    /// <summary>
    /// Captures a grouped in-memory snapshot of all persistent entities in the world.
    /// </summary>
    public EntityPersistenceSnapshot CapturePersistenceSnapshot(PersistenceTypeRegistry registry)
        => CapturePersistenceSnapshot(registry, ReadOnlySpan<ComponentId>.Empty, excludeEmpty: true);

    /// <summary>
    /// Captures a grouped in-memory snapshot of all persistent entities in the filtered world view.
    /// </summary>
    public EntityPersistenceSnapshot CapturePersistenceSnapshot(
        PersistenceTypeRegistry registry,
        ReadOnlySpan<ComponentId> required,
        bool excludeEmpty = true)
    {
        EnsureNotShutdown();
        ArgumentNullException.ThrowIfNull(registry);

        if (!registry.IsSealed)
            throw new InvalidOperationException("PersistenceTypeRegistry must be sealed before capturing a snapshot.");

        var view = new WorldPersistenceView(this, required, excludeEmpty);
        if (view.EntityCount == 0)
            return EntityPersistenceSnapshot.Empty;

        var groups = new Dictionary<PersistenceSignature, SnapshotGroupBuilder>();

        for (int i = 0; i < view.Archetypes.Count; i++)
        {
            PersistenceArchetypeView archetypeView = view.Archetypes[i];
            if (!PersistenceSignatureBuilder.TryBuild(Schema, archetypeView, registry, out PersistenceSignature? signature, out PersistenceComponentDescriptor[] descriptors))
                continue;

            if (!groups.TryGetValue(signature!, out SnapshotGroupBuilder? builder))
            {
                builder = new SnapshotGroupBuilder(signature!, descriptors);
                groups.Add(signature!, builder);
            }

            builder.CaptureFrom(archetypeView);
        }

        if (groups.Count == 0)
            return EntityPersistenceSnapshot.Empty;

        var capturedGroups = new EntityPersistenceGroup[groups.Count];
        int write = 0;
        foreach ((_, SnapshotGroupBuilder builder) in groups)
            capturedGroups[write++] = builder.Build();

        return new EntityPersistenceSnapshot(capturedGroups);
    }

    /// <summary>
    /// Restores an in-memory persistence snapshot by appending its entities into this world.
    /// </summary>
    public int RestorePersistenceSnapshot(EntityPersistenceSnapshot snapshot)
    {
        EnsureNotShutdown();
        ArgumentNullException.ThrowIfNull(snapshot);

        int restored = 0;
        for (int i = 0; i < snapshot.Groups.Count; i++)
            restored += RestorePersistenceGroup(snapshot.Groups[i]);

        return restored;
    }

    private int RestorePersistenceGroup(EntityPersistenceGroup group)
    {
        int entityCount = group.EntityCount;
        if (entityCount == 0)
            return 0;

        var targetComponentIds = new ComponentId[group.Sections.Length];
        for (int i = 0; i < group.Sections.Length; i++)
            targetComponentIds[i] = Schema.GetComponentId(group.Sections[i].Descriptor.ClrType);

        ArchetypeSignature signature = new(targetComponentIds);
        Archetype archetype = Archetypes.GetOrCreate(signature);
        archetype.Reserve(entityCount);

        Entity[] rented = ArrayPool<Entity>.Shared.Rent(entityCount);
        try
        {
            Span<Entity> entities = rented.AsSpan(0, entityCount);
            int allocated = EntityStore.AllocateBulk(entities);
            int startSlot = archetype.AddEntitiesUninitialized(entities[..allocated]);
            EntityStore.SetLocationsBulkAssumeValid(entities[..allocated], archetype, startSlot);
            _dirty.MarkUnique(entities[..allocated]);

            for (int i = 0; i < group.Sections.Length; i++)
            {
                EntityPersistenceSection section = group.Sections[i];
                ComponentId targetId = targetComponentIds[i];
                PersistenceComponentAccessCache.Get(section.Descriptor.ClrType)
                    .ApplyRange(archetype, targetId, startSlot, section.Values);
                _dirty.MarkUnique(targetId, entities[..allocated]);
            }

            return allocated;
        }
        finally
        {
            ArrayPool<Entity>.Shared.Return(rented, clearArray: false);
        }
    }

    private sealed class SnapshotGroupBuilder
    {
        private readonly PersistenceComponentDescriptor[] _descriptors;
        private readonly List<ulong> _entityPackedIds = new();
        private readonly List<object?>[] _valuesBySection;

        public SnapshotGroupBuilder(PersistenceSignature signature, PersistenceComponentDescriptor[] descriptors)
        {
            Signature = signature;
            _descriptors = descriptors;
            _valuesBySection = new List<object?>[descriptors.Length];
            for (int i = 0; i < descriptors.Length; i++)
                _valuesBySection[i] = new List<object?>();
        }

        public PersistenceSignature Signature { get; }

        public void CaptureFrom(PersistenceArchetypeView archetypeView)
        {
            ReadOnlySpan<Entity> entities = archetypeView.Entities;
            for (int i = 0; i < entities.Length; i++)
                _entityPackedIds.Add(entities[i].Packed);

            for (int sectionIndex = 0; sectionIndex < _descriptors.Length; sectionIndex++)
            {
                PersistenceComponentDescriptor descriptor = _descriptors[sectionIndex];
                Array values = PersistenceComponentAccessCache.Get(descriptor.ClrType).Capture(archetypeView, descriptor.RuntimeComponentId);
                List<object?> destination = _valuesBySection[sectionIndex];
                for (int i = 0; i < values.Length; i++)
                    destination.Add(values.GetValue(i));
            }
        }

        public EntityPersistenceGroup Build()
        {
            var sections = new EntityPersistenceSection[_descriptors.Length];
            for (int i = 0; i < _descriptors.Length; i++)
            {
                Type valueType = _descriptors[i].ClrType;
                Array array = Array.CreateInstance(valueType, _valuesBySection[i].Count);
                for (int valueIndex = 0; valueIndex < _valuesBySection[i].Count; valueIndex++)
                    array.SetValue(_valuesBySection[i][valueIndex], valueIndex);

                sections[i] = new EntityPersistenceSection(_descriptors[i], array);
            }

            return new EntityPersistenceGroup(Signature, _entityPackedIds.ToArray(), sections);
        }
    }
}
