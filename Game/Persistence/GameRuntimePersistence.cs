using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Persistence;

/// <summary>Expected whole-game save outcome; failures leave active authority unchanged.</summary>
public readonly record struct GameSaveResult(
    bool Succeeded,
    SaveSetManifest? Manifest,
    Exception? Failure);

/// <summary>Expected staged-load/publication outcome and new generation identity when successful.</summary>
public readonly record struct GameLoadResult(
    bool Succeeded,
    ulong GenerationId,
    Exception? Failure);

/// <summary>
/// Whole-game persistence composition. Feature-owned derived state contributes
/// staged reconstruction, but does not own sibling participants or publication.
/// </summary>
public sealed class GameRuntimePersistence
{
    private readonly ServerGameFacet _runtime;
    private readonly UltraSimGameComposition _composition;
    private readonly PersistenceRestoreOptions _restoreOptions;
    private readonly SaveSetCoordinator _coordinator;
    private readonly AccountingPersistenceOptions _accountingOptions;
    private readonly WorldPersistenceOptions _worldOptions;
    private readonly Func<PersistenceRestoreRequirements, GameStaging>? _stagingOverride;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    public GameRuntimePersistence(
        ServerGameFacet runtime,
        UltraSimGameComposition composition,
        PersistenceRestoreOptions restoreOptions,
        SaveSetCoordinator coordinator,
        AccountingPersistenceOptions? accountingOptions = null,
        Func<PersistenceRestoreRequirements, GameStaging>? stagingOverride = null,
        WorldPersistenceOptions? worldOptions = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _restoreOptions = restoreOptions;
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _accountingOptions = accountingOptions ?? new AccountingPersistenceOptions();
        _worldOptions = worldOptions ?? new WorldPersistenceOptions();
        _stagingOverride = stagingOverride;
    }

    public GameSaveResult TrySave(in SaveSetContext context, Action<Action> captureBoundary)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(captureBoundary);
        if (!TryCapture(context, captureBoundary, out CapturedSaveSet? captured, out Exception? failure))
            return new GameSaveResult(false, null, failure);
        CapturedSaveSet ownedCapture = captured!;
        using (ownedCapture)
        {
            try
            {
                return new GameSaveResult(true, _coordinator.Publish(ownedCapture), null);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                return new GameSaveResult(false, null, exception);
            }
        }
    }

    /// <summary>
    /// Captures under brief owner-thread authority, then performs encoding and
    /// crash-durable publication without holding the active generation lease.
    /// </summary>
    /// <remarks>
    /// Cancellation is a pre-capture admission signal. It is observed before
    /// authoritative snapshot ownership transfers to persistence. Once capture
    /// begins, the owned save set is published to completion (or fails normally)
    /// so cancellation cannot deliberately abandon a half-written transaction.
    /// </remarks>
    /// <exception cref="OperationCanceledException">
    /// The token was canceled before authoritative capture began.
    /// </exception>
    public Task<GameSaveResult> TrySaveAsync(
        SaveSetContext context,
        Action<Action> captureBoundary,
        CancellationToken cancellationToken = default)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(captureBoundary);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryCapture(context, captureBoundary, out CapturedSaveSet? captured, out Exception? failure))
            return Task.FromResult(new GameSaveResult(false, null, failure));
        CapturedSaveSet ownedCapture = captured!;

        return Task.Run(() =>
        {
            using (ownedCapture)
            {
                try
                {
                    return new GameSaveResult(true, _coordinator.Publish(ownedCapture), null);
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    return new GameSaveResult(false, null, exception);
                }
            }
        });
    }

    public GameLoadResult TryLoad(SaveSetRecoveryMode recoveryMode = SaveSetRecoveryMode.StrictCurrent)
    {
        EnsureOwnerThread();
        GameStaging? staging = null;
        EcsWorld? stagedWorld = null;
        AccountingStore? stagedAccounting = null;
        GameGenerationState? candidate = null;
        bool publicationAttempted = false;
        try
        {
            var ecsParticipant = new EcsWorldSaveSetParticipant(
                _composition.PersistenceCatalog,
                _restoreOptions,
                requirements =>
                {
                    GameStaging value = CreateStaging(requirements);
                    staging = value;
                    return value.World;
                });
            var accountingParticipant = new AccountingSaveSetParticipant(_accountingOptions);
            var worldParticipant = new WorldSaveSetParticipant(
                fingerprint =>
                {
                    GameStaging built = staging ??
                        throw new PersistenceValidationException(
                            "The ECS participant did not prepare game content before current-world resolution.");
                    return _composition.TryResolveWorldPack(fingerprint, built.Content.World, out BoundWorldPack? bound)
                        ? bound
                        : null;
                },
                _worldOptions);
            var clockParticipant = new WorldClockSaveSetParticipant();
            using PreparedLoadGeneration prepared = _coordinator.PrepareLoad(
                [ecsParticipant, worldParticipant, accountingParticipant, clockParticipant],
                EmptyResolver.Instance,
                recoveryMode);
            PreparedEcsWorldParticipant ecs = prepared.Participants.OfType<PreparedEcsWorldParticipant>().Single();
            PreparedWorldParticipant preparedWorld = prepared.Participants.OfType<PreparedWorldParticipant>().Single();
            PreparedAccountingParticipant accounting = prepared.Participants.OfType<PreparedAccountingParticipant>().Single();
            PreparedWorldClockParticipant clock = prepared.Participants.OfType<PreparedWorldClockParticipant>().Single();
            stagedWorld = ecs.TakeWorld();
            if (stagedWorld.PersistentWorldId != preparedWorld.DurableWorldId)
                throw new PersistenceValidationException(
                    "The restored ECS and current-world durable identities disagree.");
            if (stagedWorld.TickCount != clock.State.SimulationTick)
                throw new PersistenceValidationException(
                    "The restored ECS scheduler tick disagrees with authoritative world-clock state.");
            GameStaging built = staging ??
                throw new InvalidOperationException("The game staging factory did not produce state.");
            if (!built.WorldState.TryInstallPreparedWorld(
                    preparedWorld.BaseWorld,
                    preparedWorld.Overlay))
                throw new PersistenceValidationException(
                    "The resolved current-world view could not be installed into the private staging generation.");
            built.Inventory.Rebuild(stagedWorld);
            stagedAccounting = accounting.TakeStore();
            candidate = new GameGenerationState(
                stagedAccounting,
                built.Inventory,
                built.Content,
                built.WorldState,
                built.SimulationLookup,
                built.Replication,
                built.WorldActions);

            ulong publishedGeneration = 0;
            prepared.Commit(_ =>
            {
                publicationAttempted = true;
                if (!_runtime.TryPublishPreparedGeneration(
                    stagedWorld, candidate, clock.State, out publishedGeneration))
                {
                    throw new InvalidOperationException("The prepared game generation was not published.");
                }
            });
            return new GameLoadResult(true, publishedGeneration, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!publicationAttempted)
            {
                if (candidate is not null)
                {
                    candidate.Dispose();
                    stagedAccounting = null;
                    staging = null;
                }
                else
                {
                    stagedAccounting?.Dispose();
                    staging?.WorldState.Dispose();
                    staging?.Inventory.Dispose();
                }
                if (stagedWorld is not null && !stagedWorld.IsDisposed)
                    stagedWorld.Dispose();
            }
            return new GameLoadResult(false, 0, exception);
        }
    }

    private bool TryCapture(
        in SaveSetContext context,
        Action<Action> captureBoundary,
        out CapturedSaveSet? captured,
        out Exception? failure)
    {
        CapturedSaveSet? localCaptured = null;
        Exception? localFailure = null;
        captured = null;
        failure = null;
        if (!_runtime.TryCapturePersistentClockState(out PersistentWorldClockState clockState))
        {
            failure = new InvalidOperationException("Game persistence requires a running game generation.");
            return false;
        }

        SaveSetContext saveContext = context;
        bool ran = _runtime.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                try
                {
                    var ecs = new EcsWorldSaveSetParticipant(
                        world, _composition.PersistenceCatalog, _restoreOptions,
                        requirements => CreateStaging(requirements).World);
                    var accounting = new AccountingSaveSetParticipant(game.Accounting, _accountingOptions);
                    var currentWorld = new WorldSaveSetParticipant(
                        game.WorldState, world.PersistentWorldId, _worldOptions);
                    var clock = new WorldClockSaveSetParticipant(clockState);
                    localCaptured = _coordinator.Capture(
                        [ecs, currentWorld, accounting, clock], saveContext, captureBoundary);
                    return true;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    localFailure = exception;
                    return false;
                }
            },
            out bool succeeded);
        if (!ran)
        {
            failure = new InvalidOperationException("Game persistence requires a running game generation.");
            return false;
        }
        captured = localCaptured;
        failure = localFailure;
        return succeeded;
    }

    private GameStaging CreateStaging(PersistenceRestoreRequirements requirements) =>
        _stagingOverride?.Invoke(requirements) ?? _composition.CreateStaging(requirements);

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Game persistence is bound to its creating owner thread.");
    }

    private sealed class EmptyResolver : IExternalPersistentEntityResolver
    {
        internal static readonly EmptyResolver Instance = new();

        public bool TryResolve(PersistentEntityKey key, out object? value)
        {
            value = null;
            return false;
        }
    }
}
