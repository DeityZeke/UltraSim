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
    private double _autoSaveElapsedSeconds;
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
            long loadStart = Stopwatch.GetTimestamp();
            _hooks.Load?.Invoke(this);
            TimeSpan loadDuration = Stopwatch.GetElapsedTime(loadStart);

            _startedAtUtc = DateTimeOffset.UtcNow;
            TransitionTo(ServerApplicationState.Running, loadDuration);
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
            RequestAutoSaveIfDue(deltaSeconds);

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

    public void Log(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return;

        RaiseEvent(ServerApplicationEventKind.Log, message.Trim(), State);
    }

    public void Stop()
    {
        Stop(Options.SaveOnStop);
    }

    public void Stop(bool saveBeforeStop)
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

            if (saveBeforeStop)
                SaveCore();

            _hooks.Stop?.Invoke(this);
            _saveRequested = false;
            TransitionTo(ServerApplicationState.Stopped);
        });
    }

    public void Restart()
    {
        Restart(saveBeforeRestart: true);
    }

    public void Restart(bool saveBeforeRestart)
    {
        EnsureState(nameof(Restart), ServerApplicationState.Initialized, ServerApplicationState.Running, ServerApplicationState.Stopped, ServerApplicationState.Faulted);
        ExecuteLifecycleOperation(nameof(Restart), () =>
        {
            ServerApplicationState previousState = State;
            TransitionTo(ServerApplicationState.Restarting);

            if (saveBeforeRestart &&
                previousState is ServerApplicationState.Initialized or ServerApplicationState.Running or ServerApplicationState.Faulted)
            {
                SaveCore();
            }

            if (previousState != ServerApplicationState.Stopped)
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

    public IReadOnlyList<ServerSystemRuntimeSnapshot> GetSystemRuntimeSnapshots()
    {
        return _hooks.GetSystemRuntimeSnapshots?.Invoke(this) ?? Array.Empty<ServerSystemRuntimeSnapshot>();
    }

    public IReadOnlyList<ServerPlayerSessionSnapshot> GetPlayerSessionsSnapshot()
    {
        return _hooks.GetPlayerSessions?.Invoke(this) ?? Array.Empty<ServerPlayerSessionSnapshot>();
    }

    public IReadOnlyList<ServerAccountSummarySnapshot> GetAccountSummariesSnapshot()
    {
        return _hooks.GetAccountSummaries?.Invoke(this) ?? Array.Empty<ServerAccountSummarySnapshot>();
    }

    public ServerPlayerSessionActionResult KickPlayerSession(string sessionId)
    {
        if (_hooks.KickPlayerSession is null)
            return ServerPlayerSessionActionResult.Reject(sessionId, "Player session actions are not available.");

        return _hooks.KickPlayerSession(this, sessionId);
    }

    public ServerAccountActionResult BanAccount(string accountId)
    {
        if (_hooks.BanAccount is null)
            return ServerAccountActionResult.Reject(accountId, "Account actions are not available.");

        return _hooks.BanAccount(this, accountId);
    }

    public ServerAccountAccessUpdateResult ReplaceAccountAccess(
        string accountId,
        int accessLevel,
        IReadOnlyList<string> directPermissionGrants,
        IReadOnlyList<string> directPermissionDenies)
    {
        if (_hooks.ReplaceAccountAccess is null)
            return ServerAccountAccessUpdateResult.Reject(accountId, "Account access editing is not available.");

        return _hooks.ReplaceAccountAccess(
            this,
            accountId,
            accessLevel,
            directPermissionGrants ?? Array.Empty<string>(),
            directPermissionDenies ?? Array.Empty<string>());
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
                    if (State == ServerApplicationState.Stopped)
                    {
                        result = ServerCommandResult.NoOp("runtime already stopped");
                        return true;
                    }

                    Stop();
                    result = ServerCommandResult.Accepted("runtime stopped");
                    return true;

                case ServerControlCommandKind.Shutdown:
                    if (State == ServerApplicationState.Stopped)
                    {
                        result = ServerCommandResult.NoOp("runtime already stopped");
                        return true;
                    }

                    bool shutdownWithoutSave = IsWithoutSave(command);
                    Stop(saveBeforeStop: !shutdownWithoutSave);
                    result = ServerCommandResult.Accepted(shutdownWithoutSave
                        ? "runtime stopped without save"
                        : "runtime stopped");
                    return true;

                case ServerControlCommandKind.Restart:
                    bool restartWithoutSave = IsWithoutSave(command);
                    Restart(saveBeforeRestart: !restartWithoutSave);
                    result = ServerCommandResult.Accepted(restartWithoutSave
                        ? "runtime restarted without save"
                        : "runtime restarted");
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
                    ServerCommandResult? hookResult = _hooks.ExecuteCommand?.Invoke(this, command);
                    if (hookResult.HasValue)
                    {
                        result = hookResult.Value;
                        return result.Kind != ServerCommandResultKind.Unknown;
                    }

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
        long saveStart = Stopwatch.GetTimestamp();
        _hooks.Save?.Invoke(this);
        TimeSpan saveDuration = Stopwatch.GetElapsedTime(saveStart);
        _autoSaveElapsedSeconds = 0.0;
        _totalCompletedSaves++;
        RaiseEvent(ServerApplicationEventKind.SaveCompleted, "save completed", ServerApplicationState.Saving, saveDuration);
        State = previousState == ServerApplicationState.Saving ? ServerApplicationState.Initialized : previousState;
    }

    private void RequestAutoSaveIfDue(float deltaSeconds)
    {
        if (Options.AutoSaveIntervalSeconds <= 0.0 || _hooks.Save is null)
            return;

        _autoSaveElapsedSeconds += Math.Max(0.0f, deltaSeconds);
        if (_autoSaveElapsedSeconds < Options.AutoSaveIntervalSeconds)
            return;

        RequestSave();
    }

    private static bool IsWithoutSave(ServerControlCommand command)
    {
        return string.Equals(command.Argument, "without-save", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(command.Argument, "no-save", StringComparison.OrdinalIgnoreCase);
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

    private void TransitionTo(ServerApplicationState nextState, TimeSpan? duration = null)
    {
        if (State == nextState)
            return;

        State = nextState;
        RaiseEvent(ServerApplicationEventKind.StateChanged, $"state changed: {nextState}", nextState, duration);
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

    private void RaiseEvent(
        ServerApplicationEventKind kind,
        string message,
        ServerApplicationState? state,
        TimeSpan? duration = null)
    {
        EventRaised?.Invoke(new ServerApplicationEvent(
            kind,
            DateTimeOffset.UtcNow,
            message,
            state,
            duration));
    }
}
