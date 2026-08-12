namespace UltraSim.ECS;

/// <summary>
/// Post-seal accounting of mechanically enumerable world-owned payload. This is
/// not process working set or CLR object size; array/object headers, allocator
/// overhead, JIT code, thread stacks, and shared schema payload are excluded.
/// </summary>
public readonly struct WorldReservedPayloadSnapshot
{
    internal WorldReservedPayloadSnapshot(
        long entitySlotMetadataBytes,
        long archetypeRegistryIndexBytes,
        long archetypeSignatureAndColumnBytes,
        long archetypeStoreMetadataBytes,
        long chunkArenaSlotMetadataBytes,
        long preparedPlanSlotBytes,
        long mainStructuralCommandBytes,
        long schedulerBytes,
        long lifecycleHistoryBytes,
        long compiledContentBytes,
        long persistenceIdentityBytes,
        long nativeChunkPayloadBytes)
    {
        EntitySlotMetadataBytes = entitySlotMetadataBytes;
        ArchetypeRegistryIndexBytes = archetypeRegistryIndexBytes;
        ArchetypeSignatureAndColumnBytes = archetypeSignatureAndColumnBytes;
        ArchetypeStoreMetadataBytes = archetypeStoreMetadataBytes;
        ChunkArenaSlotMetadataBytes = chunkArenaSlotMetadataBytes;
        PreparedPlanSlotBytes = preparedPlanSlotBytes;
        MainStructuralCommandBytes = mainStructuralCommandBytes;
        SchedulerBytes = schedulerBytes;
        LifecycleHistoryBytes = lifecycleHistoryBytes;
        CompiledContentBytes = compiledContentBytes;
        PersistenceIdentityBytes = persistenceIdentityBytes;
        NativeChunkPayloadBytes = nativeChunkPayloadBytes;
        KnownManagedPayloadBytes = checked(
            entitySlotMetadataBytes + archetypeRegistryIndexBytes +
            archetypeSignatureAndColumnBytes + archetypeStoreMetadataBytes +
            chunkArenaSlotMetadataBytes + preparedPlanSlotBytes +
            mainStructuralCommandBytes + schedulerBytes + lifecycleHistoryBytes +
            compiledContentBytes + persistenceIdentityBytes);
        TotalKnownPayloadBytes = checked(KnownManagedPayloadBytes + nativeChunkPayloadBytes);
    }

    public long EntitySlotMetadataBytes { get; }
    public long ArchetypeRegistryIndexBytes { get; }
    public long ArchetypeSignatureAndColumnBytes { get; }
    public long ArchetypeStoreMetadataBytes { get; }
    public long ChunkArenaSlotMetadataBytes { get; }

    /// <summary>
    /// Top-level query, public-transition, and structural-route slot arrays.
    /// Nested cold plan objects and their route/transfer arrays are explicitly
    /// excluded until they expose a common accounting contract.
    /// </summary>
    public long PreparedPlanSlotBytes { get; }

    public long MainStructuralCommandBytes { get; }

    /// <summary>Scheduler-owned graph, job, lane, and worker payload; excludes the main arena.</summary>
    public long SchedulerBytes { get; }

    /// <summary>Fixed lifecycle-record ring payload; zero when publication is disabled.</summary>
    public long LifecycleHistoryBytes { get; }

    /// <summary>
    /// Frozen compiled-prefab entries, default descriptors/bytes, and retained
    /// stable-key/display character payload. Cold compiler temporaries are excluded.
    /// </summary>
    public long CompiledContentBytes { get; }

    /// <summary>Fixed durable-ID, generation, prefab-provenance-hash, and reverse-lookup arrays.</summary>
    public long PersistenceIdentityBytes { get; }

    public long KnownManagedPayloadBytes { get; }
    public long NativeChunkPayloadBytes { get; }
    public long TotalKnownPayloadBytes { get; }

    public const string Exclusions =
        "CLR object/array headers and alignment, allocator overhead, nested prepared-plan route/transfer arrays, " +
        "shared ComponentSchema payload, cold content-compiler temporaries, CLR strings/array headers, " +
        "durable prefab-key dictionary strings/headers, " +
        "JIT/code pages, thread stacks, and non-ECS process working set.";
}
