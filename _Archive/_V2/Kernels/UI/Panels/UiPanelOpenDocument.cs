namespace UltraSim.UI;

public sealed record UiPanelOpenDocument(
    string PanelInstanceId,
    UiSchemaIdentity SchemaIdentity,
    UiCatalogIdentity? CatalogIdentity,
    UiModelDocument Model,
    UiSchemaDocument? SchemaDefinition = null)
{
    public UiValidationResult ValidateEnvelope()
    {
        UiValidationResult instanceResult = UiValidation.ValidateStableId(
            PanelInstanceId,
            nameof(PanelInstanceId),
            UiValidation.MaxIdentityLength);
        if (!instanceResult.IsValid)
        {
            return instanceResult;
        }

        if (Model.SchemaIdentity != SchemaIdentity)
        {
            return UiValidationResult.Invalid("Panel open model schema identity does not match the open schema identity.");
        }

        if (SchemaDefinition is not null)
        {
            if (SchemaDefinition.Identity != SchemaIdentity)
            {
                return UiValidationResult.Invalid("Panel open schema definition identity does not match the open schema identity.");
            }

            if (SchemaDefinition.CatalogIdentity != CatalogIdentity)
            {
                return UiValidationResult.Invalid("Panel open schema definition catalog identity does not match the open catalog identity.");
            }
        }

        return UiValidationResult.Valid;
    }

    public UiValidationResult ValidateCold(
        UiSchemaValidationLimits? limits = null,
        UiRendererCapabilities? rendererCapabilities = null)
    {
        UiValidationResult envelopeResult = ValidateEnvelope();
        if (!envelopeResult.IsValid)
        {
            return envelopeResult;
        }

        if (SchemaDefinition is null)
        {
            return UiValidationResult.Invalid("Cold panel opens require an inline schema definition.");
        }

        return Model.ValidateAgainst(SchemaDefinition, limits, rendererCapabilities);
    }

    public UiValidationResult ValidateHot(UiSchemaCompatibilityContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        UiValidationResult envelopeResult = ValidateEnvelope();
        if (!envelopeResult.IsValid)
        {
            return envelopeResult;
        }

        UiValidationResult schemaResult = context.ValidateSchemaIdentity(SchemaIdentity);
        if (!schemaResult.IsValid)
        {
            return schemaResult;
        }

        UiValidationResult catalogResult = context.ValidateCatalogIdentity(CatalogIdentity);
        if (!catalogResult.IsValid)
        {
            return catalogResult;
        }

        return Model.ValidateAgainst(context);
    }
}
