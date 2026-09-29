using UltraSim.Framework.RuntimeHost;

var tests = new (string Name, Action Run)[]
{
    ("CanonicalIdentityAndVersion", CanonicalIdentityAndVersion),
    ("ManifestCopiesBoundedInputs", ManifestCopiesBoundedInputs),
    ("PermutationStableStagesAndFingerprint", PermutationStableStagesAndFingerprint),
    ("MissingRequiredPackageFailsBeforeStart", MissingRequiredPackageFailsBeforeStart),
    ("VersionRangeFailsBeforeStart", VersionRangeFailsBeforeStart),
    ("CapabilityAdmissionAndConflict", CapabilityAdmissionAndConflict),
    ("CycleAndBackwardStageFail", CycleAndBackwardStageFail),
    ("ContractsOnlyPlanningSurvivesOptionalCoreRemoval", ContractsOnlyPlanningSurvivesOptionalCoreRemoval),
    ("ContractsOnlyRuntimeNeedsNoEntrypointOrService", ContractsOnlyRuntimeNeedsNoEntrypointOrService),
    ("CapacityAndDuplicateSelectionFail", CapacityAndDuplicateSelectionFail),
    ("StrictFiniteManifestFormat", StrictFiniteManifestFormat),
    ("PackageAliasesAndMigrationOrderFailClosed", PackageAliasesAndMigrationOrderFailClosed),
    ("DurableSchemaDeclarationIsExact", DurableSchemaDeclarationIsExact),
    ("BuiltInLifecycleExtensionsAndRollback", RuntimeHostLifecycleTests.Run),
    ("ExplicitControlPlanePublicationAndRetirement", RuntimeHostLifecycleTests.ControlPlane)
};
foreach (var (name, run) in tests) { run(); Console.WriteLine("PASS P2 " + name); }
Console.WriteLine($"All {tests.Length} P2 manifest-planning foundation tests passed.");

static void CanonicalIdentityAndVersion()
{
    Check(!default(RuntimePackageId).IsValid && !default(PackageCapabilityId).IsValid);
    Check(new RuntimePackageId("example.contracts") != new RuntimePackageId("example.core"));
    foreach (string bad in new[] { "", "A", "a/b", "../a", "a..b", "a.", "a\\b", new string('a', 97) })
        Throws<ArgumentException>(() => new RuntimePackageId(bad));
    Equal(new RuntimePackageVersion(1, 2, 3), RuntimePackageVersion.Parse("1.2.3"));
    foreach (string bad in new[] { "1.2", "01.2.3", "-1.2.3", "1.2.3.4", "1.x.3" })
        Throws<FormatException>(() => RuntimePackageVersion.Parse(bad));
    Check(new RuntimePackageVersion(1, 3, 0).CompareTo(new RuntimePackageVersion(1, 2, 99)) > 0);
}

static void ManifestCopiesBoundedInputs()
{
    var mutable = new List<PackageCapabilityId> { new("sample.alpha") };
    var manifest = Package("sample.core", RuntimePackageStage.Core, provides: mutable);
    mutable[0] = new("sample.beta"); mutable.Add(new("sample.gamma"));
    Equal(new PackageCapabilityId("sample.alpha"), manifest.Provides[0]);
    Equal(1, manifest.Provides.Count);
    Throws<ArgumentException>(() => Package("bad", RuntimePackageStage.Core,
        provides: Enumerable.Repeat(new PackageCapabilityId("duplicate"), 2)));
    Throws<ArgumentException>(() => Package("bad", RuntimePackageStage.Core,
        provides: Enumerable.Range(0, RuntimePackageManifest.MaximumRelations + 1)
            .Select(i => new PackageCapabilityId("cap" + i))));
    Throws<ArgumentException>(() => Package("bad", RuntimePackageStage.Core,
        provides: [new("self")], requires: [new("self")]));
}

static void PermutationStableStagesAndFingerprint()
{
    var contract = Package("world.contracts", RuntimePackageStage.Contracts, provides: [new("world.ids")]);
    var core = Package("world.core", RuntimePackageStage.Core,
        dependencies: [new(new("world.contracts"), RuntimePackageVersion.Parse("1.0.0"))],
        provides: [new("world.runtime")]);
    var script = Package("shard.scripts", RuntimePackageStage.Scripts, requires: [new("world.runtime")]);
    var first = Resolve([script, core, contract]);
    var second = Resolve([contract, script, core]);
    Equal(first.ManifestFingerprint, second.ManifestFingerprint);
    Equal("world.contracts,world.core,shard.scripts", string.Join(',', first.OrderedPackages.Select(x => x.Id.Value)));
    Equal(64, first.ManifestFingerprint.Length);
    var changed = Resolve([contract, Package("world.core", RuntimePackageStage.Core,
        version: "1.0.1", dependencies: [new(new("world.contracts"), RuntimePackageVersion.Parse("1.0.0"))],
        provides: [new("world.runtime")]), script]);
    Check(changed.ManifestFingerprint != first.ManifestFingerprint);
}

static void MissingRequiredPackageFailsBeforeStart()
{
    var consumer = Package("consumer.scripts", RuntimePackageStage.Scripts,
        dependencies: [new(new("missing.contracts"), RuntimePackageVersion.Parse("1.0.0"))]);
    Error("missing-package", [consumer]);
    Error("missing-capability", [Package("consumer.scripts", RuntimePackageStage.Scripts,
        requires: [new("missing.runtime")])]);
}

static void VersionRangeFailsBeforeStart()
{
    Error("package-version", [Package("provider.contracts", RuntimePackageStage.Contracts),
        Package("consumer.scripts", RuntimePackageStage.Scripts,
            dependencies: [new(new("provider.contracts"), RuntimePackageVersion.Parse("2.0.0"))])]);
    Error("package-version", [Package("provider.contracts", RuntimePackageStage.Contracts, version: "3.0.0"),
        Package("consumer.scripts", RuntimePackageStage.Scripts,
            dependencies: [new(new("provider.contracts"), new(1, 0, 0), new(2, 0, 0))])]);
    Throws<ArgumentException>(() => new RuntimePackageDependency(new("provider.contracts"), new(2, 0, 0), new(2, 0, 0)));
    var provider = Package("provider.contracts", RuntimePackageStage.Contracts);
    string bounded = Resolve([provider, Package("consumer.scripts", RuntimePackageStage.Scripts,
        dependencies: [new(new("provider.contracts"), new(1, 0, 0), new(2, 0, 0))])]).ManifestFingerprint;
    string wider = Resolve([provider, Package("consumer.scripts", RuntimePackageStage.Scripts,
        dependencies: [new(new("provider.contracts"), new(1, 0, 0), new(3, 0, 0))])]).ManifestFingerprint;
    Check(bounded != wider);
}

static void CapabilityAdmissionAndConflict()
{
    Error("duplicate-capability", [
        Package("a.core", RuntimePackageStage.Core, provides: [new("shared.runtime")]),
        Package("b.core", RuntimePackageStage.Core, provides: [new("shared.runtime")])]);
    Error("package-conflict", [
        Package("a.core", RuntimePackageStage.Core, conflicts: [new("b.core")]),
        Package("b.core", RuntimePackageStage.Core)]);
    var optional = Resolve([Package("consumer.scripts", RuntimePackageStage.Scripts,
        optional: [new("absent.runtime")])]);
    Equal(1, optional.OrderedPackages.Count);
}

static void CycleAndBackwardStageFail()
{
    Error("dependency-cycle", [
        Package("a.core", RuntimePackageStage.Core, after: [new("b.core")]),
        Package("b.core", RuntimePackageStage.Core, after: [new("a.core")])]);
    Error("backward-stage-dependency", [
        Package("a.contracts", RuntimePackageStage.Contracts, dependencies: [new(new("b.core"), new(1, 0, 0))]),
        Package("b.core", RuntimePackageStage.Core)]);
}

static void ContractsOnlyPlanningSurvivesOptionalCoreRemoval()
{
    var contract = Package("identity.contracts", RuntimePackageStage.Contracts,
        provides: [new("identity.types")]);
    var core = Package("identity.core", RuntimePackageStage.Core,
        dependencies: [new(new("identity.contracts"), new(1, 0, 0))],
        provides: [new("identity.runtime")]);
    var optionalConsumer = Package("other.scripts", RuntimePackageStage.Scripts,
        requires: [new("identity.types")], optional: [new("identity.runtime")]);
    Equal(3, Resolve([optionalConsumer, core, contract]).OrderedPackages.Count);
    Equal(2, Resolve([optionalConsumer, contract]).OrderedPackages.Count);
    var requiredConsumer = Package("requires-core.scripts", RuntimePackageStage.Scripts,
        requires: [new("identity.runtime")]);
    Error("missing-capability", [requiredConsumer, contract]);
    // This is a manifest-planning control, not P2's required real compiled Engine/facet proof.
}

static void ContractsOnlyRuntimeNeedsNoEntrypointOrService()
{
    var contract = Package("identity.contracts", RuntimePackageStage.Contracts, provides: [new("identity.types")]);
    var consumer = Package("other.scripts", RuntimePackageStage.Scripts, requires: [new("identity.types")]);
    var host = new RuntimePackageHost();
    host.Start([contract, consumer], []);
    Check(host.State == RuntimeHostState.Running);
    Check(host.CaptureSnapshot().Packages.All(p => p.State == RuntimePackageState.Available));
    host.Stop();
    Error("missing-service", [contract, Package("needs-core.scripts", RuntimePackageStage.Scripts,
        requiresServices: [new("identity.service")])]);
    var service = Package("identity.core", RuntimePackageStage.Core, services: [new("identity.service")]);
    Throws<InvalidOperationException>(() => new RuntimePackageHost().Start([contract, service], []));
    Error("duplicate-service", [service, Package("other.core", RuntimePackageStage.Core,
        services: [new("identity.service")])]);
}

static void CapacityAndDuplicateSelectionFail()
{
    var same = Package("same.core", RuntimePackageStage.Core);
    Error("duplicate-package", [same, same]);
    Error("package-capacity", Enumerable.Range(0, RuntimeCompositionPlanner.MaximumPackages + 1)
        .Select(i => Package("p" + i, RuntimePackageStage.Core)));
}

static void StrictFiniteManifestFormat()
{
    const string valid = """
        {"format":"UltraSim.RuntimePackage/v4","id":"sample.core","version":"1.0.0",
         "stage":"Core","class":"EngineCore","provides":["sample.runtime"],
         "services":["sample.service"],"entrypoint":"Sample.CoreEntrypoint",
         "dependencies":[{"package":"sample.contracts","minimumVersion":"1.0.0","maximumVersionExclusive":"2.0.0"}]}
        """;
    RuntimePackageManifest parsed = RuntimePackageManifestJson.Parse(System.Text.Encoding.UTF8.GetBytes(valid));
    Equal(new RuntimePackageId("sample.core"), parsed.Id);
    Equal(new PackageCapabilityId("sample.runtime"), parsed.Provides[0]);
    Equal(new RuntimeServiceId("sample.service"), parsed.Services[0]);
    Equal("Sample.CoreEntrypoint", parsed.Entrypoint);
    Equal(new RuntimePackageId("sample.contracts"), parsed.Dependencies[0].Package);
    Equal(new RuntimePackageVersion(2, 0, 0), parsed.Dependencies[0].MaximumVersionExclusive);
    string aliased = valid.Replace("\"entrypoint\":\"Sample.CoreEntrypoint\"",
        "\"aliases\":[\"old.core\"],\"durableSchema\":{\"participantService\":\"sample.service\",\"artifactKey\":\"sample-store\"},\"entrypoint\":\"Sample.CoreEntrypoint\"",
        StringComparison.Ordinal);
    RuntimePackageManifest extended = RuntimePackageManifestJson.Parse(System.Text.Encoding.UTF8.GetBytes(aliased));
    Equal(new RuntimePackageId("old.core"), extended.Aliases.Single());
    Equal("sample-store", extended.DurableSchema!.Value.ArtifactKey);
    foreach (string invalid in new[]
    {
        valid.Replace("\"id\":\"sample.core\"", "\"id\":\"sample.core\",\"id\":\"other.core\""),
        valid.Replace("\"stage\":\"Core\"", "\"stage\":\"UNKNOWN\""),
        valid.Replace("\"class\":\"EngineCore\"", "\"class\":\"EngineCore\",\"unknown\":1"),
        valid.Replace("\"package\":\"sample.contracts\"", "\"package\":\"sample.contracts\",\"package\":\"other.contracts\""),
        valid.Replace("UltraSim.RuntimePackage/v4", "UltraSim.ServerModule/v2"),
        valid.Replace("UltraSim.RuntimePackage/v4", "UltraSim.RuntimePackage/v3"),
        valid.Replace("Sample.CoreEntrypoint", "Sample.Core-Entrypoint"),
        valid.Replace("\"maximumVersionExclusive\":\"2.0.0\"", "\"maximumVersionExclusive\":\"1.0.0\""),
        valid.Replace("sample.core", "../sample.core"),
        valid.Replace("\"provides\":[\"sample.runtime\"]", "\"provides\":\"sample.runtime\""),
        aliased.Replace("\"artifactKey\":\"sample-store\"", "\"artifactKey\":\"bad.name\"")
    }) Throws<FormatException>(() => RuntimePackageManifestJson.Parse(System.Text.Encoding.UTF8.GetBytes(invalid)));
    Throws<FormatException>(() => RuntimePackageManifestJson.Parse(new byte[RuntimePackageManifestJson.MaximumBytes + 1]));
}

static void PackageAliasesAndMigrationOrderFailClosed()
{
    var contracts = Package("new.contracts", RuntimePackageStage.Contracts,
        aliases: [new("old.contracts")]);
    var core = Package("new.core", RuntimePackageStage.Core,
        dependencies: [new(new("old.contracts"), new(1, 0, 0))],
        aliases: [new("old.core")]);
    var scripts = Package("new.scripts", RuntimePackageStage.Scripts,
        dependencies: [new(new("old.core"), new(1, 0, 0))]);
    RuntimeCompositionPlan plan = Resolve([scripts, core, contracts]);
    Equal(new RuntimePackageId("new.core"), plan.ResolveId(new("old.core")));
    Check(plan.TryPlanSavedPackages([new(new("old.core"), new(1, 0, 0)),
        new(new("old.contracts"), new(1, 0, 0))], out var steps, out _));
    Equal("old.contracts>new.contracts,old.core>new.core",
        string.Join(',', steps!.Select(x => x.SavedId + ">" + x.SelectedId)));
    Check(!plan.TryPlanSavedPackages([new(new("old.core"), new(1, 0, 0)),
        new(new("new.core"), new(1, 0, 0))], out _, out var ambiguity) &&
        ambiguity!.Code == "saved-package-ambiguous");
    Check(!plan.TryPlanSavedPackages([new(new("old.core"), new(2, 0, 0))], out _, out var version) &&
        version!.Code == "saved-package-version");
    Error("alias-selected-conflict", [contracts,
        Package("old.contracts", RuntimePackageStage.Contracts)]);
    Error("alias-ambiguous", [contracts,
        Package("other.contracts", RuntimePackageStage.Contracts, aliases: [new("old.contracts")])]);
    // Alias chains/cycles cannot be selected: an alias may only terminate at a selected ID.
    Error("alias-selected-conflict", [Package("a.core", RuntimePackageStage.Core, aliases: [new("b.core")]),
        Package("b.core", RuntimePackageStage.Core, aliases: [new("a.core")])]);
    Check(plan.ManifestFingerprint != Resolve([Package("new.contracts", RuntimePackageStage.Contracts),
        Package("new.core", RuntimePackageStage.Core), Package("new.scripts", RuntimePackageStage.Scripts)]).ManifestFingerprint);
}

static void DurableSchemaDeclarationIsExact()
{
    var schema = new RuntimePackageDurableSchema(new("sample.store"), "sample-store");
    var package = Package("sample.core", RuntimePackageStage.Core,
        services: [new("sample.store")], durableSchema: schema);
    Check(package.DurableSchema == schema);
    Throws<ArgumentException>(() => Package("sample.core", RuntimePackageStage.Core, durableSchema: schema));
    Throws<ArgumentException>(() => Package("sample.core", RuntimePackageStage.Core,
        services: [new("sample.store")], durableSchema: new(new("sample.store"), "bad.name")));
    Check(Resolve([package]).ManifestFingerprint != Resolve([Package("sample.core", RuntimePackageStage.Core,
        services: [new("sample.store")])]).ManifestFingerprint);
}

static RuntimePackageManifest Package(string id, RuntimePackageStage stage, string version = "1.0.0",
    IEnumerable<RuntimePackageDependency>? dependencies = null,
    IEnumerable<PackageCapabilityId>? provides = null,
    IEnumerable<PackageCapabilityId>? requires = null,
    IEnumerable<PackageCapabilityId>? optional = null,
    IEnumerable<RuntimePackageId>? before = null,
    IEnumerable<RuntimePackageId>? after = null,
    IEnumerable<RuntimePackageId>? conflicts = null,
    IEnumerable<RuntimeServiceId>? services = null,
    IEnumerable<RuntimeServiceId>? requiresServices = null,
    IEnumerable<RuntimePackageId>? aliases = null,
    RuntimePackageDurableSchema? durableSchema = null)
    => new(new(id), RuntimePackageVersion.Parse(version), stage, RuntimePackageClass.EngineCore,
        dependencies, provides, requires, optional, before, after, conflicts,
        services, requiresServices, aliases: aliases, durableSchema: durableSchema);

static RuntimeCompositionPlan Resolve(IEnumerable<RuntimePackageManifest> manifests)
{
    Check(RuntimeCompositionPlanner.TryResolve(manifests, out var plan, out var error), error?.Detail);
    return plan!;
}

static void Error(string code, IEnumerable<RuntimePackageManifest> manifests)
{
    Check(!RuntimeCompositionPlanner.TryResolve(manifests, out var plan, out var error));
    Check(plan is null);
    Equal(code, error?.Code);
}

static void Check(bool condition, string? detail = null)
{
    if (!condition) throw new InvalidOperationException("Assertion failed" + (detail is null ? "." : ": " + detail));
}
static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected {expected}; received {actual}.");
}
static void Throws<T>(Action action) where T : Exception
{
    try { action(); }
    catch (T) { return; }
    throw new InvalidOperationException($"Expected {typeof(T).Name}.");
}
