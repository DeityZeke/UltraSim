using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using UltraSim.Framework.RuntimeHost;
using UltraSim.Framework.Persistence;
using UltraSim.Framework.Definitions;

namespace UltraSim.Server.Authoring;

public sealed record ScriptPackageSnapshot(RuntimePackageId Id, RuntimePackageStage Stage,
    string CompileKey, string AssemblyHash, bool CacheHit, bool HasEntrypoint);

/// <summary>One private startup composition. Stop retires host ownership before releasing the load context.</summary>
public sealed class ServerScriptComposition : IDisposable
{
    private readonly ScriptAssemblyContext _loadContext;
    private bool _disposed;
    internal ServerScriptComposition(RuntimePackageHost host, ScriptAssemblyContext loadContext,
        string runtimeFingerprint, IReadOnlyList<ScriptPackageSnapshot> packages,
        IReadOnlyList<ISaveSetParticipant> saveParticipants)
    { Host = host; _loadContext = loadContext; RuntimeCompositionFingerprint = runtimeFingerprint;
        Packages = packages; SaveParticipants = saveParticipants; }
    public RuntimePackageHost Host { get; }
    public string RuntimeCompositionFingerprint { get; }
    public IReadOnlyList<ScriptPackageSnapshot> Packages { get; }
    /// <summary>Complete save-set ownership: ledger plus all declared durable package participants.</summary>
    public IReadOnlyList<ISaveSetParticipant> SaveParticipants { get; }
    public void Dispose()
    {
        if (_disposed) return;
        // A fault is observable; never unload native/managed owners beneath a failed Stop.
        Host.Stop();
        _loadContext.Unload();
        _disposed = true;
    }
}

/// <summary>Trusted server-author C# startup; no compiler or filesystem work occurs after Start.</summary>
public static class ServerScriptAuthoring
{
    public static ServerScriptComposition Start(string scriptsRoot, string cacheRoot,
        IEnumerable<RuntimeSavedPackage>? savedPackages = null)
    {
        IReadOnlyList<ServerScriptPackage> sources = ServerScriptSources.Discover(scriptsRoot);
        if (!RuntimeCompositionPlanner.TryResolve(sources.Select(p => p.Manifest), out var plan, out var error))
            throw new InvalidDataException($"Package composition {error?.Code}: {error?.Detail}");
        if (savedPackages is not null && !plan!.TryPlanSavedPackages(savedPackages, out _, out var migrationError))
            throw new InvalidDataException($"Saved package admission {migrationError?.Code}: {migrationError?.Detail}");
        IReadOnlyList<CompiledScriptPackage> compiled = ServerScriptCompiler.Compile(sources, plan!, cacheRoot);
        var loadContext = new ScriptAssemblyContext(compiled);
        RuntimePackageHost? startedHost = null;
        try
        {
            var bindings = new List<RuntimePackageBinding>();
            foreach (CompiledScriptPackage package in compiled)
            {
                if (package.Manifest.Entrypoint is not { } entrypoint)
                {
                    if (package.Manifest.Services.Count != 0)
                        throw new InvalidDataException($"Runtime service package {package.Manifest.Id} needs an entrypoint.");
                    continue;
                }
                Assembly assembly = loadContext.LoadSelected(package.AssemblyName);
                Type type = assembly.GetType(entrypoint, throwOnError: false, ignoreCase: false) ??
                    throw new InvalidDataException($"Entrypoint {entrypoint} not found in {package.Manifest.Id}.");
                if (!type.IsClass || type.IsAbstract || !type.IsPublic ||
                    !typeof(IRuntimePackageLifecycle).IsAssignableFrom(type) ||
                    type.GetConstructor(Type.EmptyTypes) is null)
                    throw new InvalidDataException($"Entrypoint {entrypoint} is not a public parameterless runtime lifecycle owner.");
                object instance;
                try { instance = Activator.CreateInstance(type)!; }
                catch (Exception failure)
                { throw new InvalidDataException($"Entrypoint construction failed for {package.Manifest.Id}/{entrypoint}.", failure); }
                bindings.Add(new RuntimePackageBinding(package.Manifest, (IRuntimePackageLifecycle)instance));
            }
            var text = new StringBuilder("UltraSim.RuntimeComposition/v1\n").Append(plan!.ManifestFingerprint).Append('\n');
            foreach (CompiledScriptPackage package in compiled)
                text.Append(package.Manifest.Id).Append('|').Append(package.CompileKey).Append('|')
                    .Append(package.AssemblyName).Append('|').Append(package.AssemblyHash).Append('\n');
            string fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString()))).ToLowerInvariant();
            var host = new RuntimePackageHost();
            startedHost = host;
            host.Start(plan!.OrderedPackages, bindings,
                new RuntimeCompositionBuildIdentity(fingerprint, compiled.Select(p =>
                    new RuntimePackageBuildIdentity(p.Manifest.Id, p.CompileKey, p.AssemblyHash, p.CacheHit)).ToArray()));
            var saveParticipants = new List<ISaveSetParticipant> { new RuntimePackageSchemaLedger(plan!.OrderedPackages) };
            var saveKeys = new HashSet<string>(StringComparer.Ordinal) { saveParticipants[0].Key };
            foreach (RuntimePackageManifest package in plan.OrderedPackages)
            {
                if (package.DurableSchema is not { } schema) continue;
                var handle = host.Handle<ISaveSetParticipant>(schema.ParticipantService);
                if (!host.TryGet(handle, out ISaveSetParticipant? participant) || participant is null ||
                    participant.Key != schema.ArtifactKey ||
                    !saveKeys.Add(participant.Key))
                    throw new InvalidDataException($"Durable participant admission failed for {package.Id}/{schema.ArtifactKey}.");
                saveParticipants.Add(participant);
            }
            var rows = compiled.Select(p => new ScriptPackageSnapshot(p.Manifest.Id, p.Manifest.Stage,
                p.CompileKey, p.AssemblyHash, p.CacheHit, p.Manifest.Entrypoint is not null)).ToArray();
            return new ServerScriptComposition(host, loadContext, fingerprint, rows,
                saveParticipants.AsReadOnly());
        }
        catch (Exception failure)
        {
            Exception? retirementFailure = null;
            if (startedHost?.State == RuntimeHostState.Running)
            {
                try { startedHost.Stop(); }
                catch (Exception stopFailure) { retirementFailure = stopFailure; }
            }
            loadContext.Unload();
            if (retirementFailure is not null) throw new AggregateException(failure, retirementFailure);
            throw;
        }
    }
}

/// <summary>One composition identity; admitted package images and canonical host assemblies only.</summary>
internal sealed class ScriptAssemblyContext : AssemblyLoadContext
{
    private readonly Dictionary<string, byte[]> _images;
    private readonly Dictionary<string, Assembly> _loaded = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Assembly> _hostAssemblies;
    internal ScriptAssemblyContext(IReadOnlyList<CompiledScriptPackage> packages) : base(isCollectible: true)
    {
        _images = new(StringComparer.Ordinal);
        foreach (CompiledScriptPackage package in packages)
            if (!_images.TryAdd(package.AssemblyName, package.AssemblyBytes))
                throw new InvalidDataException("Duplicate selected compiled assembly identity.");
        _hostAssemblies = new[]
        {
            typeof(RuntimePackageHost).Assembly,
            typeof(UltraSim.Framework.Persistence.SaveSetCoordinator).Assembly,
            typeof(UltraSim.Framework.ControlPlane.ControlPlaneRegistry).Assembly,
            typeof(DefinitionComposer).Assembly,
        }.ToDictionary(assembly => assembly.GetName().Name!, StringComparer.Ordinal);
    }

    internal Assembly LoadSelected(string name) => LoadFromAssemblyName(new AssemblyName(name));

    protected override Assembly? Load(AssemblyName name)
    {
        string simple = name.Name ?? throw new FileNotFoundException("Unnamed assembly reference.");
        if (_hostAssemblies.TryGetValue(simple, out Assembly? host))
        {
            if (name.Version is not null && name.Version != host.GetName().Version)
                throw new FileLoadException($"Conflicting host assembly identity: {simple}.");
            return host;
        }
        if (simple is "netstandard" or "mscorlib" or "Microsoft.CSharp" or "System" or "System.Private.CoreLib" ||
            simple.StartsWith("System.", StringComparison.Ordinal))
            return AssemblyLoadContext.Default.LoadFromAssemblyName(name);
        if (_loaded.TryGetValue(simple, out Assembly? loaded)) return loaded;
        if (!_images.TryGetValue(simple, out byte[]? image))
            throw new FileNotFoundException($"Assembly {simple} is not in the admitted composition.");
        using var stream = new MemoryStream(image, writable: false);
        Assembly assembly = LoadFromStream(stream);
        if (assembly.GetName().Name != simple)
            throw new FileLoadException($"Compiled assembly identity mismatch: {simple}.");
        _loaded.Add(simple, assembly);
        return assembly;
    }
}
