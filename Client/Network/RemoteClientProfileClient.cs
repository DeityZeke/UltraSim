using UltraSim.Client.Core;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Network;

namespace UltraSim.Client.Network;

public enum RemoteClientProfileState : byte
{
    AwaitingAdvertisement = 1,
    AwaitingDecision = 2,
    Applied = 3,
    Faulted = 4,
}

public readonly record struct RemoteClientProfileSnapshot(
    RemoteClientProfileState State,
    ulong ApplicationSessionEpoch,
    ApplicationRealmId Realm,
    ulong ProfileRevision,
    ClientProfileHash ProfileHash,
    ClientProfileApplicationMode Mode,
    bool RememberedDecision,
    ulong AdvertisementsReceived,
    ulong ProofsQueued,
    ulong RejectedMessages,
    string? Detail);

/// <summary>
/// Authenticated client-side profile gate. It maps the finite wire vocabulary to Client Core,
/// applies only an exact remembered realm/profile decision, and persists a new human decision
/// before allowing the profile-handled proof to advance the server application.
/// </summary>
public sealed class RemoteClientProfileClient : IRemoteGameProtocolHandler, IRemoteGameProtocolProgressor
{
    private readonly NetworkProtocolPeer _peer;
    private readonly ulong _applicationSessionEpoch;
    private readonly ApplicationRealmId _realm;
    private readonly ClientProfileDecisionFileStore _store;
    private readonly byte[] _proof = new byte[RemoteClientProfileAppliedMessageCodec.EncodedSize];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ClientProfileDecisionDocument _decisions;
    private RemoteClientProfileAdvertisement? _remoteProfile;
    private ClientProfileRecommendation? _recommendation;
    private ClientProfileHash _hash;
    private ClientProfileApplicationMode _mode = ClientProfileApplicationMode.Ignore;
    private RemoteClientProfileAppliedMessage _pendingProof;
    private bool _hasPendingProof;
    private bool _remembered;
    private ulong _advertisements;
    private ulong _proofsQueued;
    private ulong _rejectedMessages;
    private string? _detail;
    private RemoteClientProfileState _state = RemoteClientProfileState.AwaitingAdvertisement;

    public RemoteClientProfileClient(
        NetworkProtocolPeer peer,
        ulong applicationSessionEpoch,
        ApplicationRealmId realm,
        ClientProfileDecisionDocument decisions,
        ClientProfileDecisionFileStore store)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        if (!realm.IsValid) throw new ArgumentOutOfRangeException(nameof(realm));
        _applicationSessionEpoch = applicationSessionEpoch;
        _realm = realm;
        _decisions = decisions ?? throw new ArgumentNullException(nameof(decisions));
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }

    public ClientProfileRecommendation Recommendation => _recommendation ??
        throw new InvalidOperationException("A supported remote client profile is not available.");
    public ClientProfileDecisionDocument DecisionDocument => _decisions;

    public int ProcessReceived()
    {
        EnsureOwner();
        int processed = 0;
        while (_peer.TryPeekReceived(out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
        {
            if (message.MessageId != RemoteClientProfileHeaderCodec.Descriptor.Id)
            {
                if (_state == RemoteClientProfileState.AwaitingAdvertisement)
                    Fault("The authenticated server sent application traffic before its client profile.");
                break;
            }
            try { _ = TryProcess(in message, payload); }
            finally { _ = _peer.TryConsumeReceived(); }
            processed++;
        }
        _ = TryProgress();
        return processed;
    }

    public bool TryChoose(ClientProfileApplicationMode mode)
    {
        EnsureOwner();
        if (_state != RemoteClientProfileState.AwaitingDecision || _remoteProfile is null ||
            mode is not (ClientProfileApplicationMode.UseServerDefaults or
                ClientProfileApplicationMode.KeepMyControls or ClientProfileApplicationMode.Customize))
            return false;
        var decision = new ClientProfileDecision(
            _realm, _remoteProfile.Revision, _hash, mode);
        ClientProfileDecisionDocument candidate;
        try
        {
            candidate = _decisions.WithDecision(in decision);
            _store.Save(candidate);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or
            ArgumentException or InvalidOperationException)
        {
            _detail = $"The profile decision could not be persisted: {exception.Message}";
            return false;
        }
        _decisions = candidate;
        Activate(mode, remembered: false);
        return true;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOwner();
        if (message.MessageId != RemoteClientProfileHeaderCodec.Descriptor.Id) return false;
        string? error = null;
        if (!UltraSimGameProtocol.TryReadClientProfile(
                payload, out ulong epoch, out RemoteClientProfileAdvertisement? profile) ||
            epoch != _applicationSessionEpoch || profile is null ||
            !TryMap(profile, out ClientProfileRecommendation? recommendation,
                out ClientProfileHash hash, out error))
        {
            SaturatingIncrement(ref _rejectedMessages);
            Fault(error ?? "The authenticated server client profile was invalid or unsupported.");
            return true;
        }
        if (_remoteProfile is not null)
        {
            if (_remoteProfile.Revision != profile.Revision || _hash != hash)
            {
                SaturatingIncrement(ref _rejectedMessages);
                Fault("The server changed its client profile within one application session.");
            }
            else SaturatingIncrement(ref _advertisements);
            return true;
        }
        _remoteProfile = profile;
        _recommendation = recommendation;
        _hash = hash;
        SaturatingIncrement(ref _advertisements);
        if (_decisions.TryFindExact(
                _realm, profile.Revision, hash, out ClientProfileDecision remembered))
            Activate(remembered.Mode, remembered: true);
        else
            _state = RemoteClientProfileState.AwaitingDecision;
        return true;
    }

    public bool TryProgress()
    {
        EnsureOwner();
        if (!_hasPendingProof) return true;
        if (!RemoteClientProfileAppliedMessageCodec.TryWriteExact(_proof, in _pendingProof))
            throw new InvalidOperationException("The retained client-profile proof became invalid.");
        if (_peer.TryQueue(RemoteClientProfileAppliedMessageCodec.Descriptor.Id, _proof) !=
            NetworkQueueStatus.Accepted)
            return false;
        _hasPendingProof = false;
        SaturatingIncrement(ref _proofsQueued);
        return true;
    }

    public RemoteClientProfileSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new RemoteClientProfileSnapshot(
            _state, _applicationSessionEpoch, _realm, _remoteProfile?.Revision ?? 0,
            _hash, _mode, _remembered, _advertisements, _proofsQueued,
            _rejectedMessages, _detail);
    }

    private void Activate(ClientProfileApplicationMode mode, bool remembered)
    {
        _mode = mode;
        _remembered = remembered;
        _state = RemoteClientProfileState.Applied;
        _detail = null;
        _pendingProof = UltraSimGameProtocol.CreateClientProfileApplied(
            _applicationSessionEpoch, _remoteProfile!, RemoteClientProfileApplyStatus.Applied);
        _hasPendingProof = true;
        _ = TryProgress();
    }

    private static bool TryMap(
        RemoteClientProfileAdvertisement remote,
        out ClientProfileRecommendation? recommendation,
        out ClientProfileHash hash,
        out string? error)
    {
        recommendation = null;
        hash = new ClientProfileHash(
            remote.Hash.Part0, remote.Hash.Part1, remote.Hash.Part2, remote.Hash.Part3);
        error = null;
        try
        {
            var bindings = new ClientPhysicalBinding[remote.Bindings.Count];
            for (int index = 0; index < bindings.Length; index++)
            {
                RemoteClientProfileBinding binding = remote.Bindings[index];
                if (!ClientPhysicalInput.TryCreate(
                        (ClientPhysicalInputKind)binding.Kind,
                        binding.Code,
                        (ClientInputModifiers)binding.Modifiers,
                        out ClientPhysicalInput input))
                {
                    error = "The server profile contained an unsupported physical input.";
                    return false;
                }
                bindings[index] = new ClientPhysicalBinding(
                    (ClientSemanticAction)binding.Action, input);
            }
            recommendation = new ClientProfileRecommendation(
                (ClientControlGrammar)remote.ControlGrammar,
                (ClientCameraProfile)remote.CameraProfile,
                remote.CameraDistanceMillimeters / 1_000f,
                remote.CameraFovTenths / 10f,
                bindings);
            return hash.IsValid;
        }
        catch (ArgumentException exception)
        {
            error = $"The server profile exceeded stock client limits: {exception.Message}";
            return false;
        }
    }

    private void Fault(string detail)
    {
        _state = RemoteClientProfileState.Faulted;
        _detail = detail;
        _mode = ClientProfileApplicationMode.Ignore;
        _hasPendingProof = false;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote client-profile gate is owner-thread-bound.");
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }
}
