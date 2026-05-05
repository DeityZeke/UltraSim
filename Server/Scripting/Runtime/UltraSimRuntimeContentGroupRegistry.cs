namespace UltraSim.Server.Scripting;

public sealed class UltraSimRuntimeContentGroupRegistry : IUltraSimScriptContentGroupRegistry
{
    private readonly Dictionary<string, UltraSimRuntimeContentGroupSnapshot> _groups = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<Action>> _unmountActions = new(StringComparer.Ordinal);
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

    public UltraSimRuntimeContentGroupOperationResult RegisterUnmountAction(string key, Action action)
    {
        ArgumentNullException.ThrowIfNull(action);
        ScriptDefinitionValidation.ValidateStableKey(key, nameof(key));
        string normalized = ScriptDefinitionValidation.NormalizeKey(key);
        if (!_groups.TryGetValue(normalized, out UltraSimRuntimeContentGroupSnapshot? current))
        {
            return UltraSimRuntimeContentGroupOperationResult.Rejected(
                UltraSimRuntimeContentGroupOperationStatus.UnknownKey,
                $"Runtime content group '{normalized}' is not registered.");
        }

        if (!current.IsMounted)
        {
            return UltraSimRuntimeContentGroupOperationResult.Rejected(
                UltraSimRuntimeContentGroupOperationStatus.AlreadyUnmounted,
                $"Runtime content group '{normalized}' is not mounted.",
                current);
        }

        if (!_unmountActions.TryGetValue(normalized, out List<Action>? actions))
        {
            actions = new List<Action>();
            _unmountActions.Add(normalized, actions);
        }

        actions.Add(action);
        return UltraSimRuntimeContentGroupOperationResult.AcceptedResult(current);
    }

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

        if (state == UltraSimRuntimeContentGroupMountState.Unmounted &&
            !TryRunUnmountActions(normalized, current, out UltraSimRuntimeContentGroupOperationResult failure))
        {
            return failure;
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

    private bool TryRunUnmountActions(
        string normalized,
        UltraSimRuntimeContentGroupSnapshot current,
        out UltraSimRuntimeContentGroupOperationResult failure)
    {
        failure = default!;
        if (!_unmountActions.TryGetValue(normalized, out List<Action>? actions) || actions.Count == 0)
            return true;

        while (actions.Count > 0)
        {
            int index = actions.Count - 1;
            Action action = actions[index];
            try
            {
                action();
                actions.RemoveAt(index);
            }
            catch (Exception ex)
            {
                failure = UltraSimRuntimeContentGroupOperationResult.Rejected(
                    UltraSimRuntimeContentGroupOperationStatus.UnmountFailed,
                    $"Runtime content group '{normalized}' failed to unmount: {ex.Message}",
                    current);
                return false;
            }
        }

        _unmountActions.Remove(normalized);
        return true;
    }
}
