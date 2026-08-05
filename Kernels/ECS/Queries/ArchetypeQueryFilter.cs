namespace UltraSim.ECS;

/// <summary>
/// Cold-built, world-owned normalized structural and enable-state query filter.
/// Enabled and disabled components are implicitly required structurally.
/// </summary>
internal readonly struct ArchetypeQueryFilter
{
    private readonly ComponentId[]? _requiredComponents;
    private readonly ComponentId[]? _excludedComponents;
    private readonly ComponentId[]? _enabledComponents;
    private readonly ComponentId[]? _disabledComponents;

    private ArchetypeQueryFilter(
        WorldId worldId,
        ComponentId[] requiredComponents,
        ComponentId[] excludedComponents,
        ComponentId[] enabledComponents,
        ComponentId[] disabledComponents)
    {
        WorldId = worldId;
        _requiredComponents = requiredComponents;
        _excludedComponents = excludedComponents;
        _enabledComponents = enabledComponents;
        _disabledComponents = disabledComponents;
    }

    internal WorldId WorldId { get; }

    internal bool IsValid =>
        WorldId.IsValid &&
        _requiredComponents is not null &&
        _excludedComponents is not null &&
        _enabledComponents is not null &&
        _disabledComponents is not null;

    internal ReadOnlySpan<ComponentId> RequiredComponents => _requiredComponents;

    internal ReadOnlySpan<ComponentId> ExcludedComponents => _excludedComponents;

    internal ReadOnlySpan<ComponentId> EnabledComponents => _enabledComponents;

    internal ReadOnlySpan<ComponentId> DisabledComponents => _disabledComponents;

    internal bool Excludes(ComponentId componentId) =>
        Contains(_excludedComponents, componentId);

    internal static ArchetypeQueryFilter Create(
        WorldId worldId,
        ComponentSchema schema,
        ReadOnlySpan<ComponentId> requiredComponents,
        ReadOnlySpan<ComponentId> excludedComponents,
        ReadOnlySpan<ComponentId> enabledComponents,
        ReadOnlySpan<ComponentId> disabledComponents)
    {
        if (!worldId.IsValid)
            throw new ArgumentException("A valid world ID is required.", nameof(worldId));
        ArgumentNullException.ThrowIfNull(schema);

        ComponentId[] normalizedRequired = Normalize(
            schema,
            requiredComponents,
            requireEnableable: false,
            nameof(requiredComponents));
        ComponentId[] normalizedExcluded = Normalize(
            schema,
            excludedComponents,
            requireEnableable: false,
            nameof(excludedComponents));
        ComponentId[] normalizedEnabled = Normalize(
            schema,
            enabledComponents,
            requireEnableable: true,
            nameof(enabledComponents));
        ComponentId[] normalizedDisabled = Normalize(
            schema,
            disabledComponents,
            requireEnableable: true,
            nameof(disabledComponents));

        if (Intersects(normalizedEnabled, normalizedDisabled))
        {
            throw new ArgumentException(
                "A component cannot be required enabled and disabled by the same query filter.",
                nameof(disabledComponents));
        }

        ComponentId[] effectiveRequired = MergeUnique(
            normalizedRequired,
            normalizedEnabled,
            normalizedDisabled);
        if (Intersects(effectiveRequired, normalizedExcluded))
        {
            throw new ArgumentException(
                "A required, enabled, or disabled component cannot also be excluded.",
                nameof(excludedComponents));
        }

        return new ArchetypeQueryFilter(
            worldId,
            effectiveRequired,
            normalizedExcluded,
            normalizedEnabled,
            normalizedDisabled);
    }

    private static ComponentId[] Normalize(
        ComponentSchema schema,
        ReadOnlySpan<ComponentId> components,
        bool requireEnableable,
        string parameterName)
    {
        if (components.IsEmpty)
            return [];

        ComponentId[] sorted = components.ToArray();
        Array.Sort(sorted);
        int uniqueCount = 0;
        for (int index = 0; index < sorted.Length; index++)
        {
            ComponentId componentId = sorted[index];
            if (!schema.TryGetMetadata(componentId, out ComponentMetadata metadata))
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    componentId,
                    "A query-filter component ID is not in this world schema.");
            }
            if (requireEnableable && !metadata.IsEnableable)
            {
                throw new ArgumentException(
                    $"Component {componentId} is not enableable and cannot be used as an enable-state filter.",
                    parameterName);
            }
            if (uniqueCount == 0 || sorted[uniqueCount - 1] != componentId)
                sorted[uniqueCount++] = componentId;
        }

        if (uniqueCount == sorted.Length)
            return sorted;

        var normalized = new ComponentId[uniqueCount];
        sorted.AsSpan(0, uniqueCount).CopyTo(normalized);
        return normalized;
    }

    private static ComponentId[] MergeUnique(
        ReadOnlySpan<ComponentId> first,
        ReadOnlySpan<ComponentId> second,
        ReadOnlySpan<ComponentId> third)
    {
        if (second.IsEmpty && third.IsEmpty)
            return first.ToArray();

        var combined = new ComponentId[first.Length + second.Length + third.Length];
        first.CopyTo(combined);
        second.CopyTo(combined.AsSpan(first.Length));
        third.CopyTo(combined.AsSpan(first.Length + second.Length));
        Array.Sort(combined);

        int uniqueCount = 0;
        for (int index = 0; index < combined.Length; index++)
        {
            if (uniqueCount == 0 || combined[uniqueCount - 1] != combined[index])
                combined[uniqueCount++] = combined[index];
        }
        if (uniqueCount == combined.Length)
            return combined;

        var normalized = new ComponentId[uniqueCount];
        combined.AsSpan(0, uniqueCount).CopyTo(normalized);
        return normalized;
    }

    private static bool Intersects(
        ReadOnlySpan<ComponentId> first,
        ReadOnlySpan<ComponentId> second)
    {
        int firstIndex = 0;
        int secondIndex = 0;
        while (firstIndex < first.Length && secondIndex < second.Length)
        {
            int comparison = first[firstIndex].CompareTo(second[secondIndex]);
            if (comparison == 0)
                return true;
            if (comparison < 0)
                firstIndex++;
            else
                secondIndex++;
        }
        return false;
    }

    private static bool Contains(ComponentId[]? components, ComponentId componentId)
    {
        if (components is null)
            return false;

        int low = 0;
        int high = components.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = components[middle].CompareTo(componentId);
            if (comparison == 0)
                return true;
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        return false;
    }
}
