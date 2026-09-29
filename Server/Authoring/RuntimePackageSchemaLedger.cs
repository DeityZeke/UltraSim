using System.Text;
using UltraSim.Framework.Persistence;
using UltraSim.Framework.RuntimeHost;

namespace UltraSim.Server.Authoring;

/// <summary>Exact package/schema admission artifact; remapping an ID does not migrate durable bytes.</summary>
internal sealed class RuntimePackageSchemaLedger : ISaveSetParticipant
{
    private readonly byte[] _expected;
    internal RuntimePackageSchemaLedger(IEnumerable<RuntimePackageManifest> selected)
    {
        var text = new StringBuilder("UltraSim.RuntimePackageSchema/v1\n");
        foreach (RuntimePackageManifest package in selected.Where(p => p.DurableSchema is not null).OrderBy(p => p.Id))
            text.Append(package.Id).Append('|').Append(package.Version).Append('|')
                .Append(package.DurableSchema!.Value.ParticipantService).Append('|')
                .Append(package.DurableSchema.Value.ArtifactKey).Append('\n');
        _expected = Encoding.UTF8.GetBytes(text.ToString());
        if (_expected.Length > MaximumArtifactBytes)
            throw new InvalidDataException("Runtime package schema ledger exceeds its artifact bound.");
    }
    public string Key => "runtime-package-schemas";
    public long MaximumArtifactBytes => 32 * 1024;
    public ICapturedSaveSetParticipant Capture(in SaveSetContext context) => new CaptureState(_expected);
    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        if (!artifact.Span.SequenceEqual(_expected))
            throw new InvalidDataException("Saved runtime package/schema identity differs; explicit migration or drop is required before authority publication.");
        return new PreparedState();
    }
    private sealed class CaptureState(byte[] bytes) : ICapturedSaveSetParticipant
    {
        public string Key => "runtime-package-schemas";
        public void WriteArtifact(Stream destination) => destination.Write(bytes);
        public void Dispose() { }
    }
    private sealed class PreparedState : IPreparedLoadParticipant
    {
        public string Key => "runtime-package-schemas";
        public void Resolve(IPersistentReferenceResolver resolver) { }
        public void Validate() { }
        public void Dispose() { }
    }
}
