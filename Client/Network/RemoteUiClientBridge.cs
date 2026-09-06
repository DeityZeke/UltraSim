using UltraSim.Client.Core;
using UltraSim.Game.Admission;
using UltraSim.Game.Protocol;
using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Client.Network;

public enum RemoteClientApplicationState : byte
{
    AwaitingAuthentication = 0,
    Active = 1,
    Detached = 2,
    Faulted = 3,
}

public readonly record struct RemoteUiClientSnapshot(
    RemoteClientApplicationState State,
    ulong ApplicationSessionEpoch,
    ulong ParticipantId,
    ulong LastOperationId,
    ulong ActionsQueued,
    ulong OperationsApplied,
    ulong DuplicateOperations,
    ulong RejectedOperations);

/// <summary>
/// Client-owned translation between the game application protocol and the
/// existing renderer-neutral <see cref="UiClientRuntime"/>.
/// </summary>
/// <remarks>
/// Transport delivery acknowledgement is deliberately not application
/// acknowledgement. This bridge publishes an explicit apply result only after
/// the client UI state machine has accepted or rejected a semantic operation.
/// It owns the supplied client runtime and deterministically disposes it when
/// the remote application session detaches.
/// </remarks>
public sealed class RemoteUiClientBridge : IUiActionSink, IDisposable
{
    private readonly NetworkProtocolPeer _peer;
    private readonly UiClientRuntime _client;
    private readonly UiSemanticWireLimits _wireLimits;
    private readonly byte[] _semanticScratch;
    private readonly byte[] _messageScratch;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private RemoteClientApplicationState _state;
    private ulong _pendingAuthenticationAttempt;
    private ulong _applicationSessionEpoch;
    private ulong _participantId;
    private ulong _lastOperationId;
    private ulong _nextActionSequence = 1;
    private ulong _actionsQueued;
    private ulong _operationsApplied;
    private ulong _duplicateOperations;
    private ulong _rejectedOperations;
    private IRemoteGameProtocolHandler? _additionalHandler;
    private bool _disposed;

    public RemoteUiClientBridge(
        NetworkProtocolPeer peer,
        UiClientRuntime client,
        UiSemanticWireLimits wireLimits = default)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        if (wireLimits == default) wireLimits = UltraSimGameProtocol.DefaultUiWireLimits;
        wireLimits.Validate();
        if (wireLimits.MaximumOperationBytes + RemoteUiOperationHeaderCodec.EncodedSize >
            UltraSimGameProtocol.MaximumMessageBytes)
            throw new ArgumentOutOfRangeException(nameof(wireLimits));
        _wireLimits = wireLimits;
        _semanticScratch = new byte[wireLimits.MaximumOperationBytes];
        _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
        _client.AttachActionSink(this);
    }

    public UiClientRuntime Client => _client;

    /// <summary>Binds one additional authenticated client-projection consumer.</summary>
    public void BindProtocolHandler(IRemoteGameProtocolHandler handler)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(handler);
        if (_additionalHandler is not null)
            throw new InvalidOperationException(
                "The remote client bridge already has an additional protocol handler.");
        _additionalHandler = handler;
    }

    /// <summary>
    /// Activates this semantic bridge from an already authenticated guest/application admission.
    /// This bypasses only the older provider-credential message pair; it does not manufacture
    /// participant or application-session identity.
    /// </summary>
    public bool TryActivateAdmittedSession(ParticipantId participant, ulong applicationSessionEpoch)
    {
        EnsureOperational();
        if (_state != RemoteClientApplicationState.AwaitingAuthentication ||
            _pendingAuthenticationAttempt != 0 || !participant.IsValid ||
            applicationSessionEpoch == 0)
            return false;
        _participantId = participant.Value;
        _applicationSessionEpoch = applicationSessionEpoch;
        _state = RemoteClientApplicationState.Active;
        return true;
    }

    public bool TryBeginAuthentication(ulong attemptId, string provider, string credential)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(credential);
        if (_state != RemoteClientApplicationState.AwaitingAuthentication ||
            attemptId == 0 || _pendingAuthenticationAttempt != 0)
            return false;
        if (!UltraSimGameProtocol.TryWriteAuthenticationRequest(
                _messageScratch, attemptId, provider, credential, out int written))
            return false;
        if (_peer.TryQueue(RemoteAuthenticationRequestHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        _pendingAuthenticationAttempt = attemptId;
        return true;
    }

    /// <summary>Consumes all currently delivered application messages in protocol order.</summary>
    public int ProcessReceived()
    {
        EnsureOperational();
        int processed = 0;
        while (_peer.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            try { ProcessMessage(message, payload); }
            finally { _ = _peer.TryConsumeReceived(); }
            processed++;
            if (_state is RemoteClientApplicationState.Detached or RemoteClientApplicationState.Faulted)
                break;
        }
        if (_state == RemoteClientApplicationState.Active &&
            _additionalHandler is IRemoteGameProtocolProgressor progressor)
            _ = progressor.TryProgress();
        return processed;
    }

    void IUiActionSink.SubmitAction(UiActionRequest request)
    {
        EnsureOperational();
        if (_state != RemoteClientApplicationState.Active)
            throw new InvalidOperationException("The remote application session is not active.");
        if (_nextActionSequence == 0)
            throw new InvalidOperationException("The remote UI action sequence is exhausted.");
        var sequenced = new UiActionRequest(
            request.Instance,
            request.Definition,
            request.Action,
            request.Row,
            request.Input,
            _nextActionSequence,
            request.Source);
        if (!UiSemanticWireCodec.TryWriteAction(
                _semanticScratch, sequenced, _wireLimits, out int semanticBytes) ||
            !UltraSimGameProtocol.TryWriteUiAction(
                _messageScratch, _applicationSessionEpoch,
                _semanticScratch.AsSpan(0, semanticBytes), out int messageBytes))
            throw new InvalidOperationException("The remote UI action exceeds its bounded wire representation.");
        NetworkQueueStatus status = _peer.TryQueue(
            RemoteUiActionHeaderCodec.Descriptor.Id,
            _messageScratch.AsSpan(0, messageBytes));
        if (status != NetworkQueueStatus.Accepted)
            throw new InvalidOperationException($"The remote UI action was not queued: {status}.");
        _nextActionSequence = _nextActionSequence == ulong.MaxValue ? 0 : _nextActionSequence + 1;
        _actionsQueued++;
    }

    public RemoteUiClientSnapshot GetSnapshot()
    {
        EnsureOwnerThread();
        return new(_state, _applicationSessionEpoch, _participantId, _lastOperationId,
            _actionsQueued, _operationsApplied, _duplicateOperations, _rejectedOperations);
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        _disposed = true;
        _state = RemoteClientApplicationState.Detached;
        _client.DetachActionSink(this);
        _client.Dispose();
        _additionalHandler = null;
        Array.Clear(_semanticScratch);
        Array.Clear(_messageScratch);
    }

    private void ProcessMessage(NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        NetworkMessageId id = message.MessageId;
        if (id == RemoteAuthenticationResultMessageCodec.Descriptor.Id)
        {
            ProcessAuthenticationResult(payload);
            return;
        }
        if (id == RemoteSessionDetachMessageCodec.Descriptor.Id)
        {
            ProcessDetach(payload);
            return;
        }
        if (_state != RemoteClientApplicationState.Active)
            return;
        if (id == RemoteUiOperationHeaderCodec.Descriptor.Id)
            ProcessUiOperation(payload);
        else
            _ = _additionalHandler?.TryProcess(in message, payload);
    }

    private void ProcessAuthenticationResult(ReadOnlySpan<byte> payload)
    {
        if (_state != RemoteClientApplicationState.AwaitingAuthentication ||
            !RemoteAuthenticationResultMessageCodec.TryReadExact(payload, out RemoteAuthenticationResultMessage result) ||
            result.AttemptId == 0 || result.AttemptId != _pendingAuthenticationAttempt)
            return;
        _pendingAuthenticationAttempt = 0;
        if (result.Status != RemoteAuthenticationStatus.Accepted ||
            result.ParticipantId == 0 || result.ApplicationSessionEpoch == 0)
        {
            _state = RemoteClientApplicationState.Detached;
            return;
        }
        _participantId = result.ParticipantId;
        _applicationSessionEpoch = result.ApplicationSessionEpoch;
        _state = RemoteClientApplicationState.Active;
    }

    private void ProcessDetach(ReadOnlySpan<byte> payload)
    {
        if (!RemoteSessionDetachMessageCodec.TryReadExact(payload, out RemoteSessionDetachMessage detach) ||
            detach.ApplicationSessionEpoch == 0 || _applicationSessionEpoch == 0 ||
            detach.ApplicationSessionEpoch != _applicationSessionEpoch)
            return;
        DetachClient();
    }

    private void ProcessUiOperation(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadUiOperation(
                payload, out RemoteUiOperationHeader header, out ReadOnlySpan<byte> semantic) ||
            header.ApplicationSessionEpoch != _applicationSessionEpoch)
            return;
        if (header.OperationId <= _lastOperationId)
        {
            _duplicateOperations++;
            QueueApplyResult(header.OperationId, 0, 0, RemoteUiApplyStatus.Duplicate);
            return;
        }
        if (header.OperationId != _lastOperationId + 1)
        {
            RejectOperation(header.OperationId, 0, 0);
            return;
        }

        UiViewInstanceId instance = UiViewInstanceId.Invalid;
        ulong modelSequence = 0;
        try
        {
            bool decoded;
            switch (header.Kind)
            {
                case RemoteUiOperationKind.ColdOpen:
                    decoded = UiSemanticWireCodec.TryReadColdOpen(
                        semantic, _wireLimits, out UiOpenView open);
                    if (decoded)
                    {
                        instance = open.Instance;
                        modelSequence = open.Sequence;
                        if (_client.OpenView(open) != instance) decoded = false;
                    }
                    break;
                case RemoteUiOperationKind.HotOpen:
                    decoded = UiSemanticWireCodec.TryReadHotOpen(
                        semantic, _wireLimits,
                        key => _client.TryGetCachedDefinition(key, out CompiledUiDefinition? definition)
                            ? definition : null,
                        out UiHotOpenView hot);
                    if (decoded)
                    {
                        instance = hot.Instance;
                        modelSequence = hot.Sequence;
                        if (_client.OpenHotView(hot) != instance) decoded = false;
                    }
                    break;
                case RemoteUiOperationKind.Update:
                    decoded = UiSemanticWireCodec.TryReadUpdate(
                        semantic, _wireLimits, out UiUpdateView update);
                    if (decoded)
                    {
                        instance = update.Instance;
                        modelSequence = update.Delta.Sequence;
                        _client.UpdateView(update);
                    }
                    break;
                case RemoteUiOperationKind.Close:
                    decoded = UiSemanticWireCodec.TryReadClose(
                        semantic, _wireLimits, out UiCloseView close);
                    if (decoded)
                    {
                        instance = close.Instance;
                        decoded = _client.CloseView(close);
                    }
                    break;
                default:
                    decoded = false;
                    break;
            }
            if (!decoded)
            {
                RejectOperation(header.OperationId, instance.Value, modelSequence);
                return;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            RejectOperation(header.OperationId, instance.Value, modelSequence);
            _state = RemoteClientApplicationState.Faulted;
            return;
        }

        _lastOperationId = header.OperationId;
        _operationsApplied++;
        QueueApplyResult(header.OperationId, instance.Value, modelSequence, RemoteUiApplyStatus.Applied);
    }

    private void RejectOperation(ulong operationId, ulong instance, ulong modelSequence)
    {
        _lastOperationId = operationId;
        _rejectedOperations++;
        QueueApplyResult(operationId, instance, modelSequence, RemoteUiApplyStatus.Rejected);
    }

    private void QueueApplyResult(
        ulong operationId,
        ulong instance,
        ulong modelSequence,
        RemoteUiApplyStatus status)
    {
        var result = new RemoteUiApplyResultMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            OperationId = operationId,
            ViewInstanceId = instance,
            ModelSequence = modelSequence,
            Status = status,
        };
        int bytes = RemoteUiApplyResultMessageCodec.EncodedSize;
        if (!RemoteUiApplyResultMessageCodec.TryWriteExact(_messageScratch.AsSpan(0, bytes), in result) ||
            _peer.TryQueue(RemoteUiApplyResultMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, bytes)) != NetworkQueueStatus.Accepted)
        {
            _state = RemoteClientApplicationState.Faulted;
        }
    }

    private void DetachClient()
    {
        if (_state == RemoteClientApplicationState.Detached) return;
        _state = RemoteClientApplicationState.Detached;
        _client.DetachActionSink(this);
        _client.Dispose();
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote UI client bridge is owner-thread-bound.");
    }
}
