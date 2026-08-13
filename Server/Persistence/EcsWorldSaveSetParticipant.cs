using UltraSim.ECS;

namespace UltraSim.Server.Persistence;

/// <summary>Engine-neutral whole-world ECS participant for one save-set generation.</summary>
public sealed class EcsWorldSaveSetParticipant : ISaveSetParticipant
{
    private readonly EcsWorld _source;
    private readonly PersistenceCatalog _catalog;
    private readonly PersistenceRestoreOptions _restoreOptions;
    private readonly Func<PersistenceRestoreRequirements, EcsWorld> _stagingWorldFactory;

    public EcsWorldSaveSetParticipant(
        EcsWorld source,
        PersistenceCatalog catalog,
        PersistenceRestoreOptions restoreOptions,
        Func<PersistenceRestoreRequirements, EcsWorld> stagingWorldFactory,
        string key = "world")
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _restoreOptions = restoreOptions;
        _stagingWorldFactory = stagingWorldFactory ?? throw new ArgumentNullException(nameof(stagingWorldFactory));
        if (string.IsNullOrEmpty(key)) throw new ArgumentException("A save-set participant key is required.", nameof(key));
        Key = key;
    }

    public string Key { get; }
    public long MaximumArtifactBytes => _restoreOptions.Limits.MaximumArtifactBytes;
    public PersistenceCaptureReport LastCaptureReport { get; private set; }

    public ICapturedSaveSetParticipant Capture(in SaveSetContext context) => new CapturedEcsWorldParticipant(
        Key,
        EcsPersistence.BeginCapture(_source, _catalog,
            new PersistenceCaptureOptions(context.Sequence, context.UnixMilliseconds)),
        report => LastCaptureReport = report);

    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        PersistenceRestorePlan plan = EcsPersistenceRestore.Preflight(artifact, _catalog, _restoreOptions);
        if (plan.Requirements.SnapshotSequence != context.Sequence)
            throw new PersistenceValidationException("ECS snapshot sequence disagrees with the enclosing save-set generation.");
        return new PreparedEcsWorldParticipant(Key, plan, _stagingWorldFactory);
    }
}

internal sealed class CapturedEcsWorldParticipant : ICapturedSaveSetParticipant
{
    private PersistenceWorldCapture? _capture;
    private readonly Action<PersistenceCaptureReport> _report;
    internal CapturedEcsWorldParticipant(string key, PersistenceWorldCapture capture,
        Action<PersistenceCaptureReport> report)
    { Key = key; _capture = capture; _report = report; }
    public string Key { get; }
    public void WriteArtifact(Stream destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        PersistenceWorldCapture capture = _capture ?? throw new InvalidOperationException("The ECS capture was already consumed.");
        PersistenceSnapshotArtifact artifact = EcsPersistence.CompleteCapture(capture, out PersistenceCaptureReport report);
        _capture = null;
        artifact.WriteTo(destination);
        _report(report);
    }
    public void Dispose()
    {
        _capture?.Dispose();
        _capture = null;
    }
}

/// <summary>Prepared ECS staging generation. The publishing host takes ownership exactly once.</summary>
public sealed class PreparedEcsWorldParticipant : IPreparedLoadParticipant
{
    private PersistenceRestorePlan? _plan;
    private readonly Func<PersistenceRestoreRequirements, EcsWorld> _stagingWorldFactory;
    private EcsWorld? _world;
    private bool _taken;

    internal PreparedEcsWorldParticipant(string key, PersistenceRestorePlan plan,
        Func<PersistenceRestoreRequirements, EcsWorld> stagingWorldFactory)
    {
        Key = key;
        _plan = plan;
        _stagingWorldFactory = stagingWorldFactory;
    }

    public string Key { get; }
    public PersistenceRestoreReport RestoreReport { get; private set; }
    public EcsWorld World => _world ?? throw new InvalidOperationException("The ECS participant has not completed Resolve.");

    public void Resolve(IPersistentEntityResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (_world is not null || _plan is null) throw new InvalidOperationException("The ECS participant was already resolved.");
        _world = _plan.Restore(_stagingWorldFactory, out PersistenceRestoreReport report);
        RestoreReport = report;
        _plan = null;
    }

    public void Validate()
    {
        if (_world is null || _world.State != EcsWorldState.Running)
            throw new PersistenceValidationException("The prepared ECS world is not a complete running staging generation.");
    }

    public EcsWorld TakeWorld()
    {
        if (_world is null || _taken) throw new InvalidOperationException("The prepared ECS world is unavailable.");
        _taken = true;
        EcsWorld world = _world;
        _world = null;
        return world;
    }

    public void Dispose()
    {
        if (!_taken) _world?.Dispose();
        _world = null;
        _plan = null;
    }
}
