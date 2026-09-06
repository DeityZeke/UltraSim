using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Application;
using UltraSim.Game.Chat;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Inventory;
using UltraSim.Game.Protocol;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;

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
    private readonly ApplicationRealmId _realm;
    private readonly RemoteContentAuthorization? _contentAuthorization;
    private long _nextParticipant;

    public RemoteGuestPlayableApplicationFactory(
        ApplicationRealmId realm,
        RemoteContentAuthorization? contentAuthorization = null)
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
    }

    public IHostedUdpSessionApplication Create(ConnectionId connection, NetworkProtocolPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        long value = Interlocked.Increment(ref _nextParticipant);
        if (value <= 0)
            throw new InvalidOperationException("The remote participant identity space is exhausted.");
        return new RemoteGuestPlayableApplication(
            peer, _realm, new ParticipantId(checked((ulong)value)), CreateEpoch(), _contentAuthorization);
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
}

/// <summary>
/// One owner-thread external Concept Demo application. Game owns guest admission, control,
/// Physics, public disclosure, Chat, Inventory and informational UI; Network only carries their
/// existing bounded semantic messages.
/// </summary>
public sealed class RemoteGuestPlayableApplication : IHostedUdpSessionApplication
{
    private const int MaximumPublicationRecords = 64;
    private const int MaximumSubscriptions = 1_024;
    private const int MaximumTopologyChanges = MaximumSubscriptions * 2;
    private const long AdmissionRetryMilliseconds = 1_000;

    private readonly NetworkProtocolPeer _peer;
    private readonly ApplicationRealmId _realm;
    private readonly RemoteContentAuthorization? _contentAuthorization;
    private readonly ParticipantId _participant;
    private readonly ulong _applicationSessionEpoch;
    private readonly byte[] _messageScratch = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly OfflineDirectChunkSubscriptionChange[] _subscriptionChanges =
        new OfflineDirectChunkSubscriptionChange[MaximumTopologyChanges];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private bool _identityQueued;
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
    private GameControlSessionEpoch _controlSession;
    private GameGuestIdentity _guestIdentity;
    private WorldPublicEntityId _playerPublicId;
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
    private GamePlayableWorldEntities _entities;
    private GamePlayerCommandResult _lastCommandResult;
    private ulong _lastProcessedCommand;
    private ulong _authorityTicks;
    private ulong _ownerStateSequence;
    private ulong _commandsApplied;
    private ulong _duplicateCommands;
    private ulong _rejectedCommands;
    private ulong _ownerStatesQueued;
    private long _nextTickTimestamp;
    private long _tickInterval;
    private bool _disposed;

    public RemoteGuestPlayableApplication(
        NetworkProtocolPeer peer,
        ApplicationRealmId realm,
        ParticipantId participant,
        ulong applicationSessionEpoch,
        RemoteContentAuthorization? contentAuthorization = null)
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
        _contentApplied = contentAuthorization is null;
    }

    public bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload)
    {
        EnsureOperational();
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
        bool complete = QueueIdentity();
        if (!_identityQueued) return complete;
        if (_applicationFaulted) return complete;
        if (!_contentApplied)
        {
            complete &= QueueContentRequirement(force: false);
            return complete;
        }
        if (_admissionAttempt != 0 && !_admissionResultApplied)
            complete &= QueueAdmissionResult(force: false);
        if (!_guestAdmitted || !_admissionResultApplied || _applicationFaulted)
            return complete;

        long now = Stopwatch.GetTimestamp();
        int steps = 0;
        while (now >= _nextTickTimestamp && steps < 4)
        {
            _runtime!.Tick();
            _authorityTicks++;
            _nextTickTimestamp = checked(_nextTickTimestamp + _tickInterval);
            steps++;
            DrivePublicWorld();
            _ = QueueOwnerState();
        }
        if (steps == 4 && now >= _nextTickTimestamp)
            _nextTickTimestamp = checked(now + _tickInterval);
        _ = _chatDelivery!.TryHandoffPendingSpeech();
        complete &= _chatSession!.TryProgress();
        complete &= _worldDelivery!.TryProgress();
        return complete;
    }

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
            _worldDelivery?.GetSnapshot() ?? default,
            _chatDelivery?.GetSnapshot() ?? default);
    }

    public void Dispose()
    {
        EnsureOwner();
        if (_disposed) return;
        _disposed = true;
        _chatSession?.Dispose();
        _chatDelivery?.Dispose();
        _signUi?.Dispose();
        _inventoryUi?.Dispose();
        _chat?.Dispose();
        _admission?.Dispose();
        _ui?.Dispose();
        if (_runtime is not null)
        {
            if (_runtime.State == ServerRuntimeState.Running) _runtime.Stop();
            _runtime.Dispose();
        }
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
        _ui = new RemoteUiSemanticPort(_peer, _applicationSessionEpoch);
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
            new RemoteChunkWorldStateTransportLimits(128));
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
        _ = QueueOwnerState();
        DrivePublicWorld();
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
        _lastCommandResult = ApplyPostCommandSemantic(outcome);
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
        GamePlayerCommand admitted = chestInteraction
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
        for (int index = 0; index < subscriptions.Length && scheduled < 16; index++)
        {
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
        try { _chatDelivery?.Dispose(); } catch { }
        try { _signUi?.Dispose(); } catch { }
        try { _inventoryUi?.Dispose(); } catch { }
        try { _chat?.Dispose(); } catch { }
        try { _admission?.Dispose(); } catch { }
        try { _ui?.Dispose(); } catch { }
        try
        {
            if (_runtime?.State == ServerRuntimeState.Running) _runtime.Stop();
            _runtime?.Dispose();
        }
        catch { }
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

    private readonly record struct InventoryOpenEntities(Entity Container, Entity Owner);

    private readonly record struct OwnerCapture(
        ulong Generation,
        ulong SimulationTick,
        WorldPosition Position,
        WorldPublicDescriptor Descriptor,
        WorldPublicEntityId PublicId);
}
