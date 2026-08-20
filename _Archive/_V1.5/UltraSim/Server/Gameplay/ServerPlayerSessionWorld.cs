using System;
using System.Collections.Generic;
using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Server.Engines.Networking;
using UltraSim.Shared.Engines.Networking;
using UltraSim.Shared.Gameplay;

namespace UltraSim.Server.Gameplay
{
    public sealed class ServerPlayerSessionWorld
    {
        private readonly ServerPlayerArchetypeCatalog _playerArchetypes;
        private readonly HashSet<ulong> _entityCreateBaseline = new HashSet<ulong>();
        private readonly NetworkInboundCommandQueue<PlayerInputCommand> _inboundCommands =
            new NetworkInboundCommandQueue<PlayerInputCommand>(initialCapacity: 128, threadSafe: false);
        private readonly Dictionary<int, ushort> _lastInputAckByConnection = new Dictionary<int, ushort>(16);
        private readonly Dictionary<long, int> _spawnSlotBySession = new Dictionary<long, int>();
        private readonly Stack<int> _freeSpawnSlots = new Stack<int>(16);
        private readonly ArchetypeSignature _playerSignature;
        private readonly ArchetypeSignature _persistentPlayerSignature;
        private int _nextSpawnSlot;

        public ServerPlayerSessionWorld(
            int spawnSpacingGrid = 4,
            int spawnWrapCount = 0,
            ServerPlayerArchetypeCatalog? playerArchetypes = null)
        {
            SpawnSpacingGrid = Math.Max(0, spawnSpacingGrid);
            SpawnWrapCount = Math.Max(0, spawnWrapCount);
            _playerArchetypes = playerArchetypes ?? ServerPlayerArchetypeCatalog.CreateDefault();
            _playerSignature = CreatePlayerSignature(includePersistenceComponents: false);
            _persistentPlayerSignature = CreatePlayerSignature(includePersistenceComponents: true);
            World = CreateWorld();
            Bindings = new ServerSessionEntityBindings();
        }

        public World World { get; private set; }

        public ServerSessionEntityBindings Bindings { get; }

        public int SpawnSpacingGrid { get; }

        public int SpawnWrapCount { get; }

        public int PlayerCount => Bindings.SessionBindingCount;

        public int ReceivedInputCount { get; private set; }

        public int AppliedInputCount { get; private set; }

        public ServerPlayerArchetypeCatalog PlayerArchetypes => _playerArchetypes;

        public bool TryGetLastInputAck(int connectionId, out ushort sequence) =>
            _lastInputAckByConnection.TryGetValue(connectionId, out sequence);

        public Entity BindSession(
            long sessionId,
            int connectionId,
            ushort? requestedPrefabId = null,
            string? playerDisplayName = null,
            PlayerCharacterComponent? characterIdentity = null,
            PlayerSpawnSnapshot? spawnSnapshot = null)
        {
            if (Bindings.TryGetEntityForSession(sessionId, out Entity existing))
                return existing;

            // Durable character identity chooses the entity. The connection id is attached only after
            // the entity has been resolved or created for this session.
            if (characterIdentity.HasValue &&
                TryFindPersistentCharacterEntity(characterIdentity.Value.CharacterId, out Entity persisted))
            {
                RebindPersistentCharacterEntity(persisted, sessionId, connectionId, requestedPrefabId, playerDisplayName, characterIdentity.Value);
                Bindings.BindSessionEntity(sessionId, persisted);
                return persisted;
            }

            int spawnSlot = _freeSpawnSlots.Count > 0 ? _freeSpawnSlots.Pop() : _nextSpawnSlot++;
            ResolveSpawnPosition(spawnSlot, out float x, out float z);
            float y = 0f;
            float yawRadians = 0f;
            if (spawnSnapshot.HasValue)
            {
                x = spawnSnapshot.Value.X;
                y = spawnSnapshot.Value.Y;
                z = spawnSnapshot.Value.Z;
                yawRadians = spawnSnapshot.Value.YawRadians;
            }

            PlayerArchetypeDefinition archetype = _playerArchetypes.ResolveSpawnDefinition(connectionId, requestedPrefabId);
            ArchetypeSignature signature = characterIdentity.HasValue ? _persistentPlayerSignature : _playerSignature;
            Entity entity = CreatePlayerEntity(World, signature, connectionId, archetype, x, y, z, yawRadians, playerDisplayName, characterIdentity);
            Bindings.BindSessionEntity(sessionId, entity);
            _spawnSlotBySession[sessionId] = spawnSlot;
            return entity;
        }

        public bool UnbindSession(long sessionId)
        {
            if (!Bindings.TryGetEntityForSession(sessionId, out Entity entity))
                return false;

            bool preserveEntity = IsPersistentCharacterEntity(entity);
            int previousOwnerConnectionId = 0;
            if (preserveEntity && World.TryGetComponent(entity, out PlayerState preservedState))
            {
                previousOwnerConnectionId = preservedState.OwnerConnectionId;
                preservedState.OwnerConnectionId = 0;
                preservedState.MoveX = 0;
                preservedState.MoveZ = 0;
                World.TrySetComponent(entity, preservedState);
            }

            Bindings.UnbindSessionEntity(sessionId);
            if (_spawnSlotBySession.Remove(sessionId, out int spawnSlot))
                _freeSpawnSlots.Push(spawnSlot);

            if (previousOwnerConnectionId > 0)
                _lastInputAckByConnection.Remove(previousOwnerConnectionId);

            if (preserveEntity)
                return true;

            World.EnqueueDestroy(entity);
            World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
            return true;
        }

        public bool TryGetEntityForSession(long sessionId, out Entity entity) =>
            Bindings.TryGetEntityForSession(sessionId, out entity);

        public bool TryGetStateForSession(long sessionId, out PlayerState state)
        {
            if (!TryGetEntityForSession(sessionId, out Entity entity))
            {
                state = default;
                return false;
            }

            return World.TryGetComponent(entity, out state);
        }

        public bool TryGetPrefabIdForSession(long sessionId, out PrefabIdComponent prefab)
        {
            if (!TryGetEntityForSession(sessionId, out Entity entity))
            {
                prefab = default;
                return false;
            }

            return World.TryGetComponent(entity, out prefab);
        }

        public bool TryHandleInboundDatagram(long sessionId, in InboundDatagram datagram)
        {
            if (datagram.Channel != ChannelType.ReliableOrdered &&
                datagram.Channel != ChannelType.UnreliableSequenced)
                return false;

            if (!InputFloat3Codec.TryDecode(
                datagram.Payload.Span,
                out ushort sequence,
                out short moveX,
                out short moveZ,
                out _,
                out long clientTick))
            {
                return false;
            }

            _inboundCommands.Enqueue(sessionId, new PlayerInputCommand(sequence, moveX, moveZ, clientTick));
            ReceivedInputCount++;
            return true;
        }

        public void Tick(float deltaSeconds)
        {
            AppliedInputCount += _inboundCommands.Drain(ApplyInboundCommand);
            AdvancePlayerMovement(deltaSeconds);
            World.Tick(deltaSeconds);
        }

        public void ResetWorld()
        {
            World = CreateWorld();
            Bindings.Clear();
            _spawnSlotBySession.Clear();
            _freeSpawnSlots.Clear();
            _inboundCommands.Clear();
            _lastInputAckByConnection.Clear();
            _nextSpawnSlot = 0;
            ReceivedInputCount = 0;
            AppliedInputCount = 0;
        }

        public int CompactOfflinePersistentCharacters()
        {
            var toDestroy = new List<Entity>();
            World.Query<PersistentTag, PlayerCharacterComponent, PlayerState>((Entity entity, ref PersistentTag _, ref PlayerCharacterComponent character, ref PlayerState state) =>
            {
                if (character.CharacterId == 0 || state.OwnerConnectionId != 0)
                    return;

                if (Bindings.TryGetSessionForEntity(entity, out long _))
                    return;

                toDestroy.Add(entity);
            });

            if (toDestroy.Count == 0)
                return 0;

            for (int i = 0; i < toDestroy.Count; i++)
                World.EnqueueDestroy(toDestroy[i]);

            World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
            return toDestroy.Count;
        }

        public CharacterSnapshotUpdate[] CapturePersistentCharacterSnapshots()
        {
            var snapshots = new List<CharacterSnapshotUpdate>(16);
            DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

            World.Query<PersistentTag, PlayerCharacterComponent, PlayerState>((Entity entity, ref PersistentTag _, ref PlayerCharacterComponent character, ref PlayerState state) =>
            {
                if (character.CharacterId == 0)
                    return;

                ushort prefabId = 0;
                if (World.TryGetComponent(entity, out PrefabIdComponent prefab))
                    prefabId = prefab.PrefabId;

                snapshots.Add(new CharacterSnapshotUpdate(
                    character.CharacterId,
                    nowUtc,
                    prefabId,
                    state.X,
                    state.Y,
                    state.Z,
                    state.YawRadians));
            });

            return snapshots.ToArray();
        }

        private void ResolveSpawnPosition(int spawnSlot, out float x, out float z)
        {
            int spacing = SpawnSpacingGrid;
            int wrapCount = SpawnWrapCount;
            int spawnX;
            int spawnZ;

            if (wrapCount > 0)
            {
                int col = spawnSlot % wrapCount;
                int row = spawnSlot / wrapCount;
                spawnX = col * spacing;
                spawnZ = row * spacing;
            }
            else
            {
                spawnX = spawnSlot * spacing;
                spawnZ = 0;
            }

            x = spawnX * SharedGameplayConstants.WorldUnitsPerGrid;
            z = spawnZ * SharedGameplayConstants.WorldUnitsPerGrid;
        }

        private static World CreateWorld()
        {
            var options = new EcsOptions(enableStatistics: true, enableDebug: false, enableDirtyTracking: true);
            var world = new World(NoOpHost.Instance, NoOpWorldEventSink.Instance, options);
            world.Initialize();
            return world;
        }

        private static ArchetypeSignature CreatePlayerSignature(bool includePersistenceComponents)
        {
            ComponentId markerId = EnsureComponentRegistered<PlayerMarker>();
            ComponentId stateId = EnsureComponentRegistered<PlayerState>();
            ComponentId prefabId = EnsureComponentRegistered<PrefabIdComponent>();
            if (!includePersistenceComponents)
                return new ArchetypeSignature(stackalloc[] { markerId, stateId, prefabId });

            ComponentId persistentTagId = EnsureComponentRegistered<PersistentTag>();
            ComponentId nameId = EnsureComponentRegistered<PlayerNameComponent>();
            ComponentId characterId = EnsureComponentRegistered<PlayerCharacterComponent>();
            return new ArchetypeSignature(stackalloc[] { markerId, stateId, prefabId, persistentTagId, nameId, characterId });
        }

        private Entity CreatePlayerEntity(
            World world,
            ArchetypeSignature signature,
            int ownerConnectionId,
            PlayerArchetypeDefinition archetype,
            float x,
            float y,
            float z,
            float yawRadians,
            string? playerDisplayName,
            PlayerCharacterComponent? characterIdentity)
        {
            _entityCreateBaseline.Clear();
            world.Query<PlayerState>((Entity entity, ref PlayerState _) =>
            {
                _entityCreateBaseline.Add(entity.Packed);
            });

            world.EnqueueCreateDefault(signature);
            world.ProcessStructuralQueues(StructuralBarrierPhase.Manual);

            Entity created = default;
            world.Query<PlayerState>((Entity entity, ref PlayerState _) =>
            {
                if (created.IsValid() || _entityCreateBaseline.Contains(entity.Packed))
                    return;

                created = entity;
            });

            if (!created.IsValid())
            {
                world.Query<PlayerState>((Entity entity, ref PlayerState _) =>
                {
                    if (!created.IsValid())
                        created = entity;
                });
            }

            if (!created.IsValid())
                throw new InvalidOperationException("Failed to resolve newly created player entity.");

            world.TrySetComponent(created, new PlayerState
            {
                OwnerConnectionId = ownerConnectionId,
                ActorDefinitionId = archetype.ActorDefinitionId,
                X = x,
                Y = y,
                Z = z,
                YawRadians = yawRadians,
                MoveX = 0,
                MoveZ = 0,
            });
            world.TrySetComponent(created, new PrefabIdComponent
            {
                PrefabId = archetype.ArchetypeId,
            });
            if (characterIdentity.HasValue)
            {
                world.TrySetComponent(created, characterIdentity.Value);
            }
            if (!string.IsNullOrWhiteSpace(playerDisplayName))
            {
                world.SetOrAddComponent(created, PlayerNameComponent.FromString(playerDisplayName.AsSpan().Trim()));
                world.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
            }

            return created;
        }

        private void ApplyInboundCommand(in QueuedInboundCommand<PlayerInputCommand> command)
        {
            if (!Bindings.TryGetEntityForSession(command.SessionId, out Entity controlled))
                return;

            if (!World.TryGetComponent(controlled, out PlayerState state))
                return;

            state.MoveX = command.Command.MoveX;
            state.MoveZ = command.Command.MoveZ;
            World.TrySetComponent(controlled, state);
            _lastInputAckByConnection[state.OwnerConnectionId] = command.Command.Sequence;
        }

        private void AdvancePlayerMovement(float deltaSeconds)
        {
            World.Query<PlayerState>((Entity entity, ref PlayerState state) =>
            {
                float moveX = MovementQuantization.DequantizeDirection(state.MoveX);
                float moveZ = MovementQuantization.DequantizeDirection(state.MoveZ);
                float lengthSquared = moveX * moveX + moveZ * moveZ;
                if (lengthSquared > 1.0f)
                {
                    float scale = 1.0f / MathF.Sqrt(lengthSquared);
                    moveX *= scale;
                    moveZ *= scale;
                    lengthSquared = moveX * moveX + moveZ * moveZ;
                }

                if (lengthSquared <= 0.0001f)
                    return;

                state.X += moveX * MovementProtocolConstants.PlayerMoveSpeedMetersPerSecond * deltaSeconds;
                state.Z += moveZ * MovementProtocolConstants.PlayerMoveSpeedMetersPerSecond * deltaSeconds;
                state.YawRadians = MathF.Atan2(moveX, moveZ);
                World.TrySetComponent(entity, state);
            });
        }

        private bool TryFindPersistentCharacterEntity(ulong characterId, out Entity entity)
        {
            Entity found = default;
            World.Query<PlayerCharacterComponent>((Entity candidate, ref PlayerCharacterComponent character) =>
            {
                if (found.IsValid() || character.CharacterId != characterId)
                    return;

                found = candidate;
            });

            entity = found;
            return found.IsValid();
        }

        private bool IsPersistentCharacterEntity(Entity entity) =>
            World.TryGetComponent(entity, out PersistentTag _) &&
            World.TryGetComponent(entity, out PlayerCharacterComponent character) &&
            character.CharacterId != 0;

        private void RebindPersistentCharacterEntity(
            Entity entity,
            long sessionId,
            int connectionId,
            ushort? requestedPrefabId,
            string? playerDisplayName,
            PlayerCharacterComponent characterIdentity)
        {
            if (World.TryGetComponent(entity, out PlayerState state))
            {
                state.OwnerConnectionId = connectionId;
                state.MoveX = 0;
                state.MoveZ = 0;
                World.TrySetComponent(entity, state);
            }

            if (requestedPrefabId.HasValue && World.TryGetComponent(entity, out PrefabIdComponent prefab))
            {
                prefab.PrefabId = requestedPrefabId.Value;
                World.TrySetComponent(entity, prefab);
            }

            if (World.TryGetComponent(entity, out PlayerCharacterComponent existingCharacter))
            {
                existingCharacter.CharacterId = characterIdentity.CharacterId;
                existingCharacter.SlotIndex = characterIdentity.SlotIndex;
                World.TrySetComponent(entity, existingCharacter);
            }

            if (!string.IsNullOrWhiteSpace(playerDisplayName))
            {
                World.SetOrAddComponent(entity, PlayerNameComponent.FromString(playerDisplayName.AsSpan().Trim()));
                World.ProcessStructuralQueues(StructuralBarrierPhase.Manual);
            }

            int spawnSlot = _freeSpawnSlots.Count > 0 ? _freeSpawnSlots.Pop() : _nextSpawnSlot++;
            _spawnSlotBySession[sessionId] = spawnSlot;
        }

        private static ComponentId EnsureComponentRegistered<T>() where T : unmanaged, IComponent<T>
        {
            if (ComponentManager.TryGetId(typeof(T), out ComponentId existing))
                return existing;

            if (ComponentManager.IsSealed)
                throw new InvalidOperationException($"Component {typeof(T).Name} is not registered and ComponentManager is sealed.");

            return ComponentManager.Register<T>();
        }
    }
}
