using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Protocol;
using UltraSim.Network;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Replication;

/// <summary>Cold authentication policy over an already secured network channel.</summary>
public delegate bool RemoteIdentityAuthenticator(
    string provider,
    string credential,
    out TrustedIdentity trustedIdentity);

public enum RemoteServerApplicationState : byte
{
    AwaitingAuthentication = 0,
    Active = 1,
    Rejected = 2,
    Detached = 3,
}

public readonly record struct RemoteServerApplicationSnapshot(
    RemoteServerApplicationState State,
    ulong ApplicationSessionEpoch,
    ParticipantId Participant,
    ulong AuthenticationAttempt,
    ulong MessagesProcessed,
    RemoteUiPortSnapshot Ui);

/// <summary>
/// Server application binding for one secure protocol peer.
/// </summary>
/// <remarks>
/// The secure handshake proves the transport endpoint. This layer separately
/// authenticates a provider credential into <see cref="TrustedIdentity"/>,
/// allocates the game participant on the server, and delegates authority to the
/// existing Accounting and Admission workflows. Network never resolves an ECS
/// entity or mutates game state directly.
/// </remarks>
public sealed class RemoteGameApplicationSession : IDisposable
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ServerGameFacet _runtime;
    private readonly IAdmissionEntityBinder _binder;
    private readonly RemoteIdentityAuthenticator _authenticator;
    private readonly ParticipantId _serverParticipant;
    private readonly ulong _applicationSessionEpoch;
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private RemoteServerApplicationState _state;
    private AdmissionService? _admission;
    private RemoteUiSemanticPort? _ui;
    private RemoteAuthenticationResultMessage _authenticationResult;
    private ulong _authenticationAttempt;
    private ulong _messagesProcessed;
    private bool _disposed;

    public RemoteGameApplicationSession(
        NetworkProtocolPeer peer,
        ServerGameFacet runtime,
        IAdmissionEntityBinder binder,
        RemoteIdentityAuthenticator authenticator,
        ParticipantId serverParticipant,
        ulong applicationSessionEpoch)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _binder = binder ?? throw new ArgumentNullException(nameof(binder));
        _authenticator = authenticator ?? throw new ArgumentNullException(nameof(authenticator));
        if (!serverParticipant.IsValid)
            throw new ArgumentException("The server-created participant is invalid.", nameof(serverParticipant));
        if (applicationSessionEpoch == 0)
            throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        _serverParticipant = serverParticipant;
        _applicationSessionEpoch = applicationSessionEpoch;
    }

    public AdmissionService Admission => _admission ??
        throw new InvalidOperationException("The remote application session is not authenticated.");

    public RemoteUiSemanticPort Ui => _ui ??
        throw new InvalidOperationException("The remote application session is not authenticated.");

    /// <summary>Reports isolated presentation cleanup failures after authority/session retirement.</summary>
    public event Action<Exception>? PresentationFaulted;

    /// <summary>Reports an unexpected application-authentication implementation failure.</summary>
    public event Action<Exception>? ApplicationFaulted;

    /// <summary>Drains delivered application messages without exposing them to Network policy.</summary>
    public int ProcessReceived()
    {
        EnsureOperational();
        int processed = 0;
        while (_peer.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            try { ProcessMessage(message, payload); }
            finally { _ = _peer.TryConsumeReceived(); }
            processed++;
            _messagesProcessed++;
        }
        return processed;
    }

    public RemoteServerApplicationSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new(
            _state,
            _state == RemoteServerApplicationState.Active ? _applicationSessionEpoch : 0,
            _state == RemoteServerApplicationState.Active ? _serverParticipant : default,
            _authenticationAttempt,
            _messagesProcessed,
            _ui?.GetSnapshot() ?? default);
    }

    /// <summary>
    /// Ends the application session. Reliable UI cleanup is presentation-only;
    /// authoritative Admission state is retired before a detach notice is queued.
    /// </summary>
    public void Disconnect()
    {
        EnsureOperational();
        if (_state == RemoteServerApplicationState.Detached) return;
        DisposeAdmissionBestEffort();
        _admission = null;
        var detach = new RemoteSessionDetachMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
        };
        int bytes = RemoteSessionDetachMessageCodec.EncodedSize;
        if (RemoteSessionDetachMessageCodec.TryWriteExact(_messageScratch.AsSpan(0, bytes), in detach))
            _ = _peer.TryQueue(RemoteSessionDetachMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, bytes));
        _ui?.Dispose();
        _ui = null;
        _state = RemoteServerApplicationState.Detached;
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        if (_state != RemoteServerApplicationState.Detached)
            Disconnect();
        _disposed = true;
        PresentationFaulted = null;
        ApplicationFaulted = null;
        Array.Clear(_messageScratch);
    }

    private void ProcessMessage(NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        if (message.MessageId == RemoteAuthenticationRequestHeaderCodec.Descriptor.Id)
        {
            ProcessAuthentication(payload);
            return;
        }
        if (_state != RemoteServerApplicationState.Active || _ui is null)
            return;
        if (message.MessageId == RemoteUiActionHeaderCodec.Descriptor.Id)
        {
            _ = _ui.TryProcessAction(payload);
        }
        else if (message.MessageId == RemoteUiApplyResultMessageCodec.Descriptor.Id)
        {
            _ = _ui.TryProcessApplyResult(payload);
        }
    }

    private void ProcessAuthentication(ReadOnlySpan<byte> payload)
    {
        if (!UltraSimGameProtocol.TryReadAuthenticationRequest(
                payload, out RemoteAuthenticationRequestHeader request,
                out string provider, out string credential))
            return;

        if (_authenticationAttempt != 0)
        {
            if (request.AttemptId == _authenticationAttempt)
                _ = QueueAuthenticationResult(in _authenticationResult);
            return;
        }

        _authenticationAttempt = request.AttemptId;
        bool authenticated;
        TrustedIdentity identity = default;
        try { authenticated = _authenticator(provider, credential, out identity); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            _state = RemoteServerApplicationState.Detached;
            NotifyIsolated(ApplicationFaulted, exception);
            return;
        }

        if (authenticated && identity.IsValid)
        {
            _ui = new RemoteUiSemanticPort(_peer, _applicationSessionEpoch);
            _admission = new AdmissionService(_runtime, _binder, _ui);
            AdmissionResult admission = _admission.JoinAccountBacked(_serverParticipant, identity);
            if (admission.IsWaitingForCharacter || admission.IsEntered)
            {
                _state = RemoteServerApplicationState.Active;
                _authenticationResult = new RemoteAuthenticationResultMessage
                {
                    AttemptId = request.AttemptId,
                    Status = RemoteAuthenticationStatus.Accepted,
                    ParticipantId = _serverParticipant.Value,
                    ApplicationSessionEpoch = _applicationSessionEpoch,
                };
                if (!QueueAuthenticationResult(in _authenticationResult))
                {
                    DisposeAdmissionBestEffort();
                    _admission = null;
                    _ui.Dispose();
                    _ui = null;
                    _state = RemoteServerApplicationState.Detached;
                }
                return;
            }
            DisposeAdmissionBestEffort();
            _admission = null;
            _ui.Dispose();
            _ui = null;
        }

        _state = RemoteServerApplicationState.Rejected;
        _authenticationResult = new RemoteAuthenticationResultMessage
        {
            AttemptId = request.AttemptId,
            Status = RemoteAuthenticationStatus.Rejected,
        };
        if (!QueueAuthenticationResult(in _authenticationResult))
            _state = RemoteServerApplicationState.Detached;
    }

    private bool QueueAuthenticationResult(in RemoteAuthenticationResultMessage result)
    {
        int bytes = RemoteAuthenticationResultMessageCodec.EncodedSize;
        if (!RemoteAuthenticationResultMessageCodec.TryWriteExact(
                _messageScratch.AsSpan(0, bytes), in result))
            return false;
        return _peer.TryQueue(
            RemoteAuthenticationResultMessageCodec.Descriptor.Id,
            _messageScratch.AsSpan(0, bytes)) == NetworkQueueStatus.Accepted;
    }

    private void DisposeAdmissionBestEffort()
    {
        if (_admission is null) return;
        try { _admission.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            NotifyIsolated(PresentationFaulted, exception);
        }
    }

    private static void NotifyIsolated(Action<Exception>? eventHandler, Exception exception)
    {
        Delegate[] observers = eventHandler?.GetInvocationList() ?? [];
        foreach (Delegate observer in observers)
        {
            try { ((Action<Exception>)observer)(exception); }
            catch { }
        }
    }

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote game application session is owner-thread-bound.");
    }
}
