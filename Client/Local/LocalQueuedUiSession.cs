using UltraSim.Client.Core;
using UltraSim.UI;

namespace UltraSim.Client.Local;

internal enum LocalUiOperationKind : byte
{
    ColdOpen = 1,
    HotOpen = 2,
    Update = 3,
    Close = 4,
}

internal readonly record struct LocalUiProof(
    ulong OperationId,
    UiViewInstanceId Instance,
    LocalSemanticProofStatus Status);

internal readonly record struct LocalUiAction(UiActionRequest Request);

/// <summary>
/// Expected bounded-channel pressure. Callers may retain their prior read model and retry later;
/// other InvalidOperationException instances still represent semantic/lifetime defects.
/// </summary>
internal sealed class LocalUiTransportBackpressureException(string message)
    : InvalidOperationException(message);

internal sealed class LocalUiOperationRow
{
    internal ulong OperationId;
    internal LocalUiOperationKind Kind;
    internal UiViewInstanceId Instance;
    internal UiOpenView ColdOpen;
    internal UiHotOpenView HotOpen;
    internal UiUpdateView Update;
    internal UiCloseView Close;

    internal void Clear()
    {
        OperationId = 0;
        Kind = default;
        Instance = default;
        ColdOpen = default;
        HotOpen = default;
        Update = default;
        Close = default;
    }
}

/// <summary>
/// Fixed local UI occurrence store shared by the authority port and one Client
/// Core consumer. Operations remain retained until explicit application proof.
/// </summary>
internal sealed class LocalQueuedUiChannel
{
    private readonly object _gate = new();
    private readonly LocalUiOperationRow[] _rows;
    private readonly int[] _free;
    private readonly bool[] _queued;
    private readonly BoundedSpscQueue<int> _ready;
    private readonly BoundedSpscQueue<LocalUiProof> _proofs;
    private readonly BoundedSpscQueue<LocalUiAction> _actions;
    private int _freeCount;
    private ulong _nextOperationId = 1;
    private ulong _applied;
    private ulong _rejected;
    private ulong _coldOpensApplied;
    private ulong _hotOpensApplied;

    internal LocalQueuedUiChannel(LocalPlayableSessionLimits limits)
    {
        _rows = new LocalUiOperationRow[limits.UiOperationCapacity];
        _free = new int[_rows.Length];
        _queued = new bool[_rows.Length];
        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index] = new LocalUiOperationRow();
            _free[index] = _rows.Length - index - 1;
        }
        _freeCount = _free.Length;
        _ready = new BoundedSpscQueue<int>(limits.UiOperationCapacity);
        _proofs = new BoundedSpscQueue<LocalUiProof>(limits.UiProofCapacity);
        _actions = new BoundedSpscQueue<LocalUiAction>(limits.UiActionCapacity);
    }

    internal ulong Applied { get { lock (_gate) return _applied; } }
    internal ulong Rejected { get { lock (_gate) return _rejected; } }
    internal ulong ColdOpensApplied { get { lock (_gate) return _coldOpensApplied; } }
    internal ulong HotOpensApplied { get { lock (_gate) return _hotOpensApplied; } }
    internal int Pending { get { lock (_gate) return _rows.Length - _freeCount; } }
    internal long ReservedBytes => checked(
        (long)_rows.Length * IntPtr.Size * 6L +
        (long)_free.Length * sizeof(int) + _queued.Length +
        (long)_ready.Capacity * sizeof(int));

    internal int Publish(
        LocalUiOperationKind kind,
        UiViewInstanceId instance,
        in UiOpenView cold,
        in UiHotOpenView hot,
        in UiUpdateView update,
        in UiCloseView close)
    {
        lock (_gate)
        {
            if (_freeCount == 0 || _nextOperationId == 0)
                throw new LocalUiTransportBackpressureException(
                    "The bounded local UI operation store is full.");
            int rowIndex = _free[--_freeCount];
            LocalUiOperationRow row = _rows[rowIndex];
            row.OperationId = _nextOperationId;
            row.Kind = kind;
            row.Instance = instance;
            row.ColdOpen = cold;
            row.HotOpen = hot;
            row.Update = update;
            row.Close = close;
            if (!_ready.TryEnqueue(rowIndex))
            {
                row.Clear();
                _free[_freeCount++] = rowIndex;
                throw new LocalUiTransportBackpressureException(
                    "The bounded local UI ready queue is full.");
            }
            _queued[rowIndex] = true;
            _nextOperationId = _nextOperationId == ulong.MaxValue ? 0 : _nextOperationId + 1;
            return rowIndex;
        }
    }

    internal bool TryPeekClient(out int index, out LocalUiOperationRow? row)
    {
        if (!_ready.TryPeek(out index))
        {
            row = null;
            return false;
        }
        lock (_gate)
        {
            row = (uint)index < (uint)_rows.Length && _queued[index] ? _rows[index] : null;
            return row is not null;
        }
    }

    internal bool TryCompleteClient(int index, LocalSemanticProofStatus status)
    {
        lock (_gate)
        {
            if ((uint)index >= (uint)_rows.Length || !_queued[index]) return false;
            LocalUiOperationRow row = _rows[index];
            if (!_proofs.TryEnqueue(new LocalUiProof(row.OperationId, row.Instance, status)))
                return false;
            if (!_ready.TryDequeue(out int actual) || actual != index)
                throw new InvalidOperationException("The local UI ready queue lost FIFO ownership.");
            _queued[index] = false;
            return true;
        }
    }

    internal bool TryTakeProof(out LocalUiProof proof) => _proofs.TryDequeue(out proof);
    internal bool TrySubmitAction(in UiActionRequest request) =>
        _actions.TryEnqueue(new LocalUiAction(request));
    internal bool TryTakeAction(out UiActionRequest request)
    {
        if (_actions.TryDequeue(out LocalUiAction action))
        {
            request = action.Request;
            return true;
        }
        request = default;
        return false;
    }

    internal void ReleaseProof(in LocalUiProof proof)
    {
        lock (_gate)
        {
            for (int index = 0; index < _rows.Length; index++)
            {
                LocalUiOperationRow row = _rows[index];
                if (row.OperationId != proof.OperationId) continue;
                LocalUiOperationKind kind = row.Kind;
                row.Clear();
                _free[_freeCount++] = index;
                if (proof.Status == LocalSemanticProofStatus.Applied)
                {
                    _applied++;
                    if (kind == LocalUiOperationKind.ColdOpen) _coldOpensApplied++;
                    else if (kind == LocalUiOperationKind.HotOpen) _hotOpensApplied++;
                }
                else _rejected++;
                return;
            }
        }
    }
}

/// <summary>Authority-owner-thread UI port over the bounded local semantic channel.</summary>
internal sealed class LocalQueuedUiSemanticPort : IUiSemanticPort, IDisposable
{
    private readonly LocalQueuedUiChannel _channel;
    private readonly HashSet<UiViewInstanceId> _openViews = [];
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>?> _actions = [];
    private readonly int _maximumViews;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _nextView = 1;
    private bool _disposed;

    internal LocalQueuedUiSemanticPort(LocalQueuedUiChannel channel, int maximumViews)
    {
        _channel = channel;
        _maximumViews = maximumViews;
    }

    public UiViewInstanceId OpenView(UiOpenView operation)
    {
        EnsureOperational();
        UiViewInstanceId instance = Allocate(operation.Instance);
        var delivered = new UiOpenView(
            operation.CacheKey, operation.Definition, operation.SnapshotModel(), instance, operation.Sequence);
        try
        {
            _ = _channel.Publish(LocalUiOperationKind.ColdOpen, instance, delivered,
                default, default, default);
        }
        catch
        {
            _openViews.Remove(instance);
            throw;
        }
        return instance;
    }

    public UiViewInstanceId OpenHotView(UiHotOpenView operation)
    {
        EnsureOperational();
        UiViewInstanceId instance = Allocate(operation.Instance);
        var delivered = new UiHotOpenView(
            operation.CacheKey, operation.SnapshotModel(), instance, operation.Sequence);
        try
        {
            _ = _channel.Publish(LocalUiOperationKind.HotOpen, instance, default,
                delivered, default, default);
        }
        catch
        {
            _openViews.Remove(instance);
            throw;
        }
        return instance;
    }

    public void UpdateView(UiUpdateView operation)
    {
        EnsureOperational();
        if (!_openViews.Contains(operation.Instance))
            throw new InvalidOperationException("The local UI view is not open.");
        _ = _channel.Publish(LocalUiOperationKind.Update, operation.Instance, default,
            default, operation, default);
    }

    public bool CloseView(UiCloseView operation)
    {
        EnsureOperational();
        if (!_openViews.Contains(operation.Instance)) return false;
        _ = _channel.Publish(LocalUiOperationKind.Close, operation.Instance, default,
            default, default, operation);
        _actions.Remove(operation.Instance);
        _openViews.Remove(operation.Instance);
        return true;
    }

    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(handler);
        if (!instance.IsValid || !_openViews.Contains(instance))
            throw new InvalidOperationException("The local UI action endpoint is invalid.");
        if (_actions.ContainsKey(instance))
            throw new InvalidOperationException("The local UI action endpoint is already bound.");
        _actions.Add(instance, handler);
    }

    public void UnbindActions(UiViewInstanceId instance)
    {
        EnsureOperational();
        _actions.Remove(instance);
    }

    internal void Pump()
    {
        EnsureOperational();
        while (_channel.TryTakeProof(out LocalUiProof proof))
        {
            _channel.ReleaseProof(proof);
            if (proof.Status == LocalSemanticProofStatus.Rejected)
            {
                // Match the remote UI contract: a rejected projection leaves
                // this view desynchronized. Do not retry an operation against
                // a client view whose prior state is no longer known.
                _actions.Remove(proof.Instance);
                _openViews.Remove(proof.Instance);
            }
        }
        while (_channel.TryTakeAction(out UiActionRequest request))
        {
            if (_actions.TryGetValue(request.Instance, out Action<UiActionRequest>? handler) && handler is not null)
                handler(request);
        }
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        _openViews.Clear();
        _actions.Clear();
    }

    private UiViewInstanceId Allocate(UiViewInstanceId requested)
    {
        if (_openViews.Count >= _maximumViews)
            throw new InvalidOperationException("The local UI view capacity was reached.");
        if (requested.IsValid)
        {
            if (_openViews.Contains(requested) || requested.Value < _nextView)
                throw new InvalidOperationException("The requested local UI identity is stale or active.");
            _nextView = requested.Value == ulong.MaxValue ? 0 : requested.Value + 1;
            _openViews.Add(requested);
            return requested;
        }
        if (_nextView == 0) throw new InvalidOperationException("The local UI identity space is exhausted.");
        var result = new UiViewInstanceId(_nextView);
        _nextView = _nextView == ulong.MaxValue ? 0 : _nextView + 1;
        // Reserve the identity before BindActions. The placeholder also lets
        // Update/Close distinguish an allocated view during ordinary use.
        _openViews.Add(result);
        return result;
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The local UI authority port is owner-thread-bound.");
    }
}

/// <summary>Client-owner-thread endpoint that applies local UI debt and returns proof/actions.</summary>
public sealed class LocalQueuedUiClient : IUiActionSink, IDisposable
{
    private readonly LocalQueuedUiChannel _channel;
    private readonly UiClientRuntime _client;
    private readonly Action _detached;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _disposed;

    internal LocalQueuedUiClient(
        LocalQueuedUiChannel channel,
        UiClientRuntime client,
        Action detached)
    {
        _channel = channel;
        _client = client;
        _detached = detached;
        _client.AttachActionSink(this);
    }

    public int Pump(int maximumOperations = 32)
    {
        EnsureOperational();
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumOperations);
        int applied = 0;
        while (applied < maximumOperations && _channel.TryPeekClient(out int index, out LocalUiOperationRow? row))
        {
            LocalSemanticProofStatus status = LocalSemanticProofStatus.Applied;
            try
            {
                switch (row!.Kind)
                {
                    case LocalUiOperationKind.ColdOpen:
                        _ = _client.OpenView(row.ColdOpen);
                        break;
                    case LocalUiOperationKind.HotOpen:
                        _ = _client.OpenHotView(row.HotOpen);
                        break;
                    case LocalUiOperationKind.Update:
                        _client.UpdateView(row.Update);
                        break;
                    case LocalUiOperationKind.Close:
                        _ = _client.CloseView(row.Close);
                        break;
                    default:
                        status = LocalSemanticProofStatus.Rejected;
                        break;
                }
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                status = LocalSemanticProofStatus.Rejected;
            }
            if (!_channel.TryCompleteClient(index, status)) break;
            applied++;
        }
        return applied;
    }

    public void SubmitAction(UiActionRequest request)
    {
        EnsureOperational();
        if (!_channel.TrySubmitAction(request))
            throw new InvalidOperationException("The bounded local UI action queue is full.");
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        try { _client.DetachActionSink(this); }
        finally { _detached(); }
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The local UI client bridge is owner-thread-bound.");
    }
}
