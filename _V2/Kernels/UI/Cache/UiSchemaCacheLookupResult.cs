namespace UltraSim.UI;

public readonly record struct UiSchemaCacheLookupResult(
    UiDefinitionCacheLookupKind Kind,
    UiSchemaCacheEntry? Entry,
    UiSchemaIdentity? SupersededByIdentity)
{
    public static UiSchemaCacheLookupResult Hit(UiSchemaCacheEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new UiSchemaCacheLookupResult(UiDefinitionCacheLookupKind.Hit, entry, null);
    }

    public static UiSchemaCacheLookupResult Miss { get; } =
        new(UiDefinitionCacheLookupKind.Miss, null, null);

    public static UiSchemaCacheLookupResult Superseded(UiSchemaIdentity supersededByIdentity)
    {
        return new UiSchemaCacheLookupResult(UiDefinitionCacheLookupKind.Superseded, null, supersededByIdentity);
    }
}
