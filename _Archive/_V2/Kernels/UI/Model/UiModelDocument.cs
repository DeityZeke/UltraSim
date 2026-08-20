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
                selectOptionValuesBySlot.TryGetValue(value.SlotId, out HashSet<string>? selectOptionValues))
            {
                UiValidationResult optionResult = ValidateOptionModelValue(value, selectOptionValues);
                if (!optionResult.IsValid)
                {
                    return optionResult;
                }
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

            if (context.TryGetSelectOptions(value.SlotId, out IReadOnlyList<UiSelectOptionDescriptor> options))
            {
                UiValidationResult optionResult = ValidateOptionModelValue(value, options);
                if (!optionResult.IsValid)
                {
                    return optionResult;
                }
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

    private static UiValidationResult ValidateOptionModelValue(UiModelSlotValue value, IReadOnlyCollection<string> optionValues)
    {
        if (value.Kind == UiModelValueKind.String)
        {
            string selected = value.StringValue ?? string.Empty;
            return optionValues.Contains(selected)
                ? UiValidationResult.Valid
                : UiValidationResult.Invalid($"Model option slot '{value.SlotId}' contains unknown option value '{selected}'.");
        }

        if (value.Kind == UiModelValueKind.StringList)
        {
            IReadOnlyList<string> selectedValues = value.StringListValue ?? [];
            for (int i = 0; i < selectedValues.Count; i++)
            {
                if (!optionValues.Contains(selectedValues[i]))
                {
                    return UiValidationResult.Invalid($"Model option slot '{value.SlotId}' contains unknown option value '{selectedValues[i]}'.");
                }
            }
        }

        return UiValidationResult.Valid;
    }

    private static UiValidationResult ValidateOptionModelValue(UiModelSlotValue value, IReadOnlyList<UiSelectOptionDescriptor> options)
    {
        if (value.Kind == UiModelValueKind.String)
        {
            string selected = value.StringValue ?? string.Empty;
            return ContainsSelectOptionValue(options, selected)
                ? UiValidationResult.Valid
                : UiValidationResult.Invalid($"Model option slot '{value.SlotId}' contains unknown option value '{selected}'.");
        }

        if (value.Kind == UiModelValueKind.StringList)
        {
            IReadOnlyList<string> selectedValues = value.StringListValue ?? [];
            for (int i = 0; i < selectedValues.Count; i++)
            {
                if (!ContainsSelectOptionValue(options, selectedValues[i]))
                {
                    return UiValidationResult.Invalid($"Model option slot '{value.SlotId}' contains unknown option value '{selectedValues[i]}'.");
                }
            }
        }

        return UiValidationResult.Valid;
    }
}
