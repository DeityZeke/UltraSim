using UltraSim.ECS;

namespace UltraSim.Server.Runtime;

/// <summary>
/// One coherent, generation-owned authoritative root.
/// </summary>
/// <remarks>
/// Instances are constructed only by <see cref="ServerRuntime"/>. The runtime
/// publishes the entire instance as one root and disposes old generations only
/// after they are no longer active.
/// </remarks>
internal sealed class ServerGeneration
{
    private bool _disposed;

    internal ServerGeneration(
        ulong generationId,
        EcsWorld ecsWorld,
        WorldClock clock,
        object? gameState)
    {
        GenerationId = generationId;
        EcsWorld = ecsWorld;
        Clock = clock;
        GameState = gameState;
    }

    public ulong GenerationId { get; }

    public EcsWorld EcsWorld { get; }

    public WorldClock Clock { get; }

    /// <summary>
    /// Opaque game-owned state that must change coherently with this ECS world.
    /// Runtime owns its lifetime but never interprets its domain meaning.
    /// </summary>
    public object? GameState { get; }

    public bool IsPublished { get; private set; }

    public bool IsDisposed => _disposed;

    internal void Prepare()
    {
        EcsWorld.Seal();
        EcsWorld.Initialize();
        if (!EcsWorld.HasScheduler)
        {
            throw new InvalidOperationException(
                "A server generation requires an enabled ECS scheduler.");
        }
        EcsWorld.BeginRun();
    }

    internal void MarkPublished() => IsPublished = true;

    internal void TickSimulationStep()
    {
        EcsWorld.Tick(Clock.FixedStepSeconds);
        Clock.AdvanceSimulationTick();
    }

    internal void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Exception? failure = null;
        if (GameState is IDisposable disposable)
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
            EcsWorld.Dispose();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is not null)
            throw new InvalidOperationException("A server generation could not release all owned state.", failure);
    }
}

/// <summary>
/// Cold configuration surface for one private <see cref="ServerGeneration"/>.
/// </summary>
/// <remarks>
/// Valid only during <see cref="IServerComposition.Configure"/>. Retaining this
/// builder or its <see cref="World"/> would create an unauthorized second route
/// to candidate/active state and is outside the composition contract.
/// </remarks>
public sealed class ServerGenerationBuilder
{
    private readonly ulong _generationId;
    private object? _gameState;
    private bool _gameStateAssigned;
    private bool _built;

    internal ServerGenerationBuilder(
        ulong generationId,
        EcsWorld world,
        WorldClock clock)
    {
        _generationId = generationId;
        World = world;
        Clock = clock;
    }

    public ulong GenerationId => _generationId;

    public EcsWorld World { get; }

    /// <summary>
    /// Installs the single opaque game-owned state root for this private
    /// generation candidate.
    /// </summary>
    /// <remarks>
    /// Use one composition-owned aggregate when several game-domain objects
    /// must publish with the ECS world. Runtime owns and disposes the supplied
    /// object when it implements <see cref="IDisposable"/> but never interprets
    /// its type or contents.
    /// </remarks>
    public void SetGameState<TState>(TState state) where TState : class
    {
        ArgumentNullException.ThrowIfNull(state);
        if (_built)
            throw new InvalidOperationException("A built generation candidate is no longer cold-configurable.");
        if (_gameStateAssigned)
            throw new InvalidOperationException("A generation can own only one game-state root.");

        _gameState = state;
        _gameStateAssigned = true;
    }

    internal WorldClock Clock { get; }

    /// <summary>Restores clock state while the candidate is still private.</summary>
    internal void RestoreClockSnapshot(WorldClockSnapshot snapshot)
    {
        if (_built)
            throw new InvalidOperationException("A built generation candidate is no longer cold-configurable.");
        Clock.RestoreSnapshot(snapshot);
    }

    internal ServerGeneration Build()
    {
        if (_built)
            throw new InvalidOperationException("A server generation builder can only build once.");

        _built = true;
        return new ServerGeneration(_generationId, World, Clock, _gameState);
    }

    internal void DisposeUnbuilt()
    {
        if (_built)
            return;

        _built = true;
        Exception? failure = null;
        if (_gameState is IDisposable disposable)
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
            World.Dispose();
        }
        catch (Exception exception)
        {
            failure ??= exception;
        }

        if (failure is not null)
            throw new InvalidOperationException("An unbuilt server generation could not release all owned state.", failure);
    }
}
