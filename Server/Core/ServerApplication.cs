using System.Diagnostics;

namespace UltraSim.Server;

public sealed class ServerApplication : IServerApplication
{
    private readonly ServerApplicationHooks _hooks;
    private bool _hasInitialized;
    private bool _saveRequested;
    private DateTimeOffset? _startedAtUtc;
    private long _tickCount;
    private long _totalCommands;
    private long _totalSaveRequests;
    private long _totalCompletedSaves;
    private long _totalFaults;
    private double _tickMillisecondsTotal;
    private double _maxTickMilliseconds;
    private int _peakSessions;

    public ServerApplication(
        ServerApplicationOptions? options = null,
        ServerApplicationHooks? hooks = null)
    {
        Options = options ?? ServerApplicationOptions.Default;
        _hooks = hooks ?? new ServerApplicationHooks();
        State = ServerApplicationState.Created;
    }

    public ServerApplicationOptions Options { get; }
    public ServerApplicationFault? LastFault { get; private set; }
    public ServerApplicationState State { get; private set; }
    public bool IsInitialized => _hasInitialized && State != ServerApplicationState.Faulted;
    public bool IsRunning => State == ServerApplicationState.Running;

    public event Action<ServerApplicationEvent>? EventRaised;

    public void Initialize()
    {
        EnsureState(nameof(Initialize), ServerApplicationState.Created, ServerApplicationState.Stopped);
        ExecuteLifecycleOperation(nameof(Initialize), () =>
        {
            TransitionTo(ServerApplicationState.Initializing);
            _hooks.Initialize?.Invoke(this);
            _hasInitialized = true;
            TransitionTo(ServerApplicationState.Initialized);
        });
    }

    public void Start()
    {
        if (State == ServerApplicationState.Running)
            return;

        if (!_hasInitialized)
            Initialize();

        EnsureState(nameof(Start), ServerApplicationState.Initialized, ServerApplicationState.Stopped);
        ExecuteLifecycleOperation(nameof(Start), () =>
        {
            TransitionTo(ServerApplicationState.Starting);
            _hooks.Start?.Invoke(this);

            TransitionTo(ServerApplicationState.Loading);
            _hooks.Load?.Invoke(this);

            _startedAtUtc = DateTimeOffset.UtcNow;
            TransitionTo(ServerApplicationState.Running);
        });
    }

    public void Tick(float deltaSeconds)
    {
        EnsureState(nameof(Tick), ServerApplicationState.Running);
        ExecuteLifecycleOperation(nameof(Tick), () =>
        {
            long start = Stopwatch.GetTimestamp();
            _hooks.Tick?.Invoke(this, deltaSeconds);
            _tickCount++;

            double elapsedMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            _tickMillisecondsTotal += elapsedMs;
            if (elapsedMs > _maxTickMilliseconds)
                _maxTickMilliseconds = elapsedMs;

            if (_saveRequested)
            {
                _saveRequested = false;
                SaveCore();
            }
        });
    }

    public void RequestSave()
    {
        _totalSaveRequests++;
        _saveRequested = true;
        RaiseEvent(ServerApplicationEventKind.SaveRequested, "save requested", State);
    }

    public bool Save()
    {
        EnsureState(nameof(Save), ServerApplicationState.Initialized, ServerApplicationState.Running, ServerApplicationState.Faulted);
        ExecuteLifecycleOperation(nameof(Save), SaveCore);
        return true;
    }

    public void Stop()
    {
        if (State == ServerApplicationState.Stopped)
            return;

        if (State == ServerApplicationState.Created)
        {
            TransitionTo(ServerApplicationState.Stopped);
            return;
        }

        EnsureState(nameof(Stop), ServerApplicationState.Initialized, ServerApplicationState.Running, ServerApplicationState.Faulted);
        ExecuteLifecycleOperation(nameof(Stop), () =>
        {
            TransitionTo(ServerApplicationState.Stopping);

            if (Options.SaveOnStop)
                SaveCore();

            _hooks.Stop?.Invoke(this);
            _saveRequested = false;
            TransitionTo(ServerApplicationState.Stopped);
        });
    }

    public void Restart()
    {
        EnsureState(nameof(Restart), ServerApplicationState.Initialized, ServerApplicationState.Running, ServerApplicationState.Stopped, ServerApplicationState.Faulted);
        ExecuteLifecycleOperation(nameof(Restart), () =>
        {
            TransitionTo(ServerApplicationState.Restarting);

            if (State != ServerApplicationState.Stopped)
            {
                _hooks.Stop?.Invoke(this);
                _saveRequested = false;
            }

            TransitionTo(ServerApplicationState.Stopped);
            Initialize();
            Start();
        });
    }

    public ServerStatusSnapshot GetStatusSnapshot()
    {
        int activeSessions = _hooks.GetActiveSessions?.Invoke(this) ?? 0;
        if (activeSessions > _peakSessions)
            _peakSessions = activeSessions;

        DateTimeOffset? startedAt = _startedAtUtc;
        TimeSpan uptime = startedAt.HasValue && State != ServerApplicationState.Created
            ? DateTimeOffset.UtcNow - startedAt.Value
            : TimeSpan.Zero;

        return new ServerStatusSnapshot(
            Options.ProfileName,
            State,
            IsInitialized,
            IsRunning,
            _hooks.IsAcceptingConnections?.Invoke(this) ?? false,
            _tickCount,
            activeSessions,
            _peakSessions,
            startedAt,
            uptime);
    }

    public ServerMetricsSnapshot GetMetricsSnapshot()
    {
        double meanTickMilliseconds = _tickCount == 0 ? 0d : _tickMillisecondsTotal / _tickCount;
        return new ServerMetricsSnapshot(
            _tickCount,
            _totalCommands,
            _totalSaveRequests,
            _totalCompletedSaves,
            _totalFaults,
            meanTickMilliseconds,
            _maxTickMilliseconds);
    }

    public IReadOnlyList<ServerPlayerSessionSnapshot> GetPlayerSessionsSnapshot()
    {
        return _hooks.GetPlayerSessions?.Invoke(this) ?? Array.Empty<ServerPlayerSessionSnapshot>();
    }

    public IReadOnlyList<ServerAccountSummarySnapshot> GetAccountSummariesSnapshot()
    {
        return _hooks.GetAccountSummaries?.Invoke(this) ?? Array.Empty<ServerAccountSummarySnapshot>();
    }

    public bool TryExecuteCommand(ServerControlCommand command, out ServerCommandResult result)
    {
        _totalCommands++;

        try
        {
            switch (command.Kind)
            {
                case ServerControlCommandKind.Start:
                    if (State == ServerApplicationState.Running)
                    {
                        result = ServerCommandResult.NoOp("runtime already running");
                        return true;
                    }

                    Start();
                    result = ServerCommandResult.Accepted("runtime started");
                    return true;

                case ServerControlCommandKind.Stop:
                case ServerControlCommandKind.Shutdown:
                    if (State == ServerApplicationState.Stopped)
                    {
                        result = ServerCommandResult.NoOp("runtime already stopped");
                        return true;
                    }

                    Stop();
                    result = ServerCommandResult.Accepted("runtime stopped");
                    return true;

                case ServerControlCommandKind.Restart:
                    Restart();
                    result = ServerCommandResult.Accepted("runtime restarted");
                    return true;

                case ServerControlCommandKind.Save:
                    Save();
                    result = ServerCommandResult.Accepted("save completed");
                    return true;

                case ServerControlCommandKind.Status:
                    ServerStatusSnapshot snapshot = GetStatusSnapshot();
                    result = ServerCommandResult.Accepted($"{snapshot.State} ticks={snapshot.TickCount} sessions={snapshot.ActiveSessions}");
                    return true;

                default:
                    result = ServerCommandResult.Unknown("unknown command");
                    return false;
            }
        }
        finally
        {
            RaiseEvent(ServerApplicationEventKind.CommandHandled, $"command handled: {command.Kind}", State);
        }
    }

    private void SaveCore()
    {
        ServerApplicationState previousState = State;
        TransitionTo(ServerApplicationState.Saving);
        _hooks.Save?.Invoke(this);
        _totalCompletedSaves++;
        RaiseEvent(ServerApplicationEventKind.SaveCompleted, "save completed", ServerApplicationState.Saving);
        TransitionTo(previousState == ServerApplicationState.Saving ? ServerApplicationState.Initialized : previousState);
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
        ServerApplicationFault fault = new(State, operation, exception, DateTimeOffset.UtcNow);
        LastFault = fault;
        _totalFaults++;
        TransitionTo(ServerApplicationState.Faulted);
        RaiseEvent(ServerApplicationEventKind.Faulted, $"{operation}: {fault.Message}", ServerApplicationState.Faulted);
    }

    private void TransitionTo(ServerApplicationState nextState)
    {
        if (State == nextState)
            return;

        State = nextState;
        RaiseEvent(ServerApplicationEventKind.StateChanged, $"state changed: {nextState}", nextState);
    }

    private void EnsureState(string operation, params ServerApplicationState[] validStates)
    {
        for (int i = 0; i < validStates.Length; i++)
        {
            if (State == validStates[i])
                return;
        }

        throw new InvalidOperationException($"{operation} is not valid while runtime state is {State}.");
    }

    private void RaiseEvent(ServerApplicationEventKind kind, string message, ServerApplicationState? state)
    {
        EventRaised?.Invoke(new ServerApplicationEvent(
            kind,
            DateTimeOffset.UtcNow,
            message,
            state));
    }
}
