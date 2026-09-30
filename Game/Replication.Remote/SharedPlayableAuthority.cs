using System.Diagnostics;
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
using UltraSim.UI;

namespace UltraSim.Game.Replication;

/// <summary>Bounded ownership profile for one shared Concept Demo authority.</summary>
public readonly record struct SharedPlayableAuthorityLimits(int MaximumParticipants = 8)
{
    private const int BaseWorldEntityCapacity = 256;
    private const int EntitiesPerParticipant = 3;

    public static SharedPlayableAuthorityLimits Default => new(8);

    /// <summary>
    /// Reserves the fixed Concept Demo/content headroom plus the player, backpack, and starter
    /// item owned by every admitted participant. This makes the advertised participant limit a
    /// real construction bound instead of leaving the ECS world at the old eight-player size.
    /// </summary>
    public int RequiredEntityCapacity => checked(
        BaseWorldEntityCapacity + MaximumParticipants * EntitiesPerParticipant);

    internal InventoryLimits RequiredInventoryLimits
    {
        get
        {
            int participantHeadroom = checked(MaximumParticipants + 16);
            return new InventoryLimits(
                Math.Max(32, participantHeadroom),
                Math.Max(128, participantHeadroom),
                32);
        }
    }

    public void Validate()
    {
        if (MaximumParticipants is <= 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(MaximumParticipants));
    }
}

/// <summary>One admitted participant binding inside the shared authoritative generation.</summary>
public readonly record struct SharedPlayableParticipant(
    ParticipantId Participant,
    GameGuestIdentity GuestIdentity,
    GameControlSessionEpoch ControlSession,
    GamePlayableParticipantEntities Entities,
    WorldPublicEntityId PublicEntity,
    int Slot)
{
    public bool IsValid => Participant.IsValid && GuestIdentity.IsValid && ControlSession.IsValid &&
        Entities.IsValid && PublicEntity.IsValid && Slot >= 0;
}

/// <summary>Finite public action vocabulary retained only for actor presentation.</summary>
public enum GamePublicActorActionKind : byte
{
    None = 0,
    Interact = 1,
    Primary = 2,
}

/// <summary>Finite authoritative vertical state disclosed for actor presentation.</summary>
public enum GamePublicActorVerticalPhase : byte
{
    Grounded = 1,
    Rising = 2,
    Falling = 3,
}

/// <summary>
/// Bounded public actor presentation read model. MainHand identifies only the public prefab to
/// display; private item rolls and Inventory authority never enter this projection.
/// </summary>
public readonly record struct GamePublicActorPresentation(
    SharedPlayableParticipant Participant,
    ulong Revision,
    uint MainHandPrefabId,
    GamePublicActorVerticalPhase VerticalPhase,
    GamePublicActorActionKind Action,
    ulong ActionSequence)
{
    public bool IsValid => Participant.IsValid && Revision != 0 &&
        VerticalPhase is >= GamePublicActorVerticalPhase.Grounded and
            <= GamePublicActorVerticalPhase.Falling &&
        Action is >= GamePublicActorActionKind.None and <= GamePublicActorActionKind.Primary &&
        (Action == GamePublicActorActionKind.None ? ActionSequence == 0 : ActionSequence != 0);
}

/// <summary>
/// Owner-thread, slot-indexed view of one prepared authoritative actor-presentation revision.
/// Recipient delivery and proof state never enter this shared source view.
/// </summary>
internal readonly struct SharedActorPresentationView
{
    private readonly GamePublicActorPresentation[]? _rows;

    internal SharedActorPresentationView(
        GamePublicActorPresentation[] rows,
        ulong version,
        int count)
    {
        _rows = rows;
        Version = version;
        Count = count;
    }

    internal ulong Version { get; }
    internal int Count { get; }
    internal int Capacity => _rows?.Length ?? 0;

    internal bool TryGet(int slot, out GamePublicActorPresentation presentation)
    {
        if (_rows is null || (uint)slot >= (uint)_rows.Length ||
            !_rows[slot].IsValid)
        {
            presentation = default;
            return false;
        }
        presentation = _rows[slot];
        return true;
    }
}

/// <summary>Bounded owner-local evidence for shared actor-presentation preparation.</summary>
internal readonly record struct SharedActorPresentationCensus(
    ulong SourceVersion,
    ulong ViewReads,
    ulong Preparations,
    ulong ParticipantRecordsExamined,
    ulong ChangedRecords,
    ulong ExplicitInvalidations);

/// <summary>
/// Owner-thread immutable-until-next-preparation view of the bounded public ground-item source.
/// Recipient interest, delivery identity, and applied proof never enter this shared source.
/// </summary>
internal readonly struct SharedPublicItemPresentationView
{
    private readonly GamePublicWeaponPresentation[]? _rows;

    internal SharedPublicItemPresentationView(
        GamePublicWeaponPresentation[] rows,
        ulong version,
        int count)
    {
        _rows = rows;
        Version = version;
        Count = count;
    }

    internal ulong Version { get; }
    internal int Count { get; }

    internal GamePublicWeaponPresentation this[int index]
    {
        get
        {
            if (_rows is null || (uint)index >= (uint)Count)
                throw new IndexOutOfRangeException();
            return _rows[index];
        }
    }
}

internal readonly record struct SharedPublicItemPresentationCensus(
    ulong SourceVersion,
    ulong ViewReads,
    ulong Preparations,
    ulong SourceRecordsCopied,
    ulong ChangedPreparations,
    ulong ExplicitInvalidations);

internal enum SharedActorPresentationPreparationFault : byte
{
    None = 0,
    ReturnFalse = 1,
    Throw = 2,
}

/// <summary>Read-only evidence for one shared Game/World authority.</summary>
public readonly record struct SharedPlayableAuthoritySnapshot(
    ulong GenerationId,
    ulong SimulationTick,
    int ParticipantCount,
    int MaximumParticipants,
    ulong Joins,
    ulong Leaves,
    ulong Ticks,
    bool OwnsRuntime,
    GameTickTimingSnapshot Timing,
    SharedRemoteReplicationCensusSnapshot RemoteReplication);

public readonly record struct SharedDirtyFanoutBuckets(
    ulong Zero,
    ulong One,
    ulong TwoToFour,
    ulong FiveToEight,
    ulong NineToSixteen,
    ulong SeventeenToThirtyTwo,
    ulong ThirtyThreeToSixtyFour,
    ulong SixtyFiveOrMore);

/// <summary>Detailed-only authority-wide attribution; it never participates in disclosure.</summary>
public readonly record struct SharedRemoteReplicationCensusSnapshot(
    GameTelemetryLevel Level,
    ulong SourceCommitsObserved,
    ulong SourceCommitGaps,
    ulong DirtyChunksObserved,
    ulong DirtyRecipientFanout,
    GameValueDistribution RecipientsPerDirtyChunk,
    SharedDirtyFanoutBuckets FanoutBuckets,
    int CurrentRemoteRecipients,
    int CurrentUniqueSharedChunks,
    int CurrentMemberships,
    GameValueDistribution SubscriptionsPerRecipient,
    GameValueDistribution ImmediateSubscriptionsPerRecipient,
    GameValueDistribution NearSubscriptionsPerRecipient,
    GameValueDistribution FarSubscriptionsPerRecipient,
    GameValueDistribution RecipientsPerChunk,
    int CurrentOccupiedChunks,
    int CurrentMultiplySubscribedChunks);

public enum GameTestMarkerPhase : byte
{
    Start = 1,
    Complete = 2,
}

public readonly record struct GameTestMarkerResult(
    bool Accepted,
    ulong Sequence,
    GameTestMarkerPhase Phase,
    string? Label,
    string? Message);

/// <summary>
/// One owner-thread Game generation and Concept Demo world serving several independent participant
/// and control lineages. Network and local adapters borrow this authority; neither owns its world.
/// </summary>
public sealed class SharedPlayableAuthority : IDisposable
{
    internal const int MaximumPublicItemPresentations = 64;
    private readonly SharedPlayableAuthorityLimits _limits;
    private readonly SharedPlayableParticipant[] _participants;
    private readonly bool[] _occupied;
    private readonly bool[] _externallyOwned;
    private readonly RemoteGuestPlayableApplication?[] _remoteApplications;
    private readonly GamePublicActorActionKind[] _desiredPresentationActions;
    private readonly ulong[] _desiredPresentationActionSequences;
    private readonly GamePublicActorPresentation[] _actorPresentations;
    private readonly GamePublicWeaponPresentation[] _publicItemPresentations =
        new GamePublicWeaponPresentation[MaximumPublicItemPresentations];
    private readonly GamePublicWeaponPresentation[] _publicItemPresentationStaging =
        new GamePublicWeaponPresentation[MaximumPublicItemPresentations];
    private readonly ServerGenerationStateOperation<IGameGenerationStateProvider, bool>
        _prepareActorPresentationsOperation;
    private readonly ServerGenerationStateOperation<IGameGenerationStateProvider, int>
        _preparePublicItemPresentationsOperation;
    private readonly bool _ownsRuntime;
    private readonly GameTickTimingWindow _tickTimings;
    private readonly ChunkPublicationStore _publicationSource;
    private readonly RemoteChunkDeltaBodyStore _remoteDeltaBodies;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly long _generationStartedTimestamp;
    private readonly long _tickInterval;
    private long _nextTickTimestamp;
    private int _participantCount;
    private ulong _joins;
    private ulong _leaves;
    private ulong _ticks;
    private ulong _monitorRevision;
    private ulong _testMarkerSequence;
    private string? _activeTestMarker;
    private string? _lastTestMarker;
    private GameValueDistributionWindow? _remoteDirtyFanout;
    private readonly ulong[] _remoteDirtyFanoutBuckets = new ulong[8];
    private ulong _remoteObservedCommit;
    private ulong _remoteSourceCommitsObserved;
    private ulong _remoteSourceCommitGaps;
    private ulong _remoteDirtyChunksObserved;
    private ulong _remoteDirtyRecipientFanout;
    private ulong _actorPresentationVersion;
    private ulong _actorPresentationPreparedGeneration;
    private ulong _actorPresentationPreparedSimulationTick;
    private ulong _actorPresentationViewReads;
    private ulong _actorPresentationPreparations;
    private ulong _actorPresentationRecordsExamined;
    private ulong _actorPresentationChangedRecords;
    private ulong _actorPresentationExplicitInvalidations;
    private ulong _actorPresentationPreparationGeneration;
    private ulong _actorPresentationPreparationChangedRecords;
    private bool _actorPresentationPreparationContentChanged;
    private bool _actorPresentationPrepared;
    private bool _actorPresentationDirty = true;
    private bool _actorPresentationTopologyChanged = true;
    private SharedActorPresentationPreparationFault _actorPresentationFaultForTesting;
    private SharedActorPresentationPreparationFault _actorPresentationObservedFaultForTesting;
    private int _actorPresentationFaultSlotForTesting = -1;
    private ulong _publicItemPresentationVersion;
    private ulong _publicItemPresentationPreparedGeneration;
    private ulong _publicItemPresentationPreparedSimulationTick;
    private ulong _publicItemPresentationViewReads;
    private ulong _publicItemPresentationPreparations;
    private ulong _publicItemPresentationSourceRecordsCopied;
    private ulong _publicItemPresentationChangedPreparations;
    private ulong _publicItemPresentationExplicitInvalidations;
    private int _publicItemPresentationCount;
    private bool _publicItemPresentationPrepared;
    private bool _publicItemPresentationDirty = true;
    private bool _disposed;

    /// <summary>Complete durable generation selected at cold startup, or zero for a fresh world.</summary>
    public ulong RestoredSaveSequence { get; private set; }

    /// <summary>Transient guest actors retired after a cold reopen; sessions themselves are never durable.</summary>
    public int RecoveredGuestCount { get; private set; }

    private SharedPlayableAuthority(
        WorldPack worldPack,
        UltraSimGameComposition composition,
        ServerRuntime runtime,
        AdmissionService admission,
        GameChatService chat,
        RemoteChatDeliveryService chatDelivery,
        GamePlayableWorldScene scene,
        bool ownsRuntime,
        GameTickTimingWindow tickTimings,
        long generationStartedTimestamp,
        SharedPlayableAuthorityLimits limits)
    {
        WorldPack = worldPack;
        Composition = composition;
        Runtime = runtime;
        Admission = admission;
        Chat = chat;
        ChatDelivery = chatDelivery;
        Scene = scene;
        _ownsRuntime = ownsRuntime;
        _tickTimings = tickTimings ?? throw new ArgumentNullException(nameof(tickTimings));
        if (generationStartedTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(generationStartedTimestamp));
        _generationStartedTimestamp = generationStartedTimestamp;
        _limits = limits;
        _participants = new SharedPlayableParticipant[limits.MaximumParticipants];
        _occupied = new bool[limits.MaximumParticipants];
        _externallyOwned = new bool[limits.MaximumParticipants];
        _remoteApplications = new RemoteGuestPlayableApplication[limits.MaximumParticipants];
        _desiredPresentationActions = new GamePublicActorActionKind[limits.MaximumParticipants];
        _desiredPresentationActionSequences = new ulong[limits.MaximumParticipants];
        _actorPresentations = new GamePublicActorPresentation[limits.MaximumParticipants];
        _prepareActorPresentationsOperation = PrepareActorPresentationsCore;
        _preparePublicItemPresentationsOperation = PreparePublicItemPresentationsCore;
        if (!runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
                ChunkPublicationStore>(
                static (world, _, game) => game.WorldState.Publications,
                out _publicationSource))
            throw new InvalidOperationException(
                "The shared authority requires an active public-state source.");
        _remoteDeltaBodies = new RemoteChunkDeltaBodyStore(_publicationSource);
        _tickInterval = Math.Max(1, Stopwatch.Frequency / runtime.SimulationFrequencyHz);
        _nextTickTimestamp = checked(Stopwatch.GetTimestamp() + _tickInterval);
        _remoteObservedCommit = _publicationSource.CommitSequence;
    }

    /// <summary>Creates and owns a fresh shared Concept Demo server on the calling thread.</summary>
    public static SharedPlayableAuthority CreateConceptDemo(
        SharedPlayableAuthorityLimits limits = default,
        int simulationFrequencyHz = WorldClockOptions.DefaultSimulationFrequencyHz,
        Func<ServerRuntime, UltraSimGameComposition, ulong>? coldRestore = null)
    {
        if (limits == default) limits = SharedPlayableAuthorityLimits.Default;
        limits.Validate();
        long generationStartedTimestamp = Stopwatch.GetTimestamp();
        ConceptDemoStructuralSource source = GamePlayableWorld.CreateConceptDemoStructuralSource();
        // Keep the frozen combined Pack only for legacy in-process regression callers. W7
        // Remote deployment authorizes the canonical manifest and independent Pack artifacts.
        WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
        var composition = new UltraSimGameComposition(
            limits.RequiredInventoryLimits,
            runtimeProfile: GameRuntimeProfile.Networked,
            conceptDemoStructuralSource: source);
        var runtime = new ServerRuntime(
            composition,
            new ServerRuntimeOptions(
                UltraSimGameComposition.CreateWorldOptions(
                    limits.RequiredEntityCapacity,
                    new PersistenceIdentityOptions(new PersistentWorldId(0x4D_0000UL)),
                    GameRuntimeProfile.Networked),
                new WorldClockOptions(simulationFrequencyHz)));
        AdmissionService? admission = null;
        GameChatService? chat = null;
        RemoteChatDeliveryService? delivery = null;
        try
        {
            runtime.Initialize();
            runtime.Start();
            admission = new AdmissionService(runtime.Game, composition);
            if (!runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
                    GamePlayableWorldScene>(
                    static (world, _, game) =>
                        GamePlayableWorld.PopulateSharedConceptDemoScene(world, game),
                    out GamePlayableWorldScene scene) || !scene.IsValid)
                throw new InvalidOperationException("The shared Concept Demo scene could not populate.");
            var tickTimings = new GameTickTimingWindow();
            long initialTickStarted = Stopwatch.GetTimestamp();
            runtime.Tick();
            tickTimings.RecordElapsed(initialTickStarted);
            ulong restoredSequence = 0;
            int recoveredGuests = 0;
            if (coldRestore is not null)
            {
                restoredSequence = coldRestore(runtime, composition);
                if (restoredSequence == 0)
                    throw new InvalidDataException("The cold restore callback selected no complete generation.");
                if (!runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
                        (GamePlayableWorldScene Scene, int Guests)>(
                        (world, _, game) =>
                        {
                            GamePlayableWorldScene rebound = RebindSavedScene(world, game, scene);
                            int guests = RetireSavedConceptDemoGuests(world, game);
                            return (rebound, guests);
                        }, out var recovered))
                    throw new InvalidOperationException("The restored shared authority has no active generation.");
                scene = recovered.Scene;
                recoveredGuests = recovered.Guests;
                if (recoveredGuests != 0)
                {
                    long cleanupStarted = Stopwatch.GetTimestamp();
                    runtime.Tick();
                    tickTimings.RecordElapsed(cleanupStarted);
                }
            }
            var chatLimits = GameChatLimits.Default with
            {
                MaximumParticipants = limits.MaximumParticipants,
                MaximumRecipientsPerSpeech = limits.MaximumParticipants,
            };
            chat = new GameChatService(runtime.Game, admission, chatLimits);
            delivery = new RemoteChatDeliveryService(
                runtime.Game,
                chat,
                new RemoteChatServerLimits(
                    limits.MaximumParticipants,
                    checked(limits.MaximumParticipants * 64),
                    64,
                    8,
                    2_000,
                    1_000));
            var authority = new SharedPlayableAuthority(
                pack, composition, runtime, admission, chat, delivery, scene,
                ownsRuntime: true, tickTimings, generationStartedTimestamp, limits);
            authority.RestoredSaveSequence = restoredSequence;
            authority.RecoveredGuestCount = recoveredGuests;
            return authority;
        }
        catch
        {
            delivery?.Dispose();
            chat?.Dispose();
            admission?.Dispose();
            if (runtime.State == ServerRuntimeState.Running) runtime.Stop();
            runtime.Dispose();
            throw;
        }
    }

    private static GamePlayableWorldScene RebindSavedScene(
        EcsWorld world, IGameGenerationStateProvider game, in GamePlayableWorldScene original)
    {
        Entity Match(PersistentEntityId id, PrefabKey expected)
        {
            if (!world.TryResolvePersistentEntity(id, out Entity entity) ||
                !world.TryGetPrefabProvenance(entity, out PrefabKey actual) || actual != expected)
                throw new InvalidDataException(
                    "The restored Concept Demo scene does not match its fixed authored identities.");
            return entity;
        }

        GamePlayableWorldScene rebound = original with
        {
            CabinDoor = Match(original.CabinDoorId, GamePrefabKeys.CabinDoor),
            CabinChest = Match(original.CabinChestId, GamePrefabKeys.WorldChest),
            WoodlandAnimal = Match(original.WoodlandAnimalId, GamePrefabKeys.WoodlandAnimal),
            SkyTelepad = Match(original.SkyTelepadId, GamePrefabKeys.SkyTelepad),
            CentralFarGate = Match(original.CentralFarGateId, GamePrefabKeys.FarGate),
            FarReturnGate = Match(original.FarReturnGateId, GamePrefabKeys.FarGate),
            WelcomeSign = Match(original.WelcomeSignId, GamePrefabKeys.InformationalSign),
            FallbackLabSign = Match(original.FallbackLabSignId, GamePrefabKeys.InformationalSign),
            IronSwordSpawner = Match(original.IronSwordSpawnerId, GamePrefabKeys.IronSwordSpawner),
        };
        // Generation initialization already rebuilt and validated dynamic blocker projection.
        if (!game.WorldState.TryReadPublic(world, rebound.CabinDoor, out _) ||
            !game.WorldState.TryReadPublic(world, rebound.CabinChest, out _))
            throw new InvalidDataException("The restored scene has no public blocker truth.");
        return rebound;
    }

    private static int RetireSavedConceptDemoGuests(EcsWorld world, IGameGenerationStateProvider game)
    {
        // This composition admits accountless guests only. Admission sessions and control epochs
        // are process-local, so persisted guest actors must be retired before new admission opens.
        // Do not apply this policy to an account-backed world without durable session ownership.
        var guests = new Entity[64];
        int count = 0;
        uint capacity = checked((uint)world.Options.EntityCapacity.MaximumEntities);
        for (uint index = 0; index < capacity; index++)
        {
            if (!world.TryGetEntity(index, out Entity entity) ||
                !world.TryGetPrefabProvenance(entity, out PrefabKey prefab) ||
                (prefab != GamePrefabKeys.Character && prefab != GamePrefabKeys.CharacterMale))
                continue;
            if (count == guests.Length)
                throw new InvalidDataException("The saved guest census exceeds the product participant bound.");
            guests[count++] = entity;
        }
        for (int guest = 0; guest < count; guest++)
        {
            Entity actor = guests[guest];
            Entity backpack = Entity.Null;
            for (uint index = 0; index < capacity; index++)
            {
                if (!world.TryGetEntity(index, out Entity entity) ||
                    !game.Inventory.TryGetContainerOwner(world, entity, out Entity owner) || owner != actor)
                    continue;
                if (backpack.IsValid || !world.TryGetPrefabProvenance(entity, out PrefabKey prefab) ||
                    prefab != GamePrefabKeys.Backpack)
                    throw new InvalidDataException("A saved guest has ambiguous or invalid inventory ownership.");
                backpack = entity;
            }
            if (!backpack.IsValid ||
                !game.TryRetireTransientParticipantInventory(world, actor, backpack) ||
                !world.TryDestroy(actor))
                throw new InvalidDataException("A saved guest could not retire through Game lifetime policy.");
        }
        return count;
    }

    /// <summary>
    /// Attaches remote participant adapters to an already-running internal server and its existing
    /// local participant. The borrowed runtime remains owned and ticked by its original local
    /// authority thread; enabling Network recipients neither restarts nor replaces that world.
    /// </summary>
    public static SharedPlayableAuthority AttachExisting(
        WorldPack worldPack,
        UltraSimGameComposition composition,
        ServerRuntime runtime,
        AdmissionService admission,
        GameChatService chat,
        RemoteChatDeliveryService chatDelivery,
        GamePlayableWorldScene scene,
        SharedPlayableParticipant localParticipant,
        GameTickTimingWindow tickTimings,
        long generationStartedTimestamp,
        SharedPlayableAuthorityLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(worldPack);
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(runtime);
        ArgumentNullException.ThrowIfNull(admission);
        ArgumentNullException.ThrowIfNull(chat);
        ArgumentNullException.ThrowIfNull(chatDelivery);
        ArgumentNullException.ThrowIfNull(tickTimings);
        if (generationStartedTimestamp < 0)
            throw new ArgumentOutOfRangeException(nameof(generationStartedTimestamp));
        if (limits == default) limits = SharedPlayableAuthorityLimits.Default;
        limits.Validate();
        if (runtime.State != ServerRuntimeState.Running || !scene.IsValid ||
            !localParticipant.IsValid || localParticipant.Slot >= limits.MaximumParticipants)
            throw new ArgumentException(
                "The borrowed shared-authority composition is not currently playable.");
        var authority = new SharedPlayableAuthority(
            worldPack, composition, runtime, admission, chat, chatDelivery, scene,
            ownsRuntime: false, tickTimings, generationStartedTimestamp, limits);
        int slot = localParticipant.Slot;
        authority._participants[slot] = localParticipant;
        authority._occupied[slot] = true;
        authority._externallyOwned[slot] = true;
        authority._participantCount = 1;
        authority._joins = 1;
        authority.InvalidateActorPresentations(topologyChanged: true);
        return authority;
    }

    public WorldPack WorldPack { get; }
    public GamePlayableWorldScene Scene { get; }
    internal UltraSimGameComposition Composition { get; }
    internal ServerRuntime Runtime { get; }
    internal AdmissionService Admission { get; }
    internal GameChatService Chat { get; }
    internal RemoteChatDeliveryService ChatDelivery { get; }
    internal RemoteChunkDeltaBodyStore RemoteDeltaBodies => _remoteDeltaBodies;
    public GameTelemetryLevel TelemetryLevel => _tickTimings.Level;

    /// <summary>
    /// Publishes one authority-originated routed Chat occurrence to the exact currently entered
    /// participant census. Presentation timing remains a client concern.
    /// </summary>
    public GameSystemBroadcastResult PublishSystem(
        string text,
        string displayLabel = "System")
    {
        EnsureOperational();
        GameSystemBroadcastResult result = Chat.PublishSystem(text, displayLabel);
        if (result.Accepted)
            _ = ChatDelivery.TryHandoffPendingOccurrences();
        return result;
    }

    /// <summary>Configures bounded timing diagnostics without changing authority cadence.</summary>
    public void ConfigureTelemetry(GameTelemetryLevel level)
    {
        EnsureOperational();
        if (_remoteApplications.Any(static application => application is not null))
            throw new InvalidOperationException(
                "Authority telemetry must be selected before remote applications exist.");
        _tickTimings.Configure(level);
        _publicationSource.ConfigureChangeCensus(level == GameTelemetryLevel.Detailed);
        _remoteDeltaBodies.ConfigureDetailedTiming(level == GameTelemetryLevel.Detailed);
        if (level == GameTelemetryLevel.Detailed)
            _remoteDirtyFanout ??= new GameValueDistributionWindow();
        ResetRemoteReplicationTelemetry();
    }

    /// <summary>Resets only bounded diagnostics at an explicit measurement barrier.</summary>
    public void ResetTelemetryWindow()
    {
        EnsureOperational();
        _tickTimings.Configure(_tickTimings.Level);
        ResetRemoteReplicationTelemetry();
        _remoteDeltaBodies.ResetStatistics();
        for (int index = 0; index < _remoteApplications.Length; index++)
            _remoteApplications[index]?.ResetTelemetryWindow();
    }

    /// <summary>
    /// Emits one trusted test-marker transition through the ordinary System occurrence path.
    /// This is an authority API, not a participant Chat command or administrative client action.
    /// </summary>
    public GameTestMarkerResult PublishTestMarker(GameTestMarkerPhase phase, string label)
    {
        EnsureOperational();
        string canonical = label?.Trim() ?? string.Empty;
        if (phase is not (GameTestMarkerPhase.Start or GameTestMarkerPhase.Complete) ||
            canonical.Length is 0 or > 64 || canonical.Any(char.IsControl))
            return new GameTestMarkerResult(false, _testMarkerSequence, phase, canonical,
                "The test marker phase or label is invalid.");
        if (phase == GameTestMarkerPhase.Start && _activeTestMarker is not null)
            return new GameTestMarkerResult(false, _testMarkerSequence, phase, canonical,
                "Another test marker is already active.");
        if (phase == GameTestMarkerPhase.Complete &&
            !string.Equals(_activeTestMarker, canonical, StringComparison.Ordinal))
            return new GameTestMarkerResult(false, _testMarkerSequence, phase, canonical,
                "The completed marker does not match the active marker.");

        ulong sequence = phase == GameTestMarkerPhase.Start
            ? checked(_testMarkerSequence + 1)
            : _testMarkerSequence;
        string text = $"TEST {phase.ToString().ToUpperInvariant()} #{sequence}: {canonical}";
        GameSystemBroadcastResult broadcast = PublishSystem(text, "Test");
        if (!broadcast.Accepted)
            return new GameTestMarkerResult(false, _testMarkerSequence, phase, canonical,
                $"The System occurrence was rejected: {broadcast.Status}.");
        if (phase == GameTestMarkerPhase.Start)
        {
            _testMarkerSequence = sequence;
            _activeTestMarker = canonical;
            _lastTestMarker = $"#{sequence} START — {canonical}";
        }
        else
        {
            _lastTestMarker = $"#{sequence} COMPLETE — {canonical}";
            _activeTestMarker = null;
        }
        return new GameTestMarkerResult(true, sequence, phase, canonical, text);
    }

    /// <summary>Captures the generic owner-thread Runtime control policy without exposing mutable state.</summary>
    public ServerRuntimeControlSnapshot CaptureRuntimeControlSnapshot()
    {
        EnsureOperational();
        return Runtime.CaptureControlSnapshot();
    }

    /// <summary>Captures one bounded sanitized monitor view of this authority.</summary>
    public ServerMonitorSnapshot CaptureServerMonitor(
        RemoteServerPerformanceProfile? performanceProfile = null,
        ulong rateLimitedCommands = 0)
    {
        EnsureOperational();
        var rows = new ServerMonitorParticipant[_participantCount];
        int count = 0;
        int entityCount = 0;
        bool captured = Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                entityCount = world.AliveCount;
                for (int slot = 0; slot < _participants.Length; slot++)
                {
                    if (!_occupied[slot]) continue;
                    SharedPlayableParticipant participant = _participants[slot];
                    Entity actor = Resolve(world, participant.Entities.PlayerId);
                    if (!game.WorldState.TryReadPosition(world, actor, out WorldPosition position))
                        throw new InvalidOperationException(
                            "A monitored participant had no authoritative position.");
                    bool local = _externallyOwned[slot];
                    rows[count++] = new ServerMonitorParticipant(
                        participant.Participant,
                        participant.PublicEntity.Index,
                        participant.PublicEntity.Generation,
                        participant.GuestIdentity.DisplayName!,
                        local ? ServerMonitorConnectionKind.Local : ServerMonitorConnectionKind.Remote,
                        local ? -1 : FindRoundTripMilliseconds(participant.Participant),
                        position.Chunk.X,
                        position.Chunk.Z);
                }
                return true;
            }, out bool complete) && complete;
        if (!captured || count != rows.Length)
            throw new InvalidOperationException("The shared Server Monitor snapshot was incomplete.");
        Array.Sort(rows, static (left, right) =>
            left.Participant.Value.CompareTo(right.Participant.Value));
        GameTickTimingSnapshot timing = _tickTimings.Capture();
        ulong tick = Runtime.SimulationTick;
        int frequency = Runtime.SimulationFrequencyHz;
        return new ServerMonitorSnapshot(
            checked(++_monitorRevision),
            Runtime.ActiveGenerationId,
            tick,
            frequency,
            ElapsedSeconds(_generationStartedTimestamp),
            timing.RecentMicroseconds,
            timing.P95Microseconds,
            entityCount,
            _limits.MaximumParticipants,
            _joins,
            _leaves,
            performanceProfile?.Revision ?? 0,
            ShortPerformanceHash(performanceProfile),
            checked((int)(performanceProfile?.MaximumNetworkServiceIntervalMicroseconds ?? 0)),
            performanceProfile?.MaximumInputHz ?? 0,
            performanceProfile?.RecommendedInputHz ?? 0,
            rateLimitedCommands,
            _lastTestMarker,
            rows);
    }

    private static string? ShortPerformanceHash(RemoteServerPerformanceProfile? profile) =>
        profile is null ? null : profile.Hash.Part0.ToString("X16")[..12];

    private static long ElapsedSeconds(long startedTimestamp)
    {
        long elapsed = Math.Max(0, Stopwatch.GetTimestamp() - startedTimestamp);
        return elapsed / Stopwatch.Frequency;
    }

    /// <summary>Advances an owned server clock without coupling it to any one connection.</summary>
    public int TickDue()
    {
        EnsureOperational();
        if (!_ownsRuntime)
            throw new InvalidOperationException("A borrowed authority is ticked by its internal server owner.");
        long now = Stopwatch.GetTimestamp();
        int steps = 0;
        while (now >= _nextTickTimestamp && steps < 4)
        {
            long started = _tickTimings.Start();
            long actual = started == 0 ? now : started;
            _tickTimings.RecordScheduledStart(
                _nextTickTimestamp, actual, _tickInterval, catchUp: steps != 0);
            Runtime.Tick();
            ObserveRemotePublicationFanout();
            _tickTimings.RecordElapsed(started);
            _ticks++;
            _nextTickTimestamp = checked(_nextTickTimestamp + _tickInterval);
            steps++;
        }
        if (steps == 4 && now >= _nextTickTimestamp)
        {
            _tickTimings.RecordSkippedOpportunity();
            _nextTickTimestamp = checked(now + _tickInterval);
        }
        _ = ChatDelivery.TryHandoffPendingOccurrences();
        return steps;
    }

    internal AdmissionResult Join(
        ParticipantId participant,
        in GameGuestAdmissionRequest request,
        out SharedPlayableParticipant playable)
    {
        EnsureOperational();
        playable = default;
        int slot = FindFreeSlot();
        if (slot < 0)
            return new AdmissionResult(
                participant, AdmissionStage.Rejected, Entity.Null, default, default,
                Runtime.ActiveGenerationId, default, AdmissionRejection.EntityCreationFailed,
                "The shared authority participant capacity is full.");
        AdmissionResult entered = Admission.JoinGuest(participant, request);
        if (!entered.IsEntered || !entered.GuestIdentity.IsValid)
            return entered;
        try
        {
            WorldPosition start = SpawnForSlot(slot);
            GameControlSessionEpoch control = GameControlSessionEpoch.Create();
            if (!Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
                    JoinCapture>(
                    (world, _, game) =>
                    {
                        GamePlayableParticipantEntities entities =
                            GamePlayableWorld.PopulateSharedParticipant(
                                world, game, entered.Entity, start);
                        GameWorldActionStatus status = game.WorldActions.BeginControlSession(
                            world, entered.Entity, control);
                        if (status != GameWorldActionStatus.Accepted)
                            throw new InvalidOperationException(
                                $"The shared control session could not bind: {status}.");
                        if (!game.WorldState.Publications.TryGetPublicEntityId(
                                entered.Entity, out WorldPublicEntityId publicId))
                        {
                            // Publication identity is assigned when lifecycle maintenance runs.
                            publicId = default;
                        }
                        return new JoinCapture(entities, publicId);
                    }, out JoinCapture capture))
                throw new InvalidOperationException("The shared participant had no active generation.");
            long started = Stopwatch.GetTimestamp();
            Runtime.Tick();
            ObserveRemotePublicationFanout();
            _tickTimings.RecordElapsed(started);
            _ticks++;
            if (!capture.PublicEntity.IsValid)
            {
                if (!Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
                        WorldPublicEntityId>(
                        (world, _, game) =>
                        {
                            Entity actor = Resolve(world, capture.Entities.PlayerId);
                            if (!game.WorldState.Publications.TryGetPublicEntityId(
                                    actor, out WorldPublicEntityId id))
                                throw new InvalidOperationException(
                                    "The shared participant has no public identity.");
                            return id;
                        }, out WorldPublicEntityId published))
                    throw new InvalidOperationException(
                        "The shared participant public identity was unavailable.");
                capture = capture with { PublicEntity = published };
            }
            playable = new SharedPlayableParticipant(
                participant, entered.GuestIdentity, control,
                capture.Entities, capture.PublicEntity, slot);
            _participants[slot] = playable;
            _occupied[slot] = true;
            _actorPresentations[slot] = default;
            _desiredPresentationActions[slot] = GamePublicActorActionKind.None;
            _desiredPresentationActionSequences[slot] = 0;
            _participantCount++;
            _joins++;
            InvalidateActorPresentations(topologyChanged: true);
            if (!Chat.RefreshEnteredParticipants())
                throw new InvalidOperationException("Chat could not admit the shared participant census.");
            return entered;
        }
        catch
        {
            _ = Admission.TryLeave(participant, out _);
            throw;
        }
    }

    internal bool Leave(in SharedPlayableParticipant playable)
    {
        EnsureOperational();
        if (!playable.IsValid || playable.Slot >= _participants.Length ||
            !_occupied[playable.Slot] || _participants[playable.Slot] != playable)
            return false;
        if (_externallyOwned[playable.Slot])
            throw new InvalidOperationException(
                "A borrowed local participant is retired by its internal server owner.");
        SharedPlayableParticipant retiring = playable;
        bool retired = Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, generationId, game) =>
            {
                Entity player = Resolve(world, retiring.Entities.PlayerId);
                _ = game.WorldActions.EndControlSession(world, player, retiring.ControlSession);
                Entity backpack = Resolve(world, retiring.Entities.BackpackId);
                if (!game.TryRetireTransientParticipantInventory(world, player, backpack) ||
                    !world.TryDestroy(player))
                    throw new InvalidOperationException(
                        "The shared participant entities could not retire cleanly.");
                return true;
            }, out bool complete) && complete;
        if (!retired || !Admission.TryLeave(playable.Participant, out _))
            throw new InvalidOperationException("The shared participant admission could not retire.");
        _participants[playable.Slot] = default;
        _occupied[playable.Slot] = false;
        _actorPresentations[playable.Slot] = default;
        _desiredPresentationActions[playable.Slot] = GamePublicActorActionKind.None;
        _desiredPresentationActionSequences[playable.Slot] = 0;
        _participantCount--;
        _leaves++;
        InvalidateActorPresentations(topologyChanged: true);
        long started = Stopwatch.GetTimestamp();
        Runtime.Tick();
        ObserveRemotePublicationFanout();
        _tickTimings.RecordElapsed(started);
        _ticks++;
        if (!Chat.RefreshEnteredParticipants())
            throw new InvalidOperationException("Chat could not retire the shared participant census.");
        return true;
    }

    public int CopyParticipants(Span<SharedPlayableParticipant> destination)
    {
        EnsureOperational();
        if (destination.Length < _participantCount) return -1;
        int count = 0;
        for (int slot = 0; slot < _participants.Length; slot++)
            if (_occupied[slot]) destination[count++] = _participants[slot];
        return count;
    }

    /// <summary>
    /// Records one newly accepted presentation action. The sequence belongs to that participant's
    /// control lineage; rejected Game actions never advance this public presentation state.
    /// </summary>
    public bool RecordPresentationAction(
        in SharedPlayableParticipant participant,
        ulong actionSequence,
        GamePublicActorActionKind action)
    {
        EnsureOperational();
        if (!participant.IsValid || participant.Slot >= _participants.Length ||
            !_occupied[participant.Slot] || _participants[participant.Slot] != participant ||
            action is not (GamePublicActorActionKind.Interact or
                GamePublicActorActionKind.Primary) || actionSequence == 0 ||
            actionSequence <= _desiredPresentationActionSequences[participant.Slot])
            return false;
        _desiredPresentationActions[participant.Slot] = action;
        _desiredPresentationActionSequences[participant.Slot] = actionSequence;
        InvalidateActorPresentations(topologyChanged: false);
        return true;
    }

    /// <summary>Copies the current bounded public actor appearance/action read model.</summary>
    public int CopyActorPresentations(Span<GamePublicActorPresentation> destination)
    {
        SharedActorPresentationView view = GetActorPresentationView();
        if (destination.Length < view.Count) return -1;
        int count = 0;
        for (int slot = 0; slot < view.Capacity; slot++)
            if (view.TryGet(slot, out GamePublicActorPresentation presentation))
                destination[count++] = presentation;
        return count == view.Count ? count : -1;
    }

    /// <summary>
    /// Returns one owner-thread shared source view. Preparation is shared across recipients and
    /// remains independent from every recipient's delivery identity and applied proof.
    /// </summary>
    internal SharedActorPresentationView GetActorPresentationView()
    {
        EnsureOperational();
        SaturatingIncrement(ref _actorPresentationViewReads);
        ulong generation = Runtime.ActiveGenerationId;
        ulong simulationTick = Runtime.SimulationTick;
        if (!_actorPresentationPrepared || _actorPresentationDirty ||
            _actorPresentationPreparedGeneration != generation ||
            _actorPresentationPreparedSimulationTick != simulationTick)
            PrepareActorPresentations(generation, simulationTick);
        return new SharedActorPresentationView(
            _actorPresentations, _actorPresentationVersion, _participantCount);
    }

    /// <summary>
    /// Invalidates shared source facts after an accepted between-tick participant mutation.
    /// This does not advance any recipient delivery or proof state.
    /// </summary>
    internal bool ObserveActorPresentationMutation(in SharedPlayableParticipant participant)
    {
        EnsureOperational();
        if (!participant.IsValid || participant.Slot >= _participants.Length ||
            !_occupied[participant.Slot] || _participants[participant.Slot] != participant)
            return false;
        InvalidateActorPresentations(topologyChanged: false);
        return true;
    }

    internal SharedActorPresentationCensus GetActorPresentationCensus()
    {
        EnsureOperational();
        return new SharedActorPresentationCensus(
            _actorPresentationVersion,
            _actorPresentationViewReads,
            _actorPresentationPreparations,
            _actorPresentationRecordsExamined,
            _actorPresentationChangedRecords,
            _actorPresentationExplicitInvalidations);
    }

    /// <summary>
    /// Returns the shared bounded public ground-item source. Preparation is transactional and
    /// shared across every remote recipient; interest and proof remain recipient-owned.
    /// </summary>
    internal SharedPublicItemPresentationView GetPublicItemPresentationView()
    {
        EnsureOperational();
        SaturatingIncrement(ref _publicItemPresentationViewReads);
        ulong generation = Runtime.ActiveGenerationId;
        ulong simulationTick = Runtime.SimulationTick;
        if (!_publicItemPresentationPrepared || _publicItemPresentationDirty ||
            _publicItemPresentationPreparedGeneration != generation ||
            _publicItemPresentationPreparedSimulationTick != simulationTick)
            PreparePublicItemPresentations(generation, simulationTick);
        return new SharedPublicItemPresentationView(
            _publicItemPresentations,
            _publicItemPresentationVersion,
            _publicItemPresentationCount);
    }

    internal void ObservePublicItemPresentationMutation()
    {
        EnsureOperational();
        _publicItemPresentationDirty = true;
        SaturatingIncrement(ref _publicItemPresentationExplicitInvalidations);
    }

    internal SharedPublicItemPresentationCensus GetPublicItemPresentationCensus()
    {
        EnsureOperational();
        return new SharedPublicItemPresentationCensus(
            _publicItemPresentationVersion,
            _publicItemPresentationViewReads,
            _publicItemPresentationPreparations,
            _publicItemPresentationSourceRecordsCopied,
            _publicItemPresentationChangedPreparations,
            _publicItemPresentationExplicitInvalidations);
    }

    internal void SetActorPresentationPreparationFaultForTesting(
        int slot,
        SharedActorPresentationPreparationFault fault)
    {
        EnsureOperational();
        if ((uint)slot >= (uint)_actorPresentations.Length)
            throw new ArgumentOutOfRangeException(nameof(slot));
        if (fault is not (SharedActorPresentationPreparationFault.ReturnFalse or
            SharedActorPresentationPreparationFault.Throw))
            throw new ArgumentOutOfRangeException(nameof(fault));
        _actorPresentationFaultSlotForTesting = slot;
        _actorPresentationFaultForTesting = fault;
    }

    private void PrepareActorPresentations(ulong generation, ulong simulationTick)
    {
        _actorPresentationPreparationGeneration = generation;
        _actorPresentationPreparationContentChanged = !_actorPresentationPrepared ||
            _actorPresentationPreparedGeneration != generation ||
            _actorPresentationTopologyChanged;
        _actorPresentationPreparationChangedRecords = 0;
        _actorPresentationObservedFaultForTesting =
            SharedActorPresentationPreparationFault.None;
        bool captured;
        try
        {
            captured = Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                _prepareActorPresentationsOperation, out bool complete) && complete;
        }
        catch
        {
            InvalidateFailedActorPresentationPreparation();
            throw;
        }
        if (!captured)
        {
            InvalidateFailedActorPresentationPreparation();
            if (_actorPresentationObservedFaultForTesting ==
                SharedActorPresentationPreparationFault.Throw)
                throw new InvalidOperationException(
                    "Injected actor-presentation preparation failure.");
            throw new InvalidOperationException(
                "The authoritative actor-presentation source could not be prepared.");
        }
        SaturatingIncrement(ref _actorPresentationPreparations);
        SaturatingAdd(ref _actorPresentationChangedRecords,
            _actorPresentationPreparationChangedRecords);
        if (_actorPresentationPreparationContentChanged)
            _actorPresentationVersion = checked(_actorPresentationVersion + 1);
        _actorPresentationPreparedGeneration = generation;
        _actorPresentationPreparedSimulationTick = simulationTick;
        _actorPresentationPrepared = true;
        _actorPresentationDirty = false;
        _actorPresentationTopologyChanged = false;
    }

    private bool PrepareActorPresentationsCore(
        EcsWorld world,
        ulong activeGeneration,
        IGameGenerationStateProvider game)
    {
        if (activeGeneration != _actorPresentationPreparationGeneration) return false;
        for (int slot = 0; slot < _participants.Length; slot++)
        {
            ref GamePublicActorPresentation retained = ref _actorPresentations[slot];
            if (!_occupied[slot])
            {
                if (retained.IsValid)
                {
                    retained = default;
                    _actorPresentationPreparationContentChanged = true;
                    _actorPresentationPreparationChangedRecords++;
                }
                continue;
            }
            SaturatingIncrement(ref _actorPresentationRecordsExamined);
            SharedPlayableParticipant participant = _participants[slot];
            Entity actor = Resolve(world, participant.Entities.PlayerId);
            uint mainHandPrefab = 0;
            if (game.Inventory.Index.TryGetEquipped(
                    actor, InventoryEquipSlot.MainHand, out Entity item) &&
                game.WorldState.TryReadPublic(
                    world, item, out WorldPublicDescriptor itemPresentation))
                mainHandPrefab = itemPresentation.PrefabId;
            if (!game.WorldActions.TryReadVerticalMotion(
                    world, actor, out GameVerticalMotionSnapshot vertical) ||
                vertical.Kind == GameVerticalMotionKind.None)
                return false;
            GamePublicActorVerticalPhase verticalPhase = vertical.Kind switch
            {
                GameVerticalMotionKind.Grounded => GamePublicActorVerticalPhase.Grounded,
                GameVerticalMotionKind.Airborne when
                    vertical.VelocityMillimetersPerSecond > 0 =>
                        GamePublicActorVerticalPhase.Rising,
                GameVerticalMotionKind.Airborne => GamePublicActorVerticalPhase.Falling,
                _ => throw new InvalidOperationException(
                    "The authoritative vertical state was not presentable."),
            };
            GamePublicActorActionKind action = _desiredPresentationActions[slot];
            ulong actionSequence = _desiredPresentationActionSequences[slot];

            if (_actorPresentationFaultForTesting != SharedActorPresentationPreparationFault.None &&
                _actorPresentationFaultSlotForTesting == slot)
            {
                SharedActorPresentationPreparationFault fault =
                    _actorPresentationFaultForTesting;
                _actorPresentationFaultForTesting = SharedActorPresentationPreparationFault.None;
                _actorPresentationFaultSlotForTesting = -1;
                _actorPresentationObservedFaultForTesting = fault;
                return false;
            }
            if (retained.IsValid && retained.Participant == participant &&
                retained.MainHandPrefabId == mainHandPrefab &&
                retained.VerticalPhase == verticalPhase &&
                retained.Action == action && retained.ActionSequence == actionSequence)
                continue;
            ulong revision = retained.IsValid &&
                retained.Participant.PublicEntity == participant.PublicEntity
                    ? checked(retained.Revision + 1)
                    : 1;
            retained = new GamePublicActorPresentation(
                participant, revision, mainHandPrefab, verticalPhase,
                action, actionSequence);
            _actorPresentationPreparationContentChanged = true;
            _actorPresentationPreparationChangedRecords++;
        }
        return true;
    }

    private void InvalidateFailedActorPresentationPreparation()
    {
        // Preparation writes retained rows incrementally. None of those rows may be exposed as a
        // valid old source revision after a later row fails. Forcing the next complete capture to
        // start from an invalid prepared state guarantees one new source version on recovery.
        _actorPresentationPrepared = false;
        _actorPresentationDirty = true;
    }

    private void InvalidateActorPresentations(bool topologyChanged)
    {
        _actorPresentationDirty = true;
        _actorPresentationTopologyChanged |= topologyChanged;
        SaturatingIncrement(ref _actorPresentationExplicitInvalidations);
    }

    private void PreparePublicItemPresentations(ulong generation, ulong simulationTick)
    {
        int desiredCount;
        try
        {
            bool captured = Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, int>(
                _preparePublicItemPresentationsOperation, out desiredCount);
            if (!captured || desiredCount < 0 ||
                desiredCount > _publicItemPresentationStaging.Length)
                throw new InvalidOperationException(
                    "The authoritative public-item presentation source could not be prepared.");
        }
        catch
        {
            _publicItemPresentationPrepared = false;
            _publicItemPresentationDirty = true;
            throw;
        }

        SaturatingIncrement(ref _publicItemPresentationPreparations);
        SaturatingAdd(ref _publicItemPresentationSourceRecordsCopied, (ulong)desiredCount);
        bool changed = !_publicItemPresentationPrepared ||
            _publicItemPresentationPreparedGeneration != generation ||
            desiredCount != _publicItemPresentationCount ||
            !PublicItemPresentationsEqual(desiredCount);
        if (changed)
        {
            _publicItemPresentationStaging.AsSpan(0, desiredCount).CopyTo(
                _publicItemPresentations);
            if (desiredCount < _publicItemPresentationCount)
                Array.Clear(
                    _publicItemPresentations,
                    desiredCount,
                    _publicItemPresentationCount - desiredCount);
            _publicItemPresentationCount = desiredCount;
            _publicItemPresentationVersion = checked(_publicItemPresentationVersion + 1);
            SaturatingIncrement(ref _publicItemPresentationChangedPreparations);
        }
        _publicItemPresentationPreparedGeneration = generation;
        _publicItemPresentationPreparedSimulationTick = simulationTick;
        _publicItemPresentationPrepared = true;
        _publicItemPresentationDirty = false;
    }

    private int PreparePublicItemPresentationsCore(
        EcsWorld world,
        ulong _,
        IGameGenerationStateProvider game)
    {
        return game.Items.CopyPublicWeaponPresentations(
            world,
            game.WorldState,
            _publicItemPresentationStaging);
    }

    private bool PublicItemPresentationsEqual(int count)
    {
        for (int index = 0; index < count; index++)
        {
            ref readonly GamePublicWeaponPresentation retained =
                ref _publicItemPresentations[index];
            ref readonly GamePublicWeaponPresentation staged =
                ref _publicItemPresentationStaging[index];
            GameWeaponInstance retainedWeapon = retained.Weapon;
            GameWeaponInstance stagedWeapon = staged.Weapon;
            if (retained.Id != staged.Id || retained.Position != staged.Position ||
                retained.StateFlags != staged.StateFlags ||
                !WeaponsEqual(in retainedWeapon, in stagedWeapon) ||
                !string.Equals(retained.DisplayName, staged.DisplayName,
                    StringComparison.Ordinal) ||
                !Equals(retained.InspectionProperties, staged.InspectionProperties))
                return false;
        }
        return true;
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

    /// <summary>Copies bounded cold diagnostics for the currently attached remote adapters.</summary>
    public int CopyRemoteSessionSnapshots(Span<RemoteGuestPlayableServerSnapshot> destination)
    {
        EnsureOperational();
        int required = 0;
        for (int index = 0; index < _remoteApplications.Length; index++)
            if (_remoteApplications[index] is not null) required++;
        if (destination.Length < required) return -1;
        int count = 0;
        for (int index = 0; index < _remoteApplications.Length; index++)
            if (_remoteApplications[index] is RemoteGuestPlayableApplication application)
                destination[count++] = application.GetSnapshot();
        return count;
    }

    internal int CopyRemotePresentationCensus(Span<RemoteSharedPresentationCensus> destination)
    {
        EnsureOperational();
        int required = 0;
        for (int index = 0; index < _remoteApplications.Length; index++)
            if (_remoteApplications[index] is not null) required++;
        if (destination.Length < required) return -1;
        int count = 0;
        for (int index = 0; index < _remoteApplications.Length; index++)
            if (_remoteApplications[index] is RemoteGuestPlayableApplication application)
                destination[count++] = application.GetSharedPresentationCensus();
        return count;
    }

    internal int CopyRemotePublicItemPresentationCensus(
        Span<RemotePublicItemPresentationCensus> destination)
    {
        EnsureOperational();
        int required = 0;
        for (int index = 0; index < _remoteApplications.Length; index++)
            if (_remoteApplications[index] is not null) required++;
        if (destination.Length < required) return -1;
        int count = 0;
        for (int index = 0; index < _remoteApplications.Length; index++)
            if (_remoteApplications[index] is RemoteGuestPlayableApplication application)
                destination[count++] = application.GetPublicItemPresentationCensus();
        return count;
    }

    internal void RegisterRemoteApplication(RemoteGuestPlayableApplication application)
    {
        EnsureOperational();
        ArgumentNullException.ThrowIfNull(application);
        for (int index = 0; index < _remoteApplications.Length; index++)
        {
            if (_remoteApplications[index] is not null) continue;
            _remoteApplications[index] = application;
            return;
        }
        throw new InvalidOperationException("The bounded shared remote-adapter table is full.");
    }

    internal void UnregisterRemoteApplication(RemoteGuestPlayableApplication application)
    {
        EnsureOperational();
        for (int index = 0; index < _remoteApplications.Length; index++)
        {
            if (!ReferenceEquals(_remoteApplications[index], application)) continue;
            _remoteApplications[index] = null;
            return;
        }
    }

    public SharedPlayableAuthoritySnapshot GetSnapshot()
    {
        EnsureOperational();
        return new SharedPlayableAuthoritySnapshot(
            Runtime.ActiveGenerationId,
            Runtime.SimulationTick,
            _participantCount,
            _limits.MaximumParticipants,
            _joins,
            _leaves,
            _ticks,
            _ownsRuntime,
            _tickTimings.Capture(),
            CaptureRemoteReplicationCensus());
    }

    private void ResetRemoteReplicationTelemetry()
    {
        _remoteDirtyFanout?.Clear();
        _publicationSource.ConfigureChangeCensus(
            _tickTimings.Level == GameTelemetryLevel.Detailed);
        Array.Clear(_remoteDirtyFanoutBuckets);
        _remoteSourceCommitsObserved = _remoteSourceCommitGaps = 0;
        _remoteDirtyChunksObserved = _remoteDirtyRecipientFanout = 0;
        _remoteObservedCommit = _publicationSource.CommitSequence;
    }

    private void ObserveRemotePublicationFanout()
    {
        if (_tickTimings.Level != GameTelemetryLevel.Detailed) return;
        ChunkPublicationStore source = _publicationSource;
        ulong commit = source.CommitSequence;
        if (commit == _remoteObservedCommit) return;
        if (_remoteObservedCommit == ulong.MaxValue || commit != _remoteObservedCommit + 1)
        {
            SaturatingIncrement(ref _remoteSourceCommitGaps);
            _remoteObservedCommit = commit;
            return;
        }
        SaturatingIncrement(ref _remoteSourceCommitsObserved);
        ReadOnlySpan<SpatialChunkKey> dirty = source.LastCommittedDirtyChunks;
        for (int dirtyIndex = 0; dirtyIndex < dirty.Length; dirtyIndex++)
        {
            int fanout = 0;
            for (int applicationIndex = 0;
                 applicationIndex < _remoteApplications.Length; applicationIndex++)
                if (_remoteApplications[applicationIndex]?.IsSubscribedTo(dirty[dirtyIndex]) == true)
                    fanout++;
            SaturatingIncrement(ref _remoteDirtyChunksObserved);
            SaturatingAdd(ref _remoteDirtyRecipientFanout, checked((ulong)fanout));
            _remoteDirtyFanout!.Record(checked((ulong)fanout));
            int bucket = fanout switch
            {
                0 => 0, 1 => 1, <= 4 => 2, <= 8 => 3, <= 16 => 4,
                <= 32 => 5, <= 64 => 6, _ => 7,
            };
            SaturatingIncrement(ref _remoteDirtyFanoutBuckets[bucket]);
        }
        _remoteObservedCommit = commit;
    }

    private SharedRemoteReplicationCensusSnapshot CaptureRemoteReplicationCensus()
    {
        if (_tickTimings.Level != GameTelemetryLevel.Detailed) return default;
        var subscriptions = new GameValueDistributionWindow();
        var immediate = new GameValueDistributionWindow();
        var near = new GameValueDistributionWindow();
        var far = new GameValueDistributionWindow();
        var perChunk = new Dictionary<SpatialChunkKey, int>();
        var occupiedChunks = new HashSet<SpatialChunkKey>();
        int recipients = 0;
        int memberships = 0;
        for (int index = 0; index < _remoteApplications.Length; index++)
        {
            RemoteGuestPlayableApplication? application = _remoteApplications[index];
            if (application is null) continue;
            application.GetSubscriptionTopology(
                out int total, out int immediateCount, out int nearCount, out int farCount);
            recipients++;
            memberships = checked(memberships + total);
            subscriptions.Record(checked((ulong)total));
            immediate.Record(checked((ulong)immediateCount));
            near.Record(checked((ulong)nearCount));
            far.Record(checked((ulong)farCount));
            application.AccumulateSubscriptionCounts(perChunk);
            if (application.TryGetInterestAnchor(out WorldPosition anchor))
                occupiedChunks.Add(new SpatialChunkKey(GameWorldState.DefaultSpace, anchor.Chunk));
        }
        var recipientsPerChunk = new GameValueDistributionWindow();
        int multiplySubscribedChunks = 0;
        foreach (int count in perChunk.Values)
        {
            recipientsPerChunk.Record(checked((ulong)count));
            if (count > 1) multiplySubscribedChunks++;
        }
        return new SharedRemoteReplicationCensusSnapshot(
            _tickTimings.Level,
            _remoteSourceCommitsObserved,
            _remoteSourceCommitGaps,
            _remoteDirtyChunksObserved,
            _remoteDirtyRecipientFanout,
            _remoteDirtyFanout?.Capture() ?? default,
            new SharedDirtyFanoutBuckets(
                _remoteDirtyFanoutBuckets[0], _remoteDirtyFanoutBuckets[1],
                _remoteDirtyFanoutBuckets[2], _remoteDirtyFanoutBuckets[3],
                _remoteDirtyFanoutBuckets[4], _remoteDirtyFanoutBuckets[5],
                _remoteDirtyFanoutBuckets[6], _remoteDirtyFanoutBuckets[7]),
            recipients,
            perChunk.Count,
            memberships,
            subscriptions.Capture(), immediate.Capture(), near.Capture(), far.Capture(),
            recipientsPerChunk.Capture(), occupiedChunks.Count, multiplySubscribedChunks);
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private static void SaturatingAdd(ref ulong value, ulong amount) =>
        value = ulong.MaxValue - value < amount ? ulong.MaxValue : value + amount;

    /// <summary>Captures the one authoritative public-chunk source for cold diagnostics.</summary>
    public ChunkPublicationStoreSnapshot GetPublicationSnapshot()
    {
        EnsureOperational();
        if (!Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider,
                ChunkPublicationStoreSnapshot>(
                static (world, _, game) => game.WorldState.Publications.GetSnapshot(),
                out ChunkPublicationStoreSnapshot snapshot))
            throw new InvalidOperationException(
                "The active Game generation could not expose its publication source.");
        return snapshot;
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        if (_disposed) return;
        if (_remoteApplications.Any(static application => application is not null) ||
            HasOwnedParticipants())
            throw new InvalidOperationException(
                "Shared authority must retire every remote participant before disposal.");
        _disposed = true;
        ChatDelivery.Dispose();
        if (!_ownsRuntime)
        {
            Array.Clear(_participants);
            Array.Clear(_occupied);
            Array.Clear(_externallyOwned);
            _participantCount = 0;
            return;
        }
        Chat.Dispose();
        Admission.Dispose();
        if (Runtime.State == ServerRuntimeState.Running) Runtime.Stop();
        Runtime.Dispose();
        Array.Clear(_participants);
        Array.Clear(_occupied);
        Array.Clear(_externallyOwned);
        Array.Clear(_remoteApplications);
    }

    private bool HasOwnedParticipants()
    {
        for (int index = 0; index < _participants.Length; index++)
            if (_occupied[index] && !_externallyOwned[index]) return true;
        return false;
    }

    private int FindFreeSlot()
    {
        for (int slot = 0; slot < _occupied.Length; slot++)
            if (!_occupied[slot]) return slot;
        return -1;
    }

    private int FindRoundTripMilliseconds(ParticipantId participant)
    {
        for (int index = 0; index < _remoteApplications.Length; index++)
            if (_remoteApplications[index] is RemoteGuestPlayableApplication application &&
                application.Participant == participant)
                return application.GetSanitizedRoundTripMilliseconds();
        return 0;
    }

    private static WorldPosition SpawnForSlot(int slot)
    {
        int column = slot & 1;
        int row = slot >> 1;
        if (!WorldPosition.TryFromGlobalMillimeters(
                checked(GamePlayableWorld.PlayerStart.GlobalXMillimeters + column * 1_500L),
                GamePlayableWorld.PlayerStart.YMillimeters,
                checked(GamePlayableWorld.PlayerStart.GlobalZMillimeters + row * 1_500L),
                out WorldPosition position))
            throw new InvalidOperationException("The bounded shared spawn is unrepresentable.");
        return position;
    }

    private static Entity Resolve(EcsWorld world, PersistentEntityId id) =>
        world.TryResolvePersistentEntity(id, out Entity entity)
            ? entity
            : throw new InvalidOperationException($"Persistent entity {id} was unavailable.");

    private void EnsureOperational()
    {
        EnsureOwnerThread();
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The shared Game authority belongs to another thread.");
    }

    private readonly record struct JoinCapture(
        GamePlayableParticipantEntities Entities,
        WorldPublicEntityId PublicEntity);
}

/// <summary>
/// Hosted application factory whose connections become independent sessions in one borrowed
/// shared authority. The factory never owns or replaces that authority.
/// </summary>
public sealed class SharedRemoteGuestPlayableApplicationFactory : IHostedUdpSessionApplicationFactory
{
    private const int MaximumHotOpenUiDefinitions = 256;
    private readonly SharedPlayableAuthority _authority;
    private readonly ApplicationRealmId _realm;
    private readonly RemoteContentAuthorization? _contentAuthorization;
    private readonly RemoteContentAuthorization? _presentationAuthorization;
    private readonly RemoteStructuralWorldAuthorization? _structuralAuthorization;
    private readonly UiDefinitionIdentity[] _contentBackedUiDefinitions;
    private readonly UiDefinitionIdentity[] _standardUiDefinitions;
    private readonly RemoteClientProfileAdvertisement? _clientProfile;
    private readonly RemoteServerPerformanceProfile? _serverPerformanceProfile;
    private readonly RemoteChunkPublicationPolicy _publicationPolicy;
    private long _nextParticipant;

    public SharedRemoteGuestPlayableApplicationFactory(
        SharedPlayableAuthority authority,
        ApplicationRealmId realm,
        RemoteContentAuthorization? contentAuthorization = null,
        IEnumerable<UiDefinitionIdentity>? contentBackedUiDefinitions = null,
        RemoteClientProfileAdvertisement? clientProfile = null,
        IEnumerable<UiDefinitionIdentity>? standardUiDefinitions = null,
        RemoteServerPerformanceProfile? serverPerformanceProfile = null,
        RemoteChunkPublicationPolicy publicationPolicy = default,
        ulong firstParticipantId = 1,
        RemoteContentAuthorization? presentationAuthorization = null,
        RemoteStructuralWorldAuthorization? structuralAuthorization = null)
    {
        _authority = authority ?? throw new ArgumentNullException(nameof(authority));
        if (!realm.IsValid) throw new ArgumentOutOfRangeException(nameof(realm));
        if (firstParticipantId == 0 || firstParticipantId > long.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(firstParticipantId));
        if (contentAuthorization is not null &&
            (!contentAuthorization.IsAuthorityValid ||
             contentAuthorization.RealmHigh != realm.High ||
             contentAuthorization.RealmLow != realm.Low))
            throw new ArgumentException(
                "The required content authorization must belong to the shared realm.",
                nameof(contentAuthorization));
        _realm = realm;
        _contentAuthorization = contentAuthorization;
        if (presentationAuthorization is not null &&
            (presentationAuthorization.RealmHigh != realm.High ||
             presentationAuthorization.RealmLow != realm.Low))
            throw new ArgumentException("Presentation authorization must belong to this realm.",
                nameof(presentationAuthorization));
        _presentationAuthorization = presentationAuthorization;
        if (structuralAuthorization is not null && contentAuthorization is null)
            throw new ArgumentException(
                "Structural World authorization requires exact application Content authorization.",
                nameof(structuralAuthorization));
        if (structuralAuthorization is not null &&
            !MatchesCurrentStructuralAuthority(authority, structuralAuthorization))
            throw new ArgumentException(
                "Remote structural deployment cannot represent this authority's effective World.",
                nameof(structuralAuthorization));
        _structuralAuthorization = structuralAuthorization;
        _contentBackedUiDefinitions = CopyDefinitions(
            contentBackedUiDefinitions, nameof(contentBackedUiDefinitions));
        _standardUiDefinitions = CopyDefinitions(
            standardUiDefinitions, nameof(standardUiDefinitions));
        if (_contentBackedUiDefinitions.Length != 0 && contentAuthorization is null)
            throw new ArgumentException(
                "Content-backed UI requires exact authenticated content authorization.",
                nameof(contentBackedUiDefinitions));
        if (_contentBackedUiDefinitions.Length + _standardUiDefinitions.Length >
                MaximumHotOpenUiDefinitions ||
            _contentBackedUiDefinitions.Intersect(_standardUiDefinitions).Any())
            throw new ArgumentException(
                "The shared hot-open definition sets exceed capacity or overlap.",
                nameof(standardUiDefinitions));
        _clientProfile = clientProfile;
        _serverPerformanceProfile = serverPerformanceProfile;
        if (publicationPolicy == default)
            publicationPolicy = RemoteChunkPublicationPolicy.CurrentBundled;
        publicationPolicy.Validate();
        _publicationPolicy = publicationPolicy;
        _nextParticipant = checked((long)firstParticipantId - 1);
    }

    public IHostedUdpSessionApplication Create(ConnectionId connection, NetworkProtocolPeer peer)
    {
        ArgumentNullException.ThrowIfNull(peer);
        // An authority overlay may have changed since deployment construction. W7 has no
        // overlay transport, so a later session must not enter with stale base-Pack truth.
        if (_structuralAuthorization is not null &&
            !MatchesCurrentStructuralAuthority(_authority, _structuralAuthorization))
            throw new InvalidOperationException(
                "Remote structural admission cannot represent the current effective World.");
        long value = Interlocked.Increment(ref _nextParticipant);
        if (value <= 0)
            throw new InvalidOperationException("The shared participant identity space is exhausted.");
        var application = new RemoteGuestPlayableApplication(
            peer,
            _realm,
            new ParticipantId(checked((ulong)value)),
            CreateEpoch(),
            _contentAuthorization,
            _contentBackedUiDefinitions,
            _clientProfile,
            _standardUiDefinitions,
            _authority,
            _serverPerformanceProfile,
            _publicationPolicy,
            _presentationAuthorization,
            _structuralAuthorization);
        try
        {
            _authority.RegisterRemoteApplication(application);
            return application;
        }
        catch
        {
            application.Dispose();
            throw;
        }
    }

    private static bool MatchesCurrentStructuralAuthority(SharedPlayableAuthority authority,
        RemoteStructuralWorldAuthorization authorization) =>
        authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) => authorization.MatchesUnmodifiedAuthority(
                game.WorldState.PreparedComposition), out bool exactAuthority) &&
        exactAuthority;

    private static UiDefinitionIdentity[] CopyDefinitions(
        IEnumerable<UiDefinitionIdentity>? source,
        string parameterName)
    {
        UiDefinitionIdentity[] values = (source ?? [])
            .Take(MaximumHotOpenUiDefinitions + 1).ToArray();
        if (values.Length > MaximumHotOpenUiDefinitions ||
            values.Any(static value => !value.IsValid) ||
            values.Distinct().Count() != values.Length)
            throw new ArgumentException(
                "The shared UI definition set is invalid, duplicated, or over capacity.",
                parameterName);
        return values;
    }

    private static ulong CreateEpoch()
    {
        Span<byte> random = stackalloc byte[sizeof(ulong)];
        ulong epoch;
        do
        {
            System.Security.Cryptography.RandomNumberGenerator.Fill(random);
            epoch = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(random);
        } while (epoch == 0);
        return epoch;
    }
}
