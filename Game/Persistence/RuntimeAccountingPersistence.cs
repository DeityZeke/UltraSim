using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Server.Persistence;
using UltraSim.Server.Runtime;

namespace UltraSim.Game.Persistence;

/// <summary>Result of a save attempt that did not mutate runtime authority.</summary>
public readonly record struct SaveAttemptResult(
    bool Succeeded,
    SaveSetManifest? Manifest,
    Exception? Failure);

/// <summary>Result of a staged load attempt.</summary>
public readonly record struct LoadAttemptResult(
    bool Succeeded,
    ulong GenerationId,
    Exception? Failure);

/// <summary>
/// Game-owned bridge joining the real ServerRuntime, ECS save participant, and
/// Accounting save participant. Runtime remains unaware of all Accounting
/// types; only this bridge transfers the prepared game state at publication.
/// </summary>
public sealed class RuntimeAccountingPersistence
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
        Exception? failure = null;
        ulong publishedGeneration = 0;
        bool ran = _runtime.TryUseActiveGeneration<IAccountingGenerationStateProvider, bool>(
            (world, _, provider) =>
            {
                AccountingStore activeAccounting = provider.Accounting;
                try
                {
                    var ecsParticipant = new EcsWorldSaveSetParticipant(
                        world,
                        _catalog,
                        _restoreOptions,
                        _stagingWorldFactory);
                    var accountingParticipant = new AccountingSaveSetParticipant(
                        activeAccounting,
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
                    EcsWorld stagedWorld = ecs.TakeWorld();
                    AccountingStore stagedAccounting = accounting.TakeStore();
                    bool publicationSucceeded = false;
                    prepared.Commit(_ =>
                    {
                        publicationSucceeded = _runtime.TryPublishPreparedGeneration(
                            stagedWorld,
                            stagedAccounting,
                            out publishedGeneration);
                    });
                    if (!publicationSucceeded)
                        throw new InvalidOperationException("The prepared generation was not published.");
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
            return new LoadAttemptResult(false, 0, failure);
        }
        return new LoadAttemptResult(succeeded, succeeded ? publishedGeneration : 0, failure);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Runtime persistence is bound to its creating owner thread.");
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
