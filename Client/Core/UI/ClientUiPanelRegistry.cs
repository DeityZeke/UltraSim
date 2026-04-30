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

    public ClientUiModelUpdateApplyResult ApplyModelUpdate(UiModelUpdateDocument update)
    {
        ArgumentNullException.ThrowIfNull(update);

        if (!_panelsById.TryGetValue(update.PanelInstanceId, out ClientUiPanelInstance? panel))
        {
            return ClientUiModelUpdateApplyResult.MissingPanel("UI panel instance is not open.");
        }

        UiValidationResult validationResult = update.ValidateAgainst(panel.CompatibilityContext);
        if (!validationResult.IsValid)
        {
            return ClientUiModelUpdateApplyResult.Rejected(validationResult.Error);
        }

        if (update.Sequence <= panel.LastModelUpdateSequence)
        {
            return ClientUiModelUpdateApplyResult.IgnoredStale(panel, update);
        }

        ClientUiModelUpdateApplyResult? lifecycleResult = update.Kind switch
        {
            UiModelUpdateKind.SchemaSuperseded => ClientUiModelUpdateApplyResult.SchemaSuperseded(panel, update),
            UiModelUpdateKind.CatalogSuperseded => ClientUiModelUpdateApplyResult.CatalogSuperseded(panel, update),
            _ => null,
        };
        if (lifecycleResult is not null)
        {
            ClientUiPanelInstance markedPanel = panel with { LastModelUpdateSequence = update.Sequence };
            _panelsById[panel.PanelInstanceId] = markedPanel;
            return lifecycleResult with { Panel = markedPanel };
        }

        ClientUiPanelInstance updatedPanel = update.Kind switch
        {
            UiModelUpdateKind.FullModel => ApplyFullModel(panel, update),
            UiModelUpdateKind.SlotPatch or UiModelUpdateKind.ScopedPatch => ApplySlotPatch(panel, update),
            UiModelUpdateKind.RepeaterRows => ApplyRepeaterRows(panel, update),
            _ => panel,
        };

        UiValidationResult modelResult = updatedPanel.Model.ValidateAgainst(updatedPanel.CompatibilityContext);
        if (!modelResult.IsValid)
        {
            return ClientUiModelUpdateApplyResult.Rejected(modelResult.Error);
        }

        _panelsById[updatedPanel.PanelInstanceId] = updatedPanel;
        return ClientUiModelUpdateApplyResult.AppliedUpdate(updatedPanel, update);
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
            EmptyRepeaterRows,
            compatibilityContext,
            LastModelUpdateSequence: -1,
            ++_nextOpenSequence);
    }

    private static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ClientUiRepeaterRowState>> EmptyRepeaterRows { get; } =
        new Dictionary<string, IReadOnlyDictionary<string, ClientUiRepeaterRowState>>(0, StringComparer.Ordinal);

    private static ClientUiPanelInstance ApplyFullModel(
        ClientUiPanelInstance panel,
        UiModelUpdateDocument update)
    {
        return panel with
        {
            Model = new UiModelDocument(update.SchemaIdentity, update.SlotValues),
            LastModelUpdateSequence = update.Sequence,
        };
    }

    private static ClientUiPanelInstance ApplySlotPatch(
        ClientUiPanelInstance panel,
        UiModelUpdateDocument update)
    {
        var valuesBySlot = new Dictionary<string, UiModelSlotValue>(StringComparer.Ordinal);
        for (int i = 0; i < panel.Model.Values.Count; i++)
        {
            UiModelSlotValue value = panel.Model.Values[i];
            valuesBySlot[value.SlotId] = value;
        }

        for (int i = 0; i < update.ClearedSlotIds.Count; i++)
        {
            valuesBySlot.Remove(update.ClearedSlotIds[i]);
        }

        for (int i = 0; i < update.SlotValues.Count; i++)
        {
            UiModelSlotValue value = update.SlotValues[i];
            valuesBySlot[value.SlotId] = value;
        }

        return panel with
        {
            Model = new UiModelDocument(update.SchemaIdentity, valuesBySlot.Values.ToArray()),
            LastModelUpdateSequence = update.Sequence,
        };
    }

    private static ClientUiPanelInstance ApplyRepeaterRows(
        ClientUiPanelInstance panel,
        UiModelUpdateDocument update)
    {
        var repeaters = panel.RepeaterRows.ToDictionary(
            static pair => pair.Key,
            static pair => new Dictionary<string, ClientUiRepeaterRowState>(pair.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);

        for (int i = 0; i < update.RowDeltas.Count; i++)
        {
            UiRepeaterRowDelta delta = update.RowDeltas[i];
            if (!repeaters.TryGetValue(delta.RepeaterSlotId, out Dictionary<string, ClientUiRepeaterRowState>? rows))
            {
                rows = new Dictionary<string, ClientUiRepeaterRowState>(StringComparer.Ordinal);
                repeaters[delta.RepeaterSlotId] = rows;
            }

            switch (delta.Kind)
            {
                case UiRepeaterRowUpdateKind.Add:
                case UiRepeaterRowUpdateKind.Replace:
                    rows[delta.RowKey] = new ClientUiRepeaterRowState(delta.RepeaterSlotId, delta.RowKey, delta.Values);
                    break;
                case UiRepeaterRowUpdateKind.Update:
                    rows[delta.RowKey] = MergeRow(rows.TryGetValue(delta.RowKey, out ClientUiRepeaterRowState? existing)
                        ? existing
                        : new ClientUiRepeaterRowState(delta.RepeaterSlotId, delta.RowKey, []), delta);
                    break;
                case UiRepeaterRowUpdateKind.Remove:
                    rows.Remove(delta.RowKey);
                    break;
            }

            if (rows.Count == 0)
            {
                repeaters.Remove(delta.RepeaterSlotId);
            }
        }

        return panel with
        {
            RepeaterRows = repeaters.ToDictionary(
                static pair => pair.Key,
                static pair => (IReadOnlyDictionary<string, ClientUiRepeaterRowState>)pair.Value,
                StringComparer.Ordinal),
            LastModelUpdateSequence = update.Sequence,
        };
    }

    private static ClientUiRepeaterRowState MergeRow(
        ClientUiRepeaterRowState existing,
        UiRepeaterRowDelta delta)
    {
        var valuesBySlot = new Dictionary<string, UiModelSlotValue>(StringComparer.Ordinal);
        for (int i = 0; i < existing.Values.Count; i++)
        {
            UiModelSlotValue value = existing.Values[i];
            valuesBySlot[value.SlotId] = value;
        }

        for (int i = 0; i < delta.Values.Count; i++)
        {
            UiModelSlotValue value = delta.Values[i];
            valuesBySlot[value.SlotId] = value;
        }

        return existing with { Values = valuesBySlot.Values.ToArray() };
    }
}
