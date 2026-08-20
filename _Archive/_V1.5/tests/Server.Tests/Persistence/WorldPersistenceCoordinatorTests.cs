using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UltraSim.ECS;
using UltraSim.ECS.Persistence;
using UltraSim.Network;
using UltraSim.Server;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Server.Gameplay;
using UltraSim.Shared.Gameplay;
using Xunit;

namespace Server.Tests.Persistence
{
    public sealed class WorldPersistenceCoordinatorTests : IDisposable
    {
        [PersistenceId(39991u)]
        private struct UnregisteredPersistentTestComponent : ISerializableComponent<UnregisteredPersistentTestComponent>
        {
            public int Value;

            public static void Serialize(ref UltraSim.ECS.BitWriter writer, in UnregisteredPersistentTestComponent value)
                => writer.WriteInt32(value.Value);

            public static UnregisteredPersistentTestComponent Deserialize(ref UltraSim.ECS.BitReader reader, ushort version)
                => new() { Value = reader.ReadInt32() };
        }

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

        public WorldPersistenceCoordinatorTests() => ServerTestRuntime.ResetStatics();
        public void Dispose() => ServerTestRuntime.ResetStatics();

        private static WorldPersistenceCoordinator CreateCoordinator(
            string root,
            WorldPersistenceRecoveryMode recoveryMode = WorldPersistenceRecoveryMode.Abort,
            IWorldPersistenceRecoveryInteraction? recoveryInteraction = null,
            WorldSaveParallelMode parallelMode = WorldSaveParallelMode.Serial)
        {
            var coordinator = new WorldPersistenceCoordinator(
                new WorldPersistenceOptions(
                    SaveDirectory: root,
                    RecoveryMode: recoveryMode,
                    RecoveryInteraction: recoveryInteraction,
                    ParallelMode: parallelMode));
            ServerPersistenceRegistration.RegisterPersistentTypes(coordinator);
            return coordinator;
        }

        private static ServerRuntime CreateRuntime(string root, WorldPersistenceCoordinator coordinator)
        {
            return new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "test_runtime",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false,
                    SaveDirectory: root),
                hooks: new ServerRuntimeHooks
                {
                    Load = runtime => coordinator.Load(runtime),
                    CaptureSaveSnapshot = runtime => coordinator.CaptureSnapshot(runtime),
                    WriteSaveSnapshot = snapshot => coordinator.WriteSnapshot((WorldPersistenceSaveSnapshot)snapshot),
                });
        }

        private static ServerRuntime CreateRuntime(
            string root,
            WorldPersistenceCoordinator coordinator,
            IAccountAuthenticationProvider authProvider)
        {
            return new ServerRuntime(
                new FakeTransport(),
                new ServerRuntimeOptions(
                    "test_runtime",
                    StartTransportOnStart: false,
                    StopTransportOnStop: false,
                    SaveOnStop: false,
                    SaveDirectory: root,
                    AccountAuthenticationProvider: authProvider),
                hooks: new ServerRuntimeHooks
                {
                    Load = runtime => coordinator.Load(runtime),
                    CaptureSaveSnapshot = runtime => coordinator.CaptureSnapshot(runtime),
                    WriteSaveSnapshot = snapshot => coordinator.WriteSnapshot((WorldPersistenceSaveSnapshot)snapshot),
                });
        }

        private static void RegisterComponents()
        {
            ServerPersistenceRegistration.EnsureComponentsRegistered();
        }

        private static ArchetypeSignature Sig(params ComponentId[] ids) => new ArchetypeSignature(ids);

        private sealed class FakeRecoveryInteraction : IWorldPersistenceRecoveryInteraction
        {
            public WorldPersistenceRecoveryMode? Response { get; set; }
            public WorldPersistenceRecoveryRequest? LastRequest { get; private set; }

            public WorldPersistenceRecoveryMode? ResolveMissingOptionalSections(WorldPersistenceRecoveryRequest request)
            {
                LastRequest = request;
                return Response;
            }
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

        private static CharacterModel CreateCharacter(ServerRuntime runtime, string username, string name, ushort prefabId)
        {
            byte[] payload = new byte[32];
            byte[] challenge = new byte[16];
            Assert.True(runtime.Accounts.TryLogin(
                username,
                payload,
                payload,
                challenge,
                "127.0.0.1",
                out AccountModel? account,
                out _,
                out _));
            Assert.NotNull(account);
            Assert.True(runtime.Accounts.TryCreateCharacter(account!, name, prefabId, out _, out CharacterModel? character, out string errorMessage));
            Assert.True(string.IsNullOrEmpty(errorMessage));
            Assert.NotNull(character);
            return character!;
        }

        [Fact]
        public void Coordinator_RoundTripsPersistentEntities_ViaRuntimeHooks()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_{Guid.NewGuid():N}");
            try
            {
                RegisterComponents();
                WorldPersistenceCoordinator saveCoordinator = CreateCoordinator(root);
                ServerRuntime saveRuntime = CreateRuntime(root, saveCoordinator);
                saveRuntime.Start();

                ComponentId tagId = ComponentManager.GetId<PersistentTag>();
                ComponentId nameId = ComponentManager.GetId<PlayerNameComponent>();
                ComponentId characterId = ComponentManager.GetId<PlayerCharacterComponent>();
                saveRuntime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, nameId, characterId));
                saveRuntime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, nameId, characterId));
                saveRuntime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

                int index = 0;
                saveRuntime.PlayerWorld.World.Query<PersistentTag, PlayerNameComponent, PlayerCharacterComponent>((Entity entity, ref PersistentTag tag, ref PlayerNameComponent name, ref PlayerCharacterComponent character) =>
                {
                    if (index == 0)
                    {
                        name = PlayerNameComponent.FromString("Alice");
                        character = new PlayerCharacterComponent { CharacterId = 1001, SlotIndex = 1 };
                    }
                    else
                    {
                        name = PlayerNameComponent.FromString("Bob");
                        character = new PlayerCharacterComponent { CharacterId = 2002, SlotIndex = 2 };
                    }

                    index++;
                });

                Assert.True(saveRuntime.Save());
                saveRuntime.Stop();

                ServerTestRuntime.ResetStatics();
                RegisterComponents();
                WorldPersistenceCoordinator loadCoordinator = CreateCoordinator(root);
                ServerRuntime loadRuntime = CreateRuntime(root, loadCoordinator);
                loadRuntime.Start();

                var loadedCharacters = new List<(string Name, ulong CharacterId, byte SlotIndex)>();
                loadRuntime.PlayerWorld.World.Query<PersistentTag, PlayerNameComponent, PlayerCharacterComponent>((Entity entity, ref PersistentTag tag, ref PlayerNameComponent name, ref PlayerCharacterComponent character) =>
                {
                    loadedCharacters.Add((name.GetName(), character.CharacterId, character.SlotIndex));
                });

                Assert.Equal(
                    [("Alice", 1001UL, (byte)1), ("Bob", 2002UL, (byte)2)],
                    loadedCharacters.OrderBy(static v => v.CharacterId).ToArray());
                loadRuntime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Coordinator_CaptureSnapshot_ReusesReleasedBuffer()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_pool_{Guid.NewGuid():N}");
            try
            {
                RegisterComponents();
                WorldPersistenceCoordinator coordinator = CreateCoordinator(root);
                ServerRuntime runtime = CreateRuntime(root, coordinator);
                runtime.Start();

                ComponentId tagId = ComponentManager.GetId<PersistentTag>();
                runtime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId));
                runtime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

                WorldPersistenceSaveSnapshot first = coordinator.CaptureSnapshot(runtime);
                coordinator.WriteSnapshot(first);

                WorldPersistenceSaveSnapshot second = coordinator.CaptureSnapshot(runtime);
                Assert.Same(first, second);
                coordinator.WriteSnapshot(second);

                runtime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Coordinator_SystemsParallel_RoundTripsPersistentEntities_ViaRuntimeHooks()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_parallel_{Guid.NewGuid():N}");
            try
            {
                RegisterComponents();
                WorldPersistenceCoordinator saveCoordinator = CreateCoordinator(root, parallelMode: WorldSaveParallelMode.SystemsParallel);
                ServerRuntime saveRuntime = CreateRuntime(root, saveCoordinator);
                saveRuntime.Start();

                ComponentId tagId = ComponentManager.GetId<PersistentTag>();
                ComponentId nameId = ComponentManager.GetId<PlayerNameComponent>();
                ComponentId characterId = ComponentManager.GetId<PlayerCharacterComponent>();
                saveRuntime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, nameId, characterId));
                saveRuntime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, nameId, characterId));
                saveRuntime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

                int index = 0;
                saveRuntime.PlayerWorld.World.Query<PersistentTag, PlayerNameComponent, PlayerCharacterComponent>((Entity entity, ref PersistentTag tag, ref PlayerNameComponent name, ref PlayerCharacterComponent character) =>
                {
                    if (index == 0)
                    {
                        name = PlayerNameComponent.FromString("Alice");
                        character = new PlayerCharacterComponent { CharacterId = 1001, SlotIndex = 1 };
                    }
                    else
                    {
                        name = PlayerNameComponent.FromString("Bob");
                        character = new PlayerCharacterComponent { CharacterId = 2002, SlotIndex = 2 };
                    }

                    index++;
                });

                Assert.True(saveRuntime.Save());
                saveRuntime.Stop();

                ServerTestRuntime.ResetStatics();
                RegisterComponents();
                WorldPersistenceCoordinator loadCoordinator = CreateCoordinator(root, parallelMode: WorldSaveParallelMode.SystemsParallel);
                ServerRuntime loadRuntime = CreateRuntime(root, loadCoordinator);
                loadRuntime.Start();

                var loadedCharacters = new List<(string Name, ulong CharacterId, byte SlotIndex)>();
                loadRuntime.PlayerWorld.World.Query<PersistentTag, PlayerNameComponent, PlayerCharacterComponent>((Entity entity, ref PersistentTag tag, ref PlayerNameComponent name, ref PlayerCharacterComponent character) =>
                {
                    loadedCharacters.Add((name.GetName(), character.CharacterId, character.SlotIndex));
                });

                Assert.Equal(
                    [("Alice", 1001UL, (byte)1), ("Bob", 2002UL, (byte)2)],
                    loadedCharacters.OrderBy(static v => v.CharacterId).ToArray());
                loadRuntime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Coordinator_ContinueWithoutEntities_AllowsMissingEntityFiles()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_missing_{Guid.NewGuid():N}");
            try
            {
                RegisterComponents();
                WorldPersistenceCoordinator saveCoordinator = CreateCoordinator(root);
                ServerRuntime saveRuntime = CreateRuntime(root, saveCoordinator);
                saveRuntime.Start();

                ComponentId tagId = ComponentManager.GetId<PersistentTag>();
                ComponentId nameId = ComponentManager.GetId<PlayerNameComponent>();
                saveRuntime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, nameId));
                saveRuntime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
                Assert.True(saveRuntime.Save());
                saveRuntime.Stop();

                File.Delete(Path.Combine(root, "Entities", "entities.idx"));
                File.Delete(Path.Combine(root, "Entities", "entities.bin"));

                ServerTestRuntime.ResetStatics();
                RegisterComponents();
                WorldPersistenceCoordinator loadCoordinator = CreateCoordinator(root, WorldPersistenceRecoveryMode.ContinueWithoutEntities);
                ServerRuntime loadRuntime = CreateRuntime(root, loadCoordinator);
                loadRuntime.Start();

                int persistentCount = 0;
                loadRuntime.PlayerWorld.World.Query<PersistentTag>((Entity entity, ref PersistentTag tag) => persistentCount++);
                Assert.Equal(0, persistentCount);
                loadRuntime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Coordinator_RoundTripsPersistentPlayerEntity_And_RebindsByCharacterId()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_player_{Guid.NewGuid():N}");
            try
            {
                RegisterComponents();
                WorldPersistenceCoordinator saveCoordinator = CreateCoordinator(root);
                ServerRuntime saveRuntime = CreateRuntime(root, saveCoordinator);
                saveRuntime.Start();

                var characterIdentity = new PlayerCharacterComponent
                {
                    CharacterId = 3003,
                    SlotIndex = 4,
                };

                Entity created = saveRuntime.PlayerWorld.BindSession(
                    sessionId: 100,
                    connectionId: 10,
                    requestedPrefabId: PlayerArchetypeIds.Default,
                    playerDisplayName: "Alice",
                    characterIdentity: characterIdentity);

                Assert.True(saveRuntime.PlayerWorld.World.TryGetComponent(created, out PlayerState createdState));
                createdState.X = 12.5f;
                createdState.Y = 1.25f;
                createdState.Z = -9.75f;
                createdState.YawRadians = 0.75f;
                createdState.MoveX = 7;
                createdState.MoveZ = -3;
                Assert.True(saveRuntime.PlayerWorld.World.TrySetComponent(created, createdState));

                Assert.True(saveRuntime.Save());
                saveRuntime.Stop();

                ServerTestRuntime.ResetStatics();
                RegisterComponents();
                WorldPersistenceCoordinator loadCoordinator = CreateCoordinator(root);
                ServerRuntime loadRuntime = CreateRuntime(root, loadCoordinator);
                loadRuntime.Start();

                var loaded = new List<(Entity Entity, string Name, ulong CharacterId, byte SlotIndex, ushort ArchetypeId, int OwnerConnectionId, float X, float Y, float Z, float YawRadians, short MoveX, short MoveZ)>();
                loadRuntime.PlayerWorld.World.Query<PersistentTag, PlayerNameComponent, PlayerCharacterComponent>(
                    (Entity entity, ref PersistentTag tag, ref PlayerNameComponent name, ref PlayerCharacterComponent character) =>
                    {
                        Assert.True(loadRuntime.PlayerWorld.World.TryGetComponent(entity, out PrefabIdComponent prefab));
                        Assert.True(loadRuntime.PlayerWorld.World.TryGetComponent(entity, out PlayerState state));
                        loaded.Add((entity, name.GetName(), character.CharacterId, character.SlotIndex, prefab.PrefabId, state.OwnerConnectionId, state.X, state.Y, state.Z, state.YawRadians, state.MoveX, state.MoveZ));
                    });

                Assert.Single(loaded);
                var loadedPlayer = loaded[0];
                Assert.Equal("Alice", loadedPlayer.Name);
                Assert.Equal(3003UL, loadedPlayer.CharacterId);
                Assert.Equal((byte)4, loadedPlayer.SlotIndex);
                Assert.Equal(PlayerArchetypeIds.Default, loadedPlayer.ArchetypeId);
                Assert.Equal(0, loadedPlayer.OwnerConnectionId);
                Assert.Equal(12.5f, loadedPlayer.X);
                Assert.Equal(1.25f, loadedPlayer.Y);
                Assert.Equal(-9.75f, loadedPlayer.Z);
                Assert.Equal(0.75f, loadedPlayer.YawRadians);
                Assert.Equal((short)0, loadedPlayer.MoveX);
                Assert.Equal((short)0, loadedPlayer.MoveZ);

                Entity rebound = loadRuntime.PlayerWorld.BindSession(
                    sessionId: 200,
                    connectionId: 77,
                    requestedPrefabId: PlayerArchetypeIds.Default,
                    playerDisplayName: "Alice",
                    characterIdentity: characterIdentity);

                Assert.Equal(loadedPlayer.Entity.Packed, rebound.Packed);

                int persistentCount = 0;
                loadRuntime.PlayerWorld.World.Query<PersistentTag>((Entity entity, ref PersistentTag tag) => persistentCount++);
                Assert.Equal(1, persistentCount);
                Assert.True(loadRuntime.PlayerWorld.World.TryGetComponent(rebound, out PlayerState reboundState));
                Assert.Equal(77, reboundState.OwnerConnectionId);
                Assert.Equal(12.5f, reboundState.X);
                Assert.Equal(-9.75f, reboundState.Z);
                loadRuntime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void PersistentPlayerEntity_SurvivesUnbind_And_RebindsWithoutSave()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_live_{Guid.NewGuid():N}");
            RegisterComponents();
            WorldPersistenceCoordinator coordinator = CreateCoordinator(root);
            ServerRuntime runtime = CreateRuntime(root, coordinator);
            try
            {
                runtime.Start();

                var characterIdentity = new PlayerCharacterComponent
                {
                    CharacterId = 4004,
                    SlotIndex = 1,
                };

                Entity created = runtime.PlayerWorld.BindSession(
                    sessionId: 10,
                    connectionId: 21,
                    requestedPrefabId: PlayerArchetypeIds.Default,
                    playerDisplayName: "Carol",
                    characterIdentity: characterIdentity);

                Assert.True(runtime.PlayerWorld.World.TryGetComponent(created, out PlayerState state));
                state.X = 33.5f;
                state.Z = -4.5f;
                state.MoveX = 2;
                state.MoveZ = -2;
                Assert.True(runtime.PlayerWorld.World.TrySetComponent(created, state));

                Assert.True(runtime.PlayerWorld.UnbindSession(10));

                int persistentCount = 0;
                runtime.PlayerWorld.World.Query<PersistentTag>((Entity entity, ref PersistentTag tag) => persistentCount++);
                Assert.Equal(1, persistentCount);
                Assert.True(runtime.PlayerWorld.World.TryGetComponent(created, out PlayerState parkedState));
                Assert.Equal(0, parkedState.OwnerConnectionId);
                Assert.Equal((short)0, parkedState.MoveX);
                Assert.Equal((short)0, parkedState.MoveZ);
                Assert.Equal(33.5f, parkedState.X);
                Assert.Equal(-4.5f, parkedState.Z);

                Entity rebound = runtime.PlayerWorld.BindSession(
                    sessionId: 11,
                    connectionId: 22,
                    requestedPrefabId: PlayerArchetypeIds.Default,
                    playerDisplayName: "Carol",
                    characterIdentity: characterIdentity);

                Assert.Equal(created.Packed, rebound.Packed);
                Assert.True(runtime.PlayerWorld.World.TryGetComponent(rebound, out PlayerState reboundState));
                Assert.Equal(22, reboundState.OwnerConnectionId);
                Assert.Equal(33.5f, reboundState.X);
                Assert.Equal(-4.5f, reboundState.Z);
            }
            finally
            {
                runtime.Stop();
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Save_CapturesCharacterSnapshot_And_RespawnsFromIt_WhenNoPersistedEntityExists()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_snapshot_{Guid.NewGuid():N}");
            RegisterComponents();
            WorldPersistenceCoordinator coordinator = CreateCoordinator(root);
            ServerRuntime runtime = CreateRuntime(root, coordinator, new TestAuthProvider());
            try
            {
                runtime.Start();
                CharacterModel character = CreateCharacter(runtime, "snapshotuser", "Dana", PlayerArchetypeIds.DefaultFemale);
                var characterIdentity = new PlayerCharacterComponent
                {
                    CharacterId = character.CharacterId,
                    SlotIndex = character.SlotIndex,
                };

                Entity created = runtime.PlayerWorld.BindSession(
                    sessionId: 50,
                    connectionId: 5,
                    requestedPrefabId: character.ArchetypeId,
                    playerDisplayName: character.Name,
                    characterIdentity: characterIdentity,
                    spawnSnapshot: new PlayerSpawnSnapshot(character.X, character.Y, character.Z, character.YawRadians));

                Assert.True(runtime.PlayerWorld.World.TryGetComponent(created, out PlayerState state));
                state.X = 42.25f;
                state.Y = 3.5f;
                state.Z = -18.75f;
                state.YawRadians = 1.5f;
                Assert.True(runtime.PlayerWorld.World.TrySetComponent(created, state));

                Assert.True(runtime.Save());
                Assert.True(runtime.Accounts.TryGetCharacter(character.CharacterId, out CharacterModel refreshed));
                Assert.Equal(PlayerArchetypeIds.DefaultFemale, refreshed.ArchetypeId);
                Assert.Equal(42.25f, refreshed.X);
                Assert.Equal(3.5f, refreshed.Y);
                Assert.Equal(-18.75f, refreshed.Z);
                Assert.Equal(1.5f, refreshed.YawRadians);

                runtime.PlayerWorld.World.EnqueueDestroy(created);
                runtime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

                Entity respawned = runtime.PlayerWorld.BindSession(
                    sessionId: 51,
                    connectionId: 6,
                    requestedPrefabId: refreshed.ArchetypeId,
                    playerDisplayName: refreshed.Name,
                    characterIdentity: characterIdentity,
                    spawnSnapshot: new PlayerSpawnSnapshot(refreshed.X, refreshed.Y, refreshed.Z, refreshed.YawRadians));

                Assert.True(runtime.PlayerWorld.World.TryGetComponent(respawned, out PlayerState respawnedState));
                Assert.True(runtime.PlayerWorld.World.TryGetComponent(respawned, out PrefabIdComponent respawnedPrefab));
                Assert.NotEqual(created.Packed, respawned.Packed);
                Assert.Equal(42.25f, respawnedState.X);
                Assert.Equal(3.5f, respawnedState.Y);
                Assert.Equal(-18.75f, respawnedState.Z);
                Assert.Equal(1.5f, respawnedState.YawRadians);
                Assert.Equal(PlayerArchetypeIds.DefaultFemale, respawnedPrefab.PrefabId);
            }
            finally
            {
                runtime.Stop();
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Coordinator_SaveFailure_DoesNotReplaceExistingSaveSet_And_CleansStaging()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_atomic_{Guid.NewGuid():N}");
            try
            {
                RegisterComponents();
                ComponentId unregisteredId = ComponentManager.Register<UnregisteredPersistentTestComponent>();
                WorldPersistenceCoordinator coordinator = CreateCoordinator(root);
                ServerRuntime runtime = CreateRuntime(root, coordinator);
                runtime.Start();

                ComponentId tagId = ComponentManager.GetId<PersistentTag>();
                ComponentId nameId = ComponentManager.GetId<PlayerNameComponent>();
                runtime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, nameId));
                runtime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
                Assert.True(runtime.Save());

                byte[] originalMeta = File.ReadAllBytes(Path.Combine(root, "meta.bin"));
                byte[] originalTypes = File.ReadAllBytes(Path.Combine(root, "types.tdb"));

                runtime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, unregisteredId));
                runtime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => coordinator.Save(runtime));
                Assert.Contains("not registered", ex.Message, StringComparison.Ordinal);

                Assert.Equal(originalMeta, File.ReadAllBytes(Path.Combine(root, "meta.bin")));
                Assert.Equal(originalTypes, File.ReadAllBytes(Path.Combine(root, "types.tdb")));
                Assert.Empty(Directory.EnumerateDirectories(root, ".staging-*"));

                runtime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Coordinator_SystemsParallel_SaveFailure_PreservesOriginalExceptionType()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_parallel_atomic_{Guid.NewGuid():N}");
            try
            {
                RegisterComponents();
                ComponentId unregisteredId = ComponentManager.Register<UnregisteredPersistentTestComponent>();
                WorldPersistenceCoordinator coordinator = CreateCoordinator(root, parallelMode: WorldSaveParallelMode.SystemsParallel);
                ServerRuntime runtime = CreateRuntime(root, coordinator);
                runtime.Start();

                ComponentId tagId = ComponentManager.GetId<PersistentTag>();
                ComponentId nameId = ComponentManager.GetId<PlayerNameComponent>();
                runtime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, nameId));
                runtime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
                Assert.True(runtime.Save());

                runtime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, unregisteredId));
                runtime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

                InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => coordinator.Save(runtime));
                Assert.Contains("not registered", ex.Message, StringComparison.Ordinal);

                runtime.Stop();
            }
            finally
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
        }

        [Fact]
        public void Coordinator_RecoveryInteraction_CanOverrideConfiguredMode()
        {
            string root = Path.Combine(Path.GetTempPath(), $"worldcoord_prompt_{Guid.NewGuid():N}");
            try
            {
                RegisterComponents();
                WorldPersistenceCoordinator saveCoordinator = CreateCoordinator(root);
                ServerRuntime saveRuntime = CreateRuntime(root, saveCoordinator);
                saveRuntime.Start();

                ComponentId tagId = ComponentManager.GetId<PersistentTag>();
                ComponentId nameId = ComponentManager.GetId<PlayerNameComponent>();
                saveRuntime.PlayerWorld.World.EnqueueCreateDefault(Sig(tagId, nameId));
                saveRuntime.PlayerWorld.World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
                Assert.True(saveRuntime.Save());
                saveRuntime.Stop();

                File.Delete(Path.Combine(root, "Entities", "entities.idx"));
                File.Delete(Path.Combine(root, "Entities", "entities.bin"));

                ServerTestRuntime.ResetStatics();
                RegisterComponents();
                var recoveryInteraction = new FakeRecoveryInteraction
                {
                    Response = WorldPersistenceRecoveryMode.ContinueWithoutEntities,
                };
                WorldPersistenceCoordinator loadCoordinator = CreateCoordinator(
                    root,
                    WorldPersistenceRecoveryMode.Abort,
                    recoveryInteraction);
                ServerRuntime loadRuntime = CreateRuntime(root, loadCoordinator);
                loadRuntime.Start();

                Assert.NotNull(recoveryInteraction.LastRequest);
                Assert.Equal(WorldPersistenceRecoveryMode.Abort, recoveryInteraction.LastRequest!.ConfiguredMode);
                Assert.Equal(2, recoveryInteraction.LastRequest.MissingEntityFiles.Count);
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
