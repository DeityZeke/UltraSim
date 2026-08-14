using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Persistence;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Inventory;

/// <summary>Private staged ECS world plus its rebuilt derived inventory state.</summary>
public readonly record struct InventoryStaging(
    EcsWorld World,
    InventoryGenerationState State);

/// <summary>Result of an inventory save attempt.</summary>
public readonly record struct InventorySaveResult(
    bool Succeeded,
    SaveSetManifest? Manifest,
    Exception? Failure);

/// <summary>Result of an inventory restore/publication attempt.</summary>
public readonly record struct InventoryLoadResult(
    bool Succeeded,
    ulong GenerationId,
    Exception? Failure);

/// <summary>
/// Game-owned persistence bridge for the S0 aggregate. InventoryIndex is
/// rebuilt from restored ECS truth and deliberately has no participant artifact;
/// Accounting remains an independent durable participant.
/// </summary>
public sealed class InventoryRuntimePersistence
{
    private readonly ServerGameFacet _runtime;
    private readonly PersistenceCatalog _catalog;
    private readonly PersistenceRestoreOptions _restoreOptions;
    private readonly Func<PersistenceRestoreRequirements, InventoryStaging> _stagingFactory;
    private readonly SaveSetCoordinator _coordinator;
    private readonly AccountingPersistenceOptions _accountingOptions;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    public InventoryRuntimePersistence(
        ServerGameFacet runtime,
        PersistenceCatalog catalog,
        PersistenceRestoreOptions restoreOptions,
        Func<PersistenceRestoreRequirements, InventoryStaging> stagingFactory,
        SaveSetCoordinator coordinator,
        AccountingPersistenceOptions? accountingOptions = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _restoreOptions = restoreOptions;
        _stagingFactory = stagingFactory ?? throw new ArgumentNullException(nameof(stagingFactory));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _accountingOptions = accountingOptions ?? new AccountingPersistenceOptions();
    }

    public InventorySaveResult TrySave(in SaveSetContext context, Action<Action> captureBoundary)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(captureBoundary);
        SaveSetManifest? manifest = null;
        Exception? failure = null;
        SaveSetContext saveContext = context;
        bool ran = _runtime.TryUseActiveGeneration<IInventoryGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                try
                {
                    var participant = new EcsWorldSaveSetParticipant(
                        world,
                        _catalog,
                        _restoreOptions,
                        requirements => _stagingFactory(requirements).World);
                    var accounting = new AccountingSaveSetParticipant(
                        game.Accounting,
                        _accountingOptions);
                    manifest = _coordinator.Save([participant, accounting], saveContext, captureBoundary);
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
            failure = new InvalidOperationException(
                "Inventory persistence requires a running aggregate game generation.");
            return new InventorySaveResult(false, null, failure);
        }
        return new InventorySaveResult(succeeded, manifest, failure);
    }

    public InventoryLoadResult TryLoad(
        SaveSetRecoveryMode recoveryMode = SaveSetRecoveryMode.StrictCurrent)
    {
        EnsureOwnerThread();
        Exception? failure = null;
        ulong publishedGeneration = 0;
        bool ran = _runtime.TryUseActiveGeneration<IInventoryGameGenerationStateProvider, bool>(
            (world, _, game) =>
            {
                InventoryStaging? staging = null;
                EcsWorld? stagedWorld = null;
                AccountingStore? stagedAccounting = null;
                InventoryGameGenerationState? candidate = null;
                bool transferred = false;
                try
                {
                    var participant = new EcsWorldSaveSetParticipant(
                        world,
                        _catalog,
                        _restoreOptions,
                        requirements =>
                        {
                            InventoryStaging value = _stagingFactory(requirements);
                            staging = value;
                            return value.World;
                        });
                    var accountingParticipant = new AccountingSaveSetParticipant(
                        game.Accounting,
                        _accountingOptions);
                    using PreparedLoadGeneration prepared = _coordinator.PrepareLoad(
                        [participant, accountingParticipant],
                        EmptyResolver.Instance,
                        recoveryMode);
                    PreparedEcsWorldParticipant ecs = prepared.Participants
                        .OfType<PreparedEcsWorldParticipant>()
                        .Single();
                    PreparedAccountingParticipant accounting = prepared.Participants
                        .OfType<PreparedAccountingParticipant>()
                        .Single();
                    stagedWorld = ecs.TakeWorld();
                    InventoryGenerationState stagedState = staging?.State ??
                        throw new InvalidOperationException("The inventory staging factory did not produce state.");
                    stagedState.Rebuild(stagedWorld);
                    stagedAccounting = accounting.TakeStore();
                    candidate = new InventoryGameGenerationState(stagedAccounting, stagedState);

                    prepared.Commit(_ =>
                    {
                        // Runtime owns/reclaims both values from this point,
                        // including terminal failures after publication starts.
                        transferred = true;
                        if (!_runtime.TryPublishPreparedGeneration(
                                stagedWorld,
                                candidate,
                                out publishedGeneration))
                        {
                            throw new InvalidOperationException("The prepared inventory generation was not published.");
                        }
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
                            staging?.State.Dispose();
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
            failure = new InvalidOperationException(
                "Inventory persistence requires a running aggregate game generation.");
            return new InventoryLoadResult(false, 0, failure);
        }
        return new InventoryLoadResult(succeeded, succeeded ? publishedGeneration : 0, failure);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Inventory persistence is bound to its creating owner thread.");
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
