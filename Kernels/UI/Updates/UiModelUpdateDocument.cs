namespace UltraSim.UI;

public sealed record UiModelUpdateDocument(
    string PanelInstanceId,
    UiSchemaIdentity SchemaIdentity,
    UiCatalogIdentity? CatalogIdentity,
    long Sequence,
    UiModelUpdateKind Kind,
    string? ScopeId,
    IReadOnlyList<UiModelSlotValue> SlotValues,
    IReadOnlyList<string> ClearedSlotIds,
    IReadOnlyList<UiRepeaterRowDelta> RowDeltas)
{
    public UiValidationResult ValidateAgainst(
        UiSchemaDocument schema,
        UiSchemaValidationLimits? limits = null,
        UiRendererCapabilities? rendererCapabilities = null)
    {
        UiValidationResult selfResult = ValidateEnvelope(schema);
        if (!selfResult.IsValid)
        {
            return selfResult;
        }

        UiValidationResult schemaResult = schema.Validate(limits, rendererCapabilities);
        if (!schemaResult.IsValid)
        {
            return schemaResult;
        }

        return Kind switch
        {
            UiModelUpdateKind.FullModel => ValidateFullModel(schema, limits, rendererCapabilities),
            UiModelUpdateKind.SlotPatch => ValidateSlotPatch(schema),
            UiModelUpdateKind.ScopedPatch => ValidateScopedPatch(schema),
            UiModelUpdateKind.RepeaterRows => ValidateRepeaterRows(),
            UiModelUpdateKind.SchemaSuperseded => ValidateSupersededPayload(),
            UiModelUpdateKind.CatalogSuperseded => ValidateSupersededPayload(),
            _ => UiValidationResult.Invalid("Model update kind is invalid."),
        };
    }

    public UiValidationResult ValidateAgainst(UiSchemaCompatibilityContext context)
    {
        UiValidationResult selfResult = ValidateEnvelope(context);
        if (!selfResult.IsValid)
        {
            return selfResult;
        }

        return Kind switch
        {
            UiModelUpdateKind.FullModel => ValidateFullModel(context),
            UiModelUpdateKind.SlotPatch => ValidateSlotPatch(context),
            UiModelUpdateKind.ScopedPatch => ValidateScopedPatch(context),
            UiModelUpdateKind.RepeaterRows => ValidateRepeaterRows(),
            UiModelUpdateKind.SchemaSuperseded => ValidateSupersededPayload(),
            UiModelUpdateKind.CatalogSuperseded => ValidateSupersededPayload(),
            _ => UiValidationResult.Invalid("Model update kind is invalid."),
        };
    }

    private UiValidationResult ValidateEnvelope(UiSchemaDocument schema)
    {
        UiValidationResult panelResult = UiValidation.ValidateStableId(
            PanelInstanceId,
            nameof(PanelInstanceId),
            UiValidation.MaxIdentityLength);
        if (!panelResult.IsValid)
        {
            return panelResult;
        }

        if (SchemaIdentity != schema.Identity)
        {
            return UiValidationResult.Invalid("Model update schema identity does not match schema document identity.");
        }

        if (schema.CatalogIdentity is null && CatalogIdentity is not null)
        {
            return UiValidationResult.Invalid("Model update declares a catalog identity for a non-catalog schema.");
        }

        if (schema.CatalogIdentity is not null && CatalogIdentity != schema.CatalogIdentity)
        {
            return UiValidationResult.Invalid("Model update catalog identity does not match schema document catalog identity.");
        }

        if (Sequence < 0)
        {
            return UiValidationResult.Invalid("Model update sequence cannot be negative.");
        }

        if (!UiValidation.IsDefined(Kind))
        {
            return UiValidationResult.Invalid("Model update kind is invalid.");
        }

        if (ScopeId is null)
        {
            return UiValidationResult.Valid;
        }

        return UiValidation.ValidateStableId(ScopeId, nameof(ScopeId), UiValidation.MaxIdentityLength);
    }

    private UiValidationResult ValidateEnvelope(UiSchemaCompatibilityContext context)
    {
        UiValidationResult panelResult = UiValidation.ValidateStableId(
            PanelInstanceId,
            nameof(PanelInstanceId),
            UiValidation.MaxIdentityLength);
        if (!panelResult.IsValid)
        {
            return panelResult;
        }

        UiValidationResult schemaIdentityResult = context.ValidateSchemaIdentity(SchemaIdentity);
        if (!schemaIdentityResult.IsValid)
        {
            return schemaIdentityResult;
        }

        UiValidationResult catalogIdentityResult = context.ValidateCatalogIdentity(CatalogIdentity);
        if (!catalogIdentityResult.IsValid)
        {
            return catalogIdentityResult;
        }

        if (Sequence < 0)
        {
            return UiValidationResult.Invalid("Model update sequence cannot be negative.");
        }

        if (!UiValidation.IsDefined(Kind))
        {
            return UiValidationResult.Invalid("Model update kind is invalid.");
        }

        if (ScopeId is null)
        {
            return UiValidationResult.Valid;
        }

        return UiValidation.ValidateStableId(ScopeId, nameof(ScopeId), UiValidation.MaxIdentityLength);
    }

    private UiValidationResult ValidateFullModel(
        UiSchemaDocument schema,
        UiSchemaValidationLimits? limits,
        UiRendererCapabilities? rendererCapabilities)
    {
        if (ScopeId is not null || ClearedSlotIds.Count != 0 || RowDeltas.Count != 0)
        {
            return UiValidationResult.Invalid("Full model updates cannot contain scope, cleared slots, or row deltas.");
        }

        var model = new UiModelDocument(SchemaIdentity, SlotValues);
        return model.ValidateAgainst(schema, limits, rendererCapabilities);
    }

    private UiValidationResult ValidateFullModel(UiSchemaCompatibilityContext context)
    {
        if (ScopeId is not null || ClearedSlotIds.Count != 0 || RowDeltas.Count != 0)
        {
            return UiValidationResult.Invalid("Full model updates cannot contain scope, cleared slots, or row deltas.");
        }

        var model = new UiModelDocument(SchemaIdentity, SlotValues);
        return model.ValidateAgainst(context);
    }

    private UiValidationResult ValidateSlotPatch(UiSchemaDocument schema)
    {
        if (ScopeId is not null || RowDeltas.Count != 0)
        {
            return UiValidationResult.Invalid("Slot patch updates cannot contain scope or row deltas.");
        }

        return ValidatePatchValuesAndClears(schema);
    }

    private UiValidationResult ValidateSlotPatch(UiSchemaCompatibilityContext context)
    {
        if (ScopeId is not null || RowDeltas.Count != 0)
        {
            return UiValidationResult.Invalid("Slot patch updates cannot contain scope or row deltas.");
        }

        return ValidatePatchValuesAndClears(context);
    }

    private UiValidationResult ValidateScopedPatch(UiSchemaDocument schema)
    {
        if (string.IsNullOrWhiteSpace(ScopeId))
        {
            return UiValidationResult.Invalid("Scoped patch updates require a scope id.");
        }

        if (RowDeltas.Count != 0)
        {
            return UiValidationResult.Invalid("Scoped patch updates cannot contain row deltas.");
        }

        return ValidatePatchValuesAndClears(schema);
    }

    private UiValidationResult ValidateScopedPatch(UiSchemaCompatibilityContext context)
    {
        if (string.IsNullOrWhiteSpace(ScopeId))
        {
            return UiValidationResult.Invalid("Scoped patch updates require a scope id.");
        }

        if (RowDeltas.Count != 0)
        {
            return UiValidationResult.Invalid("Scoped patch updates cannot contain row deltas.");
        }

        return ValidatePatchValuesAndClears(context);
    }

    private UiValidationResult ValidatePatchValuesAndClears(UiSchemaDocument schema)
    {
        var patchModel = new UiModelDocument(SchemaIdentity, SlotValues);
        UiValidationResult valueResult = patchModel.ValidateValuesAgainstSlots(schema.PayloadSlots, requireRequiredSlots: false);
        if (!valueResult.IsValid)
        {
            return valueResult;
        }

        var declaredSlots = new Dictionary<string, UiPayloadSlotDescriptor>(StringComparer.Ordinal);
        for (int i = 0; i < schema.PayloadSlots.Count; i++)
        {
            UiPayloadSlotDescriptor descriptor = schema.PayloadSlots[i];
            if (!declaredSlots.TryAdd(descriptor.SlotId, descriptor))
            {
                return UiValidationResult.Invalid($"Duplicate payload slot id '{descriptor.SlotId}'.");
            }
        }

        var cleared = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ClearedSlotIds.Count; i++)
        {
            string slotId = ClearedSlotIds[i];
            UiValidationResult slotResult = UiValidation.ValidateStableId(slotId, nameof(ClearedSlotIds), UiValidation.MaxSlotIdLength);
            if (!slotResult.IsValid)
            {
                return slotResult;
            }

            if (!declaredSlots.TryGetValue(slotId, out UiPayloadSlotDescriptor? descriptor))
            {
                return UiValidationResult.Invalid($"Model update clears unknown slot '{slotId}'.");
            }

            if (descriptor.Required)
            {
                return UiValidationResult.Invalid($"Model update cannot clear required slot '{slotId}'.");
            }

            if (!cleared.Add(slotId))
            {
                return UiValidationResult.Invalid($"Model update clears duplicate slot '{slotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidatePatchValuesAndClears(UiSchemaCompatibilityContext context)
    {
        var patchModel = new UiModelDocument(SchemaIdentity, SlotValues);
        UiValidationResult valueResult = patchModel.ValidateValuesAgainstContext(context, requireRequiredSlots: false);
        if (!valueResult.IsValid)
        {
            return valueResult;
        }

        var cleared = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < ClearedSlotIds.Count; i++)
        {
            string slotId = ClearedSlotIds[i];
            UiValidationResult slotResult = UiValidation.ValidateStableId(slotId, nameof(ClearedSlotIds), UiValidation.MaxSlotIdLength);
            if (!slotResult.IsValid)
            {
                return slotResult;
            }

            if (!context.TryGetPayloadSlot(slotId, out UiPayloadSlotDescriptor descriptor))
            {
                return UiValidationResult.Invalid($"Model update clears unknown slot '{slotId}'.");
            }

            if (descriptor.Required)
            {
                return UiValidationResult.Invalid($"Model update cannot clear required slot '{slotId}'.");
            }

            if (!cleared.Add(slotId))
            {
                return UiValidationResult.Invalid($"Model update clears duplicate slot '{slotId}'.");
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateRepeaterRows()
    {
        if (ScopeId is not null || SlotValues.Count != 0 || ClearedSlotIds.Count != 0)
        {
            return UiValidationResult.Invalid("Repeater row updates cannot contain scope, slot values, or cleared slots.");
        }

        if (RowDeltas.Count == 0)
        {
            return UiValidationResult.Invalid("Repeater row updates require at least one row delta.");
        }

        for (int i = 0; i < RowDeltas.Count; i++)
        {
            UiValidationResult rowResult = RowDeltas[i].Validate();
            if (!rowResult.IsValid)
            {
                return rowResult;
            }
        }

        return UiValidationResult.Valid;
    }

    private UiValidationResult ValidateSupersededPayload()
    {
        if (SlotValues.Count != 0 || ClearedSlotIds.Count != 0 || RowDeltas.Count != 0)
        {
            return UiValidationResult.Invalid("Schema/catalog superseded updates cannot contain payload deltas.");
        }

        return UiValidationResult.Valid;
    }
}
