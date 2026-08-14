using System.Text;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Reflection;
using UltraSim.ECS;
using UltraSim.Server.Persistence;

if (args.Length == 3 && args[0] == "--crash-writer")
{
    string childRoot = args[1];
    SaveSetFaultPoint childPoint = Enum.Parse<SaveSetFaultPoint>(args[2]);
    new SaveSetCoordinator(childRoot, faults: new ExitAt(childPoint)).Save(
        [new FakeParticipant("world", "generation-2")], new SaveSetContext(2, 2), action => action());
    Environment.Exit(98);
}

CrashPointsLeaveLastOrNewCompleteGeneration();
PrepareFailureNeverPublishesMixedParticipants();
CompatibleRecoveryFindsCompleteGenerationWithoutCurrentPointer();
EcsParticipantPublishesOnlyThroughGenerationCommit();
EncodingAndDurabilityOccurOutsideCommonCaptureEpoch();
ParticipantArtifactBoundStopsTheWrite();
PublicationAttemptCannotBeRetried();
MalformedCurrentFallsBackOnlyInCompatibleMode();
PreparedParticipantKeyMustMatch();
CompatibleRecoveryDoesNotHideImplementationFailures();
ConcurrentWritersCannotMoveCurrentBackward();
CapturedStateIsDisposedWhenPublicationPreflightFails();
StagedNamespaceCannotFallBackToExternalAuthority();
EcsSnapshotSequenceMustMatchSaveSet();
ProcessTerminationLeavesRecoverableGeneration();
Console.WriteLine("All save-set transaction tests passed.");

static void CrashPointsLeaveLastOrNewCompleteGeneration()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var participant = new FakeParticipant("world", "generation-1");
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([participant], new SaveSetContext(1, 100), action => action());
        ulong sequence = 2;
        foreach (SaveSetFaultPoint point in Enum.GetValues<SaveSetFaultPoint>().Where(static point => point != SaveSetFaultPoint.None))
        {
            participant.Value = "generation-" + sequence;
            var faulted = new SaveSetCoordinator(root, faults: new ThrowAt(point));
            try { faulted.Save([participant], new SaveSetContext(sequence, checked((long)sequence * 100)), action => action()); }
            catch (InjectedFailure) { }
            using PreparedLoadGeneration prepared = new SaveSetCoordinator(root).PrepareLoad(
                [participant], EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete);
            bool committed = false;
            prepared.Commit(states =>
            {
                if (states.Count != 1 || states[0] is not FakePrepared fake || string.IsNullOrEmpty(fake.Value))
                    throw new InvalidOperationException("Prepared generation is incomplete.");
                committed = true;
            });
            if (!committed) throw new InvalidOperationException("A valid generation was not atomically published.");
            sequence++;
        }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void PrepareFailureNeverPublishesMixedParticipants()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var first = new FakeParticipant("accounts", "a");
        var second = new FakeParticipant("world", "w");
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([first, second], new SaveSetContext(1, 1), action => action());
        second.FailPrepare = true;
        bool published = false;
        try
        {
            using PreparedLoadGeneration prepared = coordinator.PrepareLoad([first, second], EmptyResolver.Instance);
            prepared.Commit(_ => published = true);
        }
        catch (InvalidDataException) { }
        if (published || first.LastPrepared is null || !first.LastPrepared.Disposed)
            throw new InvalidOperationException("Failed participant preparation leaked or published partial state.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void CompatibleRecoveryFindsCompleteGenerationWithoutCurrentPointer()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var participant = new FakeParticipant("world", "recoverable");
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([participant], new SaveSetContext(1, 1), action => action());
        File.Delete(Path.Combine(root, "CURRENT"));
        using PreparedLoadGeneration prepared = coordinator.PrepareLoad(
            [participant], EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete);
        bool committed = false;
        prepared.Commit(states => committed = states.Count == 1 && ((FakePrepared)states[0]).Value == "recoverable");
        if (!committed) throw new InvalidOperationException("Compatible recovery did not locate the complete generation.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void EcsParticipantPublishesOnlyThroughGenerationCommit()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        ComponentSchema schema = ComponentSchema.Create(new SchemaId(0x34, 0x34),
        [
            ComponentRegistration.Create<CoordinatorValue>(new ComponentId(0), "coordinator.value", StableHash64.Compute("coordinator.value"),
                version: 1, ComponentFlags.Persistent, persistenceId: 340034,
                ComponentPersistenceMode.Value),
        ]);
        PersistenceCatalog catalog = new PersistenceCatalogBuilder(schema)
            .Register<CoordinatorValue>(new PersistenceSchemaFingerprint(0x34, 0x3400), new CoordinatorValueCodec())
            .Build();
        var identity = new PersistenceIdentityOptions(new PersistentWorldId(0x340034));
        using var source = new EcsWorld(schema, CoordinatorOptions(identity));
        ArchetypeHandle shape = source.RegisterArchetype([new ComponentId(0)], ChunkLayoutOptions.KiB16, 1);
        ComponentLookup<CoordinatorValue> lookup = source.PrepareLookup<CoordinatorValue>();
        source.Seal(); source.Initialize(); source.BeginRun();
        AssertCoordinator(source.TryCreate(shape, out Entity entity), "source entity create failed");
        AssertCoordinator(source.TryWrite(entity, lookup, new CoordinatorValue { Value = 12345 }), "source write failed");

        var restoreOptions = new PersistenceRestoreOptions(
            new PersistenceLimits(1_000_000, 100, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16);
        ComponentLookup<CoordinatorValue> restoredLookup = default;
        var participant = new EcsWorldSaveSetParticipant(source, catalog, restoreOptions,
            requirements =>
            {
                var staging = new EcsWorld(requirements.Schema, CoordinatorOptions(requirements.IdentityOptions));
                restoredLookup = staging.PrepareLookup<CoordinatorValue>();
                return staging;
            });
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([participant], new SaveSetContext(1, 1), action => action());
        EcsWorld? published = null;
        using (PreparedLoadGeneration prepared = coordinator.PrepareLoad([participant], EmptyResolver.Instance))
        {
            prepared.Commit(states => published = ((PreparedEcsWorldParticipant)states[0]).TakeWorld());
        }
        using (published)
        {
            if (published is null || published.AliveCount != 1)
                throw new InvalidOperationException("staging world was not atomically published");
            AssertCoordinator(published.TryGetEntity(0, out Entity restoredEntity) &&
                published.TryRead(restoredEntity, restoredLookup, out CoordinatorValue value) && value.Value == 12345,
                "published ECS participant state differs from the saved generation");
        }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void EncodingAndDurabilityOccurOutsideCommonCaptureEpoch()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        bool boundary = false, capturedInside = false, wroteInside = false;
        var participant = new FakeParticipant("world", "epoch")
        {
            OnCapture = () => capturedInside = boundary,
            OnWrite = () => wroteInside = boundary,
        };
        new SaveSetCoordinator(root).Save([participant], new SaveSetContext(1, 1), action =>
        {
            boundary = true;
            try { action(); }
            finally { boundary = false; }
        });
        if (!capturedInside || wroteInside)
            throw new InvalidOperationException("Participant encoding leaked into the common logical capture epoch.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void ParticipantArtifactBoundStopsTheWrite()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var participant = new FakeParticipant("world", "eight!!!") { ArtifactBound = 4 };
        try
        {
            new SaveSetCoordinator(root).Save([participant], new SaveSetContext(1, 1), action => action());
            throw new InvalidOperationException("An oversized participant artifact was accepted.");
        }
        catch (IOException) { }
        string pending = Path.Combine(root, "generations", "00000000000000000001.pending");
        if (Directory.Exists(pending))
            throw new InvalidOperationException("An ordinary save failure left its private pending generation behind.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void PublicationAttemptCannotBeRetried()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var participant = new FakeParticipant("world", "once");
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([participant], new SaveSetContext(1, 1), action => action());
        using PreparedLoadGeneration prepared = coordinator.PrepareLoad([participant], EmptyResolver.Instance);
        int attempts = 0;
        try { prepared.Commit(_ => { attempts++; throw new InvalidOperationException("after publication"); }); }
        catch (InvalidOperationException exception) when (exception.Message == "after publication") { }
        try
        {
            prepared.Commit(_ => attempts++);
            throw new InvalidOperationException("A partially published generation was retryable.");
        }
        catch (InvalidOperationException exception) when (exception.Message != "A partially published generation was retryable.") { }
        if (attempts != 1) throw new InvalidOperationException("Publication callback executed more than once.");
        if (participant.LastPrepared is null || participant.LastPrepared.Disposed)
            throw new InvalidOperationException("Coordinator reclaimed participant ownership after publication began.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void MalformedCurrentFallsBackOnlyInCompatibleMode()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var participant = new FakeParticipant("world", "recoverable");
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([participant], new SaveSetContext(1, 1), action => action());
        File.WriteAllText(Path.Combine(root, "CURRENT"), "../outside");
        using PreparedLoadGeneration recovered = coordinator.PrepareLoad(
            [participant], EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete);
        if (recovered.Sequence != 1) throw new InvalidOperationException("Malformed CURRENT did not fall back to a complete generation.");
        try
        {
            coordinator.PrepareLoad([participant], EmptyResolver.Instance, SaveSetRecoveryMode.StrictCurrent);
            throw new InvalidOperationException("Strict recovery accepted malformed CURRENT.");
        }
        catch (InvalidDataException) { }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void PreparedParticipantKeyMustMatch()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var participant = new FakeParticipant("world", "wrong") { PreparedKey = "accounts" };
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([participant], new SaveSetContext(1, 1), action => action());
        try
        {
            coordinator.PrepareLoad([participant], EmptyResolver.Instance);
            throw new InvalidOperationException("A mismatched prepared participant key was accepted.");
        }
        catch (InvalidDataException) { }
        if (participant.LastPrepared is null || !participant.LastPrepared.Disposed)
            throw new InvalidOperationException("The rejected prepared participant was not disposed.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void CompatibleRecoveryDoesNotHideImplementationFailures()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var participant = new FakeParticipant("world", "valid");
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([participant], new SaveSetContext(1, 1), action => action());
        participant.PrepareException = new InvalidOperationException("implementation defect");
        try
        {
            coordinator.PrepareLoad([participant], EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete);
            throw new InvalidOperationException("Compatible recovery swallowed an implementation failure.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "implementation defect") { }
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void ConcurrentWritersCannotMoveCurrentBackward()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var newer = new FakeParticipant("world", "new") { OnWrite = () => { entered.Set(); release.Wait(); } };
        var older = new FakeParticipant("world", "old");
        Task<SaveSetManifest> newSave = Task.Run(() => new SaveSetCoordinator(root).Save(
            [newer], new SaveSetContext(2, 2), action => action()));
        if (!entered.Wait(TimeSpan.FromSeconds(5))) throw new InvalidOperationException("Concurrent writer fixture did not enter capture.");
        Task oldSave = Task.Run(() => new SaveSetCoordinator(root).Save(
            [older], new SaveSetContext(1, 1), action => action()));
        release.Set();
        _ = newSave.GetAwaiter().GetResult();
        try
        {
            oldSave.GetAwaiter().GetResult();
            throw new InvalidOperationException("An older concurrent save moved publication backward.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("does not advance", StringComparison.Ordinal)) { }
        string current = File.ReadAllText(Path.Combine(root, "CURRENT")).Trim();
        if (current != "00000000000000000002") throw new InvalidOperationException("CURRENT moved backward after concurrent saves.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void CapturedStateIsDisposedWhenPublicationPreflightFails()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var participant = new FakeParticipant("world", "first");
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([participant], new SaveSetContext(1, 1), action => action());
        using CapturedSaveSet captured = coordinator.Capture(
            [participant], new SaveSetContext(1, 2), action => action());
        FakeCapture owned = participant.LastCapture ?? throw new InvalidOperationException("No capture was produced.");
        try
        {
            coordinator.Publish(captured);
            throw new InvalidOperationException("A non-advancing save sequence was accepted.");
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("does not advance", StringComparison.Ordinal)) { }
        if (!owned.Disposed)
            throw new InvalidOperationException("Publication preflight failure leaked captured participant ownership.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void StagedNamespaceCannotFallBackToExternalAuthority()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var world = new PersistentWorldId(0x9933);
        var key = new PersistentEntityKey(world, new PersistentEntityId(7));
        var owner = new NamespaceParticipant("owner", world);
        var consumer = new ResolvingParticipant("consumer", key);
        var coordinator = new SaveSetCoordinator(root);
        coordinator.Save([owner, consumer], new SaveSetContext(1, 1), action => action());
        using PreparedLoadGeneration prepared = coordinator.PrepareLoad(
            [owner, consumer], new ExternalResolver(key, new object()));
        if (consumer.LastPrepared?.Resolved == true)
            throw new InvalidOperationException("A missing staged entity fell through to external authority.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static void EcsSnapshotSequenceMustMatchSaveSet()
{
    ComponentSchema schema = ComponentSchema.Create(new SchemaId(0x88, 0x88),
    [
        ComponentRegistration.Create<CoordinatorValue>(new ComponentId(0), "coordinator.value", StableHash64.Compute("coordinator.value"),
            version: 1, ComponentFlags.Persistent, persistenceId: 880088, ComponentPersistenceMode.Value),
    ]);
    PersistenceCatalog catalog = new PersistenceCatalogBuilder(schema)
        .Register<CoordinatorValue>(new PersistenceSchemaFingerprint(0x88, 0x8800), new CoordinatorValueCodec()).Build();
    using var source = new EcsWorld(schema, CoordinatorOptions(new PersistenceIdentityOptions(new PersistentWorldId(0x880088))));
    ArchetypeHandle shape = source.RegisterArchetype([new ComponentId(0)], ChunkLayoutOptions.KiB16, 1);
    source.Seal(); source.Initialize(); source.BeginRun();
    AssertCoordinator(source.TryCreate(shape, out _), "sequence fixture create failed");
    PersistenceSnapshotArtifact artifact = EcsPersistence.Capture(source, catalog, new PersistenceCaptureOptions(1), out _);
    var participant = new EcsWorldSaveSetParticipant(source, catalog,
        new PersistenceRestoreOptions(new PersistenceLimits(1_000_000, 10, 10, 10, 100, 1_000_000, 1_000_000), ChunkLayoutOptions.KiB16),
        requirements => new EcsWorld(requirements.Schema, CoordinatorOptions(requirements.IdentityOptions)));
    try
    {
        participant.Prepare(artifact.Bytes, new SaveSetContext(2, 2));
        throw new InvalidOperationException("ECS artifact/save-set sequence mismatch was accepted.");
    }
    catch (PersistenceValidationException) { }
}

static void ProcessTerminationLeavesRecoverableGeneration()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-save-set-" + Guid.NewGuid().ToString("N"));
    try
    {
        var participant = new FakeParticipant("world", "generation-1");
        new SaveSetCoordinator(root).Save([participant], new SaveSetContext(1, 1), action => action());
        string assembly = Assembly.GetExecutingAssembly().Location;
        using var child = Process.Start(new ProcessStartInfo("dotnet")
        {
            ArgumentList = { assembly, "--crash-writer", root, nameof(SaveSetFaultPoint.AfterPendingDirectoryFlush) },
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        }) ?? throw new InvalidOperationException("Could not start crash-recovery child process.");
        if (!child.WaitForExit(15_000) || child.ExitCode != 97)
            throw new InvalidOperationException("Crash-recovery child did not terminate at the requested durable boundary.");
        using PreparedLoadGeneration recovered = new SaveSetCoordinator(root).PrepareLoad(
            [participant], EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete);
        if (recovered.Sequence != 1) throw new InvalidOperationException("A process-terminated pending generation displaced the last complete generation.");
        participant.Value = "generation-3";
        new SaveSetCoordinator(root).Save([participant], new SaveSetContext(3, 3), action => action());
        if (Directory.EnumerateDirectories(Path.Combine(root, "generations"), "*.pending").Any())
            throw new InvalidOperationException("The next exclusive writer did not clean the abandoned pending generation.");
    }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static EcsWorldOptions CoordinatorOptions(PersistenceIdentityOptions identity) => new(
    new EntityCapacity(100), maximumArchetypes: 4, maximumPreparedPlans: 4,
    maximumPreparedTransitions: 0, maximumStructuralCommands: 0, persistence: identity);

static void AssertCoordinator(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed class FakeParticipant : ISaveSetParticipant
{
    internal FakeParticipant(string key, string value) { Key = key; Value = value; }
    public string Key { get; }
    internal string Value { get; set; }
    internal bool FailPrepare { get; set; }
    internal Action? OnCapture { get; init; }
    internal Action? OnWrite { get; init; }
    internal long ArtifactBound { get; init; } = Array.MaxLength;
    internal string? PreparedKey { get; init; }
    internal Exception? PrepareException { get; set; }
    public long MaximumArtifactBytes => ArtifactBound;
    internal FakePrepared? LastPrepared { get; private set; }
    internal FakeCapture? LastCapture { get; private set; }
    public ICapturedSaveSetParticipant Capture(in SaveSetContext context)
    {
        OnCapture?.Invoke();
        return LastCapture = new FakeCapture(Key, Encoding.UTF8.GetBytes(Value), OnWrite);
    }
    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        if (PrepareException is not null) throw PrepareException;
        if (FailPrepare) throw new InvalidDataException("injected prepare failure");
        return LastPrepared = new FakePrepared(PreparedKey ?? Key, Encoding.UTF8.GetString(artifact.Span));
    }
}

sealed class FakeCapture : ICapturedSaveSetParticipant
{
    private byte[]? _bytes;
    private readonly Action? _onWrite;
    internal FakeCapture(string key, byte[] bytes, Action? onWrite) { Key = key; _bytes = bytes; _onWrite = onWrite; }
    public string Key { get; }
    internal bool Disposed { get; private set; }
    public void WriteArtifact(Stream destination)
    {
        byte[] bytes = _bytes ?? throw new InvalidOperationException("capture already consumed");
        _onWrite?.Invoke();
        destination.Write(bytes);
        _bytes = null;
    }
    public void Dispose() { _bytes = null; Disposed = true; }
}

sealed class NamespaceParticipant : ISaveSetParticipant
{
    private readonly PersistentWorldId _world;
    internal NamespaceParticipant(string key, PersistentWorldId world) { Key = key; _world = world; }
    public string Key { get; }
    public ICapturedSaveSetParticipant Capture(in SaveSetContext context) =>
        new FakeCapture(Key, [1], null);
    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context) =>
        new NamespacePrepared(Key, _world);
}

sealed class NamespacePrepared : IPreparedLoadParticipant,
    IPreparedPersistentEntityResolver, IPreparedPersistentEntityNamespace
{
    private readonly PersistentWorldId _world;
    internal NamespacePrepared(string key, PersistentWorldId world) { Key = key; _world = world; }
    public string Key { get; }
    public bool Owns(PersistentEntityKey key) => key.World == _world;
    public bool TryResolve(PersistentEntityKey key, out object? value) { value = null; return false; }
    public void Resolve(IPersistentEntityResolver resolver) { }
    public void Validate() { }
    public void Dispose() { }
}

sealed class ResolvingParticipant : ISaveSetParticipant
{
    private readonly PersistentEntityKey _key;
    internal ResolvingParticipant(string key, PersistentEntityKey entityKey) { Key = key; _key = entityKey; }
    public string Key { get; }
    internal ResolvingPrepared? LastPrepared { get; private set; }
    public ICapturedSaveSetParticipant Capture(in SaveSetContext context) =>
        new FakeCapture(Key, [1], null);
    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context) =>
        LastPrepared = new ResolvingPrepared(Key, _key);
}

sealed class ResolvingPrepared : IPreparedLoadParticipant
{
    private readonly PersistentEntityKey _key;
    internal ResolvingPrepared(string key, PersistentEntityKey entityKey) { Key = key; _key = entityKey; }
    public string Key { get; }
    internal bool Resolved { get; private set; }
    public void Resolve(IPersistentEntityResolver resolver) => Resolved = resolver.TryResolve(_key, out _);
    public void Validate() { }
    public void Dispose() { }
}

sealed class ExternalResolver : IExternalPersistentEntityResolver
{
    private readonly PersistentEntityKey _key;
    private readonly object _value;
    internal ExternalResolver(PersistentEntityKey key, object value) { _key = key; _value = value; }
    public bool TryResolve(PersistentEntityKey key, out object? value)
    {
        value = key.Equals(_key) ? _value : null;
        return value is not null;
    }
}

sealed class FakePrepared : IPreparedLoadParticipant
{
    internal FakePrepared(string key, string value) { Key = key; Value = value; }
    public string Key { get; }
    internal string Value { get; }
    internal bool Disposed { get; private set; }
    public void Resolve(IPersistentEntityResolver resolver) { }
    public void Validate() { if (Value.Length == 0) throw new InvalidDataException(); }
    public void Dispose() => Disposed = true;
}

sealed class EmptyResolver : IExternalPersistentEntityResolver
{
    internal static readonly EmptyResolver Instance = new();
    public bool TryResolve(PersistentEntityKey key, out object? value) { value = null; return false; }
}

sealed class ThrowAt : ISaveSetFaultInjector
{
    private readonly SaveSetFaultPoint _point;
    internal ThrowAt(SaveSetFaultPoint point) => _point = point;
    public void Reach(SaveSetFaultPoint point, string path) { if (point == _point) throw new InjectedFailure(); }
}

sealed class InjectedFailure : Exception;

sealed class ExitAt : ISaveSetFaultInjector
{
    private readonly SaveSetFaultPoint _point;
    internal ExitAt(SaveSetFaultPoint point) => _point = point;
    public void Reach(SaveSetFaultPoint point, string path)
    {
        if (point == _point) Environment.Exit(97);
    }
}

struct CoordinatorValue : IComponent<CoordinatorValue> { public int Value; }

sealed class CoordinatorValueCodec : IPersistenceComponentCodec<CoordinatorValue>, IPersistenceBatchComponentCodec<CoordinatorValue>
{
    public int EncodedSize => sizeof(int);
    public void Encode(in CoordinatorValue value, Span<byte> destination, PersistenceWriteContext context) =>
        BinaryPrimitives.WriteInt32LittleEndian(destination, value.Value);
    public void Decode(ReadOnlySpan<byte> source, ref CoordinatorValue value, PersistenceReadContext context) =>
        value.Value = BinaryPrimitives.ReadInt32LittleEndian(source);
    public void EncodeBatch(ReadOnlySpan<CoordinatorValue> values, Span<byte> destination, PersistenceWriteContext context)
    {
        for (int i = 0; i < values.Length; i++) BinaryPrimitives.WriteInt32LittleEndian(destination[(i * 4)..], values[i].Value);
    }
    public void DecodeBatch(ReadOnlySpan<byte> source, Span<CoordinatorValue> values, PersistenceReadContext context)
    {
        for (int i = 0; i < values.Length; i++) values[i].Value = BinaryPrimitives.ReadInt32LittleEndian(source[(i * 4)..]);
    }
}
