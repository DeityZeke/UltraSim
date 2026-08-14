using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Composition;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Persistence;

public readonly record struct GameSaveResult(
    bool Succeeded,
    SaveSetManifest? Manifest,
    Exception? Failure);

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
    private readonly Func<PersistenceRestoreRequirements, GameStaging>? _stagingOverride;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    public GameRuntimePersistence(
        ServerGameFacet runtime,
        UltraSimGameComposition composition,
        PersistenceRestoreOptions restoreOptions,
        SaveSetCoordinator coordinator,
        AccountingPersistenceOptions? accountingOptions = null,
        Func<PersistenceRestoreRequirements, GameStaging>? stagingOverride = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _composition = composition ?? throw new ArgumentNullException(nameof(composition));
        _restoreOptions = restoreOptions;
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _accountingOptions = accountingOptions ?? new AccountingPersistenceOptions();
        _stagingOverride = stagingOverride;
    }

    public GameSaveResult TrySave(in SaveSetContext context, Action<Action> captureBoundary)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(captureBoundary);
        SaveSetManifest? manifest = null;
        Exception? failure = null;
        SaveSetContext saveContext = context;
        bool ran = _runtime.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                try
                {
                    var ecs = new EcsWorldSaveSetParticipant(
                        world,
                        _composition.PersistenceCatalog,
                        _restoreOptions,
                        requirements => CreateStaging(requirements).World);
                    var accounting = new AccountingSaveSetParticipant(game.Accounting, _accountingOptions);
                    manifest = _coordinator.Save([ecs, accounting], saveContext, captureBoundary);
                    return true;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    failure = exception;
                    return false;
                }
            },
            out bool succeeded);
        if (!ran)
        {
            failure = new InvalidOperationException("Game persistence requires a running game generation.");
            return new GameSaveResult(false, null, failure);
        }
        return new GameSaveResult(succeeded, manifest, failure);
    }

    public GameLoadResult TryLoad(SaveSetRecoveryMode recoveryMode = SaveSetRecoveryMode.StrictCurrent)
    {
        EnsureOwnerThread();
        Exception? failure = null;
        ulong publishedGeneration = 0;
        bool ran = _runtime.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                GameStaging? staging = null;
                EcsWorld? stagedWorld = null;
                AccountingStore? stagedAccounting = null;
                GameGenerationState? candidate = null;
                bool transferred = false;
                try
                {
                    var ecsParticipant = new EcsWorldSaveSetParticipant(
                        world,
                        _composition.PersistenceCatalog,
                        _restoreOptions,
                        requirements =>
                        {
                            GameStaging value = CreateStaging(requirements);
                            staging = value;
                            return value.World;
                        });
                    var accountingParticipant = new AccountingSaveSetParticipant(
                        game.Accounting,
                        _accountingOptions);
                    using PreparedLoadGeneration prepared = _coordinator.PrepareLoad(
                        [ecsParticipant, accountingParticipant],
                        EmptyResolver.Instance,
                        recoveryMode);
                    PreparedEcsWorldParticipant ecs = prepared.Participants
                        .OfType<PreparedEcsWorldParticipant>()
                        .Single();
                    PreparedAccountingParticipant accounting = prepared.Participants
                        .OfType<PreparedAccountingParticipant>()
                        .Single();
                    stagedWorld = ecs.TakeWorld();
                    GameStaging built = staging ??
                        throw new InvalidOperationException("The game staging factory did not produce state.");
                    built.Inventory.Rebuild(stagedWorld);
                    stagedAccounting = accounting.TakeStore();
                    candidate = new GameGenerationState(stagedAccounting, built.Inventory, built.Content);

                    prepared.Commit(_ =>
                    {
                        transferred = true;
                        if (!_runtime.TryPublishPreparedGeneration(stagedWorld, candidate, out publishedGeneration))
                            throw new InvalidOperationException("The prepared game generation was not published.");
                    });
                    return true;
                }
                catch (Exception exception) when (exception is not OutOfMemoryException)
                {
                    if (!transferred)
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
                            staging?.Inventory.Dispose();
                        }
                        if (stagedWorld is not null && !stagedWorld.IsDisposed)
                            stagedWorld.Dispose();
                    }
                    failure = exception;
                    return false;
                }
            },
            out bool succeeded);
        if (!ran)
        {
            failure = new InvalidOperationException("Game persistence requires a running game generation.");
            return new GameLoadResult(false, 0, failure);
        }
        return new GameLoadResult(succeeded, succeeded ? publishedGeneration : 0, failure);
    }

    private GameStaging CreateStaging(PersistenceRestoreRequirements requirements) =>
        _stagingOverride?.Invoke(requirements) ?? _composition.CreateStaging(requirements);

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Game persistence is bound to its creating owner thread.");
    }

    private sealed class EmptyResolver : IPersistentEntityResolver
    {
        internal static readonly EmptyResolver Instance = new();

        public bool TryResolve(PersistentEntityKey key, out object? value)
        {
            value = null;
            return false;
        }
    }
}
