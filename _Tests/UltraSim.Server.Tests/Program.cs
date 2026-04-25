using System.Reflection;
using System.Net;
using UltraSim.Client;
using UltraSim.ECS;
using UltraSim.Server;
using UltraSim.Server.Console;
using UltraSim.Server.Godot;
using UltraSim.Server.Hosting;
using UltraSim.Server.Headless;
using UltraSim.Server.Network;
using UltraSim.Network;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(ServerApplicationState_PreservesExpectedLifecycleOrder), ServerApplicationState_PreservesExpectedLifecycleOrder);
        Run(nameof(ServerControlCommand_FactoryHelpers_ProduceExpectedKinds), ServerControlCommand_FactoryHelpers_ProduceExpectedKinds);
        Run(nameof(ServerCommandResult_FactoryHelpers_ProduceExpectedKinds), ServerCommandResult_FactoryHelpers_ProduceExpectedKinds);
        Run(nameof(ServerStatusSnapshot_PreservesSuppliedValues), ServerStatusSnapshot_PreservesSuppliedValues);
        Run(nameof(ServerMetricsSnapshot_PreservesSuppliedValues), ServerMetricsSnapshot_PreservesSuppliedValues);
        Run(nameof(ServerSystemRuntimeSnapshot_PreservesSuppliedValues), ServerSystemRuntimeSnapshot_PreservesSuppliedValues);
        Run(nameof(ServerPlayerSessionSnapshot_PreservesSuppliedValues), ServerPlayerSessionSnapshot_PreservesSuppliedValues);
        Run(nameof(ServerAccountSummarySnapshot_PreservesSuppliedValues), ServerAccountSummarySnapshot_PreservesSuppliedValues);
        Run(nameof(ServerApplicationEvent_PreservesSuppliedValues), ServerApplicationEvent_PreservesSuppliedValues);
        Run(nameof(IServerApplication_ExposesExpectedControlSurface), IServerApplication_ExposesExpectedControlSurface);
        Run(nameof(ServerApplication_Initialize_Start_Tick_Stop_FollowsExpectedHookOrder), ServerApplication_Initialize_Start_Tick_Stop_FollowsExpectedHookOrder);
        Run(nameof(ServerApplication_Start_AutoInitializesFromCreated), ServerApplication_Start_AutoInitializesFromCreated);
        Run(nameof(ServerApplication_RequestSave_DefersSaveUntilEndOfNextTick), ServerApplication_RequestSave_DefersSaveUntilEndOfNextTick);
        Run(nameof(ServerApplication_Save_ExecutesImmediately), ServerApplication_Save_ExecutesImmediately);
        Run(nameof(ServerApplication_Stop_FromCreated_MovesDirectlyToStopped), ServerApplication_Stop_FromCreated_MovesDirectlyToStopped);
        Run(nameof(ServerApplication_Stop_WithSaveOnStop_PerformsSaveBeforeStopHook), ServerApplication_Stop_WithSaveOnStop_PerformsSaveBeforeStopHook);
        Run(nameof(ServerApplication_CommandDispatch_UsesSharedLifecycleSurface), ServerApplication_CommandDispatch_UsesSharedLifecycleSurface);
        Run(nameof(ServerCommandRegistry_DispatchesStringPrefixCommandsAndNamedArgs), ServerCommandRegistry_DispatchesStringPrefixCommandsAndNamedArgs);
        Run(nameof(ServerCommandRegistry_AllowsUnprefixedConsoleCommandsButNotPlainChat), ServerCommandRegistry_AllowsUnprefixedConsoleCommandsButNotPlainChat);
        Run(nameof(ServerChatService_AnnounceCommandPublishesSystemEvent), ServerChatService_AnnounceCommandPublishesSystemEvent);
        Run(nameof(ServerApplication_Faults_WhenHookThrows), ServerApplication_Faults_WhenHookThrows);
        Run(nameof(ServerApplication_StatusAndMetricsSnapshots_ReflectRuntimeActivity), ServerApplication_StatusAndMetricsSnapshots_ReflectRuntimeActivity);
        Run(nameof(ServerApplication_SystemRuntimeSnapshots_ReflectHookData), ServerApplication_SystemRuntimeSnapshots_ReflectHookData);
        Run(nameof(ServerApplication_PlayerAndAccountSnapshots_ReflectHookData), ServerApplication_PlayerAndAccountSnapshots_ReflectHookData);
        Run(nameof(ServerWrapperDefaults_Use16MillisecondTick), ServerWrapperDefaults_Use16MillisecondTick);
        Run(nameof(ServerHostLoop_Pump_TicksAtFixedStepCadence), ServerHostLoop_Pump_TicksAtFixedStepCadence);
        Run(nameof(ServerHostLoop_Pump_RespectsCatchUpCap), ServerHostLoop_Pump_RespectsCatchUpCap);
        Run(nameof(ServerHostLoop_Pump_ClampsDroppedCatchUpTicksOnHugeElapsed), ServerHostLoop_Pump_ClampsDroppedCatchUpTicksOnHugeElapsed);
        Run(nameof(ServerHostLoop_Pump_DrainsQueuedCommandsBeforeTicks), ServerHostLoop_Pump_DrainsQueuedCommandsBeforeTicks);
        Run(nameof(ServerHostLoop_ForwardsApplicationEventsToSinks), ServerHostLoop_ForwardsApplicationEventsToSinks);
        Run(nameof(HostedServerRuntime_StartImmediately_AndStopSafely_UsesSharedHostingSurface), HostedServerRuntime_StartImmediately_AndStopSafely_UsesSharedHostingSurface);
        Run(nameof(HeadlessServerArgumentParser_ParsesExpectedOptions), HeadlessServerArgumentParser_ParsesExpectedOptions);
        Run(nameof(HeadlessServerShell_RunAsync_AutoStartsAndStopsCleanly), HeadlessServerShell_RunAsync_AutoStartsAndStopsCleanly);
        Run(nameof(HeadlessServerShell_RunAsync_ReturnsFailureWhenApplicationFaults), HeadlessServerShell_RunAsync_ReturnsFailureWhenApplicationFaults);
        Run(nameof(ConsoleServerArgumentParser_ParsesExpectedOptions), ConsoleServerArgumentParser_ParsesExpectedOptions);
        Run(nameof(ConsoleShellCommandParser_ParsesExpectedCommandsAndAliases), ConsoleShellCommandParser_ParsesExpectedCommandsAndAliases);
        Run(nameof(ConsoleServerShell_RunAsync_ProcessesInputCommandsAndStopsCleanly), ConsoleServerShell_RunAsync_ProcessesInputCommandsAndStopsCleanly);
        Run(nameof(GodotServerShellController_ExposesSharedControlSurface), GodotServerShellController_ExposesSharedControlSurface);
        Run(nameof(ServerRuntimeNetworkPublisher_PublishCurrentState_EmitsScopedLatestStateItems), ServerRuntimeNetworkPublisher_PublishCurrentState_EmitsScopedLatestStateItems);
        Run(nameof(ServerRuntimeNetworkPublisher_ApplicationEvents_PublishOrderedEvents), ServerRuntimeNetworkPublisher_ApplicationEvents_PublishOrderedEvents);
        Run(nameof(ServerRuntimeNetworkPublisher_UsesConfiguredOrderedEventCapacityWhenConstructingOwnStream), ServerRuntimeNetworkPublisher_UsesConfiguredOrderedEventCapacityWhenConstructingOwnStream);
        Run(nameof(ServerRuntimeNetworkPublisher_ScheduleAndDispatch_ProducesOperatorDatagrams), ServerRuntimeNetworkPublisher_ScheduleAndDispatch_ProducesOperatorDatagrams);
        Run(nameof(ServerNetworkSessionRuntime_FirstTick_SendsFilteredBaselineForUnacknowledgedConnection), ServerNetworkSessionRuntime_FirstTick_SendsFilteredBaselineForUnacknowledgedConnection);
        Run(nameof(ServerNetworkSessionRuntime_AfterBaselineAck_SendsDeltaAndReliableEvents), ServerNetworkSessionRuntime_AfterBaselineAck_SendsDeltaAndReliableEvents);
        Run(nameof(ServerNetworkSessionRuntime_ScopedReliableEventsUseContiguousConnectionSequences), ServerNetworkSessionRuntime_ScopedReliableEventsUseContiguousConnectionSequences);
        Run(nameof(ServerEcsWorldNetworkPublisher_PublishRuntimeState_EmitsWorldAndSystemSnapshots), ServerEcsWorldNetworkPublisher_PublishRuntimeState_EmitsWorldAndSystemSnapshots);
        Run(nameof(ServerEcsWorldNetworkPublisher_UsesConfiguredOrderedEventCapacityWhenConstructingOwnStream), ServerEcsWorldNetworkPublisher_UsesConfiguredOrderedEventCapacityWhenConstructingOwnStream);
        Run(nameof(ServerEcsWorldNetworkPublisher_PublishDirtyLatestState_EmitsScopedComponentUpdates), ServerEcsWorldNetworkPublisher_PublishDirtyLatestState_EmitsScopedComponentUpdates);
        Run(nameof(ServerEcsWorldNetworkPublisher_PublishDirtyLatestState_DoesNotDropBeyondInitialBuffer), ServerEcsWorldNetworkPublisher_PublishDirtyLatestState_DoesNotDropBeyondInitialBuffer);
        Run(nameof(ServerEcsWorldNetworkPublisher_Rules_ClassifyBulkLatestStateAndBulkOrderedEvents), ServerEcsWorldNetworkPublisher_Rules_ClassifyBulkLatestStateAndBulkOrderedEvents);
        Run(nameof(ServerEcsWorldNetworkPublisher_ChunkAoiLatestState_UsesActualRingScopedSubscriptions), ServerEcsWorldNetworkPublisher_ChunkAoiLatestState_UsesActualRingScopedSubscriptions);
        Run(nameof(ServerEcsWorldNetworkPublisher_ChunkAoiLatestState_SupportsRingCadencePolicy), ServerEcsWorldNetworkPublisher_ChunkAoiLatestState_SupportsRingCadencePolicy);
        Run(nameof(ServerEcsWorldNetworkPublisher_ChunkAoiLatestState_RecipientPhasingCarriesRingCadenceHints), ServerEcsWorldNetworkPublisher_ChunkAoiLatestState_RecipientPhasingCarriesRingCadenceHints);
        Run(nameof(ServerEcsWorldNetworkPublisher_ChunkAoiOrderedEvents_UseRingScopedReliableAndBulkClassification), ServerEcsWorldNetworkPublisher_ChunkAoiOrderedEvents_UseRingScopedReliableAndBulkClassification);
        Run(nameof(ServerEcsWorldNetworkPublisher_ScheduleAndDispatch_ProducesScopedDatagrams), ServerEcsWorldNetworkPublisher_ScheduleAndDispatch_ProducesScopedDatagrams);
        Run(nameof(ServerClientMovementBridges_Loopback_RoundTripAuthoritativeMovement), ServerClientMovementBridges_Loopback_RoundTripAuthoritativeMovement);
        Run(nameof(ServerClientMovementBridges_UdpHandshakeThenMovement_RoundTripAuthoritativeMovement), ServerClientMovementBridges_UdpHandshakeThenMovement_RoundTripAuthoritativeMovement);
        Run(nameof(ServerEcsMovementNetworkBridge_OwnerCorrectionPolicy_SuppressesSmallIntermediateStates), ServerEcsMovementNetworkBridge_OwnerCorrectionPolicy_SuppressesSmallIntermediateStates);
        Run(nameof(ServerMovementSessionHost_TracksReliableEventAcksFromClient), ServerMovementSessionHost_TracksReliableEventAcksFromClient);
        Run(nameof(ServerMovementSessionHost_TracksStateBaselineAcksFromClient), ServerMovementSessionHost_TracksStateBaselineAcksFromClient);
        Run(nameof(ServerMovementSessionHost_StateBaselineAck_SeedsReliableEventAckFloor), ServerMovementSessionHost_StateBaselineAck_SeedsReliableEventAckFloor);
        Run(nameof(ServerMovementSessionHost_OwnsSubscriptionsAndDispatchesPublishedBaseline), ServerMovementSessionHost_OwnsSubscriptionsAndDispatchesPublishedBaseline);
        Run(nameof(ServerMovementSessionHost_UsesConfiguredOrderedEventAndReplayCapacities), ServerMovementSessionHost_UsesConfiguredOrderedEventAndReplayCapacities);
        Run(nameof(ServerMovementSessionHosts_Udp_MultiClientHandshakeAndMovement), ServerMovementSessionHosts_Udp_MultiClientHandshakeAndMovement);

        Console.WriteLine("All Server contract tests passed.");
    }

    private static void ServerApplicationState_PreservesExpectedLifecycleOrder()
    {
        Assert.Equal(0, (int)ServerApplicationState.Created);
        Assert.Equal(1, (int)ServerApplicationState.Initializing);
        Assert.Equal(2, (int)ServerApplicationState.Initialized);
        Assert.Equal(3, (int)ServerApplicationState.Starting);
        Assert.Equal(4, (int)ServerApplicationState.Loading);
        Assert.Equal(5, (int)ServerApplicationState.Running);
        Assert.Equal(6, (int)ServerApplicationState.Saving);
        Assert.Equal(7, (int)ServerApplicationState.Stopping);
        Assert.Equal(8, (int)ServerApplicationState.Stopped);
        Assert.Equal(9, (int)ServerApplicationState.Restarting);
        Assert.Equal(10, (int)ServerApplicationState.Faulted);
    }

    private static void ServerControlCommand_FactoryHelpers_ProduceExpectedKinds()
    {
        Assert.Equal(ServerControlCommandKind.Start, ServerControlCommand.Start().Kind);
        Assert.Equal(ServerControlCommandKind.Stop, ServerControlCommand.Stop().Kind);
        Assert.Equal(ServerControlCommandKind.Restart, ServerControlCommand.Restart().Kind);
        Assert.Equal(ServerControlCommandKind.Save, ServerControlCommand.Save().Kind);
        Assert.Equal(ServerControlCommandKind.Status, ServerControlCommand.Status().Kind);
        Assert.Equal(ServerControlCommandKind.Shutdown, ServerControlCommand.Shutdown().Kind);

        var command = new ServerControlCommand(ServerControlCommandKind.Status, "detail");
        Assert.Equal("detail", command.Argument);
    }

    private static void ServerCommandResult_FactoryHelpers_ProduceExpectedKinds()
    {
        Assert.Equal(ServerCommandResultKind.Accepted, ServerCommandResult.Accepted("ok").Kind);
        Assert.Equal(ServerCommandResultKind.Rejected, ServerCommandResult.Rejected("no").Kind);
        Assert.Equal(ServerCommandResultKind.NoOp, ServerCommandResult.NoOp("noop").Kind);
        Assert.Equal(ServerCommandResultKind.Unknown, ServerCommandResult.Unknown("unknown").Kind);
    }

    private static void ServerStatusSnapshot_PreservesSuppliedValues()
    {
        DateTimeOffset startedAt = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);
        TimeSpan uptime = TimeSpan.FromSeconds(42);
        var snapshot = new ServerStatusSnapshot(
            "test",
            ServerApplicationState.Running,
            IsInitialized: true,
            IsRunning: true,
            IsAcceptingConnections: true,
            TickCount: 77,
            ActiveSessions: 3,
            PeakSessions: 9,
            StartedAtUtc: startedAt,
            Uptime: uptime);

        Assert.Equal("test", snapshot.ProfileName);
        Assert.Equal(ServerApplicationState.Running, snapshot.State);
        Assert.True(snapshot.IsInitialized);
        Assert.True(snapshot.IsRunning);
        Assert.True(snapshot.IsAcceptingConnections);
        Assert.Equal(77L, snapshot.TickCount);
        Assert.Equal(3, snapshot.ActiveSessions);
        Assert.Equal(9, snapshot.PeakSessions);
        Assert.Equal(startedAt, snapshot.StartedAtUtc);
        Assert.Equal(uptime, snapshot.Uptime);
    }

    private static void ServerMetricsSnapshot_PreservesSuppliedValues()
    {
        var snapshot = new ServerMetricsSnapshot(
            TotalTicks: 100,
            TotalCommands: 8,
            TotalSaveRequests: 3,
            TotalCompletedSaves: 2,
            TotalFaults: 1,
            MeanTickMilliseconds: 1.25,
            MaxTickMilliseconds: 4.5);

        Assert.Equal(100L, snapshot.TotalTicks);
        Assert.Equal(8L, snapshot.TotalCommands);
        Assert.Equal(3L, snapshot.TotalSaveRequests);
        Assert.Equal(2L, snapshot.TotalCompletedSaves);
        Assert.Equal(1L, snapshot.TotalFaults);
        Assert.Equal(1.25, snapshot.MeanTickMilliseconds);
        Assert.Equal(4.5, snapshot.MaxTickMilliseconds);
    }

    private static void ServerSystemRuntimeSnapshot_PreservesSuppliedValues()
    {
        var snapshot = new ServerSystemRuntimeSnapshot(
            Name: "Movement",
            Stage: "Movement",
            Enabled: true,
            Manual: false,
            PeriodTicks: 4,
            StartOffset: 1,
            LastRunTick: 77,
            UpdateCount: 11,
            LastUpdateMilliseconds: 0.25,
            AverageUpdateMilliseconds: 0.5,
            PeakUpdateMilliseconds: 1.5,
            MinUpdateMilliseconds: 0.1,
            ControlCount: 2);

        Assert.Equal("Movement", snapshot.Name);
        Assert.Equal("Movement", snapshot.Stage);
        Assert.True(snapshot.Enabled);
        Assert.False(snapshot.Manual);
        Assert.Equal(4, snapshot.PeriodTicks);
        Assert.Equal(1, snapshot.StartOffset);
        Assert.Equal((uint)77, snapshot.LastRunTick);
        Assert.Equal(11L, snapshot.UpdateCount);
        Assert.Equal(0.25, snapshot.LastUpdateMilliseconds);
        Assert.Equal(0.5, snapshot.AverageUpdateMilliseconds);
        Assert.Equal(1.5, snapshot.PeakUpdateMilliseconds);
        Assert.Equal(0.1, snapshot.MinUpdateMilliseconds);
        Assert.Equal(2, snapshot.ControlCount);
    }

    private static void ServerPlayerSessionSnapshot_PreservesSuppliedValues()
    {
        var snapshot = new ServerPlayerSessionSnapshot(
            SessionId: "session-1",
            PlayerName: "Aster",
            AccountId: "acct-7",
            SessionState: "Authenticated",
            Detail: "zone=island-a");

        Assert.Equal("session-1", snapshot.SessionId);
        Assert.Equal("Aster", snapshot.PlayerName);
        Assert.Equal("acct-7", snapshot.AccountId);
        Assert.Equal("Authenticated", snapshot.SessionState);
        Assert.Equal("zone=island-a", snapshot.Detail);
    }

    private static void ServerAccountSummarySnapshot_PreservesSuppliedValues()
    {
        var snapshot = new ServerAccountSummarySnapshot(
            AccountId: "acct-7",
            DisplayName: "Aster",
            IsOnline: true,
            IsBanned: false,
            Detail: "role=operator");

        Assert.Equal("acct-7", snapshot.AccountId);
        Assert.Equal("Aster", snapshot.DisplayName);
        Assert.True(snapshot.IsOnline);
        Assert.False(snapshot.IsBanned);
        Assert.Equal("role=operator", snapshot.Detail);
    }

    private static void ServerApplicationEvent_PreservesSuppliedValues()
    {
        DateTimeOffset timestamp = new(2026, 4, 20, 12, 0, 0, TimeSpan.Zero);
        var evt = new ServerApplicationEvent(
            ServerApplicationEventKind.StateChanged,
            timestamp,
            "runtime started",
            ServerApplicationState.Running);

        Assert.Equal(ServerApplicationEventKind.StateChanged, evt.Kind);
        Assert.Equal(timestamp, evt.TimestampUtc);
        Assert.Equal("runtime started", evt.Message);
        Assert.Equal(ServerApplicationState.Running, evt.State);
    }

    private static void IServerApplication_ExposesExpectedControlSurface()
    {
        Type contract = typeof(IServerApplication);

        Assert.True(contract.IsInterface);
        AssertMethod(contract, nameof(IServerApplication.Initialize), typeof(void));
        AssertMethod(contract, nameof(IServerApplication.Start), typeof(void));
        AssertMethod(contract, nameof(IServerApplication.Tick), typeof(void), typeof(float));
        AssertMethod(contract, nameof(IServerApplication.RequestSave), typeof(void));
        AssertMethod(contract, nameof(IServerApplication.Save), typeof(bool));
        AssertMethod(contract, nameof(IServerApplication.Stop), typeof(void));
        AssertMethod(contract, nameof(IServerApplication.Restart), typeof(void));
        AssertMethod(contract, nameof(IServerApplication.GetStatusSnapshot), typeof(ServerStatusSnapshot));
        AssertMethod(contract, nameof(IServerApplication.GetMetricsSnapshot), typeof(ServerMetricsSnapshot));
        AssertMethod(contract, nameof(IServerApplication.GetSystemRuntimeSnapshots), typeof(IReadOnlyList<ServerSystemRuntimeSnapshot>));
        AssertMethod(contract, nameof(IServerApplication.GetPlayerSessionsSnapshot), typeof(IReadOnlyList<ServerPlayerSessionSnapshot>));
        AssertMethod(contract, nameof(IServerApplication.GetAccountSummariesSnapshot), typeof(IReadOnlyList<ServerAccountSummarySnapshot>));

        MethodInfo? tryExecute = contract.GetMethod(nameof(IServerApplication.TryExecuteCommand));
        Assert.NotNull(tryExecute);
        Assert.Equal(typeof(bool), tryExecute!.ReturnType);
        ParameterInfo[] parameters = tryExecute.GetParameters();
        Assert.Equal(2, parameters.Length);
        Assert.Equal(typeof(ServerControlCommand), parameters[0].ParameterType);
        Assert.Equal(typeof(ServerCommandResult).MakeByRefType(), parameters[1].ParameterType);
        Assert.True(parameters[1].IsOut);

        EventInfo? eventInfo = contract.GetEvent(nameof(IServerApplication.EventRaised));
        Assert.NotNull(eventInfo);
        Assert.Equal(typeof(Action<ServerApplicationEvent>), eventInfo!.EventHandlerType);
    }

    private static void ServerApplication_Initialize_Start_Tick_Stop_FollowsExpectedHookOrder()
    {
        var log = new List<string>();
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Initialize = _ => log.Add("Initialize"),
                Start = _ => log.Add("Start"),
                Load = _ => log.Add("Load"),
                Tick = (_, dt) => log.Add($"Tick:{dt:0.000}"),
                Stop = _ => log.Add("Stop"),
            });

        app.Initialize();
        app.Start();
        app.Tick(0.25f);
        app.Stop();

        Assert.Equal(ServerApplicationState.Stopped, app.State);
        Assert.SequenceEqual(
            new[] { "Initialize", "Start", "Load", "Tick:0.250", "Stop" },
            log);
    }

    private static void ServerApplication_Start_AutoInitializesFromCreated()
    {
        var log = new List<string>();
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Initialize = _ => log.Add("Initialize"),
                Start = _ => log.Add("Start"),
                Load = _ => log.Add("Load"),
            });

        app.Start();

        Assert.True(app.IsInitialized);
        Assert.True(app.IsRunning);
        Assert.Equal(ServerApplicationState.Running, app.State);
        Assert.SequenceEqual(new[] { "Initialize", "Start", "Load" }, log);
    }

    private static void ServerApplication_RequestSave_DefersSaveUntilEndOfNextTick()
    {
        var log = new List<string>();
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Tick = (_, _) => log.Add("Tick"),
                Save = _ => log.Add("Save"),
            });

        app.Start();
        app.RequestSave();

        Assert.SequenceEqual(Array.Empty<string>(), log);

        app.Tick(0.016f);
        Assert.SequenceEqual(new[] { "Tick", "Save" }, log);

        app.Tick(0.016f);
        Assert.SequenceEqual(new[] { "Tick", "Save", "Tick" }, log);
    }

    private static void ServerApplication_Save_ExecutesImmediately()
    {
        int saves = 0;
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Save = _ => saves++,
            });

        app.Start();
        bool result = app.Save();

        Assert.True(result);
        Assert.Equal(1, saves);
        Assert.Equal(ServerApplicationState.Running, app.State);
    }

    private static void ServerApplication_Stop_FromCreated_MovesDirectlyToStopped()
    {
        var app = new ServerApplication(new ServerApplicationOptions("test"));

        app.Stop();

        Assert.Equal(ServerApplicationState.Stopped, app.State);
        Assert.False(app.IsInitialized);
        Assert.False(app.IsRunning);
    }

    private static void ServerApplication_Stop_WithSaveOnStop_PerformsSaveBeforeStopHook()
    {
        var log = new List<string>();
        var app = new ServerApplication(
            new ServerApplicationOptions("test", SaveOnStop: true),
            new ServerApplicationHooks
            {
                Save = _ => log.Add("Save"),
                Stop = _ => log.Add("Stop"),
            });

        app.Start();
        app.Stop();

        Assert.SequenceEqual(new[] { "Save", "Stop" }, log);
        Assert.Equal(ServerApplicationState.Stopped, app.State);
    }

    private static void ServerApplication_CommandDispatch_UsesSharedLifecycleSurface()
    {
        int saves = 0;
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Save = _ => saves++,
            });

        Assert.True(app.TryExecuteCommand(ServerControlCommand.Start(), out ServerCommandResult startResult));
        Assert.Equal(ServerCommandResultKind.Accepted, startResult.Kind);
        Assert.Equal(ServerApplicationState.Running, app.State);

        Assert.True(app.TryExecuteCommand(ServerControlCommand.Status(), out ServerCommandResult statusResult));
        Assert.Equal(ServerCommandResultKind.Accepted, statusResult.Kind);

        Assert.True(app.TryExecuteCommand(ServerControlCommand.Save(), out ServerCommandResult saveResult));
        Assert.Equal(ServerCommandResultKind.Accepted, saveResult.Kind);
        Assert.Equal(1, saves);

        Assert.True(app.TryExecuteCommand(ServerControlCommand.Stop(), out ServerCommandResult stopResult));
        Assert.Equal(ServerCommandResultKind.Accepted, stopResult.Kind);
        Assert.Equal(ServerApplicationState.Stopped, app.State);
    }

    private static void ServerCommandRegistry_DispatchesStringPrefixCommandsAndNamedArgs()
    {
        var registry = new ServerCommandRegistry();
        bool invoked = false;
        string? hue = null;
        string? name = null;
        string? itemType = null;

        registry.Register(
            "add",
            (in ServerCommandRequest _, ServerTextCommandInvocation invocation) =>
            {
                invoked = true;
                itemType = invocation.Arguments[0];
                hue = invocation.NamedArguments["hue"];
                name = invocation.NamedArguments["name"];
            },
            prefix: "[",
            minAccessLevel: 10);

        var request = new ServerCommandRequest(
            ServerCommandSource.ChatInput,
            "[add item hue=0x200, name=\"The Sword of Awesome\"",
            AccessLevel: 10);

        Assert.True(registry.TryDispatch(in request, out ServerCommandDispatchResult result));
        Assert.Equal(ServerCommandDispatchStatus.Executed, result.Status);
        Assert.True(invoked);
        Assert.Equal("item", itemType!);
        Assert.Equal("0x200", hue!);
        Assert.Equal("The Sword of Awesome", name!);
    }

    private static void ServerCommandRegistry_AllowsUnprefixedConsoleCommandsButNotPlainChat()
    {
        var registry = new ServerCommandRegistry();
        bool invoked = false;
        registry.Register(
            "announce",
            (in ServerCommandRequest _, ServerTextCommandInvocation _) => invoked = true,
            prefix: "",
            minAccessLevel: 100);

        var chatRequest = new ServerCommandRequest(
            ServerCommandSource.ChatInput,
            "announce hello",
            AccessLevel: 100);
        Assert.False(registry.TryDispatch(in chatRequest, out _));
        Assert.False(invoked);

        var consoleRequest = new ServerCommandRequest(
            ServerCommandSource.ServerConsole,
            "announce hello",
            AccessLevel: 100);
        Assert.True(registry.TryDispatch(in consoleRequest, out ServerCommandDispatchResult result));
        Assert.Equal(ServerCommandDispatchStatus.Executed, result.Status);
        Assert.True(invoked);
    }

    private static void ServerChatService_AnnounceCommandPublishesSystemEvent()
    {
        var orderedEvents = new OrderedEventStream(capacity: 8);
        var registry = new ServerCommandRegistry();
        var chat = new ServerChatService(orderedEvents, registry);
        ServerChatCommandBootstrap.RegisterDefaults(registry, chat);

        var request = new ServerCommandRequest(
            ServerCommandSource.ServerConsole,
            "announce \"Maintenance in 10 minutes\"",
            AccessLevel: 100);

        Assert.True(registry.TryDispatch(in request, out ServerCommandDispatchResult result));
        Assert.Equal(ServerCommandDispatchStatus.Executed, result.Status);
        Assert.Equal(1L, chat.PublishedMessages);

        OrderedEventEntry[] entries = new OrderedEventEntry[4];
        OrderedEventDrainResult drain = orderedEvents.DrainSince(new OrderedEventCursor(0), entries);
        Assert.Equal(OrderedEventDrainStatus.Success, drain.Status);
        Assert.Equal(1, drain.Count);
        Assert.Equal(ServerChatScopes.System, entries[0].ScopeKey);

        Assert.True(PacketCodec.TryDecodeEnvelope(entries[0].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
        Assert.Equal(ChatMessageEventPacket.PacketId, header.PacketId);
        var reader = new UltraSim.Network.BitReader(payload);
        ChatMessageEventPacket packet = ChatMessageEventPacket.Deserialize(ref reader);
        Assert.Equal(ServerChatChannelIds.System, packet.ChannelId);
        Assert.Equal("Maintenance in 10 minutes", packet.Text);
        Assert.Equal(ChatMessageFlags.System | ChatMessageFlags.Announcement, packet.Flags);
    }

    private static void ServerApplication_Faults_WhenHookThrows()
    {
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Tick = static (_, _) => throw new InvalidOperationException("boom"),
            });

        app.Start();

        InvalidOperationException ex = Assert.Throws<InvalidOperationException>(() => app.Tick(0.016f));
        Assert.Equal("boom", ex.Message);
        Assert.Equal(ServerApplicationState.Faulted, app.State);
        Assert.NotNull(app.LastFault);
        Assert.Equal("Tick", app.LastFault!.Operation);
        Assert.Equal("boom", app.LastFault.Message);
    }

    private static void ServerApplication_StatusAndMetricsSnapshots_ReflectRuntimeActivity()
    {
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                IsAcceptingConnections = _ => true,
                GetActiveSessions = _ => 3,
            });

        app.Start();
        app.RequestSave();
        app.Tick(0.016f);
        app.TryExecuteCommand(ServerControlCommand.Status(), out _);

        ServerStatusSnapshot status = app.GetStatusSnapshot();
        ServerMetricsSnapshot metrics = app.GetMetricsSnapshot();

        Assert.Equal("test", status.ProfileName);
        Assert.Equal(ServerApplicationState.Running, status.State);
        Assert.True(status.IsInitialized);
        Assert.True(status.IsRunning);
        Assert.True(status.IsAcceptingConnections);
        Assert.Equal(1L, status.TickCount);
        Assert.Equal(3, status.ActiveSessions);
        Assert.Equal(3, status.PeakSessions);
        Assert.True(status.StartedAtUtc.HasValue);
        Assert.True(status.Uptime >= TimeSpan.Zero);

        Assert.Equal(1L, metrics.TotalTicks);
        Assert.Equal(1L, metrics.TotalCommands);
        Assert.Equal(1L, metrics.TotalSaveRequests);
        Assert.Equal(1L, metrics.TotalCompletedSaves);
        Assert.Equal(0L, metrics.TotalFaults);
        Assert.True(metrics.MeanTickMilliseconds >= 0d);
        Assert.True(metrics.MaxTickMilliseconds >= 0d);
    }

    private static void ServerApplication_PlayerAndAccountSnapshots_ReflectHookData()
    {
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                GetPlayerSessions = _ => new[]
                {
                    new ServerPlayerSessionSnapshot("session-1", "Aster", "acct-7", "Authenticated", "zone=island-a"),
                },
                GetAccountSummaries = _ => new[]
                {
                    new ServerAccountSummarySnapshot("acct-7", "Aster", IsOnline: true, IsBanned: false, Detail: "role=operator"),
                },
            });

        IReadOnlyList<ServerPlayerSessionSnapshot> players = app.GetPlayerSessionsSnapshot();
        IReadOnlyList<ServerAccountSummarySnapshot> accounts = app.GetAccountSummariesSnapshot();

        Assert.Equal(1, players.Count);
        Assert.Equal("Aster", players[0].PlayerName);
        Assert.Equal(1, accounts.Count);
        Assert.Equal("acct-7", accounts[0].AccountId);
    }

    private static void ServerWrapperDefaults_Use16MillisecondTick()
    {
        Assert.Equal(TimeSpan.FromMilliseconds(16), ServerHostLoopOptions.Default.TickInterval);
        Assert.Equal(16, HeadlessServerShellOptions.Default.TickMilliseconds);
        Assert.Equal(16, ConsoleServerShellOptions.Default.TickMilliseconds);
        Assert.Equal(16, GodotServerShellOptions.Default.TickMilliseconds);
    }

    private static void ServerApplication_SystemRuntimeSnapshots_ReflectHookData()
    {
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                GetSystemRuntimeSnapshots = _ => new[]
                {
                    new ServerSystemRuntimeSnapshot(
                        "Movement",
                        "Movement",
                        Enabled: true,
                        Manual: false,
                        PeriodTicks: 4,
                        StartOffset: 1,
                        LastRunTick: 77,
                        UpdateCount: 11,
                        LastUpdateMilliseconds: 0.25,
                        AverageUpdateMilliseconds: 0.5,
                        PeakUpdateMilliseconds: 1.5,
                        MinUpdateMilliseconds: 0.1,
                        ControlCount: 2),
                },
            });

        IReadOnlyList<ServerSystemRuntimeSnapshot> snapshots = app.GetSystemRuntimeSnapshots();

        Assert.Equal(1, snapshots.Count);
        Assert.Equal("Movement", snapshots[0].Name);
        Assert.Equal(2, snapshots[0].ControlCount);
    }

    private static void ServerHostLoop_Pump_TicksAtFixedStepCadence()
    {
        int ticks = 0;
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Tick = (_, _) => ticks++,
            });
        app.Start();

        using var host = new ServerHostLoop(
            app,
            new ServerHostLoopOptions(TimeSpan.FromMilliseconds(50), MaxCatchUpTicksPerPump: 8));

        ServerHostPumpResult first = host.Pump(TimeSpan.FromMilliseconds(20));
        Assert.Equal(0, first.TicksProcessed);
        Assert.Equal(0, ticks);

        ServerHostPumpResult second = host.Pump(TimeSpan.FromMilliseconds(40));
        Assert.Equal(1, second.TicksProcessed);
        Assert.Equal(1, ticks);

        ServerHostPumpResult third = host.Pump(TimeSpan.FromMilliseconds(100));
        Assert.Equal(2, third.TicksProcessed);
        Assert.Equal(3, ticks);
    }

    private static void ServerHostLoop_Pump_RespectsCatchUpCap()
    {
        int ticks = 0;
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Tick = (_, _) => ticks++,
            });
        app.Start();

        using var host = new ServerHostLoop(
            app,
            new ServerHostLoopOptions(TimeSpan.FromMilliseconds(50), MaxCatchUpTicksPerPump: 2));

        ServerHostPumpResult result = host.Pump(TimeSpan.FromMilliseconds(260));

        Assert.Equal(2, result.TicksProcessed);
        Assert.Equal(3, result.DroppedCatchUpTicks);
        Assert.Equal(2, ticks);
    }

    private static void ServerHostLoop_Pump_ClampsDroppedCatchUpTicksOnHugeElapsed()
    {
        int ticks = 0;
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Tick = (_, _) => ticks++,
            });
        app.Start();

        using var host = new ServerHostLoop(
            app,
            new ServerHostLoopOptions(TimeSpan.FromMilliseconds(50), MaxCatchUpTicksPerPump: 2));

        ServerHostPumpResult result = host.Pump(TimeSpan.FromDays(5000));

        Assert.Equal(2, result.TicksProcessed);
        Assert.Equal(int.MaxValue, result.DroppedCatchUpTicks);
        Assert.Equal(2, ticks);
    }

    private static void ServerHostLoop_Pump_DrainsQueuedCommandsBeforeTicks()
    {
        var log = new List<string>();
        var app = new ServerApplication(
            new ServerApplicationOptions("test"),
            new ServerApplicationHooks
            {
                Start = _ => log.Add("Start"),
                Load = _ => log.Add("Load"),
                Save = _ => log.Add("Save"),
                Tick = (_, _) => log.Add("Tick"),
            });

        using var host = new ServerHostLoop(
            app,
            new ServerHostLoopOptions(TimeSpan.FromMilliseconds(50), MaxCatchUpTicksPerPump: 8));

        host.EnqueueCommand(ServerControlCommand.Start());
        host.EnqueueCommand(ServerControlCommand.Save());

        ServerHostPumpResult result = host.Pump(TimeSpan.FromMilliseconds(50));

        Assert.Equal(2, result.CommandsHandled);
        Assert.Equal(1, result.TicksProcessed);
        Assert.SequenceEqual(new[] { "Start", "Load", "Save", "Tick" }, log);
    }

    private static void ServerHostLoop_ForwardsApplicationEventsToSinks()
    {
        var sink = new RecordingHostSink();
        var app = new ServerApplication(new ServerApplicationOptions("test"));

        using var host = new ServerHostLoop(app, sinks: new[] { sink });
        host.EnqueueCommand(ServerControlCommand.Start());
        host.Pump(TimeSpan.Zero);

        Assert.True(sink.Events.Count > 0);
        Assert.Equal(ServerApplicationEventKind.CommandHandled, sink.Events[^1].Kind);
    }

    private static void HostedServerRuntime_StartImmediately_AndStopSafely_UsesSharedHostingSurface()
    {
        using var runtime = new HostedServerRuntime(
            new ServerApplication(new ServerApplicationOptions("test")),
            new ServerHostLoopOptions(TimeSpan.FromMilliseconds(10), MaxCatchUpTicksPerPump: 4));

        runtime.StartImmediately();
        Assert.Equal(ServerApplicationState.Running, runtime.State);

        ServerHostPumpResult result = runtime.Pump(TimeSpan.FromMilliseconds(35));
        Assert.Equal(3, result.TicksProcessed);

        runtime.StopSafely();
        Assert.Equal(ServerApplicationState.Stopped, runtime.State);
    }

    private static void HeadlessServerArgumentParser_ParsesExpectedOptions()
    {
        string[] args =
        {
            "--profile=test-headless",
            "--tick-ms=16",
            "--max-catchup=4",
            "--run-for-seconds=0.25",
            "--idle-delay-ms=2",
            "--no-auto-start",
            "--save-on-stop",
            "--quiet",
        };

        bool parsed = HeadlessServerArgumentParser.TryParse(args, out HeadlessServerShellOptions options, out string? error, out bool showHelp);

        Assert.True(parsed);
        Assert.Equal<string?>(null, error);
        Assert.False(showHelp);
        Assert.Equal("test-headless", options.ProfileName);
        Assert.Equal(16, options.TickMilliseconds);
        Assert.Equal(4, options.MaxCatchUpTicksPerPump);
        Assert.False(options.AutoStart);
        Assert.True(options.SaveOnStop);
        Assert.True(options.Quiet);
        Assert.Equal(TimeSpan.FromSeconds(0.25), options.RunFor);
        Assert.Equal(TimeSpan.FromMilliseconds(2), options.IdleDelay);
    }

    private static void HeadlessServerShell_RunAsync_AutoStartsAndStopsCleanly()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new HeadlessServerShellOptions(
            "test-headless",
            TickMilliseconds: 10,
            RunFor: TimeSpan.FromMilliseconds(30),
            IdleDelay: TimeSpan.Zero);
        var shell = new HeadlessServerShell(options, stdout, stderr);

        int exitCode = shell.RunAsync().GetAwaiter().GetResult();
        string output = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("state changed: Running", output);
        Assert.Contains("state changed: Stopped", output);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    private static void HeadlessServerShell_RunAsync_ReturnsFailureWhenApplicationFaults()
    {
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new HeadlessServerShellOptions(
            "faulted-headless",
            TickMilliseconds: 10,
            RunFor: TimeSpan.FromMilliseconds(30),
            IdleDelay: TimeSpan.Zero);
        var shell = new HeadlessServerShell(
            options,
            stdout,
            stderr,
            _ => new ServerApplication(
                new ServerApplicationOptions("faulted-headless"),
                new ServerApplicationHooks
                {
                    Tick = static (_, _) => throw new InvalidOperationException("headless-boom"),
                }));

        int exitCode = shell.RunAsync().GetAwaiter().GetResult();

        Assert.Equal(1, exitCode);
        Assert.Contains("headless-boom", stderr.ToString());
    }

    private static void ConsoleServerArgumentParser_ParsesExpectedOptions()
    {
        string[] args =
        {
            "--profile=test-console",
            "--tick-ms=20",
            "--max-catchup=5",
            "--run-for-seconds=0.5",
            "--idle-delay-ms=3",
            "--no-auto-start",
            "--save-on-stop",
            "--no-input",
        };

        bool parsed = ConsoleServerArgumentParser.TryParse(args, out ConsoleServerShellOptions options, out string? error, out bool showHelp);

        Assert.True(parsed);
        Assert.Equal<string?>(null, error);
        Assert.False(showHelp);
        Assert.Equal("test-console", options.ProfileName);
        Assert.Equal(20, options.TickMilliseconds);
        Assert.Equal(5, options.MaxCatchUpTicksPerPump);
        Assert.False(options.AutoStart);
        Assert.True(options.SaveOnStop);
        Assert.False(options.EnableInput);
        Assert.Equal(TimeSpan.FromSeconds(0.5), options.RunFor);
        Assert.Equal(TimeSpan.FromMilliseconds(3), options.IdleDelay);
    }

    private static void ConsoleShellCommandParser_ParsesExpectedCommandsAndAliases()
    {
        Assert.Equal(ConsoleShellDirectiveKind.Help, ConsoleShellCommandParser.Parse("help").Kind);
        Assert.Equal(ConsoleShellDirectiveKind.None, ConsoleShellCommandParser.Parse(" ").Kind);
        Assert.Equal(ServerControlCommandKind.Status, ConsoleShellCommandParser.Parse("status").Command.Kind);
        Assert.Equal(ServerControlCommandKind.Save, ConsoleShellCommandParser.Parse("save").Command.Kind);
        Assert.Equal(ServerControlCommandKind.Restart, ConsoleShellCommandParser.Parse("restart").Command.Kind);
        Assert.Equal(ServerControlCommandKind.Shutdown, ConsoleShellCommandParser.Parse("quit").Command.Kind);
        Assert.Equal(ConsoleShellDirectiveKind.Invalid, ConsoleShellCommandParser.Parse("bogus").Kind);
    }

    private static void ConsoleServerShell_RunAsync_ProcessesInputCommandsAndStopsCleanly()
    {
        var stdin = new StringReader("status\nsave\nshutdown\n");
        var stdout = new StringWriter();
        var stderr = new StringWriter();
        var options = new ConsoleServerShellOptions(
            "test-console",
            TickMilliseconds: 10,
            IdleDelay: TimeSpan.Zero);
        var shell = new ConsoleServerShell(options, stdin, stdout, stderr);

        int exitCode = shell.RunAsync().GetAwaiter().GetResult();
        string output = stdout.ToString();

        Assert.Equal(0, exitCode);
        Assert.Contains("StateChanged: state changed: Running", output);
        Assert.Contains("> Status: Accepted", output);
        Assert.Contains("> Save: Accepted", output);
        Assert.Contains("> Shutdown: Accepted", output);
        Assert.Contains("StateChanged: state changed: Stopped", output);
        Assert.Equal(string.Empty, stderr.ToString());
    }

    private static void GodotServerShellController_ExposesSharedControlSurface()
    {
        var observed = new List<ServerApplicationEvent>();
        using var controller = new GodotServerShellController(
            new GodotServerShellOptions("test-godot", TickMilliseconds: 10),
            _ => new ServerApplication(
                new ServerApplicationOptions("test-godot"),
                new ServerApplicationHooks
                {
                    GetSystemRuntimeSnapshots = _ => new[]
                    {
                        new ServerSystemRuntimeSnapshot(
                            "Movement",
                            "Movement",
                            Enabled: true,
                            Manual: false,
                            PeriodTicks: 4,
                            StartOffset: 1,
                            LastRunTick: 77,
                            UpdateCount: 11,
                            LastUpdateMilliseconds: 0.25,
                            AverageUpdateMilliseconds: 0.5,
                            PeakUpdateMilliseconds: 1.5,
                            MinUpdateMilliseconds: 0.1,
                            ControlCount: 2),
                    },
                    GetPlayerSessions = _ => new[]
                    {
                        new ServerPlayerSessionSnapshot("session-1", "Aster", "acct-7", "Authenticated"),
                    },
                    GetAccountSummaries = _ => new[]
                    {
                        new ServerAccountSummarySnapshot("acct-7", "Aster", IsOnline: true, IsBanned: false),
                    },
                }));
        controller.EventRaised += observed.Add;

        controller.Start();
        controller.TickFrame(0.03d);
        ServerCommandResult status = controller.Execute(ServerControlCommand.Status());
        IReadOnlyList<ServerSystemRuntimeSnapshot> systems = controller.GetSystemRuntimeSnapshots();
        IReadOnlyList<ServerPlayerSessionSnapshot> players = controller.GetPlayerSessionsSnapshot();
        IReadOnlyList<ServerAccountSummarySnapshot> accounts = controller.GetAccountSummariesSnapshot();
        controller.Shutdown();

        Assert.Equal(ServerApplicationState.Stopped, controller.State);
        Assert.Equal(ServerCommandResultKind.Accepted, status.Kind);
        Assert.Equal(1, systems.Count);
        Assert.Equal(1, players.Count);
        Assert.Equal(1, accounts.Count);
        Assert.True(observed.Count > 0);
        Assert.Equal(ServerApplicationEventKind.StateChanged, observed[0].Kind);
    }

    private static void ServerRuntimeNetworkPublisher_PublishCurrentState_EmitsScopedLatestStateItems()
    {
        var app = new ServerApplication(
            new ServerApplicationOptions("net-test"),
            new ServerApplicationHooks
            {
                IsAcceptingConnections = _ => true,
                GetActiveSessions = _ => 2,
                GetSystemRuntimeSnapshots = _ => new[]
                {
                    new ServerSystemRuntimeSnapshot("Movement", "Movement", true, false, 4, 1, 77, 11, 0.25, 0.5, 1.5, 0.1, 2),
                },
                GetPlayerSessions = _ => new[]
                {
                    new ServerPlayerSessionSnapshot("session-1", "Aster", "acct-7", "Authenticated", "zone=island-a"),
                },
                GetAccountSummaries = _ => new[]
                {
                    new ServerAccountSummarySnapshot("acct-7", "Aster", IsOnline: true, IsBanned: false, Detail: "role=operator"),
                },
            });
        app.Start();

        using var publisher = new ServerRuntimeNetworkPublisher(app);
        ServerRuntimeNetworkPublishResult publish = publisher.PublishCurrentState();

        Assert.Equal(1, publish.StatusCount);
        Assert.Equal(1, publish.MetricsCount);
        Assert.Equal(1, publish.SystemCount);
        Assert.Equal(1, publish.PlayerCount);
        Assert.Equal(1, publish.AccountCount);

        var updates = new LatestStateUpdate[8];
        LatestStateDrainResult drain = publisher.LatestStateStream.DrainSince(default, updates);
        Assert.Equal(5, drain.Count);

        Assert.Equal((ulong)ServerRuntimeNetworkScopes.OperatorStatus.Value, updates[0].ScopeKey.Value);
        ServerStatusSnapshot status = ServerRuntimeNetworkPayloadCodec.Decode<ServerStatusSnapshot>(updates[0].Payload);
        Assert.Equal("net-test", status.ProfileName);

        ServerMetricsSnapshot metrics = ServerRuntimeNetworkPayloadCodec.Decode<ServerMetricsSnapshot>(updates[1].Payload);
        Assert.Equal(0L, metrics.TotalTicks);

        Assert.Equal((ulong)ServerRuntimeNetworkScopes.OperatorSystems.Value, updates[2].ScopeKey.Value);
        ServerSystemRuntimeSnapshot system = ServerRuntimeNetworkPayloadCodec.Decode<ServerSystemRuntimeSnapshot>(updates[2].Payload);
        Assert.Equal("Movement", system.Name);

        Assert.Equal((ulong)ServerRuntimeNetworkScopes.OperatorPlayers.Value, updates[3].ScopeKey.Value);
        ServerPlayerSessionSnapshot player = ServerRuntimeNetworkPayloadCodec.Decode<ServerPlayerSessionSnapshot>(updates[3].Payload);
        Assert.Equal("session-1", player.SessionId);

        Assert.Equal((ulong)ServerRuntimeNetworkScopes.OperatorAccounts.Value, updates[4].ScopeKey.Value);
        ServerAccountSummarySnapshot account = ServerRuntimeNetworkPayloadCodec.Decode<ServerAccountSummarySnapshot>(updates[4].Payload);
        Assert.Equal("acct-7", account.AccountId);
    }

    private static void ServerRuntimeNetworkPublisher_UsesConfiguredOrderedEventCapacityWhenConstructingOwnStream()
    {
        var app = new ServerApplication(new ServerApplicationOptions("capacity-test"));

        using var publisher = new ServerRuntimeNetworkPublisher(app, orderedEventCapacity: 1536);

        Assert.Equal(1536, publisher.OrderedEventStream.Capacity);
    }

    private static void ServerRuntimeNetworkPublisher_ApplicationEvents_PublishOrderedEvents()
    {
        var app = new ServerApplication(new ServerApplicationOptions("event-test"));
        using var publisher = new ServerRuntimeNetworkPublisher(app);

        app.Start();
        app.RequestSave();

        var entries = new OrderedEventEntry[8];
        OrderedEventDrainResult drain = publisher.OrderedEventStream.DrainSince(default, entries);

        Assert.True(drain.Count > 0);
        Assert.Equal((ulong)ServerRuntimeNetworkScopes.OperatorEvents.Value, entries[0].ScopeKey.Value);
        ServerApplicationEvent evt = ServerRuntimeNetworkPayloadCodec.Decode<ServerApplicationEvent>(entries[0].Payload);
        Assert.Equal(ServerApplicationEventKind.StateChanged, evt.Kind);
    }

    private static void ServerRuntimeNetworkPublisher_ScheduleAndDispatch_ProducesOperatorDatagrams()
    {
        var app = new ServerApplication(
            new ServerApplicationOptions("bridge-test"),
            new ServerApplicationHooks
            {
                GetSystemRuntimeSnapshots = _ => new[]
                {
                    new ServerSystemRuntimeSnapshot("Movement", "Movement", true, false, 4, 1, 77, 11, 0.25, 0.5, 1.5, 0.1, 2),
                },
                GetPlayerSessions = _ => new[]
                {
                    new ServerPlayerSessionSnapshot("session-1", "Aster", "acct-7", "Authenticated"),
                },
                GetAccountSummaries = _ => new[]
                {
                    new ServerAccountSummarySnapshot("acct-7", "Aster", IsOnline: true, IsBanned: false),
                },
            });
        app.Start();

        using var publisher = new ServerRuntimeNetworkPublisher(app);
        publisher.PublishCurrentState();

        var subscriptions = new ConnectionSubscriptionRegistry();
        subscriptions.Subscribe(new ConnectionId(1), ServerRuntimeNetworkScopes.OperatorStatus);
        subscriptions.Subscribe(new ConnectionId(1), ServerRuntimeNetworkScopes.OperatorSystems);
        subscriptions.Subscribe(new ConnectionId(1), ServerRuntimeNetworkScopes.OperatorPlayers);
        subscriptions.Subscribe(new ConnectionId(1), ServerRuntimeNetworkScopes.OperatorAccounts);

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
            new ScheduledWorkFanoutBridge(subscriptions));
        var scheduled = new ScheduledWorkItem[16];
        var subscriberScratch = new ConnectionId[4];
        var output = new OutboundDatagram[16];

        ConnectionScheduledFanoutResult result = bridge.ScheduleAndDispatch(
            default,
            ConnectionSendBudget.Unlimited,
            scheduled,
            subscriberScratch,
            output);

        Assert.Equal(5, result.Schedule.Count);
        Assert.Equal(5, result.Fanout.DatagramCount);

        int statusSeen = 0;
        int metricsSeen = 0;
        int systemSeen = 0;
        int playerSeen = 0;
        int accountSeen = 0;
        for (int i = 0; i < result.Fanout.DatagramCount; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(output[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            Assert.Equal(RealtimeStateFramePacket.PacketId, header.PacketId);
            var reader = new UltraSim.Network.BitReader(payload);
            RealtimeStateFramePacket frame = RealtimeStateFramePacket.Deserialize(ref reader);

            if (frame.ItemKey == ServerRuntimeNetworkItemKeys.Status)
            {
                statusSeen++;
            }
            else if (frame.ItemKey == ServerRuntimeNetworkItemKeys.Metrics)
            {
                metricsSeen++;
            }
            else
            {
                string json = System.Text.Encoding.UTF8.GetString(frame.Payload.Span);
                if (json.Contains("\"sessionId\":\"session-1\"", StringComparison.Ordinal))
                    playerSeen++;
                else if (json.Contains("\"accountId\":\"acct-7\"", StringComparison.Ordinal))
                    accountSeen++;
                else if (json.Contains("\"name\":\"Movement\"", StringComparison.Ordinal))
                    systemSeen++;
            }
        }

        Assert.Equal(1, statusSeen);
        Assert.Equal(1, metricsSeen);
        Assert.Equal(1, systemSeen);
        Assert.Equal(1, playerSeen);
        Assert.Equal(1, accountSeen);
    }

    private static void ServerNetworkSessionRuntime_FirstTick_SendsFilteredBaselineForUnacknowledgedConnection()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        var subscriptions = new ConnectionSubscriptionRegistry();
        ConnectionId connectionId = new(41);
        SubscriptionKey visibleScope = new(501);
        SubscriptionKey hiddenScope = new(502);
        subscriptions.Subscribe(connectionId, visibleScope);

        latest.Publish(1001, visibleScope, ImmutablePayloadBuffer.CopyFrom([1]));
        latest.Publish(1002, hiddenScope, ImmutablePayloadBuffer.CopyFrom([2]));

        var runtime = new ServerNetworkSessionRuntime(
            pair.Server,
            latest,
            ordered,
            subscriptions,
            new ReliableEventReplayBuffer(capacityPerConnection: 8));
        runtime.BindConnection(connectionId);

        ServerNetworkSessionRuntimeTickResult result = runtime.DispatchTick(100, ConnectionSendBudget.Unlimited);

        Assert.Equal(1, result.ConnectionCount);
        Assert.Equal(3, result.BaselineDatagramCount);
        Assert.Equal(0, result.OrderedEventDatagramCount);
        Assert.Equal(0, result.DeltaDatagramCount);

        var inbound = new InboundDatagram[8];
        int count = pair.Client.PollInbound(inbound);
        Assert.Equal(3, count);

        int baselineFrames = 0;
        for (int i = 0; i < count; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(inbound[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            if (header.PacketId == StateBaselineFramePacket.PacketId)
            {
                baselineFrames++;
                var reader = new UltraSim.Network.BitReader(payload);
                StateBaselineFramePacket frame = StateBaselineFramePacket.Deserialize(ref reader);
                Assert.Equal((ulong)1001, frame.ItemKey);
            }
        }

        Assert.Equal(1, baselineFrames);
        Assert.True(runtime.LatestStateSentTracker.TryGetLastSentSequence(connectionId, 1001, out _));
        Assert.False(runtime.LatestStateSentTracker.TryGetLastSentSequence(connectionId, 1002, out _));
    }

    private static void ServerNetworkSessionRuntime_AfterBaselineAck_SendsDeltaAndReliableEvents()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 8);
        var subscriptions = new ConnectionSubscriptionRegistry();
        ConnectionId connectionId = new(42);
        SubscriptionKey scope = new(601);
        subscriptions.Subscribe(connectionId, scope);

        latest.Publish(1001, scope, ImmutablePayloadBuffer.CopyFrom([1]));

        var runtime = new ServerNetworkSessionRuntime(
            pair.Server,
            latest,
            ordered,
            subscriptions,
            new ReliableEventReplayBuffer(capacityPerConnection: 8));
        runtime.BindConnection(connectionId);

        runtime.DispatchTick(100, ConnectionSendBudget.Unlimited);

        var baselineInbound = new InboundDatagram[8];
        int baselineCount = pair.Client.PollInbound(baselineInbound);
        Assert.Equal(3, baselineCount);

        StateBaselineDescriptor descriptor = default;
        for (int i = 0; i < baselineCount; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(baselineInbound[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            if (header.PacketId != StateBaselineBeginPacket.PacketId)
                continue;

            var reader = new UltraSim.Network.BitReader(payload);
            StateBaselineBeginPacket begin = StateBaselineBeginPacket.Deserialize(ref reader);
            descriptor = begin.Descriptor;
        }

        Assert.True(descriptor.BaselineId != 0);

        pair.Client.EnqueueOutbound(new OutboundDatagram(
            connectionId,
            StateBaselineAckPacket.Plane,
            StateBaselineAckPacket.PacketClass,
            StateBaselineAckPacket.Channel,
            PacketCodec.Encode(new StateBaselineAckPacket(descriptor))));
        pair.Client.FlushOutbound();

        var ackInbound = new InboundDatagram[4];
        int ackCount = pair.Server.PollInbound(ackInbound);
        Assert.Equal(1, ackCount);
        Assert.Equal(1, runtime.ProcessInbound(ackInbound.AsSpan(0, ackCount)));

        latest.Publish(1001, scope, ImmutablePayloadBuffer.CopyFrom([9]));
        ordered.Publish(scope, ImmutablePayloadBuffer.CopyFrom([7, 8]));

        ServerNetworkSessionRuntimeTickResult tick = runtime.DispatchTick(200, ConnectionSendBudget.Unlimited);

        Assert.Equal(0, tick.BaselineDatagramCount);
        Assert.Equal(1, tick.OrderedEventDatagramCount);
        Assert.Equal(1, tick.DeltaDatagramCount);

        var inbound = new InboundDatagram[8];
        int count = pair.Client.PollInbound(inbound);
        Assert.Equal(2, count);

        int reliableSeen = 0;
        int deltaSeen = 0;
        for (int i = 0; i < count; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(inbound[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            if (header.PacketId == ReliableEventFramePacket.PacketId)
            {
                reliableSeen++;
                var reader = new UltraSim.Network.BitReader(payload);
                ReliableEventFramePacket frame = ReliableEventFramePacket.Deserialize(ref reader);
                Assert.True(frame.Payload.Span.SequenceEqual(new byte[] { 7, 8 }));
            }
            else if (header.PacketId == StateDeltaFramePacket.PacketId)
            {
                deltaSeen++;
                var reader = new UltraSim.Network.BitReader(payload);
                StateDeltaFramePacket frame = StateDeltaFramePacket.Deserialize(ref reader);
                Assert.Equal(descriptor.BaselineId, frame.BaselineId);
                Assert.Equal((ulong)1001, frame.ItemKey);
                Assert.True(frame.Payload.Span.SequenceEqual(new byte[] { 9 }));
            }
        }

        Assert.Equal(1, reliableSeen);
        Assert.Equal(1, deltaSeen);
    }

    private static void ServerNetworkSessionRuntime_ScopedReliableEventsUseContiguousConnectionSequences()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        var latest = new LatestStateStream();
        var ordered = new OrderedEventStream(capacity: 16);
        var subscriptions = new ConnectionSubscriptionRegistry();
        ConnectionId connectionId = new(42);
        SubscriptionKey subscribedScope = new(601);
        SubscriptionKey otherScope = new(602);
        subscriptions.Subscribe(connectionId, subscribedScope);
        latest.Publish(1001, subscribedScope, ImmutablePayloadBuffer.CopyFrom([1]));

        var runtime = new ServerNetworkSessionRuntime(
            pair.Server,
            latest,
            ordered,
            subscriptions,
            new ReliableEventReplayBuffer(capacityPerConnection: 8));
        runtime.BindConnection(connectionId);

        runtime.DispatchTick(100, ConnectionSendBudget.Unlimited);
        StateBaselineDescriptor descriptor = ReadBaselineDescriptor(pair.Client);

        pair.Client.EnqueueOutbound(new OutboundDatagram(
            connectionId,
            StateBaselineAckPacket.Plane,
            StateBaselineAckPacket.PacketClass,
            StateBaselineAckPacket.Channel,
            PacketCodec.Encode(new StateBaselineAckPacket(descriptor))));
        pair.Client.FlushOutbound();
        var ackInbound = new InboundDatagram[4];
        int ackCount = pair.Server.PollInbound(ackInbound);
        runtime.ProcessInbound(ackInbound.AsSpan(0, ackCount));

        ordered.Publish(otherScope, ImmutablePayloadBuffer.CopyFrom([9]));
        ordered.Publish(subscribedScope, ImmutablePayloadBuffer.CopyFrom([1]));
        ordered.Publish(otherScope, ImmutablePayloadBuffer.CopyFrom([8]));
        ordered.Publish(subscribedScope, ImmutablePayloadBuffer.CopyFrom([2]));

        runtime.DispatchTick(200, ConnectionSendBudget.Unlimited);

        var inbound = new InboundDatagram[8];
        int count = pair.Client.PollInbound(inbound);
        var sequences = new List<ulong>();
        for (int i = 0; i < count; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(inbound[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            if (header.PacketId != ReliableEventFramePacket.PacketId)
                continue;

            var reader = new UltraSim.Network.BitReader(payload);
            ReliableEventFramePacket frame = ReliableEventFramePacket.Deserialize(ref reader);
            sequences.Add(frame.Sequence);
        }

        Assert.SequenceEqual([1UL, 2UL], sequences);
    }

    private static void ServerEcsWorldNetworkPublisher_PublishRuntimeState_EmitsWorldAndSystemSnapshots()
    {
        World world = CreateEcsTestWorld(enableProfiling: true);
        world.Tick(0.016f);

        var publisher = new ServerEcsWorldNetworkPublisher(world);
        ServerEcsWorldPublishResult publish = publisher.PublishRuntimeState();

        Assert.Equal(1, publish.WorldCount);
        Assert.Equal(1, publish.SystemCount);

        var updates = new LatestStateUpdate[4];
        LatestStateDrainResult drain = publisher.LatestStateStream.DrainSince(default, updates);
        Assert.Equal(2, drain.Count);

        Assert.Equal((ulong)ServerEcsNetworkScopes.OperatorWorld.Value, updates[0].ScopeKey.Value);
        ServerEcsWorldSnapshot worldSnapshot = ServerRuntimeNetworkPayloadCodec.Decode<ServerEcsWorldSnapshot>(updates[0].Payload);
        Assert.Equal((uint)1, worldSnapshot.WorldTick);
        Assert.Equal(1, worldSnapshot.SystemCount);
        Assert.True(worldSnapshot.ProfilingEnabled);

        Assert.Equal((ulong)ServerEcsNetworkScopes.OperatorSystems.Value, updates[1].ScopeKey.Value);
        SystemRuntimeSnapshot systemSnapshot = ServerRuntimeNetworkPayloadCodec.Decode<SystemRuntimeSnapshot>(updates[1].Payload);
        Assert.Equal("ServerEcsTestSystem", systemSnapshot.Name);
    }

    private static void ServerEcsWorldNetworkPublisher_UsesConfiguredOrderedEventCapacityWhenConstructingOwnStream()
    {
        World world = CreateEcsTestWorld(enableProfiling: false, registerSystems: false);

        var publisher = new ServerEcsWorldNetworkPublisher(world, orderedEventCapacity: 2048);

        Assert.Equal(2048, publisher.OrderedEventStream.Capacity);
    }

    private static void ServerEcsWorldNetworkPublisher_PublishDirtyLatestState_EmitsScopedComponentUpdates()
    {
        World world = CreateEcsTestWorld(enableProfiling: false, registerSystems: false);
        ComponentId positionId = world.Schema.GetComponentId<ServerEcsPosition>();
        ArchetypeSignature signature = new([positionId]);
        Entity left = world.CreateDefault(signature);
        Entity right = world.CreateDefault(signature);

        Assert.True(world.SetComponent(left, new ServerEcsPosition(2, 4)));
        Assert.True(world.SetComponent(right, new ServerEcsPosition(20, 8)));

        var publisher = new ServerEcsWorldNetworkPublisher(world);
        int published = publisher.PublishDirtyLatestState<ServerEcsPosition>(
            scopeSelector: static (_, position) => position.X < 10 ? new SubscriptionKey(1) : new SubscriptionKey(2),
            payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position));

        Assert.Equal(2, published);

        var updates = new LatestStateUpdate[4];
        LatestStateDrainResult drain = publisher.LatestStateStream.DrainSince(default, updates);
        Assert.Equal(2, drain.Count);

        ulong leftKey = ServerEcsNetworkItemKeys.GetComponentItemKey<ServerEcsPosition>(left);
        ulong rightKey = ServerEcsNetworkItemKeys.GetComponentItemKey<ServerEcsPosition>(right);

        int leftSeen = 0;
        int rightSeen = 0;
        for (int i = 0; i < drain.Count; i++)
        {
            ServerEcsPosition position = ServerRuntimeNetworkPayloadCodec.Decode<ServerEcsPosition>(updates[i].Payload);
            if (updates[i].ItemKey == leftKey)
            {
                leftSeen++;
                Assert.Equal((ulong)1, updates[i].ScopeKey.Value);
                Assert.Equal(new ServerEcsPosition(2, 4), position);
            }
            else if (updates[i].ItemKey == rightKey)
            {
                rightSeen++;
                Assert.Equal((ulong)2, updates[i].ScopeKey.Value);
                Assert.Equal(new ServerEcsPosition(20, 8), position);
            }
        }

        Assert.Equal(1, leftSeen);
        Assert.Equal(1, rightSeen);
        Assert.Equal(0, publisher.PublishDirtyLatestState<ServerEcsPosition>(
            scopeSelector: static (_, position) => position.X < 10 ? new SubscriptionKey(1) : new SubscriptionKey(2),
            payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position)));
    }

    private static void ServerEcsWorldNetworkPublisher_PublishDirtyLatestState_DoesNotDropBeyondInitialBuffer()
    {
        World world = CreateEcsTestWorld(enableProfiling: false, registerSystems: false);
        ComponentId positionId = world.Schema.GetComponentId<ServerEcsPosition>();
        ArchetypeSignature signature = new([positionId]);

        for (int i = 0; i < 300; i++)
        {
            Entity entity = world.CreateDefault(signature);
            Assert.True(world.SetComponent(entity, new ServerEcsPosition(i, i)));
        }

        var publisher = new ServerEcsWorldNetworkPublisher(world);
        int published = publisher.PublishDirtyLatestState<ServerEcsPosition>(
            scopeSelector: static (_, _) => new SubscriptionKey(9),
            payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
            batchSize: 64);

        Assert.Equal(300, published);
        Assert.Equal(300, publisher.LatestStateStream.GetSnapshot().ItemCount);
        Assert.Equal(0, world.GetDirtyCount<ServerEcsPosition>());
    }

    private static void ServerEcsWorldNetworkPublisher_Rules_ClassifyBulkLatestStateAndBulkOrderedEvents()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId positionId = builder.RegisterComponent<ServerEcsPosition>();
        ComponentId eventId = builder.RegisterComponent<ServerEcsEvent>();
        World world = new(builder.Build());

        ArchetypeSignature latestSignature = new([positionId]);
        ArchetypeSignature eventSignature = new([eventId]);
        Entity latestEntity = world.CreateDefault(latestSignature);
        Entity eventEntity = world.CreateDefault(eventSignature);
        Assert.True(world.SetComponent(latestEntity, new ServerEcsPosition(5, 6)));
        Assert.True(world.SetComponent(eventEntity, new ServerEcsEvent(77)));

        var publisher = new ServerEcsWorldNetworkPublisher(world);
        int latestPublished = publisher.PublishDirtyLatestState(
            new ServerEcsLatestStateRule<ServerEcsPosition>(
                scopeSelector: static (_, _) => new SubscriptionKey(91),
                payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
                packetClass: PacketClass.Bulk));
        int eventPublished = publisher.PublishDirtyOrderedEvents(
            new ServerEcsOrderedEventRule<ServerEcsEvent>(
                scopeSelector: static (_, _) => new SubscriptionKey(92),
                payloadSelector: static (_, evt) => ServerRuntimeNetworkPayloadCodec.Encode(evt),
                packetClass: PacketClass.Bulk));

        Assert.Equal(1, latestPublished);
        Assert.Equal(1, eventPublished);

        var subscriptions = new ConnectionSubscriptionRegistry();
        subscriptions.Subscribe(new ConnectionId(1), new SubscriptionKey(91));
        subscriptions.Subscribe(new ConnectionId(1), new SubscriptionKey(92));

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
            new ScheduledWorkFanoutBridge(subscriptions));
        var scheduled = new ScheduledWorkItem[4];
        var subscriberScratch = new ConnectionId[2];
        var output = new OutboundDatagram[4];

        ConnectionScheduledFanoutResult result = bridge.ScheduleAndDispatch(
            default,
            ConnectionSendBudget.Unlimited,
            scheduled,
            subscriberScratch,
            output);

        Assert.Equal(2, result.Schedule.Count);
        Assert.Equal(0, result.Schedule.RealtimeItemsScheduled);
        Assert.Equal(0, result.Schedule.ReliableEventItemsScheduled);
        Assert.Equal(2, result.Schedule.BulkItemsScheduled);

        int bulkLatestSeen = 0;
        int bulkOrderedSeen = 0;
        for (int i = 0; i < result.Fanout.DatagramCount; i++)
        {
            Assert.Equal(PacketClass.Bulk, output[i].PacketClass);
            Assert.True(PacketCodec.TryDecodeEnvelope(output[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));

            if (header.PacketId == RealtimeStateFramePacket.PacketId)
            {
                bulkLatestSeen++;
                Assert.Equal(ChannelType.ReliableOrdered, output[i].Channel);
                var reader = new UltraSim.Network.BitReader(payload);
                RealtimeStateFramePacket frame = RealtimeStateFramePacket.Deserialize(ref reader);
                Assert.Equal(ServerEcsNetworkItemKeys.GetComponentItemKey<ServerEcsPosition>(latestEntity), frame.ItemKey);
            }
            else if (header.PacketId == ReliableEventFramePacket.PacketId)
            {
                bulkOrderedSeen++;
                Assert.Equal(ChannelType.ReliableOrdered, output[i].Channel);
                var reader = new UltraSim.Network.BitReader(payload);
                ReliableEventFramePacket frame = ReliableEventFramePacket.Deserialize(ref reader);
                ServerEcsEvent evt = ServerRuntimeNetworkPayloadCodec.Decode<ServerEcsEvent>(frame.Payload);
                Assert.Equal(77, evt.EventCode);
            }
        }

        Assert.Equal(1, bulkLatestSeen);
        Assert.Equal(1, bulkOrderedSeen);
    }

    private static void ServerEcsWorldNetworkPublisher_ChunkAoiLatestState_UsesActualRingScopedSubscriptions()
    {
        World world = CreateEcsTestWorld(enableProfiling: false, registerSystems: false);
        ComponentId positionId = world.Schema.GetComponentId<ServerEcsPosition>();
        ArchetypeSignature signature = new([positionId]);
        Entity entity = world.CreateDefault(signature);
        Assert.True(world.SetComponent(entity, new ServerEcsPosition(8, 8)));

        var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, 7.5f, 23.5f);
        var classifier = new ServerEcsChunkAoiScopeClassifier(policy);
        var publisher = new ServerEcsWorldNetworkPublisher(world);
        int published = publisher.PublishChunkAoiLatestState(
            new ServerEcsChunkAoiLatestStateRule<ServerEcsPosition>(
                classifier,
                positionSelector: static (_, position) => new WorldPosition2(position.X, position.Y),
                payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
                packetClass: PacketClass.Realtime));

        Assert.Equal(policy.RingCount, published);

        var latestBatch = new LatestStateUpdate[4];
        LatestStateDrainResult latestDrain = publisher.LatestStateStream.DrainSince(default, latestBatch);
        Assert.Equal(2, latestDrain.Count);
        Assert.Equal(latestBatch[0].ItemKey, latestBatch[1].ItemKey);
        Assert.True(latestBatch[0].ScopeKey != latestBatch[1].ScopeKey);

        var subscriptions = new ConnectionSubscriptionRegistry();
        var maintainer = new ChunkAoiSubscriptionMaintainer(policy);
        AoiSubscriptionUpdateResult near = maintainer.UpdateConnection(new ConnectionId(1), new WorldPosition2(8f, 8f), subscriptions);
        AoiSubscriptionUpdateResult mid = maintainer.UpdateConnection(new ConnectionId(2), new WorldPosition2(32f, 8f), subscriptions);
        AoiSubscriptionUpdateResult far = maintainer.UpdateConnection(new ConnectionId(3), new WorldPosition2(80f, 8f), subscriptions);

        Assert.True(near.AddedCount > 0);
        Assert.True(mid.AddedCount > 0);
        Assert.True(far.AddedCount > 0);

        publisher.PublishChunkAoiLatestState(
            new ServerEcsChunkAoiLatestStateRule<ServerEcsPosition>(
                classifier,
                positionSelector: static (_, position) => new WorldPosition2(position.X, position.Y),
                payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
                packetClass: PacketClass.Realtime));

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
            new ScheduledWorkFanoutBridge(subscriptions));
        var scheduled = new ScheduledWorkItem[8];
        var subscriberScratch = new ConnectionId[16];
        var output = new OutboundDatagram[8];

        ConnectionScheduledFanoutResult result = bridge.ScheduleAndDispatch(
            default,
            ConnectionSendBudget.Unlimited,
            scheduled,
            subscriberScratch,
            output);

        Assert.Equal(2, result.Schedule.Count);
        Assert.Equal(2, result.Fanout.DatagramCount);

        bool connection1Seen = false;
        bool connection2Seen = false;
        bool connection3Seen = false;
        for (int i = 0; i < result.Fanout.DatagramCount; i++)
        {
            if (output[i].ConnectionId.Value == 1)
                connection1Seen = true;
            else if (output[i].ConnectionId.Value == 2)
                connection2Seen = true;
            else if (output[i].ConnectionId.Value == 3)
                connection3Seen = true;
        }

        Assert.True(connection1Seen);
        Assert.True(connection2Seen);
        Assert.False(connection3Seen);
    }

    private static void ServerEcsWorldNetworkPublisher_ChunkAoiLatestState_SupportsRingCadencePolicy()
    {
        World world = CreateEcsTestWorld(enableProfiling: false, registerSystems: false);
        ComponentId positionId = world.Schema.GetComponentId<ServerEcsPosition>();
        ArchetypeSignature signature = new([positionId]);
        Entity entity = world.CreateDefault(signature);
        Assert.True(world.SetComponent(entity, new ServerEcsPosition(8, 8)));

        var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, 7.5f, 23.5f, 39.5f);
        var classifier = new ServerEcsChunkAoiScopeClassifier(policy);
        var publisher = new ServerEcsWorldNetworkPublisher(world);
        var cadence = new ServerEcsChunkAoiRingCadencePolicy(1, 2, 3);

        int publishedTick1 = publisher.PublishChunkAoiLatestState(
            new ServerEcsChunkAoiLatestStateRule<ServerEcsPosition>(
                classifier,
                positionSelector: static (_, position) => new WorldPosition2(position.X, position.Y),
                payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
                ringCadence: cadence,
                packetClass: PacketClass.Realtime),
            publishTick: 1);

        Assert.Equal(1, publishedTick1);

        var latestBatch = new LatestStateUpdate[4];
        LatestStateDrainResult latestDrain = publisher.LatestStateStream.DrainSince(default, latestBatch);
        Assert.Equal(1, latestDrain.Count);

        Assert.True(world.SetComponent(entity, new ServerEcsPosition(9, 8)));
        int publishedTick2 = publisher.PublishChunkAoiLatestState(
            new ServerEcsChunkAoiLatestStateRule<ServerEcsPosition>(
                classifier,
                positionSelector: static (_, position) => new WorldPosition2(position.X, position.Y),
                payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
                ringCadence: cadence,
                packetClass: PacketClass.Realtime),
            publishTick: 2);

        Assert.Equal(2, publishedTick2);
    }

    private static void ServerEcsWorldNetworkPublisher_ChunkAoiLatestState_RecipientPhasingCarriesRingCadenceHints()
    {
        World world = CreateEcsTestWorld(enableProfiling: false, registerSystems: false);
        ComponentId positionId = world.Schema.GetComponentId<ServerEcsPosition>();
        ArchetypeSignature signature = new([positionId]);
        Entity entity = world.CreateDefault(signature);
        Assert.True(world.SetComponent(entity, new ServerEcsPosition(8, 8)));

        var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, 7.5f, 23.5f, 39.5f);
        var classifier = new ServerEcsChunkAoiScopeClassifier(policy);
        var publisher = new ServerEcsWorldNetworkPublisher(world);
        var cadence = new ServerEcsChunkAoiRingCadencePolicy(1, 2, 3);

        int published = publisher.PublishChunkAoiLatestState(
            new ServerEcsChunkAoiLatestStateRule<ServerEcsPosition>(
                classifier,
                positionSelector: static (_, position) => new WorldPosition2(position.X, position.Y),
                payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position),
                ringCadence: cadence,
                packetClass: PacketClass.Realtime,
                recipientPhasing: true),
            publishTick: 1);

        Assert.Equal(3, published);

        var latestBatch = new LatestStateUpdate[4];
        LatestStateDrainResult latestDrain = publisher.LatestStateStream.DrainSince(default, latestBatch);
        Assert.Equal(3, latestDrain.Count);
        Assert.Equal((uint)1, latestBatch[0].DeliveryCadenceTicks);
        Assert.Equal((uint)2, latestBatch[1].DeliveryCadenceTicks);
        Assert.Equal((uint)3, latestBatch[2].DeliveryCadenceTicks);
    }

    private static void ServerEcsWorldNetworkPublisher_ChunkAoiOrderedEvents_UseRingScopedReliableAndBulkClassification()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId eventId = builder.RegisterComponent<ServerEcsAoiEvent>();
        World world = new(builder.Build());
        ArchetypeSignature signature = new([eventId]);
        Entity entity = world.CreateDefault(signature);
        Assert.True(world.SetComponent(entity, new ServerEcsAoiEvent(8, 8, 101)));

        var policy = new ChunkAoiPolicy(chunkSizeMeters: 16f, 7.5f, 23.5f);
        var classifier = new ServerEcsChunkAoiScopeClassifier(policy);
        var publisher = new ServerEcsWorldNetworkPublisher(world);
        var subscriptions = new ConnectionSubscriptionRegistry();
        var maintainer = new ChunkAoiSubscriptionMaintainer(policy);
        maintainer.UpdateConnection(new ConnectionId(1), new WorldPosition2(8f, 8f), subscriptions);
        maintainer.UpdateConnection(new ConnectionId(2), new WorldPosition2(32f, 8f), subscriptions);
        maintainer.UpdateConnection(new ConnectionId(3), new WorldPosition2(80f, 8f), subscriptions);

        int reliablePublished = publisher.PublishChunkAoiOrderedEvents(
            new ServerEcsChunkAoiOrderedEventRule<ServerEcsAoiEvent>(
                classifier,
                positionSelector: static (_, evt) => new WorldPosition2(evt.X, evt.Y),
                payloadSelector: static (_, evt) => ServerRuntimeNetworkPayloadCodec.Encode(evt),
                packetClass: PacketClass.ReliableEvent));

        Assert.Equal(policy.RingCount, reliablePublished);

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
            new ScheduledWorkFanoutBridge(subscriptions));
        var scheduled = new ScheduledWorkItem[8];
        var subscriberScratch = new ConnectionId[16];
        var output = new OutboundDatagram[8];

        ConnectionScheduledFanoutResult reliableResult = bridge.ScheduleAndDispatch(
            default,
            ConnectionSendBudget.Unlimited,
            scheduled,
            subscriberScratch,
            output);

        Assert.Equal(2, reliableResult.Schedule.Count);
        Assert.Equal(2, reliableResult.Fanout.DatagramCount);

        bool connection1Seen = false;
        bool connection2Seen = false;
        bool connection3Seen = false;
        for (int i = 0; i < reliableResult.Fanout.DatagramCount; i++)
        {
            Assert.Equal(PacketClass.ReliableEvent, output[i].PacketClass);
            if (output[i].ConnectionId.Value == 1)
                connection1Seen = true;
            else if (output[i].ConnectionId.Value == 2)
                connection2Seen = true;
            else if (output[i].ConnectionId.Value == 3)
                connection3Seen = true;
        }

        Assert.True(connection1Seen);
        Assert.True(connection2Seen);
        Assert.False(connection3Seen);

        Assert.True(world.SetComponent(entity, new ServerEcsAoiEvent(8, 8, 202)));
        int bulkPublished = publisher.PublishChunkAoiOrderedEvents(
            new ServerEcsChunkAoiOrderedEventRule<ServerEcsAoiEvent>(
                classifier,
                positionSelector: static (_, evt) => new WorldPosition2(evt.X, evt.Y),
                payloadSelector: static (_, evt) => ServerRuntimeNetworkPayloadCodec.Encode(evt),
                packetClass: PacketClass.Bulk));

        Assert.Equal(policy.RingCount, bulkPublished);

        ConnectionScheduledFanoutResult bulkResult = bridge.ScheduleAndDispatch(
            reliableResult.Schedule.NextState,
            ConnectionSendBudget.Unlimited,
            scheduled,
            subscriberScratch,
            output);

        Assert.Equal(2, bulkResult.Schedule.Count);
        Assert.Equal(2, bulkResult.Fanout.DatagramCount);
        for (int i = 0; i < bulkResult.Fanout.DatagramCount; i++)
            Assert.Equal(PacketClass.Bulk, output[i].PacketClass);
    }

    private static void ServerEcsWorldNetworkPublisher_ScheduleAndDispatch_ProducesScopedDatagrams()
    {
        World world = CreateEcsTestWorld(enableProfiling: true);
        ComponentId positionId = world.Schema.GetComponentId<ServerEcsPosition>();
        ArchetypeSignature signature = new([positionId]);
        Entity entity = world.CreateDefault(signature);
        Assert.True(world.SetComponent(entity, new ServerEcsPosition(12, 24)));
        world.Tick(0.016f);

        var publisher = new ServerEcsWorldNetworkPublisher(world);
        publisher.PublishRuntimeState();
        int dirtyPublished = publisher.PublishDirtyLatestState<ServerEcsPosition>(
            scopeSelector: static (_, _) => new SubscriptionKey(77),
            payloadSelector: static (_, position) => ServerRuntimeNetworkPayloadCodec.Encode(position));

        Assert.Equal(1, dirtyPublished);

        var subscriptions = new ConnectionSubscriptionRegistry();
        subscriptions.Subscribe(new ConnectionId(1), ServerEcsNetworkScopes.OperatorWorld);
        subscriptions.Subscribe(new ConnectionId(1), ServerEcsNetworkScopes.OperatorSystems);
        subscriptions.Subscribe(new ConnectionId(1), new SubscriptionKey(77));

        var bridge = new ConnectionStreamFanoutBridge(
            new ConnectionStreamScheduler(publisher.LatestStateStream, publisher.OrderedEventStream),
            new ScheduledWorkFanoutBridge(subscriptions));
        var scheduled = new ScheduledWorkItem[16];
        var subscriberScratch = new ConnectionId[4];
        var output = new OutboundDatagram[16];

        ConnectionScheduledFanoutResult result = bridge.ScheduleAndDispatch(
            default,
            ConnectionSendBudget.Unlimited,
            scheduled,
            subscriberScratch,
            output);

        Assert.Equal(3, result.Schedule.Count);
        Assert.Equal(3, result.Fanout.DatagramCount);

        int worldSeen = 0;
        int systemSeen = 0;
        int positionSeen = 0;
        for (int i = 0; i < result.Fanout.DatagramCount; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(output[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            Assert.Equal(RealtimeStateFramePacket.PacketId, header.PacketId);
            var reader = new UltraSim.Network.BitReader(payload);
            RealtimeStateFramePacket frame = RealtimeStateFramePacket.Deserialize(ref reader);

            if (frame.ItemKey == ServerEcsNetworkItemKeys.World)
            {
                worldSeen++;
            }
            else if (frame.ItemKey == ServerEcsNetworkItemKeys.GetSystemRuntimeItemKey(world.GetSystemRuntimeSnapshots()[0]))
            {
                systemSeen++;
            }
            else if (frame.ItemKey == ServerEcsNetworkItemKeys.GetComponentItemKey<ServerEcsPosition>(entity))
            {
                positionSeen++;
            }
        }

        Assert.Equal(1, worldSeen);
        Assert.Equal(1, systemSeen);
        Assert.Equal(1, positionSeen);
    }

    private static void ServerClientMovementBridges_Loopback_RoundTripAuthoritativeMovement()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<ServerEcsMovementBody>();
        World world = new(builder.Build());
        ArchetypeSignature signature = new([movementId]);
        Entity entity = world.CreateDefault(signature);
        Assert.True(world.SetComponent(entity, new ServerEcsMovementBody(0f, 0f, 0f, 0f)));

        using var pair = new LoopbackTransportPair();
        pair.Client.Start();
        pair.Server.Start();

        void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            float velocityX = input.MoveX / 100f;
            float velocityY = input.MoveY / 100f;
            body = body with
            {
                VelocityX = velocityX,
                VelocityY = velocityY,
                PositionX = body.PositionX + velocityX * deltaSeconds,
                PositionY = body.PositionY + velocityY * deltaSeconds,
            };
            Assert.True(movementWorld.SetComponent(movementEntity, body));
        }

        bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            packet = new MovementStatePacket(
                networkEntityId,
                serverTick,
                lastProcessedInputSequence,
                body.PositionX,
                body.PositionY,
                body.VelocityX,
                body.VelocityY);
            return true;
        }

        var client = new ClientMovementNetworkBridge(pair.Client, new ConnectionId(1));
        var server = new ServerEcsMovementNetworkBridge(
            world,
            pair.Server,
            ApplyMovementInput,
            ProjectMovementState);
        server.Bind(new ConnectionId(1), entity, networkEntityId: 77);

        bool sent = client.TrySendInput(
            new ClientMovementInput(160, -80, 1),
            clientTick: 1000,
            predictedState: new ClientMovementState(1.6f, -0.8f, 1.6f, -0.8f),
            out ClientMovementInputSendResult sendResult);
        Assert.True(sent);
        Assert.Equal((ushort)1, sendResult.Sequence);
        Assert.Equal(1, client.Sequencer.PendingCount);
        Assert.Equal(1, client.PredictionStore.Count);

        pair.Client.FlushOutbound();
        Assert.Equal(1, server.PollInputs());
        Assert.Equal(1, server.LastReceivedInputCount);
        Assert.Equal(0, server.LastIgnoredInputCount);

        Assert.Equal(1, server.AdvanceAndSend(0.5f, serverTick: 42));
        Assert.Equal(1, server.LastSentStateCount);

        Assert.True(world.TryGetComponent(entity, out ServerEcsMovementBody authoritative));
        Assert.AlmostEqual(0.8f, authoritative.PositionX);
        Assert.AlmostEqual(-0.4f, authoritative.PositionY);
        Assert.AlmostEqual(1.6f, authoritative.VelocityX);
        Assert.AlmostEqual(-0.8f, authoritative.VelocityY);

        Assert.Equal(1, client.PollInbound());
        Assert.Equal((ushort)1, client.LastReceiveResult.LastProcessedInputSequence);
        Assert.Equal((uint)42, client.LastReceiveResult.ServerTick);
        Assert.Equal((uint)77, client.LastReceiveResult.NetworkEntityId);
        Assert.Equal(1, client.LastReceiveResult.RemovedPendingInputs);
        Assert.Equal(1, client.LastReceiveResult.RemovedPredictions);
        Assert.Equal(0, client.Sequencer.PendingCount);
        Assert.Equal(0, client.PredictionStore.Count);

        Assert.True(client.TrySampleInterpolatedState(
            42,
            static (in ClientMovementState from, in ClientMovementState to, float alpha) => new ClientMovementState(
                from.PositionX + (to.PositionX - from.PositionX) * alpha,
                from.PositionY + (to.PositionY - from.PositionY) * alpha,
                from.VelocityX + (to.VelocityX - from.VelocityX) * alpha,
                from.VelocityY + (to.VelocityY - from.VelocityY) * alpha),
            out ClientMovementState sampled));
        Assert.AlmostEqual(0.8f, sampled.PositionX);
        Assert.AlmostEqual(-0.4f, sampled.PositionY);
        Assert.AlmostEqual(1.6f, sampled.VelocityX);
        Assert.AlmostEqual(-0.8f, sampled.VelocityY);
    }

    private static void ServerClientMovementBridges_UdpHandshakeThenMovement_RoundTripAuthoritativeMovement()
    {
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        using var serverTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata));
        serverTransport.Start();
        var serverEndPoint = (IPEndPoint)Assert.NotNull(serverTransport.LocalEndPoint);

        using var clientTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata,
            serverEndPoint,
            new ConnectionId(1),
            AllowDynamicConnections: false));
        clientTransport.Start();

        var handshakePolicy = new HandshakePolicy(
            ProtocolVersion: 3,
            SchemaHash: 4,
            ContentHash: 5,
            RequiredFeatures: FeatureFlags.SharedPayloadFanout);
        var handshakeHello = new HandshakeHelloPacket(3, 4, 5, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression);
        var serverControl = new NetworkControlPlaneHost(NetworkControlPlaneRole.Server, serverTransport, handshakePolicy: handshakePolicy);
        var clientControl = new NetworkControlPlaneHost(NetworkControlPlaneRole.Client, clientTransport, clientHello: handshakeHello);

        SessionId clientSession = clientControl.BeginClientHandshake(new ConnectionId(1), nowTicks: 100);
        for (int i = 0; i < 8; i++)
        {
            serverControl.Pump(101 + i * 2);
            clientControl.Pump(102 + i * 2);
        }

        Assert.True(serverControl.Sessions.TryGetSessionIdByConnection(new ConnectionId(1), out SessionId serverSession));
        Assert.True(serverControl.Sessions.TryGetSnapshot(serverSession, out SessionSnapshot serverSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, serverSnapshot.State);
        Assert.True(clientControl.Sessions.TryGetSnapshot(clientSession, out SessionSnapshot clientSnapshot));
        Assert.Equal(ConnectionSessionState.Ready, clientSnapshot.State);

        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<ServerEcsMovementBody>();
        World world = new(builder.Build());
        ArchetypeSignature signature = new([movementId]);
        Entity entity = world.CreateDefault(signature);
        Assert.True(world.SetComponent(entity, new ServerEcsMovementBody(0f, 0f, 0f, 0f)));

        void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            float velocityX = input.MoveX / 100f;
            float velocityY = input.MoveY / 100f;
            body = body with
            {
                VelocityX = velocityX,
                VelocityY = velocityY,
                PositionX = body.PositionX + velocityX * deltaSeconds,
                PositionY = body.PositionY + velocityY * deltaSeconds,
            };
            Assert.True(movementWorld.SetComponent(movementEntity, body));
        }

        bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            packet = new MovementStatePacket(
                networkEntityId,
                serverTick,
                lastProcessedInputSequence,
                body.PositionX,
                body.PositionY,
                body.VelocityX,
                body.VelocityY);
            return true;
        }

        var client = new ClientMovementNetworkBridge(clientTransport, new ConnectionId(1));
        var server = new ServerEcsMovementNetworkBridge(world, serverTransport, ApplyMovementInput, ProjectMovementState);
        server.Bind(new ConnectionId(1), entity, networkEntityId: 88);

        Assert.True(client.TrySendInput(
            new ClientMovementInput(120, 40, 1),
            clientTick: 1000,
            predictedState: new ClientMovementState(1.2f, 0.4f, 1.2f, 0.4f),
            out ClientMovementInputSendResult sendResult));
        Assert.Equal((ushort)1, sendResult.Sequence);

        clientTransport.FlushOutbound();
        Assert.Equal(1, PollUntilNonZero(static serverBridge => serverBridge.PollInputs(), server, maxAttempts: 256));
        Assert.Equal(1, server.LastReceivedInputCount);
        Assert.Equal(1, server.AdvanceAndSend(0.5f, serverTick: 50));

        Assert.Equal(1, PollUntilNonZero(static clientBridge => clientBridge.PollInbound(), client, maxAttempts: 256));
        Assert.Equal((uint)88, client.LastReceiveResult.NetworkEntityId);
        Assert.Equal((uint)50, client.LastReceiveResult.ServerTick);
        Assert.Equal((ushort)1, client.LastReceiveResult.LastProcessedInputSequence);

        Assert.True(world.TryGetComponent(entity, out ServerEcsMovementBody authoritative));
        Assert.AlmostEqual(0.6f, authoritative.PositionX);
        Assert.AlmostEqual(0.2f, authoritative.PositionY);
        Assert.AlmostEqual(1.2f, authoritative.VelocityX);
        Assert.AlmostEqual(0.4f, authoritative.VelocityY);
    }

    private static void ServerMovementSessionHosts_Udp_MultiClientHandshakeAndMovement()
    {
        const int clientCount = 3;
        PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
        using var serverTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
            new IPEndPoint(IPAddress.Loopback, 0),
            metadata));
        serverTransport.Start();
        var serverEndPoint = (IPEndPoint)Assert.NotNull(serverTransport.LocalEndPoint);

        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<ServerEcsMovementBody>();
        World world = new(builder.Build());
        ArchetypeSignature signature = new([movementId]);
        uint nextNetworkEntityId = 1;
        var spawnedEntities = new List<Entity>(clientCount);

        void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            float velocityX = input.MoveX / 100f;
            float velocityY = input.MoveY / 100f;
            body = body with
            {
                VelocityX = velocityX,
                VelocityY = velocityY,
                PositionX = body.PositionX + velocityX * deltaSeconds,
                PositionY = body.PositionY + velocityY * deltaSeconds,
            };
            Assert.True(movementWorld.SetComponent(movementEntity, body));
        }

        bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            packet = new MovementStatePacket(
                networkEntityId,
                serverTick,
                lastProcessedInputSequence,
                body.PositionX,
                body.PositionY,
                body.VelocityX,
                body.VelocityY);
            return true;
        }

        ServerMovementSessionSpawn SpawnSession(World movementWorld, ConnectionId _)
        {
            Entity entity = movementWorld.CreateDefault(signature);
            Assert.True(movementWorld.SetComponent(entity, new ServerEcsMovementBody(0f, 0f, 0f, 0f)));
            spawnedEntities.Add(entity);
            return new ServerMovementSessionSpawn(entity, nextNetworkEntityId++);
        }

        var handshakePolicy = new HandshakePolicy(
            ProtocolVersion: 3,
            SchemaHash: 4,
            ContentHash: 5,
            RequiredFeatures: FeatureFlags.SharedPayloadFanout);
        var serverHost = new ServerMovementSessionHost(
            world,
            serverTransport,
            ApplyMovementInput,
            ProjectMovementState,
            SpawnSession,
            handshakePolicy: handshakePolicy);
        serverHost.Start();

        var clients = new List<(UdpSocketNetworkTransport Transport, ClientMovementSessionHost Host)>(clientCount);
        for (int i = 0; i < clientCount; i++)
        {
            var clientTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
                new IPEndPoint(IPAddress.Loopback, 0),
                metadata,
                serverEndPoint,
                new ConnectionId(1),
                AllowDynamicConnections: false));
            clientTransport.Start();
            var clientHost = new ClientMovementSessionHost(
                clientTransport,
                new ConnectionId(1),
                new HandshakeHelloPacket(3, 4, 5, FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression));
            clientHost.Start();
            clientHost.BeginHandshake(100 + i);
            clients.Add((clientTransport, clientHost));
        }

        for (int step = 0; step < 32; step++)
        {
            serverHost.Pump(200 + step * 2, 0.016f, (uint)(step + 1));
            for (int i = 0; i < clients.Count; i++)
                clients[i].Host.Pump(201 + step * 2);
        }

        Assert.Equal(clientCount, serverHost.BoundConnectionCount);
        for (int i = 0; i < clients.Count; i++)
            Assert.True(clients[i].Host.IsReady);

        for (int i = 0; i < clients.Count; i++)
        {
            Assert.True(clients[i].Host.TrySendInput(
                new ClientMovementInput((short)(100 + i * 10), (short)(20 + i * 5), 1),
                clientTick: (uint)(1000 + i),
                predictedState: new ClientMovementState(),
                out _));
        }

        for (int step = 0; step < 8; step++)
        {
            serverHost.Pump(400 + step * 2, 0.5f, (uint)(100 + step));
            for (int i = 0; i < clients.Count; i++)
                clients[i].Host.Pump(401 + step * 2);
        }

        Assert.Equal(clientCount, spawnedEntities.Count);
        for (int i = 0; i < spawnedEntities.Count; i++)
        {
            Assert.True(world.TryGetComponent(spawnedEntities[i], out ServerEcsMovementBody body));
            Assert.True(body.PositionX > 0f);
            Assert.True(body.PositionY > 0f);
        }

        for (int i = 0; i < clients.Count; i++)
        {
            Assert.Equal((ushort)1, clients[i].Host.Movement.LastReceiveResult.LastProcessedInputSequence);
            clients[i].Transport.Dispose();
        }
    }

    private static void ServerMovementSessionHost_TracksReliableEventAcksFromClient()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<ServerEcsMovementBody>();
        World world = new(builder.Build());
        ArchetypeSignature signature = new([movementId]);

        void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            body = body with
            {
                VelocityX = input.MoveX / 100f,
                VelocityY = input.MoveY / 100f,
                PositionX = body.PositionX + (input.MoveX / 100f) * deltaSeconds,
                PositionY = body.PositionY + (input.MoveY / 100f) * deltaSeconds,
            };
            Assert.True(movementWorld.SetComponent(movementEntity, body));
        }

        bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            packet = new MovementStatePacket(
                networkEntityId,
                serverTick,
                lastProcessedInputSequence,
                body.PositionX,
                body.PositionY,
                body.VelocityX,
                body.VelocityY);
            return true;
        }

        ServerMovementSessionSpawn SpawnSession(World movementWorld, ConnectionId _)
        {
            Entity entity = movementWorld.CreateDefault(signature);
            Assert.True(movementWorld.SetComponent(entity, new ServerEcsMovementBody(0f, 0f, 0f, 0f)));
            return new ServerMovementSessionSpawn(entity, 1);
        }

        var host = new ServerMovementSessionHost(
            world,
            pair.Server,
            ApplyMovementInput,
            ProjectMovementState,
            SpawnSession);

        pair.Client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventAckPacket.Plane,
            ReliableEventAckPacket.PacketClass,
            ReliableEventAckPacket.Channel,
            PacketCodec.Encode(new ReliableEventAckPacket(27))));
        pair.Client.FlushOutbound();

        Assert.Equal(1, host.Pump(nowTicks: 1000, deltaSeconds: 0.016f, serverTick: 1));
        Assert.True(host.TryGetLastReliableEventAck(new ConnectionId(1), out ulong acknowledgedSequence));
        Assert.Equal((ulong)27, acknowledgedSequence);

        pair.Client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            ReliableEventAckPacket.Plane,
            ReliableEventAckPacket.PacketClass,
            ReliableEventAckPacket.Channel,
            PacketCodec.Encode(new ReliableEventAckPacket(26))));
        pair.Client.FlushOutbound();

        Assert.Equal(1, host.Pump(nowTicks: 1002, deltaSeconds: 0.016f, serverTick: 2));
        Assert.True(host.TryGetLastReliableEventAck(new ConnectionId(1), out acknowledgedSequence));
        Assert.Equal((ulong)27, acknowledgedSequence);
    }

    private static void ServerMovementSessionHost_TracksStateBaselineAcksFromClient()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<ServerEcsMovementBody>();
        World world = new(builder.Build());
        ArchetypeSignature signature = new([movementId]);

        void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            body = body with
            {
                VelocityX = input.MoveX / 100f,
                VelocityY = input.MoveY / 100f,
                PositionX = body.PositionX + (input.MoveX / 100f) * deltaSeconds,
                PositionY = body.PositionY + (input.MoveY / 100f) * deltaSeconds,
            };
            Assert.True(movementWorld.SetComponent(movementEntity, body));
        }

        bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            packet = new MovementStatePacket(
                networkEntityId,
                serverTick,
                lastProcessedInputSequence,
                body.PositionX,
                body.PositionY,
                body.VelocityX,
                body.VelocityY);
            return true;
        }

        ServerMovementSessionSpawn SpawnSession(World movementWorld, ConnectionId _)
        {
            Entity entity = movementWorld.CreateDefault(signature);
            Assert.True(movementWorld.SetComponent(entity, new ServerEcsMovementBody(0f, 0f, 0f, 0f)));
            return new ServerMovementSessionSpawn(entity, 1);
        }

        var host = new ServerMovementSessionHost(
            world,
            pair.Server,
            ApplyMovementInput,
            ProjectMovementState,
            SpawnSession);

        StateBaselineDescriptor descriptor = new(3, 50, 42, 7);
        pair.Client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineAckPacket.Plane,
            StateBaselineAckPacket.PacketClass,
            StateBaselineAckPacket.Channel,
            PacketCodec.Encode(new StateBaselineAckPacket(descriptor))));
        pair.Client.FlushOutbound();

        Assert.Equal(1, host.Pump(nowTicks: 1000, deltaSeconds: 0.016f, serverTick: 1));
        Assert.True(host.TryGetLastStateBaselineAck(new ConnectionId(1), out StateBaselineDescriptor acknowledged));
        Assert.Equal(descriptor, acknowledged);

        pair.Client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineAckPacket.Plane,
            StateBaselineAckPacket.PacketClass,
            StateBaselineAckPacket.Channel,
            PacketCodec.Encode(new StateBaselineAckPacket(new StateBaselineDescriptor(2, 10, 9, 1)))));
        pair.Client.FlushOutbound();

        Assert.Equal(1, host.Pump(nowTicks: 1002, deltaSeconds: 0.016f, serverTick: 2));
        Assert.True(host.TryGetLastStateBaselineAck(new ConnectionId(1), out acknowledged));
        Assert.Equal(descriptor, acknowledged);
    }

    private static void ServerMovementSessionHost_StateBaselineAck_SeedsReliableEventAckFloor()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<ServerEcsMovementBody>();
        World world = new(builder.Build());
        ArchetypeSignature signature = new([movementId]);

        void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            body = body with
            {
                VelocityX = input.MoveX / 100f,
                VelocityY = input.MoveY / 100f,
                PositionX = body.PositionX + (input.MoveX / 100f) * deltaSeconds,
                PositionY = body.PositionY + (input.MoveY / 100f) * deltaSeconds,
            };
            Assert.True(movementWorld.SetComponent(movementEntity, body));
        }

        bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            packet = new MovementStatePacket(
                networkEntityId,
                serverTick,
                lastProcessedInputSequence,
                body.PositionX,
                body.PositionY,
                body.VelocityX,
                body.VelocityY);
            return true;
        }

        ServerMovementSessionSpawn SpawnSession(World movementWorld, ConnectionId _)
        {
            Entity entity = movementWorld.CreateDefault(signature);
            Assert.True(movementWorld.SetComponent(entity, new ServerEcsMovementBody(0f, 0f, 0f, 0f)));
            return new ServerMovementSessionSpawn(entity, 1);
        }

        var host = new ServerMovementSessionHost(
            world,
            pair.Server,
            ApplyMovementInput,
            ProjectMovementState,
            SpawnSession);

        StateBaselineDescriptor descriptor = new(4, 60, 55, 7);
        pair.Client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineAckPacket.Plane,
            StateBaselineAckPacket.PacketClass,
            StateBaselineAckPacket.Channel,
            PacketCodec.Encode(new StateBaselineAckPacket(descriptor))));
        pair.Client.FlushOutbound();

        Assert.Equal(1, host.Pump(nowTicks: 1000, deltaSeconds: 0.016f, serverTick: 1));
        Assert.True(host.TryGetLastReliableEventAck(new ConnectionId(1), out ulong acknowledgedSequence));
        Assert.Equal((ulong)55, acknowledgedSequence);
    }

    private static void ServerMovementSessionHost_OwnsSubscriptionsAndDispatchesPublishedBaseline()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<ServerEcsMovementBody>();
        World world = new(builder.Build());
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();

        ArchetypeSignature signature = new([movementId]);

        void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            body = body with
            {
                VelocityX = input.MoveX / 100f,
                VelocityY = input.MoveY / 100f,
                PositionX = body.PositionX + (input.MoveX / 100f) * deltaSeconds,
                PositionY = body.PositionY + (input.MoveY / 100f) * deltaSeconds,
            };
            Assert.True(movementWorld.SetComponent(movementEntity, body));
        }

        bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            packet = new MovementStatePacket(
                networkEntityId,
                serverTick,
                lastProcessedInputSequence,
                body.PositionX,
                body.PositionY,
                body.VelocityX,
                body.VelocityY);
            return true;
        }

        ServerMovementSessionSpawn SpawnSession(World movementWorld, ConnectionId _)
        {
            Entity entity = movementWorld.CreateDefault(signature);
            Assert.True(movementWorld.SetComponent(entity, new ServerEcsMovementBody(0f, 0f, 0f, 0f)));
            return new ServerMovementSessionSpawn(entity, 1);
        }

        var host = new ServerMovementSessionHost(
            world,
            pair.Server,
            ApplyMovementInput,
            ProjectMovementState,
            SpawnSession);
        host.SessionRuntime.BindConnection(new ConnectionId(1));
        Assert.Equal(1, host.SessionRuntime.ConnectionCount);

        host.Subscriptions.Subscribe(new ConnectionId(1), ServerEcsNetworkScopes.OperatorWorld);
        ServerEcsWorldPublishResult published = host.WorldPublisher.PublishRuntimeState();
        Assert.Equal(1, published.WorldCount);

        Assert.True(host.Subscriptions.HasSubscriptions(new ConnectionId(1)));

        host.Pump(400, 0.016f, 50);
        var inbound = new InboundDatagram[8];
        int inboundCount = pair.Client.PollInbound(inbound);
        Assert.Equal(3, inboundCount);

        StateBaselineDescriptor descriptor = default;
        int baselineFrames = 0;
        for (int i = 0; i < inboundCount; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(inbound[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            if (header.PacketId == StateBaselineBeginPacket.PacketId)
            {
                var reader = new UltraSim.Network.BitReader(payload);
                StateBaselineBeginPacket begin = StateBaselineBeginPacket.Deserialize(ref reader);
                descriptor = begin.Descriptor;
            }
            else if (header.PacketId == StateBaselineFramePacket.PacketId)
            {
                baselineFrames++;
            }
        }

        Assert.True(descriptor.BaselineId != 0);
        Assert.Equal(1, baselineFrames);

        pair.Client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            StateBaselineAckPacket.Plane,
            StateBaselineAckPacket.PacketClass,
            StateBaselineAckPacket.Channel,
            PacketCodec.Encode(new StateBaselineAckPacket(descriptor))));
        pair.Client.FlushOutbound();

        host.Pump(402, 0.016f, 51);
        Assert.True(host.TryGetLastStateBaselineAck(new ConnectionId(1), out StateBaselineDescriptor acknowledged));
        Assert.Equal(descriptor, acknowledged);
        Assert.True(descriptor.BaselineId != 0);
    }

    private static void ServerMovementSessionHost_UsesConfiguredOrderedEventAndReplayCapacities()
    {
        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<ServerEcsMovementBody>();
        World world = new(builder.Build());
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();

        void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
        {
        }

        bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
        {
            packet = default;
            return false;
        }

        ServerMovementSessionSpawn SpawnSession(World movementWorld, ConnectionId _)
        {
            Entity entity = movementWorld.CreateDefault(new ArchetypeSignature([movementId]));
            return new ServerMovementSessionSpawn(entity, 1);
        }

        var host = new ServerMovementSessionHost(
            world,
            pair.Server,
            ApplyMovementInput,
            ProjectMovementState,
            SpawnSession,
            runtimeOptions: new ServerNetworkSessionRuntimeOptions(
                MaxConnections: 64,
                MaxSnapshotItems: 512,
                MaxScheduledItems: 256,
                MaxReplayItems: 64,
                MaxDatagrams: 1024,
                OrderedEventCapacity: 1536,
                ReplayBufferCapacityPerConnection: 192,
                RetransmitPolicy: ReliableEventRetransmitPolicy.Default));

        Assert.Equal(1536, host.WorldPublisher.OrderedEventStream.Capacity);
        Assert.Equal(192, host.SessionRuntime.ReplayBufferCapacityPerConnection);
    }

    private static void ServerEcsMovementNetworkBridge_OwnerCorrectionPolicy_SuppressesSmallIntermediateStates()
    {
        using var pair = new LoopbackTransportPair();
        pair.Server.Start();
        pair.Client.Start();
        var builder = new EcsSchemaBuilder();
        ComponentId movementId = builder.RegisterComponent<ServerEcsMovementBody>();
        World world = new(builder.Build());
        ArchetypeSignature signature = new([movementId]);
        Entity entity = world.CreateDefault(signature);
        Assert.True(world.SetComponent(entity, new ServerEcsMovementBody(0f, 0f, 0f, 0f)));

        void ApplyMovementInput(World movementWorld, Entity movementEntity, in MovementInputPacket input, float deltaSeconds)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            float velocityX = input.MoveX / 100f;
            float velocityY = input.MoveY / 100f;
            body = body with
            {
                VelocityX = velocityX,
                VelocityY = velocityY,
                PositionX = body.PositionX + velocityX * deltaSeconds,
                PositionY = body.PositionY + velocityY * deltaSeconds,
            };
            Assert.True(movementWorld.SetComponent(movementEntity, body));
        }

        bool ProjectMovementState(World movementWorld, Entity movementEntity, uint networkEntityId, ushort lastProcessedInputSequence, uint serverTick, out MovementStatePacket packet)
        {
            Assert.True(movementWorld.TryGetComponent(movementEntity, out ServerEcsMovementBody body));
            packet = new MovementStatePacket(
                networkEntityId,
                serverTick,
                lastProcessedInputSequence,
                body.PositionX,
                body.PositionY,
                body.VelocityX,
                body.VelocityY);
            return true;
        }

        var server = new ServerEcsMovementNetworkBridge(
            world,
            pair.Server,
            ApplyMovementInput,
            ProjectMovementState,
            new ServerMovementOwnerCorrectionPolicy(positionDeadband: 0.1f, maxSilentTicks: 3));
        server.Bind(new ConnectionId(1), entity, networkEntityId: 91);

        pair.Client.EnqueueOutbound(new OutboundDatagram(
            new ConnectionId(1),
            MovementInputPacket.Plane,
            MovementInputPacket.PacketClass,
            MovementInputPacket.Channel,
            PacketCodec.Encode(new MovementInputPacket(1, 1000, 80, 0, 0))));
        pair.Client.FlushOutbound();

        Assert.Equal(1, server.PollInputs());
        Assert.Equal(1, server.AdvanceAndSend(0.016f, serverTick: 1));
        Assert.Equal(1, server.LastSentStateCount);

        Assert.Equal(0, server.AdvanceAndSend(0.016f, serverTick: 2));
        Assert.Equal(0, server.LastSentStateCount);

        Assert.Equal(0, server.AdvanceAndSend(0.016f, serverTick: 3));
        Assert.Equal(0, server.LastSentStateCount);

        Assert.Equal(1, server.AdvanceAndSend(0.016f, serverTick: 4));
        Assert.Equal(1, server.LastSentStateCount);
    }

    private static World CreateEcsTestWorld(bool enableProfiling, bool registerSystems = true)
    {
        var builder = new EcsSchemaBuilder();
        builder.RegisterComponent<ServerEcsPosition>();
        World world = new(builder.Build());
        world.ProfilingEnabled = enableProfiling;

        if (registerSystems)
        {
            world.RegisterSystem(new ServerEcsTestSystem(
                new SystemDescriptor(
                    "ServerEcsTestSystem",
                    SystemStage.AI,
                    defaultPeriodTicks: 1,
                    defaultStartOffset: 0,
                    ComponentMask.Empty,
                    ComponentMask.Empty)));
        }

        world.Initialize();
        return world;
    }

    private static int PollUntilNonZero<TState>(Func<TState, int> poll, TState state, int maxAttempts)
    {
        for (int i = 0; i < maxAttempts; i++)
        {
            int result = poll(state);
            if (result > 0)
                return result;

            Thread.SpinWait(64);
        }

        return 0;
    }

    private static StateBaselineDescriptor ReadBaselineDescriptor(INetworkTransport transport)
    {
        var inbound = new InboundDatagram[8];
        int count = transport.PollInbound(inbound);
        for (int i = 0; i < count; i++)
        {
            Assert.True(PacketCodec.TryDecodeEnvelope(inbound[i].Payload.Span, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> payload));
            if (header.PacketId != StateBaselineBeginPacket.PacketId)
                continue;

            var reader = new UltraSim.Network.BitReader(payload);
            StateBaselineBeginPacket begin = StateBaselineBeginPacket.Deserialize(ref reader);
            return begin.Descriptor;
        }

        throw new InvalidOperationException("Expected a baseline begin packet.");
    }

    private static void AssertMethod(Type contract, string name, Type returnType, params Type[] parameterTypes)
    {
        MethodInfo? method = contract.GetMethod(name, parameterTypes);
        Assert.NotNull(method);
        Assert.Equal(returnType, method!.ReturnType);
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
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void AlmostEqual(float expected, float actual, float tolerance = 0.0001f)
    {
        if (MathF.Abs(expected - actual) > tolerance)
            throw new InvalidOperationException($"Expected '{expected}' ± {tolerance}, got '{actual}'.");
    }

    public static void True(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected condition to be true.");
    }

    public static void NotNull(object? value)
    {
        if (value is null)
            throw new InvalidOperationException("Expected value to be non-null.");
    }

    public static T NotNull<T>(T? value)
        where T : class
    {
        if (value is null)
            throw new InvalidOperationException("Expected value to be non-null.");

        return value;
    }

    public static void False(bool value)
    {
        if (value)
            throw new InvalidOperationException("Expected condition to be false.");
    }

    public static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected exception of type {typeof(TException).Name}.");
    }

    public static void SequenceEqual<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual)
    {
        if (expected.Count != actual.Count)
            throw new InvalidOperationException($"Expected sequence count {expected.Count}, got {actual.Count}.");

        for (int i = 0; i < expected.Count; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                throw new InvalidOperationException($"Sequence mismatch at {i}: expected '{expected[i]}', got '{actual[i]}'.");
        }
    }

    public static void Contains(string expectedSubstring, string actual)
    {
        if (actual is null || !actual.Contains(expectedSubstring, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected substring '{expectedSubstring}' was not found.");
    }
}

internal sealed class RecordingHostSink : IServerHostEventSink
{
    public List<ServerApplicationEvent> Events { get; } = new();

    public void OnEvent(ServerApplicationEvent evt)
    {
        Events.Add(evt);
    }
}

internal readonly record struct ServerEcsPosition(int X, int Y) : IComponent<ServerEcsPosition>;
internal readonly record struct ServerEcsEvent(int EventCode) : IComponent<ServerEcsEvent>;
internal readonly record struct ServerEcsAoiEvent(int X, int Y, int EventCode) : IComponent<ServerEcsAoiEvent>;
internal readonly record struct ServerEcsMovementBody(float PositionX, float PositionY, float VelocityX, float VelocityY) : IComponent<ServerEcsMovementBody>;

internal sealed class ServerEcsTestSystem : SystemBase
{
    public ServerEcsTestSystem(SystemDescriptor descriptor)
    {
        Descriptor = descriptor;
    }

    public override SystemDescriptor Descriptor { get; }

    public override void Update(in SystemContext ctx)
    {
    }
}
