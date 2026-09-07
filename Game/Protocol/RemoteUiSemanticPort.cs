using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Game.Protocol;

public readonly record struct RemoteUiPortLimits(
    int MaximumOpenViews = 1_024,
    int MaximumContentBackedDefinitions = 256)
{
    public static RemoteUiPortLimits Default => new(1_024, 256);
    internal void Validate()
    {
        if (MaximumOpenViews <= 0 || MaximumOpenViews > 65_536)
            throw new ArgumentOutOfRangeException(nameof(MaximumOpenViews));
        if (MaximumContentBackedDefinitions <= 0 || MaximumContentBackedDefinitions > 4_096)
            throw new ArgumentOutOfRangeException(nameof(MaximumContentBackedDefinitions));
    }
}

public readonly record struct RemoteUiPortSnapshot(
    ulong ApplicationSessionEpoch,
    int OpenViews,
    ulong NextOperationId,
    ulong LastAppliedOperationId,
    ulong AcceptedActions,
    ulong RejectedActions,
    int DesynchronizedViews,
    ulong ContentBackedHotOpens);

/// <summary>Normal bounded transport backpressure while publishing a remote UI operation.</summary>
public sealed class RemoteUiTransportBackpressureException : InvalidOperationException
{
    public RemoteUiTransportBackpressureException(NetworkQueueStatus status)
        : base($"The remote UI operation was not queued: {status}.") => Status = status;

    public NetworkQueueStatus Status { get; }
}

/// <summary>
/// Server-owned semantic UI adapter over one authenticated protocol peer.
/// It allocates view identities locally and never waits for a network round trip.
/// </summary>
public sealed class RemoteUiSemanticPort : IUiSemanticPort, IDisposable
{
    private readonly NetworkProtocolPeer _peer;
    private readonly UiSemanticWireLimits _wireLimits;
    private readonly RemoteUiPortLimits _limits;
    private readonly byte[] _semanticScratch;
    private readonly byte[] _messageScratch;
    private readonly Dictionary<UiViewInstanceId, ViewState> _views = [];
    private readonly HashSet<UiDefinitionIdentity> _contentBackedDefinitions;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _applicationSessionEpoch;
    private ulong _nextView = 1;
    private ulong _nextOperation = 1;
    private ulong _lastAppliedOperation;
    private ulong _acceptedActions;
    private ulong _rejectedActions;
    private ulong _contentBackedHotOpens;
    private bool _disposed;

    public RemoteUiSemanticPort(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        UiSemanticWireLimits wireLimits = default,
        RemoteUiPortLimits limits = default,
        IEnumerable<UiDefinitionIdentity>? contentBackedDefinitions = null)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        if (applicationSessionEpoch == 0)
            throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        if (wireLimits == default) wireLimits = UltraSimGameProtocol.DefaultUiWireLimits;
        if (limits == default) limits = RemoteUiPortLimits.Default;
        wireLimits.Validate();
        limits.Validate();
        if (wireLimits.MaximumOperationBytes + RemoteUiOperationHeaderCodec.EncodedSize >
            UltraSimGameProtocol.MaximumMessageBytes)
            throw new ArgumentOutOfRangeException(nameof(wireLimits));
        _wireLimits = wireLimits;
        _limits = limits;
        UiDefinitionIdentity[] admitted = (contentBackedDefinitions ?? [])
            .Take(limits.MaximumContentBackedDefinitions + 1).ToArray();
        if (admitted.Length > limits.MaximumContentBackedDefinitions ||
            admitted.Any(static identity => !identity.IsValid))
            throw new ArgumentException(
                "The content-backed UI definition set is invalid or exceeds its bounded capacity.",
                nameof(contentBackedDefinitions));
        _contentBackedDefinitions = new HashSet<UiDefinitionIdentity>(admitted);
        if (_contentBackedDefinitions.Count != admitted.Length)
            throw new ArgumentException(
                "The content-backed UI definition set contains duplicates.",
                nameof(contentBackedDefinitions));
        _applicationSessionEpoch = applicationSessionEpoch;
        _semanticScratch = new byte[wireLimits.MaximumOperationBytes];
        _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    }

    public event Action<Exception>? PresentationFaulted;

    public UiViewInstanceId OpenView(UiOpenView operation)
    {
        EnsureOperational();
        UiViewInstanceId instance = AllocateView(operation.Instance);
        UiModel model = operation.SnapshotModel();
        bool contentBacked = _contentBackedDefinitions.Contains(
            UiDefinitionIdentity.From(operation.Definition));
        RemoteUiOperationKind kind;
        int bytes;
        if (contentBacked)
        {
            var hot = new UiHotOpenView(
                operation.CacheKey, model, instance, operation.Sequence);
            if (!UiSemanticWireCodec.TryWriteHotOpen(
                    _semanticScratch, hot, _wireLimits, out bytes))
                throw new InvalidOperationException("The content-backed UI operation exceeds its bounded wire representation.");
            kind = RemoteUiOperationKind.HotOpen;
        }
        else
        {
            var remote = new UiOpenView(
                operation.CacheKey, operation.Definition, model, instance, operation.Sequence);
            if (!UiSemanticWireCodec.TryWriteColdOpen(
                    _semanticScratch, remote, _wireLimits, out bytes))
                throw new InvalidOperationException("The cold UI operation exceeds its bounded wire representation.");
            kind = RemoteUiOperationKind.ColdOpen;
        }
        ulong operationId = QueueOperation(kind, _semanticScratch.AsSpan(0, bytes));
        if (contentBacked && _contentBackedHotOpens != ulong.MaxValue)
            _contentBackedHotOpens++;
        _views.Add(instance, new ViewState(instance, operation.CacheKey, operation.Definition.Hash,
            operation.Sequence, operationId));
        return instance;
    }

    public UiViewInstanceId OpenHotView(UiHotOpenView operation)
    {
        EnsureOperational();
        UiViewInstanceId instance = AllocateView(operation.Instance);
        var remote = new UiHotOpenView(
            operation.CacheKey,
            operation.SnapshotModel(),
            instance,
            operation.Sequence);
        if (!UiSemanticWireCodec.TryWriteHotOpen(
                _semanticScratch, remote, _wireLimits, out int bytes))
            throw new InvalidOperationException("The hot UI operation exceeds its bounded wire representation.");
        ulong operationId = QueueOperation(RemoteUiOperationKind.HotOpen, _semanticScratch.AsSpan(0, bytes));
        _views.Add(instance, new ViewState(instance, operation.CacheKey,
            operation.CacheKey.DefinitionHash,
            operation.Sequence,
            operationId));
        return instance;
    }

    public void UpdateView(UiUpdateView operation)
    {
        EnsureOperational();
        if (!_views.TryGetValue(operation.Instance, out ViewState? view) || view.Desynchronized)
            throw new InvalidOperationException("The remote UI view is unavailable or requires a resnapshot.");
        if (!Matches(view.DefinitionHash, operation.Delta.Definition) ||
            operation.Delta.Sequence <= view.LastModelSequence)
            throw new InvalidOperationException("The remote UI update is stale or targets another definition.");
        if (!UiSemanticWireCodec.TryWriteUpdate(
                _semanticScratch, operation, _wireLimits, out int bytes))
            throw new InvalidOperationException("The UI update exceeds its bounded wire representation.");
        ulong operationId = QueueOperation(RemoteUiOperationKind.Update, _semanticScratch.AsSpan(0, bytes));
        view.LastModelSequence = operation.Delta.Sequence;
        view.LastOperationId = operationId;
    }

    public bool CloseView(UiCloseView operation)
    {
        EnsureOperational();
        if (!_views.ContainsKey(operation.Instance)) return false;
        if (!UiSemanticWireCodec.TryWriteClose(
                _semanticScratch, operation, _wireLimits, out int bytes))
            throw new InvalidOperationException("The UI close exceeds its bounded wire representation.");
        _ = QueueOperation(RemoteUiOperationKind.Close, _semanticScratch.AsSpan(0, bytes));
        _views.Remove(operation.Instance);
        return true;
    }

    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(handler);
        if (!_views.TryGetValue(instance, out ViewState? view))
            throw new InvalidOperationException("The remote UI view is not open.");
        if (view.Handler is not null)
            throw new InvalidOperationException("The remote UI view already has an action endpoint.");
        view.Handler = handler;
    }

    public void UnbindActions(UiViewInstanceId instance)
    {
        EnsureOperational();
        if (_views.TryGetValue(instance, out ViewState? view)) view.Handler = null;
    }

    public bool TryProcessAction(ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
        if (!UltraSimGameProtocol.TryReadUiAction(payload, out RemoteUiActionHeader header,
                out ReadOnlySpan<byte> semantic) ||
            header.ApplicationSessionEpoch != _applicationSessionEpoch ||
            !UiSemanticWireCodec.TryReadAction(semantic, _wireLimits, out UiActionRequest request) ||
            !_views.TryGetValue(request.Instance, out ViewState? view) || view.Desynchronized ||
            view.Handler is null || !Matches(view.DefinitionHash, request.Definition) ||
            request.ClientSequence == 0 ||
            request.ClientSequence <= view.LastClientActionSequence)
        {
            _rejectedActions++;
            return false;
        }
        view.LastClientActionSequence = request.ClientSequence;
        _acceptedActions++;
        view.Handler.Invoke(request);
        return true;
    }

    public bool TryProcessApplyResult(ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
        if (!RemoteUiApplyResultMessageCodec.TryReadExact(payload, out RemoteUiApplyResultMessage result) ||
            result.ApplicationSessionEpoch != _applicationSessionEpoch || result.OperationId == 0 ||
            result.OperationId >= _nextOperation || result.OperationId <= _lastAppliedOperation)
            return false;
        _lastAppliedOperation = result.OperationId;
        if (result.Status != RemoteUiApplyStatus.Rejected) return true;
        ViewState? view = null;
        if (result.ViewInstanceId != 0)
            _views.TryGetValue(new UiViewInstanceId(result.ViewInstanceId), out view);
        else
            view = _views.Values.FirstOrDefault(candidate =>
                candidate.LastOperationId == result.OperationId);
        if (view is not null)
        {
            view.Desynchronized = true;
            view.Handler = null;
        }
        NotifyPresentationFault(new InvalidOperationException(
            $"Remote UI operation {result.OperationId} was rejected by the client projection."));
        return true;
    }

    public RemoteUiPortSnapshot GetSnapshot()
    {
        EnsureOperational();
        int desynchronized = 0;
        foreach (ViewState view in _views.Values)
            if (view.Desynchronized) desynchronized++;
        return new(_applicationSessionEpoch, _views.Count, _nextOperation,
            _lastAppliedOperation, _acceptedActions, _rejectedActions, desynchronized,
            _contentBackedHotOpens);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _disposed = true;
        foreach (ViewState view in _views.Values) view.Handler = null;
        _views.Clear();
        Array.Clear(_semanticScratch);
        Array.Clear(_messageScratch);
    }

    private UiViewInstanceId AllocateView(UiViewInstanceId requested)
    {
        if (_views.Count >= _limits.MaximumOpenViews)
            throw new InvalidOperationException("The remote UI open-view capacity was reached.");
        if (requested.IsValid)
        {
            if (_views.ContainsKey(requested) || requested.Value < _nextView)
                throw new InvalidOperationException("The requested remote view identity is stale or active.");
            _nextView = requested.Value == ulong.MaxValue ? 0 : requested.Value + 1;
            return requested;
        }
        if (_nextView == 0)
            throw new InvalidOperationException("The remote UI view identity space is exhausted.");
        var result = new UiViewInstanceId(_nextView);
        _nextView = _nextView == ulong.MaxValue ? 0 : _nextView + 1;
        return result;
    }

    private ulong QueueOperation(RemoteUiOperationKind kind, ReadOnlySpan<byte> semantic)
    {
        if (_nextOperation == 0)
            throw new InvalidOperationException("The remote UI operation identity space is exhausted.");
        ulong operationId = _nextOperation;
        var header = new RemoteUiOperationHeader
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            OperationId = operationId,
            Kind = kind,
        };
        if (!UltraSimGameProtocol.TryWriteUiOperation(
                _messageScratch, in header, semantic, out int bytes))
            throw new InvalidOperationException("The remote UI operation could not be encoded.");
        NetworkQueueStatus status = _peer.TryQueue(
            RemoteUiOperationHeaderCodec.Descriptor.Id,
            _messageScratch.AsSpan(0, bytes));
        if (status != NetworkQueueStatus.Accepted)
            throw new RemoteUiTransportBackpressureException(status);
        _nextOperation = operationId == ulong.MaxValue ? 0 : operationId + 1;
        return operationId;
    }

    private void NotifyPresentationFault(Exception exception)
    {
        Delegate[] observers = PresentationFaulted?.GetInvocationList() ?? [];
        foreach (Delegate observer in observers)
        {
            try { ((Action<Exception>)observer)(exception); }
            catch { }
        }
    }

    private static bool Matches(UiDefinitionHash hash, UiDefinitionHandle handle) =>
        hash.Low == handle.Low && hash.High == handle.High;

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote UI port is bound to its creating owner thread.");
    }

    private sealed class ViewState
    {
        internal ViewState(
            UiViewInstanceId instance,
            UiDefinitionCacheKey cacheKey,
            UiDefinitionHash definitionHash,
            ulong modelSequence,
            ulong operationId)
        {
            Instance = instance;
            CacheKey = cacheKey;
            DefinitionHash = definitionHash;
            LastModelSequence = modelSequence;
            LastOperationId = operationId;
        }

        internal UiViewInstanceId Instance { get; }
        internal UiDefinitionCacheKey CacheKey { get; }
        internal UiDefinitionHash DefinitionHash { get; }
        internal ulong LastModelSequence { get; set; }
        internal ulong LastOperationId { get; set; }
        internal ulong LastClientActionSequence { get; set; }
        internal Action<UiActionRequest>? Handler { get; set; }
        internal bool Desynchronized { get; set; }
    }
}
