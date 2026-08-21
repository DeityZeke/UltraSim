using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using UltraSim.Client.Core;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Inventory;
using UltraSim.Game.Persistence;
using UltraSim.Game.UI;
using UltraSim.Server.Hosting;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.UI;

const string Workload = "ultrasim-s0.8-integrated-acceptance-v3";
string? output = ReadOption(args, "--output");

var results = new List<TestResult>();
Run("AccountBackedInternalToHeadlessCanonicalRoundTrip", AccountBackedRoundTrip, results);
Run("AccountlessAdmissionAndPersistence", AccountlessRoundTrip, results);
Run("AuthoritativeClockRoundTripsAcrossHostCadences", AuthoritativeClockRoundTripsAcrossHostCadences, results);
Run("WarmedRuntimeTickHasNoAllocationAndBoundedWrapperTax", RuntimeTickBenchmark, results);

var report = new AcceptanceResult(
    Workload,
    DateTimeOffset.UtcNow,
    Environment.Version.ToString(),
    Environment.ProcessorCount,
    results.ToArray());
string json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
Console.WriteLine(json);
if (!string.IsNullOrWhiteSpace(output))
{
    string path = Path.GetFullPath(output);
    Directory.CreateDirectory(Path.GetDirectoryName(path)!);
    File.WriteAllText(path, json + Environment.NewLine);
}
Console.WriteLine($"All {results.Count} S0.8 integrated acceptance gates passed.");

static TestMetrics AccountBackedRoundTrip()
{
    string root = TempRoot("account-backed");
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_0001));
    var composition = new UltraSimGameComposition(new InventoryLimits(32, 128, 32));
    var renderer = new RecordingRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    using var ui = new RecordingUiPort(link);
    try
    {
        using var host = new InternalHost(composition, RuntimeOptions(identity));
        host.Start();
        host.SetPlaybackRate(2);
        ServerRuntime runtime = host.RuntimeForTesting;
        FixtureKeys keys = PopulateAccountBacked(runtime.Game);

        Expect(client.CachedDefinitionCount == 0, "The CharacterSelect definition cache was not initially cold.");
        using var admission = new AdmissionService(runtime.Game, composition, ui);
        ParticipantId aliceParticipant = new(1);
        AdmissionResult waiting = admission.JoinAccountBacked(
            aliceParticipant,
            new TrustedLocalIdentity("alice-local"));
        Expect(waiting.IsWaitingForCharacter, "Account-backed admission did not enter CharacterSelect.");
        Expect(ui.ColdOpenCount == 1 && ui.HotOpenCount == 0,
            "The first CharacterSelect open did not use the cold path.");
        ReadOnlySpan<UiListRow> characterRows = client.GetRows(
            waiting.CharacterSelectView,
            CharacterSelectUi.Characters);
        Expect(characterRows.Length == 1 && characterRows[0].Values[1].Text == "Alice",
            "CharacterSelect did not project Alice.");
        client.SubmitAction(waiting.CharacterSelectView, CharacterSelectUi.Play, characterRows[0].Id);
        Expect(admission.TryGetSession(aliceParticipant, out AdmissionResult entered) && entered.IsEntered,
            "The Play action did not enter Alice.");
        Expect(Resolve(runtime.Game, keys.Alice) == entered.Entity,
            "Admission did not resolve Alice's durable entity key.");

        ParticipantId retainedSelector = new(2);
        AdmissionResult retainedWaiting = admission.JoinAccountBacked(
            retainedSelector,
            new TrustedLocalIdentity("alice-local"));
        Expect(retainedWaiting.IsWaitingForCharacter,
            "The retained selector session was not established for the hot-reopen gate.");

        using var inventory = new InventoryUiController(runtime.Game, ui);
        Entity backpack = Resolve(runtime.Game, keys.Backpack);
        InventoryUiOpenResult opened = inventory.Open(backpack);
        Expect(opened.Succeeded, "The initial inventory open failed.");
        ExpectRowNames(client.GetRows(opened.View, InventoryUi.Items), "Bread", "Iron Sword");

        Entity splitEntity = Entity.Null;
        inventory.MutationCompleted += result =>
        {
            if (result.Accepted && result.CreatedItem.IsValid)
                splitEntity = result.CreatedItem;
        };
        UiRowId breadRow = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Bread");
        client.SubmitAction(opened.View, InventoryUi.Select, breadRow);
        client.SubmitAction(opened.View, InventoryUi.Split);
        Expect(splitEntity.IsValid, "The inventory split did not publish its created item.");
        Expect(ui.LastListKinds.SequenceEqual([UiListDeltaKind.Update, UiListDeltaKind.Add]),
            "Split did not publish exactly Update + Add row deltas.");
        Expect(!ui.LastListKinds.Contains(UiListDeltaKind.Replace),
            "Split regressed to a full-list replacement.");
        keys = keys with { SplitBread = GetPersistentId(runtime.Game, splitEntity) };

        UiRowId swordRow = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Iron Sword");
        client.SubmitAction(opened.View, InventoryUi.Select, swordRow);
        client.SubmitAction(opened.View, InventoryUi.Equip);
        Expect(ui.LastListKinds.SequenceEqual([UiListDeltaKind.Remove]),
            "Equip did not publish exactly one Remove row delta.");
        Expect(!ui.LastListKinds.Contains(UiListDeltaKind.Replace),
            "Equip regressed to a full-list replacement.");
        ExpectRowNames(client.GetRows(opened.View, InventoryUi.Items), "Bread", "Bread");

        const int savedTicks = 12;
        for (int tick = 0; tick < savedTicks; tick++) host.Tick();
        Expect(ReadSimulation(runtime.Game, keys.Alice).TickMarker == savedTicks,
            "The real scheduled simulation system did not update Alice once per tick.");
        ulong savedSimulationTick = runtime.SimulationTick;
        long savedWorldTime = runtime.WorldTimeMicroseconds;
        string savedCanonical = CaptureCanonical(runtime, keys);

        var persistence = new GameRuntimePersistence(
            runtime.Game,
            composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        long pauseTicks = 0;
        long saveAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long saveStarted = Stopwatch.GetTimestamp();
        long captureCallStarted = Stopwatch.GetTimestamp();
        Task<GameSaveResult> saveTask = persistence.TrySaveAsync(
            new SaveSetContext(1, 1_700_000_800_001),
            capture =>
            {
                long started = Stopwatch.GetTimestamp();
                capture();
                pauseTicks += Stopwatch.GetTimestamp() - started;
            });
        long captureCallTicks = Stopwatch.GetTimestamp() - captureCallStarted;
        // The immutable capture is now detached: authoritative execution is
        // legal while encoding, hashing and durable publication run elsewhere.
        host.Tick();
        GameSaveResult saved = saveTask.GetAwaiter().GetResult();
        host.SetPlaybackRate(4);
        long saveTicks = Stopwatch.GetTimestamp() - saveStarted;
        long saveAllocated = GC.GetAllocatedBytesForCurrentThread() - saveAllocatedBefore;
        Expect(saved.Succeeded && saved.Manifest is not null,
            "The account-backed save generation was not accepted.");
        Expect(saved.Manifest!.Artifacts.Length == 4 &&
            saved.Manifest.Artifacts.Any(static artifact => artifact.Key == "world") &&
            saved.Manifest.Artifacts.Any(static artifact => artifact.Key == "worldview") &&
            saved.Manifest.Artifacts.Any(static artifact => artifact.Key == "accounting") &&
            saved.Manifest.Artifacts.Any(static artifact => artifact.Key == "clock"),
            "The game save did not contain ECS, current-world, Accounting, and authoritative clock participants.");
        Expect(captureCallTicks >= pauseTicks,
            "The capture-pause measurement exceeded the complete synchronous capture call.");

        Entity oldAlice = Resolve(runtime.Game, keys.Alice);
        Entity oldBackpack = Resolve(runtime.Game, keys.Backpack);
        DivergeAfterSave(runtime.Game, keys);
        for (int tick = 0; tick < 7; tick++) host.Tick();
        string divergentCanonical = CaptureCanonical(runtime, keys, allowMissingItems: true);
        Expect(divergentCanonical != savedCanonical,
            "The live generation did not diverge visibly after save.");

        long loadAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long loadStarted = Stopwatch.GetTimestamp();
        GameLoadResult loaded = persistence.TryLoad();
        long loadTicks = Stopwatch.GetTimestamp() - loadStarted;
        long loadAllocated = GC.GetAllocatedBytesForCurrentThread() - loadAllocatedBefore;
        Expect(loaded.Succeeded && loaded.GenerationId > 1,
            "The staged game generation was not published.");
        Expect(runtime.SimulationTick == savedSimulationTick &&
            runtime.WorldTimeMicroseconds == savedWorldTime,
            "Restore did not recover the authoritative saved clock state.");
        Expect(runtime.PlaybackRate == 4,
            "Restore incorrectly replaced current host playback policy with save-time policy.");
        Entity restoredAlice = Resolve(runtime.Game, keys.Alice);
        Entity restoredBackpack = Resolve(runtime.Game, keys.Backpack);
        Expect(restoredAlice != oldAlice && restoredAlice.World != oldAlice.World,
            "The restored Alice reused the old runtime entity identity.");
        Expect(restoredBackpack != oldBackpack && restoredBackpack.World != oldBackpack.World,
            "The restored Backpack reused the old runtime entity identity.");
        Expect(IsNotAlive(runtime.Game, oldAlice) && IsNotAlive(runtime.Game, oldBackpack),
            "Old-generation entity handles resolved in the replacement world.");
        string restoredCanonical = CaptureCanonical(runtime, keys);
        Expect(restoredCanonical == savedCanonical,
            "The restored logical game state differs from the accepted save state.");
        Expect(admission.TryGetSession(aliceParticipant, out AdmissionResult rebound) &&
            rebound.IsEntered && rebound.Entity == restoredAlice,
            "The entered admission session did not rebind to the replacement generation.");

        int hotBefore = ui.HotOpenCount;
        AdmissionResult hotSelector = admission.ReopenCharacterSelect(retainedSelector);
        Expect(hotSelector.IsWaitingForCharacter && ui.HotOpenCount == hotBefore + 1,
            "CharacterSelect did not reopen through the exact cached hot path.");
        ReadOnlySpan<UiListRow> hotCharacters = client.GetRows(
            hotSelector.CharacterSelectView,
            CharacterSelectUi.Characters);
        Expect(hotCharacters.Length == 1 && hotCharacters[0].Values[1].Text == "Alice",
            "The hot CharacterSelect model did not project restored Alice.");

        Expect(inventory.Close(opened.View), "The pre-restore inventory view did not close.");
        hotBefore = ui.HotOpenCount;
        InventoryUiOpenResult hotInventory = inventory.OpenHot(restoredBackpack);
        Expect(hotInventory.Succeeded && ui.HotOpenCount == hotBefore + 1,
            "Inventory did not reopen through its cached hot path.");
        ExpectRowNames(client.GetRows(hotInventory.View, InventoryUi.Items), "Bread", "Bread");

        host.Stop();
        Expect(host.State == ServerRuntimeState.Stopped, "InternalHost did not stop gracefully.");

        var headlessComposition = new UltraSimGameComposition(new InventoryLimits(32, 128, 32));
        using var headless = new HeadlessHost(
            headlessComposition,
            RuntimeOptions(identity),
            new HostCapabilities(new NullHostLogSink()));
        headless.Start();
        var headlessPersistence = new GameRuntimePersistence(
            headless.RuntimeForTesting.Game,
            headlessComposition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        GameLoadResult headlessLoaded = headlessPersistence.TryLoad();
        Expect(headlessLoaded.Succeeded, "HeadlessHost could not load the accepted save generation.");
        Expect(headless.SimulationTick == savedSimulationTick &&
            headless.WorldTimeMicroseconds == savedWorldTime,
            "A fresh HeadlessHost did not restore the saved authoritative clock.");
        Expect(headless.PlaybackRate == 1,
            "A fresh HeadlessHost inherited save-time playback policy.");
        string headlessCanonical = CaptureCanonical(headless.RuntimeForTesting, keys);
        Expect(headlessCanonical == restoredCanonical,
            "InternalHost and HeadlessHost produced different canonical logical state.");
        headless.Shutdown();
        Expect(headless.State == ServerRuntimeState.Disposed,
            "HeadlessHost did not complete explicit shutdown.");

        long artifactBytes = saved.Manifest.Artifacts.Sum(static artifact => artifact.ByteCount);
        return new TestMetrics(
            CanonicalHash: restoredCanonical,
            SaveMeanNanoseconds: ToNanoseconds(saveTicks),
            LoadMeanNanoseconds: ToNanoseconds(loadTicks),
            CapturePauseNanoseconds: ToNanoseconds(captureCallTicks),
            SaveAllocatedBytes: saveAllocated,
            LoadAllocatedBytes: loadAllocated,
            ArtifactBytes: artifactBytes,
            DirectTickMeanNanoseconds: 0,
            HostedTickMeanNanoseconds: 0,
            HostedTickP95Nanoseconds: 0,
            TickWrapperRatio: 0,
            TimedTickAllocatedBytes: 0,
            Notes: "ECS+Accounting+Clock; owner-thread immutable capture then durable worker publication; cold CharacterSelect/Inventory; row deltas; staged restore; session rebind; Internal/Headless generation parity.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static TestMetrics AccountlessRoundTrip()
{
    string root = TempRoot("accountless");
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_0002));
    var composition = new UltraSimGameComposition(new InventoryLimits(16, 32, 8));
    try
    {
        using var host = new HeadlessHost(
            composition,
            RuntimeOptions(identity),
            new HostCapabilities(new NullHostLogSink()));
        host.Start();
        ServerRuntime runtime = host.RuntimeForTesting;
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(new ParticipantId(11));
        Expect(entered.IsEntered && entered.Entity.IsValid,
            "Accountless admission did not produce a playable ECS entity.");
        PersistentEntityId id = GetPersistentId(runtime.Game, entered.Entity);
        Expect(GetAccountingCounts(runtime.Game) == (0, 0),
            "Accountless admission created Accounting records.");
        for (int tick = 0; tick < 4; tick++) host.Tick();
        Expect(ReadSimulation(runtime.Game, id).TickMarker == 4,
            "The accountless playable did not participate in real scheduled simulation.");

        var persistence = new GameRuntimePersistence(
            runtime.Game,
            composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        GameSaveResult save = persistence.TrySave(
            new SaveSetContext(1, 1_700_000_800_002),
            static capture => capture());
        Expect(save.Succeeded, "The declared accountless composition save set failed.");
        Entity old = entered.Entity;
        GameLoadResult load = persistence.TryLoad();
        Expect(load.Succeeded, "The accountless composition could not restore its save set.");
        Entity restored = Resolve(runtime.Game, id);
        Expect(restored != old && restored.World != old.World && IsNotAlive(runtime.Game, old),
            "Accountless restore did not retire the old runtime entity identity.");
        Expect(ReadSimulation(runtime.Game, id).TickMarker == 4,
            "Accountless restore did not recover scheduled component state.");
        Expect(GetAccountingCounts(runtime.Game) == (0, 0),
            "Accountless restore invented Accounting state.");
        Expect(admission.TryGetSession(entered.Participant, out AdmissionResult invalidated) &&
            invalidated.Rejection == AdmissionRejection.GenerationReplaced,
            "An accountless admission session retained a stale generation-local entity after restore.");
        host.Shutdown();

        return new TestMetrics(
            HashText($"{identity.WorldId.Value}:{id.Value}:4:0:0"),
            0, 0, 0, 0, 0,
            save.Manifest?.Artifacts.Sum(static artifact => artifact.ByteCount) ?? 0,
            0, 0, 0, 0, 0,
            "Accountless admission used no account records; the composition's declared empty Accounting and authoritative Clock participants remained round-trippable.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static TestMetrics AuthoritativeClockRoundTripsAcrossHostCadences()
{
    string root = TempRoot("cross-cadence-clock");
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_0004));
    try
    {
        var sourceComposition = new UltraSimGameComposition(new InventoryLimits(8, 16, 4));
        using var source = new InternalHost(
            sourceComposition,
            RuntimeOptions(identity, frequencyHz: 60, worldTimeRate: 7));
        source.Start();
        Expect(source.RuntimeForTesting.Game.TryUseActiveGeneration<IGameGenerationStateProvider, PersistentEntityId>(
            (world, _, game) =>
            {
                if (!world.TryInstantiate(game.Content.Character, out Entity character))
                    throw new InvalidOperationException("Cross-cadence character creation failed.");
                return world.GetOrAssignPersistentId(character);
            },
            out PersistentEntityId characterId),
            "Cross-cadence character bootstrap did not run.");
        source.Tick();
        PersistentWorldClockState at60 = CapturePersistentClock(source.RuntimeForTesting);
        Expect(at60.SimulationTick == 1 &&
            at60.WorldTimeMicroseconds == 116_666 &&
            at60.WorldTimeFractionQuanta == 2_400_000,
            "The 60 Hz source did not produce the expected exact fractional world time.");
        var sourcePersistence = new GameRuntimePersistence(
            source.RuntimeForTesting.Game,
            sourceComposition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        GameSaveResult firstSave = sourcePersistence.TrySave(
            new SaveSetContext(1, 1_700_000_800_004),
            static capture => capture());
        Expect(firstSave.Succeeded, "The 60 Hz source save failed.");

        var at20Composition = new UltraSimGameComposition(new InventoryLimits(8, 16, 4));
        using var at20Host = new HeadlessHost(
            at20Composition,
            RuntimeOptions(identity, frequencyHz: 20, worldTimeRate: 7),
            new HostCapabilities(new NullHostLogSink()));
        at20Host.Start();
        var at20Persistence = new GameRuntimePersistence(
            at20Host.RuntimeForTesting.Game,
            at20Composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        Expect(at20Persistence.TryLoad().Succeeded, "The 20 Hz host could not load the 60 Hz save.");
        Expect(at20Host.SimulationFrequencyHz == 20,
            "Restore replaced the 20 Hz host cadence with the save producer cadence.");
        Expect(CapturePersistentClock(at20Host.RuntimeForTesting) == at60,
            "60 -> 20 Hz restore changed authoritative clock state.");
        Expect(ReadSimulation(at20Host.RuntimeForTesting.Game, characterId).TickMarker == 1,
            "60 -> 20 Hz restore changed durable ECS/game state.");

        at20Host.Tick();
        PersistentWorldClockState after20Step = CapturePersistentClock(at20Host.RuntimeForTesting);
        Expect(after20Step.SimulationTick == 2 &&
            after20Step.WorldTimeMicroseconds == 466_666 &&
            after20Step.WorldTimeFractionQuanta == 2_400_000,
            "The restored clock did not advance exactly at 20 Hz.");
        Expect(ReadSimulation(at20Host.RuntimeForTesting.Game, characterId).TickMarker == 2,
            "The restored ECS/game state did not execute the next 20 Hz step.");
        GameSaveResult secondSave = at20Persistence.TrySave(
            new SaveSetContext(2, 1_700_000_800_005),
            static capture => capture());
        Expect(secondSave.Succeeded, "The 20 Hz source save failed.");

        var at60Composition = new UltraSimGameComposition(new InventoryLimits(8, 16, 4));
        using var at60Host = new HeadlessHost(
            at60Composition,
            RuntimeOptions(identity, frequencyHz: 60, worldTimeRate: 7),
            new HostCapabilities(new NullHostLogSink()));
        at60Host.Start();
        var at60Persistence = new GameRuntimePersistence(
            at60Host.RuntimeForTesting.Game,
            at60Composition,
            RestoreOptions(),
            new SaveSetCoordinator(root));
        Expect(at60Persistence.TryLoad().Succeeded, "The 60 Hz host could not load the 20 Hz save.");
        Expect(at60Host.SimulationFrequencyHz == 60,
            "Restore replaced the 60 Hz host cadence with the save producer cadence.");
        Expect(CapturePersistentClock(at60Host.RuntimeForTesting) == after20Step,
            "20 -> 60 Hz restore changed authoritative clock state.");
        Expect(ReadSimulation(at60Host.RuntimeForTesting.Game, characterId).TickMarker == 2,
            "20 -> 60 Hz restore changed durable ECS/game state.");

        return new TestMetrics(
            HashText($"{after20Step.SimulationTick}:{after20Step.WorldTimeMicroseconds}:" +
                $"{after20Step.WorldTimeFractionQuanta}"),
            0, 0, 0, 0, 0,
            secondSave.Manifest?.Artifacts.Sum(static artifact => artifact.ByteCount) ?? 0,
            0, 0, 0, 0, 0,
            "Full ECS+Accounting+Clock save/load at 60->20 and 20->60 Hz; host cadence remained local policy and fixed-point clock state remained exact.");
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static TestMetrics RuntimeTickBenchmark()
{
    const int population = 4_096;
    const int warmupTicks = 256;
    const int ticksPerSample = 256;
    const int samples = 21;
    var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x80_0003));

    var directComposition = new UltraSimGameComposition(
        new InventoryLimits(8, population + 8, 8),
        maximumChunksPerContentArchetype: 16);
    GameStaging direct = directComposition.CreateFresh(population + 16, identity);
    try
    {
        direct.World.Seal();
        direct.World.Initialize();
        direct.World.BeginRun();
        for (int index = 0; index < population; index++)
            Expect(direct.World.TryInstantiate(direct.Content.Character, out _), "Direct benchmark population failed.");
        direct.WorldState.Initialize(direct.World);

        var hostedComposition = new UltraSimGameComposition(
            new InventoryLimits(8, population + 8, 8),
            maximumChunksPerContentArchetype: 16);
        using var host = new InternalHost(hostedComposition, RuntimeOptions(identity, population + 16));
        host.Start();
        Expect(host.RuntimeForTesting.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, generationId, game) =>
            {
                for (int index = 0; index < population; index++)
                {
                    if (!world.TryInstantiate(game.Content.Character, out _)) return false;
                }
                return true;
            },
            out bool populated) && populated,
            "Hosted benchmark population failed.");

        // Intentional fixed-60 performance control. Variable/adaptive cadence
        // is disabled in both paths so cadence cannot conceal a regression.
        Action directTick = () =>
        {
            direct.World.Tick(1d / 60d);
            direct.WorldState.CommitAfterEcsStep(direct.World);
        };
        Action hostedTick = host.Tick;

        for (int tick = 0; tick < warmupTicks; tick++)
        {
            directTick();
            hostedTick();
        }

        double[] directSamples = new double[samples];
        double[] hostedSamples = new double[samples];
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int sample = 0; sample < samples; sample++)
        {
            if ((sample & 1) == 0)
            {
                directSamples[sample] = MeasureTicks(ticksPerSample, directTick);
                hostedSamples[sample] = MeasureTicks(ticksPerSample, hostedTick);
            }
            else
            {
                hostedSamples[sample] = MeasureTicks(ticksPerSample, hostedTick);
                directSamples[sample] = MeasureTicks(ticksPerSample, directTick);
            }
        }
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        double[] pairedRatios = directSamples.Zip(
            hostedSamples,
            static (direct, hosted) => hosted / Math.Max(1d, direct)).ToArray();
        Array.Sort(pairedRatios);
        Array.Sort(directSamples);
        Array.Sort(hostedSamples);
        double directMean = directSamples.Average();
        double hostedMean = hostedSamples.Average();
        double hostedP95 = Percentile(hostedSamples, 0.95);
        double ratio = Percentile(pairedRatios, 0.50);
        Expect(allocated == 0, $"Warmed direct/hosted ticks allocated {allocated} caller bytes.");
        Expect(ratio <= 1.05,
            $"The paired-median hosted runtime wrapper tax was {ratio:P1}, exceeding the 5% acceptance margin.");

        host.Stop();
        return new TestMetrics(
            HashText($"{population}:{warmupTicks}:{ticksPerSample}:{samples}"),
            0, 0, 0, 0, 0, 0,
            directMean,
            hostedMean,
            hostedP95,
            ratio,
            allocated,
            "21 alternating paired A/B samples; ratio is paired median; fixed 60 Hz, cadence changes/adaptive off, playback 1x; 4,096 scheduled rows; 256 ticks/sample; all caller/worker execution is serial on the owner thread.");
    }
    finally
    {
        direct.WorldState.Dispose();
        direct.Inventory.Dispose();
        direct.World.Dispose();
    }
}

static FixtureKeys PopulateAccountBacked(ServerGameFacet runtime)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, FixtureKeys>(
        (world, generationId, game) =>
        {
            Expect(world.TryInstantiate(game.Content.Character, out Entity alice), "Alice creation failed.");
            Expect(game.Inventory.TryCreateOwnedContainer(
                world, game.Content.Backpack, alice, 8, out Entity backpack), "Backpack creation failed.");
            Expect(game.Inventory.TryCreateItem(
                world, backpack, game.Content.Bread, 5, out Entity bread), "Bread creation failed.");
            Expect(game.Inventory.TryCreateItem(
                world, backpack, game.Content.IronSword, 1, out Entity sword), "Sword creation failed.");
            PersistentEntityId aliceId = world.GetOrAssignPersistentId(alice);
            PersistentEntityId backpackId = world.GetOrAssignPersistentId(backpack);
            PersistentEntityId breadId = world.GetOrAssignPersistentId(bread);
            PersistentEntityId swordId = world.GetOrAssignPersistentId(sword);
            Expect(world.TryGetPrefabProvenance(alice, out PrefabKey prefab), "Alice has no prefab provenance.");
            var account = new AccountId(1);
            game.Accounting.AddAccount(new AccountRecord(account));
            game.Accounting.BindTrustedIdentity(new TrustedLocalIdentity("alice-local"), account);
            game.Accounting.AddCharacter(new CharacterRecord(
                new CharacterId(1),
                account,
                0,
                "Alice",
                prefab,
                new PersistentEntityKey(world.PersistentWorldId, aliceId)));
            return new FixtureKeys(aliceId, backpackId, breadId, swordId, default);
        },
        out FixtureKeys keys),
        "The account-backed fixture did not run against the active game generation.");
    return keys;
}

static void DivergeAfterSave(ServerGameFacet runtime, FixtureKeys keys)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
        (world, generationId, game) =>
        {
            Entity sword = ResolveInWorld(world, keys.Sword);
            Entity split = ResolveInWorld(world, keys.SplitBread);
            Entity backpack = ResolveInWorld(world, keys.Backpack);
            Expect(game.Inventory.DestroyItem(world, sword).Accepted,
                "The post-save sword divergence failed.");
            Expect(game.Inventory.DestroyItem(world, split).Accepted,
                "The post-save split-stack divergence failed.");
            Expect(game.Inventory.TryCreateItem(
                world, backpack, game.Content.Apple, 3, out Entity apple),
                "The post-save Apple divergence failed.");
            _ = world.GetOrAssignPersistentId(apple);
            return true;
        },
        out bool diverged) && diverged,
        "Post-save divergence did not run.");
}

static string CaptureCanonical(
    ServerRuntime runtime,
    FixtureKeys keys,
    bool allowMissingItems = false)
{
    ulong simulationTick = runtime.SimulationTick;
    long worldTime = runtime.WorldTimeMicroseconds;
    Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, string>(
        (world, _, game) =>
        {
            var text = new StringBuilder(512);
            text.Append("tick=").Append(simulationTick)
                .Append("|world-time=").Append(worldTime)
                .Append("|world=").Append(world.PersistentWorldId.Value);
            Entity alice = ResolveInWorld(world, keys.Alice);
            Expect(game.TryReadSimulationState(world, alice, out GameSimulationState simulation),
                "Alice has no simulation state.");
            text.Append("|alice=").Append(keys.Alice.Value)
                .Append(':').Append(simulation.TickMarker);

            AccountingSnapshot accounting = game.Accounting.CaptureSnapshot();
            foreach (AccountRecord account in accounting.Accounts)
                text.Append("|account=").Append(account.AccountId.Value).Append(':').Append((byte)account.Status);
            foreach (CharacterRecord character in accounting.Characters)
            {
                text.Append("|character=").Append(character.CharacterId.Value)
                    .Append(':').Append(character.AccountId.Value)
                    .Append(':').Append(character.Slot)
                    .Append(':').Append(character.DisplayName)
                    .Append(':').Append(character.PrefabKey.Value)
                    .Append(':').Append(character.PersistentEntityKey.World.Value)
                    .Append(':').Append(character.PersistentEntityKey.Entity.Value);
            }

            InventoryIndexSnapshot index = game.Inventory.Index.CaptureSnapshot();
            text.Append("|index=").Append(index.ContainerCount)
                .Append(':').Append(index.ItemCount)
                .Append(':').Append(index.ContainedItemCount);
            AppendContainer(text, world, game.Inventory, keys.Backpack);
            AppendItem(text, world, game.Inventory, keys.Bread, allowMissingItems);
            AppendItem(text, world, game.Inventory, keys.SplitBread, allowMissingItems);
            AppendItem(text, world, game.Inventory, keys.Sword, allowMissingItems);
            return HashText(text.ToString());
        },
        out string canonical),
        "Canonical state capture did not run.");
    return canonical;
}

static void AppendContainer(
    StringBuilder text,
    EcsWorld world,
    InventoryGenerationState inventory,
    PersistentEntityId containerId)
{
    Entity container = ResolveInWorld(world, containerId);
    InventoryItemView[] rows = inventory.Snapshot(world, container);
    text.Append("|container=").Append(containerId.Value).Append('[');
    foreach (InventoryItemView row in rows)
    {
        text.Append(row.PersistentId.Value).Append(':')
            .Append(row.Prefab.Value).Append(':')
            .Append((byte)row.EquipSlot).Append(':')
            .Append(row.Quantity).Append(';');
    }
    text.Append(']');
}

static void AppendItem(
    StringBuilder text,
    EcsWorld world,
    InventoryGenerationState inventory,
    PersistentEntityId id,
    bool allowMissing)
{
    if (!world.TryResolvePersistentEntity(id, out Entity entity))
    {
        if (!allowMissing) throw new InvalidOperationException($"Persistent item {id.Value} is missing.");
        text.Append("|item=").Append(id.Value).Append(":missing");
        return;
    }
    Expect(inventory.TryReadItem(world, entity, out InventoryItem item),
        $"Persistent item {id.Value} has no InventoryItem component.");
    Expect(world.TryGetPersistentId(item.Parent, out PersistentEntityId parent),
        $"Persistent item {id.Value} has a nonpersistent parent.");
    Expect(world.TryGetPrefabProvenance(entity, out PrefabKey prefab),
        $"Persistent item {id.Value} has no prefab provenance.");
    text.Append("|item=").Append(id.Value)
        .Append(':').Append(prefab.Value)
        .Append(':').Append(parent.Value)
        .Append(':').Append((byte)item.EquipSlot)
        .Append(':').Append(item.Quantity);
}

static GameSimulationState ReadSimulation(ServerGameFacet runtime, PersistentEntityId id)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, GameSimulationState>(
        (world, _, game) =>
        {
            Entity entity = ResolveInWorld(world, id);
            Expect(game.TryReadSimulationState(world, entity, out GameSimulationState value),
                "The playable entity has no simulation state.");
            return value;
        },
        out GameSimulationState state),
        "Simulation read did not run.");
    return state;
}

static (int Accounts, int Characters) GetAccountingCounts(ServerGameFacet runtime)
{
    Expect(runtime.TryUseActiveGeneration<IGameGenerationStateProvider, (int, int)>(
        (_, _, game) => (game.Accounting.AccountCount, game.Accounting.CharacterCount),
        out (int, int) counts),
        "Accounting count read did not run.");
    return counts;
}

static PersistentEntityId GetPersistentId(ServerGameFacet runtime, Entity entity)
{
    Expect(runtime.TryUseActiveGeneration(
        (world, _) => world.GetOrAssignPersistentId(entity),
        out PersistentEntityId id),
        "Persistent identity assignment did not run.");
    return id;
}

static PersistentWorldClockState CapturePersistentClock(ServerRuntime runtime)
{
    Expect(runtime.Game.TryCapturePersistentClockState(out PersistentWorldClockState state),
        "The active persistent clock state could not be captured.");
    return state;
}

static Entity Resolve(ServerGameFacet runtime, PersistentEntityId id)
{
    Expect(runtime.TryUseActiveGeneration(
        (world, generationId) => ResolveInWorld(world, id),
        out Entity entity),
        "Persistent entity resolution did not run.");
    return entity;
}

static Entity ResolveInWorld(EcsWorld world, PersistentEntityId id)
{
    if (!world.TryResolvePersistentEntity(id, out Entity entity))
        throw new InvalidOperationException($"Persistent entity {id.Value} did not resolve.");
    return entity;
}

static bool IsNotAlive(ServerGameFacet runtime, Entity entity)
{
    Expect(runtime.TryUseActiveGeneration(
        (world, _) => !world.IsAlive(entity),
        out bool stale),
        "Stale-handle validation did not run.");
    return stale;
}

static ServerRuntimeOptions RuntimeOptions(
    PersistenceIdentityOptions identity,
    int capacity = 512,
    int frequencyHz = 60,
    int worldTimeRate = 24) => new(
    UltraSimGameComposition.CreateWorldOptions(capacity, identity),
    new WorldClockOptions(simulationFrequencyHz: frequencyHz, worldTimeRate: worldTimeRate),
    allowVariablePlayback: true);

static PersistenceRestoreOptions RestoreOptions() => new(
    new PersistenceLimits(
        maximumArtifactBytes: 16 * 1024 * 1024,
        maximumEntities: 100_000,
        maximumShapes: 64,
        maximumComponents: 64,
        maximumSections: 256,
        maximumComponentPayloadBytes: 8 * 1024 * 1024,
        maximumTemporaryBytes: 64 * 1024 * 1024),
    ChunkLayoutOptions.KiB16);

static double MeasureTicks(int count, Action tick)
{
    long started = Stopwatch.GetTimestamp();
    for (int index = 0; index < count; index++) tick();
    return ToNanoseconds(Stopwatch.GetTimestamp() - started) / count;
}

static double Percentile(double[] sorted, double fraction) =>
    sorted[Math.Clamp((int)Math.Ceiling(sorted.Length * fraction) - 1, 0, sorted.Length - 1)];

static double ToNanoseconds(long stopwatchTicks) =>
    stopwatchTicks * (1_000_000_000d / Stopwatch.Frequency);

static string HashText(string value) =>
    Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

static void ExpectRowNames(ReadOnlySpan<UiListRow> rows, params string[] expected)
{
    string[] actual = rows.ToArray()
        .Select(static row => row.Values.Length == 0 ? string.Empty : row.Values[0].Text ?? string.Empty)
        .Order(StringComparer.Ordinal)
        .ToArray();
    string[] orderedExpected = expected.Order(StringComparer.Ordinal).ToArray();
    Expect(actual.SequenceEqual(orderedExpected),
        $"Expected UI rows [{string.Join(", ", orderedExpected)}], got [{string.Join(", ", actual)}].");
}

static UiRowId FindRow(ReadOnlySpan<UiListRow> rows, string name)
{
    foreach (UiListRow row in rows)
    {
        if (row.Values.Length != 0 && row.Values[0].Text == name) return row.Id;
    }
    throw new InvalidOperationException($"UI row '{name}' was not found.");
}

static void Run(string name, Func<TestMetrics> body, List<TestResult> results)
{
    long started = Stopwatch.GetTimestamp();
    TestMetrics metrics = body();
    double elapsed = ToNanoseconds(Stopwatch.GetTimestamp() - started);
    results.Add(new TestResult(name, true, elapsed, metrics));
    Console.WriteLine($"PASS {name}");
}

static string TempRoot(string name) =>
    Path.Combine(Path.GetTempPath(), "ultrasim-s0-8-" + name + '-' + Guid.NewGuid().ToString("N"));

static string? ReadOption(string[] values, string key)
{
    for (int index = 0; index + 1 < values.Length; index++)
    {
        if (StringComparer.Ordinal.Equals(values[index], key)) return values[index + 1];
    }
    return null;
}

static void Expect(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

readonly record struct FixtureKeys(
    PersistentEntityId Alice,
    PersistentEntityId Backpack,
    PersistentEntityId Bread,
    PersistentEntityId Sword,
    PersistentEntityId SplitBread);

sealed record AcceptanceResult(
    string Workload,
    DateTimeOffset CapturedAtUtc,
    string Runtime,
    int LogicalProcessorCount,
    TestResult[] Tests);

sealed record TestResult(
    string Name,
    bool Passed,
    double TotalNanoseconds,
    TestMetrics Metrics);

sealed record TestMetrics(
    string CanonicalHash,
    double SaveMeanNanoseconds,
    double LoadMeanNanoseconds,
    double CapturePauseNanoseconds,
    long SaveAllocatedBytes,
    long LoadAllocatedBytes,
    long ArtifactBytes,
    double DirectTickMeanNanoseconds,
    double HostedTickMeanNanoseconds,
    double HostedTickP95Nanoseconds,
    double TickWrapperRatio,
    long TimedTickAllocatedBytes,
    string Notes);

sealed class NullHostLogSink : IHostLogSink
{
    public void Write(HostLogLevel level, string message) { }
}

sealed class RecordingUiPort : IUiSemanticPort, IDisposable
{
    private readonly LocalSemanticLink _link;
    private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];
    private bool _disposed;

    internal RecordingUiPort(LocalSemanticLink link)
    {
        _link = link;
        _link.ActionSubmitted += Forward;
    }

    internal int ColdOpenCount { get; private set; }
    internal int HotOpenCount { get; private set; }
    internal UiListDeltaKind[] LastListKinds { get; private set; } = [];

    public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
        _actions.Add(instance, handler);
    public void UnbindActions(UiViewInstanceId instance) => _actions.Remove(instance);

    public UiViewInstanceId OpenView(UiOpenView operation)
    {
        ColdOpenCount++;
        return _link.OpenView(operation);
    }

    public UiViewInstanceId OpenHotView(UiHotOpenView operation)
    {
        HotOpenCount++;
        return _link.OpenHotView(operation);
    }

    public void UpdateView(UiUpdateView operation)
    {
        LastListKinds = operation.Delta.Lists.ToArray()
            .Select(static patch => patch.Kind)
            .ToArray();
        _link.UpdateView(operation);
    }

    public bool CloseView(UiCloseView operation) => _link.CloseView(operation);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _link.ActionSubmitted -= Forward;
        _actions.Clear();
    }

    private void Forward(UiActionRequest request)
    {
        if (_actions.TryGetValue(request.Instance, out Action<UiActionRequest>? handler)) handler(request);
    }
}

sealed class RecordingRenderer : IUiRenderer
{
    internal int Creates { get; private set; }
    internal int Applies { get; private set; }
    internal int Closes { get; private set; }

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        Creates++;
        return new Handle();
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction) => Applies++;

    public void Close(IUiRenderHandle handle) => Closes++;

    private sealed class Handle : IUiRenderHandle;
}
