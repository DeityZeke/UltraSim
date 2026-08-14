using UltraSim.ECS;

namespace UltraSim.Server.Runtime;

/// <summary>
/// Concrete reusable authoritative runtime shared by all hosts.
/// </summary>
/// <remarks>
/// This type owns lifecycle and one active <see cref="ServerGeneration"/> root.
/// Game policy, account state, inventory state, UI, networking, and host
/// services are deliberately outside this surface.
/// </remarks>
public sealed class ServerRuntime : IDisposable
{
    private readonly int _ownerThreadId;
    private readonly IServerComposition _composition;
    private readonly ComponentSchema _schema;
    private readonly ServerRuntimeOptions _options;
    private ServerGeneration? _currentGeneration;
    private ulong _nextGenerationId = 1;
    private ServerRuntimeState _state = ServerRuntimeState.Created;
    private RuntimeOperation _operation;

    public ServerRuntime(IServerComposition composition, ServerRuntimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(composition);
        ArgumentNullException.ThrowIfNull(options);
        ComponentSchema schema = composition.Schema ??
            throw new ArgumentException("Server composition returned no component schema.", nameof(composition));

        _ownerThreadId = Environment.CurrentManagedThreadId;
        _composition = composition;
        _schema = schema;
        _options = options;
        Game = new ServerGameFacet(this);
    }

    /// <summary>
    /// Trusted callback-scoped game authority over the active generation.
    /// Hosts deliberately do not expose this facet.
    /// </summary>
    public ServerGameFacet Game { get; }

    public ServerRuntimeState State
    {
        get
        {
            EnsureOwnerThread();
            return _state;
        }
    }

    internal ServerGeneration CurrentGeneration
    {
        get
        {
            EnsureOwnerThread();
            if (_state == ServerRuntimeState.Disposed)
                throw new ObjectDisposedException(nameof(ServerRuntime));
            return _currentGeneration ??
                throw new InvalidOperationException("The server runtime has no active generation.");
        }
    }

    internal ServerRuntimeOptions OptionsForTesting => _options;

    internal bool TryUseActiveGeneration<TResult>(
        ServerGenerationOperation<TResult> operation,
        out TResult result)
    {
        EnsureOwnerThread();
        if (_state != ServerRuntimeState.Running || _currentGeneration is null)
        {
            result = default!;
            return false;
        }

        RuntimeOperation previousOperation = EnterGameCallback(nameof(TryUseActiveGeneration));
        ServerGeneration borrowed = _currentGeneration;
        _operation = RuntimeOperation.GameCallback;
        try
        {
            result = operation(borrowed.EcsWorld, borrowed.GenerationId);
            EnsureBorrowedGenerationUnchanged(borrowed);
            return true;
        }
        catch
        {
            // A trusted game callback may already have mutated the active ECS.
            // Its failure therefore has the same terminal policy as a failed
            // simulation step: there is no provable rollback boundary.
            if (_state == ServerRuntimeState.Running)
                _state = ServerRuntimeState.Faulted;
            throw;
        }
        finally
        {
            _operation = previousOperation;
        }
    }

    internal bool TryUseActiveGeneration<TState, TResult>(
        ServerGenerationStateOperation<TState, TResult> operation,
        out TResult result)
        where TState : class
    {
        EnsureOwnerThread();
        if (_state != ServerRuntimeState.Running ||
            _currentGeneration is null ||
            _currentGeneration.GameState is not TState gameState)
        {
            result = default!;
            return false;
        }

        RuntimeOperation previousOperation = EnterGameCallback(nameof(TryUseActiveGeneration));
        ServerGeneration borrowed = _currentGeneration;
        _operation = RuntimeOperation.GameCallback;
        try
        {
            result = operation(
                borrowed.EcsWorld,
                borrowed.GenerationId,
                gameState);
            EnsureBorrowedGenerationUnchanged(borrowed);
            return true;
        }
        catch
        {
            if (_state == ServerRuntimeState.Running)
                _state = ServerRuntimeState.Faulted;
            throw;
        }
        finally
        {
            _operation = previousOperation;
        }
    }

    internal bool TryPublishPreparedGeneration(
        EcsWorld preparedWorld,
        object? preparedGameState,
        PersistentWorldClockState? persistentClockState,
        out ulong generationId)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(preparedWorld);
        if (_currentGeneration is not null && ReferenceEquals(preparedWorld, _currentGeneration.EcsWorld))
            throw new InvalidOperationException("The active ECS world cannot be published as its own replacement.");
        if (_currentGeneration is not null && preparedGameState is not null &&
            ReferenceEquals(preparedGameState, _currentGeneration.GameState))
            throw new InvalidOperationException("Active game state cannot be published as its own replacement.");

        ServerGeneration? candidate = null;
        try
        {
            EnsureRunning(nameof(TryPublishPreparedGeneration));
            EnsureIdleOperation(nameof(TryPublishPreparedGeneration));
            ServerGeneration active = _currentGeneration!;
            if (preparedWorld.OwnerThreadId != _ownerThreadId)
                throw new InvalidOperationException("A prepared ECS world belongs to a different owner thread.");
            if (!preparedWorld.IsRunning || !preparedWorld.HasScheduler)
                throw new InvalidOperationException("A prepared generation requires a running scheduled ECS world.");
            if (preparedWorld.Schema.Id != _schema.Id)
                throw new InvalidOperationException("A prepared generation uses a different ECS schema.");
            if (_nextGenerationId == ulong.MaxValue)
                throw new InvalidOperationException("The server generation identity space is exhausted.");

            int playbackRate = active.Clock.PlaybackRate;
            int simulationFrequencyHz = active.Clock.SimulationFrequencyHz;
            PersistentWorldClockState clockState = persistentClockState ??
                active.Clock.CapturePersistentState();
            candidate = new ServerGeneration(
                _nextGenerationId,
                preparedWorld,
                new WorldClock(_options.ClockOptions),
                preparedGameState);
            candidate.Clock.SetSimulationFrequency(simulationFrequencyHz);
            candidate.Clock.RestorePersistentState(clockState);
            candidate.Clock.SetPlaybackRate(playbackRate);
            _operation = RuntimeOperation.Publication;
            try
            {
                PublishCandidate(candidate);
                _operation = RuntimeOperation.PublicationNotification;
                Game.NotifyGenerationPublished(candidate.GenerationId);
            }
            finally
            {
                _operation = RuntimeOperation.None;
            }
            generationId = candidate.GenerationId;
            _nextGenerationId++;
            return true;
        }
        catch
        {
            // Once PublishCandidate marks the candidate, it is the active
            // terminal root. Before that boundary every transferred candidate
            // object must be reclaimed together.
            if (candidate is null)
                DisposePreparedGeneration(preparedWorld, preparedGameState);
            else if (!candidate.IsPublished)
                candidate.Dispose();
            else
                _state = ServerRuntimeState.Faulted;
            throw;
        }
    }

    internal bool TryCapturePersistentClockState(out PersistentWorldClockState state)
    {
        EnsureOwnerThread();
        if (_state != ServerRuntimeState.Running || _currentGeneration is null)
        {
            state = default;
            return false;
        }
        EnsureIdleOperation(nameof(TryCapturePersistentClockState));
        state = _currentGeneration.Clock.CapturePersistentState();
        return true;
    }

    /// <summary>Identity of the currently active authoritative generation.</summary>
    public ulong ActiveGenerationId
    {
        get
        {
            EnsureOwnerThread();
            return GetActiveGeneration().GenerationId;
        }
    }

    /// <summary>Completed fixed simulation steps in the active generation.</summary>
    public ulong SimulationTick
    {
        get
        {
            EnsureOwnerThread();
            return GetActiveGeneration().Clock.SimulationTick;
        }
    }

    /// <summary>Authoritative generation-owned world time in integer microseconds.</summary>
    public long WorldTimeMicroseconds
    {
        get
        {
            EnsureOwnerThread();
            return GetActiveGeneration().Clock.WorldTimeMicroseconds;
        }
    }

    /// <summary>Current host-selected fixed-step cadence.</summary>
    public int SimulationFrequencyHz
    {
        get
        {
            EnsureOwnerThread();
            return GetActiveGeneration().Clock.SimulationFrequencyHz;
        }
    }

    /// <summary>Elapsed simulation seconds supplied to the next ECS step.</summary>
    public double SimulationDeltaSeconds
    {
        get
        {
            EnsureOwnerThread();
            return GetActiveGeneration().Clock.FixedStepSeconds;
        }
    }

    /// <summary>Current host playback multiplier.</summary>
    public int PlaybackRate
    {
        get
        {
            EnsureOwnerThread();
            return GetActiveGeneration().Clock.PlaybackRate;
        }
    }

    public void Initialize()
    {
        EnsureOwnerThread();
        EnsureIdleOperation(nameof(Initialize));
        EnsureState(ServerRuntimeState.Created, nameof(Initialize));

        try
        {
            ServerGeneration candidate = BuildPreparedCandidate(_nextGenerationId);
            PublishCandidate(candidate);
            _nextGenerationId = checked(_nextGenerationId + 1);
            _state = ServerRuntimeState.Initialized;
        }
        catch
        {
            if (_state != ServerRuntimeState.Faulted)
                _state = ServerRuntimeState.Faulted;
            throw;
        }
    }

    public void Start()
    {
        EnsureOwnerThread();
        EnsureIdleOperation(nameof(Start));
        EnsureState(ServerRuntimeState.Initialized, nameof(Start));
        _state = ServerRuntimeState.Running;
    }

    /// <summary>Executes exactly one fixed simulation step.</summary>
    public void Tick()
    {
        EnsureOwnerThread();
        EnsureRunning(nameof(Tick));
        EnsureIdleOperation(nameof(Tick));
        ExecuteSimulationStep();
    }

    /// <summary>
    /// Advances from host-provided monotonic elapsed time using fixed simulation
    /// steps and the generation's playback rate.
    /// </summary>
    /// <returns>The number of fixed simulation steps executed.</returns>
    public int Tick(TimeSpan hostElapsed)
    {
        EnsureOwnerThread();
        EnsureRunning(nameof(Tick));
        EnsureIdleOperation(nameof(Tick));

        // Host elapsed validation and step-count calculation are transactional
        // expected-input work. They must reject before simulation state changes
        // and must not terminally fault an otherwise healthy generation.
        int steps = _currentGeneration!.Clock.ConsumeHostElapsed(hostElapsed);
        for (int index = 0; index < steps; index++)
            ExecuteSimulationStep();
        return steps;
    }

    /// <summary>Changes host playback speed when the host explicitly allows it.</summary>
    public void SetPlaybackRate(int playbackRate)
    {
        EnsureOwnerThread();
        EnsureIdleOperation(nameof(SetPlaybackRate));
        if (_state is not (ServerRuntimeState.Initialized or ServerRuntimeState.Running))
            throw new InvalidOperationException(
                $"SetPlaybackRate is invalid while the runtime is {_state}.");
        if (!_options.AllowVariablePlayback)
            throw new InvalidOperationException("This runtime uses fixed authoritative playback at 1x.");

        _currentGeneration!.Clock.SetPlaybackRate(playbackRate);
    }

    /// <summary>
    /// Changes the fixed-step cadence at an explicit idle owner boundary.
    /// </summary>
    /// <remarks>
    /// The operation preserves authoritative tick, world time, fractional
    /// time, playback, ECS state, and game state. Any partial host elapsed-time
    /// residue is discarded deliberately; future elapsed time is interpreted
    /// only under the new cadence. Cadence is host execution policy and is not
    /// restored from a durable save.
    /// </remarks>
    public void SetSimulationFrequency(int simulationFrequencyHz)
    {
        EnsureOwnerThread();
        EnsureIdleOperation(nameof(SetSimulationFrequency));
        if (_state is not (ServerRuntimeState.Initialized or ServerRuntimeState.Running))
        {
            throw new InvalidOperationException(
                $"SetSimulationFrequency is invalid while the runtime is {_state}.");
        }
        if (!_options.AllowVariableSimulationFrequency)
        {
            throw new InvalidOperationException(
                "This runtime uses a fixed simulation frequency. Variable cadence must be opted in at construction.");
        }

        // WorldClock validates before mutating, so an unsupported cadence is a
        // transactional expected-input rejection rather than a Runtime fault.
        _currentGeneration!.Clock.SetSimulationFrequency(simulationFrequencyHz);
    }

    /// <summary>
    /// Builds and validates a private replacement, then publishes it with one
    /// active-root assignment.
    /// </summary>
    internal void ReplaceGeneration()
    {
        EnsureOwnerThread();
        EnsureRunning(nameof(ReplaceGeneration));
        EnsureIdleOperation(nameof(ReplaceGeneration));
        if (_nextGenerationId == ulong.MaxValue)
            throw new InvalidOperationException("The server generation identity space is exhausted.");

        ServerGeneration candidate = BuildPreparedCandidate(_nextGenerationId);
        try
        {
            _operation = RuntimeOperation.Publication;
            try
            {
                PublishCandidate(candidate);
                _operation = RuntimeOperation.PublicationNotification;
                Game.NotifyGenerationPublished(candidate.GenerationId);
            }
            finally { _operation = RuntimeOperation.None; }
            _nextGenerationId++;
        }
        catch
        {
            // A candidate that crossed the publication boundary is now the
            // active terminal root and must remain owned by this runtime. A
            // pre-publication failure is still private and must be reclaimed.
            if (!candidate.IsPublished)
                candidate.Dispose();
            else
                _state = ServerRuntimeState.Faulted;
            throw;
        }
    }

    public void Stop()
    {
        EnsureOwnerThread();
        EnsureIdleOperation(nameof(Stop));
        if (_state == ServerRuntimeState.Stopped)
            return;
        EnsureState(ServerRuntimeState.Running, nameof(Stop));
        _state = ServerRuntimeState.Stopped;
    }

    public void Dispose()
    {
        EnsureOwnerThread();
        EnsureIdleOperation(nameof(Dispose));
        if (_state == ServerRuntimeState.Disposed)
            return;

        try
        {
            _currentGeneration?.Dispose();
        }
        finally
        {
            _currentGeneration = null;
            _state = ServerRuntimeState.Disposed;
        }
    }

    private ServerGeneration BuildPreparedCandidate(ulong generationId)
    {
        var builder = new ServerGenerationBuilder(
            generationId,
            new EcsWorld(_schema, _options.WorldOptions),
            new WorldClock(_options.ClockOptions));

        if (_currentGeneration is not null)
        {
            builder.Clock.SetPlaybackRate(_currentGeneration.Clock.PlaybackRate);
            builder.Clock.SetSimulationFrequency(_currentGeneration.Clock.SimulationFrequencyHz);
        }

        ServerGeneration? candidate = null;
        try
        {
            _composition.Configure(builder);
            if (!_options.AllowVariablePlayback &&
                builder.Clock.PlaybackRate != WorldClockOptions.DefaultPlaybackRate)
            {
                throw new InvalidOperationException(
                    "The composition changed playback on a fixed-playback runtime.");
            }
            candidate = builder.Build();
            candidate.Prepare();
            return candidate;
        }
        catch
        {
            if (candidate is null)
                builder.DisposeUnbuilt();
            else
                candidate.Dispose();
            throw;
        }
    }

    private void PublishCandidate(ServerGeneration candidate)
    {
        ServerGeneration? previous = _currentGeneration;
        _currentGeneration = candidate;
        candidate.MarkPublished();

        try
        {
            _options.PublicationBoundaryHook?.Invoke(candidate, previous);
        }
        catch
        {
            _state = ServerRuntimeState.Faulted;
            try { previous?.Dispose(); }
            catch { }
            throw;
        }

        try
        {
            previous?.Dispose();
        }
        catch
        {
            _state = ServerRuntimeState.Faulted;
            throw;
        }
    }

    private void ExecuteSimulationStep()
    {
        _operation = RuntimeOperation.SimulationStep;
        try
        {
            _currentGeneration!.TickSimulationStep();
        }
        catch
        {
            _state = ServerRuntimeState.Faulted;
            throw;
        }
        finally
        {
            _operation = RuntimeOperation.None;
        }
    }

    private static void DisposePreparedGeneration(EcsWorld world, object? gameState)
    {
        Exception? failure = null;
        if (gameState is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }

        try
        {
            world.Dispose();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is not null)
            throw new InvalidOperationException("A rejected prepared generation could not release all owned state.", failure);
    }

    private void EnsureRunning(string operation)
    {
        if (_state != ServerRuntimeState.Running)
            throw new InvalidOperationException($"{operation} is invalid while the runtime is {_state}.");
    }

    private void EnsureIdleOperation(string operation)
    {
        if (_operation != RuntimeOperation.None)
            throw new InvalidOperationException(
                $"{operation} cannot run reentrantly during {_operation}.");
    }

    private RuntimeOperation EnterGameCallback(string operation)
    {
        if (_operation is not (RuntimeOperation.None or RuntimeOperation.PublicationNotification))
            throw new InvalidOperationException(
                $"{operation} cannot run reentrantly during {_operation}.");
        return _operation;
    }

    private void EnsureBorrowedGenerationUnchanged(ServerGeneration borrowed)
    {
        if (!ReferenceEquals(_currentGeneration, borrowed) || borrowed.IsDisposed)
            throw new InvalidOperationException(
                "The active generation changed while callback-scoped authority was borrowed.");
    }

    private ServerGeneration GetActiveGeneration()
    {
        if (_state == ServerRuntimeState.Disposed)
            throw new ObjectDisposedException(nameof(ServerRuntime));
        return _currentGeneration ??
            throw new InvalidOperationException("The server runtime has no active generation.");
    }

    private void EnsureState(ServerRuntimeState expected, string operation)
    {
        if (_state == ServerRuntimeState.Disposed)
            throw new ObjectDisposedException(nameof(ServerRuntime));
        if (_state != expected)
            throw new InvalidOperationException($"{operation} is invalid while the runtime is {_state}.");
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
        {
            throw new InvalidOperationException(
                "ServerRuntime is single-owner; host control must remain on its construction thread.");
        }
    }

    private enum RuntimeOperation : byte
    {
        None = 0,
        GameCallback = 1,
        SimulationStep = 2,
        Publication = 3,
        PublicationNotification = 4,
    }
}
