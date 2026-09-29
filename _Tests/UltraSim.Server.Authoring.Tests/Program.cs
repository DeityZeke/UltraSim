using System.Reflection;
using UltraSim.Bridges.ControlPlane.RuntimeHost;
using UltraSim.Framework.ControlPlane;
using UltraSim.Framework.Persistence;
using UltraSim.Framework.RuntimeHost;
using UltraSim.Server.Authoring;

string repository = FindRepository();
string root = Path.Combine(Path.GetTempPath(), "ultrasim-p2-scripts-" + Guid.NewGuid().ToString("N"));
try
{
    string scripts = Path.Combine(root, "Scripts");
    string cache = Path.Combine(root, "cache");
    CopySample(Path.Combine(repository, "Server", "Scripts.Sample"), scripts);
    var discovered = ServerScriptSources.Discover(scripts);
    Check(discovered.Count == 3 && discovered[0].Manifest.Id.Value == "sample.contracts");
    Console.WriteLine("PASS P2 deterministic bounded source discovery");

    string nestedScripts = Path.Combine(root, "nested", "Scripts");
    CopySample(Path.Combine(repository, "Server", "Scripts.Sample"), nestedScripts);
    string nestedFolder = Path.Combine(nestedScripts, "sample.scripts", "Items");
    Directory.CreateDirectory(nestedFolder);
    File.Move(Path.Combine(nestedScripts, "sample.scripts", "Script.cs"),
        Path.Combine(nestedFolder, "Weapons.cs"));
    Check(ServerScriptSources.Discover(nestedScripts).Single(p => p.Manifest.Id.Value == "sample.scripts")
        .Sources.Single().RelativePath == "Items/Weapons.cs");
    string nestedScriptKey;
    using (ServerScriptComposition nested = ServerScriptAuthoring.Start(nestedScripts, Path.Combine(root, "nested-cache")))
    {
        Check(nested.Host.State == RuntimeHostState.Running);
        nestedScriptKey = nested.Packages.Single(p => p.Id.Value == "sample.scripts").CompileKey;
    }
    string deep = Path.Combine(nestedFolder, "a", "b", "c", "d");
    Directory.CreateDirectory(deep);
    Throws<InvalidDataException>(() => ServerScriptSources.Discover(nestedScripts), "depth exceeded");
    Directory.Delete(Path.Combine(nestedFolder, "a"), recursive: true);
    Console.WriteLine("PASS P2 bounded nested canonical C# source paths compile; excess depth rejects");

    string saveRoot = Path.Combine(root, "save");
    string fingerprint;
    string contractsKey;
    string coreKey;
    string registryIdentity;
    string contractsAssemblyIdentity;
    using (ServerScriptComposition first = ServerScriptAuthoring.Start(scripts, cache))
    {
        Check(first.Host.State == RuntimeHostState.Running && first.Packages.All(p => !p.CacheHit));
        Check(first.Host.CaptureSnapshot().Packages[0].State == RuntimePackageState.Available);
        fingerprint = first.RuntimeCompositionFingerprint;
        contractsKey = first.Packages.Single(p => p.Id.Value == "sample.contracts").CompileKey;
        coreKey = first.Packages.Single(p => p.Id.Value == "sample.core").CompileKey;
        Check(nestedScriptKey != first.Packages.Single(p => p.Id.Value == "sample.scripts").CompileKey);
        object firstConsumer = Service<object>(first.Host, "sample.consumer");
        contractsAssemblyIdentity = (string)firstConsumer.GetType().GetProperty("ContractsAssembly")!.GetValue(firstConsumer)!;
        ISaveSetParticipant store = Service<ISaveSetParticipant>(first.Host, "sample.store");
        ControlPlaneRegistry registry = Service<ControlPlaneRegistry>(first.Host, "sample.registry");
        registryIdentity = registry.GetType().Assembly.FullName!;
        Check(ReferenceEquals(registry.GetType().Assembly, typeof(ControlPlaneRegistry).Assembly));
        var principal = new ControlPrincipal("operator", ControlPrincipalKind.LocalOperator,
            [new ControlCapabilityId("sample.read"), new ControlCapabilityId("framework.runtimehost.read")]);
        using var hostPublication = RuntimeHostControlPublication.Publish(first.Host, registry);
        var hostProvider = registry.Discover(principal).Single(p => p.Definition.Id.Value == "framework.runtimehost");
        Check(registry.Read(principal, new("framework.runtimehost"), hostProvider.Generation,
            new DiagnosticId("runtime-fingerprint"), out ControlDiagnosticSnapshot? compositionDiagnostic) ==
            ControlResultCode.Accepted && compositionDiagnostic!.Value.AsText() == fingerprint);
        Check(registry.Read(principal, new("framework.runtimehost"), hostProvider.Generation,
            new ControlCollectionId("packages"), out ControlCollectionSnapshot? packageRows) ==
            ControlResultCode.Accepted && packageRows!.Rows.Count == 3 &&
            packageRows.Rows.Single(row => row.Id.Value == "sample.core").Values[0].AsText() == "1.0.0" &&
            packageRows.Rows.Single(row => row.Id.Value == "sample.core").Values[1].AsText() == "Core");
        Check(first.Host.CaptureSnapshot().Packages.All(p => p.CompileKey is { Length: 64 }));
        var provider = registry.Discover(principal).Single(p => p.Definition.Id.Value == "sample.core");
        Check(registry.Read(principal, new("sample.core"), provider.Generation,
            new DiagnosticId("status"), out ControlDiagnosticSnapshot? status) == ControlResultCode.Accepted &&
            status!.Value.AsText() == "Running");
        var coordinator = new SaveSetCoordinator(saveRoot);
        coordinator.Save(first.SaveParticipants, new(1, 100), action => action());
        SetValue(store, 99);
        using (PreparedLoadGeneration prepared = coordinator.PrepareLoad(first.SaveParticipants, EmptyResolver.Instance))
            prepared.Commit(parts => store.GetType().GetMethod("CommitPrepared")!.Invoke(null,
                [parts.Single(p => p.Key == store.Key)]));
        Check(GetValue(store) == 17);
        SetValue(store, -1);
        coordinator.Save(first.SaveParticipants, new(2, 200), action => action());
        SetValue(store, 77);
        try { using var rejected = coordinator.PrepareLoad(first.SaveParticipants, EmptyResolver.Instance); throw new Exception("Expected validation failure."); }
        catch (InvalidDataException) { }
        Check(GetValue(store) == 77);
        using (PreparedLoadGeneration recovered = coordinator.PrepareLoad(
            first.SaveParticipants, EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete))
            Check(recovered.Sequence == 1);
        long[] allocations = new long[3];
        int sum = 0;
        for (int i = 0; i < 20_000; i++) sum += store.Key.Length;
        for (int sample = 0; sample < allocations.Length; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++) sum += store.Key.Length;
            allocations[sample] = GC.GetAllocatedBytesForCurrentThread() - before;
        }
        Check(sum > 0 && allocations.Contains(0));
        Console.WriteLine($"P2_COMPILED_DIRECT_SERVICE allocations_10k=[{string.Join(',', allocations)}]");
        first.Host.Stop();
        Check(registry.Read(principal, new("sample.core"), provider.Generation,
            new DiagnosticId("status"), out _) == ControlResultCode.UnknownProvider);
    }
    Console.WriteLine("PASS P2 compiled Contracts/Core/Scripts, host identity, Control Plane retirement, Framework persistence");

    using (ServerScriptComposition warm = ServerScriptAuthoring.Start(scripts, cache))
    {
        Check(warm.Packages.All(p => p.CacheHit));
        Check(warm.RuntimeCompositionFingerprint == fingerprint);
        Check(Service<ControlPlaneRegistry>(warm.Host, "sample.registry").GetType().Assembly.FullName == registryIdentity);
    }
    Console.WriteLine("PASS P2 exact warm cache reuse and stable runtime fingerprint");
    string relocatedScripts = Path.Combine(root, "relocated", "Scripts");
    CopySample(Path.Combine(repository, "Server", "Scripts.Sample"), relocatedScripts);
    using (ServerScriptComposition relocated = ServerScriptAuthoring.Start(relocatedScripts, cache))
        Check(relocated.Packages.All(p => p.CacheHit) && relocated.RuntimeCompositionFingerprint == fingerprint);

    string versionScripts = Path.Combine(root, "version", "Scripts");
    CopySample(Path.Combine(repository, "Server", "Scripts.Sample"), versionScripts);
    string versionManifest = Path.Combine(versionScripts, "sample.core", "manifest.json");
    File.WriteAllText(versionManifest, File.ReadAllText(versionManifest).Replace(
        "\"version\":\"1.0.0\"", "\"version\":\"1.0.1\"", StringComparison.Ordinal));
    using (ServerScriptComposition versioned = ServerScriptAuthoring.Start(versionScripts, Path.Combine(root, "version-cache")))
        Throws<IOException>(() => new SaveSetCoordinator(saveRoot).PrepareLoad(versioned.SaveParticipants,
            EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete), "compatible save-set");
    string aliasScripts = Path.Combine(root, "alias", "Scripts");
    CopySample(Path.Combine(repository, "Server", "Scripts.Sample"), aliasScripts);
    string oldDirectory = Path.Combine(aliasScripts, "sample.core");
    string newDirectory = Path.Combine(aliasScripts, "renamed.core");
    Directory.Move(oldDirectory, newDirectory);
    string aliasManifest = Path.Combine(newDirectory, "manifest.json");
    File.WriteAllText(aliasManifest, File.ReadAllText(aliasManifest).Replace(
        "\"id\":\"sample.core\"", "\"id\":\"renamed.core\",\"aliases\":[\"sample.core\"]",
        StringComparison.Ordinal));
    string aliasConsumerManifest = Path.Combine(aliasScripts, "sample.scripts", "manifest.json");
    File.WriteAllText(aliasConsumerManifest, File.ReadAllText(aliasConsumerManifest).Replace(
        "\"maximumVersionExclusive\":\"2.0.0\"}],\"requires\"",
        "\"maximumVersionExclusive\":\"2.0.0\"},{\"package\":\"sample.core\",\"minimumVersion\":\"1.0.0\"}],\"requires\"",
        StringComparison.Ordinal));
    Check(File.ReadAllText(aliasConsumerManifest).Contains("\"package\":\"sample.core\"", StringComparison.Ordinal));
    using (ServerScriptComposition renamed = ServerScriptAuthoring.Start(aliasScripts, Path.Combine(root, "alias-cache"),
        [new(new("sample.core"), new(1, 0, 0))]))
        Throws<IOException>(() => new SaveSetCoordinator(saveRoot).PrepareLoad(renamed.SaveParticipants,
            EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete), "compatible save-set");
    string conflictCache = Path.Combine(root, "alias-conflict-cache");
    Throws<InvalidDataException>(() => ServerScriptAuthoring.Start(aliasScripts, conflictCache,
        [new(new("sample.core"), new(1, 0, 0)), new(new("renamed.core"), new(1, 0, 0))]),
        "saved-package-ambiguous");
    Check(!Directory.Exists(conflictCache));
    Console.WriteLine("PASS P2 durable schema version/renamed-package removal cannot silently load prior save");

    string coreSource = Path.Combine(scripts, "sample.core", "Core.cs");
    File.AppendAllText(coreSource, "\n// source edit changes compiled identity\n");
    using (ServerScriptComposition edited = ServerScriptAuthoring.Start(scripts, cache))
    {
        Check(edited.Packages.Single(p => p.Id.Value == "sample.contracts").CompileKey == contractsKey);
        Check(edited.Packages.Single(p => p.Id.Value == "sample.core").CompileKey != coreKey);
        Check(edited.RuntimeCompositionFingerprint != fingerprint);
    }
    Console.WriteLine("PASS P2 source edit invalidates Core/dependents but not Contracts");

    string contractAssembly = Path.Combine(cache, contractsKey, "assembly.dll");
    File.WriteAllBytes(contractAssembly, [1, 2, 3]);
    using (ServerScriptComposition repaired = ServerScriptAuthoring.Start(scripts, cache))
        Check(!repaired.Packages.Single(p => p.Id.Value == "sample.contracts").CacheHit);
    Console.WriteLine("PASS P2 corrupt cache image rejected and recompiled");

    Directory.Delete(Path.Combine(scripts, "sample.core"), recursive: true);
    using (ServerScriptComposition noCore = ServerScriptAuthoring.Start(scripts, cache))
    {
        Check(noCore.Packages.Single(p => p.Id.Value == "sample.contracts").CompileKey == contractsKey);
        Check(noCore.Host.State == RuntimeHostState.Running);
        Check(!noCore.Host.TryGet(new RuntimeServiceHandle<object>(new("sample.greeting"), noCore.Host.Generation), out _));
        object consumer = Service<object>(noCore.Host, "sample.consumer");
        Check((bool)consumer.GetType().GetProperty("HasCore")!.GetValue(consumer)! == false);
        Check((string)consumer.GetType().GetProperty("ContractsType")!.GetValue(consumer)! == "Sample.Contracts.IGreeting");
        Check((string)consumer.GetType().GetProperty("ContractsAssembly")!.GetValue(consumer)! == contractsAssemblyIdentity);
        Throws<IOException>(() => new SaveSetCoordinator(saveRoot).PrepareLoad(noCore.SaveParticipants,
            EmptyResolver.Instance, SaveSetRecoveryMode.CurrentThenNewestComplete), "compatible save-set");
    }
    string requiredManifest = Path.Combine(scripts, "sample.scripts", "manifest.json");
    string optionalText = File.ReadAllText(requiredManifest);
    File.WriteAllText(requiredManifest, optionalText.Replace("\"optionalServices\":[\"sample.greeting\"]",
        "\"requiresServices\":[\"sample.greeting\"]", StringComparison.Ordinal));
    Throws<InvalidDataException>(() => ServerScriptAuthoring.Start(scripts, cache), "missing-service");
    File.WriteAllText(requiredManifest, optionalText);
    Console.WriteLine("PASS P2 real compiled Contracts-only survivability and required-Core rejection");

    File.AppendAllText(Path.Combine(scripts, "sample.scripts", "Script.cs"), "\n#error TEST_COMPILER_FAILURE\n");
    try { ServerScriptAuthoring.Start(scripts, cache); throw new Exception("Expected compiler failure."); }
    catch (ServerScriptCompilationException error)
    { Check(error.Package.Value == "sample.scripts" && error.Path == "sample.scripts/Script.cs" &&
        error.Line > 0 && error.Column > 0 && error.Code == "CS1029"); }
    Console.WriteLine("PASS P2 bounded package/source/line compiler diagnostic");

    string unexpected = Path.Combine(scripts, "sample.scripts", "bad.txt");
    File.WriteAllText(unexpected, "not a source");
    Throws<InvalidDataException>(() => ServerScriptSources.Discover(scripts), "Unexpected source input");
    File.Delete(unexpected);
    string conflicting = Path.Combine(scripts, "sample.scripts", "script.cs");
    File.WriteAllText(conflicting, "// case conflict");
    Throws<InvalidDataException>(() => ServerScriptSources.Discover(scripts), "Case-conflicting");
    File.Delete(conflicting);
    string oversized = Path.Combine(scripts, "sample.scripts", "oversized.cs");
    File.WriteAllBytes(oversized, new byte[ServerScriptSources.MaximumSourceBytesPerPackage + 1]);
    Throws<InvalidDataException>(() => ServerScriptSources.Discover(scripts), "byte bound");
    File.Delete(oversized);
    if (!OperatingSystem.IsWindows())
    {
        string link = Path.Combine(scripts, "sample.scripts", "escaped.cs");
        File.CreateSymbolicLink(link, Path.Combine(repository, "Server", "Scripts.Sample", "sample.contracts", "Contracts.cs"));
        Throws<InvalidDataException>(() => ServerScriptSources.Discover(scripts), "Reparse/symlink");
        File.Delete(link);
    }
    Console.WriteLine("PASS P2 source admission rejects unexpected, case-conflicting, oversized and symlink inputs");

    foreach ((string package, string file) in new[]
    {
        ("sample.contracts", "Contracts.cs"), ("sample.core", "Core.cs"), ("sample.scripts", "Script.cs")
    })
    {
        string caseScripts = Path.Combine(root, "compile-" + package, "Scripts");
        CopySample(Path.Combine(repository, "Server", "Scripts.Sample"), caseScripts);
        File.AppendAllText(Path.Combine(caseScripts, package, file), "\n#error INTENTIONAL_STAGE_FAILURE\n");
        try { ServerScriptAuthoring.Start(caseScripts, Path.Combine(root, "compile-cache-" + package));
            throw new Exception("Expected stage compilation failure."); }
        catch (ServerScriptCompilationException error)
        { Check(error.Package.Value == package && error.Path == package + "/" + file && error.Code == "CS1029"); }
    }
    Console.WriteLine("PASS P2 Contracts/Core/Scripts compilation failures stay pre-authority");

    string lifecycleScripts = Path.Combine(root, "lifecycle", "Scripts");
    string lifecycleCache = Path.Combine(root, "lifecycle-cache");
    CopySample(Path.Combine(repository, "Server", "Scripts.Sample"), lifecycleScripts);
    string lifecycleCore = Path.Combine(lifecycleScripts, "sample.core", "Core.cs");
    string originalCore = File.ReadAllText(lifecycleCore);
    using (ServerScriptComposition active = ServerScriptAuthoring.Start(lifecycleScripts, lifecycleCache))
    {
        foreach ((string label, string before, string after) in new[]
        {
            ("construction", "public sealed class CoreEntry : IRuntimePackageLifecycle\n{",
                "public sealed class CoreEntry : IRuntimePackageLifecycle\n{\n    public CoreEntry() { throw new InvalidOperationException(\"construction\"); }"),
            ("register", "public void Register(RuntimePackageContext context)\n    {",
                "public void Register(RuntimePackageContext context)\n    {\n        throw new InvalidOperationException(\"register\");"),
            ("configure", "public void Configure(RuntimePackageContext context) { }",
                "public void Configure(RuntimePackageContext context) { throw new InvalidOperationException(\"configure\"); }"),
            ("initialize", "context.StagePublication(new SamplePublication(_registry));",
                "throw new InvalidOperationException(\"initialize\");"),
            ("publication", "public void Publish()\n    {",
                "public void Publish()\n    {\n        throw new InvalidOperationException(\"publication\");"),
            ("persistence-registration", "context.Provide(new RuntimeServiceId(\"sample.store\"), _store);",
                "throw new InvalidOperationException(\"persistence-registration\");"),
        })
        {
            Check(originalCore.Contains(before, StringComparison.Ordinal));
            File.WriteAllText(lifecycleCore, originalCore.Replace(before, after, StringComparison.Ordinal));
            Throws<Exception>(() => ServerScriptAuthoring.Start(lifecycleScripts, lifecycleCache), label);
            Check(active.Host.State == RuntimeHostState.Running &&
                Service<ISaveSetParticipant>(active.Host, "sample.store").Key == "sample-store");
        }
        File.WriteAllText(lifecycleCore, originalCore);
        string manifestPath = Path.Combine(lifecycleScripts, "sample.core", "manifest.json");
        string originalManifest = File.ReadAllText(manifestPath);
        File.WriteAllText(manifestPath, originalManifest.Replace("Sample.CoreEntry", "Sample.Greeting", StringComparison.Ordinal));
        Throws<InvalidDataException>(() => ServerScriptAuthoring.Start(lifecycleScripts, lifecycleCache), "Entrypoint");
        Check(active.Host.State == RuntimeHostState.Running);
        File.WriteAllText(manifestPath, originalManifest.Replace("\"artifactKey\":\"sample-store\"",
            "\"artifactKey\":\"wrong-store\"", StringComparison.Ordinal));
        Throws<InvalidDataException>(() => ServerScriptAuthoring.Start(lifecycleScripts, lifecycleCache),
            "Durable participant admission");
        Check(active.Host.State == RuntimeHostState.Running);
        File.WriteAllText(manifestPath, originalManifest);
    }
    Console.WriteLine("PASS P2 construction/register/configure/initialize/publication/persistence admission rollback preserves old authority");
}
finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }

Console.WriteLine("All P2 server-authoring controls passed.");

static T Service<T>(RuntimePackageHost host, string id) where T : class
{
    var handle = host.Handle<T>(new RuntimeServiceId(id));
    if (!host.TryGet(handle, out T? value)) throw new InvalidOperationException("Missing service " + id);
    return value!;
}
static int GetValue(ISaveSetParticipant participant) =>
    (int)participant.GetType().GetProperty("Value")!.GetValue(participant)!;
static void SetValue(ISaveSetParticipant participant, int value) =>
    participant.GetType().GetProperty("Value")!.SetValue(participant, value);
static void Check(bool condition)
{ if (!condition) throw new InvalidOperationException("P2 authoring assertion failed."); }
static void Throws<T>(Action action, string message) where T : Exception
{
    try { action(); }
    catch (T error) when (error.Message.Contains(message, StringComparison.Ordinal)) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name} containing {message}.");
}
static string FindRepository()
{
    for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        if (Directory.Exists(Path.Combine(directory.FullName, "Server", "Scripts.Sample"))) return directory.FullName;
    throw new DirectoryNotFoundException("Server/Scripts.Sample not found.");
}
static void CopySample(string source, string target)
{
    Directory.CreateDirectory(target);
    foreach (string directory in Directory.GetDirectories(source))
    {
        string child = Path.Combine(target, Path.GetFileName(directory));
        Directory.CreateDirectory(child);
        foreach (string file in Directory.GetFiles(directory))
            File.Copy(file, Path.Combine(child, Path.GetFileName(file)));
    }
}
sealed class EmptyResolver : IExternalPersistentReferenceResolver
{
    internal static readonly EmptyResolver Instance = new();
    public bool TryResolve(PersistentReferenceKey key, out object? value) { value = null; return false; }
}
