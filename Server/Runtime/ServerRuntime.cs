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

        try
        {
            result = operation(_currentGeneration.EcsWorld, _currentGeneration.GenerationId);
            return true;
        }
        catch
        {
            // A trusted game callback may already have mutated the active ECS.
            // Its failure therefore has the same terminal policy as a failed
            // simulation step: there is no provable rollback boundary.
            _state = ServerRuntimeState.Faulted;
            throw;
        }
    }

    internal bool TryPublishPreparedGeneration(
        EcsWorld preparedWorld,
        Action? publicationAction,
        out ulong generationId)
    {
        EnsureOwnerThread();
        EnsureRunning(nameof(TryPublishPreparedGeneration));
        ArgumentNullException.ThrowIfNull(preparedWorld);
        ServerGeneration? candidate = null;
        try
        {
            if (preparedWorld.OwnerThreadId != _ownerThreadId)
                throw new InvalidOperationException("A prepared ECS world belongs to a different owner thread.");
            if (!preparedWorld.IsRunning || !preparedWorld.HasScheduler)
                throw new InvalidOperationException("A prepared generation requires a running scheduled ECS world.");
            if (preparedWorld.Schema.Id != _schema.Id)
                throw new InvalidOperationException("A prepared generation uses a different ECS schema.");
            if (ReferenceEquals(preparedWorld, _currentGeneration!.EcsWorld))
                throw new InvalidOperationException("The active ECS world cannot be published as its own replacement.");
            if (_nextGenerationId == ulong.MaxValue)
                throw new InvalidOperationException("The server generation identity space is exhausted.");

            WorldClockSnapshot clockSnapshot = _currentGeneration.Clock.CaptureSnapshot();
            candidate = new ServerGeneration(
                _nextGenerationId,
                preparedWorld,
                new WorldClock(_options.ClockOptions));
            candidate.Clock.RestoreSnapshot(clockSnapshot);
            PublishCandidate(candidate, publicationAction);
            generationId = candidate.GenerationId;
            _nextGenerationId++;
            return true;
        }
        catch
        {
            // Once PublishCandidate marks the candidate, it is the active
            // terminal root even if a publication callback fails. Before that
            // boundary ownership remains with the caller of this method.
            if (candidate is null)
                preparedWorld.Dispose();
            else if (!candidate.IsPublished)
                candidate.Dispose();
            throw;
        }
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
        EnsureState(ServerRuntimeState.Initialized, nameof(Start));
        _state = ServerRuntimeState.Running;
    }

    /// <summary>Executes exactly one fixed simulation step.</summary>
    public void Tick()
    {
        EnsureOwnerThread();
        EnsureRunning(nameof(Tick));
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
        if (_state is not (ServerRuntimeState.Initialized or ServerRuntimeState.Running))
            throw new InvalidOperationException(
                $"SetPlaybackRate is invalid while the runtime is {_state}.");
        if (!_options.AllowVariablePlayback)
            throw new InvalidOperationException("This runtime uses fixed authoritative playback at 1x.");

        _currentGeneration!.Clock.SetPlaybackRate(playbackRate);
    }

    /// <summary>
    /// Builds and validates a private replacement, then publishes it with one
    /// active-root assignment.
    /// </summary>
    internal void ReplaceGeneration()
    {
        EnsureOwnerThread();
        EnsureRunning(nameof(ReplaceGeneration));
        if (_nextGenerationId == ulong.MaxValue)
            throw new InvalidOperationException("The server generation identity space is exhausted.");

        ServerGeneration candidate = BuildPreparedCandidate(_nextGenerationId);
        try
        {
            PublishCandidate(candidate);
            _nextGenerationId++;
        }
        catch
        {
            // A candidate that crossed the publication boundary is now the
            // active terminal root and must remain owned by this runtime. A
            // pre-publication failure is still private and must be reclaimed.
            if (!candidate.IsPublished)
                candidate.Dispose();
            throw;
        }
    }

    public void Stop()
    {
        EnsureOwnerThread();
        if (_state == ServerRuntimeState.Stopped)
            return;
        EnsureState(ServerRuntimeState.Running, nameof(Stop));
        _state = ServerRuntimeState.Stopped;
    }

    public void Dispose()
    {
        EnsureOwnerThread();
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
            builder.Clock.SetPlaybackRate(_currentGeneration.Clock.PlaybackRate);

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

    private void PublishCandidate(ServerGeneration candidate, Action? publicationAction = null)
    {
        ServerGeneration? previous = _currentGeneration;
        _currentGeneration = candidate;
        candidate.MarkPublished();

        try
        {
            // Game-owned participant state transfers before the optional
            // observation hook so the first observable post-swap state is the
            // complete composition. The callback is still after the active
            // ECS root swap and remains terminal if it fails.
            publicationAction?.Invoke();
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
        try
        {
            _currentGeneration!.TickSimulationStep();
        }
        catch
        {
            _state = ServerRuntimeState.Faulted;
            throw;
        }
    }

    private void EnsureRunning(string operation)
    {
        if (_state != ServerRuntimeState.Running)
            throw new InvalidOperationException($"{operation} is invalid while the runtime is {_state}.");
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
}
