using UltraSim.UI;

namespace UltraSim.Client.UI;

public sealed class ClientUiPanelRegistry
{
    private readonly UiDefinitionCache _definitionCache;
    private readonly Dictionary<string, ClientUiPanelInstance> _panelsById = new(StringComparer.Ordinal);
    private long _nextOpenSequence;

    public ClientUiPanelRegistry(UiDefinitionCache? definitionCache = null)
    {
        _definitionCache = definitionCache ?? new UiDefinitionCache();
    }

    public int Count => _panelsById.Count;

    public UiValidationResult CacheSchema(
        UiSchemaDocument schema,
        UiSchemaValidationLimits? limits = null,
        UiRendererCapabilities? rendererCapabilities = null)
    {
        return _definitionCache.PutSchema(schema, limits, rendererCapabilities);
    }

    public UiValidationResult CacheCatalogIdentity(UiCatalogIdentity identity)
    {
        return _definitionCache.PutCatalogIdentity(identity);
    }

    public ClientUiPanelOpenResult OpenPanel(
        UiPanelOpenDocument open,
        UiSchemaValidationLimits? limits = null,
        UiRendererCapabilities? rendererCapabilities = null)
    {
        ArgumentNullException.ThrowIfNull(open);

        if (open.SchemaDefinition is not null)
        {
            UiValidationResult coldResult = open.ValidateCold(limits, rendererCapabilities);
            if (!coldResult.IsValid)
            {
                return ClientUiPanelOpenResult.Rejected(coldResult.Error);
            }

            UiValidationResult cacheResult = _definitionCache.PutSchema(open.SchemaDefinition, limits, rendererCapabilities);
            if (!cacheResult.IsValid)
            {
                return ClientUiPanelOpenResult.Rejected(cacheResult.Error);
            }

            ClientUiPanelOpenResult? catalogResult = CheckCatalog(open.CatalogIdentity);
            if (catalogResult is not null)
            {
                return catalogResult;
            }

            UiSchemaCacheLookupResult schemaLookup = _definitionCache.TryGetSchema(open.SchemaIdentity);
            if (schemaLookup.Kind != UiDefinitionCacheLookupKind.Hit || schemaLookup.Entry is null)
            {
                return ClientUiPanelOpenResult.Rejected("Cold-open schema was not available after cache insert.");
            }

            var panel = CreatePanel(open, schemaLookup.Entry.Schema.Metadata, schemaLookup.Entry.CompatibilityContext);
            _panelsById[panel.PanelInstanceId] = panel;
            return ClientUiPanelOpenResult.ColdOpened(panel);
        }

        UiSchemaCacheLookupResult hotSchemaLookup = _definitionCache.TryGetSchema(open.SchemaIdentity);
        if (hotSchemaLookup.Kind == UiDefinitionCacheLookupKind.Miss)
        {
            return ClientUiPanelOpenResult.MissingSchema("UI schema is not cached.");
        }

        if (hotSchemaLookup.Kind == UiDefinitionCacheLookupKind.Superseded)
        {
            return ClientUiPanelOpenResult.SupersededSchema(hotSchemaLookup.SupersededByIdentity!.Value);
        }

        if (hotSchemaLookup.Entry is null)
        {
            return ClientUiPanelOpenResult.Rejected("UI schema cache hit did not include an entry.");
        }

        ClientUiPanelOpenResult? missingCatalog = CheckCatalog(open.CatalogIdentity);
        if (missingCatalog is not null)
        {
            return missingCatalog;
        }

        UiValidationResult hotResult = open.ValidateHot(hotSchemaLookup.Entry.CompatibilityContext);
        if (!hotResult.IsValid)
        {
            return ClientUiPanelOpenResult.Rejected(hotResult.Error);
        }

        ClientUiPanelInstance hotPanel = CreatePanel(
            open,
            hotSchemaLookup.Entry.Schema.Metadata,
            hotSchemaLookup.Entry.CompatibilityContext);
        _panelsById[hotPanel.PanelInstanceId] = hotPanel;
        return ClientUiPanelOpenResult.HotOpened(hotPanel);
    }

    public ClientUiActionRouteResult ValidateActionRequest(UiActionRequestDocument request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!_panelsById.TryGetValue(request.PanelInstanceId, out ClientUiPanelInstance? panel))
        {
            return ClientUiActionRouteResult.MissingPanel("UI panel instance is not open.");
        }

        UiValidationResult result = request.ValidateAgainst(panel.CompatibilityContext);
        return result.IsValid
            ? ClientUiActionRouteResult.Validated(request, panel)
            : ClientUiActionRouteResult.Rejected(result.Error);
    }

    public bool TryGetPanel(string panelInstanceId, out ClientUiPanelInstance panel)
    {
        return _panelsById.TryGetValue(panelInstanceId, out panel!);
    }

    public bool ClosePanel(string panelInstanceId)
    {
        return _panelsById.Remove(panelInstanceId);
    }

    public void Clear()
    {
        _panelsById.Clear();
    }

    public void ClearServer(string serverIdentity)
    {
        _definitionCache.ClearServer(serverIdentity);
        foreach (string panelId in _panelsById
            .Where(pair => string.Equals(pair.Value.SchemaIdentity.ServerIdentity, serverIdentity.Trim(), StringComparison.Ordinal))
            .Select(pair => pair.Key)
            .ToArray())
        {
            _panelsById.Remove(panelId);
        }
    }

    private ClientUiPanelOpenResult? CheckCatalog(UiCatalogIdentity? catalogIdentity)
    {
        if (catalogIdentity is null)
        {
            return null;
        }

        UiCatalogCacheLookupResult catalogLookup = _definitionCache.TryGetCatalog(catalogIdentity.Value);
        return catalogLookup.Kind switch
        {
            UiDefinitionCacheLookupKind.Hit => null,
            UiDefinitionCacheLookupKind.Superseded => ClientUiPanelOpenResult.SupersededCatalog(catalogLookup.SupersededByIdentity!.Value),
            _ => ClientUiPanelOpenResult.MissingCatalog("UI catalog is not cached."),
        };
    }

    private ClientUiPanelInstance CreatePanel(
        UiPanelOpenDocument open,
        UiPanelMetadata metadata,
        UiSchemaCompatibilityContext compatibilityContext)
    {
        return new ClientUiPanelInstance(
            open.PanelInstanceId,
            open.SchemaIdentity,
            open.CatalogIdentity,
            metadata,
            open.Model,
            compatibilityContext,
            ++_nextOpenSequence);
    }
}
