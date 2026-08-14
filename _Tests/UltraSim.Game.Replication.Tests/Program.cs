using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Replication;
using UltraSim.Server.Runtime;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("NetworkProfileIsExplicitAndLocalProfilePaysNoLifecycleReservation", NetworkProfileIsExplicit),
    ("BaselineAndOfflineDirectPublishCanonicalTruth", BaselineAndOfflineDirectPublishCanonicalTruth),
    ("ChangedConsumersIngressAndWarmedCaptureAreIndependentAndBounded", ChangedConsumersIngressAndAllocation),
    ("PostGraphLifecycleAndRapidMembershipChangesAreCaptured", PostGraphLifecycleAndRapidMembershipChanges),
    ("CapacityAndUnexpectedProjectionFailuresRemainContained", CapacityAndUnexpectedFailuresRemainContained),
    ("LifecycleHistoryOverflowRequiresBaseline", LifecycleHistoryOverflowRequiresBaseline),
    ("GenerationReplacementRebuildsEpochAndInvalidatesAccountlessAuthority", GenerationReplacementRebuildsEpoch),
    ("GenerationReplacementRebindsDurableAdmissionAuthority", GenerationReplacementRebindsDurableAdmission),
    ("ApplicationEventsAreIndependentBoundedFacts", ApplicationEventsAreIndependentBoundedFacts),
    ("OfflineDirectRejectsInvalidFramesAtomically", OfflineDirectRejectsInvalidFramesAtomically),
    ("CanonicalProjectionAndSemanticTraceAreDeterministic", CanonicalProjectionAndTraceAreDeterministic),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} Network N0.1 game-replication tests passed.");

static void NetworkProfileIsExplicit()
{
    EcsWorldOptions local = UltraSimGameComposition.CreateWorldOptions(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x9101)),
        GameRuntimeProfile.Local);
    EcsWorldOptions networked = UltraSimGameComposition.CreateWorldOptions(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(0x9102)),
        GameRuntimeProfile.Networked);
    Check.Equal(0, local.LifecycleRecordCapacity);
    Check.Equal(UltraSimGameComposition.NetworkLifecycleRecordCapacity, networked.LifecycleRecordCapacity);

    using var fixture = new Fixture(GameRuntimeProfile.Local, 0x9103, 64);
    using var projection = new GameProjectionService(fixture.Runtime.Game, fixture.Admission,
        Limits(maximumEntities: 64));
    GameProjectionCaptureResult result = projection.RebuildBaseline();
    Check.Equal(GameProjectionCaptureStatus.ReplicationNotConfigured, result.Status);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
}

static void BaselineAndOfflineDirectPublishCanonicalTruth()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9201, 64);
    ParticipantId participant = new(11);
    AdmissionResult entered = fixture.Admission.JoinAccountless(participant);
    Check.True(entered.IsEntered);
    using var projection = fixture.CreateProjection();
    Check.True(projection.SynchronizeParticipant(participant));
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, baseline.Status);
    Check.True(baseline.Frame.StateEpoch.IsValid);
    Check.Equal(1, projection.GetSnapshot().EntityCount);
    Check.Equal(1, projection.GetSnapshot().ParticipantCount);

    var offline = new OfflineDirectGameProjection(64, 8, 512);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyBaseline(projection, baseline.Frame));
    OfflineProjectionSnapshot snapshot = offline.GetSnapshot();
    Check.Equal(baseline.Frame.StateEpoch, snapshot.StateEpoch);
    Check.Equal(1, snapshot.EntityCount);
    Check.Equal(1, snapshot.ParticipantCount);
    Span<GameProjectionParticipant> bindings = stackalloc GameProjectionParticipant[1];
    Check.Equal(1, offline.CopyParticipants(bindings));
    Check.Equal(participant, bindings[0].Participant);
    Check.True(bindings[0].ControlledEntity.IsValid);
}

static void ChangedConsumersIngressAndAllocation()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9301, 64);
    ParticipantId participant = new(21);
    AdmissionResult entered = fixture.Admission.JoinAccountless(participant);
    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 4_096);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));

    ChangeCursor independent = default;
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, _) => { independent = world.CreateChangeCursorFromNow(); return true; }, out bool cursorReady));
    Check.True(cursorReady);

    Check.True(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(participant, 1, 7)));
    GameAuthoritativeIngressResult ingress = projection.PublishAuthoritativeIngress();
    Check.Equal(GameAuthoritativeIngressStatus.Success, ingress.Status);
    Check.Equal(1, ingress.Accepted);
    fixture.Runtime.Tick();
    GameProjectionCaptureResult captured = projection.CaptureAfterTick();
    Check.True(captured.Succeeded);
    Span<GameProjectionDelta> deltas = stackalloc GameProjectionDelta[64];
    int deltaCount = projection.CopyLastFrameDeltas(deltas);
    Check.True(deltaCount >= 1);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, captured.Frame, deltas[..deltaCount]));
    Check.Equal(8, ReadSimulation(fixture, entered.Entity).TickMarker);

    int independentlyDelivered = 0;
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, game) =>
        {
            var visitor = new CountingChangedVisitor();
            independentlyDelivered = game.Replication!.ExecuteChanges(world, ref independent, ref visitor);
            return true;
        }, out bool independentRead));
    Check.True(independentRead);
    Check.Equal(1, independentlyDelivered);

    // Warm delegates, scheduler paths, projection staging, and OfflineDirect before allocation measurement.
    for (int index = 0; index < 4; index++)
        TickCaptureApply(fixture, projection, offline, deltas);
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int index = 0; index < 16; index++)
        TickCaptureApply(fixture, projection, offline, deltas);
    long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Check.Equal(0L, allocated);

    Check.True(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(participant, 1, 99)));
    GameAuthoritativeIngressResult duplicate = projection.PublishAuthoritativeIngress();
    Check.Equal(0, duplicate.Accepted);
    Check.Equal(1, duplicate.Rejected);
    Check.False(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(
        participant,
        2,
        (GameSimulationCommandKind)byte.MaxValue,
        0)));

    Check.True(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(participant, 3, 1)));
    Check.True(projection.TryEnqueueAuthoritativeInput(new GameAuthoritativeInput(participant, 2, 100)));
    GameAuthoritativeIngressResult ordered = projection.PublishAuthoritativeIngress();
    Check.Equal(1, ordered.Accepted);
    Check.Equal(1, ordered.Rejected);
    TickCaptureApply(fixture, projection, offline, deltas);
}

static void PostGraphLifecycleAndRapidMembershipChanges()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9401, 64);
    ParticipantId participant = new(31);
    AdmissionResult entered = fixture.Admission.JoinAccountless(participant);
    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 2_048);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));
    Span<GameProjectionDelta> deltas = stackalloc GameProjectionDelta[64];

    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 1, GameSimulationCommandKind.RemoveSimulationState, 0));
    GameProjectionCaptureResult removed = projection.CaptureAfterTick();
    int removedCount = projection.CopyLastFrameDeltas(deltas);
    Check.Contains(deltas[..removedCount], GameProjectionDeltaKind.ComponentRemoved);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, removed.Frame, deltas[..removedCount]));

    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 2, GameSimulationCommandKind.AddSimulationState, 41));
    GameProjectionCaptureResult added = projection.CaptureAfterTick();
    int addedCount = projection.CopyLastFrameDeltas(deltas);
    Check.Contains(deltas[..addedCount], GameProjectionDeltaKind.ComponentAdded);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, added.Frame, deltas[..addedCount]));
    Check.True(ReadSimulation(fixture, entered.Entity).TickMarker >= 41);

    // Remove then add before one capture: both committed topology facts remain
    // ordered while canonical state folds to the final present value.
    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 3, GameSimulationCommandKind.RemoveSimulationState, 0));
    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 4, GameSimulationCommandKind.AddSimulationState, 73));
    GameProjectionCaptureResult folded = projection.CaptureAfterTick();
    int foldedCount = projection.CopyLastFrameDeltas(deltas);
    Check.Contains(deltas[..foldedCount], GameProjectionDeltaKind.ComponentRemoved);
    Check.Contains(deltas[..foldedCount], GameProjectionDeltaKind.ComponentAdded);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, folded.Frame, deltas[..foldedCount]));

    EnqueueAndTick(fixture, projection,
        new GameAuthoritativeInput(participant, 5, GameSimulationCommandKind.RetireControlledEntity, 0));
    GameProjectionCaptureResult destroyed = projection.CaptureAfterTick();
    int destroyedCount = projection.CopyLastFrameDeltas(deltas);
    Check.Contains(deltas[..destroyedCount], GameProjectionDeltaKind.Destroyed);
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyFrame(projection, destroyed.Frame, deltas[..destroyedCount]));
    Check.Equal(0, projection.GetSnapshot().EntityCount);
    Check.Equal(0, offline.GetSnapshot().EntityCount);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
}

static void CapacityAndUnexpectedFailuresRemainContained()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9501, 64);
    using var projection = new GameProjectionService(
        fixture.Runtime.Game,
        fixture.Admission,
        new GameReplicationLimits(64, 8, 1, 1, 8, 8, 8));
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, projection.RebuildBaseline().Status);
    fixture.Admission.JoinAccountless(new ParticipantId(41));
    fixture.Admission.JoinAccountless(new ParticipantId(42));
    GameProjectionCaptureResult overflow = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.CapacityExceeded, overflow.Status);
    Check.Equal(0, projection.GetSnapshot().EntityCount);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
    // A repeated failure proves the uncommitted source cursor was not advanced.
    Check.Equal(GameProjectionCaptureStatus.CapacityExceeded, projection.CaptureAfterTick().Status);

    using var faultFixture = new Fixture(GameRuntimeProfile.Networked, 0x9502, 64);
    faultFixture.Admission.JoinAccountless(new ParticipantId(43));
    using var faulted = faultFixture.CreateProjection();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, faulted.RebuildBaseline().Status);
    faultFixture.Runtime.Tick();
    faulted.FaultInjector = point =>
    {
        if (point == GameProjectionFaultPoint.BeforeCaptureCommit)
            throw new InvalidOperationException("injected derived-state failure");
    };
    GameProjectionCaptureResult failure = faulted.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.Faulted, failure.Status);
    Check.True(faulted.IsFaulted);
    Check.Equal(ServerRuntimeState.Running, faultFixture.Runtime.State);
}

static void LifecycleHistoryOverflowRequiresBaseline()
{
    const int population = UltraSimGameComposition.NetworkLifecycleRecordCapacity + 1;
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9601, population + 16);
    using var projection = new GameProjectionService(
        fixture.Runtime.Game,
        fixture.Admission,
        new GameReplicationLimits(population + 16, 8, population + 8, population + 8, 32, 8, 8));
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, projection.RebuildBaseline().Status);
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, generationId, game) =>
        {
            for (int index = 0; index < population; index++)
                if (!world.TryInstantiate(game.Content.Character, out _)) return false;
            return true;
        }, out bool populated));
    Check.True(populated);
    GameProjectionCaptureResult overflow = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.LifecycleResyncRequired, overflow.Status);
    Check.True(overflow.RequiresBaseline);
    Check.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, projection.RebuildBaseline().Status);
    Check.Equal(population, projection.GetSnapshot().EntityCount);
}

static void ApplicationEventsAreIndependentBoundedFacts()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9701, 64);
    ParticipantId participant = new(51);
    fixture.Admission.JoinAccountless(participant);
    using var projection = new GameProjectionService(
        fixture.Runtime.Game,
        fixture.Admission,
        new GameReplicationLimits(64, 8, 64, 128, 8, 2, 8));
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    Check.True(projection.TryPublishApplicationEvent(participant, 1, 10, 100, out GameApplicationEvent first));
    Check.True(projection.TryPublishApplicationEvent(participant, 2, 20, 200, out GameApplicationEvent second));
    Check.False(projection.TryPublishApplicationEvent(participant, 3, 30, 300, out _));
    Span<GameApplicationEvent> events = stackalloc GameApplicationEvent[2];
    GameProjectionHistoryReadResult read = projection.ReadApplicationEvents(0, events);
    Check.Equal(GameProjectionHistoryReadStatus.Success, read.Status);
    Check.Equal(first, events[0]);
    Check.Equal(second, events[1]);
    var offline = new OfflineDirectGameProjection(64, 8, 32);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyApplicationEvents(events));
    projection.DiscardApplicationEventsThrough(read.NextSequence);
    Check.True(projection.TryPublishApplicationEvent(participant, 3, 30, 300, out _));
}

static void GenerationReplacementRebindsDurableAdmission()
{
    const ulong persistentWorld = 0x9682;
    var ui = new TestUiPort();
    using var fixture = new Fixture(GameRuntimeProfile.Networked, persistentWorld, 64, ui);
    ParticipantId participant = new(48);
    AccountId accountId = new(480);
    CharacterId characterId = new(481);
    var identity = new TrustedLocalIdentity("network-rebind");
    CharacterRecord character = PopulateDurableCharacter(
        fixture.Runtime,
        accountId,
        characterId,
        identity);
    AdmissionResult waiting = fixture.Admission.JoinAccountBacked(participant, identity);
    Check.True(waiting.IsWaitingForCharacter);
    ui.Submit(new UiActionRequest(
        waiting.CharacterSelectView,
        CharacterSelectUi.Definition.Handle,
        CharacterSelectUi.Play,
        new UiRowId(characterId.Value)));
    Check.True(fixture.Admission.TryGetSession(participant, out AdmissionResult entered));
    Check.True(entered.IsEntered);

    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    Span<GameProjectionParticipant> beforeBindings = stackalloc GameProjectionParticipant[1];
    Check.Equal(1, projection.CopyParticipants(beforeBindings));

    GameStaging staging = fixture.Composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(persistentWorld)));
    staging.World.Seal();
    staging.World.Initialize();
    staging.World.BeginRun();
    Check.True(staging.World.TryInstantiate(staging.Content.Character, out Entity reboundEntity));
    PersistentEntityId reboundPersistentId = staging.World.GetOrAssignPersistentId(reboundEntity);
    Check.Equal(character.PersistentEntityKey.Entity, reboundPersistentId);
    var accounting = new AccountingStore();
    accounting.AddAccount(new AccountRecord(accountId));
    accounting.BindTrustedIdentity(identity, accountId);
    accounting.AddCharacter(character);
    var candidate = new GameGenerationState(
        accounting,
        staging.Inventory,
        staging.Content,
        staging.SimulationLookup,
        staging.Replication);
    Check.True(fixture.Runtime.Game.TryPublishPreparedGeneration(
        staging.World,
        candidate,
        out ulong replacementGeneration));
    Check.True(fixture.Admission.TryGetSession(participant, out AdmissionResult rebound));
    Check.True(rebound.IsEntered);
    Check.Equal(replacementGeneration, rebound.GenerationId);
    Check.Equal(reboundEntity, rebound.Entity);

    GameProjectionCaptureResult replacement = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, replacement.Status);
    Span<GameProjectionParticipant> afterBindings = stackalloc GameProjectionParticipant[1];
    Check.Equal(1, projection.CopyParticipants(afterBindings));
    Check.Equal(participant, afterBindings[0].Participant);
    Check.True(afterBindings[0].ControlledEntity.IsValid);
    Check.Equal(replacementGeneration, afterBindings[0].ServerGenerationId);
    Check.False(replacement.Frame.StateEpoch == baseline.Frame.StateEpoch);
}

static void GenerationReplacementRebuildsEpoch()
{
    const ulong persistentWorld = 0x9681;
    using var fixture = new Fixture(GameRuntimeProfile.Networked, persistentWorld, 64);
    ParticipantId participant = new(47);
    AdmissionResult entered = fixture.Admission.JoinAccountless(participant);
    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 128);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));

    GameStaging staging = fixture.Composition.CreateFresh(
        64,
        new PersistenceIdentityOptions(new PersistentWorldId(persistentWorld)));
    staging.World.Seal();
    staging.World.Initialize();
    staging.World.BeginRun();
    var candidate = new GameGenerationState(
        new UltraSim.Game.Accounting.AccountingStore(),
        staging.Inventory,
        staging.Content,
        staging.SimulationLookup,
        staging.Replication);
    Check.True(fixture.Runtime.Game.TryPublishPreparedGeneration(
        staging.World,
        candidate,
        out ulong replacementGeneration));
    Check.True(replacementGeneration > baseline.Frame.ServerGenerationId);
    Check.True(fixture.Admission.TryGetSession(participant, out AdmissionResult rejected));
    Check.True(rejected.IsRejected);
    Check.Equal(AdmissionRejection.GenerationReplaced, rejected.Rejection);

    GameProjectionCaptureResult replacement = projection.CaptureAfterTick();
    Check.Equal(GameProjectionCaptureStatus.BaselineRebuilt, replacement.Status);
    Check.False(replacement.Frame.StateEpoch == baseline.Frame.StateEpoch);
    Check.Equal(0, projection.GetSnapshot().EntityCount);
    Check.Equal(0, projection.GetSnapshot().ParticipantCount);
    Check.Equal(OfflineProjectionApplyStatus.BaselineRequired,
        offline.ApplyFrame(projection, replacement.Frame, ReadOnlySpan<GameProjectionDelta>.Empty));
    Check.Equal(OfflineProjectionApplyStatus.Success,
        offline.ApplyBaseline(projection, replacement.Frame));
    Check.Equal(0, offline.GetSnapshot().EntityCount);
    Check.False(entered.Entity == default);
}

static void OfflineDirectRejectsInvalidFramesAtomically()
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, 0x9801, 64);
    fixture.Admission.JoinAccountless(new ParticipantId(61));
    using var projection = fixture.CreateProjection();
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 128);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));
    OfflineProjectionSnapshot before = offline.GetSnapshot();
    var invalid = new GameProjectionFrame(
        baseline.Frame.StateEpoch,
        baseline.Frame.ServerGenerationId,
        baseline.Frame.SimulationTick,
        99,
        100,
        0,
        0, 0, 0, 0);
    Check.Equal(OfflineProjectionApplyStatus.BaselineRequired,
        offline.ApplyFrame(projection, invalid, ReadOnlySpan<GameProjectionDelta>.Empty));
    Check.Equal(before, offline.GetSnapshot());
}

static void CanonicalProjectionAndTraceAreDeterministic()
{
    OfflineProjectionSnapshot left = RunDeterministicScenario(0x9901);
    OfflineProjectionSnapshot right = RunDeterministicScenario(0x9902);
    Check.Equal(left.EntityCount, right.EntityCount);
    Check.Equal(left.ParticipantCount, right.ParticipantCount);
    Check.Equal(left.CanonicalHash, right.CanonicalHash);
    Check.Equal(left.SemanticTraceDigest, right.SemanticTraceDigest);
}

static OfflineProjectionSnapshot RunDeterministicScenario(ulong worldIdentity)
{
    using var fixture = new Fixture(GameRuntimeProfile.Networked, worldIdentity, 64);
    ParticipantId participant = new(71);
    fixture.Admission.JoinAccountless(participant);
    using var projection = fixture.CreateProjection();
    projection.SynchronizeParticipant(participant);
    GameProjectionCaptureResult baseline = projection.RebuildBaseline();
    var offline = new OfflineDirectGameProjection(64, 8, 512);
    Check.Equal(OfflineProjectionApplyStatus.Success, offline.ApplyBaseline(projection, baseline.Frame));
    Span<GameProjectionDelta> deltas = stackalloc GameProjectionDelta[64];
    for (int tick = 0; tick < 5; tick++)
    {
        if (tick == 2)
        {
            Check.True(projection.TryEnqueueAuthoritativeInput(
                new GameAuthoritativeInput(participant, 1, 5)));
            Check.Equal(GameAuthoritativeIngressStatus.Success,
                projection.PublishAuthoritativeIngress().Status);
        }
        fixture.Runtime.Tick();
        GameProjectionCaptureResult frame = projection.CaptureAfterTick();
        int count = projection.CopyLastFrameDeltas(deltas);
        Check.Equal(OfflineProjectionApplyStatus.Success,
            offline.ApplyFrame(projection, frame.Frame, deltas[..count]));
    }
    return offline.GetSnapshot();
}

static void TickCaptureApply(
    Fixture fixture,
    GameProjectionService projection,
    OfflineDirectGameProjection offline,
    Span<GameProjectionDelta> scratch)
{
    fixture.Runtime.Tick();
    GameProjectionCaptureResult capture = projection.CaptureAfterTick();
    if (!capture.Succeeded) throw new InvalidOperationException("Capture failed during allocation gate.");
    int count = projection.CopyLastFrameDeltas(scratch);
    if (offline.ApplyFrame(projection, capture.Frame, scratch[..count]) != OfflineProjectionApplyStatus.Success)
        throw new InvalidOperationException("Offline apply failed during allocation gate.");
}

static void EnqueueAndTick(
    Fixture fixture,
    GameProjectionService projection,
    in GameAuthoritativeInput input)
{
    Check.True(projection.TryEnqueueAuthoritativeInput(input));
    GameAuthoritativeIngressResult ingress = projection.PublishAuthoritativeIngress();
    Check.Equal(GameAuthoritativeIngressStatus.Success, ingress.Status);
    Check.Equal(1, ingress.Accepted);
    fixture.Runtime.Tick();
}

static GameSimulationState ReadSimulation(Fixture fixture, Entity entity)
{
    GameSimulationState result = default;
    Check.True(fixture.Runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, game) => game.TryReadSimulationState(world, entity, out result), out bool read));
    Check.True(read);
    return result;
}

static CharacterRecord PopulateDurableCharacter(
    ServerRuntime runtime,
    AccountId accountId,
    CharacterId characterId,
    TrustedLocalIdentity identity)
{
    CharacterRecord record = default;
    Check.True(runtime.Game.TryUseActiveGeneration<GameGenerationState, bool>(
        (world, _, game) =>
        {
            if (!world.TryInstantiate(game.Content.Character, out Entity entity)) return false;
            var key = new PersistentEntityKey(world.PersistentWorldId, world.GetOrAssignPersistentId(entity));
            game.Accounting.AddAccount(new AccountRecord(accountId));
            game.Accounting.BindTrustedIdentity(identity, accountId);
            record = new CharacterRecord(
                characterId,
                accountId,
                slot: 0,
                displayName: "Network Character",
                GamePrefabKeys.Character,
                key);
            game.Accounting.AddCharacter(record);
            return true;
        }, out bool populated));
    Check.True(populated);
    return record;
}

static GameReplicationLimits Limits(int maximumEntities) =>
    new(maximumEntities, 8, 128, 256, 256, 64, 64);

sealed class Fixture : IDisposable
{
    internal Fixture(
        GameRuntimeProfile profile,
        ulong worldIdentity,
        int entityCapacity,
        IUiSemanticPort? ui = null)
    {
        Profile = profile;
        Composition = new UltraSimGameComposition(runtimeProfile: profile);
        EcsWorldOptions world = UltraSimGameComposition.CreateWorldOptions(
            entityCapacity,
            new PersistenceIdentityOptions(new PersistentWorldId(worldIdentity)),
            profile);
        Runtime = new ServerRuntime(Composition, new ServerRuntimeOptions(world, WorldClockOptions.Default));
        Runtime.Initialize();
        Runtime.Start();
        Admission = new AdmissionService(Runtime.Game, Composition, ui);
    }

    internal GameRuntimeProfile Profile { get; }
    internal UltraSimGameComposition Composition { get; }
    internal ServerRuntime Runtime { get; }
    internal AdmissionService Admission { get; }

    internal GameProjectionService CreateProjection() =>
        new(Runtime.Game, Admission,
            new GameReplicationLimits(RuntimeOptions.MaximumEntities, 8, 128, 256, 256, 64, 64));

    private EntityCapacity RuntimeOptions => Runtime.Game.TryUseActiveGeneration(
        static (world, _) => world.Options.EntityCapacity,
        out EntityCapacity capacity) ? capacity : throw new InvalidOperationException("Runtime is not running.");

    public void Dispose()
    {
        Admission.Dispose();
        Runtime.Dispose();
    }
}

sealed class TestUiPort : IUiSemanticPort
{
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];
    private ulong _nextView = 1;

    public UiViewInstanceId OpenView(UiOpenView operation) => new(_nextView++);
    public UiViewInstanceId OpenHotView(UiHotOpenView operation) => new(_nextView++);
    public void UpdateView(UiUpdateView operation) { }
    public bool CloseView(UiCloseView operation) => true;
    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
        _actions.Add(instance, handler);
    public void UnbindActions(UiViewInstanceId instance) => _actions.Remove(instance);
    internal void Submit(in UiActionRequest request)
    {
        if (_actions.TryGetValue(request.Instance, out Action<UiActionRequest>? action)) action(request);
    }
}

struct CountingChangedVisitor : IReadOnlyChunkJob<GameSimulationState>
{
    public void Execute(in ReadOnlyComponentChunk<GameSimulationState> chunk)
    {
    }
}

static class Check
{
    internal static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }

    internal static void False(bool condition)
    {
        if (condition) throw new InvalidOperationException("Expected false.");
    }

    internal static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    internal static void Contains(ReadOnlySpan<GameProjectionDelta> values, GameProjectionDeltaKind kind)
    {
        foreach (ref readonly GameProjectionDelta value in values)
            if (value.Kind == kind) return;
        throw new InvalidOperationException($"Expected projection delta {kind}.");
    }
}
