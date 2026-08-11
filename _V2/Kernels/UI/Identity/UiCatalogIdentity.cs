namespace UltraSim.UI;

public readonly record struct UiCatalogIdentity(
    string ServerIdentity,
    string CatalogType,
    int CatalogRevision,
    string CatalogHashHex,
    int ProtocolVersion,
    string? LocaleOrVariant)
{
    public static bool TryCreate(
        string? serverIdentity,
        string? catalogType,
        int catalogRevision,
        string? catalogHashHex,
        int protocolVersion,
        string? localeOrVariant,
        out UiCatalogIdentity identity,
        out string error)
    {
        identity = default;

        UiValidationResult serverResult = UiValidation.ValidateStableId(serverIdentity, nameof(ServerIdentity));
        if (!serverResult.IsValid)
        {
            error = serverResult.Error;
            return false;
        }

        UiValidationResult catalogResult = UiValidation.ValidateStableId(catalogType, nameof(CatalogType), UiValidation.MaxCatalogTypeLength);
        if (!catalogResult.IsValid)
        {
            error = catalogResult.Error;
            return false;
        }

        if (catalogRevision < 0)
        {
            error = "Catalog revision cannot be negative.";
            return false;
        }

        UiValidationResult hashResult = UiValidation.ValidateSha256Hex(catalogHashHex, nameof(CatalogHashHex));
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

        string? normalizedLocale = string.IsNullOrWhiteSpace(localeOrVariant)
            ? null
            : UiValidation.NormalizeStableId(localeOrVariant);
        if (normalizedLocale is not null)
        {
            UiValidationResult localeResult = UiValidation.ValidateStableId(normalizedLocale, nameof(LocaleOrVariant), 64);
            if (!localeResult.IsValid)
            {
                error = localeResult.Error;
                return false;
            }
        }

        identity = new UiCatalogIdentity(
            UiValidation.NormalizeStableId(serverIdentity!),
            UiValidation.NormalizeStableId(catalogType!),
            catalogRevision,
            UiValidation.NormalizeHash(catalogHashHex!),
            protocolVersion,
            normalizedLocale);
        error = string.Empty;
        return true;
    }
}
