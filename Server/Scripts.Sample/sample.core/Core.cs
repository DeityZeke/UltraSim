using System;
using System.IO;
using Sample.Contracts;
using UltraSim.Framework.ControlPlane;
using UltraSim.Framework.Persistence;
using UltraSim.Framework.RuntimeHost;

namespace Sample;

public sealed class CoreEntry : IRuntimePackageLifecycle
{
    private readonly SampleStore _store = new();
    private readonly ControlPlaneRegistry _registry = new();
    private readonly Greeting _greeting = new();
    public void Register(RuntimePackageContext context)
    {
        context.Provide(new RuntimeServiceId(SampleIdentity.Greeting), _greeting);
        context.Provide(new RuntimeServiceId("sample.store"), _store);
        context.Provide(new RuntimeServiceId("sample.registry"), _registry);
    }
    public void Configure(RuntimePackageContext context) { }
    public void Initialize(RuntimePackageContext context) =>
        context.StagePublication(new SamplePublication(_registry));
    public void Stop() { }
}

public sealed class Greeting : IGreeting { public string Name => "hello"; }

public sealed class SampleStore : ISaveSetParticipant
{
    private int _value = 17;
    public string Key => "sample-store";
    public long MaximumArtifactBytes => 32;
    public int Value { get => _value; set => _value = value; }
    public ICapturedSaveSetParticipant Capture(in SaveSetContext context) => new Captured(Key, _value);
    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        if (artifact.Length != 5 || artifact.Span[0] != 1)
            throw new InvalidDataException("Unsupported sample store schema.");
        return new Prepared(this, BitConverter.ToInt32(artifact.Span.Slice(1)));
    }
    private sealed class Captured : ICapturedSaveSetParticipant
    {
        private readonly int _value;
        public Captured(string key, int value) { Key = key; _value = value; }
        public string Key { get; }
        public void WriteArtifact(Stream destination)
        {
            destination.WriteByte(1);
            destination.Write(BitConverter.GetBytes(_value));
        }
        public void Dispose() { }
    }
    private sealed class Prepared : IPreparedLoadParticipant
    {
        private readonly SampleStore _owner;
        private readonly int _value;
        public Prepared(SampleStore owner, int value) { _owner = owner; _value = value; }
        public string Key => _owner.Key;
        public void Resolve(IPersistentReferenceResolver resolver) { }
        public void Validate() { if (_value < 0) throw new InvalidDataException("Negative sample value."); }
        public void Dispose() { }
        public void Commit() => _owner.Value = _value;
    }
    public static void CommitPrepared(IPreparedLoadParticipant prepared) => ((Prepared)prepared).Commit();
}

public sealed class SamplePublication : IStagedRuntimePublication
{
    private static readonly ControlProviderId Id = new("sample.core");
    private readonly ControlPlaneRegistry _registry;
    private ulong _generation;
    public SamplePublication(ControlPlaneRegistry registry) { _registry = registry; }
    public void Publish()
    {
        if (!_registry.TryRegister(new SampleProvider(), out _generation, out string reason))
            throw new InvalidOperationException("Sample provider registration failed: " + reason);
    }
    public void Retire()
    {
        if (_generation != 0) _registry.Retire(Id, _generation);
        _generation = 0;
    }
    private sealed class SampleProvider : IControlProvider
    {
        public ControlProviderDefinition Definition { get; } = new(Id, diagnostics: new[]
        {
            new DiagnosticDescriptor(new DiagnosticId("status"),
                new ControlCategoryPath("Sample/Core"), "Status", "Package-owned explicit provider.",
                new ControlValueSchema(ControlValueKind.Text), new ControlCapabilityId("sample.read"))
        });
        public ulong Revision => 1;
        public ControlState Capture(ControlId id) => throw new ArgumentException("No mutable controls.");
        public ControlValue Capture(DiagnosticId id) => ControlValue.Text("Running");
        public System.Collections.Generic.IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id) =>
            Array.Empty<ControlCollectionRow>();
        public ControlOwnerResult Set(ControlRequestContext context,
            System.Collections.Generic.IReadOnlyList<ControlMutation> mutations) =>
            ControlOwnerResult.Reject(ControlResultCode.UnknownPublication);
        public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id,
            System.Collections.Generic.IReadOnlyList<ControlValue> arguments) =>
            ControlOwnerResult.Reject(ControlResultCode.UnknownPublication);
    }
}
