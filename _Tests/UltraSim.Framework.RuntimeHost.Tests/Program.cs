using UltraSim.Framework.RuntimeHost;

var tests = new (string Name, Action Run)[]
{
    ("CanonicalIdentityAndVersion", CanonicalIdentityAndVersion),
    ("ManifestCopiesBoundedInputs", ManifestCopiesBoundedInputs),
    ("PermutationStableStagesAndFingerprint", PermutationStableStagesAndFingerprint),
    ("MissingRequiredPackageFailsBeforeStart", MissingRequiredPackageFailsBeforeStart),
    ("MinimumVersionFailsBeforeStart", MinimumVersionFailsBeforeStart),
    ("CapabilityAdmissionAndConflict", CapabilityAdmissionAndConflict),
    ("CycleAndBackwardStageFail", CycleAndBackwardStageFail),
    ("ContractsOnlyPlanningSurvivesOptionalCoreRemoval", ContractsOnlyPlanningSurvivesOptionalCoreRemoval),
    ("CapacityAndDuplicateSelectionFail", CapacityAndDuplicateSelectionFail),
    ("StrictFiniteManifestFormat", StrictFiniteManifestFormat)
};
foreach (var (name, run) in tests) { run(); Console.WriteLine("PASS P2 " + name); }
Console.WriteLine($"All {tests.Length} P2 manifest-planning foundation tests passed.");

static void CanonicalIdentityAndVersion()
{
    Check(!default(ModulePackageId).IsValid && !default(ModuleCapabilityId).IsValid);
    Check(new ModulePackageId("example.contracts") != new ModulePackageId("example.core"));
    foreach (string bad in new[] { "", "A", "a/b", "../a", "a..b", "a.", "a\\b", new string('a', 97) })
        Throws<ArgumentException>(() => new ModulePackageId(bad));
    Equal(new ModuleVersion(1, 2, 3), ModuleVersion.Parse("1.2.3"));
    foreach (string bad in new[] { "1.2", "01.2.3", "-1.2.3", "1.2.3.4", "1.x.3" })
        Throws<FormatException>(() => ModuleVersion.Parse(bad));
    Check(new ModuleVersion(1, 3, 0).CompareTo(new ModuleVersion(1, 2, 99)) > 0);
}

static void ManifestCopiesBoundedInputs()
{
    var mutable = new List<ModuleCapabilityId> { new("sample.alpha") };
    var manifest = Package("sample.core", ModulePackageStage.Core, provides: mutable);
    mutable[0] = new("sample.beta"); mutable.Add(new("sample.gamma"));
    Equal(new ModuleCapabilityId("sample.alpha"), manifest.Provides[0]);
    Equal(1, manifest.Provides.Count);
    Throws<ArgumentException>(() => Package("bad", ModulePackageStage.Core,
        provides: Enumerable.Repeat(new ModuleCapabilityId("duplicate"), 2)));
    Throws<ArgumentException>(() => Package("bad", ModulePackageStage.Core,
        provides: Enumerable.Range(0, ModulePackageManifest.MaximumRelations + 1)
            .Select(i => new ModuleCapabilityId("cap" + i))));
    Throws<ArgumentException>(() => Package("bad", ModulePackageStage.Core,
        provides: [new("self")], requires: [new("self")]));
}

static void PermutationStableStagesAndFingerprint()
{
    var contract = Package("world.contracts", ModulePackageStage.Contracts, provides: [new("world.ids")]);
    var core = Package("world.core", ModulePackageStage.Core,
        dependencies: [new(new("world.contracts"), ModuleVersion.Parse("1.0.0"))],
        provides: [new("world.runtime")]);
    var script = Package("shard.scripts", ModulePackageStage.Scripts, requires: [new("world.runtime")]);
    var first = Resolve([script, core, contract]);
    var second = Resolve([contract, script, core]);
    Equal(first.ManifestFingerprint, second.ManifestFingerprint);
    Equal("world.contracts,world.core,shard.scripts", string.Join(',', first.OrderedPackages.Select(x => x.Id.Value)));
    Equal(64, first.ManifestFingerprint.Length);
    var changed = Resolve([contract, Package("world.core", ModulePackageStage.Core,
        version: "1.0.1", dependencies: [new(new("world.contracts"), ModuleVersion.Parse("1.0.0"))],
        provides: [new("world.runtime")]), script]);
    Check(changed.ManifestFingerprint != first.ManifestFingerprint);
}

static void MissingRequiredPackageFailsBeforeStart()
{
    var consumer = Package("consumer.scripts", ModulePackageStage.Scripts,
        dependencies: [new(new("missing.contracts"), ModuleVersion.Parse("1.0.0"))]);
    Error("missing-package", [consumer]);
    Error("missing-capability", [Package("consumer.scripts", ModulePackageStage.Scripts,
        requires: [new("missing.runtime")])]);
}

static void MinimumVersionFailsBeforeStart()
{
    Error("package-version", [Package("provider.contracts", ModulePackageStage.Contracts),
        Package("consumer.scripts", ModulePackageStage.Scripts,
            dependencies: [new(new("provider.contracts"), ModuleVersion.Parse("2.0.0"))])]);
}

static void CapabilityAdmissionAndConflict()
{
    Error("duplicate-capability", [
        Package("a.core", ModulePackageStage.Core, provides: [new("shared.runtime")]),
        Package("b.core", ModulePackageStage.Core, provides: [new("shared.runtime")])]);
    Error("package-conflict", [
        Package("a.core", ModulePackageStage.Core, conflicts: [new("b.core")]),
        Package("b.core", ModulePackageStage.Core)]);
    var optional = Resolve([Package("consumer.scripts", ModulePackageStage.Scripts,
        optional: [new("absent.runtime")])]);
    Equal(1, optional.OrderedPackages.Count);
}

static void CycleAndBackwardStageFail()
{
    Error("dependency-cycle", [
        Package("a.core", ModulePackageStage.Core, after: [new("b.core")]),
        Package("b.core", ModulePackageStage.Core, after: [new("a.core")])]);
    Error("backward-stage-dependency", [
        Package("a.contracts", ModulePackageStage.Contracts, dependencies: [new(new("b.core"), new(1, 0, 0))]),
        Package("b.core", ModulePackageStage.Core)]);
}

static void ContractsOnlyPlanningSurvivesOptionalCoreRemoval()
{
    var contract = Package("identity.contracts", ModulePackageStage.Contracts,
        provides: [new("identity.types")]);
    var core = Package("identity.core", ModulePackageStage.Core,
        dependencies: [new(new("identity.contracts"), new(1, 0, 0))],
        provides: [new("identity.runtime")]);
    var optionalConsumer = Package("other.scripts", ModulePackageStage.Scripts,
        requires: [new("identity.types")], optional: [new("identity.runtime")]);
    Equal(3, Resolve([optionalConsumer, core, contract]).OrderedPackages.Count);
    Equal(2, Resolve([optionalConsumer, contract]).OrderedPackages.Count);
    var requiredConsumer = Package("requires-core.scripts", ModulePackageStage.Scripts,
        requires: [new("identity.runtime")]);
    Error("missing-capability", [requiredConsumer, contract]);
    // This is a manifest-planning control, not P2's required real compiled Engine/facet proof.
}

static void CapacityAndDuplicateSelectionFail()
{
    var same = Package("same.core", ModulePackageStage.Core);
    Error("duplicate-package", [same, same]);
    Error("package-capacity", Enumerable.Range(0, ModuleCompositionPlanner.MaximumPackages + 1)
        .Select(i => Package("p" + i, ModulePackageStage.Core)));
}

static void StrictFiniteManifestFormat()
{
    const string valid = """
        {"format":"UltraSim.ServerModule/v1","id":"sample.core","version":"1.0.0",
         "stage":"Core","class":"Engine","provides":["sample.runtime"],
         "dependencies":[{"package":"sample.contracts","minimumVersion":"1.0.0"}]}
        """;
    ModulePackageManifest parsed = ModuleManifestJson.Parse(System.Text.Encoding.UTF8.GetBytes(valid));
    Equal(new ModulePackageId("sample.core"), parsed.Id);
    Equal(new ModuleCapabilityId("sample.runtime"), parsed.Provides[0]);
    Equal(new ModulePackageId("sample.contracts"), parsed.Dependencies[0].Package);
    foreach (string invalid in new[]
    {
        valid.Replace("\"id\":\"sample.core\"", "\"id\":\"sample.core\",\"id\":\"other.core\""),
        valid.Replace("\"stage\":\"Core\"", "\"stage\":\"UNKNOWN\""),
        valid.Replace("\"class\":\"Engine\"", "\"class\":\"Engine\",\"unknown\":1"),
        valid.Replace("\"package\":\"sample.contracts\"", "\"package\":\"sample.contracts\",\"package\":\"other.contracts\""),
        valid.Replace("UltraSim.ServerModule/v1", "UltraSim.ServerModule/v2"),
        valid.Replace("sample.core", "../sample.core"),
        valid.Replace("\"provides\":[\"sample.runtime\"]", "\"provides\":\"sample.runtime\"")
    }) Throws<FormatException>(() => ModuleManifestJson.Parse(System.Text.Encoding.UTF8.GetBytes(invalid)));
    Throws<FormatException>(() => ModuleManifestJson.Parse(new byte[ModuleManifestJson.MaximumBytes + 1]));
}

static ModulePackageManifest Package(string id, ModulePackageStage stage, string version = "1.0.0",
    IEnumerable<ModuleDependency>? dependencies = null,
    IEnumerable<ModuleCapabilityId>? provides = null,
    IEnumerable<ModuleCapabilityId>? requires = null,
    IEnumerable<ModuleCapabilityId>? optional = null,
    IEnumerable<ModulePackageId>? before = null,
    IEnumerable<ModulePackageId>? after = null,
    IEnumerable<ModulePackageId>? conflicts = null)
    => new(new(id), ModuleVersion.Parse(version), stage, ModulePackageClass.Engine,
        dependencies, provides, requires, optional, before, after, conflicts);

static ModuleCompositionPlan Resolve(IEnumerable<ModulePackageManifest> manifests)
{
    Check(ModuleCompositionPlanner.TryResolve(manifests, out var plan, out var error), error?.Detail);
    return plan!;
}

static void Error(string code, IEnumerable<ModulePackageManifest> manifests)
{
    Check(!ModuleCompositionPlanner.TryResolve(manifests, out var plan, out var error));
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
