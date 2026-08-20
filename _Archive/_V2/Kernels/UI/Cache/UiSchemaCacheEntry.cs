namespace UltraSim.UI;

public sealed record UiSchemaCacheEntry(
    UiSchemaDocument Schema,
    UiSchemaCompatibilityContext CompatibilityContext,
    long CacheSequence);
