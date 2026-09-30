using System.Net;
using System.Text.Json;
using UltraSim.Framework.ControlPlane;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.Composition;
using UltraSim.Game.Persistence;
using UltraSim.Game.Admission;
using UltraSim.Game.World;
using UltraSim.ECS;
using UltraSim.Framework.Persistence;
using UltraSim.Network;
using UltraSim.Server.Operator;

var tests = new (string Name, Action Body)[]
{
    ("AuthorizationAuditAndFencing", AuthorizationAuditAndFencing),
    ("SaveBackupAndSupervisedRestart", SaveBackupAndSupervisedRestart),
    ("AutosaveCadenceAndConsole", AutosaveCadenceAndConsole),
    ("BackupRetentionAndIntegrity", BackupRetentionAndIntegrity),
    ("ShutdownWaitsForBackupThenSaves", ShutdownWaitsForBackupThenSaves),
    ("ColdRecoveryRetiresSavedGuestsAndFencesCorruptCurrent", ColdRecoveryRetiresSavedGuestsAndFencesCorruptCurrent),
    ("RestartRejectsExhaustedSaveSequenceWithoutClosingAdmission", RestartRejectsExhaustedSaveSequenceWithoutClosingAdmission),
    ("SelectedBackupColdRestorePublishesFreshLiveGeneration", SelectedBackupColdRestorePublishesFreshLiveGeneration),
    ("BoundedOperatorLogIsObservableWithoutChangingAuthority", BoundedOperatorLogIsObservableWithoutChangingAuthority),
    ("LocalGuiFramingUsesSameTypedOwnerActions", LocalGuiFramingUsesSameTypedOwnerActions),
};
foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine("PASS " + name);
}

static void LocalGuiFramingUsesSameTypedOwnerActions()
{
    using var fixture = new Fixture(new ServerOperatorPolicy(false, 300, 2, false));
    var gui = new OperatorGuiStdioAdapter(fixture.Registry, fixture.Principal);
    static string Line(OperatorGuiRequest request) => OperatorGuiStdioAdapter.RequestPrefix +
        JsonSerializer.Serialize(request);
    Check(!gui.TryExecute("save", out _), "The GUI adapter intercepted ordinary console input.");
    Check(gui.TryExecute(Line(new(1, "discover")), out string discovery) &&
        discovery.StartsWith(OperatorGuiStdioAdapter.ResponsePrefix, StringComparison.Ordinal) &&
        discovery.Contains("save-now", StringComparison.Ordinal) &&
        discovery.Contains("Server/Persistence", StringComparison.Ordinal),
        "The local GUI did not discover the owner-published action vocabulary.");
    Check(gui.TryExecute(Line(new(2, "action", "shutdown")), out string confirmation) &&
        confirmation.Contains("confirmation", StringComparison.OrdinalIgnoreCase) &&
        !fixture.AdmissionClosed,
        "The GUI initiated a destructive action without explicit confirmation.");
    Check(gui.TryExecute(Line(new(3, "action", "save-now")), out string save) &&
        save.Contains("Accepted", StringComparison.Ordinal),
        "The GUI Save did not route through the typed owner action.");
    Check(gui.TryExecute(Line(new(3, "action", "save-now")), out string replay) &&
        replay.Contains("stale", StringComparison.OrdinalIgnoreCase),
        "A repeated GUI request identity was accepted twice.");
    fixture.PumpUntil(() => !fixture.Service.Capture().SaveInProgress);
    Check(fixture.Service.Capture().LastSavedSequence == 1,
        "The GUI Save did not publish the same durable owner generation as console Save.");
    var console = new OperatorConsole(fixture.Registry, fixture.Principal);
    Check(console.Execute("save").StartsWith("Accepted:", StringComparison.Ordinal),
        "The independent console frontend did not reach the same owner action.");
    fixture.PumpUntil(() => !fixture.Service.Capture().SaveInProgress);
    Check(fixture.Service.Capture().LastSavedSequence == 2 && fixture.Audit.Sequence >= 2,
        "Console and GUI Save did not converge on consecutive owner publications and audit.");
    var auditRows = new OperatorAuditRow[OperatorAuditRing.Capacity];
    int auditCount = fixture.Audit.CopyTo(auditRows);
    Check(auditCount >= 2 && auditRows.Take(auditCount).Count(row =>
        row.Provider == ServerOperatorProvider.ProviderId.Value &&
        row.Action == ServerOperatorProvider.SaveNow.Value &&
        row.Status == ControlResultStatus.Accepted) == 2,
        "Console and GUI Save did not share the same audited owner action identity.");
    Check(gui.TryExecute(Line(new(4, "status")), out string status) &&
        status.Contains("last-save", StringComparison.Ordinal) &&
        status.Contains("autosave-enabled", StringComparison.Ordinal) &&
        status.Contains("playback-rate=1", StringComparison.Ordinal),
        "The GUI status did not expose current owner diagnostics and cold policy.");
    ControlProviderSnapshot playbackPublication = fixture.Registry.Discover(fixture.Principal).Single();
    Check(fixture.Registry.Read(fixture.Principal, ServerOperatorProvider.ProviderId,
        playbackPublication.Generation, new ControlId("playback-rate"),
        out ControlState? playback) == ControlResultCode.Accepted &&
        playback is { Writable: false, DisabledCode: ControlResultCode.Disabled } &&
        playback.DisabledReason.Contains("Pause", StringComparison.Ordinal),
        "The fixed-rate product did not explicitly disable unsupported Pause/playback controls.");
    Check(gui.TryExecute(Line(new(5, "participants")), out string participants) &&
        participants.Contains("(empty)", StringComparison.Ordinal),
        "The GUI participant query failed on an empty authority.");
    Check(gui.TryExecute(Line(new(6, "log")), out string events) &&
        events.Contains("Persistence", StringComparison.Ordinal),
        "The GUI event query did not use the bounded owner log.");
    // The same generic viewer model used by Godot management surfaces is the trusted
    // local/test Admin route. No remote player principal or claimed operator flag is used.
    var adminViewer = new ControlPlaneViewer(fixture.Registry, fixture.Principal);
    ControlViewerItem saveItem = adminViewer.Capture().SelectMany(category => category.Items)
        .Single(item => item.Descriptor is ControlActionDescriptor action &&
            action.Id == ServerOperatorProvider.SaveNow);
    ControlResult? adminSave = adminViewer.RequestAction(new("admin.save.1"),
        saveItem.Provider, saveItem.Generation, ServerOperatorProvider.SaveNow, []);
    Check(adminSave?.Outcome.Status == ControlResultStatus.Accepted,
        "The trusted local Admin viewer did not reach the shared Save action.");
    fixture.PumpUntil(() => !fixture.Service.Capture().SaveInProgress);
    Check(fixture.Service.Capture().LastSavedSequence == 3,
        "Console, GUI and local Admin did not publish through one durable owner.");
    int threeCount = fixture.Audit.CopyTo(auditRows);
    Check(auditRows.Take(threeCount).Count(row =>
        row.Provider == ServerOperatorProvider.ProviderId.Value &&
        row.Action == ServerOperatorProvider.SaveNow.Value &&
        row.Status == ControlResultStatus.Accepted) == 3,
        "The three frontends did not share one audited Save action identity.");
    ControlViewerItem shutdownItem = adminViewer.Capture().SelectMany(category => category.Items)
        .Single(item => item.Descriptor is ControlActionDescriptor action &&
            action.Id == ServerOperatorProvider.Shutdown);
    Check(adminViewer.RequestAction(new("admin.stop.1"), shutdownItem.Provider,
        shutdownItem.Generation, ServerOperatorProvider.Shutdown, []) is null &&
        adminViewer.PendingConfirmation is not null,
        "The local Admin viewer skipped the required lifecycle confirmation gesture.");
    Check(adminViewer.ConfirmAction().Outcome.Status == ControlResultStatus.Accepted &&
        fixture.Service.Capture().ExitDisposition == OperatorExitDisposition.Shutdown,
        "The local Admin viewer did not route confirmed Shutdown to the host owner.");
}

static void BoundedOperatorLogIsObservableWithoutChangingAuthority()
{
    var ring = new OperatorLogRing();
    for (int index = 1; index <= 300; index++)
        ring.Write(OperatorLogSeverity.Information, OperatorLogCategory.Lifecycle,
            "event." + index, DateTimeOffset.UnixEpoch);
    var rows = new OperatorLogEntry[OperatorLogRing.Capacity];
    Check(ring.Count == OperatorLogRing.Capacity && ring.CopyTo(rows) == rows.Length &&
        rows[0].Sequence == 45 && rows[^1].Sequence == 300 && ring.Dropped == 0,
        "Operational log retention did not retain the latest finite event window.");

    using var fixture = new Fixture(new ServerOperatorPolicy(false, 300, 2, false));
    var console = new OperatorConsole(fixture.Registry, fixture.Principal);
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.SaveNow).Outcome.Status ==
        ControlResultStatus.Accepted, "Log control Save was rejected.");
    fixture.PumpUntil(() => !fixture.Service.Capture().SaveInProgress);
    Check(console.Execute("logs").Contains("Persistence", StringComparison.Ordinal),
        "Console did not discover owner-published operational log events.");
    ControlProviderSnapshot published = fixture.Registry.Discover(fixture.Principal).Single();
    Check(fixture.Registry.Read(fixture.Principal, ServerOperatorProvider.ProviderId,
        published.Generation, new ControlCollectionId("log"), out ControlCollectionSnapshot? log) ==
        ControlResultCode.Accepted && log is not null && log.Rows.Count >= 2,
        "The generic Control Plane did not expose the bounded operational log.");
}

static void SelectedBackupColdRestorePublishesFreshLiveGeneration()
{
    string root = Path.Combine(Path.GetTempPath(),
        $"ultrasim-p7-backup-recovery-{Environment.ProcessId}-{Guid.NewGuid():N}");
    string saves = Path.Combine(root, "saves");
    string backups = Path.Combine(root, "backups");
    try
    {
        using (ServerOperatorInstanceLease first = ServerOperatorInstanceLease.Acquire(root))
        {
            bool rejected = false;
            try { using ServerOperatorInstanceLease duplicate = ServerOperatorInstanceLease.Acquire(root); }
            catch (IOException) { rejected = true; }
            Check(rejected, "The identity lease admitted concurrent live/recovery ownership.");
        }
        using ServerOperatorInstanceLease exclusive = ServerOperatorInstanceLease.Acquire(root);
        using (SharedPlayableAuthority source = SharedPlayableAuthority.CreateConceptDemo())
        {
            var coordinator = new SaveSetCoordinator(saves);
            var persistence = new GameRuntimePersistence(source.Runtime.Game, source.Composition,
                new PersistenceRestoreOptions(PersistenceLimits.ServerDefault, ChunkLayoutOptions.KiB16),
                coordinator);
            Check(persistence.TrySave(new SaveSetContext(1, 1), static capture => capture()).Succeeded,
                "Backup source generation did not publish.");
            var store = new OperatorBackupStore(saves, backups, 2);
            Check(store.TryBackupCurrent().Succeeded, "The selected backup did not publish.");
            Check(persistence.TrySave(new SaveSetContext(2, 2), static capture => capture()).Succeeded,
                "Newer live generation did not publish.");
        }
        string before = File.ReadAllText(Path.Combine(saves, "CURRENT"));
        var backupStore = new OperatorBackupStore(saves, backups, 2);
        var candidateRows = new OperatorBackupCandidate[128];
        Check(backupStore.CopyCandidates(candidateRows) == 1 &&
            candidateRows[0].Sequence == 1 && candidateRows[0].Complete,
            "The selected complete backup was not visible to the bounded recovery browser.");
        bool missingRejected = false;
        try { using OperatorColdRestoreStage missing = backupStore.StageSelectedForColdRecovery(2); }
        catch (Exception failure) when (failure is IOException or InvalidDataException)
        { missingRejected = true; }
        Check(missingRejected && File.ReadAllText(Path.Combine(saves, "CURRENT")) == before,
            "An unavailable selected backup changed the live CURRENT generation.");
        string stagedRoot;
        using (OperatorColdRestoreStage staged = backupStore.StageSelectedForColdRecovery(1))
        {
            stagedRoot = staged.SaveRoot;
            using SharedPlayableAuthority restored = SharedPlayableAuthority.CreateConceptDemo(
                coldRestore: (runtime, composition) => ServerOperatorColdRecovery.Restore(
                    runtime, composition, staged.SaveRoot, SaveSetRecoveryMode.StrictCurrent));
            Check(restored.RestoredSaveSequence == 1,
                "The selected backup did not pass normal staged restore validation.");
            var live = new SaveSetCoordinator(saves);
            Check(live.TryGetNextSequence(out ulong sequence) && sequence == 3,
                "The recovery publication did not respect live save high-water.");
            var persistence = new GameRuntimePersistence(restored.Runtime.Game, restored.Composition,
                new PersistenceRestoreOptions(PersistenceLimits.ServerDefault, ChunkLayoutOptions.KiB16),
                live);
            Check(persistence.TrySave(new SaveSetContext(sequence, 3), static capture => capture()).Succeeded,
                "The recovered backup did not publish a fresh live generation.");
        }
        Check(!Directory.Exists(stagedRoot), "Private selected-backup staging was retained.");
        using SharedPlayableAuthority reopened = SharedPlayableAuthority.CreateConceptDemo(
            coldRestore: (runtime, composition) => ServerOperatorColdRecovery.Restore(
                runtime, composition, saves, SaveSetRecoveryMode.StrictCurrent));
        Check(reopened.RestoredSaveSequence == 3 &&
            File.ReadAllText(Path.Combine(saves, "CURRENT")) == 3UL.ToString("D20") + "\n",
            "A normal cold start did not select the newly published recovery generation.");
        string current = File.ReadAllText(Path.Combine(saves, "CURRENT"));
        File.AppendAllText(Path.Combine(backups, 1UL.ToString("D20"), "manifest.json"), "corrupt");
        bool corruptRejected = false;
        try { using OperatorColdRestoreStage corrupt = backupStore.StageSelectedForColdRecovery(1); }
        catch (Exception failure) when (failure is IOException or InvalidDataException or
            System.Text.Json.JsonException) { corruptRejected = true; }
        Check(corruptRejected && File.ReadAllText(Path.Combine(saves, "CURRENT")) == current,
            "Corrupt selected-backup bytes changed the recovered live generation.");
        Check(backupStore.CopyCandidates(candidateRows) == 1 &&
            candidateRows[0].Sequence == 1 && !candidateRows[0].Complete,
            "The recovery browser falsely presented a corrupt candidate as complete.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void RestartRejectsExhaustedSaveSequenceWithoutClosingAdmission()
{
    using var fixture = new Fixture(new ServerOperatorPolicy(false, 300, 2, true));
    Directory.CreateDirectory(Path.Combine(fixture.SaveRoot, "generations",
        ulong.MaxValue.ToString("D20")));
    ControlResult restart = fixture.Invoke(fixture.Principal,
        ServerOperatorProvider.RestartWithSave);
    Check(restart.Outcome.Status == ControlResultStatus.Rejected &&
        !fixture.AdmissionClosed &&
        fixture.Service.Capture().ExitDisposition == OperatorExitDisposition.None,
        "A final save that cannot reserve a sequence still closed admission or requested restart.");
}

static void ColdRecoveryRetiresSavedGuestsAndFencesCorruptCurrent()
{
    string root = Path.Combine(Path.GetTempPath(),
        $"ultrasim-p7-cold-recovery-{Environment.ProcessId}-{Guid.NewGuid():N}");
    string saves = Path.Combine(root, "saves");
    try
    {
        using (SharedPlayableAuthority original = SharedPlayableAuthority.CreateConceptDemo(
            new SharedPlayableAuthorityLimits(2)))
        {
            bool created = original.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (world, _, game) =>
                {
                    if (!original.Composition.TryCreateGuestPlayable(world, new ParticipantId(1),
                            GameGuestBody.HumanFemale, out Entity actor))
                        return false;
                    return GamePlayableWorld.PopulateSharedParticipant(world, game, actor,
                        GamePlayableWorld.PlayerStart).IsValid;
                }, out bool populated) && populated;
            Check(created, "The durable guest fixture could not be populated.");
            original.Runtime.Tick();
            var coordinator = new SaveSetCoordinator(saves);
            var persistence = new GameRuntimePersistence(original.Runtime.Game, original.Composition,
                new PersistenceRestoreOptions(PersistenceLimits.ServerDefault, ChunkLayoutOptions.KiB16),
                coordinator);
            Check(persistence.TrySave(new SaveSetContext(1, 1), static capture => capture()).Succeeded,
                "The first recovery generation did not publish.");
            Check(persistence.TrySave(new SaveSetContext(2, 2), static capture => capture()).Succeeded,
                "The second recovery generation did not publish.");
        }

        using (SharedPlayableAuthority strict = SharedPlayableAuthority.CreateConceptDemo(
            new SharedPlayableAuthorityLimits(2),
            coldRestore: (runtime, composition) => ServerOperatorColdRecovery.Restore(
                runtime, composition, saves, SaveSetRecoveryMode.StrictCurrent)))
            Check(strict.RestoredSaveSequence == 2 && strict.RecoveredGuestCount == 1,
                "Strict cold recovery did not reopen CURRENT and retire its process-local guest.");

        string current = File.ReadAllText(Path.Combine(saves, "CURRENT"));
        string corrupt = Path.Combine(saves, "generations", 2UL.ToString("D20"), "world.bin");
        using (var stream = new FileStream(corrupt, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            int first = stream.ReadByte();
            Check(first >= 0, "The ECS artifact was empty.");
            stream.Position = 0;
            stream.WriteByte((byte)(first ^ 0xFF));
        }
        bool strictRejected = false;
        try
        {
            using SharedPlayableAuthority rejected = SharedPlayableAuthority.CreateConceptDemo(
                new SharedPlayableAuthorityLimits(2),
                coldRestore: (runtime, composition) => ServerOperatorColdRecovery.Restore(
                    runtime, composition, saves, SaveSetRecoveryMode.StrictCurrent));
        }
        catch (InvalidDataException) { strictRejected = true; }
        Check(strictRejected, "Strict cold recovery accepted a corrupt CURRENT generation.");
        using SharedPlayableAuthority recovered = SharedPlayableAuthority.CreateConceptDemo(
            new SharedPlayableAuthorityLimits(2),
            coldRestore: (runtime, composition) => ServerOperatorColdRecovery.Restore(
                runtime, composition, saves, SaveSetRecoveryMode.CurrentThenNewestComplete));
        Check(recovered.RestoredSaveSequence == 1 && recovered.RecoveredGuestCount == 1,
            "Fallback did not select the intact first generation and retire the saved guest.");
        Check(recovered.GetSnapshot().ParticipantCount == 0 && recovered.Admission.SessionCount == 0,
            "Cold recovery invented a live participant session.");
        Check(File.ReadAllText(Path.Combine(saves, "CURRENT")) == current,
            "Cold recovery rewrote the durable CURRENT pointer.");
        Check(recovered.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                if (!world.TryResolvePersistentEntity(recovered.Scene.CabinDoorId, out Entity door) ||
                    door != recovered.Scene.CabinDoor)
                    return false;
                for (uint index = 0; index < (uint)world.Options.EntityCapacity.MaximumEntities; index++)
                    if (world.TryGetEntity(index, out Entity entity) &&
                        world.TryGetPrefabProvenance(entity, out PrefabKey prefab) &&
                        (prefab == GamePrefabKeys.Character || prefab == GamePrefabKeys.CharacterMale))
                        return false;
                return true;
            }, out bool clean) && clean,
            "Recovered scene identity or transient guest retirement was incorrect.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void AuthorizationAuditAndFencing()
{
    using var fixture = new Fixture(new ServerOperatorPolicy(false, 300, 2, false));
    var reader = new ControlPrincipal("reader", ControlPrincipalKind.LocalOperator,
        [ServerOperatorProvider.Read], "test/read");
    var player = new ControlPrincipal("player", ControlPrincipalKind.LocalUser,
        [ServerOperatorProvider.Read, ServerOperatorProvider.Save], "test/player");
    ControlProviderSnapshot publication = fixture.Registry.Discover(fixture.Principal).Single();
    Check(fixture.Registry.Discover(player).Count == 0, "Player saw operator publication.");
    ControlResult unauthorized = fixture.Invoke(reader, ServerOperatorProvider.SaveNow, publication);
    Check(unauthorized.Outcome.Code == ControlResultCode.Unauthorized,
        "Read-only principal could save.");
    Check(fixture.Service.Capture().LastSavedSequence == 0, "Unauthorized request mutated authority.");
    Check(fixture.Audit.Count == 1, "Rejected request was not audited.");
    ControlResult stale = fixture.Registry.Invoke(ControlActionRequest.Create(
        new(new("stale"), fixture.Principal, DateTimeOffset.UtcNow),
        ServerOperatorProvider.ProviderId, ServerOperatorProvider.SaveNow,
        publication.Generation + 1, [], publication.Revision));
    Check(stale.Outcome.Code == ControlResultCode.StaleGeneration,
        "Stale provider generation was accepted.");
    Check(fixture.Audit.Count == 2, "Stale request was not audited.");
    ControlResult accepted = fixture.Invoke(fixture.Principal, ServerOperatorProvider.SaveNow);
    Check(accepted.Outcome.Status == ControlResultStatus.Accepted, "Save was rejected.");
    fixture.PumpUntil(() => !fixture.Service.Capture().SaveInProgress);
    Check(fixture.Service.Capture().LastSavedSequence == 1, "Save did not publish.");
    Check(fixture.Registry.Discover(fixture.Principal).Single().Revision > publication.Revision,
        "Audit/service revision did not advance.");
    OperatorAuditRow[] audit = new OperatorAuditRow[OperatorAuditRing.Capacity];
    int count = fixture.Audit.CopyTo(audit);
    Check(count == 3 && audit[0].Code == ControlResultCode.Unauthorized &&
        audit[2].Action == "save-now", "Audit attribution was incorrect.");
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.Kick,
        arguments: [ControlValue.UInt64(42)]).Outcome.Code == ControlResultCode.OwnerRejected,
        "Stale participant kick was accepted.");
    ControlValue[] oversizedAuditArguments = Enumerable.Repeat(
        ControlValue.Option(new ControlOptionId(new string('a', 90))), 16).ToArray();
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.SaveNow,
        arguments: oversizedAuditArguments).Outcome.Code == ControlResultCode.InvalidValue,
        "Invalid action arguments reached owner.");
    ControlProviderSnapshot latest = fixture.Registry.Discover(fixture.Principal).Single();
    Check(fixture.Registry.Read(fixture.Principal, ServerOperatorProvider.ProviderId,
        latest.Generation, new ControlCollectionId("audit"), out ControlCollectionSnapshot? rows) ==
        ControlResultCode.Accepted && rows is not null && rows.Rows.Count == 5,
        "Bounded audit could not be read after invalid oversized argument set.");
}

static void SaveBackupAndSupervisedRestart()
{
    using var fixture = new Fixture(new ServerOperatorPolicy(false, 300, 2, true));
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.BackupNow).Outcome.Status ==
        ControlResultStatus.Accepted, "Backup request was rejected.");
    fixture.PumpUntil(() => !fixture.Service.Capture().BackupInProgress);
    Check(fixture.Service.Capture().LastBackupSequence == 0 &&
        fixture.Service.Capture().LastFailure.Length != 0,
        "Backup of missing CURRENT did not fail closed.");
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.SaveNow).Outcome.Status ==
        ControlResultStatus.Accepted, "Save was rejected.");
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.SaveNow).Outcome.Status ==
        ControlResultStatus.Rejected, "Overlapping save was admitted.");
    fixture.PumpUntil(() => !fixture.Service.Capture().SaveInProgress);
    Check(fixture.Service.Capture().LastSavedSequence == 1, "Save sequence was not published.");
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.BackupNow).Outcome.Status ==
        ControlResultStatus.Accepted, "Backup was rejected.");
    fixture.PumpUntil(() => !fixture.Service.Capture().BackupInProgress);
    Span<ulong> sequences = stackalloc ulong[2];
    Check(fixture.Service.Backups.CopyCompleteSequences(sequences) == 1 && sequences[0] == 1,
        "Validated backup was not published.");
    ControlResult restart = fixture.Invoke(fixture.Principal, ServerOperatorProvider.RestartWithSave);
    Check(restart.Outcome.Status == ControlResultStatus.Accepted &&
        fixture.Service.Capture().ExitDisposition == OperatorExitDisposition.None &&
        fixture.AdmissionClosed,
        "Restart did not close admission immediately after its coherent final capture.");
    fixture.PumpUntil(() => fixture.Service.Capture().ExitDisposition != OperatorExitDisposition.None);
    Check(fixture.Service.Capture().ExitDisposition == OperatorExitDisposition.RestartRequested &&
        fixture.Service.Capture().LastSavedSequence == 2 && fixture.AdmissionClosed,
        "Restart did not publish a fresh save before closing admission and requesting supervisor exit.");
}

static void AutosaveCadenceAndConsole()
{
    var clock = new ManualTime();
    using var fixture = new Fixture(new ServerOperatorPolicy(true, 10, 2, false), clock);
    var console = new OperatorConsole(fixture.Registry, fixture.Principal);
    Check(console.Execute("help").Contains("save", StringComparison.Ordinal),
        "Console help omitted published action.");
    Check(console.Execute("restart") .StartsWith("Rejected:", StringComparison.Ordinal),
        "Restart lacked confirmation.");
    Check(console.Execute("kick 42 confirm").Contains("OwnerRejected", StringComparison.Ordinal),
        "Console did not route typed Kick.");
    clock.AdvanceSeconds(9);
    fixture.Service.Pump();
    Check(fixture.Service.Capture().LastSavedSequence == 0 &&
        !fixture.Service.Capture().SaveInProgress, "Autosave fired early.");
    clock.AdvanceSeconds(1);
    fixture.Service.Pump();
    fixture.PumpUntil(() => !fixture.Service.Capture().SaveInProgress);
    Check(fixture.Service.Capture().LastSavedSequence == 1, "Autosave did not use Save Now path.");
    Check(console.Execute("shutdown confirm").StartsWith("Accepted:", StringComparison.Ordinal),
        "Console shutdown did not reach owner action.");
    Check(fixture.Service.Capture().ExitDisposition == OperatorExitDisposition.Shutdown,
        "Console shutdown disposition was not published.");
    Check(fixture.AdmissionClosed, "Shutdown exit did not close admission.");
}

static void BackupRetentionAndIntegrity()
{
    using var fixture = new Fixture(new ServerOperatorPolicy(false, 300, 2, false));
    for (ulong sequence = 1; sequence <= 3; sequence++)
    {
        Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.SaveNow).Outcome.Status ==
            ControlResultStatus.Accepted, "Save was rejected.");
        fixture.PumpUntil(() => !fixture.Service.Capture().SaveInProgress);
        Check(fixture.Service.Capture().LastSavedSequence == sequence, "Save sequence skipped.");
        Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.BackupNow).Outcome.Status ==
            ControlResultStatus.Accepted, "Backup was rejected.");
        fixture.PumpUntil(() => !fixture.Service.Capture().BackupInProgress);
    }
    Span<ulong> sequences = stackalloc ulong[2];
    Check(fixture.Service.Backups.CopyCompleteSequences(sequences) == 2 &&
        sequences[0] == 3 && sequences[1] == 2,
        "Backup retention removed a reachable generation or exceeded its bound.");
    string corrupted = Path.Combine(fixture.Service.Backups.BackupRoot,
        3UL.ToString("D20"), "manifest.json");
    File.AppendAllText(corrupted, "corrupt");
    bool rejected = false;
    try { fixture.Service.Backups.CopyCompleteSequences(sequences); }
    catch (Exception failure) when (failure is InvalidDataException or System.Text.Json.JsonException)
    { rejected = true; }
    Check(rejected, "Corrupted backup appeared complete.");
}

static void ShutdownWaitsForBackupThenSaves()
{
    using var fixture = new Fixture(new ServerOperatorPolicy(false, 300, 2, true));
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.SaveNow).Outcome.Status ==
        ControlResultStatus.Accepted, "Initial save was rejected.");
    fixture.PumpUntil(() => !fixture.Service.Capture().SaveInProgress);
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.BackupNow).Outcome.Status ==
        ControlResultStatus.Accepted, "Backup was rejected.");
    Check(fixture.Invoke(fixture.Principal, ServerOperatorProvider.Shutdown).Outcome.Status ==
        ControlResultStatus.Accepted, "Shutdown did not coalesce behind backup.");
    Check(!fixture.AdmissionClosed,
        "Shutdown closed admission before the prior backup and final capture completed.");
    fixture.PumpUntil(() => fixture.Service.Capture().ExitDisposition ==
        OperatorExitDisposition.Shutdown);
    Check(fixture.Service.Capture().LastBackupSequence == 1 &&
        fixture.Service.Capture().LastSavedSequence == 2 && fixture.AdmissionClosed,
        "Shutdown did not wait for backup, publish final save and close admission.");
}

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

sealed class ManualTime : TimeProvider
{
    private long _timestamp;
    public override long TimestampFrequency => 1_000;
    public override long GetTimestamp() => _timestamp;
    public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddMilliseconds(_timestamp);
    public void AdvanceSeconds(int seconds) => _timestamp += seconds * TimestampFrequency;
}

sealed class Fixture : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(),
        "ultrasim-p7-operator-" + Guid.NewGuid().ToString("N"));
    private readonly NetworkServerIdentity _identity = NetworkServerIdentity.CreateEphemeral();
    private readonly SharedPlayableAuthority _authority = SharedPlayableAuthority.CreateConceptDemo();
    private readonly HostedUdpAdmissionHost _host;
    private readonly ServerOperatorPublication _publication;
    private ulong _request;

    public Fixture(ServerOperatorPolicy policy, TimeProvider? time = null)
    {
        Directory.CreateDirectory(_root);
        _host = new HostedUdpAdmissionHost(new IPEndPoint(IPAddress.Loopback, 0), _identity,
            NetworkProtocolLimits.Default, UdpProtocolLimits.Default,
            SharedUdpProtocolLimits.Default with { MaximumSessions = 64 },
            HostedUdpAdmissionLimits.Default,
            new SharedRemoteGuestPlayableApplicationFactory(_authority,
                new ApplicationRealmId(0x7, 0x7)), UltraSimGameProtocol.Manifest);
        Service = new ServerOperatorService(_authority, _host,
            _authority.Runtime.Game, _authority.Composition,
            Path.Combine(_root, "saves"), Path.Combine(_root, "backups"), policy, time);
        Audit = new OperatorAuditRing();
        Registry = new ControlPlaneRegistry(Audit);
        _publication = new ServerOperatorPublication(Registry, new ServerOperatorProvider(Service, Audit));
        Principal = new ControlPrincipal("fixture-operator", ControlPrincipalKind.LocalOperator,
            [ServerOperatorProvider.Read, ServerOperatorProvider.Save, ServerOperatorProvider.Backup,
             ServerOperatorProvider.Lifecycle, ServerOperatorProvider.Sessions], "test/local");
    }

    public ServerOperatorService Service { get; }
    public OperatorAuditRing Audit { get; }
    public ControlPlaneRegistry Registry { get; }
    public ControlPrincipal Principal { get; }
    public bool AdmissionClosed => _host.AdmissionClosed;
    public string SaveRoot => Path.Combine(_root, "saves");

    public ControlResult Invoke(ControlPrincipal principal, ControlActionId action,
        ControlProviderSnapshot? publication = null, ControlValue[]? arguments = null)
    {
        publication ??= Registry.Discover(Principal).Single();
        return Registry.Invoke(ControlActionRequest.Create(
            new(new("request." + ++_request), principal, DateTimeOffset.UtcNow),
            ServerOperatorProvider.ProviderId, action, publication.Generation,
            arguments ?? [], publication.Revision));
    }

    public void PumpUntil(Func<bool> done)
    {
        long deadline = Environment.TickCount64 + 30_000;
        while (!done() && Environment.TickCount64 < deadline)
        {
            _host.Pump();
            _ = _authority.TickDue();
            Service.Pump();
            Thread.Sleep(1);
        }
        if (!done()) throw new Exception("Operator operation did not finish within bound.");
        Service.Pump();
    }

    public void Dispose()
    {
        _publication.Dispose();
        Service.Dispose();
        _host.Dispose();
        _authority.Dispose();
        _identity.Dispose();
        Directory.Delete(_root, recursive: true);
    }
}
