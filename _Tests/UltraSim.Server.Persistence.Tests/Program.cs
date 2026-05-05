using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using UltraSim.Server.Accounting;
using UltraSim.Server.Persistence;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(ServerPersistenceCoordinator_Save_WritesAccountingAndWorldUnderSharedManifest), ServerPersistenceCoordinator_Save_WritesAccountingAndWorldUnderSharedManifest);
        Run(nameof(ServerPersistenceCoordinator_Load_RestoresAccountingBeforeWorld), ServerPersistenceCoordinator_Load_RestoresAccountingBeforeWorld);
        Run(nameof(ServerPersistenceCoordinator_FailureDoesNotPromotePendingSaveSet), ServerPersistenceCoordinator_FailureDoesNotPromotePendingSaveSet);
        Run(nameof(ServerPersistenceCoordinator_Load_CleansPendingAndFallsBackToBackup), ServerPersistenceCoordinator_Load_CleansPendingAndFallsBackToBackup);
        Run(nameof(ServerPersistenceCoordinator_Load_FailsCorruptCurrentWithoutBackup), ServerPersistenceCoordinator_Load_FailsCorruptCurrentWithoutBackup);
        Run(nameof(ServerPersistenceCoordinator_Load_CurrentOnlyDoesNotFallbackToBackup), ServerPersistenceCoordinator_Load_CurrentOnlyDoesNotFallbackToBackup);
        Run(nameof(ServerPersistenceCoordinator_Load_RequireCurrentRefusesBackupOnly), ServerPersistenceCoordinator_Load_RequireCurrentRefusesBackupOnly);
        Run(nameof(ServerPersistenceCoordinator_Load_WarnsWhenBackupSequenceIsNewerThanCurrent), ServerPersistenceCoordinator_Load_WarnsWhenBackupSequenceIsNewerThanCurrent);
        Run(nameof(ServerPersistenceCoordinator_RotatesWholeSaveSetBackups), ServerPersistenceCoordinator_RotatesWholeSaveSetBackups);
        Run(nameof(ServerPersistenceCoordinator_CompressedBackups_ListLoadAndRestore), ServerPersistenceCoordinator_CompressedBackups_ListLoadAndRestore);
        Run(nameof(ServerPersistenceCoordinator_ZstdBackups_ListLoadAndRestore), ServerPersistenceCoordinator_ZstdBackups_ListLoadAndRestore);
        Run(nameof(ServerPersistenceCoordinator_RestoreBackup_PromotesBackupAndCreatesSafetyBackup), ServerPersistenceCoordinator_RestoreBackup_PromotesBackupAndCreatesSafetyBackup);
        Run(nameof(ServerPersistenceCoordinator_RestoreBackup_RejectsUnsafeBackupName), ServerPersistenceCoordinator_RestoreBackup_RejectsUnsafeBackupName);
        Run(nameof(ServerPersistenceCoordinator_RestoreBackup_FailureLeavesCurrentUntouched), ServerPersistenceCoordinator_RestoreBackup_FailureLeavesCurrentUntouched);
        Run(nameof(ServerPersistenceCoordinator_RejectsUnsafeArtifactPaths), ServerPersistenceCoordinator_RejectsUnsafeArtifactPaths);

        Console.WriteLine("All Server persistence tests passed.");
    }

    private static void ServerPersistenceCoordinator_Save_WritesAccountingAndWorldUnderSharedManifest()
    {
        string root = CreateTempRoot("server_persistence_save");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var accounts = new InMemoryAccountRegistry();
            ServerAccountRecord account = accounts.CreateAccount("Alice", "hash", now);
            var characters = new InMemoryCharacterRegistry();
            var characterService = new AccountCharacterService(accounts, characters);
            CharacterCreateResult created = characterService.TryCreateCharacter(
                account.AccountId,
                "Asha",
                "mobiles/human",
                now);
            Assert.True(created.Succeeded);

            World world = CreateWorld(out ComponentId persistentPositionId);
            Entity entity = world.CreateDefault(new ArchetypeSignature([persistentPositionId]));
            Assert.True(world.SetComponent(entity, new PersistentPosition(77)));

            var coordinator = new ServerPersistenceCoordinator(
                root,
                [
                    new AccountingPersistenceParticipant(accounts, characters),
                    new EcsWorldPersistenceParticipant(world)
                ]);

            ServerPersistenceSaveResult result = coordinator.Save(now);
            Assert.True(result.Succeeded);
            Assert.True(coordinator.TryLoadCurrentManifest(out ServerPersistenceSaveManifest manifest));
            Assert.Equal(1UL, manifest.SaveSequence);
            Assert.Equal(now, manifest.SavedUtc);
            Assert.Equal(2, manifest.Artifacts.Count);
            Assert.Equal("accounting", manifest.Artifacts[0].Name);
            Assert.Equal("world", manifest.Artifacts[1].Name);
            Assert.Equal("directory", manifest.Artifacts[0].Kind);
            Assert.True(manifest.Artifacts[0].FileCount > 0);
            Assert.True(manifest.Artifacts[0].SizeBytes > 0);
            Assert.Equal("file", manifest.Artifacts[1].Kind);
            Assert.Equal(1, manifest.Artifacts[1].FileCount);
            Assert.True(manifest.Artifacts[1].SizeBytes > 0);
            Assert.True(result.Timing.TotalMilliseconds >= 0d);
            Assert.Equal(2, result.Timing.Participants.Count);
            Assert.Equal("accounting", result.Timing.Participants[0].ParticipantName);
            Assert.Equal(manifest.Artifacts[0].SizeBytes, result.Timing.Participants[0].SizeBytes);

            var loadedAccounting = new JsonAccountingPersistenceStore(coordinator.CurrentSaveSetRoot).Load();
            Assert.Equal(1, loadedAccounting.Accounts.Count);
            Assert.Equal(1, loadedAccounting.Characters.Count);
            Assert.Equal(created.Character!.CharacterId, loadedAccounting.Accounts[0].CharacterSlots[0]);

            World loadedWorld = CreateWorld(out _);
            using (var reader = new BinaryDataReader(Path.Combine(coordinator.CurrentSaveSetRoot, "world", "entities.bin")))
                new BinaryWorldPersistenceAdapter(new PersistenceTypeRegistry()).Load(loadedWorld, reader);
            var view = new WorldPersistenceView(loadedWorld, ReadOnlySpan<ComponentId>.Empty, excludeEmpty: true);
            Assert.Equal(1, view.EntityCount);
            Assert.SequenceEqual(new PersistentPosition[] { new(77) }, view.Archetypes[0].GetComponentSpan<PersistentPosition>(persistentPositionId).ToArray());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_Load_RestoresAccountingBeforeWorld()
    {
        string root = CreateTempRoot("server_persistence_load");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var accounts = new InMemoryAccountRegistry();
            ServerAccountRecord account = accounts.CreateAccount("Alice", "hash", now);
            var characters = new InMemoryCharacterRegistry();
            var characterService = new AccountCharacterService(accounts, characters);
            CharacterCreateResult created = characterService.TryCreateCharacter(
                account.AccountId,
                "Asha",
                "mobiles/human",
                now,
                new CharacterPositionSnapshot(5, 6, 7, 8));
            Assert.True(created.Succeeded);

            World saveWorld = CreateWorld(out ComponentId persistentPositionId);
            Entity entity = saveWorld.CreateDefault(new ArchetypeSignature([persistentPositionId]));
            Assert.True(saveWorld.SetComponent(entity, new PersistentPosition(91)));

            var saveCoordinator = new ServerPersistenceCoordinator(
                root,
                [
                    new AccountingPersistenceParticipant(accounts, characters),
                    new EcsWorldPersistenceParticipant(saveWorld)
                ]);
            Assert.True(saveCoordinator.Save(now).Succeeded);

            var loadedAccounts = new InMemoryAccountRegistry();
            var loadedCharacters = new InMemoryCharacterRegistry();
            World loadedWorld = CreateWorld(out _);
            var loadCoordinator = new ServerPersistenceCoordinator(
                root,
                [
                    new AccountingPersistenceParticipant(loadedAccounts, loadedCharacters),
                    new EcsWorldPersistenceParticipant(loadedWorld)
                ]);

            ServerPersistenceLoadResult loaded = loadCoordinator.Load();

            Assert.True(loaded.Succeeded);
            Assert.False(loaded.StartedFresh);
            Assert.False(loaded.LoadedBackup);
            Assert.Equal(1UL, loaded.Manifest!.SaveSequence);
            Assert.True(loaded.Timing.TotalMilliseconds >= 0d);
            Assert.Equal(2, loaded.Timing.Participants.Count);
            Assert.Equal("world", loaded.Timing.Participants[1].ParticipantName);
            Assert.True(loaded.Timing.Participants[1].SizeBytes > 0);
            Assert.True(loadedAccounts.TryGetByUsername("Alice", out ServerAccountRecord loadedAccount));
            Assert.Equal(created.Character!.CharacterId, loadedAccount.CharacterSlots[0]);
            Assert.True(loadedCharacters.TryGetCharacter(created.Character.CharacterId, out ServerCharacterRecord loadedCharacter));
            Assert.Equal(new CharacterPositionSnapshot(5, 6, 7, 8), loadedCharacter.Position);

            var view = new WorldPersistenceView(loadedWorld, ReadOnlySpan<ComponentId>.Empty, excludeEmpty: true);
            Assert.Equal(1, view.EntityCount);
            Assert.SequenceEqual(new PersistentPosition[] { new(91) }, view.Archetypes[0].GetComponentSpan<PersistentPosition>(persistentPositionId).ToArray());
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_FailureDoesNotPromotePendingSaveSet()
    {
        string root = CreateTempRoot("server_persistence_failure");
        try
        {
            DateTimeOffset firstSave = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new TextFileParticipant("stable", "stable.txt", "first")]);
            ServerPersistenceSaveResult first = coordinator.Save(firstSave);
            Assert.True(first.Succeeded);
            Assert.True(coordinator.TryLoadCurrentManifest(out ServerPersistenceSaveManifest firstManifest));
            Assert.Equal(1UL, firstManifest.SaveSequence);

            var failing = new ServerPersistenceCoordinator(
                root,
                [
                    new TextFileParticipant("partial", "partial.txt", "partial"),
                    new ThrowingParticipant("world")
                ],
                initialSaveSequence: 1);
            ServerPersistenceSaveResult failed = failing.Save(firstSave.AddMinutes(1));

            Assert.False(failed.Succeeded);
            Assert.Equal("world", failed.Failures[0].ParticipantName);
            Assert.True(coordinator.TryLoadCurrentManifest(out ServerPersistenceSaveManifest currentManifest));
            Assert.Equal(1UL, currentManifest.SaveSequence);
            Assert.False(File.Exists(Path.Combine(coordinator.CurrentSaveSetRoot, "partial.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, ".pending")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_Load_CleansPendingAndFallsBackToBackup()
    {
        string root = CreateTempRoot("server_persistence_recovery");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(BackupCount: 1));
            Assert.True(coordinator.Save(now).Succeeded);
            Assert.True(coordinator.Save(now.AddSeconds(1)).Succeeded);

            Directory.CreateDirectory(Path.Combine(root, ".pending", "interrupted-save"));
            File.WriteAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "save_manifest.json"), "{ broken json");

            var loader = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(BackupCount: 1),
                initialSaveSequence: 2);
            ServerPersistenceLoadResult loaded = loader.Load();

            Assert.True(loaded.Succeeded);
            Assert.True(loaded.LoadedBackup);
            Assert.False(loaded.StartedFresh);
            Assert.Equal(1UL, loaded.Manifest!.SaveSequence);
            Assert.False(Directory.Exists(Path.Combine(root, ".pending")));
            Assert.Contains("Removed pending save-set", string.Join('\n', loaded.Warnings));
            Assert.Contains("Current save-set is not loadable", string.Join('\n', loaded.Warnings));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_Load_FailsCorruptCurrentWithoutBackup()
    {
        string root = CreateTempRoot("server_persistence_corrupt_current");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(BackupCount: 0));
            Assert.True(coordinator.Save(now).Succeeded);
            File.WriteAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "save_manifest.json"), "{ broken json");

            var loader = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(BackupCount: 0),
                initialSaveSequence: 1);
            ServerPersistenceLoadResult loaded = loader.Load();

            Assert.False(loaded.Succeeded);
            Assert.False(loaded.StartedFresh);
            Assert.Contains("Current save-set is not loadable", string.Join('\n', loaded.Warnings));
            Assert.Contains("No valid save-set", loaded.Failures[0].Message);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_Load_CurrentOnlyDoesNotFallbackToBackup()
    {
        string root = CreateTempRoot("server_persistence_current_only");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(BackupCount: 1));
            Assert.True(coordinator.Save(now).Succeeded);
            Assert.True(coordinator.Save(now.AddSeconds(1)).Succeeded);
            File.WriteAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "save_manifest.json"), "{ broken json");

            var loader = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(
                    BackupCount: 1,
                    RecoveryMode: ServerPersistenceRecoveryMode.CurrentOnly),
                initialSaveSequence: 2);
            ServerPersistenceLoadResult loaded = loader.Load();

            Assert.False(loaded.Succeeded);
            Assert.False(loaded.LoadedBackup);
            Assert.Contains("CurrentOnly", loaded.Failures[0].Message);
            Assert.Contains("ignored 1 backup", string.Join('\n', loaded.Warnings));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_Load_RequireCurrentRefusesBackupOnly()
    {
        string root = CreateTempRoot("server_persistence_require_current");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var coordinator = new ServerPersistenceCoordinator(root, [new SequenceFileParticipant()]);
            Assert.True(coordinator.Save(now).Succeeded);

            string backupRoot = Path.Combine(root, "backups", "Most Recent (2-5-2026 12-00-00)");
            CopyDirectory(coordinator.CurrentSaveSetRoot, backupRoot);
            foreach (string entry in Directory.EnumerateFileSystemEntries(root).ToArray())
            {
                if (string.Equals(Path.GetFileName(entry), "backups", StringComparison.Ordinal))
                    continue;

                DeleteFileSystemEntry(entry);
            }

            var loader = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(RecoveryMode: ServerPersistenceRecoveryMode.RequireCurrent),
                initialSaveSequence: 1);
            ServerPersistenceLoadResult loaded = loader.Load();

            Assert.False(loaded.Succeeded);
            Assert.False(loaded.LoadedBackup);
            Assert.Contains("RequireCurrent", loaded.Failures[0].Message);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_Load_WarnsWhenBackupSequenceIsNewerThanCurrent()
    {
        string root = CreateTempRoot("server_persistence_sequence_warning");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var currentCoordinator = new ServerPersistenceCoordinator(root, [new SequenceFileParticipant()]);
            Assert.True(currentCoordinator.Save(now).Succeeded);

            string backupRoot = Path.Combine(root, "backups", "Most Recent (2-5-2026 12-00-01)");
            CopyDirectory(currentCoordinator.CurrentSaveSetRoot, backupRoot);
            string manifestPath = Path.Combine(backupRoot, "save_manifest.json");
            string manifest = File.ReadAllText(manifestPath).Replace("\"saveSequence\": 1", "\"saveSequence\": 9");
            File.WriteAllText(manifestPath, manifest);

            var loader = new ServerPersistenceCoordinator(root, [new SequenceFileParticipant()]);
            ServerPersistenceLoadResult loaded = loader.Load();

            Assert.True(loaded.Succeeded);
            Assert.False(loaded.LoadedBackup);
            Assert.Equal(1UL, loaded.Manifest!.SaveSequence);
            Assert.Contains("newer than current", string.Join('\n', loaded.Warnings));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_RotatesWholeSaveSetBackups()
    {
        string root = CreateTempRoot("server_persistence_backups");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(BackupCount: 1));

            Assert.True(coordinator.Save(now).Succeeded);
            Assert.True(coordinator.Save(now.AddSeconds(1)).Succeeded);
            Assert.True(coordinator.Save(now.AddSeconds(2)).Succeeded);

            string backupRoot = Path.Combine(root, "backups");
            Assert.True(Directory.Exists(backupRoot));
            string[] backups = Directory.GetDirectories(backupRoot);
            Assert.Equal(1, backups.Length);
            Assert.True(Path.GetFileName(backups[0]).StartsWith("Most Recent (", StringComparison.Ordinal));
            Assert.Contains("3", File.ReadAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "sequence.txt")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_CompressedBackups_ListLoadAndRestore()
    {
        string root = CreateTempRoot("server_persistence_compressed_backups");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var options = new ServerPersistenceSaveSetOptions(
                BackupCount: 1,
                BackupCompressor: new ZipServerPersistenceBackupCompressor());
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                options);

            Assert.True(coordinator.Save(now).Succeeded);
            Assert.True(coordinator.Save(now.AddSeconds(1)).Succeeded);

            string backupRoot = Path.Combine(root, "backups");
            Assert.Equal(0, Directory.GetDirectories(backupRoot).Length);
            string[] backupArchives = Directory.GetFiles(backupRoot, "*.zip");
            Assert.Equal(1, backupArchives.Length);

            IReadOnlyList<ServerPersistenceBackupSnapshot> backups = coordinator.ListBackups();
            Assert.Equal(1, backups.Count);
            Assert.Equal(1UL, backups[0].Manifest.SaveSequence);
            Assert.True(backups[0].DirectoryName.EndsWith(".zip", StringComparison.Ordinal));

            File.WriteAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "save_manifest.json"), "{ broken json");
            var participant = new SequenceFileParticipant();
            var loader = new ServerPersistenceCoordinator(root, [participant], options, initialSaveSequence: 2);
            ServerPersistenceLoadResult loaded = loader.Load();
            Assert.True(loaded.Succeeded);
            Assert.True(loaded.LoadedBackup);
            Assert.Equal("1", participant.LoadedSequence);

            ServerPersistenceRestoreResult restored = coordinator.RestoreBackup(backups[0].DirectoryName, now.AddMinutes(1));
            Assert.True(restored.Succeeded);
            Assert.Equal(1UL, restored.Manifest!.SaveSequence);
            Assert.True(restored.SafetyBackupRoot is not null);
            Assert.True(File.Exists(restored.SafetyBackupRoot!));
            Assert.Equal("1", File.ReadAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "sequence.txt")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_ZstdBackups_ListLoadAndRestore()
    {
        string root = CreateTempRoot("server_persistence_zstd_backups");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var options = new ServerPersistenceSaveSetOptions(
                BackupCount: 1,
                BackupCompressor: new ZstdServerPersistenceBackupCompressor());
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                options);

            Assert.True(coordinator.Save(now).Succeeded);
            Assert.True(coordinator.Save(now.AddSeconds(1)).Succeeded);

            string backupRoot = Path.Combine(root, "backups");
            Assert.Equal(0, Directory.GetDirectories(backupRoot).Length);
            string[] backupArchives = Directory.GetFiles(backupRoot, "*.tar.zst");
            Assert.Equal(1, backupArchives.Length);

            IReadOnlyList<ServerPersistenceBackupSnapshot> backups = coordinator.ListBackups();
            Assert.Equal(1, backups.Count);
            Assert.Equal(1UL, backups[0].Manifest.SaveSequence);
            Assert.True(backups[0].DirectoryName.EndsWith(".tar.zst", StringComparison.Ordinal));

            File.WriteAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "save_manifest.json"), "{ broken json");
            var participant = new SequenceFileParticipant();
            var loader = new ServerPersistenceCoordinator(root, [participant], options, initialSaveSequence: 2);
            ServerPersistenceLoadResult loaded = loader.Load();
            Assert.True(loaded.Succeeded);
            Assert.True(loaded.LoadedBackup);
            Assert.Equal("1", participant.LoadedSequence);

            ServerPersistenceRestoreResult restored = coordinator.RestoreBackup(backups[0].DirectoryName, now.AddMinutes(1));
            Assert.True(restored.Succeeded);
            Assert.Equal(1UL, restored.Manifest!.SaveSequence);
            Assert.True(restored.SafetyBackupRoot is not null);
            Assert.True(File.Exists(restored.SafetyBackupRoot!));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_RestoreBackup_PromotesBackupAndCreatesSafetyBackup()
    {
        string root = CreateTempRoot("server_persistence_restore");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(BackupCount: 1));
            Assert.True(coordinator.Save(now).Succeeded);
            Assert.True(coordinator.Save(now.AddSeconds(1)).Succeeded);

            IReadOnlyList<ServerPersistenceBackupSnapshot> backups = coordinator.ListBackups();
            Assert.Equal(1, backups.Count);
            Assert.Equal(1UL, backups[0].Manifest.SaveSequence);

            ServerPersistenceRestoreResult restored = coordinator.RestoreBackup(
                backups[0].DirectoryName,
                now.AddMinutes(1));

            Assert.True(restored.Succeeded);
            Assert.Equal(1UL, restored.Manifest!.SaveSequence);
            Assert.True(restored.SafetyBackupRoot is not null);
            Assert.True(Directory.Exists(restored.SafetyBackupRoot!));
            Assert.Equal("1", File.ReadAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "sequence.txt")));
            Assert.Equal("2", File.ReadAllText(Path.Combine(restored.SafetyBackupRoot!, "sequence.txt")));
            Assert.True(Directory.Exists(backups[0].RootPath));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_RestoreBackup_RejectsUnsafeBackupName()
    {
        string root = CreateTempRoot("server_persistence_restore_unsafe");
        try
        {
            var coordinator = new ServerPersistenceCoordinator(root, [new SequenceFileParticipant()]);

            ServerPersistenceRestoreResult restored = coordinator.RestoreBackup("../Most Recent", DateTimeOffset.UtcNow);

            Assert.False(restored.Succeeded);
            Assert.Contains("not safe", restored.Failures[0].Message);
            Assert.False(Directory.Exists(Path.Combine(root, ".pending")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_RestoreBackup_FailureLeavesCurrentUntouched()
    {
        string root = CreateTempRoot("server_persistence_restore_failure");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new SequenceFileParticipant()],
                new ServerPersistenceSaveSetOptions(BackupCount: 1));
            Assert.True(coordinator.Save(now).Succeeded);
            Assert.True(coordinator.Save(now.AddSeconds(1)).Succeeded);

            string backupDirectory = coordinator.ListBackups()[0].DirectoryName;
            string manifestPath = Path.Combine(root, "backups", backupDirectory, "save_manifest.json");
            File.WriteAllText(manifestPath, "{ broken json");

            ServerPersistenceRestoreResult restored = coordinator.RestoreBackup(backupDirectory, now.AddMinutes(1));

            Assert.False(restored.Succeeded);
            Assert.True(!string.IsNullOrWhiteSpace(restored.Failures[0].Message));
            Assert.Equal("2", File.ReadAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "sequence.txt")));
            Assert.False(Directory.Exists(Path.Combine(root, ".pending")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void ServerPersistenceCoordinator_RejectsUnsafeArtifactPaths()
    {
        string root = CreateTempRoot("server_persistence_unsafe_artifact");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var coordinator = new ServerPersistenceCoordinator(
                root,
                [new UnsafePathParticipant()]);

            ServerPersistenceSaveResult result = coordinator.Save(now);

            Assert.False(result.Succeeded);
            Assert.Equal("unsafe", result.Failures[0].ParticipantName);
            Assert.Contains("safe relative path", result.Failures[0].Message);
            Assert.False(File.Exists(Path.Combine(root, "save_manifest.json")));
            Assert.False(Directory.Exists(Path.Combine(root, ".pending")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static World CreateWorld(out ComponentId persistentPositionId)
    {
        var builder = new EcsSchemaBuilder();
        persistentPositionId = builder.RegisterComponent<PersistentPosition>();
        return new World(builder.Build());
    }

    private static string CreateTempRoot(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);
        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.Ordinal));
        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, file.Replace(source, destination, StringComparison.Ordinal), overwrite: true);
    }

    private static void DeleteFileSystemEntry(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
            return;
        }

        if (File.Exists(path))
            File.Delete(path);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {name}: {ex}");
            Environment.ExitCode = 1;
            throw;
        }
    }

    [PersistenceId(51001)]
    private readonly record struct PersistentPosition(int X) : ISerializableComponent<PersistentPosition>
    {
        public static void Serialize(ref BitWriter writer, in PersistentPosition value)
        {
            writer.WriteInt32(value.X);
        }

        public static PersistentPosition Deserialize(ref BitReader reader, ushort version)
        {
            return new PersistentPosition(reader.ReadInt32());
        }
    }

    private sealed class TextFileParticipant : IServerPersistenceParticipant
    {
        private readonly string _relativePath;
        private readonly string _content;

        public TextFileParticipant(string name, string relativePath, string content)
        {
            Name = name;
            _relativePath = relativePath;
            _content = content;
        }

        public string Name { get; }

        public ServerPersistenceArtifact Save(string saveSetRoot, ServerPersistenceMetadata metadata)
        {
            string path = Path.Combine(saveSetRoot, _relativePath);
            File.WriteAllText(path, _content);
            return new ServerPersistenceArtifact(Name, _relativePath);
        }

        public void Load(string saveSetRoot, ServerPersistenceMetadata metadata, ServerPersistenceArtifact artifact)
        {
            _ = File.ReadAllText(Path.Combine(saveSetRoot, artifact.RelativePath));
        }
    }

    private sealed class SequenceFileParticipant : IServerPersistenceParticipant
    {
        public string Name => "sequence";
        public string? LoadedSequence { get; private set; }

        public ServerPersistenceArtifact Save(string saveSetRoot, ServerPersistenceMetadata metadata)
        {
            const string relativePath = "sequence.txt";
            File.WriteAllText(Path.Combine(saveSetRoot, relativePath), metadata.SaveSequence.ToString());
            return new ServerPersistenceArtifact(Name, relativePath);
        }

        public void Load(string saveSetRoot, ServerPersistenceMetadata metadata, ServerPersistenceArtifact artifact)
        {
            LoadedSequence = File.ReadAllText(Path.Combine(saveSetRoot, artifact.RelativePath));
        }
    }

    private sealed class ThrowingParticipant : IServerPersistenceParticipant
    {
        public ThrowingParticipant(string name)
        {
            Name = name;
        }

        public string Name { get; }

        public ServerPersistenceArtifact Save(string saveSetRoot, ServerPersistenceMetadata metadata)
        {
            throw new InvalidOperationException("forced participant failure");
        }

        public void Load(string saveSetRoot, ServerPersistenceMetadata metadata, ServerPersistenceArtifact artifact)
        {
            throw new InvalidOperationException("forced participant load failure");
        }
    }

    private sealed class UnsafePathParticipant : IServerPersistenceParticipant
    {
        public string Name => "unsafe";

        public ServerPersistenceArtifact Save(string saveSetRoot, ServerPersistenceMetadata metadata)
        {
            string path = Path.Combine(saveSetRoot, "unsafe.txt");
            File.WriteAllText(path, "not promoted");
            return new ServerPersistenceArtifact(Name, "../unsafe.txt");
        }

        public void Load(string saveSetRoot, ServerPersistenceMetadata metadata, ServerPersistenceArtifact artifact)
        {
        }
    }
}

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
    }

    public static void False(bool condition)
    {
        if (condition)
            throw new InvalidOperationException("Expected condition to be false.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException($"Expected {expected.Count} values, got {actual.Count}.");

        for (int i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                throw new InvalidOperationException($"At index {i}: expected '{expected[i]}', got '{actual[i]}'.");
        }
    }

    public static void Contains(string expectedSubstring, string actual)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected text to contain '{expectedSubstring}'.");
    }
}
