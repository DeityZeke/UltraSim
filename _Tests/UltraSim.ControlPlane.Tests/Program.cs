using System.Diagnostics;
using UltraSim.Framework.ControlPlane;
using UltraSim.Bridges.ControlPlane.ECS;
using UltraSim.Bridges.ControlPlane.ServerRuntime;
using UltraSim.Client.ControlPlane;
using UltraSim.Client.Core;
using UltraSim.ECS;
using UltraSim.Server.Runtime;

var tests = new (string, Action)[]
{
    ("Identity_GrammarAndHardBounds", Identity),
    ("Values_ClosedExactMachineAndSchema", Values),
    ("Registration_AtomicDuplicateCapacityGeneration", Registration),
    ("Authorization_DiscoveryReadWriteAndVisibility", Authorization),
    ("Mutation_ValidationRevisionDisabledBeforeOwner", Mutation),
    ("Batch_OwnerSemanticRejectionIsTransactional", Batch),
    ("Actions_TypedArgumentsAndFailureAudit", Actions),
    ("Collections_BoundedStableRowsAndRevision", Collections),
    ("Viewer_DraftRequestedPendingAndRetirement", Viewer),
    ("Ownership_NoCrossThreadOrReentrantRetirement", Ownership),
    ("ECS_GenericPanelAndPublicSnapshotParity", EcsPanel),
    ("ECS_ScheduleEnableGenerationAndReadOnlyFacts", EcsControls),
    ("ECS_RegisteredIdleZeroHotPathCallsAndAllocation", HotPath),
    ("Runtime_FixedAndVariablePolicies", Runtime),
    ("Runtime_UnsafeBoundaryRemainsDisabled", RuntimeUnsafe),
    ("Settings_BatchPersistenceGlobalRealmAndProvenance", Settings),
    ("Settings_ConsentIsNotWriteAuthorityAndPendingIsReal", SettingsConsent)
};
foreach (var (name, test) in tests) { test(); Console.WriteLine("PASS P1 " + name); }
Console.WriteLine($"All {tests.Length} P1 Control Plane tests passed.");

static void Identity()
{
    Check(!default(ControlId).IsValid);
    Check(new ControlProviderId("ecs.runtime") == new ControlProviderId("ecs.runtime"));
    Check(new ControlId("enabled") != new ControlId("disabled"));
    foreach (string bad in new[] { "", "ECS", "a/b", " a", "a ", "a..b", "a.", "a\\b", new string('a', 97) })
        Throws<ArgumentException>(() => new ControlId(bad));
    Check(new ControlId(new string('a', 96)).IsValid);
    Check(new ControlCategoryPath("ECS/Scheduler").IsValid);
    foreach (string bad in new[] { "", "/ECS", "ECS/", "ECS//Systems", "../ECS", "ECS Systems", string.Join('/', Enumerable.Repeat("ECS", 9)) })
        Throws<ArgumentException>(() => new ControlCategoryPath(bad));
    Check(new DiagnosticId("tick").IsValid && new ControlActionId("sample").IsValid &&
        new ControlCollectionId("systems").IsValid && new ControlCapabilityId("ecs.controls.read").IsValid &&
        new ControlOptionId("serial").IsValid && new ControlRowId("movement").IsValid && new ControlRequestId("r1").IsValid);
}
static void Values()
{
    Check(ControlValue.Int64(1) != ControlValue.UInt64(1));
    Check(ControlValue.Float64(-0d) == ControlValue.Float64(0d));
    Check(ControlValue.Text("stable") == ControlValue.Text("stable"));
    Check(ControlValue.Option(new("serial")) != ControlValue.Text("serial"));
    Throws<InvalidOperationException>(() => ControlValue.Bool(true).AsInt64());
    foreach (double d in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
        Throws<ArgumentOutOfRangeException>(() => ControlValue.Float64(d));
    Throws<ArgumentException>(() => ControlValue.Text(new string('x', 1025)));
    var integer = new ControlValueSchema(ControlValueKind.Int64, ControlValue.Int64(long.MaxValue - 1), ControlValue.Int64(long.MaxValue));
    Check(integer.Accepts(ControlValue.Int64(long.MaxValue)));
    Check(!integer.Accepts(ControlValue.Int64(long.MaxValue - 2)));
    var unsigned = new ControlValueSchema(ControlValueKind.UInt64, ControlValue.UInt64(ulong.MaxValue - 1), ControlValue.UInt64(ulong.MaxValue));
    Check(!unsigned.Accepts(ControlValue.UInt64(ulong.MaxValue - 2)));
    Check(!new ControlValueSchema(ControlValueKind.Text, maximumText: 3).Accepts(ControlValue.Text("four")));
    Check(!new ControlValueSchema(ControlValueKind.Option, options: [new("one")]).Accepts(ControlValue.Option(new("two"))));
    Throws<ArgumentException>(() => new ControlValueSchema(ControlValueKind.Option, options: [new("one"), new("one")]));
    Throws<ArgumentException>(() => new ControlValueSchema(ControlValueKind.Int64, ControlValue.Int64(5), ControlValue.Int64(4)));
    Check(!typeof(ControlValue).GetProperties().Any(p => p.PropertyType == typeof(object)));
    var source = new[] { new ControlMutation(new("live"), ControlValue.Int64(2)) };
    var request = SetControlsRequest.Create(Context(), new("proof"), 1, source);
    source[0] = new(new("live"), ControlValue.Int64(9));
    Equal(2L, request.Mutations[0].Value.AsInt64());
    Throws<ArgumentException>(() => SetControlsRequest.Create(Context(), new("proof"), 1,
        Enumerable.Repeat(source[0], ControlLimits.Batch + 1)));
}
static void Registration()
{
    var registry = new ControlPlaneRegistry(); var p = new ProofProvider();
    Check(registry.TryRegister(p, out ulong g, out _));
    Check(!registry.TryRegister(p, out _, out var reason)); Equal("duplicate-provider", reason);
    Throws<ArgumentException>(() => new ControlProviderDefinition(new("duplicate"), [p.Definition.Controls[0], p.Definition.Controls[0]]));
    Throws<ArgumentException>(() => new ControlProviderDefinition(new("duplicate"), [p.Definition.Controls[0]],
        [new(new("live"), new("Proof/State"), "live", "", new(ControlValueKind.Int64), new("proof.read"))]));
    Equal(1, registry.Discover(Principal()).Count);
    Check(registry.Retire(p.Definition.Id, g)); Check(!registry.Retire(p.Definition.Id, g));
    Check(registry.TryRegister(new ProofProvider(), out ulong next, out _)); Check(next > g);
    var old = Set(registry, g, "live", ControlValue.Int64(4)); Equal(ControlResultCode.StaleGeneration, old.Outcome.Code);
    var bounded = new ControlPlaneRegistry();
    for (int i = 0; i < ControlLimits.Providers; i++) Check(bounded.TryRegister(new ProofProvider("p" + i), out _, out _));
    Check(!bounded.TryRegister(new ProofProvider("overflow"), out _, out reason)); Equal("provider-capacity", reason);
    Equal(ControlLimits.Providers, bounded.Discover(Principal()).Count);
}
static void Authorization()
{
    var registry = new ControlPlaneRegistry(); var p = new ProofProvider(); registry.TryRegister(p, out var g, out _);
    var none = new ControlPrincipal("untrusted", ControlPrincipalKind.RemoteOperator, []);
    Equal(0, registry.Discover(none).Count);
    Equal(ControlResultCode.Unauthorized, registry.Read(none, new("proof"), g, new ControlId("live"), out _));
    var read = new ControlPrincipal("reader", ControlPrincipalKind.LocalUser, [new("proof.read")]);
    Check(registry.Discover(read)[0].Definition.Controls.All(c => c.Id.Value != "secret"));
    Equal(ControlResultCode.NotVisible, registry.Read(read, new("proof"), g, new ControlId("secret"), out _));
    Equal(ControlResultCode.Unauthorized, Set(registry, g, "live", ControlValue.Int64(3), read).Outcome.Code);
    Equal(ControlResultCode.Unauthorized, registry.Set(SetControlsRequest.Create(Context(read), new("proof"), g,
        [new(new("live"), ControlValue.Int64(3))], ulong.MaxValue)).Outcome.Code);
    Equal(0, p.Mutations);
}
static void Mutation()
{
    var r = new ControlPlaneRegistry(); var p = new ProofProvider(); r.TryRegister(p, out var g, out _);
    Equal(ControlResultCode.InvalidValue, Set(r, g, "live", ControlValue.Int64(11)).Outcome.Code);
    Equal(ControlResultCode.InvalidValue, Set(r, g, "live", ControlValue.Bool(true)).Outcome.Code);
    Equal(ControlResultCode.NotWritable, Set(r, g, "cold", ControlValue.Int64(2)).Outcome.Code);
    var disabled = Set(r, g, "disabled", ControlValue.Int64(2));
    Equal(ControlResultCode.Disabled, disabled.Outcome.Code); Equal("fixture disabled", disabled.Outcome.Diagnostic);
    Equal(0, p.Mutations);
    var accepted = r.Set(new SetControlRequest(Context(), new("proof"), new("live"), g, ControlValue.Int64(5)));
    Equal(ControlResultStatus.Accepted, accepted.Outcome.Status);
    Equal(5L, accepted.States.Single().Effective.AsInt64()); Check(accepted.Revision > 1);
    var stale = r.Set(SetControlsRequest.Create(Context(), new("proof"), g, [new(new("live"), ControlValue.Int64(6))], 1));
    Equal(ControlResultCode.StaleRevision, stale.Outcome.Code); Equal(1, p.Mutations);
    Equal(ControlResultCode.UnknownPublication, Set(r, g, "missing", ControlValue.Int64(1)).Outcome.Code);
    Equal(ControlResultCode.UnknownProvider, r.Set(SetControlsRequest.Create(Context(), new("missing"), 1,
        [new(new("live"), ControlValue.Int64(1))])).Outcome.Code);
}
static void Batch()
{
    var r = new ControlPlaneRegistry(); var p = new ProofProvider(); r.TryRegister(p, out var g, out _);
    var bad = r.Set(SetControlsRequest.Create(Context(), new("proof"), g,
        [new(new("live"), ControlValue.Int64(3)), new(new("cold"), ControlValue.Int64(3))]));
    Equal(ControlResultStatus.Rejected, bad.Outcome.Status); Equal(0, p.Mutations);
    p.RejectBatch = true;
    bad = r.Set(SetControlsRequest.Create(Context(), new("proof"), g,
        [new(new("live"), ControlValue.Int64(3)), new(new("restart"), ControlValue.Int64(3))]));
    Equal(ControlResultCode.OwnerRejected, bad.Outcome.Code); Equal(0, p.Mutations);
    p.RejectBatch = false;
    p.UnsupportedBatch = true;
    Equal(ControlResultCode.UnsupportedBatch, r.Set(SetControlsRequest.Create(Context(), new("proof"), g,
        [new(new("live"), ControlValue.Int64(3)), new(new("restart"), ControlValue.Int64(3))])).Outcome.Code);
    Equal(0, p.Mutations); p.UnsupportedBatch = false;
    var ok = r.Set(SetControlsRequest.Create(Context(), new("proof"), g,
        [new(new("live"), ControlValue.Int64(3)), new(new("restart"), ControlValue.Int64(4))]));
    Equal(ControlResultStatus.Accepted, ok.Outcome.Status); Equal(1, p.Mutations);
    Check(ok.States[1].Pending == ControlValue.Int64(4)); Equal(1L, ok.States[1].Effective.AsInt64());
}
static void Actions()
{
    var audit = new Audit(); var r = new ControlPlaneRegistry(audit); var p = new ProofProvider();
    r.TryRegister(p, out var g, out _);
    ControlActionRequest Request(ControlValue value) => ControlActionRequest.Create(Context(), new("proof"), new("pulse"), g, [value]);
    Equal(ControlResultCode.InvalidValue, r.Invoke(Request(ControlValue.Text("bad"))).Outcome.Code); Equal(0, p.Actions);
    var readOnly = new ControlPrincipal("reader", ControlPrincipalKind.TestAutomation, [new("proof.read")]);
    Equal(ControlResultCode.Unauthorized, r.Invoke(ControlActionRequest.Create(Context(readOnly), new("proof"),
        new("pulse"), g, [ControlValue.UInt64(2)])).Outcome.Code);
    Equal(ControlResultStatus.Accepted, r.Invoke(Request(ControlValue.UInt64(2))).Outcome.Status); Equal(1, p.Actions);
    Equal("operator", audit.Last!.Context.Principal.Identity); Check(audit.Last.Action == new ControlActionId("pulse"));
    p.Fail = true;
    Throws<InvalidOperationException>(() => r.Invoke(Request(ControlValue.UInt64(2))));
    Equal(ControlResultStatus.Failed, audit.Last!.Result.Outcome.Status);
    Throws<InvalidOperationException>(() => Set(r, g, "live", ControlValue.Int64(2)));
    Equal(ControlResultStatus.Failed, audit.Last!.Result.Outcome.Status);
    audit.Fail = true;
    try { r.Invoke(Request(ControlValue.UInt64(2))); throw new Exception("Missing aggregate failure."); }
    catch (AggregateException failures)
    {
        Equal(2, failures.InnerExceptions.Count);
        Check(failures.InnerExceptions.Any(e => e.Message == "action failure"));
        Check(failures.InnerExceptions.Any(e => e.Message == "audit failure"));
    }
    audit.Fail = false;
    p.Fail = false;
    Check(r.Retire(p.Definition.Id, g)); // exception did not leak a dispatch owner
}
static void Collections()
{
    var r = new ControlPlaneRegistry(); var p = new ProofProvider(); r.TryRegister(p, out var g, out _);
    Equal(ControlResultCode.Accepted, r.Read(Principal(), new("proof"), g, new ControlCollectionId("rows"), out var rows));
    Equal(new ControlRowId("one"), rows!.Rows[0].Id); Equal(1UL, rows.Revision);
    Set(r, g, "live", ControlValue.Int64(3));
    r.Read(Principal(), new("proof"), g, new ControlCollectionId("rows"), out var changed); Check(changed!.Revision > rows.Revision);
    p.BadRows = true; Throws<InvalidOperationException>(() => r.Read(Principal(), new("proof"), g, new ControlCollectionId("rows"), out _));
    p.BadRows = false; p.TooManyRows = true;
    Throws<ArgumentException>(() => r.Read(Principal(), new("proof"), g, new ControlCollectionId("rows"), out _));
    Throws<ArgumentException>(() => new ControlCollectionDescriptor(new("bad"), new("Proof/State"), "", "", 1025,
        [new(new("v"), "v", new(ControlValueKind.Int64))], new("proof.read")));
    Throws<ArgumentException>(() => new ControlCollectionRow(new("too-wide"), Enumerable.Repeat(ControlValue.Int64(1), 25)));
}
static void Viewer()
{
    var r = new ControlPlaneRegistry(); var p = new ProofProvider(); r.TryRegister(p, out var g, out _);
    var v = new ControlPlaneViewer(r, Principal());
    Check(v.Capture().Any(c => c.Path.Value == "Proof/State"));
    Check(v.SetDraft(new("proof"), g, new("restart"), ControlValue.Int64(4)));
    r.Read(Principal(), new("proof"), g, new ControlId("restart"), out var before);
    Equal(1L, before!.Requested.AsInt64()); Check(before.Pending is null);
    Equal(ControlResultStatus.Accepted, v.Apply(new("apply"), new("proof"), g).Outcome.Status);
    r.Read(Principal(), new("proof"), g, new ControlId("restart"), out var after);
    Equal(4L, after!.Requested.AsInt64()); Equal(1L, after.Effective.AsInt64()); Check(after.Pending.HasValue);
    Equal(ControlProvenanceKind.TestOverride, after.RequestedProvenance.Kind);
    Equal(ControlProvenanceKind.BuiltInDefault, after.EffectiveProvenance.Kind);
    Check(v.SetDraft(new("proof"), g, new("live"), ControlValue.Int64(2)));
    r.Retire(new("proof"), g); Equal(0, v.Capture().Count); Check(!v.TryGetDraft(new("proof"), g, new("live"), out _));
}
static void Ownership()
{
    var r = new ControlPlaneRegistry(); var p = new ProofProvider(); r.TryRegister(p, out var g, out _);
    Task.Run(() => Throws<InvalidOperationException>(() => r.Discover(Principal()))).GetAwaiter().GetResult();
    p.DuringSet = () => r.Retire(new("proof"), g);
    Throws<InvalidOperationException>(() => Set(r, g, "live", ControlValue.Int64(2)));
    p.DuringSet = null;
    Check(r.Retire(new("proof"), g)); Check(!r.Retire(new("proof"), g));
    Equal(ControlResultCode.UnknownProvider, Set(r, g, "live", ControlValue.Int64(2)).Outcome.Code);
}
static void EcsPanel()
{
    using var f = new EcsFixture(); var r = new ControlPlaneRegistry();
    var p = new EcsControlProvider(f.World, f.Bindings); r.TryRegister(p, out var g, out _);
    for (int i = 0; i < 10; i++) f.World.Tick(1d / 60);
    var v = new ControlPlaneViewer(r, Principal()); var categories = v.Capture();
    Check(categories.Any(c => c.Path.Value == "ECS/Scheduler")); Check(categories.Any(c => c.Path.Value == "ECS/Systems"));
    r.Read(Principal(), new("ecs.runtime"), g, new ControlCollectionId("systems"), out var collection);
    var native = new SystemRuntimeSnapshot[3]; f.World.CopySystemRuntimeSnapshots(native);
    Equal(3, collection!.Rows.Count);
    for (int i = 0; i < native.Length; i++)
    {
        var row = collection.Rows[i];
        Equal(native[i].Key, row.Values[0].AsText()); Equal(native[i].RunCount, row.Values[10].AsUInt64());
        Equal(native[i].LastMicroseconds, row.Values[11].AsFloat64()); Equal(native[i].LastRows, (int)row.Values[14].AsInt64());
        Check(row.Values[17].AsText().Contains("threshold="));
    }
    r.Read(Principal(), new("ecs.runtime"), g, new DiagnosticId("reserved-bytes"), out var reserved);
    Equal(f.World.GetSchedulerRuntimeSnapshot().ReservedPayloadBytes, reserved!.Value.AsInt64());
    Console.WriteLine("P1_ECS_PANEL " + System.Text.Json.JsonSerializer.Serialize(new
    {
        provider = "ecs.runtime", generation = g, revision = collection.Revision,
        categories = categories.Select(c => c.Path.Value).ToArray(),
        systems = collection.Rows.Select(row => new
        {
            id = row.Id.Value, key = row.Values[0].AsText(), stage = row.Values[1].AsText(),
            period = row.Values[4].AsInt64(), runs = row.Values[10].AsUInt64(),
            lastMicroseconds = row.Values[11].AsFloat64(), workItems = row.Values[16].AsInt64()
        }).ToArray(),
        reservedBytes = reserved.Value.AsInt64()
    }));
    Check(r.Retire(new("ecs.runtime"), g)); Equal(0, v.Capture().Count);
}
static void EcsControls()
{
    using var f = new EcsFixture(); var r = new ControlPlaneRegistry();
    var p = new EcsControlProvider(f.World, f.Bindings); r.TryRegister(p, out var g, out _);
    ControlResult Apply(params ControlMutation[] m) => r.Set(SetControlsRequest.Create(Context(), new("ecs.runtime"), g, m));
    Equal(ControlResultStatus.Accepted, Apply(new ControlMutation(new("movement.enabled"), ControlValue.Bool(false))).Outcome.Status);
    var native = new SystemRuntimeSnapshot[3]; f.World.CopySystemRuntimeSnapshots(native); Check(!native[0].Enabled);
    Equal(ControlResultStatus.Accepted, Apply(new(new("movement.period"), ControlValue.Int64(4)),
        new(new("movement.offset"), ControlValue.Int64(3))).Outcome.Status);
    Equal(ControlResultCode.OwnerRejected, Apply(new(new("movement.period"), ControlValue.Int64(2)),
        new(new("movement.enabled"), ControlValue.Bool(true))).Outcome.Code);
    f.World.CopySystemRuntimeSnapshots(native); Equal(4, native[0].PeriodTicks); Check(!native[0].Enabled);
    Equal(ControlResultCode.UnknownPublication, Apply(new ControlMutation(new("movement.stage"), ControlValue.Text("Combat"))).Outcome.Code);
    Equal(ControlResultCode.OwnerRejected, Apply(new ControlMutation(new("run-mode"), ControlValue.Option(new("parallel")))).Outcome.Code);
    r.Retire(new("ecs.runtime"), g); r.TryRegister(new EcsControlProvider(f.World, f.Bindings), out var next, out _);
    Equal(ControlResultCode.StaleGeneration, r.Set(SetControlsRequest.Create(Context(), new("ecs.runtime"), g,
        [new(new("movement.enabled"), ControlValue.Bool(true))])).Outcome.Code); Check(next > g);
    r.Retire(new("ecs.runtime"), next);
    using var cold = new EcsFixture(running: false);
    r.TryRegister(new EcsControlProvider(cold.World, cold.Bindings), out var coldGeneration, out _);
    r.Read(Principal(), new("ecs.runtime"), coldGeneration, new ControlId("movement.enabled"), out var disabled);
    Check(!disabled!.Writable);
}
static void HotPath()
{
    using var plain = new EcsFixture(); using var registered = new EcsFixture();
    var p = new EcsControlProvider(registered.World, registered.Bindings); var r = new ControlPlaneRegistry();
    r.TryRegister(p, out var g, out _);
    for (int i = 0; i < 2048; i++) { plain.World.Tick(1d / 60); registered.World.Tick(1d / 60); }
    ulong observations = p.Observations;
    const int ticks = 20000;
    long before = GC.GetAllocatedBytesForCurrentThread(); long start = Stopwatch.GetTimestamp();
    for (int i = 0; i < ticks; i++) plain.World.Tick(1d / 60);
    long plainTicks = Stopwatch.GetTimestamp() - start; long plainAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
    before = GC.GetAllocatedBytesForCurrentThread(); start = Stopwatch.GetTimestamp();
    for (int i = 0; i < ticks; i++) registered.World.Tick(1d / 60);
    long registeredTicks = Stopwatch.GetTimestamp() - start; long registeredAllocation = GC.GetAllocatedBytesForCurrentThread() - before;
    Equal(0L, plainAllocation); Equal(0L, registeredAllocation); Equal(observations, p.Observations);
    var a = new SystemRuntimeSnapshot[3]; var b = new SystemRuntimeSnapshot[3];
    plain.World.CopySystemRuntimeSnapshots(a); registered.World.CopySystemRuntimeSnapshots(b);
    for (int i = 0; i < 3; i++) Equal(a[i].RunCount, b[i].RunCount);
    Console.WriteLine($"P1_IDLE_ECS ticks={ticks} plainTimestampTicks={plainTicks} registeredTimestampTicks={registeredTicks} frequency={Stopwatch.Frequency} allocations={plainAllocation}/{registeredAllocation} idleObservations=0");
    Check(r.Retire(new("ecs.runtime"), g));
}
static void Runtime()
{
    foreach (bool variable in new[] { false, true })
    {
        using var runtime = new ServerRuntime(new Composition(), new(Options(), WorldClockOptions.Default, variable, variable));
        runtime.Initialize(); runtime.Start();
        var p = new ServerRuntimeControlProvider(runtime); var r = new ControlPlaneRegistry(); r.TryRegister(p, out var g, out _);
        r.Read(Principal(), new("server.runtime"), g, new ControlId("playback"), out var s); Equal(variable, s!.Writable);
        var result = r.Set(SetControlsRequest.Create(Context(), new("server.runtime"), g,
            [new(new("playback"), ControlValue.Int64(2)), new(new("frequency"), ControlValue.Int64(120))]));
        Equal(variable ? ControlResultStatus.Accepted : ControlResultStatus.Rejected, result.Outcome.Status);
        Equal(variable ? 2 : 1, runtime.PlaybackRate); Equal(variable ? 120 : 60, runtime.SimulationFrequencyHz);
        if (variable)
        {
            result = r.Set(SetControlsRequest.Create(Context(), new("server.runtime"), g,
                [new(new("playback"), ControlValue.Int64(3)), new(new("frequency"), ControlValue.Int64(997))]));
            Equal(ControlResultCode.OwnerRejected, result.Outcome.Code); Equal(2, runtime.PlaybackRate);
        }
        runtime.Tick(); var snap = runtime.CaptureControlSnapshot();
        r.Read(Principal(), new("server.runtime"), g, new DiagnosticId("tick"), out var tick); Equal(snap.SimulationTick, tick!.Value.AsUInt64());
        r.Read(Principal(), new("server.runtime"), g, new DiagnosticId("generation"), out var gen); Equal(snap.ActiveGeneration, gen!.Value.AsUInt64());
        Task.Run(() => Throws<InvalidOperationException>(() => p.Capture(new ControlId("playback")))).GetAwaiter().GetResult();
        r.Retire(new("server.runtime"), g);
    }
}
static void RuntimeUnsafe()
{
    using var runtime = new ServerRuntime(new Composition(), new(Options(), WorldClockOptions.Default, true, true));
    runtime.Initialize(); runtime.Start();
    var p = new ServerRuntimeControlProvider(runtime); var r = new ControlPlaneRegistry(); r.TryRegister(p, out var g, out _);
    runtime.Game.TryUseActiveGeneration((world, generation) =>
    {
        r.Read(Principal(), new("server.runtime"), g, new ControlId("playback"), out var state);
        Check(!state!.Writable); Check(state.DisabledReason.Contains("idle"));
        Equal(ControlResultCode.NotWritable, r.Set(SetControlsRequest.Create(Context(), new("server.runtime"), g,
            [new(new("playback"), ControlValue.Int64(2))])).Outcome.Code);
        return true;
    }, out _);
    Equal(ServerRuntimeState.Running, runtime.State); Equal(1, runtime.PlaybackRate);
}
static void Settings()
{
    string root = Path.Combine(Path.GetTempPath(), "ultrasim-p1-" + Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(root);
    try
    {
        var store = new ClientSettingsFileStore(Path.Combine(root, "settings.json"));
        var doc = ClientSettingsDocument.Empty; var effective = ClientSettingsResolver.Resolve(doc);
        int commits = 0;
        void Commit(ClientSettingsDocument candidate)
        { store.Save(candidate); doc = candidate; effective = ClientSettingsResolver.Resolve(doc); commits++; }
        var p = new ClientSettingsControlProvider(() => doc, () => effective, Commit);
        var r = new ControlPlaneRegistry(); r.TryRegister(p, out var g, out _);
        var result = r.Set(SetControlsRequest.Create(Context(), new("client.settings"), g,
            [new(new("ui-scale"), ControlValue.Float64(1.5)), new(new("invert-y"), ControlValue.Bool(true)),
             new(new("render-distance"), ControlValue.Int64(3)), new(new("grammar"), ControlValue.Option(new("third-person")))]));
        Equal(ControlResultStatus.Accepted, result.Outcome.Status); Equal(1, commits);
        Equal(1.5f, store.Load().Document.Global.UiScale!.Value); Equal(6, ClientSettingsDocument.CurrentVersion);
        string bytes = File.ReadAllText(store.Path);
        result = r.Set(SetControlsRequest.Create(Context(), new("client.settings"), g,
            [new(new("ui-scale"), ControlValue.Float64(2)), new(new("render-distance"), ControlValue.Int64(4))]));
        Equal(ControlResultCode.InvalidValue, result.Outcome.Code); Equal(1, commits); Equal(bytes, File.ReadAllText(store.Path));
        r.Read(Principal(), new("client.settings"), g, new ControlId("ui-scale"), out var global);
        Equal(ControlProvenanceKind.UserGlobal, global!.RequestedProvenance.Kind);
        r.Retire(new("client.settings"), g);
        Check(ClientSettingsScopeKey.TryCreate("realm-a", out var scope));
        effective = ClientSettingsResolver.Resolve(doc, scope);
        p = new(() => doc, () => effective, candidate =>
        { store.Save(candidate); doc = candidate; effective = ClientSettingsResolver.Resolve(doc, scope); }, scope);
        r.TryRegister(p, out g, out _);
        result = r.Set(SetControlsRequest.Create(Context(), new("client.settings"), g, [new(new("ui-scale"), ControlValue.Float64(2))]));
        Equal(ControlResultStatus.Accepted, result.Outcome.Status);
        r.Read(Principal(), new("client.settings"), g, new ControlId("ui-scale"), out var realm);
        Equal(ControlProvenanceKind.UserRealm, realm!.RequestedProvenance.Kind);
        Equal(1.5f, doc.Global.UiScale!.Value); Equal(2f, ClientSettingsResolver.Resolve(store.Load().Document, scope).UiScale);
        Check(!Directory.EnumerateFiles(root).Any(path => path.Contains("control-plane")));
    }
    finally { Directory.Delete(root, recursive: true); }
}
static void SettingsConsent()
{
    var doc = ClientSettingsDocument.Empty;
    var recommendation = new ClientProfileRecommendation(ClientControlGrammar.ThirdPersonDirect, ClientCameraProfile.ThirdPersonFollow, 10, 70);
    var effective = ClientSettingsResolver.Resolve(doc, recommendation: recommendation, mode: ClientProfileApplicationMode.UseServerDefaults);
    var p = new ClientSettingsControlProvider(() => doc, () => effective, candidate => doc = candidate,
        recommendation: recommendation, consent: ClientProfileApplicationMode.UseServerDefaults);
    var r = new ControlPlaneRegistry(); r.TryRegister(p, out var g, out _);
    r.Read(Principal(), new("client.settings"), g, new ControlId("grammar"), out var grammar);
    Equal(ControlProvenanceKind.ServerRecommendation, grammar!.EffectiveProvenance.Kind);
    Equal(ControlProvenanceKind.BuiltInDefault, grammar.RequestedProvenance.Kind);
    var server = new ControlPrincipal("server-recommendation", ControlPrincipalKind.RemoteOperator, []);
    Equal(ControlResultCode.Unauthorized, r.Set(SetControlsRequest.Create(Context(server), new("client.settings"), g,
        [new(new("invert-y"), ControlValue.Bool(true))])).Outcome.Code); Check(doc.Global.InvertY is null);
    Equal(ControlResultStatus.Accepted, r.Set(SetControlsRequest.Create(Context(), new("client.settings"), g,
        [new(new("vegetation"), ControlValue.Bool(true))])).Outcome.Status);
    r.Read(Principal(), new("client.settings"), g, new ControlId("vegetation"), out var pending);
    Check(pending!.Requested.AsBool() && !pending.Effective.AsBool() && pending.Pending!.Value.AsBool());
    effective = ClientSettingsResolver.Resolve(doc, recommendation: recommendation, mode: ClientProfileApplicationMode.UseServerDefaults);
    r.Read(Principal(), new("client.settings"), g, new ControlId("vegetation"), out var applied);
    Check(applied!.Pending is null && applied.Effective.AsBool()); Check(applied.Revision > pending.Revision);
    Check(p.Definition.Controls.First(c => c.Id.Value == "volume").Description.Contains("no Audio"));
}

static EcsWorldOptions Options() => new(new EntityCapacity(16), maximumArchetypes: 1, maximumPreparedPlans: 0,
    maximumStructuralCommands: 0, scheduler: new EcsSchedulerOptions(maximumSystems: 4, maximumDependencies: 4,
        maximumWorkItems: 4, maximumStructuralSegments: 4, backgroundWorkerCount: 0, profilingEnabled: true));
static ControlPrincipal Principal() => new("operator", ControlPrincipalKind.TestAutomation,
    new[] { "proof.read", "proof.write", "ecs.controls.read", "ecs.controls.write", "server.runtime.read", "server.runtime.write",
     "client.settings.read", "client.settings.write" }.Select(s => new ControlCapabilityId(s)));
static ControlRequestContext Context(ControlPrincipal? p = null) => new(new("test-request"), p ?? Principal(), DateTimeOffset.UnixEpoch, 42);
static ControlResult Set(ControlPlaneRegistry r, ulong g, string id, ControlValue v, ControlPrincipal? p = null) =>
    r.Set(SetControlsRequest.Create(Context(p), new("proof"), g, [new(new(id), v)]));
static void Check(bool value) { if (!value) throw new InvalidOperationException("Assertion failed."); }
static void Equal<T>(T a, T b) { if (!EqualityComparer<T>.Default.Equals(a, b)) throw new InvalidOperationException($"Expected {a}, got {b}."); }
static void Throws<T>(Action action) where T : Exception
{ try { action(); } catch (T) { return; } throw new InvalidOperationException("Expected " + typeof(T).Name); }

sealed class Audit : IControlAuditSink
{
    public ControlAuditObservation? Last; public bool Fail;
    public void Observe(ControlAuditObservation observation)
    { if (Fail) throw new InvalidOperationException("audit failure"); Last = observation; }
}
sealed class ProofProvider : IControlProvider
{
    public ProofProvider(string name = "proof")
    {
        ControlDescriptor Control(string id, ControlApplyMode apply = ControlApplyMode.Live, ControlAudience audience = ControlAudience.All) =>
            new(new(id), new("Proof/State"), id, "", new(ControlValueKind.Int64, ControlValue.Int64(0), ControlValue.Int64(10)),
                ControlValue.Int64(1), new("proof.read"), new("proof.write"), apply, audience: audience);
        Definition = new(new(name), [Control("live"), Control("restart", ControlApplyMode.ServerRestart), Control("cold", ControlApplyMode.ReadOnly),
            Control("disabled"), Control("secret", audience: ControlAudience.Test)],
            [new(new("tick"), new("Proof/State"), "tick", "", new(ControlValueKind.UInt64), new("proof.read"))],
            [new(new("pulse"), new("Proof/State"), "pulse", "", [new(ControlValueKind.UInt64)], new("proof.read"), new("proof.write"))],
            [new(new("rows"), new("Proof/State"), "rows", "", 2, [new(new("value"), "value", new(ControlValueKind.Int64))], new("proof.read"))]);
    }
    public ControlProviderDefinition Definition { get; }
    public ulong Revision { get; private set; } = 1;
    public int Mutations, Actions;
    public bool RejectBatch, UnsupportedBatch, Fail, BadRows, TooManyRows;
    public Action? DuringSet;
    private long _live = 1, _requested = 1;
    public ControlState Capture(ControlId id)
    {
        var requested = ControlValue.Int64(id.Value == "live" ? _live : id.Value == "restart" ? _requested : 1);
        var effective = ControlValue.Int64(id.Value == "restart" ? 1 : requested.AsInt64());
        var requestedProvenance = new ControlProvenance(Revision > 1 ? ControlProvenanceKind.TestOverride : ControlProvenanceKind.BuiltInDefault);
        return new(requested, effective, requested != effective ? requested : null, requestedProvenance,
            id.Value == "restart" ? new(ControlProvenanceKind.BuiltInDefault) : requestedProvenance,
            requested != effective ? requestedProvenance : null, id.Value is not ("disabled" or "cold"),
            id.Value == "disabled" ? ControlResultCode.Disabled : ControlResultCode.Accepted,
            id.Value == "disabled" ? "fixture disabled" : "");
    }
    public ControlValue Capture(DiagnosticId id) => ControlValue.UInt64(Revision);
    public IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id) => Enumerable.Range(0, TooManyRows ? 3 : 1)
        .Select(i => new ControlCollectionRow(new(i == 0 ? "one" : "row" + i),
            [BadRows ? ControlValue.Bool(true) : ControlValue.Int64(_live)]));
    public ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations)
    {
        if (RejectBatch) return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected);
        if (UnsupportedBatch && mutations.Count > 1) return ControlOwnerResult.Reject(ControlResultCode.UnsupportedBatch);
        DuringSet?.Invoke();
        if (Fail) throw new InvalidOperationException("owner failure");
        foreach (var m in mutations) { if (m.Id.Value == "live") _live = m.Value.AsInt64(); else _requested = m.Value.AsInt64(); }
        Mutations++; Revision++; return ControlOwnerResult.Accepted;
    }
    public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id, IReadOnlyList<ControlValue> arguments)
    { if (Fail) throw new InvalidOperationException("action failure"); Actions++; Revision++; return ControlOwnerResult.Accepted; }
}
sealed class EcsFixture : IDisposable
{
    public EcsFixture(bool running = true)
    {
        var schema = ComponentSchema.Create(new SchemaId(0x71, 0x01), ReadOnlySpan<ComponentRegistration>.Empty);
        World = new EcsWorld(schema, new(new EntityCapacity(16), maximumArchetypes: 1, maximumPreparedPlans: 0,
            maximumStructuralCommands: 0, scheduler: new EcsSchedulerOptions(maximumSystems: 4, maximumDependencies: 4,
                maximumWorkItems: 4, maximumStructuralSegments: 4, backgroundWorkerCount: 0, profilingEnabled: true)));
        World.RegisterArchetype(ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
        string[] names = ["movement", "world", "cleanup"];
        SystemStage[] stages = [SystemStage.Movement, SystemStage.WorldSim, SystemStage.Cleanup];
        Bindings = new EcsControlBinding[3];
        for (int i = 0; i < 3; i++)
        {
            var d = new SystemDefinition(names[i], stages[i], new SystemSchedule(periodTicks: i + 1), SystemExecutionPolicy.Serial);
            var id = World.RegisterRangeSystem<WorkSystem, WorkJob>(d, new());
            Bindings[i] = new(id, new(names[i]), d);
        }
        World.Seal();
        if (running) { World.Initialize(); World.BeginRun(); }
    }
    public EcsWorld World { get; }
    public EcsControlBinding[] Bindings { get; }
    public void Dispose() => World.Dispose();
}
sealed class WorkSystem : IRangeSystemRuntime<WorkJob>
{
    public int GetRangeCount(in SystemPrepareContext context) => 1;
    public WorkJob Prepare(in SystemPrepareContext context) => new();
}
struct WorkJob : IRangeJob { public void Execute(in SystemJobContext context, int start, int count) { } }
sealed class Composition : IServerComposition
{
    public ComponentSchema Schema { get; } = ComponentSchema.Create(new SchemaId(0x71, 0x02), ReadOnlySpan<ComponentRegistration>.Empty);
    public void Configure(ServerGenerationBuilder builder)
    {
        builder.World.RegisterArchetype(ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
        builder.World.RegisterRangeSystem<WorkSystem, WorkJob>(new("runtime", SystemStage.WorldSim, executionPolicy: SystemExecutionPolicy.Serial), new());
    }
}
