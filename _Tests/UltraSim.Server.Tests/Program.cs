using System.Reflection;
using UltraSim.Server;
using UltraSim.Server.Console;
using UltraSim.Server.Godot;
using UltraSim.Server.Hosting;
using UltraSim.Server.Headless;

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
        Run(nameof(ServerApplication_Faults_WhenHookThrows), ServerApplication_Faults_WhenHookThrows);
        Run(nameof(ServerApplication_StatusAndMetricsSnapshots_ReflectRuntimeActivity), ServerApplication_StatusAndMetricsSnapshots_ReflectRuntimeActivity);
        Run(nameof(ServerApplication_PlayerAndAccountSnapshots_ReflectHookData), ServerApplication_PlayerAndAccountSnapshots_ReflectHookData);
        Run(nameof(ServerHostLoop_Pump_TicksAtFixedStepCadence), ServerHostLoop_Pump_TicksAtFixedStepCadence);
        Run(nameof(ServerHostLoop_Pump_RespectsCatchUpCap), ServerHostLoop_Pump_RespectsCatchUpCap);
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
        IReadOnlyList<ServerPlayerSessionSnapshot> players = controller.GetPlayerSessionsSnapshot();
        IReadOnlyList<ServerAccountSummarySnapshot> accounts = controller.GetAccountSummariesSnapshot();
        controller.Shutdown();

        Assert.Equal(ServerApplicationState.Stopped, controller.State);
        Assert.Equal(ServerCommandResultKind.Accepted, status.Kind);
        Assert.Equal(1, players.Count);
        Assert.Equal(1, accounts.Count);
        Assert.True(observed.Count > 0);
        Assert.Equal(ServerApplicationEventKind.StateChanged, observed[0].Kind);
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
