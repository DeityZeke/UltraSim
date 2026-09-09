using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Application;
using UltraSim.Game.Chat;
using UltraSim.Game.Composition;
using UltraSim.Game.Inspection;
using UltraSim.Game.Input;
using UltraSim.Game.Inventory;
using UltraSim.Game.Protocol;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;
using UltraSim.UI;

namespace UltraSim.Game.Replication;

public readonly record struct RemoteGuestPlayableServerSnapshot(
    bool IdentityQueued,
    bool ContentRequired,
    bool ContentApplied,
    bool GuestAdmitted,
    bool AdmissionApplied,
    ulong ApplicationSessionEpoch,
    ParticipantId Participant,
    GameControlSessionEpoch ControlSession,
    ulong AuthorityTicks,
    ulong CommandsApplied,
    ulong DuplicateCommands,
    ulong RejectedCommands,
    ulong OwnerStatesQueued,
    ulong ContentRequirementsQueued,
    ulong DuplicateContentProofs,
    ulong RejectedContentProofs,
    ulong RejectedPreContentMessages,
    bool ClientProfileRequired,
    bool ClientProfileApplied,
    ulong ClientProfilesQueued,
    ulong DuplicateClientProfileProofs,
    ulong RejectedClientProfileProofs,
    ulong RejectedPreProfileMessages,
    bool PublicPresentationApplied,
    ulong PublicPresentationDeliveriesQueued,
    ulong DuplicatePublicPresentationProofs,
    ulong RejectedPublicPresentationProofs,
    RemoteChunkWorldStateServerSnapshot World,
    RemoteChatDeliveryServiceSnapshot Chat);

/// <summary>
/// F0.3b external guest/application factory. Each admitted transport owns one authoritative
/// playable runtime on its existing Hosted connection-owner thread. The Direct Connect product
/// currently admits one such session; shared multi-participant world ownership is deferred to the
/// separately reviewed multiplayer expansion.
/// </summary>
public sealed class RemoteGuestPlayableApplicationFactory : IHostedUdpSessionApplicationFactory
{
    private const int MaximumHotOpenUiDefinitions = 256;
    private readonly ApplicationRealmId _realm;
    private readonly RemoteContentAuthorization? _contentAuthorization;
    private readonly UiDefinitionIdentity[] _contentBackedUiDefinitions;
    private readonly UiDefinitionIdentity[] _standardUiDefinitions;
    private readonly RemoteClientProfileAdvertisement? _clientProfile;
    private long _nextParticipant;

    public RemoteGuestPlayableApplicationFactory(
        ApplicationRealmId realm,
        RemoteContentAuthorization? contentAuthorization = null,
        IEnumerable<UiDefinitionIdentity>? contentBackedUiDefinitions = null,
        RemoteClientProfileAdvertisement? clientProfile = null,
        IEnumerable<UiDefinitionIdentity>? standardUiDefinitions = null)
    {
        if (!realm.IsValid) throw new ArgumentOutOfRangeException(nameof(realm));
        if (contentAuthorization is not null &&
            (!contentAuthorization.IsAuthorityValid ||
             contentAuthorization.RealmHigh != realm.High || contentAuthorization.RealmLow != realm.Low))
            throw new ArgumentException(
                "The required content authorization must belong to this realm and include the expected result.",
                nameof(contentAuthorization));
        _realm = realm;
        _contentAuthorization = contentAuthorization;
        _contentBackedUiDefinitions = CopyUiDefinitions(
            contentBackedUiDefinitions, nameof(contentBackedUiDefinitions));
        _standardUiDefinitions = CopyUiDefinitions(
            standardUiDefinitions, nameof(standardUiDefinitions));
        _clientProfile = clientProfile;
        if (_contentBackedUiDefinitions.Length != 0 && contentAuthorization is null)
            throw new ArgumentException(
                "Content-backed UI definitions require an exact authenticated content authorization.",
                nameof(contentBackedUiDefinitions));
        ValidateHotOpenDefinitionSets(
            _contentBackedUiDefinitions, _standardUiDefinitions,
            nameof(standardUiDefinitions));
    }

    public IHostedUdpSessionApplication Create(ConnectionId connection, NetworkProtocolPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        long value = Interlocked.Increment(ref _nextParticipant);
        if (value <= 0)
            throw new InvalidOperationException("The remote participant identity space is exhausted.");
        return new RemoteGuestPlayableApplication(
            peer, _realm, new ParticipantId(checked((ulong)value)), CreateEpoch(),
            _contentAuthorization, _contentBackedUiDefinitions, _clientProfile,
            _standardUiDefinitions);
    }

    private static ulong CreateEpoch()
    {
        Span<byte> random = stackalloc byte[sizeof(ulong)];
        ulong epoch;
        do
        {
            RandomNumberGenerator.Fill(random);
            epoch = BinaryPrimitives.ReadUInt64LittleEndian(random);
        } while (epoch == 0);
        return epoch;
    }

    private static UiDefinitionIdentity[] CopyUiDefinitions(
        IEnumerable<UiDefinitionIdentity>? source,
        string parameterName)
    {
        UiDefinitionIdentity[] values = (source ?? [])
            .Take(MaximumHotOpenUiDefinitions + 1).ToArray();
        if (values.Length > MaximumHotOpenUiDefinitions ||
            values.Any(static value => !value.IsValid) ||
            values.Distinct().Count() != values.Length)
            throw new ArgumentException(
                "The UI definition set is invalid, duplicated, or over capacity.",
                parameterName);
        return values;
    }

    private static void ValidateHotOpenDefinitionSets(
        UiDefinitionIdentity[] contentBacked,
        UiDefinitionIdentity[] standard,
        string parameterName)
    {
        if (contentBacked.Length + standard.Length > MaximumHotOpenUiDefinitions ||
            standard.Intersect(contentBacked).Any())
            throw new ArgumentException(
                "Standard and content-backed hot-open UI definitions exceed capacity or overlap.",
                parameterName);
    }
}

/// <summary>
/// One owner-thread external Concept Demo application. Game owns guest admission, control,
/// Physics, public disclosure, Chat, Inventory and informational UI; Network only carries their
/// existing bounded semantic messages.
/// </summary>
public sealed class RemoteGuestPlayableApplication :
    IHostedUdpSessionApplication,
    IHostedUdpSessionApplicationLifetime
{
    private const int MaximumPublicationRecords = 64;
    private const int MaximumSubscriptions = 1_024;
    private const int MaximumTopologyChanges = MaximumSubscriptions * 2;
    private const int ReservedReliableControlMessages = 16;
    private const long AdmissionRetryMilliseconds = 1_000;

    private readonly NetworkProtocolPeer _peer;
    private readonly ApplicationRealmId _realm;
    private readonly RemoteContentAuthorization? _contentAuthorization;
    private readonly UiDefinitionIdentity[] _contentBackedUiDefinitions;
    private readonly UiDefinitionIdentity[] _standardUiDefinitions;
    private readonly RemoteClientProfileAdvertisement? _clientProfile;
    private readonly ParticipantId _participant;
    private readonly ulong _applicationSessionEpoch;
    private readonly SharedPlayableAuthority? _sharedAuthority;
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly OfflineDirectChunkSubscriptionChange[] _subscriptionChanges =
        new OfflineDirectChunkSubscriptionChange[MaximumTopologyChanges];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _identityQueued;
    private bool _clientProfileApplied;
    private long _lastClientProfileQueue;
    private ulong _clientProfilesQueued;
    private ulong _duplicateClientProfileProofs;
    private ulong _rejectedClientProfileProofs;
    private ulong _rejectedPreProfileMessages;
    private bool _contentApplied;
    private long _lastContentRequirementQueue;
    private ulong _contentRequirementsQueued;
    private ulong _duplicateContentProofs;
    private ulong _rejectedContentProofs;
    private ulong _rejectedPreContentMessages;
    private ulong _admissionAttempt;
    private RemoteGuestAdmissionResultHeader _admissionResult;
    private string? _admissionResultText;
    private bool _admissionResultApplied;
    private long _lastAdmissionResultQueue;
    private bool _guestAdmitted;
    private bool _applicationFaulted;
    private bool _retirementRequested;
    private GameControlSessionEpoch _controlSession;
    private GameGuestIdentity _guestIdentity;
    private WorldPublicEntityId _playerPublicId;
    private bool _publicPresentationApplied;
    private long _lastPublicPresentationQueue;
    private ulong _publicPresentationDeliveriesQueued;
    private ulong _duplicatePublicPresentationProofs;
    private ulong _rejectedPublicPresentationProofs;
    private readonly SharedPublicPresentationRow[]? _sharedPresentation;
    private readonly SharedPlayableParticipant[]? _sharedParticipantScratch;
    private ulong _nextSharedPresentationDeliveryId = 1;
    private int _sharedPresentationDeliverySlot = -1;
    private readonly SharedPublicPresentationRow[] _publicItemPresentations =
        new SharedPublicPresentationRow[64];
    private readonly GamePublicWeaponPresentation[] _publicItemScratch =
        new GamePublicWeaponPresentation[64];
    private ulong _nextPublicItemPresentationDeliveryId = 1UL << 63;
    private int _publicItemPresentationDeliverySlot = -1;
    private WorldPack? _worldPack;
    private UltraSimGameComposition? _composition;
    private ServerRuntime? _runtime;
    private AdmissionService? _admission;
    private RemoteUiSemanticPort? _ui;
    private InventoryUiController? _inventoryUi;
    private InformationalSignUiController? _signUi;
    private GameChatService? _chat;
    private RemoteChatDeliveryService? _chatDelivery;
    private RemoteChatServerSession? _chatSession;
    private OfflineDirectChunkRecipient? _recipient;
    private RemoteChunkWorldStateServer? _worldDelivery;
    private int _worldScheduleCursor;
    private GamePlayableWorldEntities _entities;
    private GamePlayerCommandResult _lastCommandResult;
    private ulong _lastProcessedCommand;
    private ulong _authorityTicks;
    private ulong _ownerStateSequence;
    private ulong _lastOwnerStateSimulationTick = ulong.MaxValue;
    private ulong _lastOwnerStateCommandSequence = ulong.MaxValue;
    private ulong _lastOwnerStateGeneration;
    private WorldPosition _lastOwnerStatePosition;
    private WorldYaw _lastOwnerStateOrientation;
    private ulong _commandsApplied;
    private ulong _duplicateCommands;
    private ulong _rejectedCommands;
    private ulong _uiBackpressureRejections;
    private ulong _ownerStatesQueued;
    private long _nextTickTimestamp;
    private long _tickInterval;
    private bool _disposed;
    private SharedPlayableParticipant _sharedParticipant;

    public RemoteGuestPlayableApplication(
        NetworkProtocolPeer peer,
        ApplicationRealmId realm,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        RemoteContentAuthorization? contentAuthorization = null,
        IEnumerable<UiDefinitionIdentity>? contentBackedUiDefinitions = null,
        RemoteClientProfileAdvertisement? clientProfile = null,
        IEnumerable<UiDefinitionIdentity>? standardUiDefinitions = null,
        SharedPlayableAuthority? sharedAuthority = null)
    {
        _peer = peer ?? throw new ArgumentNullException(nameof(peer));
        if (!realm.IsValid) throw new ArgumentOutOfRangeException(nameof(realm));
        if (!participant.IsValid) throw new ArgumentOutOfRangeException(nameof(participant));
        ArgumentOutOfRangeException.ThrowIfZero(applicationSessionEpoch);
        _realm = realm;
        _participant = participant;
        _applicationSessionEpoch = applicationSessionEpoch;
        if (contentAuthorization is not null &&
            (!contentAuthorization.IsAuthorityValid ||
             contentAuthorization.RealmHigh != realm.High || contentAuthorization.RealmLow != realm.Low))
            throw new ArgumentException("The required content authorization is invalid.",
                nameof(contentAuthorization));
        _contentAuthorization = contentAuthorization;
        _sharedAuthority = sharedAuthority;
        if (sharedAuthority is not null)
        {
            int participantCapacity = sharedAuthority.GetSnapshot().MaximumParticipants;
            _sharedPresentation = new SharedPublicPresentationRow[participantCapacity];
            _sharedParticipantScratch = new SharedPlayableParticipant[participantCapacity];
        }
        _clientProfile = clientProfile;
        _contentBackedUiDefinitions = (contentBackedUiDefinitions ?? [])
            .Take(257).ToArray();
        _standardUiDefinitions = (standardUiDefinitions ?? [])
            .Take(257).ToArray();
        if (_contentBackedUiDefinitions.Length > 256 ||
            _contentBackedUiDefinitions.Any(static value => !value.IsValid) ||
            _contentBackedUiDefinitions.Distinct().Count() != _contentBackedUiDefinitions.Length ||
            (_contentBackedUiDefinitions.Length != 0 && contentAuthorization is null))
            throw new ArgumentException(
                "Content-backed UI definitions require bounded exact content authorization.",
                nameof(contentBackedUiDefinitions));
        if (_standardUiDefinitions.Length > 256 ||
            _standardUiDefinitions.Any(static value => !value.IsValid) ||
            _standardUiDefinitions.Distinct().Count() != _standardUiDefinitions.Length ||
            _standardUiDefinitions.Intersect(_contentBackedUiDefinitions).Any() ||
            _standardUiDefinitions.Length + _contentBackedUiDefinitions.Length > 256)
            throw new ArgumentException(
                "Standard UI definitions must be bounded, exact, unique, and distinct from content-backed definitions.",
                nameof(standardUiDefinitions));
        _contentApplied = contentAuthorization is null;
        _clientProfileApplied = clientProfile is null;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
        if (message.MessageId == RemoteSessionDetachMessageCodec.Descriptor.Id)
        {
            if (RemoteSessionDetachMessageCodec.TryReadExact(
                    payload, out RemoteSessionDetachMessage detach) &&
                detach.ApplicationSessionEpoch == _applicationSessionEpoch)
                _retirementRequested = true;
            return true;
        }
        if (message.MessageId == RemoteClientProfileAppliedMessageCodec.Descriptor.Id)
        {
            ProcessClientProfileProof(payload);
            return true;
        }
        if (!_clientProfileApplied)
        {
            SaturatingIncrement(ref _rejectedPreProfileMessages);
            return false;
        }
        if (message.MessageId == RemoteContentSetAppliedMessageCodec.Descriptor.Id)
        {
            ProcessContentProof(payload);
            return true;
        }
        if (message.MessageId == RemoteGuestAdmissionRequestHeaderCodec.Descriptor.Id)
        {
            ProcessGuestRequest(payload);
            return true;
        }
        if (message.MessageId == RemoteGuestAdmissionAppliedMessageCodec.Descriptor.Id)
        {
            ProcessGuestProof(payload);
            return true;
        }
        if (message.MessageId == RemotePublicPresentationAppliedMessageCodec.Descriptor.Id)
        {
            ProcessPublicPresentationProof(payload);
            return true;
        }
        if (!_guestAdmitted || !_admissionResultApplied || _applicationFaulted)
            return false;
        if (message.MessageId == RemotePlayableCommandMessageCodec.Descriptor.Id)
        {
            ProcessPlayerCommand(payload);
            return true;
        }
        if (message.MessageId == RemoteUiActionHeaderCodec.Descriptor.Id)
        {
            _ = _ui!.TryProcessAction(payload);
            return true;
        }
        if (message.MessageId == RemoteUiApplyResultMessageCodec.Descriptor.Id)
        {
            _ = _ui!.TryProcessApplyResult(payload);
            return true;
        }
        if (_chatSession!.TryProcess(in message, payload)) return true;
        return _worldDelivery!.TryProcess(in message, payload);
    }

    public bool TryProgress()
    {
        EnsureOperational();
        if (_retirementRequested) return true;
        bool complete = QueueIdentity();
        if (!_identityQueued) return complete;
        if (_applicationFaulted) return complete;
        if (!_clientProfileApplied)
        {
            complete &= QueueClientProfile(force: false);
            return complete;
        }
        if (!_contentApplied)
        {
            complete &= QueueContentRequirement(force: false);
            return complete;
        }
        if (_admissionAttempt != 0 && !_admissionResultApplied)
            complete &= QueueAdmissionResult(force: false);
        if (!_guestAdmitted || !_admissionResultApplied || _applicationFaulted)
            return complete;

        complete &= QueuePublicPresentationMetadata(force: false);

        if (_sharedAuthority is null)
        {
            long now = Stopwatch.GetTimestamp();
            int steps = 0;
            while (now >= _nextTickTimestamp && steps < 4)
            {
                _runtime!.Tick();
                _authorityTicks++;
                _nextTickTimestamp = checked(_nextTickTimestamp + _tickInterval);
                steps++;
                _ = _worldDelivery!.ExpireSnapshotTransfer(_authorityTicks);
                DrivePublicWorld();
                _ = QueueOwnerState();
            }
            if (steps == 4 && now >= _nextTickTimestamp)
                _nextTickTimestamp = checked(now + _tickInterval);
        }
        else
        {
            ulong sharedTick = _runtime!.SimulationTick;
            if (sharedTick != _authorityTicks)
            {
                _authorityTicks = sharedTick;
                _ = _worldDelivery!.ExpireSnapshotTransfer(_authorityTicks);
                DrivePublicWorld();
                _ = QueueOwnerState();
            }
        }
        _ = _chatDelivery!.TryHandoffPendingOccurrences();
        complete &= _chatSession!.TryProgress();
        complete &= _worldDelivery!.TryProgress();
        return complete;
    }

    bool IHostedUdpSessionApplicationLifetime.RetirementRequested => _retirementRequested;

    public RemoteGuestPlayableServerSnapshot GetSnapshot()
    {
        EnsureOperational();
        return new RemoteGuestPlayableServerSnapshot(
            _identityQueued, _contentAuthorization is not null, _contentApplied,
            _guestAdmitted, _admissionResultApplied,
            _applicationSessionEpoch, _participant, _controlSession, _authorityTicks,
            _commandsApplied, _duplicateCommands, _rejectedCommands, _ownerStatesQueued,
            _contentRequirementsQueued, _duplicateContentProofs, _rejectedContentProofs,
            _rejectedPreContentMessages,
            _clientProfile is not null, _clientProfileApplied, _clientProfilesQueued,
            _duplicateClientProfileProofs, _rejectedClientProfileProofs,
            _rejectedPreProfileMessages,
            _publicPresentationApplied, _publicPresentationDeliveriesQueued,
            _duplicatePublicPresentationProofs, _rejectedPublicPresentationProofs,
            _worldDelivery?.GetSnapshot() ?? default,
            _chatDelivery?.GetSnapshot() ?? default);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        _chatSession?.Dispose();
        if (_sharedAuthority is null) _chatDelivery?.Dispose();
        // Connection retirement is terminal for this presentation lineage. Best-effort Close
        // messages are useful while transport accepts them, but backpressure or a vanished peer
        // must not turn terminal application cleanup into an owner-worker failure.
        try { _signUi?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        try { _inventoryUi?.Dispose(); }
        catch (Exception exception) when (exception is not OutOfMemoryException) { }
        _ui?.Dispose();
        if (_sharedAuthority is not null && _sharedParticipant.IsValid)
        {
            _ = _sharedAuthority.Leave(_sharedParticipant);
            _sharedParticipant = default;
        }
        else
        {
            _chat?.Dispose();
            _admission?.Dispose();
            if (_runtime is not null)
            {
                if (_runtime.State == ServerRuntimeState.Running) _runtime.Stop();
                _runtime.Dispose();
            }
        }
        _sharedAuthority?.UnregisterRemoteApplication(this);
        _chatSession = null;
        _chatDelivery = null;
        _signUi = null;
        _inventoryUi = null;
        _chat = null;
        _admission = null;
        _ui = null;
        _runtime = null;
        _composition = null;
        _worldPack = null;
        Array.Clear(_messageScratch);
        Array.Clear(_subscriptionChanges);
    }

    private bool QueueIdentity()
    {
        if (_identityQueued) return true;
        var identity = new RemoteApplicationSessionIdentityMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            ApplicationRealmHigh = _realm.High,
            ApplicationRealmLow = _realm.Low,
        };
        int bytes = RemoteApplicationSessionIdentityMessageCodec.EncodedSize;
        if (!RemoteApplicationSessionIdentityMessageCodec.TryWriteExact(
                _messageScratch.AsSpan(0, bytes), in identity))
            throw new InvalidOperationException("The application identity did not encode.");
        if (_peer.TryQueue(RemoteApplicationSessionIdentityMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, bytes)) != NetworkQueueStatus.Accepted)
            return false;
        _identityQueued = true;
        return true;
    }

    private bool QueueContentRequirement(bool force)
    {
        if (_contentApplied || _contentAuthorization is null) return true;
        long now = Environment.TickCount64;
        if (!force && _lastContentRequirementQueue != 0 &&
            now - _lastContentRequirementQueue < AdmissionRetryMilliseconds)
            return true;
        var requirement = new RemoteContentRequirement(
            _applicationSessionEpoch, 1, _contentAuthorization);
        if (!UltraSimGameProtocol.TryWriteContentRequirement(
                _messageScratch, in requirement, out int written))
            throw new InvalidOperationException("The retained content requirement became invalid.");
        if (_peer.TryQueue(RemoteContentRequirementHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        _lastContentRequirementQueue = now;
        SaturatingIncrement(ref _contentRequirementsQueued);
        return true;
    }

    private bool QueueClientProfile(bool force)
    {
        if (_clientProfileApplied || _clientProfile is null) return true;
        long now = Environment.TickCount64;
        if (!force && _lastClientProfileQueue != 0 &&
            now - _lastClientProfileQueue < AdmissionRetryMilliseconds)
            return true;
        if (!UltraSimGameProtocol.TryWriteClientProfile(
                _messageScratch, _applicationSessionEpoch, _clientProfile, out int written))
            throw new InvalidOperationException("The retained client profile became invalid.");
        if (_peer.TryQueue(RemoteClientProfileHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        _lastClientProfileQueue = now;
        SaturatingIncrement(ref _clientProfilesQueued);
        return true;
    }

    private void ProcessClientProfileProof(ReadOnlySpan<byte> payload)
    {
        if (_clientProfile is null ||
            !RemoteClientProfileAppliedMessageCodec.TryReadExact(
                payload, out RemoteClientProfileAppliedMessage proof) ||
            !UltraSimGameProtocol.IsValidClientProfileApplied(in proof) ||
            proof.ApplicationSessionEpoch != _applicationSessionEpoch ||
            proof.ProfileRevision != _clientProfile.Revision ||
            new RemoteClientProfileHash(
                proof.ProfileHash0, proof.ProfileHash1,
                proof.ProfileHash2, proof.ProfileHash3) != _clientProfile.Hash)
        {
            SaturatingIncrement(ref _rejectedClientProfileProofs);
            return;
        }
        if (proof.Status == RemoteClientProfileApplyStatus.Rejected)
        {
            if (_clientProfileApplied)
                SaturatingIncrement(ref _rejectedClientProfileProofs);
            else
                _applicationFaulted = true;
            return;
        }
        if (_clientProfileApplied)
        {
            SaturatingIncrement(ref _duplicateClientProfileProofs);
            return;
        }
        _clientProfileApplied = true;
    }

    private void ProcessContentProof(ReadOnlySpan<byte> payload)
    {
        if (_contentAuthorization is null ||
            !RemoteContentSetAppliedMessageCodec.TryReadExact(
                payload, out RemoteContentSetAppliedMessage proof) ||
            !UltraSimGameProtocol.IsValidContentApplied(in proof) ||
            proof.ApplicationSessionEpoch != _applicationSessionEpoch ||
            proof.RequirementSequence != 1 ||
            proof.ApplicationRealmHigh != _realm.High || proof.ApplicationRealmLow != _realm.Low ||
            proof.ActivationHigh != _contentAuthorization.ActivationHigh ||
            proof.ActivationLow != _contentAuthorization.ActivationLow ||
            new RemoteContentHash(
                proof.PlanHash0, proof.PlanHash1, proof.PlanHash2, proof.PlanHash3) !=
                _contentAuthorization.PlanHash)
        {
            SaturatingIncrement(ref _rejectedContentProofs);
            return;
        }
        if (proof.Status == RemoteContentApplyStatus.Rejected)
        {
            if (_contentApplied)
            {
                SaturatingIncrement(ref _rejectedContentProofs);
                return;
            }
            _applicationFaulted = true;
            return;
        }
        if (new RemoteContentHash(
                proof.ResolvedHash0, proof.ResolvedHash1,
                proof.ResolvedHash2, proof.ResolvedHash3) !=
            _contentAuthorization.ExpectedResolvedCatalogFingerprint)
        {
            SaturatingIncrement(ref _rejectedContentProofs);
            return;
        }
        if (_contentApplied)
        {
            SaturatingIncrement(ref _duplicateContentProofs);
            return;
        }
        _contentApplied = true;
    }

    private void ProcessGuestRequest(ReadOnlySpan<byte> payload)
    {
        if (!_contentApplied)
        {
            SaturatingIncrement(ref _rejectedPreContentMessages);
            return;
        }
        if (!UltraSimGameProtocol.TryReadGuestAdmissionRequest(
                payload, out RemoteGuestAdmissionRequestHeader header,
                out GameGuestAdmissionRequest request) ||
            header.ApplicationSessionEpoch != _applicationSessionEpoch)
            return;
        if (_admissionAttempt != 0)
        {
            if (header.AttemptSequence == _admissionAttempt)
                _ = QueueAdmissionResult(force: true);
            return;
        }
        _admissionAttempt = header.AttemptSequence;
        if (!GameGuestAdmissionPolicy.TryNormalize(
                request, out _, out AdmissionRejection validationRejection,
                out string? validationMessage))
        {
            _admissionResult = RejectedResult(header.AttemptSequence, validationRejection);
            _admissionResultText = validationMessage ?? "Guest admission was rejected.";
            _ = QueueAdmissionResult(force: true);
            return;
        }
        AdmissionResult result;
        try
        {
            result = InitializeGuest(in request);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            DisposeGameplayAfterFailedAdmission();
            _admissionResult = RejectedResult(
                header.AttemptSequence, AdmissionRejection.RuntimeNotRunning);
            _admissionResultText = $"The remote playable session could not start: {exception.Message}";
            _ = QueueAdmissionResult(force: true);
            return;
        }
        if (!result.IsEntered || !result.GuestIdentity.IsValid)
        {
            _admissionResult = RejectedResult(
                header.AttemptSequence,
                result.Rejection == AdmissionRejection.None
                    ? AdmissionRejection.EntityCreationFailed
                    : result.Rejection);
            _admissionResultText = result.Message ?? "Guest admission was rejected.";
            _ = QueueAdmissionResult(force: true);
            return;
        }
        _guestAdmitted = true;
        _guestIdentity = result.GuestIdentity;
        _admissionResult = new RemoteGuestAdmissionResultHeader
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            AttemptSequence = header.AttemptSequence,
            Status = RemoteGuestAdmissionStatus.Accepted,
            ParticipantId = _participant.Value,
            ControlSessionEpoch = _controlSession.Value,
            ServerGenerationId = _runtime!.ActiveGenerationId,
            PublicEntityIndex = _playerPublicId.Index,
            PublicEntityGeneration = _playerPublicId.Generation,
            Body = (byte)_guestIdentity.Body,
        };
        _admissionResultText = _guestIdentity.DisplayName!;
        _ = QueueAdmissionResult(force: true);
    }

    private AdmissionResult InitializeGuest(in GameGuestAdmissionRequest request)
    {
        if (_sharedAuthority is not null)
            return InitializeSharedGuest(in request);
        _worldPack = GamePlayableWorld.CreateConceptDemoWorldPack();
        _composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32),
            runtimeProfile: GameRuntimeProfile.Networked,
            baseWorldPack: _worldPack);
        _runtime = new ServerRuntime(
            _composition,
            new ServerRuntimeOptions(
                UltraSimGameComposition.CreateWorldOptions(
                    128,
                    new PersistenceIdentityOptions(new PersistentWorldId(0xF0_3B00UL)),
                    GameRuntimeProfile.Networked),
                WorldClockOptions.Default));
        _runtime.Initialize();
        _runtime.Start();
        _ui = new RemoteUiSemanticPort(
            _peer,
            _applicationSessionEpoch,
            contentBackedDefinitions: _contentBackedUiDefinitions,
            standardDefinitions: _standardUiDefinitions);
        _admission = new AdmissionService(_runtime.Game, _composition);
        AdmissionResult entered = _admission.JoinGuest(_participant, request);
        if (!entered.IsEntered) return entered;

        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
                (world, _, game) =>
                    GamePlayableWorld.PopulateConceptDemo(world, game, entered.Entity),
                out _entities))
            throw new InvalidOperationException("The remote Concept Demo could not populate.");
        _controlSession = GameControlSessionEpoch.Create();
        BindControl();
        _runtime.Tick();
        _authorityTicks++;

        _inventoryUi = new InventoryUiController(_runtime.Game, _ui);
        _signUi = new InformationalSignUiController(_ui);
        var chatLimits = GameChatLimits.Default with
        {
            MaximumParticipants = 1,
            MaximumRecipientsPerSpeech = 1,
        };
        _chat = new GameChatService(_runtime.Game, _admission, chatLimits);
        _chatDelivery = new RemoteChatDeliveryService(
            _runtime.Game,
            _chat,
            new RemoteChatServerLimits(1, 64, 64, 8, 2_000, 1_000));
        _chatSession = _chatDelivery.Register(
            _peer, _admission, _participant, _applicationSessionEpoch);

        ChunkPublicationStore publications = CapturePublications();
        _recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(
                MaximumSubscriptions, MaximumPublicationRecords, MaximumPublicationRecords),
            GameChunkInterestProfile.Default,
            OfflineDirectChunkCadence.EvidenceProfile);
        _worldDelivery = new RemoteChunkWorldStateServer(
            _peer, _recipient, publications, _applicationSessionEpoch,
            new RemoteChunkWorldStateTransportLimits(128, 64));
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, WorldPublicEntityId>(
                (world, _, game) =>
                {
                    Entity player = Resolve(world, _entities.PlayerId);
                    if (!game.WorldState.Publications.TryGetPublicEntityId(player, out WorldPublicEntityId id))
                        throw new InvalidOperationException("The admitted player has no public identity.");
                    return id;
                }, out _playerPublicId))
            throw new InvalidOperationException("The admitted player public identity was unavailable.");

        _tickInterval = Math.Max(1, Stopwatch.Frequency / _runtime.SimulationFrequencyHz);
        _nextTickTimestamp = checked(Stopwatch.GetTimestamp() + _tickInterval);
        return entered;
    }

    private AdmissionResult InitializeSharedGuest(in GameGuestAdmissionRequest request)
    {
        _worldPack = _sharedAuthority!.WorldPack;
        _composition = _sharedAuthority.Composition;
        _runtime = _sharedAuthority.Runtime;
        _admission = _sharedAuthority.Admission;
        _chat = _sharedAuthority.Chat;
        _chatDelivery = _sharedAuthority.ChatDelivery;
        AdmissionResult entered = _sharedAuthority.Join(
            _participant, in request, out _sharedParticipant);
        if (!entered.IsEntered) return entered;

        GamePlayableWorldScene scene = _sharedAuthority.Scene;
        GamePlayableParticipantEntities participant = _sharedParticipant.Entities;
        _entities = new GamePlayableWorldEntities(
            participant.Player,
            participant.Backpack,
            scene.CabinDoor,
            scene.CabinChest,
            scene.WoodlandAnimal,
            participant.Bread,
            participant.PlayerId,
            participant.BackpackId,
            scene.CabinDoorId,
            scene.CabinChestId,
            scene.WoodlandAnimalId,
            participant.BreadId,
            scene.SkyTelepad,
            scene.SkyTelepadId,
            scene.CentralFarGate,
            scene.CentralFarGateId,
            scene.FarReturnGate,
            scene.FarReturnGateId,
            scene.WelcomeSign,
            scene.WelcomeSignId,
            scene.FallbackLabSign,
            scene.FallbackLabSignId,
            scene.IronSwordSpawner,
            scene.IronSwordSpawnerId);
        _controlSession = _sharedParticipant.ControlSession;
        _playerPublicId = _sharedParticipant.PublicEntity;
        _ui = new RemoteUiSemanticPort(
            _peer,
            _applicationSessionEpoch,
            contentBackedDefinitions: _contentBackedUiDefinitions,
            standardDefinitions: _standardUiDefinitions);
        _inventoryUi = new InventoryUiController(_runtime.Game, _ui);
        _signUi = new InformationalSignUiController(_ui);
        _chatSession = _chatDelivery.Register(
            _peer, _participant, _applicationSessionEpoch);
        ChunkPublicationStore publications = CapturePublications();
        _recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(
                MaximumSubscriptions, MaximumPublicationRecords, MaximumPublicationRecords),
            GameChunkInterestProfile.Default,
            OfflineDirectChunkCadence.EvidenceProfile);
        _worldDelivery = new RemoteChunkWorldStateServer(
            _peer, _recipient, publications, _applicationSessionEpoch,
            new RemoteChunkWorldStateTransportLimits(128, 64));
        _authorityTicks = _runtime.SimulationTick;
        return entered;
    }

    private void ProcessGuestProof(ReadOnlySpan<byte> payload)
    {
        if (!RemoteGuestAdmissionAppliedMessageCodec.TryReadExact(
                payload, out RemoteGuestAdmissionAppliedMessage proof) ||
            proof.ApplicationSessionEpoch != _applicationSessionEpoch ||
            proof.AttemptSequence != _admissionAttempt ||
            proof.Status is < RemoteGuestAdmissionApplyStatus.Applied or
                > RemoteGuestAdmissionApplyStatus.Rejected)
            return;
        if (proof.Status == RemoteGuestAdmissionApplyStatus.Rejected)
        {
            _applicationFaulted = true;
            return;
        }
        _admissionResultApplied = true;
        if (!_guestAdmitted) return;
        _ = QueuePublicPresentationMetadata(force: true);
        _ = QueueOwnerState();
        DrivePublicWorld();
    }

    private bool QueuePublicPresentationMetadata(bool force)
    {
        bool complete = _sharedAuthority is not null
            ? QueueSharedPublicPresentationMetadata(force)
            : QueueOwnPublicPresentationMetadata(force);
        complete &= QueuePublicItemPresentationMetadata(force);
        return complete;
    }

    private bool QueueOwnPublicPresentationMetadata(bool force)
    {
        if (_publicPresentationApplied || !_guestAdmitted || !_admissionResultApplied ||
            _runtime is null || !_playerPublicId.IsValid || !_guestIdentity.IsValid)
            return true;
        long now = Environment.TickCount64;
        if (!force && _lastPublicPresentationQueue != 0 &&
            now - _lastPublicPresentationQueue < AdmissionRetryMilliseconds)
            return true;
        var header = new RemotePublicPresentationMetadataHeader
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            ServerGenerationId = _runtime.ActiveGenerationId,
            DeliveryId = 1,
            MetadataRevision = 1,
            PublicEntityIndex = _playerPublicId.Index,
            PublicEntityGeneration = _playerPublicId.Generation,
            Operation = RemotePublicPresentationOperation.Present,
            Flags = RemotePublicPresentationFlags.NameplateEligible,
        };
        if (!UltraSimGameProtocol.TryWritePublicPresentationMetadata(
                _messageScratch, in header, _guestIdentity.DisplayName!, null,
                out int written))
            throw new InvalidOperationException("The retained public presentation metadata became invalid.");
        if (_peer.TryQueue(RemotePublicPresentationMetadataHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        _lastPublicPresentationQueue = now;
        SaturatingIncrement(ref _publicPresentationDeliveriesQueued);
        return true;
    }

    private void ProcessPublicPresentationProof(ReadOnlySpan<byte> payload)
    {
        if (RemotePublicPresentationAppliedMessageCodec.TryReadExact(
                payload, out RemotePublicPresentationAppliedMessage itemProof) &&
            _publicItemPresentationDeliverySlot >= 0 &&
            itemProof.DeliveryId ==
                _publicItemPresentations[_publicItemPresentationDeliverySlot].DeliveryId)
        {
            ProcessPublicItemPresentationProof(in itemProof);
            return;
        }
        if (_sharedAuthority is not null)
        {
            ProcessSharedPublicPresentationProof(payload);
            return;
        }
        if (!_guestAdmitted || _runtime is null ||
            !RemotePublicPresentationAppliedMessageCodec.TryReadExact(
                payload, out RemotePublicPresentationAppliedMessage proof) ||
            !UltraSimGameProtocol.IsValidPublicPresentationApplied(in proof) ||
            proof.ApplicationSessionEpoch != _applicationSessionEpoch ||
            proof.ServerGenerationId != _runtime.ActiveGenerationId || proof.DeliveryId != 1)
        {
            SaturatingIncrement(ref _rejectedPublicPresentationProofs);
            return;
        }
        if (proof.Status != RemotePublicPresentationApplyStatus.Applied)
        {
            if (_publicPresentationApplied)
                SaturatingIncrement(ref _rejectedPublicPresentationProofs);
            else
                _applicationFaulted = true;
            return;
        }
        if (_publicPresentationApplied)
        {
            SaturatingIncrement(ref _duplicatePublicPresentationProofs);
            return;
        }
        _publicPresentationApplied = true;
    }

    private bool QueuePublicItemPresentationMetadata(bool force)
    {
        if (!_guestAdmitted || !_admissionResultApplied || _runtime is null ||
            _recipient is null)
            return true;
        ReconcilePublicItemPresentation();
        int slot = _publicItemPresentationDeliverySlot;
        if (slot < 0)
        {
            for (int index = 0; index < _publicItemPresentations.Length; index++)
            {
                if (!_publicItemPresentations[index].NeedsDelivery) continue;
                slot = index;
                ref SharedPublicPresentationRow selected =
                    ref _publicItemPresentations[index];
                if (_nextPublicItemPresentationDeliveryId == 0)
                    throw new InvalidOperationException(
                        "The public-item presentation delivery identity space is exhausted.");
                selected.DeliveryId = _nextPublicItemPresentationDeliveryId++;
                _publicItemPresentationDeliverySlot = index;
                break;
            }
        }
        if (slot < 0) return true;
        ref SharedPublicPresentationRow row = ref _publicItemPresentations[slot];
        if (row.DeliveryId == 0)
            throw new InvalidOperationException("The selected public-item delivery has no identity.");
        long now = Environment.TickCount64;
        if (!force && row.LastQueuedMilliseconds != 0 &&
            now - row.LastQueuedMilliseconds < AdmissionRetryMilliseconds)
            return true;
        var header = new RemotePublicPresentationMetadataHeader
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            ServerGenerationId = _runtime.ActiveGenerationId,
            DeliveryId = row.DeliveryId,
            MetadataRevision = row.Revision,
            PublicEntityIndex = row.PublicEntity.Index,
            PublicEntityGeneration = row.PublicEntity.Generation,
            Operation = row.DesiredPresent
                ? RemotePublicPresentationOperation.Present
                : RemotePublicPresentationOperation.Remove,
            Flags = row.DesiredPresent
                ? row.DesiredFlags
                : RemotePublicPresentationFlags.None,
        };
        string label = row.DesiredPresent ? row.DisplayLabel! : string.Empty;
        InspectionPropertySet? properties = row.DesiredPresent
            ? row.InspectionProperties
            : null;
        if (!UltraSimGameProtocol.TryWritePublicPresentationMetadata(
                _messageScratch, in header, label, properties, out int written))
            throw new InvalidOperationException(
                "The retained public-item presentation metadata became invalid.");
        if (_peer.TryQueue(RemotePublicPresentationMetadataHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        row.LastQueuedMilliseconds = now;
        SaturatingIncrement(ref _publicPresentationDeliveriesQueued);
        return true;
    }

    private void ProcessPublicItemPresentationProof(
        in RemotePublicPresentationAppliedMessage proof)
    {
        if (_publicItemPresentationDeliverySlot < 0 || _runtime is null)
        {
            SaturatingIncrement(ref _rejectedPublicPresentationProofs);
            return;
        }
        ref SharedPublicPresentationRow row =
            ref _publicItemPresentations[_publicItemPresentationDeliverySlot];
        if (
            !UltraSimGameProtocol.IsValidPublicPresentationApplied(in proof) ||
            proof.ApplicationSessionEpoch != _applicationSessionEpoch ||
            proof.ServerGenerationId != _runtime.ActiveGenerationId ||
            proof.DeliveryId != row.DeliveryId)
        {
            SaturatingIncrement(ref _rejectedPublicPresentationProofs);
            return;
        }
        if (proof.Status != RemotePublicPresentationApplyStatus.Applied)
        {
            _applicationFaulted = true;
            SaturatingIncrement(ref _rejectedPublicPresentationProofs);
            return;
        }
        row.AppliedPresent = row.DesiredPresent;
        row.AppliedPublicEntity = row.PublicEntity;
        row.AppliedFlags = row.DesiredPresent
            ? row.DesiredFlags
            : RemotePublicPresentationFlags.None;
        row.AppliedDisplayLabel = row.DesiredPresent ? row.DisplayLabel : null;
        row.AppliedInspectionProperties = row.DesiredPresent
            ? row.InspectionProperties
            : null;
        row.DeliveryId = 0;
        row.LastQueuedMilliseconds = 0;
        _publicItemPresentationDeliverySlot = -1;
    }

    private void ReconcilePublicItemPresentation()
    {
        if (_runtime is null) return;
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, int>(
                (world, _, game) => game.Items.CopyPublicWeaponPresentations(
                    world, game.WorldState, _publicItemScratch),
                out int desiredCount) || desiredCount < 0)
            throw new InvalidOperationException(
                "The public-item presentation source exceeded its bounded destination.");

        for (int slot = 0; slot < _publicItemPresentations.Length; slot++)
        {
            ref SharedPublicPresentationRow row = ref _publicItemPresentations[slot];
            if (row.DeliveryId != 0 || !row.PublicEntity.IsValid) continue;
            GamePublicWeaponPresentation desired = default;
            for (int item = 0; item < desiredCount; item++)
            {
                if (_publicItemScratch[item].Id != row.PublicEntity ||
                    !IsSubscribed(_publicItemScratch[item].Position))
                    continue;
                desired = _publicItemScratch[item];
                break;
            }
            if (!desired.IsValid)
            {
                if (!row.AppliedPresent)
                {
                    row = default;
                    continue;
                }
                if (row.DesiredPresent)
                {
                    row.DesiredPresent = false;
                    row.Revision = checked(row.Revision + 1);
                }
                continue;
            }
            if (WeaponsEqual(row.Weapon, desired.Weapon))
            {
                row.DesiredPresent = true;
                continue;
            }
            if (row.AppliedPresent)
            {
                if (row.DesiredPresent)
                {
                    row.DesiredPresent = false;
                    row.Revision = checked(row.Revision + 1);
                }
                continue;
            }
            row.Weapon = desired.Weapon;
            row.InspectionProperties = desired.InspectionProperties;
            row.Revision = checked(row.Revision + 1);
            row.DesiredPresent = true;
        }

        for (int item = 0; item < desiredCount; item++)
        {
            GamePublicWeaponPresentation desired = _publicItemScratch[item];
            if (!desired.IsValid || !IsSubscribed(desired.Position)) continue;
            bool exists = false;
            for (int slot = 0; slot < _publicItemPresentations.Length; slot++)
            {
                if (_publicItemPresentations[slot].PublicEntity != desired.Id) continue;
                exists = true;
                break;
            }
            if (exists) continue;
            int free = -1;
            for (int slot = 0; slot < _publicItemPresentations.Length; slot++)
            {
                if (_publicItemPresentations[slot].PublicEntity.IsValid) continue;
                free = slot;
                break;
            }
            if (free < 0)
                throw new InvalidOperationException(
                    "The bounded public-item presentation table is full.");
            _publicItemPresentations[free] = new SharedPublicPresentationRow
            {
                PublicEntity = desired.Id,
                DisplayLabel = desired.DisplayName,
                InspectionProperties = desired.InspectionProperties,
                DesiredFlags = RemotePublicPresentationFlags.ItemProperties,
                Weapon = desired.Weapon,
                Revision = 1,
                DesiredPresent = true,
            };
        }
    }

    private bool IsSubscribed(in WorldPosition position)
    {
        SpatialChunkKey key = new(GameWorldState.DefaultSpace, position.Chunk);
        OfflineDirectChunkSubscriptionView subscriptions = _recipient!.Subscriptions;
        for (int index = 0; index < subscriptions.Length; index++)
            if (subscriptions[index].Interest.Key == key) return true;
        return false;
    }

    private static bool WeaponsEqual(
        in GameWeaponInstance left,
        in GameWeaponInstance right) =>
        left.DamageMinimum == right.DamageMinimum &&
        left.DamageMaximum == right.DamageMaximum &&
        left.MaximumDurability == right.MaximumDurability &&
        left.CurrentDurability == right.CurrentDurability &&
        left.Quality == right.Quality &&
        left.Element == right.Element &&
        left.SpawnOrdinal == right.SpawnOrdinal;

    private bool QueueSharedPublicPresentationMetadata(bool force)
    {
        if (!_guestAdmitted || !_admissionResultApplied || _runtime is null ||
            _sharedPresentation is null || _sharedParticipantScratch is null)
            return true;

        ReconcileSharedPresentation();
        int slot = _sharedPresentationDeliverySlot;
        if (slot < 0)
        {
            for (int index = 0; index < _sharedPresentation.Length; index++)
            {
                if (!_sharedPresentation[index].NeedsDelivery) continue;
                slot = index;
                ref SharedPublicPresentationRow selected = ref _sharedPresentation[index];
                if (_nextSharedPresentationDeliveryId == 0)
                    throw new InvalidOperationException(
                        "The shared public-presentation delivery identity space is exhausted.");
                selected.DeliveryId = _nextSharedPresentationDeliveryId++;
                _sharedPresentationDeliverySlot = index;
                break;
            }
        }
        if (slot < 0)
        {
            _publicPresentationApplied = IsOwnPresentationApplied();
            return true;
        }

        ref SharedPublicPresentationRow row = ref _sharedPresentation[slot];
        long now = Environment.TickCount64;
        if (!force && row.LastQueuedMilliseconds != 0 &&
            now - row.LastQueuedMilliseconds < AdmissionRetryMilliseconds)
            return true;
        var header = new RemotePublicPresentationMetadataHeader
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            ServerGenerationId = _runtime.ActiveGenerationId,
            DeliveryId = row.DeliveryId,
            MetadataRevision = row.Revision,
            PublicEntityIndex = row.PublicEntity.Index,
            PublicEntityGeneration = row.PublicEntity.Generation,
            Operation = row.DesiredPresent
                ? RemotePublicPresentationOperation.Present
                : RemotePublicPresentationOperation.Remove,
            Flags = row.DesiredPresent
                ? row.DesiredFlags
                : RemotePublicPresentationFlags.None,
        };
        string label = row.DesiredPresent ? row.DisplayLabel! : string.Empty;
        if (!UltraSimGameProtocol.TryWritePublicPresentationMetadata(
                _messageScratch, in header, label, null, out int written))
            throw new InvalidOperationException(
                "The retained shared public-presentation metadata became invalid.");
        if (_peer.TryQueue(RemotePublicPresentationMetadataHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        row.LastQueuedMilliseconds = now;
        SaturatingIncrement(ref _publicPresentationDeliveriesQueued);
        return true;
    }

    private void ProcessSharedPublicPresentationProof(ReadOnlySpan<byte> payload)
    {
        if (!_guestAdmitted || _runtime is null || _sharedPresentation is null ||
            _sharedPresentationDeliverySlot < 0 ||
            !RemotePublicPresentationAppliedMessageCodec.TryReadExact(
                payload, out RemotePublicPresentationAppliedMessage proof) ||
            !UltraSimGameProtocol.IsValidPublicPresentationApplied(in proof) ||
            proof.ApplicationSessionEpoch != _applicationSessionEpoch ||
            proof.ServerGenerationId != _runtime.ActiveGenerationId)
        {
            SaturatingIncrement(ref _rejectedPublicPresentationProofs);
            return;
        }
        ref SharedPublicPresentationRow row =
            ref _sharedPresentation[_sharedPresentationDeliverySlot];
        if (proof.DeliveryId != row.DeliveryId)
        {
            SaturatingIncrement(ref _rejectedPublicPresentationProofs);
            return;
        }
        if (proof.Status != RemotePublicPresentationApplyStatus.Applied)
        {
            _applicationFaulted = true;
            SaturatingIncrement(ref _rejectedPublicPresentationProofs);
            return;
        }
        row.AppliedPresent = row.DesiredPresent;
        row.AppliedPublicEntity = row.PublicEntity;
        row.AppliedDisplayLabel = row.DesiredPresent ? row.DisplayLabel : null;
        row.AppliedFlags = row.DesiredPresent
            ? row.DesiredFlags
            : RemotePublicPresentationFlags.None;
        row.AppliedInspectionProperties = null;
        row.DeliveryId = 0;
        row.LastQueuedMilliseconds = 0;
        _sharedPresentationDeliverySlot = -1;
        _publicPresentationApplied = IsOwnPresentationApplied();
    }

    private void ReconcileSharedPresentation()
    {
        SharedPublicPresentationRow[] rows = _sharedPresentation!;
        int count = _sharedAuthority!.CopyParticipants(_sharedParticipantScratch!);
        if (count < 0)
            throw new InvalidOperationException("The shared participant census exceeded its bound.");
        for (int slot = 0; slot < rows.Length; slot++)
        {
            ref SharedPublicPresentationRow row = ref rows[slot];
            // A queued occurrence is immutable until its exact application proof arrives. A slot
            // may be reused while that proof is in flight; changing the retained operation here
            // would let an old delivery identity describe a newer participant (key ABA).
            if (row.DeliveryId != 0) continue;

            SharedPlayableParticipant desired = default;
            for (int index = 0; index < count; index++)
            {
                if (_sharedParticipantScratch![index].Slot != slot) continue;
                desired = _sharedParticipantScratch[index];
                break;
            }
            if (!desired.IsValid)
            {
                if (!row.PublicEntity.IsValid) continue;
                if (!row.AppliedPresent)
                {
                    row = default;
                    continue;
                }
                if (row.DesiredPresent)
                {
                    row.DesiredPresent = false;
                    row.Revision = checked(row.Revision + 1);
                }
                continue;
            }

            bool identityChanged = row.PublicEntity != desired.PublicEntity ||
                !string.Equals(row.DisplayLabel, desired.GuestIdentity.DisplayName,
                    StringComparison.Ordinal);
            if (!identityChanged)
            {
                row.DesiredPresent = true;
                continue;
            }
            if (row.AppliedPresent)
            {
                // Retire the exact older generational identity before publishing a newer occupant
                // of the bounded operational slot.
                if (row.DesiredPresent)
                {
                    row.DesiredPresent = false;
                    row.Revision = checked(row.Revision + 1);
                }
                continue;
            }
            row = new SharedPublicPresentationRow
            {
                PublicEntity = desired.PublicEntity,
                DisplayLabel = desired.GuestIdentity.DisplayName,
                DesiredFlags = RemotePublicPresentationFlags.NameplateEligible,
                Revision = 1,
                DesiredPresent = true,
            };
        }
    }

    private bool IsOwnPresentationApplied()
    {
        if (_sharedPresentation is null || !_sharedParticipant.IsValid) return false;
        ref readonly SharedPublicPresentationRow row =
            ref _sharedPresentation[_sharedParticipant.Slot];
        return row.AppliedPresent && row.AppliedPublicEntity == _playerPublicId &&
            string.Equals(row.AppliedDisplayLabel, _guestIdentity.DisplayName,
                StringComparison.Ordinal);
    }

    private bool QueueAdmissionResult(bool force)
    {
        if (_admissionResultApplied || _admissionResultText is null) return true;
        long now = Environment.TickCount64;
        if (!force && now - _lastAdmissionResultQueue < AdmissionRetryMilliseconds)
            return true;
        if (!UltraSimGameProtocol.TryWriteGuestAdmissionResult(
                _messageScratch, in _admissionResult, _admissionResultText, out int written))
            throw new InvalidOperationException("The retained guest admission result became invalid.");
        if (_peer.TryQueue(RemoteGuestAdmissionResultHeaderCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, written)) != NetworkQueueStatus.Accepted)
            return false;
        _lastAdmissionResultQueue = now;
        return true;
    }

    private void ProcessPlayerCommand(ReadOnlySpan<byte> payload)
    {
        if (!RemotePlayableCommandMessageCodec.TryReadExact(
                payload, out RemotePlayableCommandMessage remote) ||
            remote.ApplicationSessionEpoch != _applicationSessionEpoch ||
            !UltraSimGameProtocol.TryCreatePlayableCommand(
                in remote, out GamePlayerCommand command, out RemotePublicEntityTarget target) ||
            command.ControlSession != _controlSession)
        {
            SaturatingIncrement(ref _rejectedCommands);
            return;
        }
        if (command.Sequence <= _lastProcessedCommand)
        {
            SaturatingIncrement(ref _duplicateCommands);
            return;
        }
        // This lane is reliable and non-coalescible. Admit exactly the next semantic command so a
        // hostile future sequence cannot permanently poison this control-session lineage.
        if (_lastProcessedCommand == ulong.MaxValue ||
            command.Sequence != _lastProcessedCommand + 1)
        {
            SaturatingIncrement(ref _rejectedCommands);
            return;
        }
        if (!_runtime!.Game.TryUseActiveGeneration<IGameGenerationStateProvider, CommandOutcome>(
                (world, _, game) => ApplyCommand(world, game, in remote, in command, in target),
                out CommandOutcome outcome))
        {
            SaturatingIncrement(ref _rejectedCommands);
            return;
        }
        _lastProcessedCommand = command.Sequence;
        try { _lastCommandResult = ApplyPostCommandSemantic(outcome); }
        catch (RemoteUiTransportBackpressureException)
        {
            SaturatingIncrement(ref _uiBackpressureRejections);
            _lastCommandResult = outcome.Result with
            {
                Interaction = GameWorldActionStatus.InvariantViolation,
            };
        }
        SaturatingIncrement(ref _commandsApplied);
        _ = QueueOwnerState();
    }

    private CommandOutcome ApplyCommand(
        EcsWorld world,
        IGameGenerationStateProvider game,
        in RemotePlayableCommandMessage remote,
        in GamePlayerCommand command,
        in RemotePublicEntityTarget targetIdentity)
    {
        Entity player = Resolve(world, _entities.PlayerId);
        Entity target = Entity.Null;
        if (targetIdentity.IsValid)
            _ = game.WorldState.Publications.TryResolvePublicEntity(
                new WorldPublicEntityId(targetIdentity.Index, targetIdentity.Generation), out target);

        bool interactionPressed = (command.PressedActions & GamePlayerActions.Interact) != 0;
        bool chestInteraction = interactionPressed && target.IsValid &&
            game.WorldState.TryReadPublic(world, target, out WorldPublicDescriptor targetDescriptor) &&
            targetDescriptor.PrefabId == GameWorldPublicPrefabIds.WorldChest;
        bool swordInteraction = interactionPressed && target.IsValid &&
            game.WorldState.TryReadPublic(world, target, out targetDescriptor) &&
            targetDescriptor.PrefabId == GameWorldPublicPrefabIds.IronSword;
        GamePlayerCommand admitted = chestInteraction || swordInteraction
            ? command with
            {
                HeldActions = command.HeldActions & ~GamePlayerActions.Interact,
                PressedActions = command.PressedActions & ~GamePlayerActions.Interact,
            }
            : command;
        game.WorldActions.ApplyPlayerCommand(
            world, game.WorldState, player, admitted, target, out GamePlayerCommandResult result);

        if (result.WasAdmitted &&
            (remote.Flags & RemotePlayableCommandFlags.FaceRequested) != 0 &&
            ((remote.Flags & RemotePlayableCommandFlags.FacingRequiresAcceptedLocomotion) == 0 ||
             result.Locomotion == GameWorldActionStatus.Accepted))
        {
            _ = game.WorldActions.FaceActorToward(
                world, game.WorldState, player,
                remote.FaceXMillimeters, remote.FaceZMillimeters, out _);
        }

        if (chestInteraction)
            result = result with { Interaction = ToggleBreadContainer(world, game) };
        else if (swordInteraction)
            result = result with
            {
                Interaction = game.Items.TryPickup(
                    world,
                    game.WorldState,
                    game.Inventory,
                    player,
                    target,
                    Resolve(world, _entities.BackpackId)),
            };
        else if (target.IsValid && result.Interaction == GameWorldActionStatus.Accepted &&
                 game.WorldState.TryReadPublic(
                     world, target, out WorldPublicDescriptor descriptor) &&
                 descriptor.PrefabId == GameWorldPublicPrefabIds.InformationalSign)
        {
            PersistentEntityId targetId = world.GetOrAssignPersistentId(target);
            return new CommandOutcome(
                result,
                targetId == _entities.FallbackLabSignId
                    ? PostCommandSemantic.FallbackLabSign
                    : PostCommandSemantic.WelcomeSign);
        }

        if ((command.PressedActions & GamePlayerActions.Primary) != 0)
            result = result with { Primary = game.Items.TryUsePrimary(world, game.Inventory, player) };

        if (result.WasAdmitted &&
            (remote.Flags & RemotePlayableCommandFlags.OpenInventoryRequested) != 0)
            return new CommandOutcome(result, PostCommandSemantic.Inventory);
        return new CommandOutcome(result, PostCommandSemantic.None);
    }

    private GamePlayerCommandResult ApplyPostCommandSemantic(in CommandOutcome outcome)
    {
        switch (outcome.Semantic)
        {
            case PostCommandSemantic.None:
                return outcome.Result;
            case PostCommandSemantic.WelcomeSign:
            case PostCommandSemantic.FallbackLabSign:
            {
                InformationalSignUiOpenResult opened = _signUi!.Open(
                    outcome.Semantic == PostCommandSemantic.FallbackLabSign
                        ? GameInformationalSigns.FallbackLab
                        : GameInformationalSigns.ConceptDemoWelcome);
                return opened.Succeeded
                    ? outcome.Result
                    : outcome.Result with
                    {
                        Interaction = GameWorldActionStatus.InvariantViolation,
                    };
            }
            case PostCommandSemantic.Inventory:
            {
                if (!_runtime!.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
                        InventoryOpenEntities>(
                        (world, _, _) => new InventoryOpenEntities(
                            Resolve(world, _entities.BackpackId),
                            Resolve(world, _entities.PlayerId)),
                        out InventoryOpenEntities entities))
                {
                    return outcome.Result with
                    {
                        Interaction = GameWorldActionStatus.InvariantViolation,
                    };
                }
                InventoryUiOpenResult opened = _inventoryUi!.OpenOwned(
                    entities.Container, entities.Owner);
                return outcome.Result with
                {
                    Interaction = opened.Succeeded
                        ? GameWorldActionStatus.Accepted
                        : GameWorldActionStatus.InvariantViolation,
                };
            }
            default:
                throw new InvalidOperationException("The post-command semantic kind is invalid.");
        }
    }

    private GameWorldActionStatus ToggleBreadContainer(
        EcsWorld world,
        IGameGenerationStateProvider game)
    {
        Entity player = Resolve(world, _entities.PlayerId);
        Entity bread = Resolve(world, _entities.BreadId);
        Entity backpack = Resolve(world, _entities.BackpackId);
        Entity chest = Resolve(world, _entities.CabinChestId);
        if (!game.Inventory.TryReadItem(world, bread, out InventoryItem item))
            return GameWorldActionStatus.MissingTarget;
        Entity destination = item.Parent == backpack ? chest : backpack;
        return game.WorldActions.MoveItemToNearbyContainer(
            world, game.WorldState, game.Inventory,
            player, bread, item.Parent, destination, out _);
    }

    private void DrivePublicWorld()
    {
        if (!_runtime!.Game.TryUseActiveGeneration<IGameGenerationStateProvider, WorldPosition>(
                (world, _, game) =>
                {
                    Entity player = Resolve(world, _entities.PlayerId);
                    if (!game.WorldState.TryReadPosition(world, player, out WorldPosition position))
                        throw new InvalidOperationException("The remote public anchor was unavailable.");
                    return position;
                }, out WorldPosition anchor))
            throw new InvalidOperationException("The remote public authority was unavailable.");
        OfflineDirectChunkReconcileStatus reconcile = _worldDelivery!.Reconcile(
            GameWorldState.DefaultSpace, anchor, _subscriptionChanges, out _);
        if (reconcile != OfflineDirectChunkReconcileStatus.Reconciled)
            throw new InvalidOperationException($"Remote public interest failed: {reconcile}.");
        OfflineDirectChunkSubscriptionView subscriptions = _recipient!.Subscriptions;
        int scheduled = 0;
        int examined = 0;
        if (_worldScheduleCursor >= subscriptions.Length) _worldScheduleCursor = 0;
        while (examined < subscriptions.Length && scheduled < 16)
        {
            // Bulk public-state snapshots must leave bounded reliable headroom for owner state,
            // UI, Chat and other control-plane semantics on this same application session.
            if (_peer.ReliableQueued >=
                Math.Max(1, _peer.MaximumReliableMessages - ReservedReliableControlMessages))
                break;
            int index = _worldScheduleCursor;
            _worldScheduleCursor = (_worldScheduleCursor + 1) % subscriptions.Length;
            examined++;
            // Public disclosure does not manufacture structural residency. F0.3b's preinstalled
            // Concept Demo consumer stages the canonical authored WorldPack; unauthored spatial
            // keys have neither structural pages nor a client semantic destination. Later remote
            // structural acquisition will explicitly coordinate the same authored-key boundary.
            if (!_worldPack!.TryGetBaseTerrain(subscriptions[index].Interest.Key, out _))
                continue;
            RemoteChunkWorldStateScheduleStatus status =
                _worldDelivery.TrySchedule(index, _authorityTicks);
            if (status is RemoteChunkWorldStateScheduleStatus.QueuedDelta or
                RemoteChunkWorldStateScheduleStatus.QueuedAbsence or
                RemoteChunkWorldStateScheduleStatus.SnapshotTransferStarted)
                scheduled++;
            else if (status is RemoteChunkWorldStateScheduleStatus.TransportNotAccepting or
                     RemoteChunkWorldStateScheduleStatus.CapacityExceeded or
                     RemoteChunkWorldStateScheduleStatus.SnapshotTransferBusy)
                break;
        }
    }

    private bool QueueOwnerState()
    {
        if (!_guestAdmitted || !_admissionResultApplied || _runtime is null ||
            _ownerStateSequence == ulong.MaxValue)
            return false;
        if (!_runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, OwnerCapture>(
                (world, generation, game) =>
                {
                    Entity player = Resolve(world, _entities.PlayerId);
                    if (!game.WorldState.TryReadPosition(world, player, out WorldPosition position) ||
                        !game.WorldState.TryReadPublic(
                            world, player, out WorldPublicDescriptor descriptor) ||
                        !game.WorldState.Publications.TryGetPublicEntityId(
                            player, out WorldPublicEntityId publicId))
                        throw new InvalidOperationException("The owner-private actor state was incomplete.");
                    return new OwnerCapture(generation, world.TickCount, position, descriptor, publicId);
                }, out OwnerCapture capture))
            return false;
        if (_ownerStateSequence != 0 &&
            capture.Generation == _lastOwnerStateGeneration &&
            capture.Position == _lastOwnerStatePosition &&
            capture.Descriptor.Orientation == _lastOwnerStateOrientation &&
            _lastProcessedCommand == _lastOwnerStateCommandSequence)
            return true;
        ulong sequence = _ownerStateSequence + 1;
        var state = new RemotePlayableOwnerStateMessage
        {
            ApplicationSessionEpoch = _applicationSessionEpoch,
            ControlSessionEpoch = _controlSession.Value,
            ServerGenerationId = capture.Generation,
            StateSequence = sequence,
            SimulationTick = capture.SimulationTick,
            LastProcessedCommandSequence = _lastProcessedCommand,
            PublicEntityIndex = capture.PublicId.Index,
            PublicEntityGeneration = capture.PublicId.Generation,
            GlobalXMillimeters = capture.Position.GlobalXMillimeters,
            YMillimeters = capture.Position.YMillimeters,
            GlobalZMillimeters = capture.Position.GlobalZMillimeters,
            OrientationUnits = capture.Descriptor.Orientation.Units,
            Admission = Map(_lastCommandResult.Admission),
            Locomotion = Map(_lastCommandResult.Locomotion),
            Jump = Map(_lastCommandResult.Jump),
            Primary = Map(_lastCommandResult.Primary),
            Interaction = Map(_lastCommandResult.Interaction),
            Body = (byte)_guestIdentity.Body,
        };
        int bytes = RemotePlayableOwnerStateMessageCodec.EncodedSize;
        if (!RemotePlayableOwnerStateMessageCodec.TryWriteExact(
                _messageScratch.AsSpan(0, bytes), in state))
            throw new InvalidOperationException("The owner state did not encode.");
        if (_peer.TryQueue(RemotePlayableOwnerStateMessageCodec.Descriptor.Id,
                _messageScratch.AsSpan(0, bytes)) != NetworkQueueStatus.Accepted)
            return false;
        _ownerStateSequence = sequence;
        _lastOwnerStateSimulationTick = capture.SimulationTick;
        _lastOwnerStateCommandSequence = _lastProcessedCommand;
        _lastOwnerStateGeneration = capture.Generation;
        _lastOwnerStatePosition = capture.Position;
        _lastOwnerStateOrientation = capture.Descriptor.Orientation;
        SaturatingIncrement(ref _ownerStatesQueued);
        return true;
    }

    private void BindControl()
    {
        if (!_runtime!.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
                (world, _, game) => game.WorldActions.BeginControlSession(
                    world, Resolve(world, _entities.PlayerId), _controlSession),
                out GameWorldActionStatus status) || status != GameWorldActionStatus.Accepted)
            throw new InvalidOperationException($"The remote control session could not bind: {status}.");
    }

    private ChunkPublicationStore CapturePublications()
    {
        if (!_runtime!.Game.TryUseActiveGeneration<IGameGenerationStateProvider, ChunkPublicationStore>(
                static (_, _, game) => game.WorldState.Publications,
                out ChunkPublicationStore? publications))
            throw new InvalidOperationException("The remote publication source was unavailable.");
        return publications;
    }

    private static RemotePlayableActionStatus Map(GameWorldActionStatus status) => status switch
    {
        GameWorldActionStatus.NotRequested => RemotePlayableActionStatus.NotRequested,
        GameWorldActionStatus.Accepted => RemotePlayableActionStatus.Accepted,
        GameWorldActionStatus.InvalidCommand => RemotePlayableActionStatus.InvalidCommand,
        GameWorldActionStatus.StaleCommand => RemotePlayableActionStatus.StaleCommand,
        GameWorldActionStatus.StaleControlSession => RemotePlayableActionStatus.StaleControlSession,
        GameWorldActionStatus.MissingActor => RemotePlayableActionStatus.MissingActor,
        GameWorldActionStatus.MissingTarget => RemotePlayableActionStatus.MissingTarget,
        GameWorldActionStatus.UnauthorizedContainer => RemotePlayableActionStatus.UnauthorizedContainer,
        GameWorldActionStatus.OutOfRange => RemotePlayableActionStatus.OutOfRange,
        GameWorldActionStatus.OutsidePreparedWorld => RemotePlayableActionStatus.OutsidePreparedWorld,
        GameWorldActionStatus.InvariantViolation => RemotePlayableActionStatus.InvariantViolation,
        GameWorldActionStatus.Blocked => RemotePlayableActionStatus.Blocked,
        GameWorldActionStatus.Unsupported => RemotePlayableActionStatus.Unsupported,
        GameWorldActionStatus.NotGrounded => RemotePlayableActionStatus.NotGrounded,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    private RemoteGuestAdmissionResultHeader RejectedResult(
        ulong attempt,
        AdmissionRejection rejection) => new()
    {
        ApplicationSessionEpoch = _applicationSessionEpoch,
        AttemptSequence = attempt,
        Status = RemoteGuestAdmissionStatus.Rejected,
        Rejection = (byte)rejection,
    };

    private void DisposeGameplayAfterFailedAdmission()
    {
        try { _chatSession?.Dispose(); } catch { }
        if (_sharedAuthority is null)
            try { _chatDelivery?.Dispose(); } catch { }
        try { _signUi?.Dispose(); } catch { }
        try { _inventoryUi?.Dispose(); } catch { }
        try { _ui?.Dispose(); } catch { }
        if (_sharedAuthority is not null)
        {
            try
            {
                if (_sharedParticipant.IsValid)
                    _ = _sharedAuthority.Leave(_sharedParticipant);
            }
            catch { }
            _sharedParticipant = default;
        }
        else
        {
            try { _chat?.Dispose(); } catch { }
            try { _admission?.Dispose(); } catch { }
            try
            {
                if (_runtime?.State == ServerRuntimeState.Running) _runtime.Stop();
                _runtime?.Dispose();
            }
            catch { }
        }
        _chatSession = null;
        _chatDelivery = null;
        _signUi = null;
        _inventoryUi = null;
        _chat = null;
        _admission = null;
        _ui = null;
        _runtime = null;
    }

    private static Entity Resolve(EcsWorld world, PersistentEntityId id) =>
        world.TryResolvePersistentEntity(id, out Entity entity)
            ? entity
            : throw new InvalidOperationException($"Persistent entity {id} was unavailable.");

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOperational()
    {
        EnsureOwner();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The remote guest application is owner-thread-bound.");
    }

    private enum PostCommandSemantic : byte
    {
        None = 0,
        Inventory = 1,
        WelcomeSign = 2,
        FallbackLabSign = 3,
    }

    private readonly record struct CommandOutcome(
        GamePlayerCommandResult Result,
        PostCommandSemantic Semantic);

    private struct SharedPublicPresentationRow
    {
        internal WorldPublicEntityId PublicEntity;
        internal WorldPublicEntityId AppliedPublicEntity;
        internal string? DisplayLabel;
        internal string? AppliedDisplayLabel;
        internal InspectionPropertySet? InspectionProperties;
        internal InspectionPropertySet? AppliedInspectionProperties;
        internal RemotePublicPresentationFlags DesiredFlags;
        internal RemotePublicPresentationFlags AppliedFlags;
        internal GameWeaponInstance Weapon;
        internal ulong Revision;
        internal ulong DeliveryId;
        internal long LastQueuedMilliseconds;
        internal bool DesiredPresent;
        internal bool AppliedPresent;

        internal readonly bool NeedsDelivery => PublicEntity.IsValid && DeliveryId == 0 &&
            (DesiredPresent != AppliedPresent ||
             DesiredPresent && (AppliedPublicEntity != PublicEntity ||
                AppliedFlags != DesiredFlags ||
                !string.Equals(AppliedDisplayLabel, DisplayLabel, StringComparison.Ordinal) ||
                !Equals(AppliedInspectionProperties, InspectionProperties)));
    }

    private readonly record struct InventoryOpenEntities(Entity Container, Entity Owner);

    private readonly record struct OwnerCapture(
        ulong Generation,
        ulong SimulationTick,
        WorldPosition Position,
        WorldPublicDescriptor Descriptor,
        WorldPublicEntityId PublicId);
}
