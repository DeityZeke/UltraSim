using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using UltraSim.Discovery;
using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Network.Protocol;
using UltraSim.Server.Engines.Networking;
using UltraSim.Server.Gameplay;
using UltraSim.Server.Chat;
using UltraSim.Shared.Gameplay;
using UltraSim.Server.Engines.Accounting;
using UltraSim.Server.UI;
using UltraSim.UI.Contracts;
using UltraSim.UI.Contracts.Bundled;

namespace UltraSim.Server
{
    public sealed class ServerRuntime
    {
        public readonly struct RuntimeTimingSnapshot
        {
            public RuntimeTimingSnapshot(
                long worldTickCalls,
                long worldTickElapsedTicksTotal,
                long worldTickElapsedTicksMax,
                long worldTickAllocatedBytesTotal,
                long worldTickAllocatedBytesMax,
                long replicationCalls,
                long replicationElapsedTicksTotal,
                long replicationElapsedTicksMax,
                long replicationAllocatedBytesTotal,
                long replicationAllocatedBytesMax,
                long chatFlushCalls,
                long chatFlushElapsedTicksTotal,
                long chatFlushElapsedTicksMax,
                long chatFlushAllocatedBytesTotal,
                long chatFlushAllocatedBytesMax)
            {
                WorldTickCalls = worldTickCalls;
                WorldTickElapsedTicksTotal = worldTickElapsedTicksTotal;
                WorldTickElapsedTicksMax = worldTickElapsedTicksMax;
                WorldTickAllocatedBytesTotal = worldTickAllocatedBytesTotal;
                WorldTickAllocatedBytesMax = worldTickAllocatedBytesMax;
                ReplicationCalls = replicationCalls;
                ReplicationElapsedTicksTotal = replicationElapsedTicksTotal;
                ReplicationElapsedTicksMax = replicationElapsedTicksMax;
                ReplicationAllocatedBytesTotal = replicationAllocatedBytesTotal;
                ReplicationAllocatedBytesMax = replicationAllocatedBytesMax;
                ChatFlushCalls = chatFlushCalls;
                ChatFlushElapsedTicksTotal = chatFlushElapsedTicksTotal;
                ChatFlushElapsedTicksMax = chatFlushElapsedTicksMax;
                ChatFlushAllocatedBytesTotal = chatFlushAllocatedBytesTotal;
                ChatFlushAllocatedBytesMax = chatFlushAllocatedBytesMax;
            }

            public long WorldTickCalls { get; }
            public long WorldTickElapsedTicksTotal { get; }
            public long WorldTickElapsedTicksMax { get; }
            public long WorldTickAllocatedBytesTotal { get; }
            public long WorldTickAllocatedBytesMax { get; }
            public long ReplicationCalls { get; }
            public long ReplicationElapsedTicksTotal { get; }
            public long ReplicationElapsedTicksMax { get; }
            public long ReplicationAllocatedBytesTotal { get; }
            public long ReplicationAllocatedBytesMax { get; }
            public long ChatFlushCalls { get; }
            public long ChatFlushElapsedTicksTotal { get; }
            public long ChatFlushElapsedTicksMax { get; }
            public long ChatFlushAllocatedBytesTotal { get; }
            public long ChatFlushAllocatedBytesMax { get; }
        }

        private readonly ServerRuntimeHooks _hooks;
        private readonly IReadOnlyList<IServerDiscoveryPublisher> _discoveryPublishers;
        private readonly SessionLifecycleEvent[] _sessionLifecycleScratch = new SessionLifecycleEvent[64];
        private readonly InboundDatagram[] _inboundDatagramScratch = new InboundDatagram[256];
        private bool _transportStarted;
        private bool _discoveryPublished;
        private DateTimeOffset? _startedAtUtc;
        private int _peakSessions;
        private long _worldTickCalls;
        private long _worldTickElapsedTicksTotal;
        private long _worldTickElapsedTicksMax;
        private long _worldTickAllocatedBytesTotal;
        private long _worldTickAllocatedBytesMax;
        private long _replicationCalls;
        private long _replicationElapsedTicksTotal;
        private long _replicationElapsedTicksMax;
        private long _replicationAllocatedBytesTotal;
        private long _replicationAllocatedBytesMax;
        private long _chatFlushCalls;
        private long _chatFlushElapsedTicksTotal;
        private long _chatFlushElapsedTicksMax;
        private long _chatFlushAllocatedBytesTotal;
        private long _chatFlushAllocatedBytesMax;
        private double _autoSaveAccumulatorSeconds;
        // Set by RequestSave(); drained at the end-of-tick save barrier in Tick().
        private volatile bool _saveRequested;
        private readonly object _asyncSaveLock = new();
        private Task? _asyncSaveWriteTask;
        private readonly Dictionary<long, ushort?> _pendingSpawnPrefabIdsBySession = new();
        private readonly Dictionary<long, string> _pendingSpawnDisplayNamesBySession = new();
        private readonly Dictionary<long, PlayerCharacterComponent> _pendingSpawnCharactersBySession = new();
        private readonly Dictionary<long, PlayerSpawnSnapshot> _pendingSpawnSnapshotsBySession = new();

        public ServerRuntime(
            INetworkTransport transport,
            ServerRuntimeOptions? options = null,
            NetworkSessionManager? sessions = null,
            ServerEventHub? events = null,
            ServerRuntimeHooks? hooks = null,
            IReadOnlyList<IServerDiscoveryPublisher>? discoveryPublishers = null)
        {
            Transport = transport ?? throw new ArgumentNullException(nameof(transport));
            Options = options ?? ServerRuntimeOptions.Default;
            Sessions = sessions ?? new NetworkSessionManager();
            Events = events ?? new ServerEventHub();
            _hooks = hooks ?? new ServerRuntimeHooks();
            if ((_hooks.CaptureSaveSnapshot is null) != (_hooks.WriteSaveSnapshot is null))
                throw new ArgumentException("CaptureSaveSnapshot and WriteSaveSnapshot must either both be set or both be null.", nameof(hooks));
            _discoveryPublishers = discoveryPublishers ?? Array.Empty<IServerDiscoveryPublisher>();
            NetworkControl = new ServerNetworkControlService(Transport, Sessions);
            NetworkControl.ReturnToCharacterSelectRequested = sessionId => Events.PublishReturnToCharacterSelectRequested(sessionId);
            PlayerWorld = new ServerPlayerSessionWorld(
                Options.PlayerSpawnSpacingGrid,
                Options.PlayerSpawnWrapCount,
                ServerPlayerArchetypeCatalog.CreateDefault(Options.DefaultPlayerArchetypeId));
            PlayerReplication = new ServerPlayerReplicationService(
                PlayerWorld,
                Transport,
                Sessions,
                Options.EnablePlayerChunkAoi,
                Options.PlayerChunkSizeGridUnits,
                Options.PlayerChunkAoiRadius,
                Options.EnablePlayerRecipientSelectorFastPath,
                Options.EnablePlayerRecipientSelectorChunkGroupCacheKey,
                Options.EnablePlayerPriorityLod,
                Options.PlayerPriorityLodNearCadenceTicks,
                Options.PlayerPriorityLodOuterCadenceTicks,
                Options.PlayerPriorityLodRing1MaxChunks,
                Options.PlayerPriorityLodFarCadenceTicks,
                Options.EnablePlayerDenseObserverCadence,
                Options.PlayerDenseObserverCadenceStartSubscribers,
                Options.PlayerDenseObserverCadenceStepSubscribers,
                Options.PlayerDenseObserverCadenceMaxTicks,
                Options.EnablePlayerDenseObserverRecipientCap,
                Options.PlayerDenseObserverRecipientCapStartSubscribers,
                Options.PlayerDenseObserverRecipientCapMaxRecipients,
                Options.PlayerDenseObserverRecipientPriorityChunkRadius,
                Options.PlayerAoiDespawnGraceTicks,
                Options.EnablePlayerDualCorrectionStreams,
                Options.EnablePlayerCorrectionBatching,
                Options.EnablePlayerCorrectionDeadband,
                Options.PlayerCorrectionDeadbandUnits,
                Options.PlayerCorrectionMaxSilentTicks,
                Options.PlayerReplicationBuildBatchPacketLimit,
                Options.MaxReplicationBuildWorkers,
                Options.PlayerObserverStreamCadenceTicks,
                Options.EnableObserverSpatialFirstBuild);
            Chat = new ServerChatService(
                Transport,
                Sessions,
                displayNameResolver: null,
                senderContextResolver: ResolveChatSenderContext,
                localRecipientCollector: PlayerReplication.CollectLocalChatRecipients,
                commandRegistry: ServerChatCommandBootstrap.CreateDefault(),
                options: new ServerChatOptions(
                    MinTicksBetweenMessages: Options.ChatMinTicksBetweenMessages,
                    EnableCommands: Options.EnableChatCommands,
                    EnableDenseLocalChatRecipientCap: Options.EnableDenseLocalChatRecipientCap,
                    DenseLocalChatCapStartRecipients: Options.DenseLocalChatCapStartRecipients,
                    DenseLocalChatCapMaxRecipients: Options.DenseLocalChatCapMaxRecipients,
                    EnableDenseLocalChatRateCap: Options.EnableDenseLocalChatRateCap,
                    DenseLocalChatRateCapStartRecipients: Options.DenseLocalChatRateCapStartRecipients,
                    DenseLocalChatRateCapMinTicks: Options.DenseLocalChatRateCapMinTicks,
                    EnableBatching: Options.EnableChatBatching));
            UiPanels = new ServerUiPanelService(Transport, Sessions);
            UiInbound = new ServerUiInboundDatagramHandler(UiPanels);
            UiPanels.PanelAckReceived += HandlePanelAckReceived;
            UiPanels.PanelResponseReceived += HandlePanelResponseReceived;
            Accounts = new ServerAccountService(
                new ServerAccountRegistry(),
                ServerAccountOptions.Default,
                Options.AccountAuthenticationProvider);
            State = ServerRuntimeState.Created;
        }

        public ServerRuntimeOptions Options { get; }
        public INetworkTransport Transport { get; }
        public NetworkSessionManager Sessions { get; }
        public ServerNetworkControlService NetworkControl { get; }
        public ServerChatService Chat { get; }
        public ServerUiPanelService UiPanels { get; }
        public ServerUiInboundDatagramHandler UiInbound { get; }
        public ServerAccountService Accounts { get; }
        public ServerPlayerSessionWorld PlayerWorld { get; }
        public ServerPlayerReplicationService PlayerReplication { get; }
        public ServerEventHub Events { get; }
        public ServerRuntimeState State { get; private set; }
        public IReadOnlyList<IServerDiscoveryPublisher> DiscoveryPublishers => _discoveryPublishers;
        public RuntimeTimingSnapshot GetTimingSnapshot() =>
            new RuntimeTimingSnapshot(
                _worldTickCalls,
                _worldTickElapsedTicksTotal,
                _worldTickElapsedTicksMax,
                _worldTickAllocatedBytesTotal,
                _worldTickAllocatedBytesMax,
                _replicationCalls,
                _replicationElapsedTicksTotal,
                _replicationElapsedTicksMax,
                _replicationAllocatedBytesTotal,
                _replicationAllocatedBytesMax,
                _chatFlushCalls,
                _chatFlushElapsedTicksTotal,
                _chatFlushElapsedTicksMax,
                _chatFlushAllocatedBytesTotal,
                _chatFlushAllocatedBytesMax);

        public bool IsInitialized =>
            State != ServerRuntimeState.Created && State != ServerRuntimeState.Faulted;

        public bool IsRunning => State == ServerRuntimeState.Running;
        public bool IsFaulted => State == ServerRuntimeState.Faulted;
        public bool IsAcceptingConnections => _transportStarted && State == ServerRuntimeState.Running;

        public void SetPendingSessionSpawnPrefabId(long sessionId, ushort? prefabId)
        {
            if (prefabId.HasValue)
                _pendingSpawnPrefabIdsBySession[sessionId] = prefabId.Value;
            else
                _pendingSpawnPrefabIdsBySession.Remove(sessionId);
        }

        public void SetPendingSessionSpawnDisplayName(long sessionId, string? displayName)
        {
            if (string.IsNullOrWhiteSpace(displayName))
            {
                _pendingSpawnDisplayNamesBySession.Remove(sessionId);
                return;
            }

            _pendingSpawnDisplayNamesBySession[sessionId] = displayName.Trim();
        }

        public void SetPendingSessionSpawnCharacter(long sessionId, PlayerCharacterComponent? character)
        {
            if (!character.HasValue || character.Value.CharacterId == 0)
            {
                _pendingSpawnCharactersBySession.Remove(sessionId);
                return;
            }

            _pendingSpawnCharactersBySession[sessionId] = character.Value;
        }

        public void SetPendingSessionSpawnSnapshot(long sessionId, PlayerSpawnSnapshot? snapshot)
        {
            if (!snapshot.HasValue)
            {
                _pendingSpawnSnapshotsBySession.Remove(sessionId);
                return;
            }

            _pendingSpawnSnapshotsBySession[sessionId] = snapshot.Value;
        }

        public bool TryReturnSessionToAuthenticated(long sessionId, long nowTicks)
        {
            if (!Sessions.TryReturnToAuthenticated(sessionId, nowTicks))
                return false;

            DrainSessionLifecycleEvents();
            return true;
        }

        public ServerAdvertisedInfo GetAdvertisedInfo()
        {
            string[] tags = Options.PublicTags ?? Array.Empty<string>();

            return new ServerAdvertisedInfo(
                ServerId: string.IsNullOrWhiteSpace(Options.PublicServerId) ? Options.ProfileName : Options.PublicServerId,
                DisplayName: string.IsNullOrWhiteSpace(Options.PublicDisplayName) ? Options.ProfileName : Options.PublicDisplayName,
                ConnectHint: Options.PublicConnectHint,
                ConnectTargetKind: ConnectTargetKind.Direct,
                CompatibilityId: Options.PublicCompatibilityId,
                ProtocolVersion: Options.PublicProtocolVersion,
                ContentVersion: Options.PublicContentVersion,
                StatusEndpointUrl: Options.PublicStatusEndpointUrl,
                PasswordRequired: Options.PasswordRequired,
                WhitelistOnly: Options.WhitelistOnly,
                Region: Options.PublicRegion,
                Description: Options.PublicDescription,
                IsPublic: Options.PublicVisible,
                Tags: tags,
                PrimaryLanguage: Options.PublicPrimaryLanguage,
                Languages: Options.PublicLanguages,
                WebsiteUrl: Options.PublicWebsiteUrl,
                DiscordUrl: Options.PublicDiscordUrl,
                MaxUsers: Options.PublicMaxUsers,
                Visibility: Options.PublicVisibility,
                CapabilityFlags: Options.PublicCapabilityFlags);
        }

        public ServerRuntimeStatusSnapshot GetStatusSnapshot(DateTimeOffset? nowUtc = null)
        {
            int currentSessions = Sessions.Count;
            if (currentSessions > _peakSessions)
                _peakSessions = currentSessions;

            DateTimeOffset? startedAt = _startedAtUtc;
            DateTimeOffset resolvedNow = nowUtc ?? DateTimeOffset.UtcNow;
            TimeSpan uptime = startedAt.HasValue && resolvedNow >= startedAt.Value
                ? resolvedNow - startedAt.Value
                : TimeSpan.Zero;

            return new ServerRuntimeStatusSnapshot(
                Options.ProfileName,
                State,
                IsAcceptingConnections,
                currentSessions,
                _peakSessions,
                startedAt,
                uptime,
                Options.SaveDirectory);
        }

        public ServerDiscoveryStatusInfo GetDiscoveryStatusInfo(DateTimeOffset? nowUtc = null)
        {
            ServerRuntimeStatusSnapshot snapshot = GetStatusSnapshot(nowUtc);

            return new ServerDiscoveryStatusInfo(
                IsAcceptingConnections: snapshot.IsAcceptingConnections,
                CurrentUsers: snapshot.CurrentSessions,
                PeakUsers: snapshot.PeakSessions,
                Uptime: snapshot.Uptime,
                Status: snapshot.State.ToString());
        }

        public bool PublishDiscoverySnapshot()
        {
            if (!ShouldPublishDiscovery())
                return false;

            ServerAdvertisedInfo advertisedInfo = GetAdvertisedInfo();
            ServerDiscoveryStatusInfo statusInfo = GetDiscoveryStatusInfo();

            for (int i = 0; i < _discoveryPublishers.Count; i++)
            {
                try
                {
                    _discoveryPublishers[i]
                        .PublishAsync(advertisedInfo, statusInfo)
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {
                    // Discovery is best-effort — registry errors must not fault the server lifecycle.
                }
            }

            _discoveryPublished = true;
            return true;
        }

        public bool UnpublishDiscovery()
        {
            if (!_discoveryPublished || _discoveryPublishers.Count == 0)
                return false;

            string serverId = GetAdvertisedInfo().ServerId;

            for (int i = 0; i < _discoveryPublishers.Count; i++)
            {
                try
                {
                    _discoveryPublishers[i]
                        .UnpublishAsync(serverId)
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {
                    // Discovery is best-effort — registry errors must not fault the server lifecycle.
                }
            }

            _discoveryPublished = false;
            return true;
        }

        public void SendPresence(ServerDiscoveryPresenceSignal signal)
        {
            if (!_discoveryPublished || _discoveryPublishers.Count == 0)
                return;

            ServerDiscoveryStatusInfo statusInfo = GetDiscoveryStatusInfo();

            for (int i = 0; i < _discoveryPublishers.Count; i++)
            {
                try
                {
                    _discoveryPublishers[i]
                        .SendPresenceAsync(signal, statusInfo)
                        .GetAwaiter()
                        .GetResult();
                }
                catch
                {
                    // Discovery is best-effort — registry errors must not fault the server lifecycle.
                }
            }

            if (signal == ServerDiscoveryPresenceSignal.Shutdown || signal == ServerDiscoveryPresenceSignal.Offline)
                _discoveryPublished = false;
        }

        public UiPanelInstanceId OpenStatusPanel(long sessionId, ushort definitionRevision = 1)
        {
            ServerRuntimeStatusSnapshot snapshot = GetStatusSnapshot();
            string body =
                $"Profile: {snapshot.ProfileName}\n" +
                $"State: {snapshot.State}\n" +
                $"Accepting connections: {(snapshot.IsAcceptingConnections ? "Yes" : "No")}\n" +
                $"Current users: {snapshot.CurrentSessions}\n" +
                $"Peak users: {snapshot.PeakSessions}\n" +
                $"Uptime: {FormatUptime(snapshot.Uptime)}";

            return UiPanels.OpenMessageBox(
                sessionId,
                title: "Server Status",
                body: body,
                buttons: new[]
                {
                    new MessageBoxButtonDefinition(0, "Close", IsDefault: true, IsCancel: true),
                },
                definitionRevision: definitionRevision,
                presentationMode: UiPanelPresentationMode.ExclusiveMenuOnly,
                flags: UiPanelFlags.BringToFront | UiPanelFlags.HasCancelAction,
                closeOnResponse: true);
        }

        public void Initialize()
        {
            EnsureState(nameof(Initialize), ServerRuntimeState.Created);

            ExecuteLifecycleOperation(nameof(Initialize), () =>
            {
                TransitionTo(ServerRuntimeState.Initializing);
                _hooks.Initialize?.Invoke(this);
                TransitionTo(ServerRuntimeState.Initialized);
            });
        }

        public void Start()
        {
            if (State == ServerRuntimeState.Created)
                Initialize();

            EnsureState(nameof(Start), ServerRuntimeState.Initialized, ServerRuntimeState.Stopped);

            ExecuteLifecycleOperation(nameof(Start), () =>
            {
                TransitionTo(ServerRuntimeState.Starting);
                _hooks.Start?.Invoke(this);

                TransitionTo(ServerRuntimeState.Loading);
                _hooks.Load?.Invoke(this);

                if (Options.StartTransportOnStart && !_transportStarted)
                {
                    Transport.Start();
                    _transportStarted = true;
                }

                _startedAtUtc = DateTimeOffset.UtcNow;
                _peakSessions = Sessions.Count;
                _autoSaveAccumulatorSeconds = 0d;
                TransitionTo(ServerRuntimeState.Running);
                PublishDiscoverySnapshot();
                SendPresence(ServerDiscoveryPresenceSignal.Startup);
            });
        }

        public void Tick(float deltaSeconds)
        {
            EnsureState(nameof(Tick), ServerRuntimeState.Running);

            ExecuteLifecycleOperation(nameof(Tick), () =>
            {
                // Intended stable ordering:
                // 1. poll/decode inbound network
                // 2. apply inbound pre-barrier commands
                // 3. tick world/ECS
                // 4. build/send replication and other outbound traffic
                // 5. flush outbound transport
                // 6. publish metrics/snapshots
                DrainSessionLifecycleEvents();
                long worldTickStart = Stopwatch.GetTimestamp();
                long worldTickAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                PlayerWorld.Tick(deltaSeconds);
                RecordAllocatedBytes(worldTickAllocatedBefore, ref _worldTickAllocatedBytesTotal, ref _worldTickAllocatedBytesMax);
                RecordElapsed(worldTickStart, ref _worldTickCalls, ref _worldTickElapsedTicksTotal, ref _worldTickElapsedTicksMax);

                long replicationStart = Stopwatch.GetTimestamp();
                long replicationAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                PlayerReplication.ReplicateTick(PlayerWorld.World.WorldTick);
                RecordAllocatedBytes(replicationAllocatedBefore, ref _replicationAllocatedBytesTotal, ref _replicationAllocatedBytesMax);
                RecordElapsed(replicationStart, ref _replicationCalls, ref _replicationElapsedTicksTotal, ref _replicationElapsedTicksMax);

                long chatFlushStart = Stopwatch.GetTimestamp();
                long chatFlushAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                Chat.FlushPending();
                RecordAllocatedBytes(chatFlushAllocatedBefore, ref _chatFlushAllocatedBytesTotal, ref _chatFlushAllocatedBytesMax);
                RecordElapsed(chatFlushStart, ref _chatFlushCalls, ref _chatFlushElapsedTicksTotal, ref _chatFlushElapsedTicksMax);
                GetStatusSnapshot();
                _hooks.Tick?.Invoke(this, deltaSeconds);

                if (State == ServerRuntimeState.Running)
                {
                    TryAutoSave(deltaSeconds);
                    CompleteAsyncSaveWriteIfFinished();

                    // End-of-tick save barrier: drain any pending save request.
                    // Save capture executes here after the full tick body, so ECS state is
                    // quiescent and no structural mutation can race with the serializer.
                    if (_saveRequested && !IsAsyncSaveWriteInProgress())
                    {
                        _saveRequested = false;

                        if (HasAsyncSavePipeline())
                            CaptureAndQueueDeferredSaveSnapshot();
                        else
                            Save();
                    }
                }
            });
        }

        /// <summary>
        /// Schedules a save to execute at the end-of-tick barrier.
        /// Safe to call at any point; the save executes after the current (or next)
        /// <see cref="Tick"/> body completes, before the following <see cref="Tick"/> call begins.
        /// </summary>
        public void RequestSave() => _saveRequested = true;

        public bool Save()
        {
            if (State == ServerRuntimeState.Saving)
                return false;

            if (State != ServerRuntimeState.Running && State != ServerRuntimeState.Stopping)
                throw new InvalidOperationException($"Save is not valid while runtime state is {State}.");

            ServerRuntimeState resumeState = State;
            ExecuteLifecycleOperation(nameof(Save), () =>
            {
                WaitForAsyncSaveWriteCompletion();
                TransitionTo(ServerRuntimeState.Saving);
                _saveRequested = false;
                PrepareSaveBarrierState();
                _hooks.Save?.Invoke(this);

                if (HasAsyncSavePipeline())
                {
                    IServerRuntimeSaveSnapshot? snapshot = _hooks.CaptureSaveSnapshot?.Invoke(this);
                    if (snapshot is not null)
                        _hooks.WriteSaveSnapshot!(snapshot);
                }

                _autoSaveAccumulatorSeconds = 0d;
                TransitionTo(resumeState);
            });
            return State != ServerRuntimeState.Faulted;
        }

        public int ProcessInboundTransportDatagrams(long nowTicks, int maxDatagrams = 64)
        {
            if (maxDatagrams <= 0)
                return 0;

            int handled = 0;
            int batchSize = Math.Min(maxDatagrams, _inboundDatagramScratch.Length);

            while (handled < maxDatagrams)
            {
                int remaining = Math.Min(batchSize, maxDatagrams - handled);
                int polled = Transport.PollInbound(_inboundDatagramScratch.AsSpan(0, remaining));
                if (polled <= 0)
                    break;

                try
                {
                    for (int i = 0; i < polled; i++)
                    {
                        ref readonly InboundDatagram datagram = ref _inboundDatagramScratch[i];
                        if (!Sessions.TryGetSessionIdByConnection(datagram.ConnectionId, out long sessionId))
                            continue;

                        if (TryHandleControlDatagram(sessionId, in datagram, nowTicks))
                            handled++;
                    }
                }
                finally
                {
                    Transport.CompleteInboundConsume();
                }

                if (polled < remaining)
                    break;
            }

            return handled;
        }

        public bool TryHandleControlDatagram(long sessionId, in InboundDatagram datagram, long nowTicks)
        {
            if (NetworkControl.TryHandleInboundDatagram(sessionId, in datagram))
                return true;

            if (datagram.Channel == ChannelType.ReliableOrdered)
            {
                if (UiInbound.TryHandle(sessionId, datagram.Payload.Span))
                    return true;

                if (Chat.TryHandleInboundDatagram(sessionId, in datagram, nowTicks))
                    return true;
            }

            if (!Sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot) ||
                snapshot.State != SessionState.Ready)
            {
                return false;
            }

            return PlayerWorld.TryHandleInboundDatagram(sessionId, in datagram);
        }

        public void Stop()
        {
            if (State == ServerRuntimeState.Stopped)
                return;

            if (State == ServerRuntimeState.Created)
            {
                TransitionTo(ServerRuntimeState.Stopped);
                return;
            }

            EnsureState(nameof(Stop), ServerRuntimeState.Initialized, ServerRuntimeState.Running, ServerRuntimeState.Faulted);

            ExecuteLifecycleOperation(nameof(Stop), () =>
            {
                TransitionTo(ServerRuntimeState.Stopping);

                if (Options.SaveOnStop)
                    Save();

                WaitForAsyncSaveWriteCompletion();

                Sessions.DisconnectAll(SessionDisconnectReason.ServerShutdown, nowTicks: 0, removeDisconnected: true);
                DrainSessionLifecycleEvents();
                Chat.Reset();
                PlayerWorld.ResetWorld();
                PlayerReplication.Reset();
                ResetTimingCounters();
                _autoSaveAccumulatorSeconds = 0d;

                if (Options.StopTransportOnStop && _transportStarted)
                {
                    Transport.Stop();
                    _transportStarted = false;
                }

                SendPresence(ServerDiscoveryPresenceSignal.Shutdown);
                _hooks.Stop?.Invoke(this);
                TransitionTo(ServerRuntimeState.Stopped);
            });
        }

        public void Restart()
        {
            EnsureState(nameof(Restart), ServerRuntimeState.Initialized, ServerRuntimeState.Running, ServerRuntimeState.Stopped, ServerRuntimeState.Faulted);

            ExecuteLifecycleOperation(nameof(Restart), () =>
            {
                TransitionTo(ServerRuntimeState.Restarting);
                StopFromRestart();
                StartFromRestart();
            });
        }

        private void StartFromRestart()
        {
            TransitionTo(ServerRuntimeState.Starting);
            _hooks.Start?.Invoke(this);

            TransitionTo(ServerRuntimeState.Loading);
            _hooks.Load?.Invoke(this);

            if (Options.StartTransportOnStart && !_transportStarted)
            {
                Transport.Start();
                _transportStarted = true;
            }

            _startedAtUtc = DateTimeOffset.UtcNow;
            _peakSessions = Sessions.Count;
            _autoSaveAccumulatorSeconds = 0d;
            TransitionTo(ServerRuntimeState.Running);
            PublishDiscoverySnapshot();
            SendPresence(ServerDiscoveryPresenceSignal.Startup);
        }

        private void StopFromRestart()
        {
            if (State != ServerRuntimeState.Restarting && State != ServerRuntimeState.Running && State != ServerRuntimeState.Initialized && State != ServerRuntimeState.Faulted)
                return;

            Sessions.DisconnectAll(SessionDisconnectReason.ServerShutdown, nowTicks: 0, removeDisconnected: true);
            DrainSessionLifecycleEvents();
            PlayerWorld.ResetWorld();
            PlayerReplication.Reset();
            Chat.Reset();
            ResetTimingCounters();
            _autoSaveAccumulatorSeconds = 0d;

            if (Options.StopTransportOnStop && _transportStarted)
            {
                Transport.Stop();
                _transportStarted = false;
            }

            SendPresence(ServerDiscoveryPresenceSignal.Shutdown);
            _hooks.Stop?.Invoke(this);
            TransitionTo(ServerRuntimeState.Stopped);
        }

        private void ExecuteLifecycleOperation(string operation, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                Fault(operation, ex);
                throw;
            }
        }

        private void Fault(string operation, Exception exception)
        {
            ServerRuntimeFault fault = new(State, operation, exception, DateTimeOffset.UtcNow);
            TransitionTo(ServerRuntimeState.Faulted);
            Events.RaiseFaulted(fault);
        }

        private void TransitionTo(ServerRuntimeState nextState)
        {
            if (State == nextState)
                return;

            ServerRuntimeState previousState = State;
            State = nextState;
            Events.RaiseStateChanged(previousState, nextState);
        }

        private void EnsureState(string operation, params ServerRuntimeState[] validStates)
        {
            for (int i = 0; i < validStates.Length; i++)
            {
                if (State == validStates[i])
                    return;
            }

            throw new InvalidOperationException($"{operation} is not valid while runtime state is {State}.");
        }

        private void HandlePanelAckReceived(long sessionId, UiPanelAck ack) =>
            Events.RaisePanelAckReceived(sessionId, ack);

        private void HandlePanelResponseReceived(long sessionId, UiPanelResponse response) =>
            Events.RaisePanelResponseReceived(sessionId, response);

        private void DrainSessionLifecycleEvents()
        {
            while (true)
            {
                int drained = Sessions.DrainLifecycleEvents(_sessionLifecycleScratch);
                if (drained <= 0)
                    return;

                for (int i = 0; i < drained; i++)
                    HandleSessionLifecycleEvent(_sessionLifecycleScratch[i]);
            }
        }

        private void HandleSessionLifecycleEvent(in SessionLifecycleEvent evt)
        {
            switch (evt.Kind)
            {
                case SessionLifecycleEventKind.Created:
                    break;

                case SessionLifecycleEventKind.StateChanged:
                    if (evt.CurrentState == SessionState.Ready)
                    {
                        _pendingSpawnPrefabIdsBySession.TryGetValue(evt.SessionId, out ushort? requestedPrefabId);
                        _pendingSpawnDisplayNamesBySession.TryGetValue(evt.SessionId, out string? playerDisplayName);
                        _pendingSpawnCharactersBySession.TryGetValue(evt.SessionId, out PlayerCharacterComponent playerCharacter);
                        PlayerSpawnSnapshot? spawnSnapshot = _pendingSpawnSnapshotsBySession.TryGetValue(evt.SessionId, out PlayerSpawnSnapshot rawSnapshot)
                            ? rawSnapshot
                            : null;
                        PlayerWorld.BindSession(
                            evt.SessionId,
                            evt.ConnectionId,
                            requestedPrefabId,
                            playerDisplayName,
                            playerCharacter.CharacterId == 0 ? null : playerCharacter,
                            spawnSnapshot);
                        PlayerReplication.TrackConnection(evt.ConnectionId, PlayerWorld.World.WorldTick);
                        PlayerReplication.MarkAllPlayersDirty();
                        Events.PublishClientConnected(evt.SessionId);
                        Events.PublishSessionReady(evt.SessionId);
                        PublishReadyPlayerNameplates();
                    }

                    if (evt.PreviousState == SessionState.Ready &&
                        evt.CurrentState == SessionState.Authenticated)
                    {
                        SnapshotPersistentCharacterState(evt.SessionId);
                        _pendingSpawnPrefabIdsBySession.Remove(evt.SessionId);
                        _pendingSpawnDisplayNamesBySession.Remove(evt.SessionId);
                        _pendingSpawnCharactersBySession.Remove(evt.SessionId);
                        _pendingSpawnSnapshotsBySession.Remove(evt.SessionId);
                        PlayerReplication.HandleSessionDisconnected(evt.SessionId, evt.ConnectionId);
                        Chat.HandleSessionDisconnected(evt.SessionId, evt.ConnectionId);
                        PlayerWorld.UnbindSession(evt.SessionId);
                    }

                    if (evt.CurrentState == SessionState.Disconnected)
                    {
                        SnapshotPersistentCharacterState(evt.SessionId);
                        _pendingSpawnPrefabIdsBySession.Remove(evt.SessionId);
                        _pendingSpawnDisplayNamesBySession.Remove(evt.SessionId);
                        _pendingSpawnCharactersBySession.Remove(evt.SessionId);
                        _pendingSpawnSnapshotsBySession.Remove(evt.SessionId);
                        PlayerReplication.HandleSessionDisconnected(evt.SessionId, evt.ConnectionId);
                        Chat.HandleSessionDisconnected(evt.SessionId, evt.ConnectionId);
                        PlayerWorld.UnbindSession(evt.SessionId);
                        Events.PublishClientDisconnected(evt.SessionId, evt.DisconnectReason);
                    }
                    break;
            }
        }

        private ServerChatService.SenderContext? ResolveChatSenderContext(long sessionId)
        {
            if (!PlayerWorld.TryGetEntityForSession(sessionId, out Entity entity) ||
                !PlayerWorld.World.TryGetComponent(entity, out PlayerState state))
            {
                return null;
            }

            ulong senderEntityId = PlayerReplication.Bridge.EntityMap.GetOrAssignNetworkEntityId(entity).Value;

            string senderName;
            if (PlayerWorld.World.TryGetComponent(entity, out PlayerNameComponent playerName))
            {
                string resolvedName = playerName.GetName();
                senderName = string.IsNullOrWhiteSpace(resolvedName)
                    ? $"Player {state.OwnerConnectionId}"
                    : resolvedName;
            }
            else if (PlayerWorld.TryGetPrefabIdForSession(sessionId, out PrefabIdComponent prefabComp))
            {
                PlayerArchetypeDefinition archetype = PlayerWorld.PlayerArchetypes.Resolve(prefabComp.PrefabId);
                senderName = $"{archetype.DisplayName} #{state.OwnerConnectionId}";
            }
            else
            {
                senderName = $"Player {state.OwnerConnectionId}";
            }

            return new ServerChatService.SenderContext(
                sessionId,
                state.OwnerConnectionId,
                senderEntityId,
                senderName,
                accessLevel: 0);
        }

        private void PublishReadyPlayerNameplates()
        {
            Dictionary<int, long> connections = Sessions.CopyConnectionSnapshot();
            if (connections.Count == 0)
                return;

            PlayerWorld.World.Query<PlayerState, PlayerNameComponent>((Entity entity, ref PlayerState state, ref PlayerNameComponent name) =>
            {
                if (state.OwnerConnectionId <= 0)
                    return;

                string primaryText = name.GetName();
                if (string.IsNullOrWhiteSpace(primaryText))
                    return;

                NetworkEntityId entityId = PlayerReplication.Bridge.EntityMap.GetOrAssignNetworkEntityId(entity);
                SendNameplateToReadyConnections(connections, entityId, primaryText);
            });
        }

        private void SendNameplateToReadyConnections(
            Dictionary<int, long> connections,
            NetworkEntityId entityId,
            string primaryText)
        {
            int payloadCapacity = NetworkControlMessageCodec.ServerNameplateHeaderBytes + NetworkControlMessageCodec.ServerNameplateMaxUtf8Bytes;
            byte[] payload = new byte[payloadCapacity];
            if (!NetworkControlMessageCodec.TryEncodeServerNameplate(payload, entityId, primaryText, out int written) || written <= 0)
                return;

            ReadOnlyMemory<byte> datagramPayload = new(payload, 0, written);
            foreach ((int connectionId, long sessionId) in connections)
            {
                if (!Sessions.TryGetSnapshot(sessionId, out SessionSnapshot snapshot) || snapshot.State != SessionState.Ready)
                    continue;

                Transport.EnqueueOutbound(new OutboundDatagram(
                    connectionId,
                    ChannelType.ReliableOrdered,
                    datagramPayload));
            }
        }

        private bool ShouldPublishDiscovery() =>
            Options.PublicVisible && _discoveryPublishers.Count > 0;

        private static string FormatUptime(TimeSpan uptime)
        {
            if (uptime < TimeSpan.Zero)
                uptime = TimeSpan.Zero;

            if (uptime.TotalDays >= 1.0)
                return $"{(int)uptime.TotalDays}d {uptime.Hours}h {uptime.Minutes}m";

            if (uptime.TotalHours >= 1.0)
                return $"{uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";

            if (uptime.TotalMinutes >= 1.0)
                return $"{uptime.Minutes}m {uptime.Seconds}s";

            return $"{Math.Max(0, uptime.Seconds)}s";
        }

        private void SnapshotPersistentCharacterState(long sessionId)
        {
            if (!PlayerWorld.TryGetEntityForSession(sessionId, out Entity entity) ||
                !PlayerWorld.World.TryGetComponent(entity, out PlayerCharacterComponent character) ||
                character.CharacterId == 0 ||
                !PlayerWorld.World.TryGetComponent(entity, out PlayerState state))
            {
                return;
            }

            ushort prefabId = Options.DefaultPlayerArchetypeId;
            if (PlayerWorld.World.TryGetComponent(entity, out PrefabIdComponent prefab))
                prefabId = prefab.PrefabId;

            Accounts.TryUpdateCharacterSnapshot(
                character.CharacterId,
                prefabId,
                state.X,
                state.Y,
                state.Z,
                state.YawRadians,
                out _);
        }

        private void ResetTimingCounters()
        {
            _worldTickCalls = 0;
            _worldTickElapsedTicksTotal = 0;
            _worldTickElapsedTicksMax = 0;
            _worldTickAllocatedBytesTotal = 0;
            _worldTickAllocatedBytesMax = 0;
            _replicationCalls = 0;
            _replicationElapsedTicksTotal = 0;
            _replicationElapsedTicksMax = 0;
            _replicationAllocatedBytesTotal = 0;
            _replicationAllocatedBytesMax = 0;
            _chatFlushCalls = 0;
            _chatFlushElapsedTicksTotal = 0;
            _chatFlushElapsedTicksMax = 0;
            _chatFlushAllocatedBytesTotal = 0;
            _chatFlushAllocatedBytesMax = 0;
        }

        private static void RecordElapsed(
            long startTimestamp,
            ref long callCount,
            ref long elapsedTicksTotal,
            ref long elapsedTicksMax)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            callCount++;
            elapsedTicksTotal += elapsedTicks;
            if (elapsedTicks > elapsedTicksMax)
                elapsedTicksMax = elapsedTicks;
        }

        private static void RecordAllocatedBytes(
            long allocatedBefore,
            ref long allocatedBytesTotal,
            ref long allocatedBytesMax)
        {
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
            allocatedBytesTotal += allocatedBytes;
            if (allocatedBytes > allocatedBytesMax)
                allocatedBytesMax = allocatedBytes;
        }

        private bool HasAsyncSavePipeline()
            => _hooks.CaptureSaveSnapshot is not null && _hooks.WriteSaveSnapshot is not null;

        private void PrepareSaveBarrierState()
        {
            CharacterSnapshotUpdate[] snapshots = PlayerWorld.CapturePersistentCharacterSnapshots();
            Accounts.ApplyCharacterSnapshots(snapshots);
            PlayerWorld.CompactOfflinePersistentCharacters();
        }

        private void CaptureAndQueueDeferredSaveSnapshot()
        {
            PrepareSaveBarrierState();
            _hooks.Save?.Invoke(this);
            IServerRuntimeSaveSnapshot? snapshot = _hooks.CaptureSaveSnapshot?.Invoke(this);
            _autoSaveAccumulatorSeconds = 0d;

            if (snapshot is not null)
                StartAsyncSaveWrite(snapshot);
        }

        private void StartAsyncSaveWrite(IServerRuntimeSaveSnapshot snapshot)
        {
            lock (_asyncSaveLock)
            {
                if (_asyncSaveWriteTask is { IsCompleted: false })
                    throw new InvalidOperationException("An asynchronous save write is already in progress.");

                _asyncSaveWriteTask = Task.Run(() => _hooks.WriteSaveSnapshot!(snapshot));
            }
        }

        private bool IsAsyncSaveWriteInProgress()
        {
            lock (_asyncSaveLock)
                return _asyncSaveWriteTask is { IsCompleted: false };
        }

        private void CompleteAsyncSaveWriteIfFinished()
        {
            Task? completedTask = null;

            lock (_asyncSaveLock)
            {
                if (_asyncSaveWriteTask is not null && _asyncSaveWriteTask.IsCompleted)
                {
                    completedTask = _asyncSaveWriteTask;
                    _asyncSaveWriteTask = null;
                }
            }

            completedTask?.GetAwaiter().GetResult();
        }

        private void WaitForAsyncSaveWriteCompletion()
        {
            Task? task;
            lock (_asyncSaveLock)
                task = _asyncSaveWriteTask;

            if (task is null)
                return;

            task.GetAwaiter().GetResult();

            lock (_asyncSaveLock)
            {
                if (ReferenceEquals(_asyncSaveWriteTask, task))
                    _asyncSaveWriteTask = null;
            }
        }

        private void TryAutoSave(float deltaSeconds)
        {
            float intervalSeconds = Options.AutoSaveEverySeconds;
            if (intervalSeconds <= 0f || deltaSeconds <= 0f)
                return;

            _autoSaveAccumulatorSeconds += deltaSeconds;
            if (_autoSaveAccumulatorSeconds < intervalSeconds)
                return;

            _autoSaveAccumulatorSeconds %= intervalSeconds;
            RequestSave();
        }
    }
}
