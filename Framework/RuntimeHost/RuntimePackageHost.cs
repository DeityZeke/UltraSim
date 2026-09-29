using System.Collections.ObjectModel;

namespace UltraSim.Framework.RuntimeHost;

public enum RuntimeHostState : byte { Created, Registering, Configuring, Initializing, Running, Stopping, Stopped, Faulted }
public enum RuntimePackageState : byte { Selected, Registering, Registered, Configured, Initialized, Running, Stopping, Stopped, Faulted }

public readonly record struct ModuleContributionId
{
    public ModuleContributionId(string value) { ModuleIdentity.Validate(value, nameof(value)); Value = value; }
    public string Value { get; }
    public bool IsValid => Value is not null;
}

public interface IRuntimePackage
{
    ModulePackageManifest Manifest { get; }
    void Register(RuntimePackageContext context);
    void Configure(RuntimePackageContext context);
    void Initialize(RuntimePackageContext context);
    void Stop();
}

/// <summary>Cold management publication owned by the host; retired before package owners stop.</summary>
public interface IRuntimeHostPublication { void Retire(); }

public sealed record RuntimePackageSnapshot(ModulePackageId Id, RuntimePackageState State);
public sealed record RuntimeHostSnapshot(RuntimeHostState State, ulong Generation, string? ManifestFingerprint,
    ReadOnlyCollection<RuntimePackageSnapshot> Packages);

/// <summary>Retained handle is valid only in the running composition that created it.</summary>
public readonly record struct RuntimeCapabilityHandle<T>(ModuleCapabilityId Id, ulong Generation) where T : class;

/// <summary>Cold explicit registration; contributors are not exclusive capability providers.</summary>
public sealed class RuntimePackageContext
{
    private readonly RuntimePackageHost _host;
    internal RuntimePackageContext(RuntimePackageHost host, ModulePackageId package)
    { _host = host; Package = package; Generation = host.Generation; }
    public ModulePackageId Package { get; }
    public ulong Generation { get; }
    public void Provide(ModuleCapabilityId id, object implementation) => _host.Provide(Package, Generation, id, implementation);
    public void Contribute(ModuleContributionId id, object contribution) => _host.Contribute(Package, Generation, id, contribution);
    public bool TryGet<T>(ModuleCapabilityId id, out T? implementation) where T : class =>
        _host.TryGetForPackage(Package, Generation, id, out implementation);
}

/// <summary>Single-owner, cold composition lifetime. No tick hooks, reflection or global registration.</summary>
public sealed class RuntimePackageHost
{
    private sealed class Entry(IRuntimePackage package)
    {
        public IRuntimePackage Package = package;
        public RuntimePackageState State = RuntimePackageState.Selected;
        public bool Entered;
    }
    private readonly int _ownerThread = Environment.CurrentManagedThreadId;
    private readonly Dictionary<ModuleCapabilityId, (ModulePackageId Owner, object Value)> _capabilities = new();
    private readonly Dictionary<ModuleContributionId, List<(ModulePackageId Owner, object Value)>> _contributions = new();
    private readonly List<IRuntimeHostPublication> _publications = new();
    private Entry[] _entries = [];
    private ModuleCompositionPlan? _plan;
    private ModulePackageId _registeringPackage;
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
            Array.AsReadOnly(_entries.Select(e => new RuntimePackageSnapshot(e.Package.Manifest.Id, e.State)).ToArray()));
    }
    public RuntimeCapabilityHandle<T> Handle<T>(ModuleCapabilityId id) where T : class
    {
        EnsureOwner();
        if (_state != RuntimeHostState.Running || !_capabilities.TryGetValue(id, out var item) || item.Value is not T)
            throw new InvalidOperationException("Capability is not running or has the wrong contract type.");
        return new(id, _generation);
    }
    public bool TryGet<T>(RuntimeCapabilityHandle<T> handle, out T? implementation) where T : class
    {
        EnsureOwner(); implementation = null;
        if (_state != RuntimeHostState.Running || handle.Generation != _generation) return false;
        if (!_capabilities.TryGetValue(handle.Id, out var item)) return false;
        implementation = item.Value as T; return implementation is not null;
    }
    public int ContributionCount(ModuleContributionId id)
    {
        EnsureOwner(); return _state == RuntimeHostState.Running && _contributions.TryGetValue(id, out var list) ? list.Count : 0;
    }
    public void AttachPublication(IRuntimeHostPublication publication)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(publication);
        if (_state != RuntimeHostState.Running || _publications.Count == ModulePackageManifest.MaximumRelations)
            throw new InvalidOperationException("Runtime publication cannot attach outside bounded Running state.");
        _publications.Add(publication);
    }
    internal void Provide(ModulePackageId owner, ulong generation, ModuleCapabilityId id, object implementation)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(implementation);
        if (_state != RuntimeHostState.Registering || _generation != generation || _registeringPackage != owner ||
            !_plan!.OrderedPackages.Any(p => p.Id == owner && p.Provides.Contains(id)))
            throw new InvalidOperationException("Capability was not declared for this registering package.");
        if (!_capabilities.TryAdd(id, (owner, implementation))) throw new InvalidOperationException("Duplicate capability provider.");
    }
    internal void Contribute(ModulePackageId owner, ulong generation, ModuleContributionId id, object contribution)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(contribution);
        if (_state != RuntimeHostState.Registering || _generation != generation || _registeringPackage != owner || !id.IsValid)
            throw new InvalidOperationException("Contribution requires the active package Register stage.");
        if (!_contributions.TryGetValue(id, out var list)) _contributions.Add(id, list = new());
        if (list.Count == ModulePackageManifest.MaximumRelations) throw new InvalidOperationException("Contributor capacity exhausted.");
        list.Add((owner, contribution));
    }
    internal bool TryGetForPackage<T>(ModulePackageId owner, ulong generation, ModuleCapabilityId id, out T? implementation) where T : class
    {
        EnsureOwner(); implementation = null;
        if (_generation != generation || _state is not (RuntimeHostState.Configuring or RuntimeHostState.Initializing or RuntimeHostState.Running) ||
            !_plan!.OrderedPackages.Any(p => p.Id == owner && (p.Requires.Contains(id) || p.Optional.Contains(id)))) return false;
        if (!_capabilities.TryGetValue(id, out var entry)) return false;
        implementation = entry.Value as T; return implementation is not null;
    }
    public void Start(IEnumerable<IRuntimePackage> selected)
    {
        EnsureOwner(); ArgumentNullException.ThrowIfNull(selected);
        if (_state is not (RuntimeHostState.Created or RuntimeHostState.Stopped)) throw new InvalidOperationException("Host is not startable.");
        var bounded = new List<IRuntimePackage>();
        foreach (IRuntimePackage? package in selected)
        {
            if (package is null) throw new InvalidOperationException("Null package implementation.");
            if (bounded.Count == ModuleCompositionPlanner.MaximumPackages)
                throw new InvalidOperationException("Runtime package capacity exhausted.");
            bounded.Add(package);
        }
        IRuntimePackage[] implementations = bounded.ToArray();
        if (!ModuleCompositionPlanner.TryResolve(implementations.Select(p => p.Manifest), out var plan, out var error))
            throw new InvalidOperationException(error?.Detail ?? "Invalid package composition.");
        if (_generation == ulong.MaxValue) throw new InvalidOperationException("Runtime generation exhausted.");
        _generation++;
        _plan = plan;
        var byId = implementations.ToDictionary(p => p.Manifest.Id);
        _entries = plan!.OrderedPackages.Select(p => new Entry(byId[p.Id])).ToArray();
        _capabilities.Clear(); _contributions.Clear();
        try
        {
            _state = RuntimeHostState.Registering;
            foreach (Entry e in _entries)
            {
                e.Entered = true; e.State = RuntimePackageState.Registering;
                _registeringPackage = e.Package.Manifest.Id;
                e.Package.Register(new(this, e.Package.Manifest.Id));
                e.State = RuntimePackageState.Registered;
            }
            _registeringPackage = default;
            foreach (ModulePackageManifest p in plan.OrderedPackages)
                foreach (ModuleCapabilityId id in p.Provides)
                    if (!_capabilities.TryGetValue(id, out var provider) || provider.Owner != p.Id)
                        throw new InvalidOperationException($"Declared capability {id} was not registered by {p.Id}.");
            _state = RuntimeHostState.Configuring;
            foreach (Entry e in _entries)
            {
                e.Package.Configure(new(this, e.Package.Manifest.Id));
                e.State = RuntimePackageState.Configured;
            }
            _state = RuntimeHostState.Initializing;
            foreach (Entry e in _entries)
            {
                e.Package.Initialize(new(this, e.Package.Manifest.Id));
                e.State = RuntimePackageState.Initialized;
            }
            foreach (Entry e in _entries) e.State = RuntimePackageState.Running;
            _state = RuntimeHostState.Running;
        }
        catch (Exception failure)
        {
            _registeringPackage = default;
            List<Exception> failures = [failure];
            StopEntered(failures);
            _state = RuntimeHostState.Faulted;
            if (failures.Count == 1) throw;
            throw new AggregateException(failures);
        }
    }
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
        for (int i = _entries.Length - 1; i >= 0; i--)
        {
            Entry e = _entries[i];
            if (!e.Entered) continue;
            e.State = RuntimePackageState.Stopping;
            try { e.Package.Stop(); e.State = RuntimePackageState.Stopped; }
            catch (Exception failure) { e.State = RuntimePackageState.Faulted; failures.Add(failure); }
            e.Entered = false;
        }
        _capabilities.Clear(); _contributions.Clear();
    }
}
