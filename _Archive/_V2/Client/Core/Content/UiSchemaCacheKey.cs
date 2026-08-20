namespace UltraSim.Client.Content;

public readonly record struct UiSchemaCacheKey(
    ServerContentIdentity Server,
    string PanelType,
    int Revision,
    string SchemaHashHex)
{
    public const int MaxPanelTypeLength = 96;

    public static bool TryCreate(
        ServerContentIdentity server,
        string? panelType,
        int revision,
        string? schemaHashHex,
        out UiSchemaCacheKey key,
        out string error)
    {
        string normalizedPanelType = (panelType ?? string.Empty).Trim();
        if (!IsSafePanelType(normalizedPanelType))
        {
            key = default;
            error = $"Panel type must be 1-{MaxPanelTypeLength} characters and contain only letters, digits, '.', '_', or '-'.";
            return false;
        }

        if (revision < 0)
        {
            key = default;
            error = "Schema revision cannot be negative.";
            return false;
        }

        if (!ContentPathValidation.TryNormalizeSha256Hex(schemaHashHex, out string normalizedHash, out error))
        {
            key = default;
            return false;
        }

        key = new UiSchemaCacheKey(server, normalizedPanelType, revision, normalizedHash);
        error = string.Empty;
        return true;
    }

    private static bool IsSafePanelType(string value)
    {
        if (value.Length is 0 or > MaxPanelTypeLength)
        {
            return false;
        }

        for (int i = 0; i < value.Length; i++)
        {
            char c = value[i];
            if (char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '-')
            {
                continue;
            }

            return false;
        }

        return true;
    }
}
