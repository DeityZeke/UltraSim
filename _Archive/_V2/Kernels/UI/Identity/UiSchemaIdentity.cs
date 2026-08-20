namespace UltraSim.UI;

public readonly record struct UiSchemaIdentity(
    string ServerIdentity,
    string PanelType,
    int SchemaRevision,
    string SchemaHashHex,
    int ProtocolVersion)
{
    public static bool TryCreate(
        string? serverIdentity,
        string? panelType,
        int schemaRevision,
        string? schemaHashHex,
        int protocolVersion,
        out UiSchemaIdentity identity,
        out string error)
    {
        identity = default;

        UiValidationResult serverResult = UiValidation.ValidateStableId(serverIdentity, nameof(ServerIdentity));
        if (!serverResult.IsValid)
        {
            error = serverResult.Error;
            return false;
        }

        UiValidationResult panelResult = UiValidation.ValidateStableId(panelType, nameof(PanelType), UiValidation.MaxPanelTypeLength);
        if (!panelResult.IsValid)
        {
            error = panelResult.Error;
            return false;
        }

        if (schemaRevision < 0)
        {
            error = "Schema revision cannot be negative.";
            return false;
        }

        UiValidationResult hashResult = UiValidation.ValidateSha256Hex(schemaHashHex, nameof(SchemaHashHex));
        if (!hashResult.IsValid)
        {
            error = hashResult.Error;
            return false;
        }

        if (protocolVersion <= 0)
        {
            error = "Protocol version must be positive.";
            return false;
        }

        identity = new UiSchemaIdentity(
            UiValidation.NormalizeStableId(serverIdentity!),
            UiValidation.NormalizeStableId(panelType!),
            schemaRevision,
            UiValidation.NormalizeHash(schemaHashHex!),
            protocolVersion);
        error = string.Empty;
        return true;
    }
}
