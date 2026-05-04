namespace UltraSim.Server.Scripting;

public sealed class UltraSimRuntimeContentGroupRegistry : IUltraSimScriptContentGroupRegistry
{
    private readonly Dictionary<string, UltraSimRuntimeContentGroupSnapshot> _groups = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _clock;

    public UltraSimRuntimeContentGroupRegistry(Func<DateTimeOffset>? clock = null)
    {
        _clock = clock ?? (static () => DateTimeOffset.UtcNow);
    }

    public int Count => _groups.Count;

    public UltraSimRuntimeContentGroupOperationResult Register(
        UltraSimRuntimeContentGroupDescriptor descriptor,
        bool mounted = true)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        if (_groups.ContainsKey(descriptor.Key))
        {
            return UltraSimRuntimeContentGroupOperationResult.Rejected(
                UltraSimRuntimeContentGroupOperationStatus.DuplicateKey,
                $"Runtime content group '{descriptor.Key}' is already registered.",
                _groups[descriptor.Key]);
        }

        UltraSimRuntimeContentGroupSnapshot snapshot = CreateSnapshot(
            descriptor,
            mounted ? UltraSimRuntimeContentGroupMountState.Mounted : UltraSimRuntimeContentGroupMountState.Unmounted);
        _groups.Add(descriptor.Key, snapshot);
        return UltraSimRuntimeContentGroupOperationResult.AcceptedResult(snapshot);
    }

    public void RegisterOrReplace(
        UltraSimRuntimeContentGroupDescriptor descriptor,
        bool mounted = true)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        _groups[descriptor.Key] = CreateSnapshot(
            descriptor,
            mounted ? UltraSimRuntimeContentGroupMountState.Mounted : UltraSimRuntimeContentGroupMountState.Unmounted);
    }

    public UltraSimRuntimeContentGroupOperationResult Mount(string key)
        => SetMountState(key, UltraSimRuntimeContentGroupMountState.Mounted);

    public UltraSimRuntimeContentGroupOperationResult Unmount(string key)
        => SetMountState(key, UltraSimRuntimeContentGroupMountState.Unmounted);

    public bool TryGet(string key, out UltraSimRuntimeContentGroupSnapshot snapshot)
    {
        ScriptDefinitionValidation.ValidateStableKey(key, nameof(key));
        return _groups.TryGetValue(ScriptDefinitionValidation.NormalizeKey(key), out snapshot!);
    }

    public IReadOnlyList<UltraSimRuntimeContentGroupSnapshot> Snapshot(bool mountedOnly = false)
    {
        IEnumerable<UltraSimRuntimeContentGroupSnapshot> query = _groups.Values;
        if (mountedOnly)
            query = query.Where(static group => group.IsMounted);

        return query
            .OrderBy(static group => group.Descriptor.Key, StringComparer.Ordinal)
            .ToArray();
    }

    private UltraSimRuntimeContentGroupOperationResult SetMountState(
        string key,
        UltraSimRuntimeContentGroupMountState state)
    {
        ScriptDefinitionValidation.ValidateStableKey(key, nameof(key));
        string normalized = ScriptDefinitionValidation.NormalizeKey(key);
        if (!_groups.TryGetValue(normalized, out UltraSimRuntimeContentGroupSnapshot? current))
        {
            return UltraSimRuntimeContentGroupOperationResult.Rejected(
                UltraSimRuntimeContentGroupOperationStatus.UnknownKey,
                $"Runtime content group '{normalized}' is not registered.");
        }

        if (current.MountState == state)
        {
            UltraSimRuntimeContentGroupOperationStatus status = state == UltraSimRuntimeContentGroupMountState.Mounted
                ? UltraSimRuntimeContentGroupOperationStatus.AlreadyMounted
                : UltraSimRuntimeContentGroupOperationStatus.AlreadyUnmounted;
            return UltraSimRuntimeContentGroupOperationResult.Rejected(
                status,
                $"Runtime content group '{normalized}' is already {state.ToString().ToLowerInvariant()}.",
                current);
        }

        UltraSimRuntimeContentGroupSnapshot updated = current with
        {
            MountState = state,
            UpdatedUtc = _clock()
        };
        _groups[normalized] = updated;
        return UltraSimRuntimeContentGroupOperationResult.AcceptedResult(updated);
    }

    private UltraSimRuntimeContentGroupSnapshot CreateSnapshot(
        UltraSimRuntimeContentGroupDescriptor descriptor,
        UltraSimRuntimeContentGroupMountState state)
    {
        return new UltraSimRuntimeContentGroupSnapshot(descriptor, state, _clock());
    }
}
