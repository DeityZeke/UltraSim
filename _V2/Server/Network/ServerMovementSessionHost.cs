using System.Diagnostics;
using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Server.Accounting;
using UltraSim.Server.Admission;
using UltraSim.Server.Gameplay;
using UltraSim.Server.Scripting;

namespace UltraSim.Server.Network;

/// <summary>
/// Narrow live server session host that shares one transport poll path between control-plane
/// session traffic and authoritative movement traffic.
/// </summary>
public sealed class ServerMovementSessionHost
{
    private readonly World _world;
    private readonly INetworkTransport _transport;
    private readonly NetworkControlPlaneHost _control;
    private readonly ServerEcsMovementNetworkBridge _movement;
    private readonly ServerEcsWorldNetworkPublisher _worldPublisher;
    private readonly ConnectionSubscriptionRegistry _subscriptions;
    private readonly ServerNetworkSessionRuntime _sessionRuntime;
    private readonly ServerChatService _chat;
    private readonly ServerUiService _ui;
    private readonly ServerScriptRuntimeBinding? _scriptBinding;
    private readonly ServerMovementNetworkEntityIdAllocator? _allocateNetworkEntityId;
    private readonly ServerMovementSessionSpawner _spawnSession;
    private readonly ServerMovementSessionDespawner? _despawnSession;
    private readonly ServerAccountSessionEnder? _endAccountSession;
    private readonly ServerAccountPlayTimeRecorder? _recordAccountPlayTime;
    private readonly ServerAccountAccessMetadataProvider? _getAccountAccessMetadata;
    private readonly ServerCharacterPositionProjector? _characterPositionProjector;
    private readonly ServerCharacterPositionApplier? _characterPositionApplier;
    private readonly ServerSelectedCharacterSnapshotWriter? _selectedCharacterSnapshotWriter;
    private readonly Action<ConnectionId, string>? _operatorLog;
    private readonly Func<DateTimeOffset> _snapshotClock;
    private readonly bool _enableGlobalMovementObserverState;
    private readonly bool _requiresAdmission;
    private readonly SubscriptionKey _globalMovementObserverScope;
    private readonly InboundDatagram[] _inboundScratch;
    private readonly ControlPlaneEvent[] _controlEvents;
    private readonly SessionLifecycleEvent[] _sessionEvents;
    private readonly HashSet<ConnectionId> _boundConnections = new();
    private readonly Dictionary<ConnectionId, Entity> _entityByConnection = new();
    private readonly Dictionary<ConnectionId, uint> _networkEntityIdByConnection = new();
    private readonly Dictionary<ConnectionId, ServerSelectedCharacterSession> _selectedCharactersByConnection = new();
    private readonly Dictionary<ConnectionId, DateTimeOffset> _connectedAtUtcByConnection = new();
    private readonly HashSet<ConnectionId> _pendingAdmissionStarts = new();
    private uint _nextAdmissionNetworkEntityId = 1;

    public ServerMovementSessionHost(
        World world,
        INetworkTransport transport,
        ServerEcsMovementInputApplier applyInput,
        ServerEcsMovementStateProjector projectState,
        ServerMovementSessionSpawner spawnSession,
        ServerMovementSessionDespawner? despawnSession = null,
        ServerAccountSessionEnder? endAccountSession = null,
        ServerAccountPlayTimeRecorder? recordAccountPlayTime = null,
        ServerAccountAccessMetadataProvider? getAccountAccessMetadata = null,
        ServerCharacterPositionProjector? characterPositionProjector = null,
        ServerCharacterPositionApplier? characterPositionApplier = null,
        ServerSelectedCharacterSnapshotWriter? selectedCharacterSnapshotWriter = null,
        Func<DateTimeOffset>? snapshotClock = null,
        HandshakePolicy handshakePolicy = default,
        SessionAdmissionPolicy sessionPolicy = default,
        ServerMovementOwnerCorrectionPolicy ownerCorrectionPolicy = default,
        ServerNetworkSessionRuntimeOptions runtimeOptions = default,
        ServerMovementNetworkEntityIdAllocator? allocateNetworkEntityId = null,
        DefaultUltraSimScriptContext? scriptContext = null,
        IAdmissionCredentialValidator? admissionCredentialValidator = null,
        IAdmissionRuntimePolicy? admissionRuntimePolicy = null,
        Action<ConnectionId, string>? operatorLog = null,
        string commandPrefix = "/",
        ServerAccessRuleRegistry? accessRules = null,
        IServerCommandAuditSink? commandAudit = null,
        bool enableGlobalMovementObserverState = false,
        SubscriptionKey globalMovementObserverScope = default,
        int maxPollBatch = 64)
    {
        if (maxPollBatch <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxPollBatch));
        if (string.IsNullOrWhiteSpace(commandPrefix))
            throw new ArgumentException("Command prefix cannot be empty.", nameof(commandPrefix));

        _world = world ?? throw new ArgumentNullException(nameof(world));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _control = new NetworkControlPlaneHost(
            NetworkControlPlaneRole.Server,
            transport,
            handshakePolicy: handshakePolicy,
            sessionPolicy: sessionPolicy);
        _movement = new ServerEcsMovementNetworkBridge(world, transport, applyInput, projectState, ownerCorrectionPolicy);
        var latestState = new LatestStateStream();
        if (runtimeOptions == default)
            runtimeOptions = ServerNetworkSessionRuntimeOptions.Default;
        var orderedEvents = new OrderedEventStream(capacity: runtimeOptions.OrderedEventCapacity);
        _worldPublisher = new ServerEcsWorldNetworkPublisher(world, latestState, orderedEvents);
        _subscriptions = new ConnectionSubscriptionRegistry();
        _sessionRuntime = new ServerNetworkSessionRuntime(
            transport,
            latestState,
            orderedEvents,
            _subscriptions,
            new ReliableEventReplayBuffer(capacityPerConnection: runtimeOptions.ReplayBufferCapacityPerConnection),
            runtimeOptions);
        string normalizedCommandPrefix = commandPrefix.Trim();
        _chat = new ServerChatService(orderedEvents, new ServerCommandRegistry(
            normalizedCommandPrefix,
            accessRules,
            commandAudit));
        _ui = new ServerUiService(orderedEvents);
        ServerChatCommandBootstrap.RegisterDefaults(_chat.Commands, _chat, normalizedCommandPrefix);
        if (scriptContext is not null)
        {
            _scriptBinding = new ServerScriptRuntimeBinding(
                scriptContext,
                _chat.Commands,
                _ui,
                world.TryCreatePrefab,
                admissionCredentialValidator,
                admissionRuntimePolicy,
                normalizedCommandPrefix);
        }
        _requiresAdmission = scriptContext?.RegisteredAdmissionSteps.Count > 0;
        _allocateNetworkEntityId = allocateNetworkEntityId;
        _spawnSession = spawnSession ?? throw new ArgumentNullException(nameof(spawnSession));
        _despawnSession = despawnSession;
        _endAccountSession = endAccountSession;
        _recordAccountPlayTime = recordAccountPlayTime;
        _getAccountAccessMetadata = getAccountAccessMetadata;
        _characterPositionProjector = characterPositionProjector;
        _characterPositionApplier = characterPositionApplier;
        _selectedCharacterSnapshotWriter = selectedCharacterSnapshotWriter;
        _operatorLog = operatorLog;
        _snapshotClock = snapshotClock ?? (static () => DateTimeOffset.UtcNow);
        _enableGlobalMovementObserverState = enableGlobalMovementObserverState;
        _globalMovementObserverScope = globalMovementObserverScope.IsValid
            ? globalMovementObserverScope
            : ServerEcsNetworkScopes.SimulationGlobal;
        _inboundScratch = new InboundDatagram[maxPollBatch];
        _controlEvents = new ControlPlaneEvent[maxPollBatch];
        _sessionEvents = new SessionLifecycleEvent[maxPollBatch];
    }

    public NetworkControlPlaneHost Control => _control;
    public ServerEcsMovementNetworkBridge Movement => _movement;
    public ServerEcsWorldNetworkPublisher WorldPublisher => _worldPublisher;
    public ConnectionSubscriptionRegistry Subscriptions => _subscriptions;
    public ServerNetworkSessionRuntime SessionRuntime => _sessionRuntime;
    public ServerChatService Chat => _chat;
    public ServerUiService Ui => _ui;
    public ServerScriptRuntimeBinding? ScriptBinding => _scriptBinding;
    public ReliableEventAckTracker ReliableEventAcks => _sessionRuntime.ReliableEventAcks;
    public StateBaselineAckTracker StateBaselineAcks => _sessionRuntime.BaselineAcks;
    public ServerPumpProfileSnapshot LastPumpProfile { get; private set; }
    public int BoundConnectionCount => _boundConnections.Count;

    public int CopyBoundConnections(Span<ConnectionId> destination)
    {
        int count = 0;
        foreach (ConnectionId connectionId in _boundConnections)
        {
            if (count >= destination.Length)
                break;
            destination[count++] = connectionId;
        }

        return count;
    }

    public bool TryGetBoundEntity(ConnectionId connectionId, out Entity entity)
    {
        return _entityByConnection.TryGetValue(connectionId, out entity);
    }

    public bool TryGetSelectedCharacter(
        ConnectionId connectionId,
        out ServerSelectedCharacterSession selectedCharacter)
    {
        return _selectedCharactersByConnection.TryGetValue(connectionId, out selectedCharacter!);
    }

    public bool TryGetConnectedAtUtc(ConnectionId connectionId, out DateTimeOffset connectedAtUtc)
    {
        return _connectedAtUtcByConnection.TryGetValue(connectionId, out connectedAtUtc);
    }

    public bool TryEnterWorld(ConnectionId connectionId, ServerCharacterRecord character)
    {
        ArgumentNullException.ThrowIfNull(character);
        if (!_boundConnections.Contains(connectionId) ||
            !_networkEntityIdByConnection.TryGetValue(connectionId, out uint networkEntityId))
        {
            return false;
        }

        bool hadEntity = _entityByConnection.TryGetValue(connectionId, out Entity currentEntity);
        Entity sessionEntity = hadEntity ? currentEntity : default;
        bool usedPrefabEntity = false;
        if (_world.TryCreatePrefab(character.PrefabKey, out Entity prefabEntity))
        {
            if (hadEntity)
            {
                _movement.Unbind(connectionId);
                if (_despawnSession is not null)
                {
                    _despawnSession(_world, connectionId, currentEntity);
                }
                else if (_world.Entities.IsAlive(currentEntity))
                {
                    _world.Destroy(currentEntity);
                }
            }

            sessionEntity = prefabEntity;
            usedPrefabEntity = true;
            _entityByConnection[connectionId] = sessionEntity;
            _movement.Bind(connectionId, sessionEntity, networkEntityId);
        }
        else if (!hadEntity)
        {
            ServerMovementSessionSpawn spawn = _spawnSession(_world, connectionId);
            sessionEntity = spawn.Entity;
            networkEntityId = spawn.NetworkEntityId;
            _entityByConnection[connectionId] = sessionEntity;
            _networkEntityIdByConnection[connectionId] = networkEntityId;
            _movement.Bind(connectionId, sessionEntity, networkEntityId);
        }

        _characterPositionApplier?.Invoke(_world, sessionEntity, character.Position);
        ApplyCharacterProfile(sessionEntity, character);
        string presentationDefinitionId = ResolveClientMobileDefinitionId(character);
        _selectedCharactersByConnection[connectionId] = new ServerSelectedCharacterSession(
            connectionId,
            character.AccountId,
            character.CharacterId,
            character.Name,
            character.PrefabKey,
            presentationDefinitionId,
            sessionEntity,
            usedPrefabEntity);
        PublishSelectedEntityPresentations();
        return true;
    }

    private void ApplyCharacterProfile(Entity entity, ServerCharacterRecord character)
    {
        if (!_world.Schema.TryGetComponentId(typeof(BiologicalProfile), out _))
            return;

        if (!_world.TryGetComponent(entity, out BiologicalProfile profile))
            return;

        BiologicalSex sex = character.BiologicalSex.Equals("female", StringComparison.OrdinalIgnoreCase)
            ? BiologicalSex.Female
            : BiologicalSex.Male;
        _world.SetComponent(entity, profile with { Sex = sex });
    }

    public void Start() => _control.Start();

    public void Stop()
    {
        long nowTicks = Stopwatch.GetTimestamp();
        SendDisconnectNotices(SessionDisconnectReason.ServerShutdown, nowTicks);
        _control.FlushOutbound();
        HandleSessionLifecycleEvents(nowTicks);
        _control.Stop();
    }

    public bool TryKick(ConnectionId connectionId, long nowTicks)
    {
        if (!_boundConnections.Contains(connectionId))
            return false;

        if (!_control.SendDisconnectNotice(connectionId, SessionDisconnectReason.Kicked, nowTicks))
            return false;

        _control.FlushOutbound();
        HandleSessionLifecycleEvents(nowTicks);
        return true;
    }

    public int Pump(long nowTicks, float deltaSeconds, ulong serverTick, bool flushOutbound = true)
    {
        int totalInboundCount = 0;
        long pollElapsedTicks = 0;
        long pollPendingDrainElapsedTicks = 0;
        long pollSocketReceiveElapsedTicks = 0;
        long pollBundleUnpackElapsedTicks = 0;
        long pollBundleIndexElapsedTicks = 0;
        long pollBundleMaterializeElapsedTicks = 0;
        long pollBundleSliceElapsedTicks = 0;
        long pollBundleDatagramCreateElapsedTicks = 0;
        long pollBundleEnqueueElapsedTicks = 0;
        long pollPayloadCopyElapsedTicks = 0;
        long pollEnvelopeParseElapsedTicks = 0;
        long pollAsyncReceiveElapsedTicks = 0;
        int pollAsyncQueuedDatagramCount = 0;
        long controlElapsedTicks = 0;
        long controlEventElapsedTicks = 0;
        long runtimeInboundElapsedTicks = 0;
        long movementInboundElapsedTicks = 0;
        long sweepElapsedTicks = 0;
        long lifecycleElapsedTicks = 0;
        long movementAdvanceElapsedTicks = 0;
        long runtimeDispatchElapsedTicks = 0;
        long controlFlushElapsedTicks = 0;
        long controlFlushDrainElapsedTicks = 0;
        long controlFlushBundleElapsedTicks = 0;
        long controlFlushSendElapsedTicks = 0;
        long controlFlushAsyncPreviousElapsedTicks = 0;
        long controlFlushAsyncPreviousSendElapsedTicks = 0;
        long controlFlushAsyncHandoffWaitElapsedTicks = 0;
        int controlFlushAsyncPendingBatchCount = 0;
        int controlFlushWireDatagramCount = 0;
        while (true)
        {
            long pollStart = Stopwatch.GetTimestamp();
            int inboundCount = _transport.PollInbound(_inboundScratch);
            pollElapsedTicks += Stopwatch.GetTimestamp() - pollStart;
            if (_transport is UdpSocketNetworkTransport udpTransport)
            {
                TransportPollProfileSnapshot pollProfile = udpTransport.LastPollProfile;
                pollPendingDrainElapsedTicks += pollProfile.PendingDrainElapsedTicks;
                pollSocketReceiveElapsedTicks += pollProfile.SocketReceiveElapsedTicks;
                pollBundleUnpackElapsedTicks += pollProfile.BundleUnpackElapsedTicks;
                pollBundleIndexElapsedTicks += pollProfile.BundleIndexElapsedTicks;
                pollBundleMaterializeElapsedTicks += pollProfile.BundleMaterializeElapsedTicks;
                pollBundleSliceElapsedTicks += pollProfile.BundleSliceElapsedTicks;
                pollBundleDatagramCreateElapsedTicks += pollProfile.BundleDatagramCreateElapsedTicks;
                pollBundleEnqueueElapsedTicks += pollProfile.BundleEnqueueElapsedTicks;
                pollPayloadCopyElapsedTicks += pollProfile.PayloadCopyElapsedTicks;
                pollEnvelopeParseElapsedTicks += pollProfile.EnvelopeParseElapsedTicks;
                pollAsyncReceiveElapsedTicks += pollProfile.AsyncReceiveElapsedTicks;
                pollAsyncQueuedDatagramCount += pollProfile.AsyncQueuedDatagramCount;
            }
            if (inboundCount <= 0)
                break;

            ReadOnlySpan<InboundDatagram> inbound = _inboundScratch.AsSpan(0, inboundCount);
            long controlStart = Stopwatch.GetTimestamp();
            _control.ProcessInbound(inbound, nowTicks);
            controlElapsedTicks += Stopwatch.GetTimestamp() - controlStart;

            long controlEventsStart = Stopwatch.GetTimestamp();
            HandleControlPlaneEvents(nowTicks);
            controlEventElapsedTicks += Stopwatch.GetTimestamp() - controlEventsStart;

            long runtimeInboundStart = Stopwatch.GetTimestamp();
            _sessionRuntime.ProcessInbound(inbound);
            runtimeInboundElapsedTicks += Stopwatch.GetTimestamp() - runtimeInboundStart;

            ProcessChatInbound(inbound, nowTicks, serverTick);
            ProcessUiActionInbound(inbound);
            ProcessLoginCredentialInbound(inbound);

            long movementInboundStart = Stopwatch.GetTimestamp();
            _movement.ProcessInbound(inbound);
            movementInboundElapsedTicks += Stopwatch.GetTimestamp() - movementInboundStart;
            totalInboundCount += inboundCount;
            DisposeInbound(inbound);

            if (inboundCount < _inboundScratch.Length)
                break;
        }

        if (totalInboundCount == 0)
        {
            long sweepStart = Stopwatch.GetTimestamp();
            _control.Sessions.SweepTimeouts(nowTicks);
            sweepElapsedTicks += Stopwatch.GetTimestamp() - sweepStart;
        }

        long lifecycleStart = Stopwatch.GetTimestamp();
        HandleSessionLifecycleEvents(nowTicks);
        lifecycleElapsedTicks += Stopwatch.GetTimestamp() - lifecycleStart;

        long movementAdvanceStart = Stopwatch.GetTimestamp();
        _movement.AdvanceAndSend(deltaSeconds, serverTick, flushOutbound: false);
        if (_enableGlobalMovementObserverState)
            _movement.PublishObserverMovementStates(_worldPublisher, _globalMovementObserverScope, serverTick);
        movementAdvanceElapsedTicks += Stopwatch.GetTimestamp() - movementAdvanceStart;

        long runtimeDispatchStart = Stopwatch.GetTimestamp();
        _sessionRuntime.DispatchTick(nowTicks, ConnectionSendBudget.Unlimited, flushOutbound: false);
        runtimeDispatchElapsedTicks += Stopwatch.GetTimestamp() - runtimeDispatchStart;
        ProcessPendingAdmissionStarts();

        if (flushOutbound)
        {
            long controlFlushStart = Stopwatch.GetTimestamp();
            _control.FlushOutbound();
            controlFlushElapsedTicks += Stopwatch.GetTimestamp() - controlFlushStart;
            if (_transport is UdpSocketNetworkTransport flushTransport)
            {
                TransportFlushProfileSnapshot flushProfile = flushTransport.LastFlushProfile;
                controlFlushDrainElapsedTicks += flushProfile.DrainElapsedTicks;
                controlFlushBundleElapsedTicks += flushProfile.BundleElapsedTicks;
                controlFlushSendElapsedTicks += flushProfile.SendElapsedTicks;
                controlFlushAsyncPreviousElapsedTicks += flushProfile.AsyncPreviousFlushElapsedTicks;
                controlFlushAsyncPreviousSendElapsedTicks += flushProfile.AsyncPreviousSendElapsedTicks;
                controlFlushAsyncHandoffWaitElapsedTicks += flushProfile.AsyncHandoffWaitElapsedTicks;
                controlFlushAsyncPendingBatchCount = flushProfile.AsyncPendingBatchCount;
                controlFlushWireDatagramCount = flushProfile.AsyncEnabled
                    ? flushProfile.AsyncPreviousWireDatagramCount
                    : flushProfile.WireDatagramCount;
            }
        }
        LastPumpProfile = new ServerPumpProfileSnapshot(
            totalInboundCount,
            pollElapsedTicks,
            pollPendingDrainElapsedTicks,
            pollSocketReceiveElapsedTicks,
            pollBundleUnpackElapsedTicks,
            pollBundleIndexElapsedTicks,
            pollBundleMaterializeElapsedTicks,
            pollBundleSliceElapsedTicks,
            pollBundleDatagramCreateElapsedTicks,
            pollBundleEnqueueElapsedTicks,
            pollPayloadCopyElapsedTicks,
            pollEnvelopeParseElapsedTicks,
            pollAsyncReceiveElapsedTicks,
            pollAsyncQueuedDatagramCount,
            controlElapsedTicks,
            controlEventElapsedTicks,
            runtimeInboundElapsedTicks,
            movementInboundElapsedTicks,
            sweepElapsedTicks,
            lifecycleElapsedTicks,
            movementAdvanceElapsedTicks,
            runtimeDispatchElapsedTicks,
            controlFlushElapsedTicks,
            controlFlushDrainElapsedTicks,
            controlFlushBundleElapsedTicks,
            controlFlushSendElapsedTicks,
            controlFlushAsyncPreviousElapsedTicks,
            controlFlushAsyncPreviousSendElapsedTicks,
            controlFlushAsyncHandoffWaitElapsedTicks,
            controlFlushAsyncPendingBatchCount,
            controlFlushWireDatagramCount);
        return totalInboundCount;
    }

    private void SendDisconnectNotices(SessionDisconnectReason reason, long nowTicks)
    {
        foreach (ConnectionId connectionId in _boundConnections)
            _control.SendDisconnectNotice(connectionId, reason, nowTicks);
    }

    private void HandleControlPlaneEvents(long nowTicks)
    {
        while (true)
        {
            int count = _control.DrainEvents(_controlEvents);
            if (count == 0)
                break;

            for (int i = 0; i < count; i++)
            {
                ControlPlaneEvent evt = _controlEvents[i];
                if (evt.Kind != ControlPlaneEventKind.HandshakeAccepted)
                    continue;
                if (_boundConnections.Contains(evt.ConnectionId))
                    continue;

                _boundConnections.Add(evt.ConnectionId);
                _connectedAtUtcByConnection[evt.ConnectionId] = _snapshotClock();
                _operatorLog?.Invoke(evt.ConnectionId, "Connected");
                _sessionRuntime.BindConnection(evt.ConnectionId);
                _subscriptions.Subscribe(evt.ConnectionId, ServerChatScopes.System);
                _subscriptions.Subscribe(evt.ConnectionId, ServerUiScopes.ForConnection(evt.ConnectionId));
                if (_enableGlobalMovementObserverState)
                    _subscriptions.Subscribe(evt.ConnectionId, _globalMovementObserverScope);

                if (_requiresAdmission)
                {
                    _networkEntityIdByConnection[evt.ConnectionId] = ReserveNetworkEntityId(evt.ConnectionId);
                    _pendingAdmissionStarts.Add(evt.ConnectionId);
                    continue;
                }

                ServerMovementSessionSpawn spawn = _spawnSession(_world, evt.ConnectionId);
                _movement.Bind(evt.ConnectionId, spawn.Entity, spawn.NetworkEntityId);
                _entityByConnection[evt.ConnectionId] = spawn.Entity;
                _networkEntityIdByConnection[evt.ConnectionId] = spawn.NetworkEntityId;
            }
        }
    }

    private void HandleSessionLifecycleEvents(long nowTicks)
    {
        while (true)
        {
            int count = _control.Sessions.DrainLifecycleEvents(_sessionEvents);
            if (count == 0)
                break;

            for (int i = 0; i < count; i++)
            {
                SessionLifecycleEvent evt = _sessionEvents[i];
                if (evt.CurrentState != ConnectionSessionState.Disconnected &&
                    evt.Kind != SessionLifecycleEventKind.Removed)
                {
                    continue;
                }

                if (_boundConnections.Remove(evt.ConnectionId))
                {
                    _selectedCharactersByConnection.TryGetValue(
                        evt.ConnectionId,
                        out ServerSelectedCharacterSession? selectedCharacter);
                    if (_entityByConnection.TryGetValue(evt.ConnectionId, out Entity entity))
                    {
                        TrySnapshotSelectedCharacter(evt.ConnectionId, entity);
                        _movement.Unbind(evt.ConnectionId);
                        _despawnSession?.Invoke(_world, evt.ConnectionId, entity);
                        if (_despawnSession is null && _world.Entities.IsAlive(entity))
                            _world.Destroy(entity);
                    }

                    if (_enableGlobalMovementObserverState &&
                        _networkEntityIdByConnection.TryGetValue(evt.ConnectionId, out uint networkEntityId))
                    {
                        _worldPublisher.LatestStateStream.Remove(
                            ServerEcsNetworkItemKeys.GetObservedMovementItemKey(networkEntityId),
                            _globalMovementObserverScope);
                    }

                    _sessionRuntime.UnbindConnection(evt.ConnectionId);
                    _subscriptions.RemoveConnection(evt.ConnectionId);
                    _entityByConnection.Remove(evt.ConnectionId);
                    _networkEntityIdByConnection.Remove(evt.ConnectionId);
                    _selectedCharactersByConnection.Remove(evt.ConnectionId);
                    _pendingAdmissionStarts.Remove(evt.ConnectionId);
                    EndAdmissionAndAccountSession(evt.ConnectionId, selectedCharacter);
                    _connectedAtUtcByConnection.Remove(evt.ConnectionId);
                    _operatorLog?.Invoke(evt.ConnectionId, FormatDisconnectMessage(evt.DisconnectReason));
                }
                else
                {
                    EndAdmissionAndAccountSession(evt.ConnectionId, selectedCharacter: null);
                }

                if (evt.CurrentState == ConnectionSessionState.Disconnected)
                    _control.Sessions.TryRemoveDisconnected(evt.SessionId, nowTicks);
            }
        }
    }

    private void EndAdmissionAndAccountSession(
        ConnectionId connectionId,
        ServerSelectedCharacterSession? selectedCharacter)
    {
        ulong accountId = selectedCharacter?.AccountId ?? 0;
        if (_scriptBinding?.Admission.TryClearConnection(connectionId, out ulong admissionAccountId) == true &&
            accountId == 0)
        {
            accountId = admissionAccountId;
        }

        if (accountId != 0)
        {
            if (_connectedAtUtcByConnection.TryGetValue(connectionId, out DateTimeOffset connectedAtUtc))
            {
                TimeSpan elapsed = _snapshotClock() - connectedAtUtc;
                if (elapsed > TimeSpan.Zero)
                    _recordAccountPlayTime?.Invoke(accountId, elapsed);
            }

            _endAccountSession?.Invoke(connectionId, accountId);
        }
    }

    private static string FormatDisconnectMessage(SessionDisconnectReason reason)
    {
        return reason == SessionDisconnectReason.None
            ? "Disconnected"
            : $"Disconnected ({reason})";
    }

    private bool TrySnapshotSelectedCharacter(ConnectionId connectionId, Entity entity)
    {
        if (!_selectedCharactersByConnection.TryGetValue(connectionId, out ServerSelectedCharacterSession? selected) ||
            _characterPositionProjector is null ||
            _selectedCharacterSnapshotWriter is null ||
            !_world.Entities.IsAlive(entity))
        {
            return false;
        }

        if (!_characterPositionProjector(_world, entity, out CharacterPositionSnapshot position))
            return false;

        return _selectedCharacterSnapshotWriter(connectionId, selected, position, _snapshotClock());
    }

    private void ProcessChatInbound(ReadOnlySpan<InboundDatagram> inbound, long nowTicks, ulong serverTick)
    {
        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.PacketId != ChatSubmitPacket.PacketId ||
                datagram.Plane != ChatSubmitPacket.Plane ||
                datagram.PacketClass != ChatSubmitPacket.PacketClass)
            {
                continue;
            }

            if (!_control.Sessions.TryGetSessionIdByConnection(datagram.ConnectionId, out SessionId sessionId) ||
                !_control.Sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot) ||
                snapshot.State != ConnectionSessionState.Ready)
            {
                continue;
            }

            var reader = new UltraSim.Network.BitReader(datagram.EnvelopePayloadSpan);
            ChatSubmitPacket packet = ChatSubmitPacket.Deserialize(ref reader);
            _networkEntityIdByConnection.TryGetValue(datagram.ConnectionId, out uint networkEntityId);
            bool hasSelectedCharacter = _selectedCharactersByConnection.TryGetValue(
                datagram.ConnectionId,
                out ServerSelectedCharacterSession? selectedCharacter);
            string displayName = hasSelectedCharacter
                ? selectedCharacter!.CharacterName
                : $"Player {datagram.ConnectionId.Value}";
            int accessLevel = 0;
            IReadOnlyList<string>? accessRuleKeys = null;
            IReadOnlyList<string>? directPermissionGrants = null;
            IReadOnlyList<string>? directPermissionDenies = null;
            if (hasSelectedCharacter &&
                _getAccountAccessMetadata?.Invoke(selectedCharacter!.AccountId, out ServerAccountRecord account) == true)
            {
                accessLevel = account.AccessLevel;
                accessRuleKeys = account.AccessRuleKeys;
                directPermissionGrants = account.DirectPermissionGrants;
                directPermissionDenies = account.DirectPermissionDenies;
            }

            var sender = new ServerChatSenderContext(
                sessionId.Value,
                datagram.ConnectionId.Value,
                networkEntityId,
                displayName,
                accessLevel,
                accessRuleKeys,
                directPermissionGrants,
                directPermissionDenies);

            _chat.TryHandleSubmit(
                sender,
                packet.Text,
                ServerChatScopes.System,
                nowTicks,
                serverTick);
        }
    }

    private static string ResolveClientMobileDefinitionId(ServerCharacterRecord character)
    {
        return character.BiologicalSex.Equals("female", StringComparison.OrdinalIgnoreCase)
            ? "mobile.x_bot"
            : "mobile.y_bot";
    }

    private void PublishSelectedEntityPresentations()
    {
        foreach (KeyValuePair<ConnectionId, ServerSelectedCharacterSession> pair in _selectedCharactersByConnection)
        {
            if (!_networkEntityIdByConnection.TryGetValue(pair.Key, out uint networkEntityId))
            {
                continue;
            }

            ServerSelectedCharacterSession selected = pair.Value;
            PublishEntityPresentation(
                networkEntityId,
                selected.CharacterName,
                selected.PresentationDefinitionId);
        }
    }

    private void PublishEntityPresentation(uint networkEntityId, string displayName, string definitionId)
    {
        if (networkEntityId == 0 || string.IsNullOrWhiteSpace(displayName))
        {
            return;
        }

        var packet = new EntityPresentationEventPacket(
            networkEntityId,
            displayName.Trim(),
            definitionId,
            ServerTick: 0);
        _worldPublisher.OrderedEventStream.Publish(
            ServerChatScopes.System,
            PacketClass.ReliableEvent,
            PacketCodec.Encode(packet));
    }

    private void ProcessUiActionInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.PacketId != UiActionRequestPacket.PacketId ||
                datagram.Plane != UiActionRequestPacket.Plane ||
                datagram.PacketClass != UiActionRequestPacket.PacketClass)
            {
                continue;
            }

            if (!_control.Sessions.TryGetSessionIdByConnection(datagram.ConnectionId, out SessionId sessionId) ||
                !_control.Sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot) ||
                snapshot.State != ConnectionSessionState.Ready)
            {
                continue;
            }

            _ui.TryProcessAction(datagram.ConnectionId, datagram.EnvelopePayloadSpan, out _);
        }
    }

    private void ProcessLoginCredentialInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        if (_scriptBinding is null)
            return;

        for (int i = 0; i < inbound.Length; i++)
        {
            ref readonly InboundDatagram datagram = ref inbound[i];
            if (datagram.PacketId != LoginCredentialSubmitPacket.PacketId ||
                datagram.Plane != LoginCredentialSubmitPacket.Plane ||
                datagram.PacketClass != LoginCredentialSubmitPacket.PacketClass)
            {
                continue;
            }

            if (!_control.Sessions.TryGetSessionIdByConnection(datagram.ConnectionId, out SessionId sessionId) ||
                !_control.Sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot) ||
                snapshot.State != ConnectionSessionState.Ready)
            {
                continue;
            }

            var reader = new UltraSim.Network.BitReader(datagram.EnvelopePayloadSpan);
            LoginCredentialSubmitPacket packet = LoginCredentialSubmitPacket.Deserialize(ref reader);
            try
            {
                _scriptBinding.Admission.ProcessCredential(
                    datagram.ConnectionId,
                    new AdmissionCredentialRequest(
                        packet.PanelInstanceId,
                        packet.ActionId,
                        packet.Username,
                        packet.MaterialKind,
                        packet.MaterialBytes));
            }
            finally
            {
                Array.Clear(packet.MaterialBytes);
            }
        }
    }

    private void ProcessPendingAdmissionStarts()
    {
        if (_scriptBinding is null || _pendingAdmissionStarts.Count == 0)
            return;

        foreach (ConnectionId connectionId in _pendingAdmissionStarts.ToArray())
        {
            _pendingAdmissionStarts.Remove(connectionId);
            if (_boundConnections.Contains(connectionId))
                _scriptBinding.Admission.TryStart(connectionId, out _);
        }
    }

    public bool TryGetLastReliableEventAck(ConnectionId connectionId, out ulong sequence)
    {
        return _sessionRuntime.ReliableEventAcks.TryGetLastAcknowledged(connectionId, out sequence);
    }

    public bool TryGetLastStateBaselineAck(ConnectionId connectionId, out StateBaselineDescriptor descriptor)
    {
        return _sessionRuntime.BaselineAcks.TryGetLastAcknowledged(connectionId, out descriptor);
    }

    private static void DisposeInbound(ReadOnlySpan<InboundDatagram> inbound)
    {
        for (int i = 0; i < inbound.Length; i++)
            inbound[i].Dispose();
    }

    private uint ReserveNetworkEntityId(ConnectionId connectionId)
    {
        uint networkEntityId = _allocateNetworkEntityId?.Invoke(connectionId) ?? _nextAdmissionNetworkEntityId++;
        if (networkEntityId == 0)
            throw new InvalidOperationException("Reserved network entity id cannot be zero.");
        return networkEntityId;
    }
}
