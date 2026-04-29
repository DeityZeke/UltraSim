namespace UltraSim.UI;

public sealed record UiActionRequestDocument(
    string PanelInstanceId,
    UiSchemaIdentity SchemaIdentity,
    UiCatalogIdentity? CatalogIdentity,
    long ClientSequence,
    string ActionId,
    UiActionContextKind ContextKind,
    string? ContextId,
    IReadOnlyList<UiModelSlotValue> FieldValues)
{
    public UiValidationResult ValidateAgainst(UiSchemaDocument schema)
    {
        UiValidationResult schemaResult = schema.Validate();
        if (!schemaResult.IsValid)
        {
            return schemaResult;
        }

        return ValidateAgainstSchemaShape(
            schema.Identity,
            schema.CatalogIdentity,
            schema.Actions,
            value => TryGetPayloadSlot(schema.PayloadSlots, value.SlotId, out UiPayloadSlotDescriptor descriptor)
                ? value.Validate(descriptor)
                : UiValidationResult.Invalid($"Action request contains unknown field slot '{value.SlotId}'."));
    }

    public UiValidationResult ValidateAgainst(UiSchemaCompatibilityContext context)
    {
        UiValidationResult envelopeResult = ValidateEnvelope(context.SchemaIdentity, context.CatalogIdentity);
        if (!envelopeResult.IsValid)
        {
            return envelopeResult;
        }

        if (!context.TryGetAction(ActionId, out UiActionDescriptor action))
        {
            return UiValidationResult.Invalid($"Action request references unknown action '{ActionId}'.");
        }

        UiValidationResult actionResult = action.Validate();
        if (!actionResult.IsValid)
        {
            return actionResult;
        }

        if (action.Availability != UiActionAvailability.Available)
        {
            return UiValidationResult.Invalid($"Action '{ActionId}' is not currently available: {action.Availability}.");
        }

        for (int i = 0; i < FieldValues.Count; i++)
        {
            UiModelSlotValue value = FieldValues[i];
            UiValidationResult duplicateResult = ValidateFieldSlotIsUnique(i, value);
            if (!duplicateResult.IsValid)
            {
                return duplicateResult;
            }

            if (!context.TryGetPayloadSlot(value.SlotId, out UiPayloadSlotDescriptor descriptor))
            {
                return UiValidationResult.Invalid($"Action request contains unknown field slot '{value.SlotId}'.");
            }

            UiValidationResult valueResult = value.Validate(descriptor);
            if (!valueResult.IsValid)
            {
                return valueResult;
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateAgainstSchemaShape(
        UiSchemaIdentity expectedSchemaIdentity,
        UiCatalogIdentity? expectedCatalogIdentity,
        IReadOnlyList<UiActionDescriptor> actions,
        Func<UiModelSlotValue, UiValidationResult> validateValue)
    {
        UiValidationResult envelopeResult = ValidateEnvelope(expectedSchemaIdentity, expectedCatalogIdentity);
        if (!envelopeResult.IsValid)
        {
            return envelopeResult;
        }

        bool found = false;
        for (int i = 0; i < actions.Count; i++)
        {
            UiActionDescriptor action = actions[i];
            UiValidationResult descriptorResult = action.Validate();
            if (!descriptorResult.IsValid)
            {
                return descriptorResult;
            }

            if (string.Equals(action.ActionId, ActionId, StringComparison.Ordinal))
            {
                found = true;
                if (action.Availability != UiActionAvailability.Available)
                {
                    return UiValidationResult.Invalid($"Action '{ActionId}' is not currently available: {action.Availability}.");
                }
            }
        }

        if (!found)
        {
            return UiValidationResult.Invalid($"Action request references unknown action '{ActionId}'.");
        }

        for (int i = 0; i < FieldValues.Count; i++)
        {
            UiModelSlotValue value = FieldValues[i];
            UiValidationResult duplicateResult = ValidateFieldSlotIsUnique(i, value);
            if (!duplicateResult.IsValid)
            {
                return duplicateResult;
            }

            UiValidationResult valueResult = validateValue(value);
            if (!valueResult.IsValid)
            {
                return valueResult;
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateEnvelope(
        UiSchemaIdentity expectedSchemaIdentity,
        UiCatalogIdentity? expectedCatalogIdentity)
    {
        UiValidationResult panelResult = UiValidation.ValidateStableId(
            PanelInstanceId,
            nameof(PanelInstanceId),
            UiValidation.MaxIdentityLength);
        if (!panelResult.IsValid)
        {
            return panelResult;
        }

        if (SchemaIdentity != expectedSchemaIdentity)
        {
            return UiValidationResult.Invalid("Action request schema identity does not match panel schema identity.");
        }

        if (expectedCatalogIdentity is null && CatalogIdentity is not null)
        {
            return UiValidationResult.Invalid("Action request declares a catalog identity for a non-catalog panel.");
        }

        if (expectedCatalogIdentity is not null && CatalogIdentity != expectedCatalogIdentity)
        {
            return UiValidationResult.Invalid("Action request catalog identity does not match panel catalog identity.");
        }

        if (ClientSequence < 0)
        {
            return UiValidationResult.Invalid("Action request sequence cannot be negative.");
        }

        UiValidationResult actionResult = UiValidation.ValidateStableId(ActionId, nameof(ActionId), UiValidation.MaxActionIdLength);
        if (!actionResult.IsValid)
        {
            return actionResult;
        }

        if (!UiValidation.IsDefined(ContextKind))
        {
            return UiValidationResult.Invalid("Action request context kind is invalid.");
        }

        if (!string.IsNullOrWhiteSpace(ContextId))
        {
            UiValidationResult contextResult = UiValidation.ValidateStableId(ContextId, nameof(ContextId), UiValidation.MaxIdentityLength);
            if (!contextResult.IsValid)
            {
                return contextResult;
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateFieldSlotIsUnique(int valueIndex, UiModelSlotValue value)
    {
        for (int j = 0; j < valueIndex; j++)
        {
            if (string.Equals(FieldValues[j].SlotId, value.SlotId, StringComparison.Ordinal))
            {
                return UiValidationResult.Invalid($"Action request contains duplicate field slot '{value.SlotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }

    private static bool TryGetPayloadSlot(
        IReadOnlyList<UiPayloadSlotDescriptor> payloadSlots,
        string slotId,
        out UiPayloadSlotDescriptor descriptor)
    {
        for (int i = 0; i < payloadSlots.Count; i++)
        {
            if (string.Equals(payloadSlots[i].SlotId, slotId, StringComparison.Ordinal))
            {
                descriptor = payloadSlots[i];
                return true;
            }
        }

        descriptor = default!;
        return false;
    }
}
