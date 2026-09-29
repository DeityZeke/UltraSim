using UltraSim.Framework.ControlPlane;
using UltraSim.Framework.RuntimeHost;

namespace UltraSim.Bridges.ControlPlane.RuntimeHost;

/// <summary>Explicit opt-in publication; no reflection, scanning or Control Plane service lookup.</summary>
public sealed class RuntimeHostControlPublication : IRuntimeHostPublication, IDisposable
{
    private readonly ControlPlaneRegistry _registry;
    private readonly ulong _providerGeneration;
    private bool _retired;
    private RuntimeHostControlPublication(ControlPlaneRegistry registry, ulong providerGeneration)
    { _registry = registry; _providerGeneration = providerGeneration; }
    public ulong ProviderGeneration => _providerGeneration;
    public static RuntimeHostControlPublication Publish(RuntimePackageHost host, ControlPlaneRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(host); ArgumentNullException.ThrowIfNull(registry);
        if (host.State != RuntimeHostState.Running) throw new InvalidOperationException("Runtime host must be running.");
        if (!registry.TryRegister(new RuntimeHostControlProvider(host), out ulong generation, out string reason))
            throw new InvalidOperationException($"Runtime host Control Plane registration failed: {reason}.");
        var publication = new RuntimeHostControlPublication(registry, generation);
        try { host.AttachPublication(publication); }
        catch { publication.Retire(); throw; }
        return publication;
    }
    public void Retire()
    {
        if (_retired) return;
        // A separately retired/replaced registration no longer routes to this owner either.
        _registry.Retire(RuntimeHostControlProvider.Id, _providerGeneration);
        _retired = true;
    }
    public void Dispose() => Retire();
}

internal sealed class RuntimeHostControlProvider : IControlProvider
{
    internal static readonly ControlProviderId Id = new("framework.runtimehost");
    internal static readonly ControlCapabilityId Read = new("framework.runtimehost.read");
    private readonly RuntimePackageHost _host;
    internal RuntimeHostControlProvider(RuntimePackageHost host)
    {
        _host = host;
        Definition = new(Id, diagnostics: [
            new(new("state"), new("Framework/RuntimeHost"), "State", "Runtime composition lifecycle state.", new(ControlValueKind.Text), Read),
            new(new("generation"), new("Framework/RuntimeHost"), "Generation", "Process-local runtime generation fence.", new(ControlValueKind.UInt64), Read),
            new(new("manifest-fingerprint"), new("Framework/RuntimeHost"), "Manifest fingerprint", "Manifest-only plan identity, not a source/compile fingerprint.", new(ControlValueKind.Text), Read),
            new(new("runtime-fingerprint"), new("Framework/RuntimeHost"), "Runtime composition fingerprint", "Resolved compiled composition identity, not the process generation.", new(ControlValueKind.Text), Read)],
            collections: [new(new("packages"), new("Framework/RuntimeHost"), "Packages", "Selected package lifecycle states.",
                RuntimeCompositionPlanner.MaximumPackages,
                [new(new("version"), "Version", new(ControlValueKind.Text)),
                 new(new("stage"), "Stage", new(ControlValueKind.Text)),
                 new(new("state"), "State", new(ControlValueKind.Text)),
                 new(new("compile-key"), "Compile key", new(ControlValueKind.Text)),
                 new(new("cache"), "Cache", new(ControlValueKind.Text))], Read)]);
    }
    public ControlProviderDefinition Definition { get; }
    public ulong Revision => _host.Generation;
    public ControlState Capture(ControlId id) => throw new ArgumentException("Runtime host has no mutable controls.");
    public ControlValue Capture(DiagnosticId id)
    {
        RuntimeHostSnapshot snapshot = _host.CaptureSnapshot();
        return id.Value switch
        {
            "state" => ControlValue.Text(snapshot.State.ToString()),
            "generation" => ControlValue.UInt64(snapshot.Generation),
            "manifest-fingerprint" => ControlValue.Text(snapshot.ManifestFingerprint ?? ""),
            "runtime-fingerprint" => ControlValue.Text(snapshot.RuntimeCompositionFingerprint ?? ""),
            _ => throw new ArgumentException("Unknown runtime host diagnostic.")
        };
    }
    public IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id)
    {
        if (id.Value != "packages") throw new ArgumentException("Unknown runtime host collection.");
        return _host.CaptureSnapshot().Packages.Select(p => new ControlCollectionRow(
            new(p.Id.Value), [ControlValue.Text(p.Version.ToString()),
                ControlValue.Text(p.Stage.ToString()), ControlValue.Text(p.State.ToString()),
                ControlValue.Text(p.CompileKey ?? ""),
                ControlValue.Text(p.CompileKey is null ? "built-in" : p.CacheHit ? "hit" : "miss")])).ToArray();
    }
    public ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations) =>
        ControlOwnerResult.Reject(ControlResultCode.UnknownPublication);
    public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id, IReadOnlyList<ControlValue> arguments) =>
        ControlOwnerResult.Reject(ControlResultCode.UnknownPublication);
}
