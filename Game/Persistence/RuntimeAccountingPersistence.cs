using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Persistence;

/// <summary>Result of a save attempt that did not mutate runtime authority.</summary>
internal readonly record struct SaveAttemptResult(
    bool Succeeded,
    SaveSetManifest? Manifest,
    Exception? Failure);

/// <summary>Result of a staged load attempt.</summary>
internal readonly record struct LoadAttemptResult(
    bool Succeeded,
    ulong GenerationId,
    Exception? Failure);

/// <summary>
/// S0.6 compatibility bridge joining only ECS and Accounting.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
internal sealed class RuntimeAccountingPersistence
{
    private readonly ServerGameFacet _runtime;
    private readonly PersistenceCatalog _catalog;
    private readonly PersistenceRestoreOptions _restoreOptions;
    private readonly Func<PersistenceRestoreRequirements, EcsWorld> _stagingWorldFactory;
    private readonly SaveSetCoordinator _coordinator;
    private readonly AccountingPersistenceOptions _accountingOptions;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    public RuntimeAccountingPersistence(
        ServerGameFacet runtime,
        PersistenceCatalog catalog,
        PersistenceRestoreOptions restoreOptions,
        Func<PersistenceRestoreRequirements, EcsWorld> stagingWorldFactory,
        SaveSetCoordinator coordinator,
        AccountingPersistenceOptions? accountingOptions = null)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _restoreOptions = restoreOptions;
        _stagingWorldFactory = stagingWorldFactory ?? throw new ArgumentNullException(nameof(stagingWorldFactory));
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _accountingOptions = accountingOptions ?? new AccountingPersistenceOptions();
    }

    /// <summary>Uses the active generation's Accounting state synchronously.</summary>
    public bool TryUseAccounting<TResult>(Func<AccountingStore, TResult> operation, out TResult result)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(operation);
        return _runtime.TryUseActiveGeneration<IAccountingGenerationStateProvider, TResult>(
            (_, _, provider) => operation(provider.Accounting),
            out result);
    }

    /// <summary>
    /// Captures ECS and Accounting through one SaveSetCoordinator epoch. Save
    /// failures are returned and do not terminally fault Runtime because the
    /// active generation is only read during capture.
    /// </summary>
    public SaveAttemptResult TrySave(
        in SaveSetContext context,
        Action<Action> captureBoundary)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(captureBoundary);
        SaveSetContext saveContext = context;
        SaveSetManifest? manifest = null;
        Exception? failure = null;
        bool ran = _runtime.TryUseActiveGeneration<IAccountingGenerationStateProvider, bool>(
            (world, _, provider) =>
            {
                AccountingStore accounting = provider.Accounting;
                try
                {
                    ISaveSetParticipant ecs = new EcsWorldSaveSetParticipant(
                        world,
                        _catalog,
                        _restoreOptions,
                        _stagingWorldFactory);
                    ISaveSetParticipant accountingParticipant = new AccountingSaveSetParticipant(
                        accounting,
                        _accountingOptions);
                    manifest = _coordinator.Save([ecs, accountingParticipant], saveContext, captureBoundary);
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
                "Persistence requires a running ServerRuntime generation with Accounting state.");
            return new SaveAttemptResult(false, null, failure);
        }
        return new SaveAttemptResult(succeeded, manifest, failure);
    }

    /// <summary>
    /// Prepares ECS and Accounting privately, resolves Accounting links through
    /// the staged ECS participant, then performs one Runtime generation root
    /// publication. All expected preparation/validation failures are returned
    /// without touching active state.
    /// </summary>
    public LoadAttemptResult TryLoad(
        SaveSetRecoveryMode recoveryMode = SaveSetRecoveryMode.StrictCurrent)
    {
        EnsureOwnerThread();
        ulong publishedGeneration = 0;
        bool publicationAttempted = false;
        EcsWorld? stagedWorld = null;
        AccountingStore? stagedAccounting = null;
        try
        {
            var ecsParticipant = new EcsWorldSaveSetParticipant(
                _catalog,
                _restoreOptions,
                _stagingWorldFactory);
            var accountingParticipant = new AccountingSaveSetParticipant(_accountingOptions);
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
            stagedAccounting = accounting.TakeStore();
            prepared.Commit(_ =>
            {
                publicationAttempted = true;
                if (!_runtime.TryPublishPreparedGeneration(stagedWorld, stagedAccounting, out publishedGeneration))
                    throw new InvalidOperationException("The prepared generation was not published.");
            });
            return new LoadAttemptResult(true, publishedGeneration, null);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            if (!publicationAttempted)
            {
                stagedAccounting?.Dispose();
                stagedWorld?.Dispose();
            }
            return new LoadAttemptResult(false, 0, exception);
        }
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Runtime persistence is bound to its creating owner thread.");
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
