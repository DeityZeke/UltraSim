namespace UltraSim.UI;

public readonly record struct UiCatalogCacheLookupResult(
    UiDefinitionCacheLookupKind Kind,
    UiCatalogCacheEntry? Entry,
    UiCatalogIdentity? SupersededByIdentity)
{
    public static UiCatalogCacheLookupResult Hit(UiCatalogCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new UiCatalogCacheLookupResult(UiDefinitionCacheLookupKind.Hit, entry, null);
    }

    public static UiCatalogCacheLookupResult Miss { get; } =
        new(UiDefinitionCacheLookupKind.Miss, null, null);

    public static UiCatalogCacheLookupResult Superseded(UiCatalogIdentity supersededByIdentity)
    {
        return new UiCatalogCacheLookupResult(UiDefinitionCacheLookupKind.Superseded, null, supersededByIdentity);
    }
}
