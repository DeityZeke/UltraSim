using UltraSim.ECS;
using UltraSim.Game.Inventory;
using UltraSim.Server.Runtime;
using UltraSim.UI;

namespace UltraSim.Game.UI;

/// <summary>Compiled Inventory view definition and typed binding identities.</summary>
public static class InventoryUi
{
    private static readonly Contract _contract = CreateContract();

    public static CompiledUiDefinition Definition => _contract.Definition;
    public static UiSlotId Title => _contract.Title;
    public static UiSlotId Items => _contract.Items;
    public static UiSlotId Status => _contract.Status;
    public static UiSlotId Details => _contract.Details;
    public static UiSlotId TransferLabel => _contract.TransferLabel;
    public static UiSlotId TransferDestinations => _contract.TransferDestinations;
    public static UiSlotId SplitLabel => _contract.SplitLabel;
    public static UiSlotId EquipLabel => _contract.EquipLabel;
    public static UiSlotId UnequipLabel => _contract.UnequipLabel;
    public static UiSlotId DropLabel => _contract.DropLabel;
    public static UiSlotId CloseLabel => _contract.CloseLabel;
    public static UiSlotId GridLabel => _contract.GridLabel;
    public static UiSlotId GridCells => _contract.GridCells;
    public static UiActionId Select => _contract.Select;
    public static UiActionId MoveTo => _contract.MoveTo;
    public static UiActionId Split => _contract.Split;
    public static UiActionId Equip => _contract.Equip;
    public static UiActionId Unequip => _contract.Unequip;
    public static UiActionId Drop => _contract.Drop;
    public static UiActionId PlaceAt => _contract.PlaceAt;
    public static UiActionId Close => _contract.Close;

    private static Contract CreateContract()
    {
        var source = new UiDefinitionBuilder("game.inventory.gump", version: 7);
        UiSlotReference title = source.DeclareSlot("title", UiSlotValueKind.Text);
        UiSlotReference items = source.DeclareSlot("items", UiSlotValueKind.List);
        UiSlotReference status = source.DeclareSlot("status", UiSlotValueKind.Text);
        UiSlotReference details = source.DeclareSlot("details", UiSlotValueKind.Text);
        UiSlotReference transferLabel = source.DeclareSlot("transfer-label", UiSlotValueKind.Text);
        UiSlotReference transferDestinations = source.DeclareSlot("transfer-destinations", UiSlotValueKind.List);
        UiSlotReference splitLabel = source.DeclareSlot("split-label", UiSlotValueKind.Text);
        UiSlotReference equipLabel = source.DeclareSlot("equip-label", UiSlotValueKind.Text);
        UiSlotReference unequipLabel = source.DeclareSlot("unequip-label", UiSlotValueKind.Text);
        UiSlotReference dropLabel = source.DeclareSlot("drop-label", UiSlotValueKind.Text);
        UiSlotReference closeLabel = source.DeclareSlot("close-label", UiSlotValueKind.Text);
        UiSlotReference gridLabel = source.DeclareSlot("grid-label", UiSlotValueKind.Text);
        UiSlotReference gridCells = source.DeclareSlot("grid-cells", UiSlotValueKind.List);
        UiActionReference select = source.DeclareAction("select", acceptsRowContext: true);
        UiActionReference moveTo = source.DeclareAction("move-to", acceptsRowContext: true);
        UiActionReference split = source.DeclareAction("split");
        UiActionReference equip = source.DeclareAction("equip");
        UiActionReference unequip = source.DeclareAction("unequip");
        UiActionReference drop = source.DeclareAction("drop");
        UiActionReference placeAt = source.DeclareAction("place-at", acceptsRowContext: true);
        UiActionReference close = source.DeclareAction("close");
        source.AddCanvas(new UiCanvasOptions(800, 600), canvas =>
        {
            var surface = new UiCanvasPlacement(16, 16, 768, 568);
            canvas.AddPanel(
                "background",
                "inventory.surface",
                UiChildLayout.Canvas(surface));
            canvas.AddVertical(
                "content",
                content =>
                {
                    content.AddText("title", title);
                    content.AddList("items", items, select);
                    content.AddText("details", details);
                    content.AddText("transfer-label", transferLabel);
                    content.AddList("transfer-destinations", transferDestinations, moveTo);
                    content.AddText("grid-label", gridLabel);
                    content.AddList("grid-cells", gridCells, placeAt);
                    content.AddHorizontal("actions", actions =>
                    {
                        actions.AddButton("split", split, splitLabel);
                        actions.AddButton("equip", equip, equipLabel);
                        actions.AddButton("unequip", unequip, unequipLabel);
                        actions.AddButton("drop", drop, dropLabel);
                        actions.AddButton("close", close, closeLabel);
                    });
                    content.AddText("status", status);
                },
                UiChildLayout.Canvas(new UiCanvasPlacement(32, 32, 736, 536)),
                new UiFlowOptions(
                    spacing: 8,
                    padding: new UiPadding(8, 8, 8, 8),
                    alignment: UiAlignment.Stretch));
        });
        CompiledUiDefinition definition = UiDefinitionCompiler.Compile(source);
        return new Contract(
            definition,
            definition.Prepare(title),
            definition.Prepare(items),
            definition.Prepare(status),
            definition.Prepare(details),
            definition.Prepare(transferLabel),
            definition.Prepare(transferDestinations),
            definition.Prepare(splitLabel),
            definition.Prepare(equipLabel),
            definition.Prepare(unequipLabel),
            definition.Prepare(dropLabel),
            definition.Prepare(closeLabel),
            definition.Prepare(gridLabel),
            definition.Prepare(gridCells),
            definition.Prepare(select),
            definition.Prepare(moveTo),
            definition.Prepare(split),
            definition.Prepare(equip),
            definition.Prepare(unequip),
            definition.Prepare(drop),
            definition.Prepare(placeAt),
            definition.Prepare(close));
    }

    private sealed record Contract(
        CompiledUiDefinition Definition,
        UiSlotId Title,
        UiSlotId Items,
        UiSlotId Status,
        UiSlotId Details,
        UiSlotId TransferLabel,
        UiSlotId TransferDestinations,
        UiSlotId SplitLabel,
        UiSlotId EquipLabel,
        UiSlotId UnequipLabel,
        UiSlotId DropLabel,
        UiSlotId CloseLabel,
        UiSlotId GridLabel,
        UiSlotId GridCells,
        UiActionId Select,
        UiActionId MoveTo,
        UiActionId Split,
        UiActionId Equip,
        UiActionId Unequip,
        UiActionId Drop,
        UiActionId PlaceAt,
        UiActionId Close);
}

/// <summary>Stable definition-cache identity used by the Inventory server workflow.</summary>
public readonly record struct InventoryUiOptions(
    string ServerIdentity = "local-server",
    string CompositionIdentity = "game-composition",
    ushort ProtocolVersion = 1)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ServerIdentity);
        ArgumentException.ThrowIfNullOrWhiteSpace(CompositionIdentity);
        if (ProtocolVersion == 0)
            throw new ArgumentOutOfRangeException(nameof(ProtocolVersion));
    }
}

/// <summary>Expected result of opening an authoritative Inventory view session.</summary>
public readonly record struct InventoryUiOpenResult(
    bool Succeeded,
    UiViewInstanceId View,
    string Message);

/// <summary>
/// Game UI workflow for Inventory. Retained references use complete persistent
/// keys and successful mutations publish row-granular list deltas.
/// </summary>
public sealed class InventoryUiController : IDisposable
{
    private readonly ServerGameFacet _runtime;
    private readonly IUiSemanticPort _ui;
    private readonly UiDefinitionCacheKey _cacheKey;
    private readonly Dictionary<UiViewInstanceId, Session> _sessions = [];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _disposed;

    public InventoryUiController(
        ServerGameFacet runtime,
        IUiSemanticPort ui,
        InventoryUiOptions options = default)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        if (options.ServerIdentity is null && options.CompositionIdentity is null)
            options = new InventoryUiOptions(
                ServerIdentity: "local-server",
                CompositionIdentity: "game-composition",
                ProtocolVersion: 1);
        options.Validate();
        _cacheKey = new UiDefinitionCacheKey(
            options.ServerIdentity!,
            options.CompositionIdentity!,
            options.ProtocolVersion,
            InventoryUi.Definition.Key,
            InventoryUi.Definition.Version,
            InventoryUi.Definition.Hash);
        _runtime.GenerationPublished += HandleGenerationPublished;
    }

    public int OpenViewCount { get { EnsureOperational(); return _sessions.Count; } }
    public event Action<InventoryMutationResult>? MutationCompleted;
    public event Action<Exception>? PresentationFaulted;
    public event Action<Exception>? ObserverFaulted;

    public InventoryUiOpenResult Open(Entity container) =>
        OpenCore(container, hot: false, Entity.Null, Array.Empty<Entity>());

    public InventoryUiOpenResult OpenHot(Entity container) =>
        OpenCore(container, hot: true, Entity.Null, Array.Empty<Entity>());

    /// <summary>
    /// Opens an owner-private inventory only when the container and every
    /// explicit transfer destination are currently owned by the same actor.
    /// The durable owner relationship is retained and revalidated for every
    /// authoritative action and generation replacement.
    /// </summary>
    public InventoryUiOpenResult OpenOwned(
        Entity container,
        Entity owner,
        params Entity[] transferDestinations) =>
        OpenCore(container, hot: false, owner,
            transferDestinations ?? throw new ArgumentNullException(nameof(transferDestinations)));

    public InventoryUiOpenResult OpenOwnedHot(
        Entity container,
        Entity owner,
        params Entity[] transferDestinations) =>
        OpenCore(container, hot: true, owner,
            transferDestinations ?? throw new ArgumentNullException(nameof(transferDestinations)));

    public bool Close(UiViewInstanceId instance)
    {
        EnsureOperational();
        if (!_sessions.ContainsKey(instance))
            return false;
        bool closed;
        try { closed = _ui.CloseView(new UiCloseView(instance)); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _sessions.Remove(instance);
            TryUnbindDetached(instance);
            NotifyObservers(PresentationFaulted, exception);
            return false;
        }
        if (!closed)
        {
            _sessions.Remove(instance);
            TryUnbindDetached(instance);
            NotifyObservers(PresentationFaulted, new InvalidOperationException(
                $"Inventory view {instance.Value} was no longer open during cleanup."));
            return false;
        }
        _sessions.Remove(instance);
        TryUnbindDetached(instance);
        return true;
    }

    /// <summary>
    /// Republishes authoritative state for one retained view after an external
    /// Game mutation such as a container-capacity upgrade. This is not a client
    /// authority path; the caller must have already committed the Game mutation.
    /// </summary>
    public bool Refresh(UiViewInstanceId instance, string status = "Inventory refreshed.")
    {
        EnsureOperational();
        ArgumentException.ThrowIfNullOrWhiteSpace(status);
        if (!_sessions.TryGetValue(instance, out Session? session))
            return false;
        InventoryProjection projection = CaptureReplacementProjection(session);
        if (!projection.Succeeded)
        {
            DetachSession(session, new InvalidOperationException(
                $"Inventory view could not refresh: {projection.Message}"));
            return false;
        }
        return PublishCompleteProjection(session, projection, status);
    }

    private InventoryUiOpenResult OpenCore(
        Entity container,
        bool hot,
        Entity requiredOwner,
        IReadOnlyList<Entity> transferDestinations)
    {
        EnsureOperational();
        InventoryProjection projection = CaptureProjection(
            container, requiredOwner, transferDestinations);
        if (!projection.Succeeded)
            return new InventoryUiOpenResult(false, UiViewInstanceId.Invalid, projection.Message);
        UiModel model = CreateModel(
            projection.Items,
            projection.Destinations,
            projection.Title,
            "Ready",
            projection.GridWidth,
            projection.GridHeight);
        UiViewInstanceId instance = hot
            ? _ui.OpenHotView(new UiHotOpenView(_cacheKey, model, sequence: 1))
            : _ui.OpenView(new UiOpenView(_cacheKey, InventoryUi.Definition, model, sequence: 1));
        if (!instance.IsValid)
            throw new InvalidOperationException("The inventory UI port returned an invalid view ID.");
        if (_sessions.ContainsKey(instance))
        {
            TryCloseDetached(instance);
            throw new InvalidOperationException("The inventory UI port returned an already-owned view ID.");
        }
        try { _ui.BindActions(instance, HandleAction); }
        catch
        {
            TryCloseDetached(instance);
            throw;
        }
        _sessions.Add(instance, new Session(
            instance,
            projection.Container,
            projection.RequiredOwner,
            projection.Destinations,
            projection.Items,
            projection.GridWidth,
            projection.GridHeight,
            model,
            sequence: 1));
        return new InventoryUiOpenResult(true, instance, "Inventory opened.");
    }

    private void HandleAction(UiActionRequest request)
    {
        EnsureOperational();
        if (!_sessions.TryGetValue(request.Instance, out Session? session))
            return;
        // Remote decoders can prove only the action envelope. Definition,
        // source, row, input, and enabled-state validity belong to this
        // retained application model and must reject before authority begins.
        if (!session.Model.TryValidateAction(request))
            return;
        if (request.Action == InventoryUi.Close)
        {
            _ = Close(request.Instance);
            return;
        }
        if (request.Action == InventoryUi.Select)
        {
            HandleSelection(session, request.Row);
            return;
        }

        InventoryMutationResult result = new(
            InventoryMutationStatus.InvariantViolation,
            Entity.Null,
            Entity.Null,
            "The inventory action did not complete.");
        UiListPatch[] listPatches = Array.Empty<UiListPatch>();
        bool staleWorld = false;
        bool authorizationLost = false;
        bool ran = _runtime.TryUseActiveGeneration<IInventoryGenerationStateProvider, bool>(
                (world, _, provider) =>
                {
                    if (world.PersistentWorldId != session.Container.World)
                    {
                        staleWorld = true;
                        result = new InventoryMutationResult(
                            InventoryMutationStatus.RuntimeWorldMismatch,
                            Entity.Null,
                            Entity.Null,
                            "The inventory view belongs to a different persistent world.");
                        return true;
                    }
                    InventoryGenerationState state = provider.Inventory;
                    if (!session.SelectedItem.IsValid ||
                        !TryResolve(world, session.SelectedItem, out Entity item))
                    {
                        result = new InventoryMutationResult(
                            InventoryMutationStatus.MissingItem,
                            Entity.Null,
                            Entity.Null,
                            "The selected item is stale or missing.");
                        return true;
                    }
                    if (!TryResolve(world, session.Container, out Entity container) ||
                        !state.Index.ContainsContainer(container))
                    {
                        result = new InventoryMutationResult(
                            InventoryMutationStatus.MissingContainer,
                            item,
                            Entity.Null,
                            "The inventory container is stale or missing.");
                        return true;
                    }
                    Entity owner = Entity.Null;
                    if (session.RequiredOwner.IsValid)
                    {
                        if (!TryResolve(world, session.RequiredOwner, out owner) ||
                            !state.TryGetContainerOwner(world, container, out Entity currentOwner) ||
                            currentOwner != owner)
                        {
                            authorizationLost = true;
                            result = new InventoryMutationResult(
                                InventoryMutationStatus.UnauthorizedContainer,
                                item,
                                Entity.Null,
                                "The inventory container is no longer owned by the authorized actor.");
                            return true;
                        }
                    }

                    if (request.Action == InventoryUi.Split)
                    {
                        result = state.Split(world, item, container, container, quantity: 1);
                        if (result.Accepted)
                        {
                            if (!state.TryProjectItem(world, item, out InventoryItemView source) ||
                                !state.TryProjectItem(
                                    world,
                                    result.CreatedItem,
                                    out InventoryItemView created))
                            {
                                throw new InvalidOperationException(
                                    "An accepted inventory split produced an unprojectable result.");
                            }
                            listPatches =
                            [
                                UiListPatch.Update(InventoryUi.Items, ToRow(source)),
                                UiListPatch.Add(InventoryUi.Items, ToRow(created)),
                            ];
                        }
                    }
                    else if (request.Action == InventoryUi.MoveTo)
                    {
                        if (!session.TryGetDestination(request.Row, out PersistentEntityKey destinationKey) ||
                            !TryResolve(world, destinationKey, out Entity destination) ||
                            !state.Index.ContainsContainer(destination))
                        {
                            result = new InventoryMutationResult(
                                InventoryMutationStatus.MissingContainer,
                                item,
                                Entity.Null,
                                "The transfer destination is stale or missing.");
                            return true;
                        }
                        if (session.RequiredOwner.IsValid &&
                            (!state.TryGetContainerOwner(world, destination, out Entity destinationOwner) ||
                             destinationOwner != owner))
                        {
                            authorizationLost = true;
                            result = new InventoryMutationResult(
                                InventoryMutationStatus.UnauthorizedContainer,
                                item,
                                Entity.Null,
                                "The transfer destination is no longer owned by the authorized actor.");
                            return true;
                        }
                        result = state.Move(world, item, container, destination);
                        if (result.Accepted)
                        {
                            listPatches =
                            [UiListPatch.Remove(
                                InventoryUi.Items,
                                new UiRowId(session.SelectedItem.Entity.Value))];
                        }
                    }
                    else if (request.Action == InventoryUi.Equip)
                    {
                        if (!owner.IsValid &&
                            !state.TryGetContainerOwner(world, container, out owner))
                        {
                            result = new InventoryMutationResult(
                                InventoryMutationStatus.MissingEquipOwner,
                                item,
                                Entity.Null,
                                "This container has no equipment owner.");
                            return true;
                        }
                        result = state.Equip(world, item, container, owner, InventoryEquipSlot.MainHand);
                        if (result.Accepted)
                        {
                            if (session.RequiredOwner.IsValid)
                            {
                                if (!state.TryProjectItem(world, item, out InventoryItemView equipped))
                                    throw new InvalidOperationException(
                                        "An accepted inventory equip produced an unprojectable result.");
                                listPatches =
                                [UiListPatch.Update(InventoryUi.Items, ToRow(equipped))];
                            }
                            else
                            {
                                listPatches =
                                [UiListPatch.Remove(
                                    InventoryUi.Items,
                                    new UiRowId(session.SelectedItem.Entity.Value))];
                            }
                        }
                    }
                    else if (request.Action == InventoryUi.Unequip)
                    {
                        if (!session.RequiredOwner.IsValid || !owner.IsValid)
                        {
                            result = new InventoryMutationResult(
                                InventoryMutationStatus.UnauthorizedContainer,
                                item,
                                Entity.Null,
                                "Unequip requires an owner-authorized inventory view.");
                            return true;
                        }
                        if (!state.TryReadItem(world, item, out InventoryItem equipped) ||
                            equipped.EquipSlot == InventoryEquipSlot.None)
                        {
                            result = new InventoryMutationResult(
                                InventoryMutationStatus.WrongSource,
                                item,
                                Entity.Null,
                                "The selected item is not equipped.");
                            return true;
                        }
                        result = state.Unequip(
                            world, item, owner, equipped.EquipSlot, container);
                        if (result.Accepted)
                        {
                            if (!state.TryProjectItem(world, item, out InventoryItemView contained))
                                throw new InvalidOperationException(
                                    "An accepted inventory unequip produced an unprojectable result.");
                            listPatches =
                            [UiListPatch.Update(InventoryUi.Items, ToRow(contained))];
                        }
                    }
                    else if (request.Action == InventoryUi.Drop)
                    {
                        if (!owner.IsValid || provider is not IInventoryWorldItemActions worldItems)
                        {
                            result = new InventoryMutationResult(
                                InventoryMutationStatus.UnauthorizedContainer,
                                item,
                                Entity.Null,
                                "Dropping requires an owner-authorized Game inventory view.");
                            return true;
                        }
                        result = worldItems.DropOwnedItem(world, item, owner);
                        if (result.Accepted)
                        {
                            listPatches =
                            [UiListPatch.Remove(
                                InventoryUi.Items,
                                new UiRowId(session.SelectedItem.Entity.Value))];
                        }
                    }
                    else if (request.Action == InventoryUi.PlaceAt)
                    {
                        if (!session.TryGetGridCell(request.Row, out int x, out int y) ||
                            !state.TryCreateGridPlacement(world, item, x, y, out InventoryPlacement placement))
                        {
                            result = new InventoryMutationResult(
                                InventoryMutationStatus.InvalidPlacement,
                                item,
                                Entity.Null,
                                "The requested Grid cell is invalid.");
                            return true;
                        }
                        if (!state.TryReadItem(world, item, out InventoryItem selected))
                        {
                            result = new InventoryMutationResult(
                                InventoryMutationStatus.MissingItem,
                                item,
                                Entity.Null,
                                "The selected item is stale or missing.");
                            return true;
                        }
                        result = selected.EquipSlot == InventoryEquipSlot.None
                            ? state.Reposition(world, item, container, placement)
                            : owner.IsValid
                                ? state.Unequip(
                                    world, item, owner, selected.EquipSlot, container, placement)
                                : new InventoryMutationResult(
                                    InventoryMutationStatus.UnauthorizedContainer,
                                    item,
                                    Entity.Null,
                                    "Placing an equipped item requires an owner-authorized view.");
                        if (result.Accepted)
                        {
                            if (!state.TryProjectItem(world, item, out InventoryItemView placed))
                                throw new InvalidOperationException(
                                    "An accepted Grid placement produced an unprojectable result.");
                            listPatches = [UiListPatch.Update(InventoryUi.Items, ToRow(placed))];
                        }
                    }
                    else
                    {
                        result = new InventoryMutationResult(
                            InventoryMutationStatus.InvariantViolation,
                            item,
                            Entity.Null,
                            "The inventory action is unknown.");
                    }
                    return true;
                },
                out bool completed);
        if (!ran)
        {
            result = new InventoryMutationResult(
                InventoryMutationStatus.RuntimeWorldMismatch,
                Entity.Null,
                Entity.Null,
                "The inventory generation is no longer active.");
        }
        else if (!completed)
        {
            result = new InventoryMutationResult(
                InventoryMutationStatus.InvariantViolation,
                Entity.Null,
                Entity.Null,
                "The inventory action did not complete.");
        }

        if (staleWorld || authorizationLost)
        {
            DetachSession(session, new InvalidOperationException(result.Message));
            NotifyObservers(MutationCompleted, result);
            return;
        }
        PublishResult(session, result, listPatches);
        NotifyObservers(MutationCompleted, result);
    }

    private void HandleSelection(Session session, UiRowId row)
    {
        UiListRow selected = default;
        foreach (UiListRow candidate in session.Model.GetRows(InventoryUi.Items))
        {
            if (candidate.Id == row)
            {
                selected = candidate;
                break;
            }
        }
        if (!selected.Id.IsValid)
        {
            session.SelectedItem = default;
            PublishStatus(session, "Rejected: The selected inventory row is stale.",
                DisabledActionPatches());
            return;
        }
        session.SelectedItem = new PersistentEntityKey(
            session.Container.World,
            new PersistentEntityId(row.Value));
        ReadOnlySpan<UiValue> values = selected.Values;
        bool equipped = values.Length >= 3 &&
            string.Equals(values[2].Text, "Main Hand", StringComparison.Ordinal);
        bool splittable = session.GridWidth == 0 && !equipped && values.Length >= 2 &&
            values[1].Kind == UiValueKind.Number && values[1].Number > 1;
        string details = session.TryGetDescription(row, out string description)
            ? description
            : values.Length != 0 ? values[0].Text ?? "Item selected." : "Item selected.";
        PublishStatus(session, "Item selected.",
            SelectedActionPatches(
                splittable,
                canEquip: !equipped,
                canUnequip: session.GridWidth == 0 && equipped && session.RequiredOwner.IsValid,
                canMove: !equipped && session.Destinations.Length != 0,
                canPlace: session.GridWidth != 0 && session.GridHeight != 0,
                canDrop: session.RequiredOwner.IsValid &&
                    session.TryGetDescription(row, out _)),
            details);
    }

    private InventoryProjection CaptureProjection(
        Entity container,
        Entity requiredOwner,
        IReadOnlyList<Entity> transferDestinations)
    {
        bool ran = _runtime.TryUseActiveGeneration<IInventoryGenerationStateProvider, InventoryProjection>(
            (world, _, provider) => CaptureProjectionCore(
                world, provider.Inventory, container, requiredOwner, transferDestinations),
            out InventoryProjection projection);
        return ran
            ? projection
            : InventoryProjection.Rejected("The inventory runtime is not running.");
    }

    private static InventoryProjection CaptureProjectionCore(
        EcsWorld world,
        InventoryGenerationState state,
        Entity container,
        Entity requiredOwner,
        IReadOnlyList<Entity> transferDestinations)
    {
        if (!state.Index.ContainsContainer(container))
            return InventoryProjection.Rejected("The container is missing.");
        if (!world.TryGetPersistentId(container, out PersistentEntityId id))
            return InventoryProjection.Rejected("The container has no durable identity.");
        if (!state.TryGetContainerDefinition(world, container, out InventoryContainerDefinition definition))
            return InventoryProjection.Rejected("The container content is unknown.");
        if (!state.TryGetEffectiveContainerRules(world, container, out InventoryEffectiveRules rules))
            return InventoryProjection.Rejected("The container rules are invalid.");

        PersistentEntityKey ownerKey = default;
        var items = state.Snapshot(world, container).ToList();
        if (requiredOwner.IsValid)
        {
            if (!world.IsAlive(requiredOwner) ||
                !state.TryGetContainerOwner(world, container, out Entity actualOwner) ||
                actualOwner != requiredOwner ||
                !world.TryGetPersistentId(requiredOwner, out PersistentEntityId ownerId))
            {
                return InventoryProjection.Rejected(
                    "The container is not owned by the authorized actor.");
            }
            ownerKey = new PersistentEntityKey(world.PersistentWorldId, ownerId);
            if (state.Index.TryGetEquipped(
                    requiredOwner, InventoryEquipSlot.MainHand, out Entity equipped))
            {
                if (!state.TryProjectItem(world, equipped, out InventoryItemView equippedView))
                    throw new InvalidOperationException(
                        "The inventory index contains an unprojectable equipped item.");
                items.Add(equippedView);
            }
        }
        else if (transferDestinations.Count != 0)
        {
            return InventoryProjection.Rejected(
                "Transfer destinations require an owner-authorized inventory view.");
        }

        var destinations = new InventoryDestination[transferDestinations.Count];
        var seen = new HashSet<PersistentEntityId>();
        for (int index = 0; index < transferDestinations.Count; index++)
        {
            Entity destination = transferDestinations[index];
            if (destination == container || !state.Index.ContainsContainer(destination) ||
                !state.TryGetContainerOwner(world, destination, out Entity destinationOwner) ||
                destinationOwner != requiredOwner ||
                !world.TryGetPersistentId(destination, out PersistentEntityId destinationId) ||
                !seen.Add(destinationId) ||
                !state.TryGetContainerDefinition(
                    world, destination, out InventoryContainerDefinition destinationDefinition))
            {
                return InventoryProjection.Rejected(
                    "A transfer destination is invalid, duplicated, or not owned by the authorized actor.");
            }
            destinations[index] = new InventoryDestination(
                new PersistentEntityKey(world.PersistentWorldId, destinationId),
                destinationDefinition.DisplayName);
        }
        items.Sort(static (left, right) => left.PersistentId.CompareTo(right.PersistentId));
        return new InventoryProjection(
            true,
            new PersistentEntityKey(world.PersistentWorldId, id),
            ownerKey,
            definition.DisplayName,
            items.ToArray(),
            destinations,
            rules.PlacementMode == InventoryPlacementMode.Grid ? rules.GridWidth : (ushort)0,
            rules.PlacementMode == InventoryPlacementMode.Grid ? rules.GridHeight : (ushort)0,
            "Inventory opened.");
    }

    private void PublishResult(Session session, InventoryMutationResult result, UiListPatch[] listPatches)
    {
        string status = result.Accepted ? result.Message : "Rejected: " + result.Message;
        UiActionStatePatch[] actions = result.Accepted ? DisabledActionPatches() : [];
        ulong sequence = checked(session.Sequence + 1);
        var delta = new UiModelDelta(
            InventoryUi.Definition.Handle,
            sequence,
            [new UiScalarSlotPatch(InventoryUi.Status, UiValue.FromText(status))],
            listPatches,
            actions);
        try
        {
            _ui.UpdateView(new UiUpdateView(session.Instance, delta));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DetachSession(session, exception);
            return;
        }
        session.Sequence = sequence;
        session.Model.SetText(InventoryUi.Status, status);
        ApplyListPatches(session.Model, listPatches);
        if (result.Accepted)
        {
            session.SelectedItem = default;
            ApplyActionPatches(session.Model, DisabledActionPatches());
        }
    }

    private void PublishStatus(
        Session session,
        string status,
        UiActionStatePatch[] actions,
        string? details = null)
    {
        ulong sequence = checked(session.Sequence + 1);
        UiScalarSlotPatch[] scalars = details is null
            ? [new UiScalarSlotPatch(InventoryUi.Status, UiValue.FromText(status))]
            :
            [
                new UiScalarSlotPatch(InventoryUi.Status, UiValue.FromText(status)),
                new UiScalarSlotPatch(InventoryUi.Details, UiValue.FromText(details)),
            ];
        var delta = new UiModelDelta(
            InventoryUi.Definition.Handle,
            sequence,
            scalars,
            actions: actions);
        try
        {
            _ui.UpdateView(new UiUpdateView(session.Instance, delta));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DetachSession(session, exception);
            return;
        }
        session.Sequence = sequence;
        session.Model.SetText(InventoryUi.Status, status);
        if (details is not null)
            session.Model.SetText(InventoryUi.Details, details);
        ApplyActionPatches(session.Model, actions);
    }

    private static void ApplyListPatches(UiModel model, ReadOnlySpan<UiListPatch> patches)
    {
        if (patches.Length == 0)
            return;
        var rows = model.GetRows(InventoryUi.Items).ToArray().ToList();
        foreach (UiListPatch patch in patches)
        {
            int index = rows.FindIndex(row => row.Id == patch.RowId);
            switch (patch.Kind)
            {
                case UiListDeltaKind.Add when index < 0:
                    rows.Add(patch.Row);
                    break;
                case UiListDeltaKind.Update when index >= 0:
                    rows[index] = patch.Row;
                    break;
                case UiListDeltaKind.Remove when index >= 0:
                    rows.RemoveAt(index);
                    break;
                default:
                    throw new InvalidOperationException("The inventory row delta disagrees with its retained model.");
            }
        }
        rows.Sort(static (left, right) => left.Id.Value.CompareTo(right.Id.Value));
        model.ReplaceRows(InventoryUi.Items, rows.ToArray());
    }

    private static UiModel CreateModel(
        IReadOnlyList<InventoryItemView> items,
        IReadOnlyList<InventoryDestination> destinations,
        string title,
        string status,
        ushort gridWidth,
        ushort gridHeight)
    {
        var model = new UiModel(InventoryUi.Definition);
        model.SetText(InventoryUi.Title, title);
        model.ReplaceRows(InventoryUi.Items, ToRows(items));
        model.SetText(InventoryUi.Status, status);
        model.SetText(InventoryUi.Details, "Select an item to inspect its authoritative properties.");
        model.SetText(InventoryUi.TransferLabel,
            destinations.Count == 0 ? "No transfer destinations" : "Move selected item to:");
        model.ReplaceRows(InventoryUi.TransferDestinations, ToRows(destinations));
        model.SetText(InventoryUi.GridLabel,
            gridWidth == 0 ? "No Grid placement" : $"Place selected item in {gridWidth}×{gridHeight} Grid:");
        model.ReplaceRows(InventoryUi.GridCells, ToGridRows(gridWidth, gridHeight));
        model.SetText(InventoryUi.SplitLabel, "Split");
        model.SetText(InventoryUi.EquipLabel, "Equip");
        model.SetText(InventoryUi.UnequipLabel, "Unequip");
        model.SetText(InventoryUi.DropLabel, "Drop");
        model.SetText(InventoryUi.CloseLabel, "Close");
        ApplyActionPatches(model, DisabledActionPatches());
        return model;
    }

    private static UiListRow[] ToRows(IReadOnlyList<InventoryItemView> items)
    {
        var rows = new UiListRow[items.Count];
        for (int index = 0; index < rows.Length; index++)
            rows[index] = ToRow(items[index]);
        return rows;
    }

    private static UiListRow ToRow(in InventoryItemView item) => new(
        new UiRowId(item.PersistentId.Value),
        [
            UiValue.FromText(item.DisplayName),
            UiValue.FromNumber(item.Quantity),
            UiValue.FromText(item.EquipSlot == InventoryEquipSlot.None ? "Container" : "Main Hand"),
            UiValue.FromText(item.Placement.Mode == InventoryPlacementMode.Grid
                ? $"Grid ({item.Placement.X},{item.Placement.Y}) {item.Placement.Width}×{item.Placement.Height}"
                : "Unplaced"),
        ]);

    private static UiListRow[] ToGridRows(ushort width, ushort height)
    {
        if (width == 0 || height == 0)
            return [];
        var rows = new UiListRow[checked(width * height)];
        int index = 0;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            rows[index++] = new UiListRow(
                new UiRowId(checked((ulong)(y * width + x + 1))),
                [UiValue.FromText($"({x},{y})")]);
        }
        return rows;
    }

    private static UiListRow[] ToRows(IReadOnlyList<InventoryDestination> destinations)
    {
        var rows = new UiListRow[destinations.Count];
        for (int index = 0; index < rows.Length; index++)
        {
            InventoryDestination destination = destinations[index];
            rows[index] = new UiListRow(
                new UiRowId(destination.Key.Entity.Value),
                [UiValue.FromText(destination.DisplayName)]);
        }
        return rows;
    }

    private static bool TryResolve(EcsWorld world, in PersistentEntityKey key, out Entity entity)
    {
        if (key.World == world.PersistentWorldId)
            return world.TryResolvePersistentEntity(key.Entity, out entity);
        entity = Entity.Null;
        return false;
    }

    private static UiActionStatePatch[] SelectedActionPatches(
        bool canSplit,
        bool canEquip,
        bool canUnequip,
        bool canMove,
        bool canPlace,
        bool canDrop) =>
        [
            ActionPatch(InventoryUi.Split, canSplit, "Select a splittable contained stack."),
            ActionPatch(InventoryUi.Equip, canEquip, "Select a contained equippable item."),
            ActionPatch(InventoryUi.Unequip, canUnequip, "Select an equipped item."),
            ActionPatch(InventoryUi.MoveTo, canMove, "Select a contained item and destination."),
            ActionPatch(InventoryUi.PlaceAt, canPlace, "Open a Grid container and select an item."),
            ActionPatch(InventoryUi.Drop, canDrop, "Open an owner-authorized inventory."),
        ];

    private static UiActionStatePatch[] DisabledActionPatches()
    {
        var disabled = new UiActionState(false, "Select an inventory row first.");
        return
        [
            new UiActionStatePatch(InventoryUi.Split, disabled),
            new UiActionStatePatch(InventoryUi.Equip, disabled),
            new UiActionStatePatch(InventoryUi.Unequip, disabled),
            new UiActionStatePatch(InventoryUi.MoveTo, disabled),
            new UiActionStatePatch(InventoryUi.PlaceAt, disabled),
            new UiActionStatePatch(InventoryUi.Drop, disabled),
        ];
    }

    private static UiActionStatePatch ActionPatch(
        UiActionId action,
        bool enabled,
        string unavailableReason) =>
        new(action, enabled ? UiActionState.Default : new UiActionState(false, unavailableReason));

    private static void ApplyActionPatches(
        UiModel model,
        ReadOnlySpan<UiActionStatePatch> patches)
    {
        foreach (UiActionStatePatch patch in patches)
            model.SetActionState(patch.Action, patch.State);
    }

    private void HandleGenerationPublished(ulong generationId)
    {
        if (_disposed)
            return;
        foreach (Session session in _sessions.Values.ToArray())
        {
            InventoryProjection projection = CaptureReplacementProjection(session);
            if (!projection.Succeeded)
            {
                DetachSession(session, new InvalidOperationException(
                    $"Inventory view could not rebind to generation {generationId}: {projection.Message}"));
                continue;
            }
            PublishCompleteProjection(session, projection, "Inventory resynchronized.");
        }
    }

    private bool PublishCompleteProjection(
        Session session,
        in InventoryProjection projection,
        string status)
    {
        UiListRow[] itemRows = ToRows(projection.Items);
        UiListRow[] destinationRows = ToRows(projection.Destinations);
        UiActionStatePatch[] actions = DisabledActionPatches();
        ulong sequence = checked(session.Sequence + 1);
        string transferLabel = destinationRows.Length == 0
            ? "No transfer destinations"
            : "Move selected item to:";
        string gridLabel = projection.GridWidth == 0
            ? "No Grid placement"
            : $"Place selected item in {projection.GridWidth}×{projection.GridHeight} Grid:";
        var delta = new UiModelDelta(
            InventoryUi.Definition.Handle,
            sequence,
            [
                new UiScalarSlotPatch(InventoryUi.Title, UiValue.FromText(projection.Title)),
                new UiScalarSlotPatch(InventoryUi.Status, UiValue.FromText(status)),
                new UiScalarSlotPatch(
                    InventoryUi.Details,
                    UiValue.FromText("Select an item to inspect its authoritative properties.")),
                new UiScalarSlotPatch(InventoryUi.TransferLabel, UiValue.FromText(transferLabel)),
                new UiScalarSlotPatch(InventoryUi.GridLabel, UiValue.FromText(gridLabel)),
            ],
            [
                UiListPatch.Replace(InventoryUi.Items, itemRows),
                UiListPatch.Replace(InventoryUi.TransferDestinations, destinationRows),
                UiListPatch.Replace(
                    InventoryUi.GridCells,
                    ToGridRows(projection.GridWidth, projection.GridHeight)),
            ],
            actions);
        try { _ui.UpdateView(new UiUpdateView(session.Instance, delta)); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DetachSession(session, exception);
            return false;
        }
        session.Sequence = sequence;
        session.SelectedItem = default;
        session.Model.SetText(InventoryUi.Title, projection.Title);
        session.Model.SetText(InventoryUi.Status, status);
        session.Model.SetText(
            InventoryUi.Details,
            "Select an item to inspect its authoritative properties.");
        session.Model.SetText(InventoryUi.TransferLabel, transferLabel);
        session.ReplaceDescriptions(projection.Items);
        session.GridWidth = projection.GridWidth;
        session.GridHeight = projection.GridHeight;
        session.Model.SetText(InventoryUi.GridLabel, gridLabel);
        session.Model.ReplaceRows(InventoryUi.Items, itemRows);
        session.Model.ReplaceRows(InventoryUi.TransferDestinations, destinationRows);
        session.Model.ReplaceRows(
            InventoryUi.GridCells,
            ToGridRows(projection.GridWidth, projection.GridHeight));
        ApplyActionPatches(session.Model, actions);
        return true;
    }

    private InventoryProjection CaptureReplacementProjection(Session session)
    {
        bool ran = _runtime.TryUseActiveGeneration<IInventoryGenerationStateProvider, InventoryProjection>(
            (world, _, provider) =>
            {
                if (world.PersistentWorldId != session.Container.World ||
                    !TryResolve(world, session.Container, out Entity container))
                {
                    return InventoryProjection.Rejected(
                        "The replacement generation does not contain the inventory container.");
                }
                Entity owner = Entity.Null;
                if (session.RequiredOwner.IsValid &&
                    !TryResolve(world, session.RequiredOwner, out owner))
                {
                    return InventoryProjection.Rejected(
                        "The replacement generation does not contain the inventory owner.");
                }
                var destinations = new Entity[session.Destinations.Length];
                for (int index = 0; index < destinations.Length; index++)
                {
                    if (!TryResolve(world, session.Destinations[index].Key, out destinations[index]))
                    {
                        return InventoryProjection.Rejected(
                            "The replacement generation does not contain a transfer destination.");
                    }
                }
                return CaptureProjectionCore(
                    world, provider.Inventory, container, owner, destinations);
            },
            out InventoryProjection projection);
        return ran
            ? projection
            : InventoryProjection.Rejected("The inventory runtime is not running.");
    }

    private void DetachSession(Session session, Exception failure)
    {
        _sessions.Remove(session.Instance);
        TryUnbindDetached(session.Instance);
        TryCloseDetached(session.Instance);
        NotifyObservers(PresentationFaulted, failure);
    }

    private void TryUnbindDetached(UiViewInstanceId instance)
    {
        if (!instance.IsValid)
            return;
        try { _ui.UnbindActions(instance); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            NotifyObservers(PresentationFaulted, exception);
        }
    }

    private void TryCloseDetached(UiViewInstanceId instance)
    {
        if (!instance.IsValid)
            return;
        try
        {
            _ = _ui.CloseView(new UiCloseView(instance));
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            NotifyObservers(PresentationFaulted, exception);
        }
    }

    private void NotifyObservers<T>(Action<T>? observers, T value)
    {
        if (observers is null)
            return;
        foreach (Action<T> observer in observers.GetInvocationList().Cast<Action<T>>())
        {
            try { observer(value); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                NotifyObserverFault(exception);
            }
        }
    }

    private void NotifyObserverFault(Exception failure)
    {
        Action<Exception>? observers = ObserverFaulted;
        if (observers is null)
            return;
        foreach (Action<Exception> observer in observers.GetInvocationList().Cast<Action<Exception>>())
        {
            try { observer(failure); }
            catch (Exception exception) when (exception is not OutOfMemoryException) { }
        }
    }

    private void EnsureOperational()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(InventoryUiController));
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Inventory UI is bound to its creating owner thread.");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Inventory UI is bound to its creating owner thread.");
        _disposed = true;
        _runtime.GenerationPublished -= HandleGenerationPublished;
        Exception? firstFailure = null;
        foreach (UiViewInstanceId instance in _sessions.Keys.ToArray())
        {
            try
            {
                try { _ui.UnbindActions(instance); }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    firstFailure ??= exception;
                }
                if (!_ui.CloseView(new UiCloseView(instance)))
                    firstFailure ??= new InvalidOperationException($"The inventory UI port did not close view {instance.Value}.");
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                firstFailure ??= exception;
            }
        }
        _sessions.Clear();
        MutationCompleted = null;
        PresentationFaulted = null;
        ObserverFaulted = null;
        if (firstFailure is not null)
            throw new InvalidOperationException("Inventory UI disposal could not close every view.", firstFailure);
    }

    private sealed class Session
    {
        internal Session(
            UiViewInstanceId instance,
            PersistentEntityKey container,
            PersistentEntityKey requiredOwner,
            InventoryDestination[] destinations,
            InventoryItemView[] items,
            ushort gridWidth,
            ushort gridHeight,
            UiModel model,
            ulong sequence)
        {
            Instance = instance;
            Container = container;
            RequiredOwner = requiredOwner;
            Destinations = destinations;
            ReplaceDescriptions(items);
            GridWidth = gridWidth;
            GridHeight = gridHeight;
            Model = model;
            Sequence = sequence;
        }

        internal UiViewInstanceId Instance { get; }
        internal PersistentEntityKey Container { get; }
        internal PersistentEntityKey RequiredOwner { get; }
        internal InventoryDestination[] Destinations { get; }
        internal ushort GridWidth { get; set; }
        internal ushort GridHeight { get; set; }
        internal PersistentEntityKey SelectedItem { get; set; }
        internal UiModel Model { get; }
        internal ulong Sequence { get; set; }

        private readonly Dictionary<ulong, string> _descriptions = [];

        internal void ReplaceDescriptions(ReadOnlySpan<InventoryItemView> items)
        {
            _descriptions.Clear();
            foreach (ref readonly InventoryItemView item in items)
                if (!string.IsNullOrWhiteSpace(item.InstanceDescription))
                    _descriptions.Add(item.PersistentId.Value, item.InstanceDescription);
        }

        internal bool TryGetDescription(UiRowId row, out string description) =>
            _descriptions.TryGetValue(row.Value, out description!);

        internal bool TryGetDestination(UiRowId row, out PersistentEntityKey destination)
        {
            foreach (InventoryDestination candidate in Destinations)
            {
                if (candidate.Key.Entity.Value == row.Value)
                {
                    destination = candidate.Key;
                    return true;
                }
            }
            destination = default;
            return false;
        }

        internal bool TryGetGridCell(UiRowId row, out int x, out int y)
        {
            ulong count = checked((ulong)GridWidth * GridHeight);
            if (!row.IsValid || row.Value == 0 || row.Value > count || GridWidth == 0)
            {
                x = 0;
                y = 0;
                return false;
            }
            ulong index = row.Value - 1;
            x = checked((int)(index % GridWidth));
            y = checked((int)(index / GridWidth));
            return true;
        }
    }

    private readonly record struct InventoryProjection(
        bool Succeeded,
        PersistentEntityKey Container,
        PersistentEntityKey RequiredOwner,
        string Title,
        InventoryItemView[] Items,
        InventoryDestination[] Destinations,
        ushort GridWidth,
        ushort GridHeight,
        string Message)
    {
        internal static InventoryProjection Rejected(string message) =>
            new(false, default, default, "", [], [], 0, 0, message);
    }

    private readonly record struct InventoryDestination(
        PersistentEntityKey Key,
        string DisplayName);
}
