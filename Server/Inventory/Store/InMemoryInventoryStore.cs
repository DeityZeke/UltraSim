using UltraSim.Inventory;

namespace UltraSim.Server.Inventory;

public sealed class InMemoryInventoryStore
{
    private sealed class TransactionEntry
    {
        public InventoryTransactionSnapshot Snapshot { get; set; }
        public Dictionary<string, InventoryItemSnapshot> ReservedItems { get; } = new(StringComparer.Ordinal);

        public TransactionEntry(InventoryTransactionSnapshot snapshot)
        {
            Snapshot = snapshot;
        }
    }

    private readonly IInventoryRuntimeRefResolver _refResolver;
    private readonly IInventoryOwnerLifetimePolicy _ownerLifetimePolicy;
    private readonly Dictionary<string, InventoryContainerSnapshot> _containers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, InventoryContainerViewerRegistrySnapshot> _viewerRegistries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TransactionEntry> _transactions = new(StringComparer.Ordinal);

    public InMemoryInventoryStore(
        IInventoryRuntimeRefResolver refResolver,
        IInventoryOwnerLifetimePolicy ownerLifetimePolicy)
    {
        _refResolver = refResolver ?? throw new ArgumentNullException(nameof(refResolver));
        _ownerLifetimePolicy = ownerLifetimePolicy ?? throw new ArgumentNullException(nameof(ownerLifetimePolicy));
    }

    public int ContainerCount => _containers.Count;
    public int TransactionCount => _transactions.Count;

    public int ActiveContainerCount
    {
        get
        {
            int count = 0;
            foreach (InventoryContainerViewerRegistrySnapshot registry in _viewerRegistries.Values)
            {
                if (registry.Viewers.Count > 0)
                {
                    count++;
                }
            }

            return count;
        }
    }

    public bool TryGetContainer(string containerId, out InventoryContainerSnapshot container)
    {
        return _containers.TryGetValue(containerId, out container!);
    }

    public bool TryGetTransaction(string transactionId, out InventoryTransactionSnapshot transaction)
    {
        if (_transactions.TryGetValue(transactionId, out TransactionEntry? entry))
        {
            transaction = entry.Snapshot;
            return true;
        }

        transaction = null!;
        return false;
    }

    public InventoryStoreOperationResult AddContainer(InventoryContainerSnapshot container)
    {
        InventoryValidationResult validation = container.Validate();
        if (!validation.IsValid)
        {
            return InventoryStoreOperationResult.Failure(validation.Error);
        }

        if (_containers.ContainsKey(container.ContainerId))
        {
            return InventoryStoreOperationResult.Failure($"Inventory container '{container.ContainerId}' already exists.");
        }

        _containers.Add(container.ContainerId, container);
        _viewerRegistries.Add(
            container.ContainerId,
            new InventoryContainerViewerRegistrySnapshot(container.ContainerId, Revision: 0, Viewers: []));
        return InventoryStoreOperationResult.Success();
    }

    public InventoryStoreOperationResult OpenView(InventoryContainerViewSnapshot view)
    {
        InventoryValidationResult validation = view.Validate();
        if (!validation.IsValid)
        {
            return InventoryStoreOperationResult.Failure(validation.Error);
        }

        if (!_containers.ContainsKey(view.ContainerId))
        {
            return InventoryStoreOperationResult.Failure($"Inventory container '{view.ContainerId}' does not exist.");
        }

        InventoryContainerViewerRegistrySnapshot registry = _viewerRegistries[view.ContainerId];
        var viewers = new InventoryContainerViewSnapshot[registry.Viewers.Count + 1];
        for (int i = 0; i < registry.Viewers.Count; i++)
        {
            if (string.Equals(registry.Viewers[i].ViewId, view.ViewId, StringComparison.Ordinal))
            {
                return InventoryStoreOperationResult.Failure($"Inventory view '{view.ViewId}' already exists.");
            }

            viewers[i] = registry.Viewers[i];
        }

        viewers[^1] = view;
        var updated = new InventoryContainerViewerRegistrySnapshot(view.ContainerId, registry.Revision + 1, viewers);
        validation = updated.Validate();
        if (!validation.IsValid)
        {
            return InventoryStoreOperationResult.Failure(validation.Error);
        }

        _viewerRegistries[view.ContainerId] = updated;
        return InventoryStoreOperationResult.Success(
            [new InventoryStoreEvent(InventoryStoreEventKind.ViewerOpened, view.ContainerId, ViewId: view.ViewId)]);
    }

    public InventoryStoreOperationResult CloseView(string containerId, string viewId, string? reason = null)
    {
        if (!_viewerRegistries.TryGetValue(containerId, out InventoryContainerViewerRegistrySnapshot? registry))
        {
            return InventoryStoreOperationResult.Failure($"Inventory container '{containerId}' does not exist.");
        }

        int kept = 0;
        for (int i = 0; i < registry.Viewers.Count; i++)
        {
            if (!string.Equals(registry.Viewers[i].ViewId, viewId, StringComparison.Ordinal))
            {
                kept++;
            }
        }

        if (kept == registry.Viewers.Count)
        {
            return InventoryStoreOperationResult.Failure($"Inventory view '{viewId}' does not exist.");
        }

        var viewers = new InventoryContainerViewSnapshot[kept];
        int write = 0;
        for (int i = 0; i < registry.Viewers.Count; i++)
        {
            if (!string.Equals(registry.Viewers[i].ViewId, viewId, StringComparison.Ordinal))
            {
                viewers[write++] = registry.Viewers[i];
            }
        }

        _viewerRegistries[containerId] = new InventoryContainerViewerRegistrySnapshot(containerId, registry.Revision + 1, viewers);
        return InventoryStoreOperationResult.Success(
            [new InventoryStoreEvent(InventoryStoreEventKind.ViewerClosed, containerId, ViewId: viewId, Message: reason)]);
    }

    public InventoryStoreOperationResult DispatchDelta(InventoryContainerDelta delta)
    {
        if (!_containers.TryGetValue(delta.ContainerId, out InventoryContainerSnapshot? container))
        {
            return InventoryStoreOperationResult.Failure($"Inventory container '{delta.ContainerId}' does not exist.");
        }

        InventoryValidationResult validation = delta.Validate(container.PlacementMode);
        if (!validation.IsValid)
        {
            return InventoryStoreOperationResult.Failure(validation.Error);
        }

        InventoryContainerViewerRegistrySnapshot registry = _viewerRegistries[delta.ContainerId];
        var recipients = new List<string>(registry.Viewers.Count);
        for (int i = 0; i < registry.Viewers.Count; i++)
        {
            if (registry.Viewers[i].CanReceive(delta))
            {
                recipients.Add(registry.Viewers[i].ViewId);
            }
        }

        return InventoryStoreOperationResult.Success(
            dispatches: [new InventoryStoreDeltaDispatch(delta, recipients)]);
    }

    public InventoryStoreOperationResult BeginTransaction(InventoryTransactionSnapshot transaction)
    {
        InventoryValidationResult validation = transaction.Validate();
        if (!validation.IsValid)
        {
            return InventoryStoreOperationResult.Failure(validation.Error);
        }

        if (_transactions.ContainsKey(transaction.TransactionId))
        {
            return InventoryStoreOperationResult.Failure($"Inventory transaction '{transaction.TransactionId}' already exists.");
        }

        for (int i = 0; i < transaction.ParticipantContainerIds.Count; i++)
        {
            if (!_containers.ContainsKey(transaction.ParticipantContainerIds[i]))
            {
                return InventoryStoreOperationResult.Failure($"Inventory transaction participant '{transaction.ParticipantContainerIds[i]}' does not exist.");
            }
        }

        _transactions.Add(transaction.TransactionId, new TransactionEntry(transaction));
        return InventoryStoreOperationResult.Success();
    }

    public InventoryStoreOperationResult SuspendWholeItem(
        string transactionId,
        string sourceContainerId,
        string? destinationContainerId,
        string itemInstanceId)
    {
        if (!_transactions.TryGetValue(transactionId, out TransactionEntry? transaction))
        {
            return InventoryStoreOperationResult.Failure($"Inventory transaction '{transactionId}' does not exist.");
        }

        if (!_containers.TryGetValue(sourceContainerId, out InventoryContainerSnapshot? sourceContainer))
        {
            return InventoryStoreOperationResult.Failure($"Inventory source container '{sourceContainerId}' does not exist.");
        }

        if (!TransactionContainsContainer(transaction.Snapshot, sourceContainerId) ||
            (!string.IsNullOrWhiteSpace(destinationContainerId) && !TransactionContainsContainer(transaction.Snapshot, destinationContainerId)))
        {
            return InventoryStoreOperationResult.Failure("Suspended item source/destination must belong to the transaction.");
        }

        int itemIndex = FindItemIndex(sourceContainer, itemInstanceId);
        if (itemIndex < 0)
        {
            return InventoryStoreOperationResult.Failure($"Inventory item '{itemInstanceId}' does not exist in source container.");
        }

        InventoryItemSnapshot item = sourceContainer.Items[itemIndex];
        if (!transaction.ReservedItems.TryAdd(item.ItemInstanceId, item))
        {
            return InventoryStoreOperationResult.Failure($"Inventory item '{item.ItemInstanceId}' is already suspended in transaction.");
        }

        _containers[sourceContainerId] = RemoveItem(sourceContainer, itemIndex);
        var suspendedItems = new InventorySuspendedItemSnapshot[transaction.Snapshot.SuspendedItems.Count + 1];
        for (int i = 0; i < transaction.Snapshot.SuspendedItems.Count; i++)
        {
            suspendedItems[i] = transaction.Snapshot.SuspendedItems[i];
        }

        suspendedItems[^1] = new InventorySuspendedItemSnapshot(
            item.ItemInstanceId,
            sourceContainerId,
            destinationContainerId,
            item.Quantity);
        transaction.Snapshot = transaction.Snapshot with { SuspendedItems = suspendedItems };

        InventoryValidationResult validation = transaction.Snapshot.Validate();
        if (!validation.IsValid)
        {
            return InventoryStoreOperationResult.Failure(validation.Error);
        }

        var delta = new InventoryContainerDelta(
            $"delta:{transactionId}/reserved/{item.ItemInstanceId}",
            sourceContainerId,
            ContainerRevision: transaction.Snapshot.SuspendedItems.Count,
            InventoryContainerDeltaKind.ItemReserved,
            item.ItemInstanceId,
            Item: null,
            PreviousPlacement: null,
            CurrentPlacement: null,
            QuantityDelta: item.Quantity,
            InventoryAccessFlags.Read,
            VisibilityScopeId: null,
            TransactionId: transactionId);
        InventoryStoreOperationResult dispatchResult = DispatchDelta(delta);
        return dispatchResult.Succeeded
            ? InventoryStoreOperationResult.Success(dispatches: dispatchResult.Dispatches)
            : dispatchResult;
    }

    public InventoryStoreOperationResult CancelTransaction(string transactionId, string reason)
    {
        if (!_transactions.TryGetValue(transactionId, out TransactionEntry? transaction))
        {
            return InventoryStoreOperationResult.Failure($"Inventory transaction '{transactionId}' does not exist.");
        }

        var events = new List<InventoryStoreEvent>();
        ReleaseSuspendedItems(transactionId, transaction, events, reason);
        transaction.Snapshot = transaction.Snapshot with { State = InventoryTransactionState.Cancelled, SuspendedItems = [] };
        _transactions.Remove(transactionId);
        events.Add(new InventoryStoreEvent(
            InventoryStoreEventKind.TransactionCancelled,
            transaction.Snapshot.ParticipantContainerIds[0],
            TransactionId: transactionId,
            Message: reason));

        return InventoryStoreOperationResult.Success(events);
    }

    public InventoryStoreOperationResult RefreshOwnerLifetime(string containerId)
    {
        if (!_containers.TryGetValue(containerId, out InventoryContainerSnapshot? container))
        {
            return InventoryStoreOperationResult.Failure($"Inventory container '{containerId}' does not exist.");
        }

        InventoryRuntimeRefResolution ownerResolution = _refResolver.ResolveOwner(container.Owner);
        InventoryOwnerInvalidationDecision decision = _ownerLifetimePolicy.Evaluate(container, ownerResolution);
        if (decision.Action == InventoryOwnerInvalidationAction.KeepOpen)
        {
            return InventoryStoreOperationResult.Success();
        }

        var events = new List<InventoryStoreEvent>
        {
            new(
                InventoryStoreEventKind.OwnerInvalidated,
                containerId,
                Message: decision.Reason)
        };

        CloseAllViewers(containerId, events, decision.Reason);

        if (decision.ShouldReleaseTransactions)
        {
            CancelTransactionsForContainer(containerId, events, decision.Reason ?? "Owner invalidated.");
        }

        if (decision.ShouldDestroyContainer)
        {
            CancelTransactionsForContainer(containerId, events, decision.Reason ?? "Container destroyed.");
            _containers.Remove(containerId);
            _viewerRegistries.Remove(containerId);
            events.Add(new InventoryStoreEvent(
                InventoryStoreEventKind.ContainerDestroyed,
                containerId,
                Message: decision.Reason));
        }

        return InventoryStoreOperationResult.Success(events);
    }

    public InventoryStoreOperationResult DestroyContainer(string containerId, string reason)
    {
        if (!_containers.ContainsKey(containerId))
        {
            return InventoryStoreOperationResult.Failure($"Inventory container '{containerId}' does not exist.");
        }

        var events = new List<InventoryStoreEvent>();
        CloseAllViewers(containerId, events, reason);
        _containers.Remove(containerId);
        _viewerRegistries.Remove(containerId);
        events.Add(new InventoryStoreEvent(
            InventoryStoreEventKind.ContainerDestroyed,
            containerId,
            Message: reason));

        return InventoryStoreOperationResult.Success(events);
    }

    private void CloseAllViewers(string containerId, List<InventoryStoreEvent> events, string? reason)
    {
        if (!_viewerRegistries.TryGetValue(containerId, out InventoryContainerViewerRegistrySnapshot? registry))
        {
            return;
        }

        for (int i = 0; i < registry.Viewers.Count; i++)
        {
            events.Add(new InventoryStoreEvent(
                InventoryStoreEventKind.ViewerClosed,
                containerId,
                ViewId: registry.Viewers[i].ViewId,
                Message: reason));
        }

        _viewerRegistries[containerId] = new InventoryContainerViewerRegistrySnapshot(containerId, registry.Revision + 1, Viewers: []);
    }

    private void CancelTransactionsForContainer(string containerId, List<InventoryStoreEvent> events, string reason)
    {
        var transactionIds = new List<string>();
        foreach (KeyValuePair<string, TransactionEntry> pair in _transactions)
        {
            if (TransactionContainsContainer(pair.Value.Snapshot, containerId))
            {
                transactionIds.Add(pair.Key);
            }
        }

        for (int i = 0; i < transactionIds.Count; i++)
        {
            string transactionId = transactionIds[i];
            TransactionEntry transaction = _transactions[transactionId];
            ReleaseSuspendedItems(transactionId, transaction, events, reason);
            events.Add(new InventoryStoreEvent(
                InventoryStoreEventKind.TransactionCancelled,
                containerId,
                TransactionId: transactionId,
                Message: reason));
            _transactions.Remove(transactionId);
        }
    }

    private void ReleaseSuspendedItems(
        string transactionId,
        TransactionEntry transaction,
        List<InventoryStoreEvent> events,
        string reason)
    {
        for (int i = 0; i < transaction.Snapshot.SuspendedItems.Count; i++)
        {
            InventorySuspendedItemSnapshot suspended = transaction.Snapshot.SuspendedItems[i];
            if (!transaction.ReservedItems.TryGetValue(suspended.ItemInstanceId, out InventoryItemSnapshot? item))
            {
                continue;
            }

            if (_containers.TryGetValue(suspended.SourceContainerId, out InventoryContainerSnapshot? source))
            {
                _containers[suspended.SourceContainerId] = AddItem(source, item);
                events.Add(new InventoryStoreEvent(
                    InventoryStoreEventKind.SuspendedItemReleased,
                    suspended.SourceContainerId,
                    TransactionId: transactionId,
                    ItemInstanceId: suspended.ItemInstanceId,
                    Message: reason));
            }
            else
            {
                events.Add(new InventoryStoreEvent(
                    InventoryStoreEventKind.SuspendedItemReturnUnresolved,
                    suspended.SourceContainerId,
                    TransactionId: transactionId,
                    ItemInstanceId: suspended.ItemInstanceId,
                    Message: reason));
            }
        }

        transaction.ReservedItems.Clear();
        transaction.Snapshot = transaction.Snapshot with { SuspendedItems = [] };
    }

    private static bool TransactionContainsContainer(InventoryTransactionSnapshot transaction, string containerId)
    {
        for (int i = 0; i < transaction.ParticipantContainerIds.Count; i++)
        {
            if (string.Equals(transaction.ParticipantContainerIds[i], containerId, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static int FindItemIndex(InventoryContainerSnapshot container, string itemInstanceId)
    {
        for (int i = 0; i < container.Items.Count; i++)
        {
            if (string.Equals(container.Items[i].ItemInstanceId, itemInstanceId, StringComparison.Ordinal))
            {
                return i;
            }
        }

        return -1;
    }

    private static InventoryContainerSnapshot RemoveItem(InventoryContainerSnapshot container, int itemIndex)
    {
        var items = new InventoryItemSnapshot[container.Items.Count - 1];
        int write = 0;
        for (int i = 0; i < container.Items.Count; i++)
        {
            if (i != itemIndex)
            {
                items[write++] = container.Items[i];
            }
        }

        return container with { Items = items };
    }

    private static InventoryContainerSnapshot AddItem(InventoryContainerSnapshot container, InventoryItemSnapshot item)
    {
        var items = new InventoryItemSnapshot[container.Items.Count + 1];
        for (int i = 0; i < container.Items.Count; i++)
        {
            items[i] = container.Items[i];
        }

        items[^1] = item;
        return container with { Items = items };
    }
}
