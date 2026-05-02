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
        Run(nameof(ServerPersistenceCoordinator_FailureDoesNotPromotePendingSaveSet), ServerPersistenceCoordinator_FailureDoesNotPromotePendingSaveSet);
        Run(nameof(ServerPersistenceCoordinator_RotatesWholeSaveSetBackups), ServerPersistenceCoordinator_RotatesWholeSaveSetBackups);

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
                "mobiles/human/female",
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
            Assert.Equal(1, Directory.GetDirectories(backupRoot, "save-*").Length);
            Assert.Contains("3", File.ReadAllText(Path.Combine(coordinator.CurrentSaveSetRoot, "sequence.txt")));
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
    }

    private sealed class SequenceFileParticipant : IServerPersistenceParticipant
    {
        public string Name => "sequence";

        public ServerPersistenceArtifact Save(string saveSetRoot, ServerPersistenceMetadata metadata)
        {
            const string relativePath = "sequence.txt";
            File.WriteAllText(Path.Combine(saveSetRoot, relativePath), metadata.SaveSequence.ToString());
            return new ServerPersistenceArtifact(Name, relativePath);
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
