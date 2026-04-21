using System.Reflection;
using UltraSim.Server;

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
}
