namespace UltraSim.UI;

public sealed record UiModelSlotValue(
    string SlotId,
    UiModelValueKind Kind,
    string? StringValue = null,
    bool BooleanValue = false,
    long IntegerValue = 0,
    double NumberValue = 0,
    IReadOnlyList<string>? StringListValue = null)
{
    public static UiModelSlotValue Null(string slotId)
    {
        return new UiModelSlotValue(slotId, UiModelValueKind.Null);
    }

    public static UiModelSlotValue String(string slotId, string value)
    {
        return new UiModelSlotValue(slotId, UiModelValueKind.String, StringValue: value ?? string.Empty);
    }

    public static UiModelSlotValue Boolean(string slotId, bool value)
    {
        return new UiModelSlotValue(slotId, UiModelValueKind.Boolean, BooleanValue: value);
    }

    public static UiModelSlotValue Integer(string slotId, long value)
    {
        return new UiModelSlotValue(slotId, UiModelValueKind.Integer, IntegerValue: value);
    }

    public static UiModelSlotValue Number(string slotId, double value)
    {
        return new UiModelSlotValue(slotId, UiModelValueKind.Number, NumberValue: value);
    }

    public static UiModelSlotValue StringList(string slotId, IReadOnlyList<string> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return new UiModelSlotValue(slotId, UiModelValueKind.StringList, StringListValue: values.ToArray());
    }

    public UiValidationResult Validate(UiPayloadSlotDescriptor? descriptor = null)
    {
        UiValidationResult slotResult = UiValidation.ValidateStableId(SlotId, nameof(SlotId), UiValidation.MaxSlotIdLength);
        if (!slotResult.IsValid)
        {
            return slotResult;
        }

        if (!UiValidation.IsDefined(Kind))
        {
            return UiValidationResult.Invalid("Model slot value kind is invalid.");
        }

        if (descriptor is null)
        {
            return ValidateIntrinsic();
        }

        UiValidationResult descriptorResult = descriptor.Validate();
        if (!descriptorResult.IsValid)
        {
            return descriptorResult;
        }

        if (!string.Equals(SlotId, descriptor.SlotId, StringComparison.Ordinal))
        {
            return UiValidationResult.Invalid($"Model slot '{SlotId}' does not match descriptor slot '{descriptor.SlotId}'.");
        }

        if (Kind == UiModelValueKind.Null)
        {
            return descriptor.Required
                ? UiValidationResult.Invalid($"Required model slot '{SlotId}' cannot be null.")
                : UiValidationResult.Valid;
        }

        if (Kind != descriptor.ValueKind)
        {
            return UiValidationResult.Invalid($"Model slot '{SlotId}' expected {descriptor.ValueKind} but got {Kind}.");
        }

        return ValidateValueAgainstDescriptor(descriptor);
    }

    private UiValidationResult ValidateIntrinsic()
    {
        if (Kind == UiModelValueKind.String && StringValue is null)
        {
            return UiValidationResult.Invalid($"String model slot '{SlotId}' requires a string value.");
        }

        if (Kind == UiModelValueKind.StringList)
        {
            if (StringListValue is null)
            {
                return UiValidationResult.Invalid($"String list model slot '{SlotId}' requires string list values.");
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < StringListValue.Count; i++)
            {
                string value = StringListValue[i];
                UiValidationResult valueResult = UiValidation.ValidateStableId(value, nameof(StringListValue), UiValidation.MaxIdentityLength);
                if (!valueResult.IsValid)
                {
                    return valueResult;
                }

                if (!seen.Add(value))
                {
                    return UiValidationResult.Invalid($"String list model slot '{SlotId}' contains duplicate value '{value}'.");
                }
            }
        }

        if (Kind == UiModelValueKind.Number && (double.IsNaN(NumberValue) || double.IsInfinity(NumberValue)))
        {
            return UiValidationResult.Invalid($"Number model slot '{SlotId}' must be finite.");
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateValueAgainstDescriptor(UiPayloadSlotDescriptor descriptor)
    {
        UiValidationResult intrinsic = ValidateIntrinsic();
        if (!intrinsic.IsValid)
        {
            return intrinsic;
        }

        switch (Kind)
        {
            case UiModelValueKind.String:
                if ((StringValue?.Length ?? 0) > descriptor.MaxStringLength)
                {
                    return UiValidationResult.Invalid($"String model slot '{SlotId}' cannot exceed {descriptor.MaxStringLength} characters.");
                }

                break;
            case UiModelValueKind.StringList:
                for (int i = 0; i < (StringListValue?.Count ?? 0); i++)
                {
                    if (StringListValue![i].Length > descriptor.MaxStringLength)
                    {
                        return UiValidationResult.Invalid($"String list model slot '{SlotId}' value cannot exceed {descriptor.MaxStringLength} characters.");
                    }
                }

                break;
            case UiModelValueKind.Integer:
                if (descriptor.MinNumber.HasValue && IntegerValue < descriptor.MinNumber.Value)
                {
                    return UiValidationResult.Invalid($"Integer model slot '{SlotId}' is below minimum.");
                }

                if (descriptor.MaxNumber.HasValue && IntegerValue > descriptor.MaxNumber.Value)
                {
                    return UiValidationResult.Invalid($"Integer model slot '{SlotId}' exceeds maximum.");
                }

                break;
            case UiModelValueKind.Number:
                if (descriptor.MinNumber.HasValue && NumberValue < descriptor.MinNumber.Value)
                {
                    return UiValidationResult.Invalid($"Number model slot '{SlotId}' is below minimum.");
                }

                if (descriptor.MaxNumber.HasValue && NumberValue > descriptor.MaxNumber.Value)
                {
                    return UiValidationResult.Invalid($"Number model slot '{SlotId}' exceeds maximum.");
                }

                break;
        }

        return UiValidationResult.Valid;
    }
}
