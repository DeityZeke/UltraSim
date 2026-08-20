namespace UltraSim.ECS.Persistence;

/// <summary>
/// In-memory captured snapshot of persistent ECS entity data.
/// </summary>
public sealed class EntityPersistenceSnapshot
{
    public static readonly EntityPersistenceSnapshot Empty = new(Array.Empty<EntityPersistenceGroup>());

    public EntityPersistenceSnapshot(EntityPersistenceGroup[] groups)
    {
        Groups = groups ?? throw new ArgumentNullException(nameof(groups));

        int entityCount = 0;
        for (int i = 0; i < groups.Length; i++)
            entityCount += groups[i].EntityCount;

        EntityCount = entityCount;
    }

    public int EntityCount { get; }
    public IReadOnlyList<EntityPersistenceGroup> Groups { get; }
    public int GroupCount => Groups.Count;
}

/// <summary>
/// Captured persistent entity group sharing one canonical persistence signature.
/// </summary>
public sealed class EntityPersistenceGroup
{
    public EntityPersistenceGroup(
        PersistenceSignature signature,
        ulong[] sourceEntityPackedIds,
        EntityPersistenceSection[] sections)
    {
        Signature = signature ?? throw new ArgumentNullException(nameof(signature));
        SourceEntityPackedIds = sourceEntityPackedIds ?? throw new ArgumentNullException(nameof(sourceEntityPackedIds));
        Sections = sections ?? throw new ArgumentNullException(nameof(sections));

        for (int i = 0; i < sections.Length; i++)
        {
            if (sections[i].ValueCount != SourceEntityPackedIds.Length)
            {
                throw new ArgumentException(
                    $"Section '{sections[i].Descriptor.ClrType.FullName}' value count {sections[i].ValueCount} did not match entity count {SourceEntityPackedIds.Length}.",
                    nameof(sections));
            }
        }
    }

    public PersistenceSignature Signature { get; }
    public ulong[] SourceEntityPackedIds { get; }
    public EntityPersistenceSection[] Sections { get; }
    public int EntityCount => SourceEntityPackedIds.Length;
}

/// <summary>
/// Captured values for one persistent component within an entity group.
/// </summary>
public sealed class EntityPersistenceSection
{
    public EntityPersistenceSection(PersistenceComponentDescriptor descriptor, Array values)
    {
        Descriptor = descriptor;
        Values = values ?? throw new ArgumentNullException(nameof(values));
    }

    public PersistenceComponentDescriptor Descriptor { get; }
    public Array Values { get; }
    public int ValueCount => Values.Length;
}
