using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// World-owned, seal-compiled plan for every archetype containing one declared
/// read component and one distinct declared write component.
/// </summary>
internal sealed class ArchetypeReadWriteQueryPlan<TArena, TRead, TWrite> :
    IArchetypeQueryPlan<TArena>
    where TArena : class, IChunkArenaBackend
    where TRead : unmanaged, IComponent<TRead>
    where TWrite : unmanaged, IComponent<TWrite>
{
    private readonly ComponentFlags _readFlags;
    private readonly ComponentFlags _writeFlags;
    private readonly ComponentId[] _requiredComponents;
    private readonly ComponentId[] _excludedComponents;
    private readonly ComponentId[] _enabledComponents;
    private readonly ComponentId[] _disabledComponents;
    private ArchetypeReadWriteQueryRoute<TArena>[] _routes = [];
    private int _compiledArchetypeCount = -1;

    internal ArchetypeReadWriteQueryPlan(
        WorldId worldId,
        int planIndex,
        ComponentId readComponentId,
        ComponentFlags readFlags,
        ComponentId writeComponentId,
        ComponentFlags writeFlags,
        in ArchetypeQueryFilter filter)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        if (planIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(planIndex));
        if (!readComponentId.IsValid)
            throw new ArgumentException("A valid read component ID is required.", nameof(readComponentId));
        if (!writeComponentId.IsValid)
            throw new ArgumentException("A valid write component ID is required.", nameof(writeComponentId));
        if (readComponentId == writeComponentId)
            throw new ArgumentException("A query cannot declare the same component for read and write access.");
        if ((readFlags & ComponentFlags.Tag) != 0 || (writeFlags & ComponentFlags.Tag) != 0)
            throw new ArgumentException("Data query columns cannot be storage-free tags.");

        WorldId = worldId;
        PlanIndex = planIndex;
        ReadComponentId = readComponentId;
        WriteComponentId = writeComponentId;
        _readFlags = readFlags;
        _writeFlags = writeFlags;
        _requiredComponents = ArchetypeQueryPlanUtilities.MergeRequiredComponents(
            [readComponentId, writeComponentId],
            filter.RequiredComponents);
        _excludedComponents = filter.ExcludedComponents.ToArray();
        _enabledComponents = filter.EnabledComponents.ToArray();
        _disabledComponents = filter.DisabledComponents.ToArray();
    }

    internal WorldId WorldId { get; }

    internal int PlanIndex { get; }

    internal ComponentId ReadComponentId { get; }

    internal ComponentId WriteComponentId { get; }

    internal bool ReadIsEnableable => (_readFlags & ComponentFlags.Enableable) != 0;

    internal bool WriteIsEnableable => (_writeFlags & ComponentFlags.Enableable) != 0;

    internal bool IsCompiled => _compiledArchetypeCount >= 0;

    internal int MatchedArchetypeCount => _routes.Length;

    internal ReadOnlySpan<ComponentId> RequiredComponents => _requiredComponents;

    internal ReadOnlySpan<ComponentId> ExcludedComponents => _excludedComponents;

    internal ReadOnlySpan<ComponentId> EnabledComponents => _enabledComponents;

    internal ReadOnlySpan<ComponentId> DisabledComponents => _disabledComponents;

    internal ReadOnlySpan<ArchetypeReadWriteQueryRoute<TArena>> Routes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!IsCompiled)
                throw new InvalidOperationException("The query plan has not been compiled by world sealing.");
            return _routes;
        }
    }

    public void Compile(ArchetypeRegistry<TArena> registry)
    {
        ArgumentNullException.ThrowIfNull(registry);
        if (_compiledArchetypeCount == registry.Count)
            return;

        int matchCount = 0;
        for (int index = 0; index < registry.Count; index++)
        {
            ArchetypeStorage<TArena> storage = registry.GetStorage(new ArchetypeId((uint)index + 1));
            if (Matches(storage, out _, out _))
                matchCount++;
        }

        var routes = new ArchetypeReadWriteQueryRoute<TArena>[matchCount];
        int destination = 0;
        for (int index = 0; index < registry.Count; index++)
        {
            ArchetypeStorage<TArena> storage = registry.GetStorage(new ArchetypeId((uint)index + 1));
            if (!Matches(storage, out int readColumnIndex, out int writeColumnIndex))
                continue;

            ArchetypeQueryPlanUtilities.ValidateColumn<TRead>(
                storage.Columns[readColumnIndex],
                ReadComponentId,
                _readFlags);
            ArchetypeQueryPlanUtilities.ValidateColumn<TWrite>(
                storage.Columns[writeColumnIndex],
                WriteComponentId,
                _writeFlags);
            routes[destination++] = new ArchetypeReadWriteQueryRoute<TArena>(
                storage,
                readColumnIndex,
                writeColumnIndex,
                _enabledComponents,
                _disabledComponents);
        }

        if (destination != routes.Length)
        {
            ArchetypeQueryPlanUtilities.ThrowQueryInvariant(
                "Query match counting and route compilation disagree.");
        }
        _routes = routes;
        _compiledArchetypeCount = registry.Count;
    }

    public void Release()
    {
        _routes = [];
        _compiledArchetypeCount = -1;
    }

    private bool Matches(
        ArchetypeStorage<TArena> storage,
        out int readColumnIndex,
        out int writeColumnIndex)
    {
        readColumnIndex = storage.Store.GetColumnIndex(ReadComponentId);
        writeColumnIndex = storage.Store.GetColumnIndex(WriteComponentId);
        return readColumnIndex >= 0 &&
            writeColumnIndex >= 0 &&
            ArchetypeQueryPlanUtilities.SignatureMatches(
                storage.Signature,
                _requiredComponents,
                _excludedComponents);
    }

}
