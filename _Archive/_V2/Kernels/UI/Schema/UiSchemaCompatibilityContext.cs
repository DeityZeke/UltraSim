namespace UltraSim.UI;

public sealed class UiSchemaCompatibilityContext
{
    private readonly Dictionary<string, UiPayloadSlotDescriptor> _payloadSlotById;
    private readonly Dictionary<string, UiActionDescriptor> _actionById;
    private readonly Dictionary<string, IReadOnlyList<UiSelectOptionDescriptor>> _selectOptionsBySlotId;
    private readonly Dictionary<string, IReadOnlyList<UiTableColumnDescriptor>> _tableColumnsBySlotId;
    private readonly HashSet<string> _trustedInputSlotIds;
    private readonly HashSet<string> _repeaterSlotIds;
    private readonly HashSet<string> _stateBindingSlotIds;

    private UiSchemaCompatibilityContext(
        UiSchemaIdentity schemaIdentity,
        UiCatalogIdentity? catalogIdentity,
        IReadOnlyList<UiPayloadSlotDescriptor> payloadSlots,
        IReadOnlyList<UiActionDescriptor> actions,
        IReadOnlyList<UiSelectOptionDescriptor> selectOptions,
        IReadOnlyList<UiStateBindingDescriptor> stateBindings,
        IReadOnlyList<UiTableColumnDescriptor> tableColumns,
        Dictionary<string, UiPayloadSlotDescriptor> payloadSlotById,
        Dictionary<string, UiActionDescriptor> actionById,
        Dictionary<string, IReadOnlyList<UiSelectOptionDescriptor>> selectOptionsBySlotId,
        Dictionary<string, IReadOnlyList<UiTableColumnDescriptor>> tableColumnsBySlotId,
        HashSet<string> trustedInputSlotIds,
        HashSet<string> repeaterSlotIds,
        HashSet<string> stateBindingSlotIds)
    {
        SchemaIdentity = schemaIdentity;
        CatalogIdentity = catalogIdentity;
        PayloadSlots = payloadSlots;
        Actions = actions;
        SelectOptions = selectOptions;
        StateBindings = stateBindings;
        TableColumns = tableColumns;
        _payloadSlotById = payloadSlotById;
        _actionById = actionById;
        _selectOptionsBySlotId = selectOptionsBySlotId;
        _tableColumnsBySlotId = tableColumnsBySlotId;
        _trustedInputSlotIds = trustedInputSlotIds;
        _repeaterSlotIds = repeaterSlotIds;
        _stateBindingSlotIds = stateBindingSlotIds;
    }

    public UiSchemaIdentity SchemaIdentity { get; }

    public UiCatalogIdentity? CatalogIdentity { get; }

    public IReadOnlyList<UiPayloadSlotDescriptor> PayloadSlots { get; }

    public IReadOnlyList<UiActionDescriptor> Actions { get; }

    public IReadOnlyList<UiSelectOptionDescriptor> SelectOptions { get; }

    public IReadOnlyList<UiStateBindingDescriptor> StateBindings { get; }

    public IReadOnlyList<UiTableColumnDescriptor> TableColumns { get; }

    public static UiValidationResult TryCreate(
        UiSchemaDocument schema,
        out UiSchemaCompatibilityContext? context,
        UiSchemaValidationLimits? limits = null,
        UiRendererCapabilities? rendererCapabilities = null)
    {
        context = null;
        UiValidationResult schemaResult = schema.Validate(limits, rendererCapabilities);
        if (!schemaResult.IsValid)
        {
            return schemaResult;
        }

        var payloadSlotById = new Dictionary<string, UiPayloadSlotDescriptor>(
            schema.PayloadSlots.Count,
            StringComparer.Ordinal);
        for (int i = 0; i < schema.PayloadSlots.Count; i++)
        {
            UiPayloadSlotDescriptor descriptor = schema.PayloadSlots[i];
            payloadSlotById.Add(descriptor.SlotId, descriptor);
        }

        var actionById = new Dictionary<string, UiActionDescriptor>(
            schema.Actions.Count,
            StringComparer.Ordinal);
        for (int i = 0; i < schema.Actions.Count; i++)
        {
            UiActionDescriptor action = schema.Actions[i];
            actionById.Add(action.ActionId, action);
        }

        var stateBindingSlotIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < schema.StateBindings.Count; i++)
        {
            UiStateBindingDescriptor binding = schema.StateBindings[i];
            stateBindingSlotIds.Add(binding.SlotId);
        }

        var repeaterSlotIds = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < schema.Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = schema.Nodes[i];
            if (node.Kind is UiNodeKind.Repeater or UiNodeKind.Table && !string.IsNullOrWhiteSpace(node.SlotId))
            {
                repeaterSlotIds.Add(node.SlotId);
            }
        }

        IReadOnlyList<UiSelectOptionDescriptor> selectOptions = schema.GetSelectOptions();
        var groupedSelectOptions = new Dictionary<string, List<UiSelectOptionDescriptor>>(StringComparer.Ordinal);
        for (int i = 0; i < selectOptions.Count; i++)
        {
            UiSelectOptionDescriptor option = selectOptions[i];
            if (!groupedSelectOptions.TryGetValue(option.SlotId, out List<UiSelectOptionDescriptor>? optionsForSlot))
            {
                optionsForSlot = new List<UiSelectOptionDescriptor>();
                groupedSelectOptions.Add(option.SlotId, optionsForSlot);
            }

            optionsForSlot.Add(option);
        }

        var selectOptionsBySlotId = new Dictionary<string, IReadOnlyList<UiSelectOptionDescriptor>>(groupedSelectOptions.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<UiSelectOptionDescriptor>> pair in groupedSelectOptions)
        {
            selectOptionsBySlotId.Add(pair.Key, pair.Value.ToArray());
        }

        IReadOnlyList<UiTableColumnDescriptor> tableColumns = schema.GetTableColumns();
        var groupedTableColumns = new Dictionary<string, List<UiTableColumnDescriptor>>(StringComparer.Ordinal);
        for (int i = 0; i < tableColumns.Count; i++)
        {
            UiTableColumnDescriptor column = tableColumns[i];
            if (!groupedTableColumns.TryGetValue(column.TableSlotId, out List<UiTableColumnDescriptor>? columnsForTable))
            {
                columnsForTable = new List<UiTableColumnDescriptor>();
                groupedTableColumns.Add(column.TableSlotId, columnsForTable);
            }

            columnsForTable.Add(column);
        }

        var tableColumnsBySlotId = new Dictionary<string, IReadOnlyList<UiTableColumnDescriptor>>(groupedTableColumns.Count, StringComparer.Ordinal);
        foreach (KeyValuePair<string, List<UiTableColumnDescriptor>> pair in groupedTableColumns)
        {
            tableColumnsBySlotId.Add(pair.Key, pair.Value.ToArray());
        }

        var trustedInputSlotIds = new HashSet<string>(schema.GetTrustedInputSlotIds(), StringComparer.Ordinal);

        context = new UiSchemaCompatibilityContext(
            schema.Identity,
            schema.CatalogIdentity,
            schema.PayloadSlots,
            schema.Actions,
            selectOptions,
            schema.StateBindings,
            tableColumns,
            payloadSlotById,
            actionById,
            selectOptionsBySlotId,
            tableColumnsBySlotId,
            trustedInputSlotIds,
            repeaterSlotIds,
            stateBindingSlotIds);
        return UiValidationResult.Valid;
    }

    public UiValidationResult ValidateSchemaIdentity(UiSchemaIdentity schemaIdentity)
    {
        return schemaIdentity == SchemaIdentity
            ? UiValidationResult.Valid
            : UiValidationResult.Invalid("Schema identity does not match compatibility context.");
    }

    public UiValidationResult ValidateCatalogIdentity(UiCatalogIdentity? catalogIdentity)
    {
        if (CatalogIdentity is null && catalogIdentity is not null)
        {
            return UiValidationResult.Invalid("Catalog identity was supplied for a non-catalog schema.");
        }

        if (CatalogIdentity is not null && catalogIdentity != CatalogIdentity)
        {
            return UiValidationResult.Invalid("Catalog identity does not match compatibility context.");
        }

        return UiValidationResult.Valid;
    }

    public bool TryGetPayloadSlot(string slotId, out UiPayloadSlotDescriptor descriptor)
    {
        return _payloadSlotById.TryGetValue(slotId, out descriptor!);
    }

    public bool TryGetAction(string actionId, out UiActionDescriptor action)
    {
        return _actionById.TryGetValue(actionId, out action!);
    }

    public bool TryGetSelectOptions(string slotId, out IReadOnlyList<UiSelectOptionDescriptor> options)
    {
        return _selectOptionsBySlotId.TryGetValue(slotId, out options!);
    }

    public bool TryGetTableColumns(string slotId, out IReadOnlyList<UiTableColumnDescriptor> columns)
    {
        return _tableColumnsBySlotId.TryGetValue(slotId, out columns!);
    }

    public bool IsTrustedInputSlot(string slotId)
    {
        return _trustedInputSlotIds.Contains(slotId);
    }

    public bool HasRepeaterSlot(string slotId)
    {
        return _repeaterSlotIds.Contains(slotId);
    }

    public bool HasStateBindingSlot(string slotId)
    {
        return _stateBindingSlotIds.Contains(slotId);
    }
}
