namespace UltraSim.UI;

public sealed class UiSchemaCompatibilityContext
{
    private readonly Dictionary<string, UiPayloadSlotDescriptor> _payloadSlotById;
    private readonly Dictionary<string, UiActionDescriptor> _actionById;
    private readonly HashSet<string> _stateBindingSlotIds;

    private UiSchemaCompatibilityContext(
        UiSchemaIdentity schemaIdentity,
        UiCatalogIdentity? catalogIdentity,
        IReadOnlyList<UiPayloadSlotDescriptor> payloadSlots,
        IReadOnlyList<UiActionDescriptor> actions,
        IReadOnlyList<UiStateBindingDescriptor> stateBindings,
        Dictionary<string, UiPayloadSlotDescriptor> payloadSlotById,
        Dictionary<string, UiActionDescriptor> actionById,
        HashSet<string> stateBindingSlotIds)
    {
        SchemaIdentity = schemaIdentity;
        CatalogIdentity = catalogIdentity;
        PayloadSlots = payloadSlots;
        Actions = actions;
        StateBindings = stateBindings;
        _payloadSlotById = payloadSlotById;
        _actionById = actionById;
        _stateBindingSlotIds = stateBindingSlotIds;
    }

    public UiSchemaIdentity SchemaIdentity { get; }

    public UiCatalogIdentity? CatalogIdentity { get; }

    public IReadOnlyList<UiPayloadSlotDescriptor> PayloadSlots { get; }

    public IReadOnlyList<UiActionDescriptor> Actions { get; }

    public IReadOnlyList<UiStateBindingDescriptor> StateBindings { get; }

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

        context = new UiSchemaCompatibilityContext(
            schema.Identity,
            schema.CatalogIdentity,
            schema.PayloadSlots,
            schema.Actions,
            schema.StateBindings,
            payloadSlotById,
            actionById,
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

    public bool HasStateBindingSlot(string slotId)
    {
        return _stateBindingSlotIds.Contains(slotId);
    }
}
