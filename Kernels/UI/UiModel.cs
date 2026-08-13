namespace UltraSim.UI;

/// <summary>
/// Mutable dynamic state for one retained compiled UI definition.
/// </summary>
/// <remarks>
/// The definition owns all topology and binding metadata. This object owns only
/// values that may change while a view is open, so changing it never changes the
/// definition hash.
/// </remarks>
public sealed class UiModel
{
    private readonly CompiledUiDefinition _definition;
    private readonly UiValue[] _values;
    private readonly UiListRow[][] _rows;
    private readonly UiActionState[] _actionStates;

    public UiModel(CompiledUiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        _values = new UiValue[definition.SlotCount];
        _rows = new UiListRow[definition.SlotCount][];
        _actionStates = new UiActionState[definition.ActionCount];
        Array.Fill(_actionStates, UiActionState.Default);
    }

    public CompiledUiDefinition Definition => _definition;

    /// <summary>Sets one prepared scalar slot without performing source-key lookup.</summary>
    public void Set(UiSlotId slot, UiValue value)
    {
        UiSlotDefinition definition = _definition.GetSlot(slot);
        if (definition.ValueKind == UiSlotValueKind.List)
            throw new InvalidOperationException($"Slot '{definition.Key}' is a list slot; use ReplaceRows.");
        if (value.Kind == UiValueKind.Unset)
        {
            _values[slot.Value] = value;
            if (definition.ValueKind == UiSlotValueKind.Any)
                _rows[slot.Value] = null!;
            return;
        }

        UiSlotValueKind actual = ToSlotValueKind(value.Kind);
        if (definition.ValueKind != UiSlotValueKind.Any && definition.ValueKind != actual)
        {
            throw new InvalidOperationException(
                $"Slot '{definition.Key}' requires {definition.ValueKind}, but received {actual}.");
        }

        _values[slot.Value] = value;
        if (definition.ValueKind == UiSlotValueKind.Any)
            _rows[slot.Value] = null!;
    }

    public void SetText(UiSlotId slot, string value) => Set(slot, UiValue.FromText(value));

    public void SetNumber(UiSlotId slot, double value) => Set(slot, UiValue.FromNumber(value));

    public void SetBoolean(UiSlotId slot, bool value) => Set(slot, UiValue.FromBoolean(value));

    public UiValue Get(UiSlotId slot)
    {
        _definition.GetSlot(slot);
        return _values[slot.Value];
    }

    /// <summary>Replaces a list slot with a private immutable row snapshot.</summary>
    public void ReplaceRows(UiSlotId slot, ReadOnlySpan<UiListRow> rows)
    {
        UiSlotDefinition definition = _definition.GetSlot(slot);
        if (definition.ValueKind != UiSlotValueKind.List && definition.ValueKind != UiSlotValueKind.Any)
        {
            throw new InvalidOperationException($"Slot '{definition.Key}' is not a list slot.");
        }
        if (rows.Length > _definition.Limits.MaximumRowsPerList)
            throw new InvalidDataException("The UI model row limit was exceeded.");

        var copy = new UiListRow[rows.Length];
        var ids = new HashSet<UiRowId>();
        for (int index = 0; index < rows.Length; index++)
        {
            UiListRow row = rows[index];
            if (!row.Id.IsValid)
                throw new InvalidDataException("A UI model row requires a valid row ID.");
            if (!ids.Add(row.Id))
                throw new InvalidDataException($"Duplicate UI model row ID '{row.Id.Value}'.");
            copy[index] = row.Clone();
        }

        _rows[slot.Value] = copy;
        if (definition.ValueKind == UiSlotValueKind.Any)
            _values[slot.Value] = UiValue.Unset;
    }

    public ReadOnlySpan<UiListRow> GetRows(UiSlotId slot)
    {
        UiSlotDefinition definition = _definition.GetSlot(slot);
        if (definition.ValueKind != UiSlotValueKind.List && definition.ValueKind != UiSlotValueKind.Any)
            throw new InvalidOperationException($"Slot '{definition.Key}' is not a list slot.");
        return _rows[slot.Value] ?? Array.Empty<UiListRow>();
    }

    public void SetActionState(UiActionId action, UiActionState state)
    {
        _definition.GetAction(action);
        _actionStates[action.Value] = state;
    }

    public UiActionState GetActionState(UiActionId action)
    {
        _definition.GetAction(action);
        return _actionStates[action.Value];
    }

    public void ValidateAction(in UiActionRequest request)
    {
        _definition.ValidateAction(request, this);
    }

    private static UiSlotValueKind ToSlotValueKind(UiValueKind kind) => kind switch
    {
        UiValueKind.Text => UiSlotValueKind.Text,
        UiValueKind.Number => UiSlotValueKind.Number,
        UiValueKind.Boolean => UiSlotValueKind.Boolean,
        _ => UiSlotValueKind.Any,
    };
}
