using System;
using System.Diagnostics;
using System.IO;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using UltraSim.Network;
using UltraSim.Server;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Server.Gameplay;
using UltraSim.Shared.Gameplay;
using Xunit;
using Xunit.Abstractions;

namespace Server.Tests.Persistence
{
    public sealed class RuntimePersistenceIntegrationTests : IDisposable
    {
        private readonly ITestOutputHelper _output;

        private sealed class FakeTransport : INetworkTransport
        {
            public void Start() { }
            public void Stop() { }
            public int PollInbound(Span<InboundDatagram> destination) => 0;
            public void EnqueueOutbound(in OutboundDatagram datagram) { }
            public void FlushOutbound() { }
            public void CompleteInboundConsume() { }
            public void Dispose() { }
        }

        private sealed class TestAuthProvider : IAccountAuthenticationProvider
        {
            public bool TryAuthenticateExistingAccount(
                AccountModel account,
                ReadOnlySpan<byte> passwordHash32,
                ReadOnlySpan<byte> challengeResponse32,
                ReadOnlySpan<byte> challenge16,
                out string errorMessage)
            {
                errorMessage = string.Empty;
                return true;
            }

            public bool TryPrepareNewAccount(
                string username,
                ReadOnlySpan<byte> passwordHash32,
                ReadOnlySpan<byte> challengeResponse32,
                ReadOnlySpan<byte> challenge16,
                out string passwordHash,
                out string passwordSalt,
                out string errorMessage)
            {
                passwordHash = "TEST_HASH";
                passwordSalt = "TEST_SALT";
                errorMessage = string.Empty;
                return true;
            }
        }

        public RuntimePersistenceIntegrationTests(ITestOutputHelper output)
        {
            _output = output;
            ServerTestRuntime.ResetStatics();
        }

        public void Dispose() => ServerTestRuntime.ResetStatics();

        private static ServerRuntime CreateRuntime(string root)
        {
            string saveRoot = Path.Combine(root, "Saves");
            var accountCoordinator = new ServerAccountPersistenceCoordinator(serverDataRoot: root);
            var worldCoordinator = new WorldPersistenceCoordinator(
                new WorldPersistenceOptions(
                    SaveDirectory: saveRoot,
                    RecoveryMode: WorldPersistenceRecoveryMode.Abort));
            var persistenceCoordinator = new ServerPersistenceCoordinator(accountCoordinator, worldCoordinator);
            ServerPersistenceRegistration.RegisterPersistentTypes(worldCoordinator);

            return new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "runtime_persistence_integration",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false,
                    SaveDirectory: saveRoot,
                    AccountAuthenticationProvider: new TestAuthProvider()),
                hooks: new ServerRuntimeHooks
                {
                    Load = runtime => persistenceCoordinator.Load(runtime),
                    CaptureSaveSnapshot = runtime => persistenceCoordinator.CaptureSnapshot(runtime),
                    WriteSaveSnapshot = snapshot => persistenceCoordinator.WriteSnapshot((ServerPersistenceSaveSnapshot)snapshot),
                });
        }

        [Fact]
        public void Runtime_SaveAndRestartLoad_RoundTripsFullPersistenceStack()
        {
            string root = Path.Combine(Path.GetTempPath(), $"runtime_persist_e2e_{Guid.NewGuid():N}");
            string saveRoot = Path.Combine(root, "Saves");
            string accountsRoot = Path.Combine(root, "accounts");

            try
            {
                ServerPersistenceRegistration.EnsureComponentsRegistered();

                ServerRuntime saveRuntime = CreateRuntime(root);
                saveRuntime.Start();

                byte[] payload = new byte[32];
                byte[] challenge = new byte[16];
                Assert.True(saveRuntime.Accounts.TryLogin(
                    "e2euser",
                    payload,
                    payload,
                    challenge,
                    "127.0.0.1",
                    out AccountModel? account,
                    out _,
                    out _));
                Assert.NotNull(account);

                Assert.True(saveRuntime.Accounts.TryCreateCharacter(
                    account!,
                    "Ava",
                    PlayerArchetypeIds.DefaultFemale,
                    out _,
                    out CharacterModel? character,
                    out string errorMessage));
                Assert.True(string.IsNullOrEmpty(errorMessage));
                Assert.NotNull(character);

                var characterIdentity = new PlayerCharacterComponent
                {
                    CharacterId = character!.CharacterId,
                    SlotIndex = character.SlotIndex,
                };

                Entity created = saveRuntime.PlayerWorld.BindSession(
                    sessionId: 100,
                    connectionId: 10,
                    requestedPrefabId: character.ArchetypeId,
                    playerDisplayName: character.Name,
                    characterIdentity: characterIdentity,
                    spawnSnapshot: new PlayerSpawnSnapshot(character.X, character.Y, character.Z, character.YawRadians));

                Assert.True(saveRuntime.PlayerWorld.World.TryGetComponent(created, out PlayerState createdState));
                createdState.X = 14.25f;
                createdState.Y = 2.0f;
                createdState.Z = -8.5f;
                createdState.YawRadians = 1.125f;
                Assert.True(saveRuntime.PlayerWorld.World.TrySetComponent(created, createdState));

                var saveTimer = Stopwatch.StartNew();
                Assert.True(saveRuntime.Save());
                saveTimer.Stop();

                string metaPath = Path.Combine(saveRoot, "meta.bin");
                string typesPath = Path.Combine(saveRoot, "types.tdb");
                string entitiesIdxPath = Path.Combine(saveRoot, "Entities", "entities.idx");
                string entitiesBinPath = Path.Combine(saveRoot, "Entities", "entities.bin");
                string accountsPath = Path.Combine(accountsRoot, "accounts.dat");
                string keyPath = Path.Combine(accountsRoot, "server.key");

                Assert.True(File.Exists(metaPath));
                Assert.True(File.Exists(typesPath));
                Assert.True(File.Exists(entitiesIdxPath));
                Assert.True(File.Exists(entitiesBinPath));
                Assert.True(File.Exists(accountsPath));
                Assert.True(File.Exists(keyPath));

                _output.WriteLine($"Save elapsed: {saveTimer.Elapsed.TotalMilliseconds:F3} ms");
                _output.WriteLine($"meta.bin: {new FileInfo(metaPath).Length} bytes");
                _output.WriteLine($"types.tdb: {new FileInfo(typesPath).Length} bytes");
                _output.WriteLine($"entities.idx: {new FileInfo(entitiesIdxPath).Length} bytes");
                _output.WriteLine($"entities.bin: {new FileInfo(entitiesBinPath).Length} bytes");
                _output.WriteLine($"accounts.dat: {new FileInfo(accountsPath).Length} bytes");
                _output.WriteLine($"server.key: {new FileInfo(keyPath).Length} bytes");

                saveRuntime.Stop();

                ServerTestRuntime.ResetStatics();
                ServerPersistenceRegistration.EnsureComponentsRegistered();

                ServerRuntime loadRuntime = CreateRuntime(root);
                var loadTimer = Stopwatch.StartNew();
                loadRuntime.Start();
                loadTimer.Stop();

                _output.WriteLine($"Load elapsed: {loadTimer.Elapsed.TotalMilliseconds:F3} ms");

                Assert.True(loadRuntime.Accounts.TryLogin(
                    "e2euser",
                    payload,
                    payload,
                    challenge,
                    "127.0.0.1",
                    out AccountModel? loadedAccount,
                    out _,
                    out _));
                Assert.NotNull(loadedAccount);
                Assert.True(loadRuntime.Accounts.TryGetCharacter(loadedAccount!.CharacterSlots[0], out CharacterModel loadedCharacter));

                Entity loadedEntity = default;
                string loadedName = string.Empty;
                int loadedMatchCount = 0;
                loadRuntime.PlayerWorld.World.Query<PersistentTag, PlayerNameComponent, PlayerCharacterComponent>(
                    (Entity entity, ref PersistentTag _, ref PlayerNameComponent name, ref PlayerCharacterComponent playerCharacter) =>
                    {
                        if (playerCharacter.CharacterId != loadedCharacter.CharacterId)
                            return;

                        loadedEntity = entity;
                        loadedName = name.GetName();
                        loadedMatchCount++;
                    });
                Assert.Equal(1, loadedMatchCount);

                Assert.True(loadRuntime.PlayerWorld.World.TryGetComponent(loadedEntity, out PrefabIdComponent loadedPrefab));
                Assert.True(loadRuntime.PlayerWorld.World.TryGetComponent(loadedEntity, out PlayerState loadedState));

                Assert.Equal("Ava", loadedName);
                Assert.Equal(PlayerArchetypeIds.DefaultFemale, loadedPrefab.PrefabId);
                Assert.Equal(0, loadedState.OwnerConnectionId);
                Assert.Equal(14.25f, loadedState.X);
                Assert.Equal(2.0f, loadedState.Y);
                Assert.Equal(-8.5f, loadedState.Z);
                Assert.Equal(1.125f, loadedState.YawRadians);

                Entity rebound = loadRuntime.PlayerWorld.BindSession(
                    sessionId: 101,
                    connectionId: 11,
                    requestedPrefabId: loadedCharacter.ArchetypeId,
                    playerDisplayName: loadedCharacter.Name,
                    characterIdentity: new PlayerCharacterComponent
                    {
                        CharacterId = loadedCharacter.CharacterId,
                        SlotIndex = loadedCharacter.SlotIndex,
                    },
                    spawnSnapshot: new PlayerSpawnSnapshot(
                        loadedCharacter.X,
                        loadedCharacter.Y,
                        loadedCharacter.Z,
                        loadedCharacter.YawRadians));

                Assert.Equal(loadedEntity.Packed, rebound.Packed);
                Assert.True(loadRuntime.PlayerWorld.World.TryGetComponent(rebound, out PlayerState reboundState));
                Assert.Equal(11, reboundState.OwnerConnectionId);
                Assert.Equal(14.25f, reboundState.X);
                Assert.Equal(2.0f, reboundState.Y);
                Assert.Equal(-8.5f, reboundState.Z);
                Assert.Equal(1.125f, reboundState.YawRadians);

                loadRuntime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Runtime_RequestSave_And_Stop_PersistsThroughAsyncWorldSnapshotWrite()
        {
            string root = Path.Combine(Path.GetTempPath(), $"runtime_async_persist_{Guid.NewGuid():N}");
            try
            {
                ServerPersistenceRegistration.EnsureComponentsRegistered();

                ServerRuntime saveRuntime = CreateRuntime(root);
                saveRuntime.Start();

                byte[] payload = new byte[32];
                byte[] challenge = new byte[16];
                Assert.True(saveRuntime.Accounts.TryLogin(
                    "asyncuser",
                    payload,
                    payload,
                    challenge,
                    "127.0.0.1",
                    out AccountModel? account,
                    out _,
                    out _));
                Assert.NotNull(account);

                Assert.True(saveRuntime.Accounts.TryCreateCharacter(
                    account!,
                    "Mira",
                    PlayerArchetypeIds.Default,
                    out _,
                    out CharacterModel? character,
                    out string errorMessage));
                Assert.True(string.IsNullOrEmpty(errorMessage));
                Assert.NotNull(character);

                Entity created = saveRuntime.PlayerWorld.BindSession(
                    sessionId: 200,
                    connectionId: 20,
                    requestedPrefabId: character!.ArchetypeId,
                    playerDisplayName: character.Name,
                    characterIdentity: new PlayerCharacterComponent
                    {
                        CharacterId = character.CharacterId,
                        SlotIndex = character.SlotIndex,
                    },
                    spawnSnapshot: new PlayerSpawnSnapshot(character.X, character.Y, character.Z, character.YawRadians));

                Assert.True(saveRuntime.PlayerWorld.World.TryGetComponent(created, out PlayerState createdState));
                createdState.X = 7.5f;
                createdState.Z = -11.25f;
                Assert.True(saveRuntime.PlayerWorld.World.TrySetComponent(created, createdState));

                saveRuntime.RequestSave();
                saveRuntime.Tick(0.016f);
                saveRuntime.Stop();

                ServerTestRuntime.ResetStatics();
                ServerPersistenceRegistration.EnsureComponentsRegistered();

                ServerRuntime loadRuntime = CreateRuntime(root);
                loadRuntime.Start();

                Assert.True(loadRuntime.Accounts.TryLogin(
                    "asyncuser",
                    payload,
                    payload,
                    challenge,
                    "127.0.0.1",
                    out AccountModel? loadedAccount,
                    out _,
                    out _));
                Assert.NotNull(loadedAccount);
                Assert.True(loadRuntime.Accounts.TryGetCharacter(loadedAccount!.CharacterSlots[0], out CharacterModel loadedCharacter));

                Entity loadedEntity = default;
                int loadedMatches = 0;
                loadRuntime.PlayerWorld.World.Query<PersistentTag, PlayerCharacterComponent>(
                    (Entity entity, ref PersistentTag _, ref PlayerCharacterComponent playerCharacter) =>
                    {
                        if (playerCharacter.CharacterId != loadedCharacter.CharacterId)
                            return;

                        loadedEntity = entity;
                        loadedMatches++;
                    });

                Assert.Equal(1, loadedMatches);
                Assert.True(loadRuntime.PlayerWorld.World.TryGetComponent(loadedEntity, out PlayerState loadedState));
                Assert.Equal(7.5f, loadedState.X);
                Assert.Equal(-11.25f, loadedState.Z);

                loadRuntime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
