using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Persistence;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;

var tests = new (string Name, Action Body)[]
{
    ("AccountingArtifactIsDeterministic", AccountingArtifactIsDeterministic),
    ("AccountingArtifactBoundsAndCanonicalEncodingAreStrict", AccountingArtifactBoundsAndCanonicalEncodingAreStrict),
    ("DuplicateDurableCharacterLinkIsRejected", DuplicateDurableCharacterLinkIsRejected),
    ("ClockArtifactUsesFrequencyIndependentTimeAndReadsExactLegacyState", ClockArtifactUsesFrequencyIndependentTimeAndReadsExactLegacyState),
    ("RealRuntimeSaveLoadPublishesEcsAndAccountingTogether", RealRuntimeSaveLoadPublishesEcsAndAccountingTogether),
    ("SaveRestoreRecaptureIsCanonical", SaveRestoreRecaptureIsCanonical),
    ("MissingCharacterEntityLeavesActiveGenerationUntouched", MissingCharacterEntityLeavesActiveGenerationUntouched),
    ("AccountingPrepareFailureLeavesActiveGenerationUntouched", AccountingPrepareFailureLeavesActiveGenerationUntouched),
    ("AccountingDomainValidationFailureLeavesActiveGenerationUntouched", AccountingDomainValidationFailureLeavesActiveGenerationUntouched),
    ("ParticipantSequenceMismatchLeavesActiveGenerationUntouched", ParticipantSequenceMismatchLeavesActiveGenerationUntouched),
    ("EcsPrepareFailureLeavesActiveGenerationUntouched", EcsPrepareFailureLeavesActiveGenerationUntouched),
    ("CandidateConstructionFailureLeavesActiveGenerationUntouched", CandidateConstructionFailureLeavesActiveGenerationUntouched),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} S0.6 Game Persistence tests passed.");

static void AccountingArtifactIsDeterministic()
{
    using Fixture fixture = CreateFixture();
    var participant = new AccountingSaveSetParticipant(fixture.Accounting);

    byte[] first = Capture(participant, new SaveSetContext(7, 700));
    byte[] second = Capture(participant, new SaveSetContext(7, 700));
    Assert.SequenceEqual(first, second);

    var prepared = (PreparedAccountingParticipant)participant.Prepare(first, new SaveSetContext(7, 700));
    var resolver = new StaticResolver(fixture.Key, fixture.Entity);
    prepared.Resolve(resolver);
    prepared.Validate();
    Assert.Equal(fixture.Entity, prepared.ResolvedEntities[new CharacterId(1)]);
    prepared.Dispose();

    using var forward = new AccountingStore();
    forward.AddAccount(new AccountRecord(new AccountId(1)));
    forward.AddAccount(new AccountRecord(new AccountId(2)));
    forward.BindTrustedIdentity(new TrustedLocalIdentity("alpha"), new AccountId(1));
    forward.BindTrustedIdentity(new TrustedLocalIdentity("beta"), new AccountId(2));
    using var reverse = new AccountingStore();
    reverse.AddAccount(new AccountRecord(new AccountId(2)));
    reverse.AddAccount(new AccountRecord(new AccountId(1)));
    reverse.BindTrustedIdentity(new TrustedLocalIdentity("beta"), new AccountId(2));
    reverse.BindTrustedIdentity(new TrustedLocalIdentity("alpha"), new AccountId(1));
    Assert.SequenceEqual(
        Capture(new AccountingSaveSetParticipant(forward), new SaveSetContext(8, 800)),
        Capture(new AccountingSaveSetParticipant(reverse), new SaveSetContext(8, 800)));
}

static void AccountingArtifactBoundsAndCanonicalEncodingAreStrict()
{
    using Fixture fixture = CreateFixture();
    var participant = new AccountingSaveSetParticipant(fixture.Accounting);
    byte[] valid = Capture(participant, new SaveSetContext(7, 700));

    Assert.Throws<ArgumentOutOfRangeException>(() => _ = new AccountingPersistenceOptions(
        maximumAccounts: AccountingPersistenceOptions.HardMaximumRecordsPerSection + 1));
    var byteRestricted = new AccountingSaveSetParticipant(
        fixture.Accounting,
        new AccountingPersistenceOptions(maximumArtifactBytes: 32));
    Assert.Throws<PersistenceValidationException>(() =>
        byteRestricted.Prepare(valid, new SaveSetContext(7, 700)));

    byte[] countOnly = BuildAccountingArtifact(
        sequence: 7,
        accounts:
        [
            new AccountRecord(new AccountId(1)),
            new AccountRecord(new AccountId(2)),
        ],
        identities: [],
        characters: []);
    var countRestricted = new AccountingSaveSetParticipant(
        fixture.Accounting,
        new AccountingPersistenceOptions(maximumAccounts: 1));
    PersistenceValidationException countFailure = Assert.Throws<PersistenceValidationException>(() =>
        countRestricted.Prepare(countOnly, new SaveSetContext(7, 700)));
    Assert.True(countFailure.Message.Contains("account limit", StringComparison.Ordinal));

    byte[] noncanonicalOrder = BuildAccountingArtifact(
        sequence: 7,
        accounts:
        [
            new AccountRecord(new AccountId(2)),
            new AccountRecord(new AccountId(1)),
        ],
        identities: [],
        characters: []);
    Assert.Throws<PersistenceValidationException>(() =>
        participant.Prepare(noncanonicalOrder, new SaveSetContext(7, 700)));
    Assert.Throws<PersistenceValidationException>(() =>
        participant.Prepare(valid, new SaveSetContext(8, 800)));
}

static void DuplicateDurableCharacterLinkIsRejected()
{
    using Fixture fixture = CreateFixture();
    AccountRecord account = new(new AccountId(1));
    PersistentEntityKey key = fixture.Key;
    byte[] artifact = BuildAccountingArtifact(
        sequence: 7,
        accounts: [account],
        identities: [],
        characters:
        [
            new CharacterRecord(
                new CharacterId(1), account.AccountId, 0, "Alice",
                new PrefabKey("characters/alice"), key),
            new CharacterRecord(
                new CharacterId(2), account.AccountId, 1, "Bob",
                new PrefabKey("characters/bob"), key),
        ]);
    var participant = new AccountingSaveSetParticipant(fixture.Accounting);
    using IPreparedLoadParticipant prepared = participant.Prepare(
        artifact,
        new SaveSetContext(7, 700));
    Assert.Throws<PersistenceValidationException>(() =>
        prepared.Resolve(new StaticResolver(key, fixture.Entity)));

    using IPreparedLoadParticipant unresolved = participant.Prepare(
        Capture(participant, new SaveSetContext(7, 700)),
        new SaveSetContext(7, 700));
    Assert.Throws<PersistenceValidationException>(unresolved.Validate);
}

static void ClockArtifactUsesFrequencyIndependentTimeAndReadsExactLegacyState()
{
    var context = new SaveSetContext(71, 7_100);
    var state = new PersistentWorldClockState(
        worldTimeRate: 7,
        simulationTick: 123,
        worldTimeMicroseconds: 45_678,
        worldTimeFractionQuanta: 2_400_000);
    var participant = new WorldClockSaveSetParticipant(state);
    byte[] current = Capture(participant, context);
    Assert.Equal(38, current.Length);
    Assert.Equal((ushort)2, BinaryPrimitives.ReadUInt16LittleEndian(current.AsSpan(4)));
    using (var prepared = (PreparedWorldClockParticipant)new WorldClockSaveSetParticipant().Prepare(current, context))
        Assert.Equal(state, prepared.State);

    byte[] legacy = BuildLegacyClockArtifact(
        context.Sequence,
        simulationFrequencyHz: 60,
        worldTimeRate: 7,
        simulationTick: 123,
        worldTimeMicroseconds: 45_678,
        worldTimeRemainder: 37);
    using (var prepared = (PreparedWorldClockParticipant)new WorldClockSaveSetParticipant().Prepare(legacy, context))
    {
        Assert.Equal(2_220_000, prepared.State.WorldTimeFractionQuanta);
        Assert.Equal(123UL, prepared.State.SimulationTick);
        Assert.Equal(7, prepared.State.WorldTimeRate);
    }

    byte[] inexactLegacy = BuildLegacyClockArtifact(
        context.Sequence,
        simulationFrequencyHz: 7,
        worldTimeRate: 1,
        simulationTick: 1,
        worldTimeMicroseconds: 0,
        worldTimeRemainder: 1);
    Assert.Throws<PersistenceCompatibilityException>(() =>
        new WorldClockSaveSetParticipant().Prepare(inexactLegacy, context));
}

static void RealRuntimeSaveLoadPublishesEcsAndAccountingTogether()
{
    using Fixture fixture = CreateFixture();
    SaveAttemptResult save = fixture.Persistence.TrySave(
        new SaveSetContext(1, 1),
        static capture => capture());
    Assert.True(save.Succeeded);
    Assert.Equal(2, save.Manifest!.Artifacts.Length);

    EcsWorld oldWorld = fixture.CurrentWorld;
    fixture.OldWorld = oldWorld;
    Entity oldEntity = fixture.Entity;
    Assert.True(fixture.Runtime.Game.TryUseActiveGeneration(
        (world, _) =>
        {
            Assert.True(world.TryWrite(oldEntity, fixture.Composition.SourceLookup, new PersistedValue { Value = 999 }));
            return true;
        },
        out bool mutated));
    Assert.True(mutated);

    LoadAttemptResult load = fixture.Persistence.TryLoad();
    Assert.True(load.Succeeded);
    Assert.Equal(2UL, load.GenerationId);
    Assert.Equal(2UL, fixture.Runtime.ActiveGenerationId);
    Assert.True(fixture.Runtime.Game.TryUseActiveGeneration<AccountingStore, CharacterRecord>(
        (world, _, accounting) =>
        {
            Assert.True(accounting.TryGetCharacter(new CharacterId(1), out CharacterRecord restoredCharacter));
            Assert.True(world.TryResolvePersistentEntity(
                restoredCharacter.PersistentEntityKey.Entity,
                out Entity restoredEntity));
            ComponentLookup<PersistedValue> lookup = fixture.Composition.StagingLookup;
            Assert.True(world.TryRead(restoredEntity, lookup, out PersistedValue value));
            Assert.Equal(123, value.Value);
            Assert.False(restoredEntity == oldEntity);
            return restoredCharacter;
        },
        out CharacterRecord restoredCharacter));
    Assert.Equal("Alice", restoredCharacter.DisplayName);
    Assert.True(fixture.CurrentWorld.IsRunning);
    Assert.True(oldEntity.IsValid);
    Assert.True(oldWorld.IsDisposed);
    Assert.Throws<ObjectDisposedException>(() => _ = fixture.OldWorld!.IsAlive(oldEntity));
    Assert.Throws<ObjectDisposedException>(() => _ = fixture.Accounting.AccountCount);
}

static void MissingCharacterEntityLeavesActiveGenerationUntouched()
{
    using Fixture fixture = CreateFixture();
    fixture.Accounting.AddCharacter(new CharacterRecord(
        new CharacterId(2),
        new AccountId(1),
        1,
        "Missing",
        new PrefabKey("characters/missing"),
        new PersistentEntityKey(
            new PersistentWorldId(0x6006),
            new PersistentEntityId(999))));
    Assert.True(fixture.Persistence.TrySave(new SaveSetContext(1, 1), static capture => capture()).Succeeded);

    EcsWorld oldWorld = fixture.CurrentWorld;
    fixture.OldWorld = oldWorld;
    ulong oldGeneration = fixture.Runtime.ActiveGenerationId;
    LoadAttemptResult load = fixture.Persistence.TryLoad();

    Assert.False(load.Succeeded);
    Assert.True(load.Failure is PersistenceValidationException);
    Assert.Equal(oldGeneration, fixture.Runtime.ActiveGenerationId);
    Assert.True(ReferenceEquals(oldWorld, fixture.CurrentWorld));
    Assert.True(oldWorld.IsRunning);
    Assert.True(oldWorld.IsAlive(fixture.Entity));

    Assert.True(oldWorld.TryCreate(fixture.Composition.GetArchetype(oldWorld), out Entity nextEntity));
    Assert.Equal(2UL, oldWorld.GetOrAssignPersistentId(nextEntity).Value);
}

static void AccountingPrepareFailureLeavesActiveGenerationUntouched()
{
    using Fixture fixture = CreateFixture();
    fixture.Accounting.AddAccount(new AccountRecord(new AccountId(2)));
    Assert.True(fixture.Persistence.TrySave(new SaveSetContext(1, 1), static capture => capture()).Succeeded);

    var restrictive = new RuntimeAccountingPersistence(
        fixture.Runtime.Game,
        fixture.Composition.Catalog,
        fixture.RestoreOptions,
        fixture.CreateStagingWorld,
        fixture.Coordinator,
        new AccountingPersistenceOptions(maximumAccounts: 1));
    EcsWorld oldWorld = fixture.CurrentWorld;
    LoadAttemptResult load = restrictive.TryLoad();

    Assert.False(load.Succeeded);
    Assert.True(load.Failure is PersistenceValidationException);
    Assert.True(ReferenceEquals(oldWorld, fixture.CurrentWorld));
    Assert.Equal(1UL, fixture.Runtime.ActiveGenerationId);
    Assert.True(oldWorld.IsAlive(fixture.Entity));
}

static void EcsPrepareFailureLeavesActiveGenerationUntouched()
{
    using Fixture fixture = CreateFixture();
    Assert.True(fixture.Persistence.TrySave(new SaveSetContext(1, 1), static capture => capture()).Succeeded);
    var restrictive = new RuntimeAccountingPersistence(
        fixture.Runtime.Game,
        fixture.Composition.Catalog,
        new PersistenceRestoreOptions(
            new PersistenceLimits(1_000_000, 0, 10, 10, 100, 1_000_000, 1_000_000),
            ChunkLayoutOptions.KiB16),
        fixture.CreateStagingWorld,
        fixture.Coordinator);
    EcsWorld oldWorld = fixture.CurrentWorld;
    LoadAttemptResult load = restrictive.TryLoad();

    Assert.False(load.Succeeded);
    Assert.True(load.Failure is PersistenceValidationException);
    Assert.True(ReferenceEquals(oldWorld, fixture.CurrentWorld));
    Assert.Equal(1UL, fixture.Runtime.ActiveGenerationId);
    Assert.True(oldWorld.IsAlive(fixture.Entity));
}

static void AccountingDomainValidationFailureLeavesActiveGenerationUntouched()
{
    using Fixture fixture = CreateFixture();
    SaveAttemptResult save = fixture.Persistence.TrySave(
        new SaveSetContext(1, 1),
        static capture => capture());
    Assert.True(save.Succeeded);
    byte[] invalid = BuildAccountingArtifact(
        sequence: 1,
        accounts: [new AccountRecord(new AccountId(1))],
        identities: [],
        characters:
        [
            new CharacterRecord(
                new CharacterId(1), new AccountId(1), 0, "Alice",
                new PrefabKey("characters/alice"), fixture.Key),
            new CharacterRecord(
                new CharacterId(2), new AccountId(1), 1, "Bob",
                new PrefabKey("characters/bob"), fixture.Key),
        ]);
    ReplaceSavedAccountingArtifact(fixture.Root, save.Manifest!, invalid);

    EcsWorld activeWorld = fixture.CurrentWorld;
    ulong activeGeneration = fixture.Runtime.ActiveGenerationId;
    LoadAttemptResult load = fixture.Persistence.TryLoad();

    Assert.False(load.Succeeded);
    Assert.True(load.Failure is PersistenceValidationException);
    Assert.Equal(activeGeneration, fixture.Runtime.ActiveGenerationId);
    Assert.True(ReferenceEquals(activeWorld, fixture.CurrentWorld));
    Assert.True(activeWorld.IsRunning);
    Assert.Equal(1, fixture.Accounting.CharacterCount);
}

static void ParticipantSequenceMismatchLeavesActiveGenerationUntouched()
{
    using Fixture fixture = CreateFixture();
    SaveAttemptResult save = fixture.Persistence.TrySave(
        new SaveSetContext(1, 1),
        static capture => capture());
    Assert.True(save.Succeeded);
    byte[] wrongSequence = BuildAccountingArtifact(
        sequence: 2,
        accounts: [new AccountRecord(new AccountId(1))],
        identities: [],
        characters: []);
    ReplaceSavedAccountingArtifact(fixture.Root, save.Manifest!, wrongSequence);

    EcsWorld activeWorld = fixture.CurrentWorld;
    LoadAttemptResult load = fixture.Persistence.TryLoad();

    Assert.False(load.Succeeded);
    Assert.True(load.Failure is PersistenceValidationException);
    Assert.Equal(1UL, fixture.Runtime.ActiveGenerationId);
    Assert.True(ReferenceEquals(activeWorld, fixture.CurrentWorld));
    Assert.True(activeWorld.IsRunning);
}

static void SaveRestoreRecaptureIsCanonical()
{
    using Fixture fixture = CreateFixture();
    var context = new SaveSetContext(9, 900);
    byte[] originalAccounting = Capture(
        new AccountingSaveSetParticipant(fixture.Accounting),
        context);
    byte[] originalEcs = Capture(
        new EcsWorldSaveSetParticipant(
            fixture.CurrentWorld,
            fixture.Composition.Catalog,
            fixture.RestoreOptions,
            fixture.CreateStagingWorld),
        context);

    Assert.True(fixture.Persistence.TrySave(
        new SaveSetContext(1, 1),
        static capture => capture()).Succeeded);
    Assert.True(fixture.Persistence.TryLoad().Succeeded);

    Assert.True(fixture.Runtime.Game.TryUseActiveGeneration<AccountingStore, CanonicalArtifacts>(
        (world, _, accounting) => new CanonicalArtifacts(
            Capture(new AccountingSaveSetParticipant(accounting), context),
            Capture(
                new EcsWorldSaveSetParticipant(
                    world,
                    fixture.Composition.Catalog,
                    fixture.RestoreOptions,
                    fixture.CreateStagingWorld),
                context)),
        out CanonicalArtifacts restored));
    Assert.SequenceEqual(originalAccounting, restored.Accounting);
    Assert.SequenceEqual(originalEcs, restored.Ecs);
}

static void CandidateConstructionFailureLeavesActiveGenerationUntouched()
{
    using Fixture fixture = CreateFixture();
    EcsWorld activeWorld = fixture.CurrentWorld;
    ulong activeGeneration = fixture.Runtime.ActiveGenerationId;
    Assert.Throws<InvalidOperationException>(() =>
        fixture.Runtime.Game.TryPublishPreparedGeneration(
            activeWorld,
            fixture.Accounting,
            out _));
    Assert.True(activeWorld.IsRunning);
    Assert.Equal(1, fixture.Accounting.AccountCount);

    var rejectedState = new AccountingStore();
    var invalidWorld = new EcsWorld(
        fixture.Composition.Schema,
        WorldOptions());

    Assert.Throws<InvalidOperationException>(() =>
        fixture.Runtime.Game.TryPublishPreparedGeneration(
            invalidWorld,
            rejectedState,
            out _));

    Assert.Equal(activeGeneration, fixture.Runtime.ActiveGenerationId);
    Assert.True(ReferenceEquals(activeWorld, fixture.CurrentWorld));
    Assert.True(activeWorld.IsRunning);
    Assert.True(invalidWorld.IsDisposed);
    Assert.Throws<ObjectDisposedException>(() => _ = rejectedState.AccountCount);
    Assert.Equal(ServerRuntimeState.Running, fixture.Runtime.State);
}

static byte[] Capture(ISaveSetParticipant participant, SaveSetContext context)
{
    using var stream = new MemoryStream();
    using (ICapturedSaveSetParticipant capture = participant.Capture(context))
        capture.WriteArtifact(stream);
    return stream.ToArray();
}

static byte[] BuildAccountingArtifact(
    ulong sequence,
    IReadOnlyList<AccountRecord> accounts,
    IReadOnlyList<TrustedIdentityBinding> identities,
    IReadOnlyList<CharacterRecord> characters)
{
    using var stream = new MemoryStream();
    using var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true);
    writer.Write(0x43415355U);
    writer.Write((ushort)1);
    writer.Write(sequence);
    writer.Write(accounts.Count);
    writer.Write(characters.Count);
    writer.Write(identities.Count);
    foreach (AccountRecord account in accounts)
    {
        writer.Write(account.AccountId.Value);
        writer.Write((byte)account.Status);
    }
    foreach (TrustedIdentityBinding identity in identities)
    {
        WriteString(writer, identity.Identity.Value);
        writer.Write(identity.AccountId.Value);
    }
    foreach (CharacterRecord character in characters)
    {
        writer.Write(character.CharacterId.Value);
        writer.Write(character.AccountId.Value);
        writer.Write(character.Slot);
        WriteString(writer, character.DisplayName);
        WriteString(writer, character.PrefabKey.Value);
        writer.Write(character.PersistentEntityKey.World.Value);
        writer.Write(character.PersistentEntityKey.Entity.Value);
    }
    writer.Flush();
    return stream.ToArray();
}

static byte[] BuildLegacyClockArtifact(
    ulong sequence,
    int simulationFrequencyHz,
    int worldTimeRate,
    ulong simulationTick,
    long worldTimeMicroseconds,
    int worldTimeRemainder)
{
    byte[] bytes = new byte[42];
    BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0x4B434C55);
    BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(4), 1);
    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(6), sequence);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), simulationFrequencyHz);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), worldTimeRate);
    BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(22), simulationTick);
    BinaryPrimitives.WriteInt64LittleEndian(bytes.AsSpan(30), worldTimeMicroseconds);
    BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(38), worldTimeRemainder);
    return bytes;
}

static void WriteString(BinaryWriter writer, string value)
{
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
    writer.Write(bytes.Length);
    writer.Write(bytes);
}

static void ReplaceSavedAccountingArtifact(
    string root,
    SaveSetManifest manifest,
    byte[] replacement)
{
    string generation = Path.Combine(root, "generations", manifest.Sequence.ToString("D20"));
    string artifactPath = Path.Combine(generation, "accounting.bin");
    File.WriteAllBytes(artifactPath, replacement);
    SaveSetArtifact[] artifacts = manifest.Artifacts
        .Select(artifact => artifact.Key == "accounting"
            ? artifact with
            {
                ByteCount = replacement.Length,
                Sha256 = Convert.ToHexString(SHA256.HashData(replacement)),
            }
            : artifact)
        .ToArray();
    File.WriteAllText(
        Path.Combine(generation, "manifest.json"),
        JsonSerializer.Serialize(manifest with { Artifacts = artifacts }));
}

static Fixture CreateFixture()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-s0-6-" + Guid.NewGuid().ToString("N"));
    var accounting = new AccountingStore();
    accounting.AddAccount(new AccountRecord(new AccountId(1)));
    accounting.BindTrustedIdentity(new TrustedLocalIdentity("local-alice"), new AccountId(1));
    var composition = new PersistenceComposition(accounting);
    var runtime = new ServerRuntime(composition, new ServerRuntimeOptions(WorldOptions(), WorldClockOptions.Default));
    runtime.Initialize();
    runtime.Start();

    Assert.True(runtime.Game.TryUseActiveGeneration(
        (world, _) =>
        {
            ArchetypeHandle archetype = composition.GetArchetype(world);
            ComponentLookup<PersistedValue> lookup = composition.SourceLookup;
            Assert.True(world.TryCreate(archetype, out Entity entity));
            Assert.True(world.TryWrite(entity, lookup, new PersistedValue { Value = 123 }));
            var key = new PersistentEntityKey(
                world.PersistentWorldId,
                world.GetOrAssignPersistentId(entity));
            return new EntitySetup(entity, key);
        },
        out EntitySetup setup));
    accounting.AddCharacter(new CharacterRecord(
        new CharacterId(1),
        new AccountId(1),
        0,
        "Alice",
        new PrefabKey("characters/alice"),
        setup.Key));

    var coordinator = new SaveSetCoordinator(root);
    var persistence = new RuntimeAccountingPersistence(
        runtime.Game,
        composition.Catalog,
        RestoreOptions(),
        requirements => composition.CreateStagingWorld(requirements),
        coordinator);
    return new Fixture(root, runtime, composition, accounting, persistence, coordinator, setup.Entity, setup.Key, RestoreOptions());
}

static EcsWorldOptions WorldOptions() => new(
    new EntityCapacity(32),
    maximumArchetypes: 4,
    maximumPreparedPlans: 4,
    scheduler: new EcsSchedulerOptions(
        maximumSystems: 1,
        maximumDependencies: 1,
        maximumWorkItems: 2,
        maximumStructuralSegments: 2,
        backgroundWorkerCount: 0),
    persistence: new PersistenceIdentityOptions(new PersistentWorldId(0x6006)));

static PersistenceRestoreOptions RestoreOptions() => new(
    new PersistenceLimits(1_000_000, 32, 10, 10, 100, 1_000_000, 1_000_000),
    ChunkLayoutOptions.KiB16);

readonly record struct EntitySetup(Entity Entity, PersistentEntityKey Key);

readonly record struct CanonicalArtifacts(byte[] Accounting, byte[] Ecs);

sealed class Fixture : IDisposable
{
    internal Fixture(
        string root,
        ServerRuntime runtime,
        PersistenceComposition composition,
        AccountingStore accounting,
        RuntimeAccountingPersistence persistence,
        SaveSetCoordinator coordinator,
        Entity entity,
        PersistentEntityKey key,
        PersistenceRestoreOptions restoreOptions)
    {
        Root = root;
        Runtime = runtime;
        Composition = composition;
        Accounting = accounting;
        Persistence = persistence;
        Coordinator = coordinator;
        Entity = entity;
        Key = key;
        RestoreOptions = restoreOptions;
    }

    internal string Root { get; }
    internal ServerRuntime Runtime { get; }
    internal PersistenceComposition Composition { get; }
    internal AccountingStore Accounting { get; }
    internal RuntimeAccountingPersistence Persistence { get; }
    internal SaveSetCoordinator Coordinator { get; }
    internal Entity Entity { get; }
    internal PersistentEntityKey Key { get; }
    internal EcsWorld CurrentWorld => Composition.LastWorld is { IsDisposed: false }
        ? Composition.LastWorld
        : RuntimeWorld();
    internal EcsWorld? OldWorld { get; set; }
    internal PersistenceRestoreOptions RestoreOptions { get; }

    internal EcsWorld CreateStagingWorld(PersistenceRestoreRequirements requirements) =>
        Composition.CreateStagingWorld(requirements);

    private EcsWorld RuntimeWorld()
    {
        Assert.True(Runtime.Game.TryUseActiveGeneration(static (world, _) => world, out EcsWorld world));
        return world;
    }

    public void Dispose()
    {
        Runtime.Dispose();
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }
}

sealed class PersistenceComposition : IServerComposition
{
    private readonly Dictionary<WorldId, ArchetypeHandle> _archetypes = [];
    private AccountingStore? _initialAccounting;

    internal PersistenceComposition(AccountingStore initialAccounting)
    {
        _initialAccounting = initialAccounting ?? throw new ArgumentNullException(nameof(initialAccounting));
        Schema = ComponentSchema.Create(
            new SchemaId(0x60, 0x06),
            new[]
            {
                ComponentRegistration.Create<PersistedValue>(
                    new ComponentId(0),
                    "persistence.value",
                    StableHash64.Compute("persistence.value"),
                    version: 1,
                    ComponentFlags.Persistent,
                    persistenceId: 600600,
                    ComponentPersistenceMode.Value),
            });
        Catalog = new PersistenceCatalogBuilder(Schema)
            .Register<PersistedValue>(
                new PersistenceSchemaFingerprint(0x6006, 0x0600),
                new PersistedValueCodec())
            .Build();
    }

    public ComponentSchema Schema { get; }

    internal PersistenceCatalog Catalog { get; }

    internal EcsWorld? LastWorld { get; private set; }

    internal ComponentLookup<PersistedValue> SourceLookup { get; private set; }

    internal ComponentLookup<PersistedValue> StagingLookup { get; private set; }

    public void Configure(ServerGenerationBuilder generation)
    {
        AccountingStore accounting = _initialAccounting ??
            throw new InvalidOperationException("Initial Accounting was already assigned to a generation.");
        _initialAccounting = null;
        generation.SetGameState(accounting);
        LastWorld = generation.World;
        _archetypes.Add(
            generation.World.Id,
            generation.World.RegisterArchetype(
                new[] { new ComponentId(0) },
                ChunkLayoutOptions.KiB16,
                maximumChunks: 1));
        SourceLookup = generation.World.PrepareLookup<PersistedValue>();
    }

    internal ArchetypeHandle GetArchetype(EcsWorld world) => _archetypes[world.Id];

    internal EcsWorld CreateStagingWorld(PersistenceRestoreRequirements requirements)
    {
        var world = new EcsWorld(requirements.Schema, new EcsWorldOptions(
            new EntityCapacity(Math.Max(32, requirements.EntityCount)),
            maximumArchetypes: 4,
            maximumPreparedPlans: 4,
            scheduler: new EcsSchedulerOptions(
                maximumSystems: 1,
                maximumDependencies: 1,
                maximumWorkItems: 2,
                maximumStructuralSegments: 2,
                backgroundWorkerCount: 0),
            persistence: requirements.IdentityOptions));
        _ = world.RegisterArchetype(
            new[] { new ComponentId(0) },
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        StagingLookup = world.PrepareLookup<PersistedValue>();
        return world;
    }
}

sealed class StaticResolver : IPersistentEntityResolver
{
    private readonly PersistentEntityKey _key;
    private readonly Entity _entity;

    internal StaticResolver(PersistentEntityKey key, Entity entity)
    {
        _key = key;
        _entity = entity;
    }

    public bool TryResolve(PersistentEntityKey key, out object? value)
    {
        if (key.Equals(_key))
        {
            value = _entity;
            return true;
        }
        value = null;
        return false;
    }
}

struct PersistedValue : IComponent<PersistedValue>
{
    public int Value;
}

sealed class PersistedValueCodec : IPersistenceComponentCodec<PersistedValue>, IPersistenceBatchComponentCodec<PersistedValue>
{
    public int EncodedSize => sizeof(int);

    public void Encode(in PersistedValue value, Span<byte> destination, PersistenceWriteContext context) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.Value);

    public void Decode(ReadOnlySpan<byte> source, ref PersistedValue value, PersistenceReadContext context) =>
        value.Value = BinaryPrimitives.ReadInt32LittleEndian(source);

    public void EncodeBatch(ReadOnlySpan<PersistedValue> values, Span<byte> destination, PersistenceWriteContext context)
    {
        for (int index = 0; index < values.Length; index++)
            BinaryPrimitives.WriteInt32LittleEndian(destination[(index * sizeof(int))..], values[index].Value);
    }

    public void DecodeBatch(ReadOnlySpan<byte> source, Span<PersistedValue> values, PersistenceReadContext context)
    {
        for (int index = 0; index < values.Length; index++)
            values[index].Value = BinaryPrimitives.ReadInt32LittleEndian(source[(index * sizeof(int))..]);
    }
}

static class Assert
{
    public static TException Throws<TException>(Action action) where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException exception)
        {
            return exception;
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        if (!expected.SequenceEqual(actual))
            throw new InvalidOperationException("Expected sequences to be equal.");
    }

    public static void True(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected true.");
    }

    public static void False(bool value)
    {
        if (value)
            throw new InvalidOperationException("Expected false.");
    }
}
