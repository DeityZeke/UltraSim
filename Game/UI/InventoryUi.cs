using UltraSim.Client.Core;
using UltraSim.ECS;
using UltraSim.Game.Inventory;
using UltraSim.Server.Runtime;
using UltraSim.UI;

namespace UltraSim.Game.UI;

public static class InventoryUi
{
    private static readonly Contract _contract = CreateContract();

    public static CompiledUiDefinition Definition => _contract.Definition;
    public static UiSlotId Title => _contract.Title;
    public static UiSlotId Items => _contract.Items;
    public static UiSlotId Status => _contract.Status;
    public static UiSlotId SplitLabel => _contract.SplitLabel;
    public static UiSlotId EquipLabel => _contract.EquipLabel;
    public static UiActionId Select => _contract.Select;
    public static UiActionId Split => _contract.Split;
    public static UiActionId Equip => _contract.Equip;

    private static Contract CreateContract()
    {
        var source = new UiDefinitionBuilder("game.inventory.gump", version: 3);
        UiSlotReference title = source.DeclareSlot("title", UiSlotValueKind.Text);
        UiSlotReference items = source.DeclareSlot("items", UiSlotValueKind.List);
        UiSlotReference status = source.DeclareSlot("status", UiSlotValueKind.Text);
        UiSlotReference splitLabel = source.DeclareSlot("split-label", UiSlotValueKind.Text);
        UiSlotReference equipLabel = source.DeclareSlot("equip-label", UiSlotValueKind.Text);
        UiActionReference select = source.DeclareAction("select", acceptsRowContext: true);
        UiActionReference split = source.DeclareAction("split");
        UiActionReference equip = source.DeclareAction("equip");
        source.AddCanvas(canvas => canvas.AddVertical(
            "content",
            content =>
            {
                content.AddText("title", title);
                content.AddList("items", items, select);
                content.AddHorizontal("actions", actions =>
                {
                    actions.AddButton("split", split, splitLabel);
                    actions.AddButton("equip", equip, equipLabel);
                });
                content.AddText("status", status);
            },
            UiChildLayout.Canvas(new UiCanvasPlacement(16, 16, 768, 568))));
        CompiledUiDefinition definition = UiDefinitionCompiler.Compile(source);
        return new Contract(
            definition,
            definition.Bindings.RequireSlot("title"),
            definition.Bindings.RequireSlot("items"),
            definition.Bindings.RequireSlot("status"),
            definition.Bindings.RequireSlot("split-label"),
            definition.Bindings.RequireSlot("equip-label"),
            definition.Bindings.RequireAction("select"),
            definition.Bindings.RequireAction("split"),
            definition.Bindings.RequireAction("equip"));
    }

    private sealed record Contract(
        CompiledUiDefinition Definition,
        UiSlotId Title,
        UiSlotId Items,
        UiSlotId Status,
        UiSlotId SplitLabel,
        UiSlotId EquipLabel,
        UiActionId Select,
        UiActionId Split,
        UiActionId Equip);
}

public interface IInventoryUiPort
{
    event Action<UiActionRequest>? ActionSubmitted;
    UiViewInstanceId OpenView(UiOpenView operation);
    UiViewInstanceId OpenHotView(UiHotOpenView operation);
    void UpdateView(UiUpdateView operation);
    bool CloseView(UiCloseView operation);
}

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
    private readonly IInventoryUiPort _ui;
    private readonly UiDefinitionCacheKey _cacheKey;
    private readonly Dictionary<UiViewInstanceId, Session> _sessions = [];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _disposed;

    public InventoryUiController(
        ServerGameFacet runtime,
        IInventoryUiPort ui,
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
        _ui.ActionSubmitted += HandleAction;
    }

    public int OpenViewCount { get { EnsureOperational(); return _sessions.Count; } }
    public event Action<InventoryMutationResult>? MutationCompleted;

    public InventoryUiOpenResult Open(Entity container) => OpenCore(container, hot: false);
    public InventoryUiOpenResult OpenHot(Entity container) => OpenCore(container, hot: true);

    public bool Close(UiViewInstanceId instance)
    {
        EnsureOperational();
        if (!_sessions.ContainsKey(instance))
            return false;
        if (!_ui.CloseView(new UiCloseView(instance)))
            return false;
        return _sessions.Remove(instance);
    }

    private InventoryUiOpenResult OpenCore(Entity container, bool hot)
    {
        EnsureOperational();
        InventoryProjection projection = CaptureProjection(container);
        if (!projection.Succeeded)
            return new InventoryUiOpenResult(false, UiViewInstanceId.Invalid, projection.Message);
        UiModel model = CreateModel(projection.Items, projection.Title, "Ready");
        UiViewInstanceId instance = hot
            ? _ui.OpenHotView(new UiHotOpenView(_cacheKey, model, sequence: 1))
            : _ui.OpenView(new UiOpenView(_cacheKey, InventoryUi.Definition, model));
        if (!instance.IsValid)
            throw new InvalidOperationException("The inventory UI port returned an invalid view ID.");
        if (_sessions.ContainsKey(instance))
            throw new InvalidOperationException("The inventory UI port returned an already-owned view ID.");
        _sessions.Add(instance, new Session(instance, projection.Container, model, sequence: 1));
        return new InventoryUiOpenResult(true, instance, "Inventory opened.");
    }

    private void HandleAction(UiActionRequest request)
    {
        EnsureOperational();
        if (!_sessions.TryGetValue(request.Instance, out Session? session))
            return;
        session.Model.ValidateAction(request);
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
        try
        {
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

                    if (request.Action == InventoryUi.Split)
                    {
                        result = state.Split(world, item, container, container, quantity: 1);
                        if (result.Accepted &&
                            state.TryProjectItem(world, item, out InventoryItemView source) &&
                            state.TryProjectItem(world, result.CreatedItem, out InventoryItemView created))
                        {
                            listPatches =
                            [
                                UiListPatch.Update(InventoryUi.Items, ToRow(source)),
                                UiListPatch.Add(InventoryUi.Items, ToRow(created)),
                            ];
                        }
                    }
                    else if (request.Action == InventoryUi.Equip)
                    {
                        if (!state.TryGetContainerOwner(world, container, out Entity owner))
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
                            listPatches =
                            [UiListPatch.Remove(InventoryUi.Items, new UiRowId(session.SelectedItem.Entity.Value))];
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
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            result = new InventoryMutationResult(
                InventoryMutationStatus.InvariantViolation,
                Entity.Null,
                Entity.Null,
                "The inventory action was rejected: " + exception.Message);
        }

        if (staleWorld)
        {
            _ = Close(request.Instance);
            MutationCompleted?.Invoke(result);
            return;
        }
        PublishResult(session, result, listPatches);
        MutationCompleted?.Invoke(result);
    }

    private void HandleSelection(Session session, UiRowId row)
    {
        bool found = false;
        foreach (UiListRow candidate in session.Model.GetRows(InventoryUi.Items))
        {
            if (candidate.Id == row)
            {
                found = true;
                break;
            }
        }
        if (!found)
        {
            session.SelectedItem = default;
            PublishStatus(session, "Rejected: The selected inventory row is stale.", enableActions: false);
            return;
        }
        session.SelectedItem = new PersistentEntityKey(
            session.Container.World,
            new PersistentEntityId(row.Value));
        PublishStatus(session, "Item selected.", enableActions: true);
    }

    private InventoryProjection CaptureProjection(Entity container)
    {
        bool ran = _runtime.TryUseActiveGeneration<IInventoryGenerationStateProvider, InventoryProjection>(
            (world, _, provider) =>
            {
                InventoryGenerationState state = provider.Inventory;
                if (!state.Index.ContainsContainer(container))
                    return new InventoryProjection(false, default, "", [], "The container is missing.");
                if (!world.TryGetPersistentId(container, out PersistentEntityId id))
                    return new InventoryProjection(false, default, "", [], "The container has no durable identity.");
                if (!state.TryGetContainerDefinition(world, container, out InventoryContainerDefinition definition))
                    return new InventoryProjection(false, default, "", [], "The container content is unknown.");
                return new InventoryProjection(
                    true,
                    new PersistentEntityKey(world.PersistentWorldId, id),
                    definition.DisplayName,
                    state.Snapshot(world, container),
                    "Inventory opened.");
            },
            out InventoryProjection projection);
        return ran
            ? projection
            : new InventoryProjection(false, default, "", [], "The inventory runtime is not running.");
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
        _ui.UpdateView(new UiUpdateView(session.Instance, delta));
        session.Sequence = sequence;
        session.Model.SetText(InventoryUi.Status, status);
        ApplyListPatches(session.Model, listPatches);
        if (result.Accepted)
        {
            session.SelectedItem = default;
            SetActionState(session.Model, enabled: false);
        }
    }

    private void PublishStatus(Session session, string status, bool enableActions)
    {
        ulong sequence = checked(session.Sequence + 1);
        UiActionStatePatch[] actions = enableActions ? EnabledActionPatches() : DisabledActionPatches();
        var delta = new UiModelDelta(
            InventoryUi.Definition.Handle,
            sequence,
            [new UiScalarSlotPatch(InventoryUi.Status, UiValue.FromText(status))],
            actions: actions);
        _ui.UpdateView(new UiUpdateView(session.Instance, delta));
        session.Sequence = sequence;
        session.Model.SetText(InventoryUi.Status, status);
        SetActionState(session.Model, enableActions);
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

    private static UiModel CreateModel(IReadOnlyList<InventoryItemView> items, string title, string status)
    {
        var model = new UiModel(InventoryUi.Definition);
        model.SetText(InventoryUi.Title, title);
        model.ReplaceRows(InventoryUi.Items, ToRows(items));
        model.SetText(InventoryUi.Status, status);
        model.SetText(InventoryUi.SplitLabel, "Split");
        model.SetText(InventoryUi.EquipLabel, "Equip");
        SetActionState(model, enabled: false);
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
        ]);

    private static bool TryResolve(EcsWorld world, in PersistentEntityKey key, out Entity entity)
    {
        if (key.World == world.PersistentWorldId)
            return world.TryResolvePersistentEntity(key.Entity, out entity);
        entity = Entity.Null;
        return false;
    }

    private static UiActionStatePatch[] EnabledActionPatches() =>
        [
            new UiActionStatePatch(InventoryUi.Split, UiActionState.Default),
            new UiActionStatePatch(InventoryUi.Equip, UiActionState.Default),
        ];

    private static UiActionStatePatch[] DisabledActionPatches()
    {
        var disabled = new UiActionState(false, "Select an inventory row first.");
        return
        [
            new UiActionStatePatch(InventoryUi.Split, disabled),
            new UiActionStatePatch(InventoryUi.Equip, disabled),
        ];
    }

    private static void SetActionState(UiModel model, bool enabled)
    {
        UiActionState state = enabled
            ? UiActionState.Default
            : new UiActionState(false, "Select an inventory row first.");
        model.SetActionState(InventoryUi.Split, state);
        model.SetActionState(InventoryUi.Equip, state);
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
        _ui.ActionSubmitted -= HandleAction;
        Exception? firstFailure = null;
        foreach (UiViewInstanceId instance in _sessions.Keys.ToArray())
        {
            try
            {
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
        if (firstFailure is not null)
            throw new InvalidOperationException("Inventory UI disposal could not close every view.", firstFailure);
    }

    private sealed class Session
    {
        internal Session(UiViewInstanceId instance, PersistentEntityKey container, UiModel model, ulong sequence)
        {
            Instance = instance;
            Container = container;
            Model = model;
            Sequence = sequence;
        }

        internal UiViewInstanceId Instance { get; }
        internal PersistentEntityKey Container { get; }
        internal PersistentEntityKey SelectedItem { get; set; }
        internal UiModel Model { get; }
        internal ulong Sequence { get; set; }
    }

    private readonly record struct InventoryProjection(
        bool Succeeded,
        PersistentEntityKey Container,
        string Title,
        InventoryItemView[] Items,
        string Message);
}
