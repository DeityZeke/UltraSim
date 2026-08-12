using System.Text;
using System.Buffers.Binary;
using UltraSim.ECS;
using UltraSim.Server.Persistence;

CrashPointsLeaveLastOrNewCompleteGeneration();
PrepareFailureNeverPublishesMixedParticipants();
CompatibleRecoveryFindsCompleteGenerationWithoutCurrentPointer();
EcsParticipantPublishesOnlyThroughGenerationCommit();
EncodingAndDurabilityOccurOutsideCommonCaptureEpoch();
ParticipantArtifactBoundStopsTheWrite();
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
        string pending = Path.Combine(root, "generations", "00000000000000000001.pending", "world.bin");
        if (!File.Exists(pending) || new FileInfo(pending).Length > participant.ArtifactBound)
            throw new InvalidOperationException("The participant artifact bound was enforced only after an oversized file was written.");
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
    internal long ArtifactBound { get; init; } = int.MaxValue;
    public long MaximumArtifactBytes => ArtifactBound;
    internal FakePrepared? LastPrepared { get; private set; }
    public ICapturedSaveSetParticipant Capture(in SaveSetContext context)
    {
        OnCapture?.Invoke();
        return new FakeCapture(Key, Encoding.UTF8.GetBytes(Value), OnWrite);
    }
    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        if (FailPrepare) throw new InvalidDataException("injected prepare failure");
        return LastPrepared = new FakePrepared(Key, Encoding.UTF8.GetString(artifact.Span));
    }
}

sealed class FakeCapture : ICapturedSaveSetParticipant
{
    private byte[]? _bytes;
    private readonly Action? _onWrite;
    internal FakeCapture(string key, byte[] bytes, Action? onWrite) { Key = key; _bytes = bytes; _onWrite = onWrite; }
    public string Key { get; }
    public void WriteArtifact(Stream destination)
    {
        byte[] bytes = _bytes ?? throw new InvalidOperationException("capture already consumed");
        _onWrite?.Invoke();
        destination.Write(bytes);
        _bytes = null;
    }
    public void Dispose() => _bytes = null;
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

sealed class EmptyResolver : IPersistentEntityResolver
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
