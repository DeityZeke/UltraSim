namespace UltraSim.UI;

/// <summary>Exact cache identity for one server/composition UI definition revision.</summary>
public readonly record struct UiDefinitionCacheKey
{
    public const int MaximumIdentityLength = 256;

    public UiDefinitionCacheKey(
        string serverIdentity,
        string compositionIdentity,
        ushort protocolVersion,
        UiDefinitionKey definitionKey,
        ushort definitionVersion,
        UiDefinitionHash definitionHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(compositionIdentity);
        string normalizedServer = serverIdentity.Trim();
        string normalizedComposition = compositionIdentity.Trim();
        if (normalizedServer.Length > MaximumIdentityLength)
            throw new ArgumentOutOfRangeException(nameof(serverIdentity));
        if (normalizedComposition.Length > MaximumIdentityLength)
            throw new ArgumentOutOfRangeException(nameof(compositionIdentity));
        UiText.ValidateUnicode(normalizedServer, nameof(serverIdentity));
        UiText.ValidateUnicode(normalizedComposition, nameof(compositionIdentity));
        if (protocolVersion == 0)
            throw new ArgumentOutOfRangeException(nameof(protocolVersion));
        if (!definitionKey.IsValid)
            throw new ArgumentException("A valid definition key is required.", nameof(definitionKey));
        if (definitionVersion == 0)
            throw new ArgumentOutOfRangeException(nameof(definitionVersion));
        if (!definitionHash.IsValid)
            throw new ArgumentException("A valid definition hash is required.", nameof(definitionHash));

        ServerIdentity = normalizedServer;
        CompositionIdentity = normalizedComposition;
        ProtocolVersion = protocolVersion;
        DefinitionKey = definitionKey;
        DefinitionVersion = definitionVersion;
        DefinitionHash = definitionHash;
    }

    public string ServerIdentity { get; }
    public string CompositionIdentity { get; }
    public ushort ProtocolVersion { get; }
    public UiDefinitionKey DefinitionKey { get; }
    public ushort DefinitionVersion { get; }
    public UiDefinitionHash DefinitionHash { get; }

    public UiDefinitionFamilyKey Family => new(
        ServerIdentity,
        CompositionIdentity,
        ProtocolVersion,
        DefinitionKey);

    public void Validate(CompiledUiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.Key != DefinitionKey ||
            definition.Version != DefinitionVersion ||
            definition.Hash != DefinitionHash)
        {
            throw new InvalidOperationException("The UI definition does not match its cache identity.");
        }
    }
}

/// <summary>Identity of a reusable definition family before its exact revision hash.</summary>
public readonly record struct UiDefinitionFamilyKey(
    string ServerIdentity,
    string CompositionIdentity,
    ushort ProtocolVersion,
    UiDefinitionKey DefinitionKey);

/// <summary>Semantic list mutation applied within one model-delta sequence.</summary>
public enum UiListDeltaKind : byte
{
    Replace = 0,
    Add = 1,
    Update = 2,
    Remove = 3,
}

/// <summary>One scalar slot replacement in an atomic model delta.</summary>
public readonly struct UiScalarSlotPatch
{
    public UiScalarSlotPatch(UiSlotId slot, UiValue value)
    {
        if (!slot.IsValid)
            throw new ArgumentException("A scalar patch requires a valid slot ID.", nameof(slot));
        Slot = slot;
        Value = value;
    }

    public UiSlotId Slot { get; }
    public UiValue Value { get; }
}

/// <summary>One full-list or row-granular mutation for a compiled list slot.</summary>
public readonly struct UiListPatch
{
    private readonly UiListRow[]? _rows;

    private UiListPatch(UiSlotId slot, UiListDeltaKind kind, UiRowId rowId, UiListRow row, UiListRow[] rows)
    {
        if (!slot.IsValid)
            throw new ArgumentException("A list patch requires a valid slot ID.", nameof(slot));
        if ((uint)kind > (uint)UiListDeltaKind.Remove)
            throw new ArgumentOutOfRangeException(nameof(kind));
        if (kind != UiListDeltaKind.Replace && !rowId.IsValid)
            throw new ArgumentException("A row patch requires a valid row ID.", nameof(rowId));
        if ((kind == UiListDeltaKind.Add || kind == UiListDeltaKind.Update) && row.Id != rowId)
            throw new ArgumentException("The row patch ID must match the row payload.", nameof(row));
        if ((kind == UiListDeltaKind.Add || kind == UiListDeltaKind.Update) && !row.Id.IsValid)
            throw new ArgumentException("An add/update patch requires a valid row payload.", nameof(row));
        Slot = slot;
        Kind = kind;
        RowId = rowId;
        Row = row;
        _rows = rows;
    }

    public UiSlotId Slot { get; }
    public UiListDeltaKind Kind { get; }
    public UiRowId RowId { get; }
    public UiListRow Row { get; }
    public ReadOnlySpan<UiListRow> Rows => _rows;

    public static UiListPatch Replace(UiSlotId slot, ReadOnlySpan<UiListRow> rows)
    {
        var copy = new UiListRow[rows.Length];
        for (int index = 0; index < rows.Length; index++)
            copy[index] = rows[index].Clone();
        return new UiListPatch(slot, UiListDeltaKind.Replace, default, default, copy);
    }

    public static UiListPatch Add(UiSlotId slot, UiListRow row) =>
        new(slot, UiListDeltaKind.Add, row.Id, row.Clone(), Array.Empty<UiListRow>());

    public static UiListPatch Update(UiSlotId slot, UiListRow row) =>
        new(slot, UiListDeltaKind.Update, row.Id, row.Clone(), Array.Empty<UiListRow>());

    public static UiListPatch Remove(UiSlotId slot, UiRowId rowId) =>
        new(slot, UiListDeltaKind.Remove, rowId, default, Array.Empty<UiListRow>());
}

/// <summary>One enabled/visible state replacement for a compiled action.</summary>
public readonly struct UiActionStatePatch
{
    public UiActionStatePatch(UiActionId action, UiActionState state)
    {
        if (!action.IsValid)
            throw new ArgumentException("An action patch requires a valid action ID.", nameof(action));
        Action = action;
        State = state;
    }

    public UiActionId Action { get; }
    public UiActionState State { get; }
}

/// <summary>Sequence-bearing dynamic patch applied atomically to one client view.</summary>
public sealed class UiModelDelta
{
    private readonly UiScalarSlotPatch[] _scalarSlots;
    private readonly UiListPatch[] _lists;
    private readonly UiActionStatePatch[] _actions;

    public UiModelDelta(
        UiDefinitionHandle definition,
        ulong sequence,
        ReadOnlySpan<UiScalarSlotPatch> scalarSlots = default,
        ReadOnlySpan<UiListPatch> lists = default,
        ReadOnlySpan<UiActionStatePatch> actions = default)
    {
        if (!definition.IsValid)
            throw new ArgumentException("A model delta requires a valid definition handle.", nameof(definition));
        Definition = definition;
        Sequence = sequence;
        _scalarSlots = scalarSlots.ToArray();
        _lists = lists.ToArray();
        _actions = actions.ToArray();
        ValidateUniquePatches();
    }

    public UiDefinitionHandle Definition { get; }
    public ulong Sequence { get; }
    public ReadOnlySpan<UiScalarSlotPatch> ScalarSlots => _scalarSlots;
    public ReadOnlySpan<UiListPatch> Lists => _lists;
    public ReadOnlySpan<UiActionStatePatch> Actions => _actions;

    private void ValidateUniquePatches()
    {
        var slots = new HashSet<UiSlotId>();
        foreach (UiScalarSlotPatch patch in ScalarSlots)
        {
            if (!patch.Slot.IsValid)
                throw new InvalidDataException("A UI scalar patch has an invalid slot ID.");
            if (!slots.Add(patch.Slot))
                throw new InvalidDataException($"Duplicate scalar UI slot patch '{patch.Slot}'.");
        }

        var listSlots = new HashSet<UiSlotId>();
        var replacedLists = new HashSet<UiSlotId>();
        var rowTargets = new HashSet<(UiSlotId Slot, UiRowId Row)>();
        foreach (UiListPatch patch in Lists)
        {
            if (!patch.Slot.IsValid ||
                (uint)patch.Kind > (uint)UiListDeltaKind.Remove ||
                (patch.Kind != UiListDeltaKind.Replace && !patch.RowId.IsValid))
            {
                throw new InvalidDataException("A UI list patch is invalid or uninitialized.");
            }
            if (slots.Contains(patch.Slot))
                throw new InvalidDataException($"Duplicate list UI slot patch '{patch.Slot}'.");

            if (patch.Kind == UiListDeltaKind.Replace)
            {
                if (!listSlots.Add(patch.Slot))
                    throw new InvalidDataException($"A list replacement cannot be combined with other patches for slot '{patch.Slot}'.");
                replacedLists.Add(patch.Slot);
                continue;
            }

            if (replacedLists.Contains(patch.Slot))
                throw new InvalidDataException($"A list replacement cannot be combined with row patches for slot '{patch.Slot}'.");
            listSlots.Add(patch.Slot);
            if (!rowTargets.Add((patch.Slot, patch.RowId)))
            {
                throw new InvalidDataException(
                    $"Duplicate UI list-row patch '{patch.Slot}:{patch.RowId.Value}'.");
            }
        }

        var actions = new HashSet<UiActionId>();
        foreach (UiActionStatePatch patch in Actions)
        {
            if (!patch.Action.IsValid)
                throw new InvalidDataException("A UI action-state patch has an invalid action ID.");
            if (!actions.Add(patch.Action))
                throw new InvalidDataException($"Duplicate UI action state patch '{patch.Action}'.");
        }
    }

    public void ValidateAgainst(CompiledUiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (Definition != definition.Handle)
            throw new InvalidOperationException("The UI delta does not match the compiled definition.");

        foreach (UiScalarSlotPatch patch in ScalarSlots)
        {
            UiSlotValueKind kind = definition.GetSlotValueKind(patch.Slot);
            if (kind == UiSlotValueKind.List)
                throw new InvalidDataException("A scalar delta cannot target a list slot.");
            if (patch.Value.Kind == UiValueKind.Unset)
                continue;
            UiSlotValueKind actual = patch.Value.Kind switch
            {
                UiValueKind.Text => UiSlotValueKind.Text,
                UiValueKind.Number => UiSlotValueKind.Number,
                UiValueKind.Boolean => UiSlotValueKind.Boolean,
                _ => throw new InvalidDataException("A scalar delta carries an unsupported value kind."),
            };
            if (kind != UiSlotValueKind.Any && kind != actual)
                throw new InvalidDataException($"A scalar delta cannot write {actual} into a {kind} slot.");
        }

        foreach (UiListPatch patch in Lists)
        {
            UiSlotValueKind kind = definition.GetSlotValueKind(patch.Slot);
            if (kind != UiSlotValueKind.List && kind != UiSlotValueKind.Any)
                throw new InvalidDataException("A list delta cannot target a scalar slot.");
            if (patch.Kind == UiListDeltaKind.Replace &&
                patch.Rows.Length > definition.Limits.MaximumRowsPerList)
            {
                throw new InvalidDataException("A list replacement exceeds the compiled row limit.");
            }
        }

        foreach (UiActionStatePatch patch in Actions)
            definition.GetAction(patch.Action);
    }
}

/// <summary>Cold open carrying an exact compiled definition and cloned initial model.</summary>
public readonly struct UiOpenView
{
    private readonly UiModel _model;

    public UiOpenView(
        UiDefinitionCacheKey cacheKey,
        CompiledUiDefinition definition,
        UiModel model,
        UiViewInstanceId instance = default,
        ulong sequence = 0)
    {
        cacheKey.Validate(definition);
        ArgumentNullException.ThrowIfNull(model);
        if (!ReferenceEquals(model.Definition, definition))
            throw new InvalidOperationException("The opening model does not match the compiled definition.");
        CacheKey = cacheKey;
        Definition = definition;
        _model = model.Clone();
        Instance = instance;
        Sequence = sequence;
    }

    public UiDefinitionCacheKey CacheKey { get; }
    public CompiledUiDefinition Definition { get; }
    public UiViewInstanceId Instance { get; }
    public ulong Sequence { get; }

    public UiModel SnapshotModel() => CloneModel();

    internal UiModel CloneModel() => _model?.Clone() ??
        throw new InvalidOperationException("The cold-open model is not initialized.");
}

/// <summary>Hot open referencing a definition revision already admitted to the client cache.</summary>
public readonly struct UiHotOpenView
{
    private readonly UiModel _model;

    public UiHotOpenView(
        UiDefinitionCacheKey cacheKey,
        UiModel model,
        UiViewInstanceId instance = default,
        ulong sequence = 0)
    {
        ArgumentNullException.ThrowIfNull(model);
        cacheKey.Validate(model.Definition);
        CacheKey = cacheKey;
        _model = model.Clone();
        Instance = instance;
        Sequence = sequence;
    }

    public UiDefinitionCacheKey CacheKey { get; }
    public CompiledUiDefinition Definition => _model?.Definition ??
        throw new InvalidOperationException("The hot-open model is not initialized.");
    public UiViewInstanceId Instance { get; }
    public ulong Sequence { get; }

    public UiModel SnapshotModel() => CloneModel();

    internal UiModel CloneModel() => _model?.Clone() ??
        throw new InvalidOperationException("The hot-open model is not initialized.");
}

/// <summary>Sequence-bearing atomic model update for one retained view instance.</summary>
public readonly struct UiUpdateView
{
    public UiUpdateView(UiViewInstanceId instance, UiModelDelta delta)
    {
        if (!instance.IsValid)
            throw new ArgumentException("A view update requires a valid instance ID.", nameof(instance));
        ArgumentNullException.ThrowIfNull(delta);
        Instance = instance;
        Delta = delta;
    }

    public UiViewInstanceId Instance { get; }
    public UiModelDelta Delta { get; }
}

/// <summary>Request to close one retained client view instance.</summary>
public readonly struct UiCloseView
{
    public UiCloseView(UiViewInstanceId instance)
    {
        if (!instance.IsValid)
            throw new ArgumentException("A view close requires a valid instance ID.", nameof(instance));
        Instance = instance;
    }

    public UiViewInstanceId Instance { get; }
}

/// <summary>Renderer-neutral semantic UI transport used by game policy and client bridges.</summary>
public interface IUiSemanticPort
{
    UiViewInstanceId OpenView(UiOpenView operation);
    UiViewInstanceId OpenHotView(UiHotOpenView operation);
    void UpdateView(UiUpdateView operation);
    bool CloseView(UiCloseView operation);
    void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler);
    void UnbindActions(UiViewInstanceId instance);
}

/// <summary>Renderer-owned opaque lifetime handle; only its creating renderer may interpret it.</summary>
public interface IUiRenderHandle
{
}

/// <summary>Read-only renderer capability over one client-owned model snapshot.</summary>
public readonly struct UiModelView
{
    private readonly UiModel? _model;

    public UiModelView(UiModel model) => _model = model ?? throw new ArgumentNullException(nameof(model));

    public bool IsValid => _model is not null;

    public CompiledUiDefinition Definition => GetModel().Definition;

    public UiValue Get(UiSlotId slot) => GetModel().Get(slot);

    public ReadOnlySpan<UiListRow> GetRows(UiSlotId slot) => GetModel().GetRows(slot);

    public UiActionState GetActionState(UiActionId action) => GetModel().GetActionState(action);

    private UiModel GetModel() => _model ??
        throw new InvalidOperationException("The UI model view is not initialized.");
}

/// <summary>Client-side presentation boundary over immutable compiled definitions and model snapshots.</summary>
/// <remarks>Renderer exceptions terminally fault the owning client runtime because external controls may be partial.</remarks>
public interface IUiRenderer
{
    IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction);

    void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction);

    void Close(IUiRenderHandle handle);
}

/// <summary>Signals that a view targets a definition family superseded by a newer exact revision.</summary>
public sealed class UiViewSupersededException : InvalidOperationException
{
    public UiViewSupersededException(UiDefinitionFamilyKey family)
        : base($"The cached UI definition family '{family.DefinitionKey}' was superseded.") { }
}

/// <summary>
/// Terminal client-runtime failure after a renderer operation could have
/// partially changed external control state.
/// </summary>
public sealed class UiClientRuntimeFaultedException : InvalidOperationException
{
    public UiClientRuntimeFaultedException(string operation, Exception innerException)
        : base($"The UI client runtime faulted while the renderer was executing '{operation}'.", innerException) { }

    public UiClientRuntimeFaultedException(Exception innerException)
        : base("The UI client runtime is terminally faulted.", innerException) { }
}
