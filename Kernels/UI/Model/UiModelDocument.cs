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

        return ValidateValuesAgainstSlots(schema.PayloadSlots, requireRequiredSlots: true, schema.GetSelectOptions());
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
        bool requireRequiredSlots = true,
        IReadOnlyList<UiSelectOptionDescriptor>? selectOptions = null)
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

        Dictionary<string, HashSet<string>>? selectOptionValuesBySlot = null;
        if (selectOptions is not null && selectOptions.Count > 0)
        {
            selectOptionValuesBySlot = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            for (int i = 0; i < selectOptions.Count; i++)
            {
                UiSelectOptionDescriptor option = selectOptions[i];
                if (!selectOptionValuesBySlot.TryGetValue(option.SlotId, out HashSet<string>? values))
                {
                    values = new HashSet<string>(StringComparer.Ordinal);
                    selectOptionValuesBySlot.Add(option.SlotId, values);
                }

                values.Add(option.Value);
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

            if (selectOptionValuesBySlot is not null &&
                selectOptionValuesBySlot.TryGetValue(value.SlotId, out HashSet<string>? selectOptionValues) &&
                value.Kind == UiModelValueKind.String &&
                !selectOptionValues.Contains(value.StringValue ?? string.Empty))
            {
                return UiValidationResult.Invalid($"Model select slot '{value.SlotId}' contains unknown option value '{value.StringValue}'.");
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

            if (context.TryGetSelectOptions(value.SlotId, out IReadOnlyList<UiSelectOptionDescriptor> options) &&
                value.Kind == UiModelValueKind.String &&
                !ContainsSelectOptionValue(options, value.StringValue ?? string.Empty))
            {
                return UiValidationResult.Invalid($"Model select slot '{value.SlotId}' contains unknown option value '{value.StringValue}'.");
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

    private static bool ContainsSelectOptionValue(IReadOnlyList<UiSelectOptionDescriptor> options, string value)
    {
        for (int i = 0; i < options.Count; i++)
        {
            if (string.Equals(options[i].Value, value, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
