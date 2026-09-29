using UltraSim.Framework.RuntimeHost;
using UltraSim.Framework.ControlPlane;
using UltraSim.Bridges.ControlPlane.RuntimeHost;

internal static class RuntimeHostLifecycleTests
{
    internal static void Run()
    {
        var trace = new List<string>();
        var first = new SamplePackage("a.core", trace, provides: "sample.service");
        var second = new SamplePackage("b.core", trace, contributes: "sample.observers");
        var third = new SamplePackage("c.core", trace, contributes: "sample.observers");
        var host = new RuntimePackageHost();
        host.Start([third, first, second]);
        Require(host.State == RuntimeHostState.Running);
        Require(string.Join(',', trace) == "a.core.register,b.core.register,c.core.register,a.core.configure,b.core.configure,c.core.configure,a.core.initialize,b.core.initialize,c.core.initialize");
        Require(host.ContributionCount(new("sample.observers")) == 2);
        var handle = host.Handle<SamplePackage>(new("sample.service"));
        Require(host.TryGet(handle, out SamplePackage? service) && ReferenceEquals(service, first));
        Require(Task.Run(() =>
        {
            try { host.CaptureSnapshot(); return false; }
            catch (InvalidOperationException) { return true; }
        }).GetAwaiter().GetResult());
        var snapshot = host.CaptureSnapshot();
        Require(snapshot.Packages.Count == 3 && snapshot.Packages.All(p => p.State == RuntimePackageState.Running));
        for (int i = 0; i < 20_000; i++) service!.Tick(); // Warm JIT/tiering outside samples.
        var sampleBytes = new long[4];
        for (int sample = 0; sample < 4; sample++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 10_000; i++) service!.Tick();
            long after = GC.GetAllocatedBytesForCurrentThread();
            sampleBytes[sample] = after - before;
        }
        Require(sampleBytes.Contains(0) && first.TickCount == 60_000);
        Console.WriteLine($"P2_HOSTED_DIRECT_SERVICE allocations_10k=[{string.Join(',', sampleBytes)}] management_calls=0");

        // A Framework-only OOP owner can attach multiple instances of one family without an Entity.
        var extensions = new ExtensionRegistry();
        var owner = new ExtensionOwnerId("sample.owner");
        var family = new ExtensionTypeId("sample.notes");
        ExtensionFamily<Note> notes = extensions.RegisterFamily<Note>(family);
        var one = new ExtensionKey(owner, family, "one");
        var two = new ExtensionKey(owner, family, "two");
        var singleton = new ExtensionKey(ExtensionOwnerId.Global, family);
        notes.Add(one, new Note(10)); notes.Add(two, new Note(20)); notes.Add(singleton, new Note(30));
        Require(notes.Remove(one));
        Require(!notes.TryGet(one, out _));
        Require(notes.TryGet(two, out Note? retained) && retained!.Value == 20);
        Require(notes.TryGet(singleton, out retained) && retained!.Value == 30);
        try { extensions.RegisterFamily<SamplePackage>(family); throw new Exception("Expected family type collision."); }
        catch (InvalidOperationException) { }

        host.Stop(); host.Stop();
        Require(host.State == RuntimeHostState.Stopped && !host.TryGet(handle, out _));
        Require(string.Join(',', trace[^3..]) == "c.core.stop,b.core.stop,a.core.stop");
        RuntimePackageContext staleContext = first.LastContext!;
        host.Start([first, second, third]);
        Require(!staleContext.TryGet<SamplePackage>(new("sample.service"), out _));
        Require(!host.TryGet(handle, out _));
        host.Stop();

        trace.Clear();
        var good = new SamplePackage("a.core", trace);
        var bad = new SamplePackage("b.core", trace, failInitialize: true);
        var failing = new RuntimePackageHost();
        try { failing.Start([bad, good]); throw new Exception("Expected initialization failure."); }
        catch (InvalidOperationException exception) when (exception.Message == "b.core failed") { }
        Require(failing.State == RuntimeHostState.Faulted);
        Require(string.Join(',', trace[^2..]) == "b.core.stop,a.core.stop");
        Require(failing.CaptureSnapshot().Packages.All(p => p.State == RuntimePackageState.Stopped));

        trace.Clear();
        var cleanupFault = new SamplePackage("a.core", trace, failStop: true);
        var initFault = new SamplePackage("b.core", trace, failInitialize: true);
        try { new RuntimePackageHost().Start([cleanupFault, initFault]); throw new Exception("Expected aggregate."); }
        catch (AggregateException aggregate)
        { Require(aggregate.InnerExceptions.Count == 2 && aggregate.InnerExceptions[0].Message == "b.core failed" &&
            aggregate.InnerExceptions[1].Message == "a.core stop failed"); }

        foreach (string failingStage in new[] { "register", "configure" })
        {
            trace.Clear();
            var stageA = new SamplePackage("a.core", trace);
            var stageB = new SamplePackage("b.core", trace, failStage: failingStage);
            try { new RuntimePackageHost().Start([stageB, stageA]); throw new Exception("Expected stage failure."); }
            catch (InvalidOperationException exception) when (exception.Message == "b.core failed") { }
            Require(string.Join(',', trace.TakeLast(2)) == "b.core.stop,a.core.stop");
        }

        var duplicate = new RuntimePackageHost();
        try { duplicate.Start([new SamplePackage("a.core", [], provides: "same.service"),
            new SamplePackage("b.core", [], provides: "same.service")]); throw new Exception("Expected duplicate capability."); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("also provided", StringComparison.Ordinal)) { }
        try { new RuntimePackageHost().Start(Enumerable.Range(0, RuntimeCompositionPlanner.MaximumPackages + 1)
            .Select(i => (IRuntimePackage)new SamplePackage("p" + i, [])));
            throw new Exception("Expected bounded host admission."); }
        catch (InvalidOperationException exception) when (exception.Message.Contains("capacity", StringComparison.Ordinal)) { }

        // No host tick or polling API exists; direct service work above allocated zero while hosted.
    }

    internal static void ControlPlane()
    {
        var registry = new ControlPlaneRegistry();
        var principal = new ControlPrincipal("operator", ControlPrincipalKind.LocalOperator,
            [new ControlCapabilityId("framework.runtimehost.read")]);
        ulong firstGeneration = 0;
        var providerId = new ControlProviderId("framework.runtimehost");
        var host = new RuntimePackageHost();
        var package = new SamplePackage("sample.core", [], onStop: () =>
            Require(registry.Read(principal, providerId, firstGeneration,
                new DiagnosticId("state"), out _) == ControlResultCode.UnknownProvider));
        host.Start([package]);
        using var first = RuntimeHostControlPublication.Publish(host, registry);
        firstGeneration = first.ProviderGeneration;
        var viewer = new ControlPlaneViewer(registry, principal);
        var categories = viewer.Capture();
        Require(categories.Count == 1 && categories[0].Items.Count == 5);
        Require(categories[0].Items.Any(i => i.Diagnostic?.Value.AsText() == "Running"));
        host.Stop(); // Retires provider before invoking the package's Stop.
        host.Start([package]);
        using var second = RuntimeHostControlPublication.Publish(host, registry);
        Require(second.ProviderGeneration > firstGeneration);
        Require(registry.Read(principal, providerId, firstGeneration,
            new DiagnosticId("state"), out _) == ControlResultCode.StaleGeneration);
        second.Retire();
        host.Stop();
    }

    private static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("RuntimeHost lifecycle assertion failed.");
    }
    private sealed record Note(int Value);
    private sealed class SamplePackage : IRuntimePackage
    {
        private readonly List<string> _trace;
        private readonly string? _provides, _contributes;
        private readonly bool _failInitialize, _failStop;
        private readonly string? _failStage;
        private readonly Action? _onStop;
        internal SamplePackage(string id, List<string> trace, string? provides = null, string? contributes = null,
            bool failInitialize = false, bool failStop = false, Action? onStop = null, string? failStage = null)
        {
            _trace = trace; _provides = provides; _contributes = contributes;
            _failInitialize = failInitialize; _failStop = failStop; _onStop = onStop; _failStage = failStage;
            Manifest = new(new(id), new(1, 0, 0), RuntimePackageStage.Core, RuntimePackageClass.EngineCore,
                provides: provides is null ? [] : [new PackageCapabilityId(provides)],
                services: provides is null ? [] : [new RuntimeServiceId(provides)]);
        }
        public RuntimePackageManifest Manifest { get; }
        internal RuntimePackageContext? LastContext { get; private set; }
        internal int TickCount { get; private set; }
        internal void Tick() => TickCount++;
        public void Register(RuntimePackageContext context)
        {
            LastContext = context;
            _trace.Add(Manifest.Id + ".register");
            if (_provides is not null) context.Provide(new(_provides), this);
            if (_contributes is not null) context.Contribute(new(_contributes), this);
            if (_failStage == "register") throw new InvalidOperationException(Manifest.Id + " failed");
        }
        public void Configure(RuntimePackageContext context)
        {
            _trace.Add(Manifest.Id + ".configure");
            if (_failStage == "configure") throw new InvalidOperationException(Manifest.Id + " failed");
        }
        public void Initialize(RuntimePackageContext context)
        {
            _trace.Add(Manifest.Id + ".initialize");
            if (_failInitialize) throw new InvalidOperationException(Manifest.Id + " failed");
        }
        public void Stop()
        {
            _onStop?.Invoke();
            _trace.Add(Manifest.Id + ".stop");
            if (_failStop) throw new InvalidOperationException(Manifest.Id + " stop failed");
        }
    }
}
