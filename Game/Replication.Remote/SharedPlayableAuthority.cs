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
    private readonly SharedPlayableAuthorityLimits _limits;
    private readonly SharedPlayableParticipant[] _participants;
    private readonly bool[] _occupied;
    private readonly bool[] _externallyOwned;
    private readonly RemoteGuestPlayableApplication?[] _remoteApplications;
    private readonly GamePublicActorActionKind[] _desiredPresentationActions;
    private readonly ulong[] _desiredPresentationActionSequences;
    private readonly GamePublicActorPresentation[] _actorPresentations;
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
    private bool _disposed;

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
        int simulationFrequencyHz = WorldClockOptions.DefaultSimulationFrequencyHz)
    {
        if (limits == default) limits = SharedPlayableAuthorityLimits.Default;
        limits.Validate();
        long generationStartedTimestamp = Stopwatch.GetTimestamp();
        WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
        var composition = new UltraSimGameComposition(
            limits.RequiredInventoryLimits,
            runtimeProfile: GameRuntimeProfile.Networked,
            baseWorldPack: pack);
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
            return new SharedPlayableAuthority(
                pack, composition, runtime, admission, chat, delivery, scene,
                ownsRuntime: true, tickTimings, generationStartedTimestamp, limits);
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
        return true;
    }

    /// <summary>Copies the current bounded public actor appearance/action read model.</summary>
    public int CopyActorPresentations(Span<GamePublicActorPresentation> destination)
    {
        EnsureOperational();
        if (destination.Length < _participantCount) return -1;
        bool captured = Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                for (int slot = 0; slot < _participants.Length; slot++)
                {
                    if (!_occupied[slot]) continue;
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

                    ref GamePublicActorPresentation retained = ref _actorPresentations[slot];
                    GamePublicActorActionKind action = _desiredPresentationActions[slot];
                    ulong actionSequence = _desiredPresentationActionSequences[slot];
                    if (!retained.IsValid || retained.Participant != participant ||
                        retained.MainHandPrefabId != mainHandPrefab ||
                        retained.VerticalPhase != verticalPhase ||
                        retained.Action != action || retained.ActionSequence != actionSequence)
                    {
                        ulong revision = retained.IsValid &&
                            retained.Participant.PublicEntity == participant.PublicEntity
                                ? checked(retained.Revision + 1)
                                : 1;
                        retained = new GamePublicActorPresentation(
                            participant, revision, mainHandPrefab, verticalPhase,
                            action, actionSequence);
                    }
                }
                return true;
            }, out bool complete) && complete;
        if (!captured) return -1;
        int count = 0;
        for (int slot = 0; slot < _actorPresentations.Length; slot++)
            if (_occupied[slot]) destination[count++] = _actorPresentations[slot];
        return count == _participantCount ? count : -1;
    }

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
        ulong firstParticipantId = 1)
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
            _publicationPolicy);
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
