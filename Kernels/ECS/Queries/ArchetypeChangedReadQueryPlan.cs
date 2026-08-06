using System.Runtime.CompilerServices;

namespace UltraSim.ECS;

/// <summary>
/// World-owned, seal-compiled read plan that visits only chunks whose selected
/// tracked component is newer than an independently supplied consumer cursor.
/// </summary>
internal sealed class ArchetypeChangedReadQueryPlan<TArena, TRead> :
    IArchetypeQueryPlan<TArena>
    where TArena : class, IChunkArenaBackend
    where TRead : unmanaged, IComponent<TRead>
{
    private readonly ComponentFlags _readFlags;
    private readonly ComponentFlags _changedFlags;
    private readonly ComponentId[] _requiredComponents;
    private readonly ComponentId[] _excludedComponents;
    private readonly ComponentId[] _enabledComponents;
    private readonly ComponentId[] _disabledComponents;
    private ArchetypeChangedReadQueryRoute<TArena>[] _routes = [];
    private int _compiledArchetypeCount = -1;

    internal ArchetypeChangedReadQueryPlan(
        WorldId worldId,
        int planIndex,
        ComponentId readComponentId,
        ComponentFlags readFlags,
        ComponentId changedComponentId,
        ComponentFlags changedFlags,
        in ArchetypeQueryFilter filter)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        if (planIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(planIndex));
        if (!readComponentId.IsValid)
            throw new ArgumentException("A valid read component ID is required.", nameof(readComponentId));
        if (!changedComponentId.IsValid)
            throw new ArgumentException("A valid changed component ID is required.", nameof(changedComponentId));
        if ((readFlags & ComponentFlags.Tag) != 0)
            throw new ArgumentException("A changed-read data column cannot be a storage-free tag.");

        WorldId = worldId;
        PlanIndex = planIndex;
        ReadComponentId = readComponentId;
        ChangedComponentId = changedComponentId;
        _readFlags = readFlags;
        _changedFlags = changedFlags;
        _requiredComponents = ArchetypeQueryPlanUtilities.MergeRequiredComponents(
            [readComponentId, changedComponentId],
            filter.RequiredComponents);
        _excludedComponents = filter.ExcludedComponents.ToArray();
        _enabledComponents = filter.EnabledComponents.ToArray();
        _disabledComponents = filter.DisabledComponents.ToArray();
    }

    internal WorldId WorldId { get; }

    internal int PlanIndex { get; }

    internal ComponentId ReadComponentId { get; }

    internal ComponentId ChangedComponentId { get; }

    internal bool ReadIsEnableable => (_readFlags & ComponentFlags.Enableable) != 0;

    internal bool IsCompiled => _compiledArchetypeCount >= 0;

    internal int MatchedArchetypeCount => _routes.Length;

    internal ReadOnlySpan<ComponentId> RequiredComponents => _requiredComponents;

    internal ReadOnlySpan<ComponentId> ExcludedComponents => _excludedComponents;

    internal ReadOnlySpan<ComponentId> EnabledComponents => _enabledComponents;

    internal ReadOnlySpan<ComponentId> DisabledComponents => _disabledComponents;

    internal ReadOnlySpan<ArchetypeChangedReadQueryRoute<TArena>> Routes
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get
        {
            if (!IsCompiled)
                throw new InvalidOperationException("The changed-read plan has not been compiled by world sealing.");
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

        var routes = new ArchetypeChangedReadQueryRoute<TArena>[matchCount];
        int destination = 0;
        for (int index = 0; index < registry.Count; index++)
        {
            ArchetypeStorage<TArena> storage = registry.GetStorage(new ArchetypeId((uint)index + 1));
            if (!Matches(storage, out int readColumnIndex, out int changedColumnIndex))
                continue;

            ArchetypeQueryPlanUtilities.ValidateColumn<TRead>(
                storage.Columns[readColumnIndex],
                ReadComponentId,
                _readFlags);
            ArchetypeQueryPlanUtilities.ValidateTrackedColumn(
                storage.Columns[changedColumnIndex],
                ChangedComponentId,
                _changedFlags);
            routes[destination++] = new ArchetypeChangedReadQueryRoute<TArena>(
                storage,
                readColumnIndex,
                changedColumnIndex,
                _enabledComponents,
                _disabledComponents);
        }

        if (destination != routes.Length)
        {
            ArchetypeQueryPlanUtilities.ThrowQueryInvariant(
                "Changed-read match counting and route compilation disagree.");
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
        out int changedColumnIndex)
    {
        readColumnIndex = storage.Store.GetColumnIndex(ReadComponentId);
        changedColumnIndex = storage.Store.GetColumnIndex(ChangedComponentId);
        return readColumnIndex >= 0 &&
            changedColumnIndex >= 0 &&
            ArchetypeQueryPlanUtilities.SignatureMatches(
                storage.Signature,
                _requiredComponents,
                _excludedComponents);
    }
}
