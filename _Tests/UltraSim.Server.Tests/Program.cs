using System.Reflection;
using UltraSim.Server;
using UltraSim.Server.Hosting;

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
        Run(nameof(ServerHostLoop_Pump_TicksAtFixedStepCadence), ServerHostLoop_Pump_TicksAtFixedStepCadence);
        Run(nameof(ServerHostLoop_Pump_RespectsCatchUpCap), ServerHostLoop_Pump_RespectsCatchUpCap);
        Run(nameof(ServerHostLoop_Pump_DrainsQueuedCommandsBeforeTicks), ServerHostLoop_Pump_DrainsQueuedCommandsBeforeTicks);
        Run(nameof(ServerHostLoop_ForwardsApplicationEventsToSinks), ServerHostLoop_ForwardsApplicationEventsToSinks);

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
}

internal sealed class RecordingHostSink : IServerHostEventSink
{
    public List<ServerApplicationEvent> Events { get; } = new();

    public void OnEvent(ServerApplicationEvent evt)
    {
        Events.Add(evt);
    }
}
