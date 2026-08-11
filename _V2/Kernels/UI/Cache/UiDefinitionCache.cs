namespace UltraSim.UI;

public sealed class UiDefinitionCache
{
    private readonly int _maxSchemaEntries;
    private readonly int _maxCatalogEntries;
    private readonly Dictionary<UiSchemaIdentity, UiSchemaCacheEntry> _schemasByIdentity = [];
    private readonly Dictionary<UiSchemaLogicalKey, UiSchemaIdentity> _latestSchemaByKey = [];
    private readonly Dictionary<UiCatalogIdentity, UiCatalogCacheEntry> _catalogsByIdentity = [];
    private readonly Dictionary<UiCatalogLogicalKey, UiCatalogIdentity> _latestCatalogByKey = [];
    private long _nextSequence;

    public UiDefinitionCache(int maxSchemaEntries = 128, int maxCatalogEntries = 128)
    {
        if (maxSchemaEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxSchemaEntries), "Schema cache capacity must be positive.");
        }

        if (maxCatalogEntries <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCatalogEntries), "Catalog cache capacity must be positive.");
        }

        _maxSchemaEntries = maxSchemaEntries;
        _maxCatalogEntries = maxCatalogEntries;
    }

    public int SchemaCount => _schemasByIdentity.Count;

    public int CatalogCount => _catalogsByIdentity.Count;

    public UiValidationResult PutSchema(
        UiSchemaDocument schema,
        UiSchemaValidationLimits? limits = null,
        UiRendererCapabilities? rendererCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(schema);

        UiValidationResult identityResult = ValidateSchemaIdentity(schema.Identity);
        if (!identityResult.IsValid)
        {
            return identityResult;
        }

        UiValidationResult contextResult = UiSchemaCompatibilityContext.TryCreate(
            schema,
            out UiSchemaCompatibilityContext? context,
            limits,
            rendererCapabilities);
        if (!contextResult.IsValid || context is null)
        {
            return contextResult;
        }

        UiSchemaLogicalKey key = UiSchemaLogicalKey.From(schema.Identity);
        if (_latestSchemaByKey.TryGetValue(key, out UiSchemaIdentity previousIdentity) &&
            previousIdentity != schema.Identity)
        {
            _schemasByIdentity.Remove(previousIdentity);
        }

        var entry = new UiSchemaCacheEntry(schema, context, ++_nextSequence);
        _schemasByIdentity[schema.Identity] = entry;
        _latestSchemaByKey[key] = schema.Identity;
        EvictOldestSchemas();
        return UiValidationResult.Valid;
    }

    public UiSchemaCacheLookupResult TryGetSchema(UiSchemaIdentity identity)
    {
        UiValidationResult identityResult = ValidateSchemaIdentity(identity);
        if (!identityResult.IsValid)
        {
            return UiSchemaCacheLookupResult.Miss;
        }

        if (_schemasByIdentity.TryGetValue(identity, out UiSchemaCacheEntry? entry))
        {
            return UiSchemaCacheLookupResult.Hit(entry);
        }

        UiSchemaLogicalKey key = UiSchemaLogicalKey.From(identity);
        if (_latestSchemaByKey.TryGetValue(key, out UiSchemaIdentity supersededByIdentity))
        {
            return UiSchemaCacheLookupResult.Superseded(supersededByIdentity);
        }

        return UiSchemaCacheLookupResult.Miss;
    }

    public UiValidationResult PutCatalogIdentity(UiCatalogIdentity identity)
    {
        UiValidationResult identityResult = ValidateCatalogIdentity(identity);
        if (!identityResult.IsValid)
        {
            return identityResult;
        }

        UiCatalogLogicalKey key = UiCatalogLogicalKey.From(identity);
        if (_latestCatalogByKey.TryGetValue(key, out UiCatalogIdentity previousIdentity) &&
            previousIdentity != identity)
        {
            _catalogsByIdentity.Remove(previousIdentity);
        }

        var entry = new UiCatalogCacheEntry(identity, ++_nextSequence);
        _catalogsByIdentity[identity] = entry;
        _latestCatalogByKey[key] = identity;
        EvictOldestCatalogs();
        return UiValidationResult.Valid;
    }

    public UiCatalogCacheLookupResult TryGetCatalog(UiCatalogIdentity identity)
    {
        UiValidationResult identityResult = ValidateCatalogIdentity(identity);
        if (!identityResult.IsValid)
        {
            return UiCatalogCacheLookupResult.Miss;
        }

        if (_catalogsByIdentity.TryGetValue(identity, out UiCatalogCacheEntry? entry))
        {
            return UiCatalogCacheLookupResult.Hit(entry);
        }

        UiCatalogLogicalKey key = UiCatalogLogicalKey.From(identity);
        if (_latestCatalogByKey.TryGetValue(key, out UiCatalogIdentity supersededByIdentity))
        {
            return UiCatalogCacheLookupResult.Superseded(supersededByIdentity);
        }

        return UiCatalogCacheLookupResult.Miss;
    }

    public void ClearServer(string serverIdentity)
    {
        string normalized = UiValidation.NormalizeStableId(serverIdentity);

        foreach (UiSchemaIdentity identity in _schemasByIdentity.Keys.ToArray())
        {
            if (string.Equals(identity.ServerIdentity, normalized, StringComparison.Ordinal))
            {
                _schemasByIdentity.Remove(identity);
            }
        }

        foreach (UiSchemaLogicalKey key in _latestSchemaByKey.Keys.ToArray())
        {
            if (string.Equals(key.ServerIdentity, normalized, StringComparison.Ordinal))
            {
                _latestSchemaByKey.Remove(key);
            }
        }

        foreach (UiCatalogIdentity identity in _catalogsByIdentity.Keys.ToArray())
        {
            if (string.Equals(identity.ServerIdentity, normalized, StringComparison.Ordinal))
            {
                _catalogsByIdentity.Remove(identity);
            }
        }

        foreach (UiCatalogLogicalKey key in _latestCatalogByKey.Keys.ToArray())
        {
            if (string.Equals(key.ServerIdentity, normalized, StringComparison.Ordinal))
            {
                _latestCatalogByKey.Remove(key);
            }
        }
    }

    private void EvictOldestSchemas()
    {
        while (_schemasByIdentity.Count > _maxSchemaEntries)
        {
            UiSchemaIdentity oldestIdentity = _schemasByIdentity
                .OrderBy(static pair => pair.Value.CacheSequence)
                .First()
                .Key;
            _schemasByIdentity.Remove(oldestIdentity);
            _latestSchemaByKey.Remove(UiSchemaLogicalKey.From(oldestIdentity));
        }
    }

    private void EvictOldestCatalogs()
    {
        while (_catalogsByIdentity.Count > _maxCatalogEntries)
        {
            UiCatalogIdentity oldestIdentity = _catalogsByIdentity
                .OrderBy(static pair => pair.Value.CacheSequence)
                .First()
                .Key;
            _catalogsByIdentity.Remove(oldestIdentity);
            _latestCatalogByKey.Remove(UiCatalogLogicalKey.From(oldestIdentity));
        }
    }

    private static UiValidationResult ValidateSchemaIdentity(UiSchemaIdentity identity)
    {
        UiValidationResult serverResult = UiValidation.ValidateStableId(identity.ServerIdentity, nameof(identity.ServerIdentity));
        if (!serverResult.IsValid)
        {
            return serverResult;
        }

        UiValidationResult panelResult = UiValidation.ValidateStableId(identity.PanelType, nameof(identity.PanelType), UiValidation.MaxPanelTypeLength);
        if (!panelResult.IsValid)
        {
            return panelResult;
        }

        if (identity.SchemaRevision < 0)
        {
            return UiValidationResult.Invalid("Schema revision cannot be negative.");
        }

        UiValidationResult hashResult = UiValidation.ValidateSha256Hex(identity.SchemaHashHex, nameof(identity.SchemaHashHex));
        if (!hashResult.IsValid)
        {
            return hashResult;
        }

        return identity.ProtocolVersion > 0
            ? UiValidationResult.Valid
            : UiValidationResult.Invalid("Protocol version must be positive.");
    }

    private static UiValidationResult ValidateCatalogIdentity(UiCatalogIdentity identity)
    {
        UiValidationResult serverResult = UiValidation.ValidateStableId(identity.ServerIdentity, nameof(identity.ServerIdentity));
        if (!serverResult.IsValid)
        {
            return serverResult;
        }

        UiValidationResult catalogResult = UiValidation.ValidateStableId(identity.CatalogType, nameof(identity.CatalogType), UiValidation.MaxCatalogTypeLength);
        if (!catalogResult.IsValid)
        {
            return catalogResult;
        }

        if (identity.CatalogRevision < 0)
        {
            return UiValidationResult.Invalid("Catalog revision cannot be negative.");
        }

        UiValidationResult hashResult = UiValidation.ValidateSha256Hex(identity.CatalogHashHex, nameof(identity.CatalogHashHex));
        if (!hashResult.IsValid)
        {
            return hashResult;
        }

        if (identity.ProtocolVersion <= 0)
        {
            return UiValidationResult.Invalid("Protocol version must be positive.");
        }

        return identity.LocaleOrVariant is null
            ? UiValidationResult.Valid
            : UiValidation.ValidateStableId(identity.LocaleOrVariant, nameof(identity.LocaleOrVariant), 64);
    }

    private readonly record struct UiSchemaLogicalKey(
        string ServerIdentity,
        string PanelType,
        int ProtocolVersion)
    {
        public static UiSchemaLogicalKey From(UiSchemaIdentity identity)
        {
            return new UiSchemaLogicalKey(identity.ServerIdentity, identity.PanelType, identity.ProtocolVersion);
        }
    }

    private readonly record struct UiCatalogLogicalKey(
        string ServerIdentity,
        string CatalogType,
        int ProtocolVersion,
        string? LocaleOrVariant)
    {
        public static UiCatalogLogicalKey From(UiCatalogIdentity identity)
        {
            return new UiCatalogLogicalKey(
                identity.ServerIdentity,
                identity.CatalogType,
                identity.ProtocolVersion,
                identity.LocaleOrVariant);
        }
    }
}
