namespace UltraSim.UI;

public sealed class UiSchemaCompatibilityContext
{
    private readonly Dictionary<string, UiPayloadSlotDescriptor> _payloadSlotById;

    private UiSchemaCompatibilityContext(
        UiSchemaIdentity schemaIdentity,
        UiCatalogIdentity? catalogIdentity,
        IReadOnlyList<UiPayloadSlotDescriptor> payloadSlots,
        Dictionary<string, UiPayloadSlotDescriptor> payloadSlotById)
    {
        SchemaIdentity = schemaIdentity;
        CatalogIdentity = catalogIdentity;
        PayloadSlots = payloadSlots;
        _payloadSlotById = payloadSlotById;
    }

    public UiSchemaIdentity SchemaIdentity { get; }

    public UiCatalogIdentity? CatalogIdentity { get; }

    public IReadOnlyList<UiPayloadSlotDescriptor> PayloadSlots { get; }

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

        context = new UiSchemaCompatibilityContext(
            schema.Identity,
            schema.CatalogIdentity,
            schema.PayloadSlots,
            payloadSlotById);
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
}
