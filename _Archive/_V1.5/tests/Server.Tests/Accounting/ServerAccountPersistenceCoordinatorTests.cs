using System;
using System.IO;
using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Server;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Server.Gameplay;
using UltraSim.Shared.Gameplay;
using Xunit;

namespace Server.Tests.Accounting
{
    public sealed class ServerAccountPersistenceCoordinatorTests : IDisposable
    {
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

        public ServerAccountPersistenceCoordinatorTests() => ServerTestRuntime.ResetStatics();
        public void Dispose() => ServerTestRuntime.ResetStatics();

        private static ServerRuntime CreateRuntime(string root, ServerAccountPersistenceCoordinator coordinator)
        {
            return new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "account_persistence_test",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false,
                    SaveDirectory: root,
                    AccountAuthenticationProvider: new TestAuthProvider()),
                hooks: new ServerRuntimeHooks
                {
                    Load = runtime => coordinator.Load(runtime),
                    Save = runtime => coordinator.Save(runtime),
                });
        }

        [Fact]
        public void Coordinator_PersistsCharacterSnapshot_AcrossRuntimeRestart()
        {
            string root = Path.Combine(Path.GetTempPath(), $"accountcoord_{Guid.NewGuid():N}");
            try
            {
                ServerPersistenceRegistration.EnsureComponentsRegistered();
                var coordinator = new ServerAccountPersistenceCoordinator(serverDataRoot: root);
                ServerRuntime saveRuntime = CreateRuntime(root, coordinator);
                saveRuntime.Start();

                byte[] payload = new byte[32];
                byte[] challenge = new byte[16];
                Assert.True(saveRuntime.Accounts.TryLogin(
                    "snapshotuser",
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
                    "Dana",
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
                    sessionId: 10,
                    connectionId: 5,
                    requestedPrefabId: character.ArchetypeId,
                    playerDisplayName: character.Name,
                    characterIdentity: characterIdentity,
                    spawnSnapshot: new PlayerSpawnSnapshot(character.X, character.Y, character.Z, character.YawRadians));

                Assert.True(saveRuntime.PlayerWorld.World.TryGetComponent(created, out PlayerState state));
                state.X = 28.5f;
                state.Y = 2.25f;
                state.Z = -11.75f;
                state.YawRadians = 0.5f;
                Assert.True(saveRuntime.PlayerWorld.World.TrySetComponent(created, state));

                Assert.True(saveRuntime.Save());
                saveRuntime.Stop();

                ServerTestRuntime.ResetStatics();
                ServerPersistenceRegistration.EnsureComponentsRegistered();
                ServerRuntime loadRuntime = CreateRuntime(root, coordinator);
                loadRuntime.Start();

                Assert.True(loadRuntime.Accounts.TryLogin(
                    "snapshotuser",
                    payload,
                    payload,
                    challenge,
                    "127.0.0.1",
                    out AccountModel? loadedAccount,
                    out _,
                    out _));
                Assert.NotNull(loadedAccount);
                Assert.True(loadRuntime.Accounts.TryGetCharacter(loadedAccount!.CharacterSlots[0], out CharacterModel loadedCharacter));
                Assert.Equal(PlayerArchetypeIds.DefaultFemale, loadedCharacter.ArchetypeId);
                Assert.Equal(28.5f, loadedCharacter.X);
                Assert.Equal(2.25f, loadedCharacter.Y);
                Assert.Equal(-11.75f, loadedCharacter.Z);
                Assert.Equal(0.5f, loadedCharacter.YawRadians);

                Entity respawned = loadRuntime.PlayerWorld.BindSession(
                    sessionId: 11,
                    connectionId: 6,
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

                Assert.True(loadRuntime.PlayerWorld.World.TryGetComponent(respawned, out PlayerState respawnedState));
                Assert.Equal(28.5f, respawnedState.X);
                Assert.Equal(2.25f, respawnedState.Y);
                Assert.Equal(-11.75f, respawnedState.Z);
                Assert.Equal(0.5f, respawnedState.YawRadians);
                loadRuntime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Save_CompactsOfflinePersistentPlayer_And_RestoresFromCharacterSnapshot()
        {
            string root = Path.Combine(Path.GetTempPath(), $"accountcoord_compact_{Guid.NewGuid():N}");
            try
            {
                ServerPersistenceRegistration.EnsureComponentsRegistered();
                var coordinator = new ServerAccountPersistenceCoordinator(serverDataRoot: root);
                ServerRuntime saveRuntime = CreateRuntime(root, coordinator);
                saveRuntime.Start();

                byte[] payload = new byte[32];
                byte[] challenge = new byte[16];
                Assert.True(saveRuntime.Accounts.TryLogin(
                    "compactuser",
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
                    "Erin",
                    PlayerArchetypeIds.Default,
                    out _,
                    out CharacterModel? character,
                    out string errorMessage));
                Assert.True(string.IsNullOrEmpty(errorMessage));
                Assert.NotNull(character);

                var identity = new PlayerCharacterComponent
                {
                    CharacterId = character!.CharacterId,
                    SlotIndex = character.SlotIndex,
                };
                Entity created = saveRuntime.PlayerWorld.BindSession(
                    sessionId: 20,
                    connectionId: 7,
                    requestedPrefabId: character.ArchetypeId,
                    playerDisplayName: character.Name,
                    characterIdentity: identity,
                    spawnSnapshot: new PlayerSpawnSnapshot(character.X, character.Y, character.Z, character.YawRadians));

                Assert.True(saveRuntime.PlayerWorld.World.TryGetComponent(created, out PlayerState state));
                state.X = 90.0f;
                state.Y = 4.0f;
                state.Z = -12.0f;
                state.YawRadians = 2.0f;
                Assert.True(saveRuntime.PlayerWorld.World.TrySetComponent(created, state));

                Assert.True(saveRuntime.PlayerWorld.UnbindSession(20));
                int parkedCount = 0;
                saveRuntime.PlayerWorld.World.Query<PersistentTag>((Entity _, ref PersistentTag __) => parkedCount++);
                Assert.Equal(1, parkedCount);

                Assert.True(saveRuntime.Save());
                int compactedCount = 0;
                saveRuntime.PlayerWorld.World.Query<PersistentTag>((Entity _, ref PersistentTag __) => compactedCount++);
                Assert.Equal(0, compactedCount);
                saveRuntime.Stop();

                ServerTestRuntime.ResetStatics();
                ServerPersistenceRegistration.EnsureComponentsRegistered();
                ServerRuntime loadRuntime = CreateRuntime(root, coordinator);
                loadRuntime.Start();

                Assert.True(loadRuntime.Accounts.TryLogin(
                    "compactuser",
                    payload,
                    payload,
                    challenge,
                    "127.0.0.1",
                    out AccountModel? loadedAccount,
                    out _,
                    out _));
                Assert.NotNull(loadedAccount);
                Assert.True(loadRuntime.Accounts.TryGetCharacter(loadedAccount!.CharacterSlots[0], out CharacterModel loadedCharacter));

                int loadedPersistentCount = 0;
                loadRuntime.PlayerWorld.World.Query<PersistentTag>((Entity _, ref PersistentTag __) => loadedPersistentCount++);
                Assert.Equal(0, loadedPersistentCount);

                Entity respawned = loadRuntime.PlayerWorld.BindSession(
                    sessionId: 21,
                    connectionId: 8,
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

                Assert.True(loadRuntime.PlayerWorld.World.TryGetComponent(respawned, out PlayerState respawnedState));
                Assert.Equal(90.0f, respawnedState.X);
                Assert.Equal(4.0f, respawnedState.Y);
                Assert.Equal(-12.0f, respawnedState.Z);
                Assert.Equal(2.0f, respawnedState.YawRadians);
                loadRuntime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Save_CompactsOfflinePersistentPlayer_And_RebindsFromUpdatedCharacterSnapshot_WithoutRestart()
        {
            string root = Path.Combine(Path.GetTempPath(), $"accountcoord_compact_rebind_{Guid.NewGuid():N}");
            try
            {
                ServerPersistenceRegistration.EnsureComponentsRegistered();
                var coordinator = new ServerAccountPersistenceCoordinator(serverDataRoot: root);
                ServerRuntime runtime = CreateRuntime(root, coordinator);
                runtime.Start();

                byte[] payload = new byte[32];
                byte[] challenge = new byte[16];
                Assert.True(runtime.Accounts.TryLogin(
                    "rebinduser",
                    payload,
                    payload,
                    challenge,
                    "127.0.0.1",
                    out AccountModel? account,
                    out _,
                    out _));
                Assert.NotNull(account);
                Assert.True(runtime.Accounts.TryCreateCharacter(
                    account!,
                    "Lena",
                    PlayerArchetypeIds.DefaultFemale,
                    out _,
                    out CharacterModel? character,
                    out string errorMessage));
                Assert.True(string.IsNullOrEmpty(errorMessage));
                Assert.NotNull(character);

                var identity = new PlayerCharacterComponent
                {
                    CharacterId = character!.CharacterId,
                    SlotIndex = character.SlotIndex,
                };

                Entity created = runtime.PlayerWorld.BindSession(
                    sessionId: 30,
                    connectionId: 9,
                    requestedPrefabId: character.ArchetypeId,
                    playerDisplayName: character.Name,
                    characterIdentity: identity,
                    spawnSnapshot: new PlayerSpawnSnapshot(character.X, character.Y, character.Z, character.YawRadians));

                Assert.True(runtime.PlayerWorld.World.TryGetComponent(created, out PlayerState state));
                state.X = 44.5f;
                state.Y = 3.5f;
                state.Z = -19.25f;
                state.YawRadians = 1.75f;
                Assert.True(runtime.PlayerWorld.World.TrySetComponent(created, state));

                Assert.True(runtime.PlayerWorld.UnbindSession(30));
                Assert.True(runtime.Save());

                int persistentCount = 0;
                runtime.PlayerWorld.World.Query<PersistentTag>((Entity _, ref PersistentTag __) => persistentCount++);
                Assert.Equal(0, persistentCount);

                Assert.True(runtime.Accounts.TryGetCharacter(character.CharacterId, out CharacterModel updatedCharacter));
                Assert.Equal(44.5f, updatedCharacter.X);
                Assert.Equal(3.5f, updatedCharacter.Y);
                Assert.Equal(-19.25f, updatedCharacter.Z);
                Assert.Equal(1.75f, updatedCharacter.YawRadians);

                Entity rebound = runtime.PlayerWorld.BindSession(
                    sessionId: 31,
                    connectionId: 10,
                    requestedPrefabId: updatedCharacter.ArchetypeId,
                    playerDisplayName: updatedCharacter.Name,
                    characterIdentity: new PlayerCharacterComponent
                    {
                        CharacterId = updatedCharacter.CharacterId,
                        SlotIndex = updatedCharacter.SlotIndex,
                    },
                    spawnSnapshot: new PlayerSpawnSnapshot(
                        updatedCharacter.X,
                        updatedCharacter.Y,
                        updatedCharacter.Z,
                        updatedCharacter.YawRadians));

                Assert.True(runtime.PlayerWorld.World.TryGetComponent(rebound, out PlayerState reboundState));
                Assert.Equal(44.5f, reboundState.X);
                Assert.Equal(3.5f, reboundState.Y);
                Assert.Equal(-19.25f, reboundState.Z);
                Assert.Equal(1.75f, reboundState.YawRadians);

                runtime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }
    }
}
