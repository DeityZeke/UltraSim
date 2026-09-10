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

    /// <summary>Creates an independent dynamic snapshot for a client view or delta transaction.</summary>
    public UiModel Clone()
    {
        var clone = new UiModel(_definition);
        CopyStateTo(clone);
        return clone;
    }

    /// <summary>
    /// Rebinds dynamic values to another independently decoded instance of the exact same compiled
    /// definition. This is the source-convergence seam for passive cached definition artifacts.
    /// </summary>
    internal UiModel CloneForExactDefinition(CompiledUiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (_definition.Handle != definition.Handle)
            throw new InvalidOperationException("The UI model cannot bind to a different definition identity.");
        var clone = new UiModel(definition);
        CopyStateTo(clone);
        return clone;
    }

    private void CopyStateTo(UiModel clone)
    {
        Array.Copy(_values, clone._values, _values.Length);
        Array.Copy(_actionStates, clone._actionStates, _actionStates.Length);
        // Row arrays and UiListRow cell arrays are immutable snapshots: the
        // model never mutates either and ReplaceRows always publishes a new
        // private array. Clones can therefore share them until a list slot is
        // replaced, avoiding a whole-list deep copy for scalar deltas.
        Array.Copy(_rows, clone._rows, _rows.Length);
    }

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

        ValidateDynamicValue(value);

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
            foreach (UiValue value in row.Values)
                ValidateDynamicValue(value);
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
        _definition.ValidateActionState(state);
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

    /// <summary>
    /// Tries to validate untrusted semantic intent against this retained model.
    /// </summary>
    /// <remarks>
    /// Use this at a server/application boundary before entering authoritative
    /// game logic. It contains only expected definition, source, row, input,
    /// and enabled-state rejection. Exceptions raised after successful
    /// validation remain implementation or authority failures and must not be
    /// converted into ordinary user rejection.
    /// </remarks>
    public bool TryValidateAction(in UiActionRequest request)
    {
        try
        {
            _definition.ValidateAction(request, this);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static UiSlotValueKind ToSlotValueKind(UiValueKind kind) => kind switch
    {
        UiValueKind.Text => UiSlotValueKind.Text,
        UiValueKind.Number => UiSlotValueKind.Number,
        UiValueKind.Boolean => UiSlotValueKind.Boolean,
        _ => UiSlotValueKind.Any,
    };

    private void ValidateDynamicValue(UiValue value)
    {
        if (value.Kind == UiValueKind.Text &&
            value.Text!.Length > _definition.Limits.MaximumTextCharacters)
        {
            throw new InvalidDataException("The UI model text limit was exceeded.");
        }
    }
}
