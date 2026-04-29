namespace UltraSim.UI;

public sealed record UiModelDocument(
    UiSchemaIdentity SchemaIdentity,
    IReadOnlyList<UiModelSlotValue> Values)
{
    public UiValidationResult ValidateAgainst(
        UiSchemaDocument schema,
        UiSchemaValidationLimits? limits = null,
        UiRendererCapabilities? rendererCapabilities = null)
    {
        if (SchemaIdentity != schema.Identity)
        {
            return UiValidationResult.Invalid("Model schema identity does not match schema document identity.");
        }

        UiValidationResult schemaResult = schema.Validate(limits, rendererCapabilities);
        if (!schemaResult.IsValid)
        {
            return schemaResult;
        }

        return ValidateValuesAgainstSlots(schema.PayloadSlots, requireRequiredSlots: true);
    }

    public UiValidationResult ValidateAgainst(UiSchemaCompatibilityContext context)
    {
        UiValidationResult identityResult = context.ValidateSchemaIdentity(SchemaIdentity);
        if (!identityResult.IsValid)
        {
            return identityResult;
        }

        return ValidateValuesAgainstContext(context, requireRequiredSlots: true);
    }

    public UiValidationResult ValidateValuesAgainstSlots(
        IReadOnlyList<UiPayloadSlotDescriptor> payloadSlots,
        bool requireRequiredSlots = true)
    {
        var descriptors = new Dictionary<string, UiPayloadSlotDescriptor>(payloadSlots.Count, StringComparer.Ordinal);
        for (int i = 0; i < payloadSlots.Count; i++)
        {
            UiPayloadSlotDescriptor descriptor = payloadSlots[i];
            UiValidationResult descriptorResult = descriptor.Validate();
            if (!descriptorResult.IsValid)
            {
                return descriptorResult;
            }

            if (!descriptors.TryAdd(descriptor.SlotId, descriptor))
            {
                return UiValidationResult.Invalid($"Duplicate payload slot id '{descriptor.SlotId}'.");
            }
        }

        var provided = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Values.Count; i++)
        {
            UiModelSlotValue value = Values[i];
            if (!descriptors.TryGetValue(value.SlotId, out UiPayloadSlotDescriptor? descriptor))
            {
                return UiValidationResult.Invalid($"Model contains unknown slot '{value.SlotId}'.");
            }

            if (!provided.Add(value.SlotId))
            {
                return UiValidationResult.Invalid($"Model contains duplicate slot '{value.SlotId}'.");
            }

            UiValidationResult valueResult = value.Validate(descriptor);
            if (!valueResult.IsValid)
            {
                return valueResult;
            }
        }

        if (!requireRequiredSlots)
        {
            return UiValidationResult.Valid;
        }

        foreach (UiPayloadSlotDescriptor descriptor in payloadSlots)
        {
            if (descriptor.Required && !provided.Contains(descriptor.SlotId))
            {
                return UiValidationResult.Invalid($"Model is missing required slot '{descriptor.SlotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }

    public UiValidationResult ValidateValuesAgainstContext(
        UiSchemaCompatibilityContext context,
        bool requireRequiredSlots = true)
    {
        var provided = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < Values.Count; i++)
        {
            UiModelSlotValue value = Values[i];
            if (!context.TryGetPayloadSlot(value.SlotId, out UiPayloadSlotDescriptor descriptor))
            {
                return UiValidationResult.Invalid($"Model contains unknown slot '{value.SlotId}'.");
            }

            if (!provided.Add(value.SlotId))
            {
                return UiValidationResult.Invalid($"Model contains duplicate slot '{value.SlotId}'.");
            }

            UiValidationResult valueResult = value.Validate(descriptor);
            if (!valueResult.IsValid)
            {
                return valueResult;
            }
        }

        if (!requireRequiredSlots)
        {
            return UiValidationResult.Valid;
        }

        for (int i = 0; i < context.PayloadSlots.Count; i++)
        {
            UiPayloadSlotDescriptor descriptor = context.PayloadSlots[i];
            if (descriptor.Required && !provided.Contains(descriptor.SlotId))
            {
                return UiValidationResult.Invalid($"Model is missing required slot '{descriptor.SlotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }
}
