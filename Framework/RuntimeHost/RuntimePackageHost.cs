using System.Collections.ObjectModel;

namespace UltraSim.Framework.RuntimeHost;

public enum RuntimeHostState : byte { Created, Registering, Configuring, Initializing, Running, Stopping, Stopped, Faulted }
public enum RuntimePackageState : byte { Selected, Available, Registering, Registered, Configured, Initialized, Running, Stopping, Stopped, Faulted }

public readonly record struct RuntimeContributionId
{
    public RuntimeContributionId(string value) { RuntimeIdentity.Validate(value, nameof(value)); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
}

public interface IRuntimePackageLifecycle
{
    void Register(RuntimePackageContext context);
    void Configure(RuntimePackageContext context);
    void Initialize(RuntimePackageContext context);
    void Stop();
}

/// <summary>Built-in lifecycle owner paired with its manifest.</summary>
public interface IRuntimePackage : IRuntimePackageLifecycle { RuntimePackageManifest Manifest { get; } }

/// <summary>One selected manifest may have no lifecycle owner; a binding has one explicit owner.</summary>
public readonly record struct RuntimePackageBinding(RuntimePackageManifest Manifest, IRuntimePackageLifecycle Entrypoint);

/// <summary>Cold management publication owned by the host; retired before package owners stop.</summary>
public interface IRuntimeHostPublication { void Retire(); }

/// <summary>Package-owned publication staged until successful initialization.</summary>
public interface IStagedRuntimePublication : IRuntimeHostPublication { void Publish(); }

public sealed record RuntimePackageBuildIdentity(RuntimePackageId Id, string CompileKey, string AssemblyHash, bool CacheHit);
public sealed record RuntimeCompositionBuildIdentity(string Fingerprint, IReadOnlyList<RuntimePackageBuildIdentity> Packages);
public sealed record RuntimePackageSnapshot(RuntimePackageId Id, RuntimePackageState State,
    string? CompileKey, bool CacheHit);
public sealed record RuntimeHostSnapshot(RuntimeHostState State, ulong Generation, string? ManifestFingerprint,
    ReadOnlyCollection<RuntimePackageSnapshot> Packages, string? RuntimeCompositionFingerprint);

/// <summary>Retained handle is valid only in the running composition that created it.</summary>
public readonly record struct RuntimeServiceHandle<T>(RuntimeServiceId Id, ulong Generation) where T : class;

/// <summary>Cold explicit registration; contributors are not exclusive runtime services.</summary>
public sealed class RuntimePackageContext
{
    private readonly RuntimePackageHost _host;
    internal RuntimePackageContext(RuntimePackageHost host, RuntimePackageId package)
    { _host = host; Package = package; Generation = host.Generation; }
    public RuntimePackageId Package { get; }
    public ulong Generation { get; }
    public void Provide(RuntimeServiceId id, object implementation) => _host.Provide(Package, Generation, id, implementation);
    public void Contribute(RuntimeContributionId id, object contribution) => _host.Contribute(Package, Generation, id, contribution);
    public bool TryGet<T>(RuntimeServiceId id, out T? implementation) where T : class =>
        _host.TryGetForPackage(Package, Generation, id, out implementation);
    public void StagePublication(IStagedRuntimePublication publication) =>
        _host.StagePublication(Package, Generation, publication);
}

/// <summary>Single-owner, cold composition lifetime. No tick hooks, reflection or global registration.</summary>
public sealed class RuntimePackageHost
{
    private sealed class Entry(RuntimePackageManifest manifest, IRuntimePackageLifecycle? package)
    {
        public RuntimePackageManifest Manifest = manifest;
        public IRuntimePackageLifecycle? Package = package;
        public RuntimePackageState State = package is null ? RuntimePackageState.Available : RuntimePackageState.Selected;
        public bool Entered;
    }
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<RuntimeServiceId, (RuntimePackageId Owner, object Value)> _services = new();
    private readonly Dictionary<RuntimeContributionId, List<(RuntimePackageId Owner, object Value)>> _contributions = new();
    private readonly List<IRuntimeHostPublication> _publications = new();
    private readonly List<IStagedRuntimePublication> _stagedPublications = new();
    private Entry[] _entries = [];
    private RuntimeCompositionPlan? _plan;
    private RuntimeCompositionBuildIdentity? _buildIdentity;
    private RuntimePackageId _registeringPackage;
    private RuntimePackageId _initializingPackage;
    private RuntimeHostState _state = RuntimeHostState.Created;
    private ulong _generation;
    public RuntimeHostState State { get { EnsureOwner(); return _state; } }
    public ulong Generation { get { EnsureOwner(); return _generation; } }
    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThread)
            throw new InvalidOperationException("Runtime host owner thread required.");
    }
    public RuntimeHostSnapshot CaptureSnapshot()
    {
        EnsureOwner();
        return new(_state, _generation, _plan?.ManifestFingerprint,
            Array.AsReadOnly(_entries.Select(e =>
            {
                RuntimePackageBuildIdentity? build = _buildIdentity?.Packages.FirstOrDefault(p => p.Id == e.Manifest.Id);
                return new RuntimePackageSnapshot(e.Manifest.Id, e.State, build?.CompileKey, build?.CacheHit ?? false);
            }).ToArray()), _buildIdentity?.Fingerprint);
    }
    public RuntimeServiceHandle<T> Handle<T>(RuntimeServiceId id) where T : class
    {
        EnsureOwner();
        if (_state != RuntimeHostState.Running || !_services.TryGetValue(id, out var item) || item.Value is not T)
            throw new InvalidOperationException("Runtime service is not running or has the wrong contract type.");
        return new(id, _generation);
    }
    public bool TryGet<T>(RuntimeServiceHandle<T> handle, out T? implementation) where T : class
    {
        EnsureOwner(); implementation = null;
        if (_state != RuntimeHostState.Running || handle.Generation != _generation) return false;
        if (!_services.TryGetValue(handle.Id, out var item)) return false;
        implementation = item.Value as T; return implementation is not null;
    }
    public int ContributionCount(RuntimeContributionId id)
    {
        EnsureOwner(); return _state == RuntimeHostState.Running && _contributions.TryGetValue(id, out var list) ? list.Count : 0;
    }
    public void AttachPublication(IRuntimeHostPublication publication)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(publication);
        if (_state != RuntimeHostState.Running || _publications.Count == RuntimePackageManifest.MaximumRelations)
            throw new InvalidOperationException("Runtime publication cannot attach outside bounded Running state.");
        _publications.Add(publication);
    }
    internal void StagePublication(RuntimePackageId owner, ulong generation, IStagedRuntimePublication publication)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(publication);
        if (_state != RuntimeHostState.Initializing || _generation != generation || _initializingPackage != owner ||
            _stagedPublications.Count == RuntimePackageManifest.MaximumRelations)
            throw new InvalidOperationException("Publication requires the active package Initialize stage.");
        _stagedPublications.Add(publication);
    }
    internal void Provide(RuntimePackageId owner, ulong generation, RuntimeServiceId id, object implementation)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(implementation);
        if (_state != RuntimeHostState.Registering || _generation != generation || _registeringPackage != owner ||
            !_plan!.OrderedPackages.Any(p => p.Id == owner && p.Services.Contains(id)))
            throw new InvalidOperationException("Runtime service was not declared for this registering package.");
        if (!_services.TryAdd(id, (owner, implementation))) throw new InvalidOperationException("Duplicate runtime service provider.");
    }
    internal void Contribute(RuntimePackageId owner, ulong generation, RuntimeContributionId id, object contribution)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(contribution);
        if (_state != RuntimeHostState.Registering || _generation != generation || _registeringPackage != owner || !id.IsValid)
            throw new InvalidOperationException("Contribution requires the active package Register stage.");
        if (!_contributions.TryGetValue(id, out var list)) _contributions.Add(id, list = new());
        if (list.Count == RuntimePackageManifest.MaximumRelations) throw new InvalidOperationException("Contributor capacity exhausted.");
        list.Add((owner, contribution));
    }
    internal bool TryGetForPackage<T>(RuntimePackageId owner, ulong generation, RuntimeServiceId id, out T? implementation) where T : class
    {
        EnsureOwner(); implementation = null;
        if (_generation != generation || _state is not (RuntimeHostState.Configuring or RuntimeHostState.Initializing or RuntimeHostState.Running) ||
            !_plan!.OrderedPackages.Any(p => p.Id == owner && (p.RequiresServices.Contains(id) || p.OptionalServices.Contains(id)))) return false;
        if (!_services.TryGetValue(id, out var entry)) return false;
        implementation = entry.Value as T; return implementation is not null;
    }
    public void Start(IEnumerable<IRuntimePackage> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        IRuntimePackage[] packages = selected.ToArray();
        if (packages.Any(p => p is null)) throw new InvalidOperationException("Null package implementation.");
        Start(packages.Select(p => p.Manifest), packages.Select(p => new RuntimePackageBinding(p.Manifest, p)));
    }
    /// <summary>Selected compiled vocabulary need not have a lifecycle entrypoint.</summary>
    public void Start(IEnumerable<RuntimePackageManifest> selected, IEnumerable<RuntimePackageBinding> implementations,
        RuntimeCompositionBuildIdentity? buildIdentity = null)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(selected); ArgumentNullException.ThrowIfNull(implementations);
        if (_state is not (RuntimeHostState.Created or RuntimeHostState.Stopped)) throw new InvalidOperationException("Host is not startable.");
        var bounded = new List<RuntimePackageManifest>();
        foreach (RuntimePackageManifest? manifest in selected)
        {
            if (manifest is null) throw new InvalidOperationException("Null package manifest.");
            if (bounded.Count == RuntimeCompositionPlanner.MaximumPackages)
                throw new InvalidOperationException("Runtime package capacity exhausted.");
            bounded.Add(manifest);
        }
        if (!RuntimeCompositionPlanner.TryResolve(bounded, out var plan, out var error))
            throw new InvalidOperationException(error?.Detail ?? "Invalid package composition.");
        if (buildIdentity is not null &&
            (!HexIdentity(buildIdentity.Fingerprint) || buildIdentity.Packages is null ||
             buildIdentity.Packages.Count != bounded.Count ||
             buildIdentity.Packages.Select(p => p.Id).Distinct().Count() != bounded.Count ||
             buildIdentity.Packages.Any(p => !bounded.Any(m => m.Id == p.Id) ||
                 !HexIdentity(p.CompileKey) || !HexIdentity(p.AssemblyHash))))
            throw new InvalidOperationException("Runtime composition build identity does not cover the selected packages.");
        var byId = new Dictionary<RuntimePackageId, IRuntimePackageLifecycle>();
        foreach (RuntimePackageBinding implementation in implementations)
            if (implementation.Entrypoint is null ||
                !bounded.Any(m => ReferenceEquals(m, implementation.Manifest)) ||
                !byId.TryAdd(implementation.Manifest.Id, implementation.Entrypoint))
                throw new InvalidOperationException("Runtime entrypoint is null, duplicated or not bound to the selected manifest.");
        foreach (RuntimePackageManifest manifest in bounded)
            if (manifest.Services.Count != 0 && !byId.ContainsKey(manifest.Id))
                throw new InvalidOperationException($"Runtime service package {manifest.Id} has no entrypoint.");
        if (_generation == ulong.MaxValue) throw new InvalidOperationException("Runtime generation exhausted.");
        _generation++;
        _plan = plan;
        _buildIdentity = buildIdentity is null ? null :
            new RuntimeCompositionBuildIdentity(buildIdentity.Fingerprint,
                Array.AsReadOnly(buildIdentity.Packages.ToArray()));
        _entries = plan!.OrderedPackages.Select(p => new Entry(p, byId.GetValueOrDefault(p.Id))).ToArray();
        _services.Clear(); _contributions.Clear(); _stagedPublications.Clear();
        try
        {
            _state = RuntimeHostState.Registering;
            foreach (Entry e in _entries)
            {
                if (e.Package is null) continue;
                e.Entered = true; e.State = RuntimePackageState.Registering;
                _registeringPackage = e.Manifest.Id;
                e.Package.Register(new(this, e.Manifest.Id));
                e.State = RuntimePackageState.Registered;
            }
            _registeringPackage = default;
            foreach (RuntimePackageManifest p in plan.OrderedPackages)
                foreach (RuntimeServiceId id in p.Services)
                    if (!_services.TryGetValue(id, out var provider) || provider.Owner != p.Id)
                        throw new InvalidOperationException($"Declared runtime service {id} was not registered by {p.Id}.");
            _state = RuntimeHostState.Configuring;
            foreach (Entry e in _entries)
            {
                if (e.Package is null) continue;
                e.Package.Configure(new(this, e.Manifest.Id));
                e.State = RuntimePackageState.Configured;
            }
            _state = RuntimeHostState.Initializing;
            foreach (Entry e in _entries)
            {
                if (e.Package is null) continue;
                _initializingPackage = e.Manifest.Id;
                e.Package.Initialize(new(this, e.Manifest.Id));
                e.State = RuntimePackageState.Initialized;
            }
            _initializingPackage = default;
            foreach (IStagedRuntimePublication publication in _stagedPublications) publication.Publish();
            foreach (Entry e in _entries) if (e.Package is not null) e.State = RuntimePackageState.Running;
            _state = RuntimeHostState.Running;
        }
        catch (Exception failure)
        {
            _registeringPackage = default;
            _initializingPackage = default;
            List<Exception> failures = [failure];
            StopEntered(failures);
            _state = RuntimeHostState.Faulted;
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }
    }
    private static bool HexIdentity(string? value) => value is { Length: 64 } &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
    public void Stop()
    {
        EnsureOwner();
        if (_state is RuntimeHostState.Created or RuntimeHostState.Stopped) return;
        if (_state != RuntimeHostState.Running) throw new InvalidOperationException("Host cannot stop from this state.");
        _state = RuntimeHostState.Stopping;
        List<Exception> failures = [];
        StopEntered(failures);
        _state = failures.Count == 0 ? RuntimeHostState.Stopped : RuntimeHostState.Faulted;
        if (failures.Count > 0) throw new AggregateException(failures);
    }
    private void StopEntered(List<Exception> failures)
    {
        for (int i = _publications.Count - 1; i >= 0; i--)
            try { _publications[i].Retire(); }
            catch (Exception failure) { failures.Add(failure); }
        _publications.Clear();
        for (int i = _stagedPublications.Count - 1; i >= 0; i--)
            try { _stagedPublications[i].Retire(); }
            catch (Exception failure) { failures.Add(failure); }
        _stagedPublications.Clear();
        for (int i = _entries.Length - 1; i >= 0; i--)
        {
            Entry e = _entries[i];
            if (!e.Entered) continue;
            e.State = RuntimePackageState.Stopping;
            try { e.Package!.Stop(); e.State = RuntimePackageState.Stopped; }
            catch (Exception failure) { e.State = RuntimePackageState.Faulted; failures.Add(failure); }
            e.Entered = false;
        }
        _services.Clear(); _contributions.Clear();
    }
}
