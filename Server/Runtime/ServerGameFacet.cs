using UltraSim.ECS;

namespace UltraSim.Server.Runtime;

/// <summary>One synchronous trusted-game operation over the active generation.</summary>
/// <typeparam name="TResult">The logical result returned by the operation.</typeparam>
/// <param name="world">The active generation's ECS world.</param>
/// <param name="generationId">The active generation identity paired with <paramref name="world"/>.</param>
public delegate TResult ServerGenerationOperation<TResult>(EcsWorld world, ulong generationId);

/// <summary>One synchronous trusted-game operation over typed generation-owned state.</summary>
public delegate TResult ServerGenerationStateOperation<TState, TResult>(
    EcsWorld world,
    ulong generationId,
    TState gameState)
    where TState : class;

/// <summary>
/// Callback-scoped game authority over the active server generation.
/// </summary>
/// <remarks>
/// This facet exists for trusted game policy such as Admission. It does not
/// expose the generation root as a property, and hosts do not expose this
/// facet. The supplied world is valid only for the synchronous callback; code
/// must not retain it as a second active-world authority.
/// </remarks>
public sealed class ServerGameFacet
{
    private readonly ServerRuntime _runtime;

    internal ServerGameFacet(ServerRuntime runtime) => _runtime = runtime;

    /// <summary>Raised after a replacement root is fully published and the previous root is retired.</summary>
    public event Action<ulong>? GenerationPublished;

    /// <summary>Reports isolated failures from non-authoritative publication observers.</summary>
    public event Action<Exception>? ObserverFaulted;

    internal void NotifyGenerationPublished(ulong generationId)
    {
        Action<ulong>? observers = GenerationPublished;
        if (observers is null)
            return;
        foreach (Action<ulong> observer in observers.GetInvocationList().Cast<Action<ulong>>())
        {
            try { observer(generationId); }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                NotifyObserverFault(exception);
            }
        }
    }

    private void NotifyObserverFault(Exception exception)
    {
        Action<Exception>? observers = ObserverFaulted;
        if (observers is null)
            return;
        foreach (Action<Exception> observer in observers.GetInvocationList().Cast<Action<Exception>>())
        {
            try { observer(exception); }
            catch (Exception nested) when (nested is not OutOfMemoryException) { }
        }
    }

    /// <summary>
    /// Runs one trusted game operation when Runtime is running.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when the callback ran; otherwise
    /// <see langword="false"/> and <paramref name="result"/> is default.
    /// </returns>
    /// <remarks>
    /// Runtime owner-thread rules apply. The callback must finish
    /// synchronously and must not retain <see cref="EcsWorld"/>. An exception
    /// terminally faults Runtime because the callback may have partially
    /// mutated active authority.
    /// </remarks>
    public bool TryUseActiveGeneration<TResult>(
        ServerGenerationOperation<TResult> operation,
        out TResult result)
    {
        ArgumentNullException.ThrowIfNull(operation);
        return _runtime.TryUseActiveGeneration(operation, out result);
    }

    /// <summary>
    /// Runs one trusted game operation when Runtime is running and the active
    /// generation owns the requested game-state type.
    /// </summary>
    /// <remarks>
    /// The supplied world and game state are one callback-scoped authority.
    /// Neither may be retained after the callback returns.
    /// </remarks>
    public bool TryUseActiveGeneration<TState, TResult>(
        ServerGenerationStateOperation<TState, TResult> operation,
        out TResult result)
        where TState : class
    {
        ArgumentNullException.ThrowIfNull(operation);
        return _runtime.TryUseActiveGeneration(operation, out result);
    }

    /// <summary>Captures the host-independent authoritative world-clock state.</summary>
    public bool TryCapturePersistentClockState(out PersistentWorldClockState state) =>
        _runtime.TryCapturePersistentClockState(out state);

    /// <summary>
    /// Publishes a fully prepared ECS world and opaque game-owned state through
    /// the same active generation root used by normal Runtime replacements.
    /// </summary>
    /// <remarks>
    /// <paramref name="preparedGameState"/> becomes candidate-owned before
    /// publication. Runtime never interprets its domain type or contents. Once
    /// the inputs pass active-root alias rejection, this call takes ownership
    /// and reclaims both inputs if candidate validation fails.
    /// </remarks>
    public bool TryPublishPreparedGeneration(
        EcsWorld preparedWorld,
        object? preparedGameState,
        out ulong generationId)
    {
        ArgumentNullException.ThrowIfNull(preparedWorld);
        return _runtime.TryPublishPreparedGeneration(
            preparedWorld, preparedGameState, persistentClockState: null, out generationId);
    }

    /// <summary>Publishes a prepared generation with explicitly restored authoritative clock state.</summary>
    /// <remarks>
    /// Playback rate and simulation frequency remain host policy and are
    /// retained from the current host. This operation must run outside every
    /// active-generation callback.
    /// </remarks>
    public bool TryPublishPreparedGeneration(
        EcsWorld preparedWorld,
        object? preparedGameState,
        PersistentWorldClockState persistentClockState,
        out ulong generationId)
    {
        ArgumentNullException.ThrowIfNull(preparedWorld);
        return _runtime.TryPublishPreparedGeneration(
            preparedWorld, preparedGameState, persistentClockState, out generationId);
    }
}
