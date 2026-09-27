using UltraSim.ECS;
using UltraSim.Framework.ControlPlane;

namespace UltraSim.Bridges.ControlPlane.ECS;

/// <summary>Explicit cold registration binding; never introspects private scheduler graph state.</summary>
public sealed record EcsControlBinding(SystemId System, ControlId Id, SystemDefinition Definition);

/// <summary>Cold owner-thread adapter over existing public ECS APIs. Retire before releasing its world.</summary>
public sealed class EcsControlProvider : IControlProvider
{
    public static readonly ControlCapabilityId Read = new("ecs.controls.read"), Write = new("ecs.controls.write");
    private readonly EcsWorld _world;
    private readonly EcsControlBinding[] _bindings;
    private readonly SystemRuntimeSnapshot[] _rows;
    private readonly Dictionary<ControlId, (int Row, string Field)> _controls = new();
    private readonly HashSet<ControlId> _overrides = new();
    private readonly Dictionary<DiagnosticId, Func<SchedulerRuntimeSnapshot, ControlValue>> _diagnostics = new();
    private readonly ColdMetadata[] _coldMetadata;
    private readonly record struct ColdMetadata(string Requires, string Reads, string Writes, string Structural,
        int ParallelThreshold, int MinimumPartition, string RandomAccessPolicy);
    private SystemRuntimeSnapshot[]? _previous;
    private SchedulerRuntimeSnapshot _previousScheduler;
    private SchedulerRunMode _previousMode;
    private EcsWorldState _previousState;
    private ulong _previousTick, _revision;
    public ulong Observations { get; private set; }
    public EcsControlProvider(EcsWorld world, IEnumerable<EcsControlBinding> bindings)
    {
        _world = world;
        if (!world.IsSealed) throw new ArgumentException("Bind only a frozen ECS graph.");
        _bindings = bindings.Take(121).ToArray();
        if (_bindings.Length > 120 || _bindings.Length != world.SystemCount ||
            _bindings.Any(b => !b.Id.IsValid) || _bindings.Select(b => b.Id).Distinct().Count() != _bindings.Length ||
            _bindings.Select(b => b.System).Distinct().Count() != _bindings.Length)
            throw new ArgumentException("Bind the complete bounded scheduler set.");
        _rows = new SystemRuntimeSnapshot[world.SystemCount];
        Refresh();
        _coldMetadata = new ColdMetadata[_bindings.Length];
        var descriptors = new List<ControlDescriptor>();
        var boolSchema = new ControlValueSchema(ControlValueKind.Bool);
        var periodSchema = new ControlValueSchema(ControlValueKind.Int64, ControlValue.Int64(1), ControlValue.Int64(int.MaxValue));
        var offsetSchema = new ControlValueSchema(ControlValueKind.Int64, ControlValue.Int64(0), ControlValue.Int64(int.MaxValue - 1));
        for (int i = 0; i < _bindings.Length; i++)
        {
            var b = _bindings[i];
            var row = Row(i);
            if (row.Key != b.Definition.Key) throw new ArgumentException("Cold binding does not match live system.");
            _coldMetadata[i] = new(string.Join(',', b.Definition.Requires), string.Join(',', b.Definition.AdditionalReads),
                string.Join(',', b.Definition.AdditionalWrites), string.Join(',', b.Definition.StructuralComponents),
                b.Definition.ParallelRowThreshold, b.Definition.MinimumRowsPerPartition,
                b.Definition.ParallelRandomAccessPolicy.ToString());
            var cold = _coldMetadata[i];
            if (new[] { cold.Requires, cold.Reads, cold.Writes, cold.Structural, cold.RandomAccessPolicy }
                .Any(text => text.Length > ControlLimits.Text)) throw new ArgumentException("Cold metadata exceeds bounded text.");
            Add(i, "enabled", boolSchema, ControlValue.Bool(row.Enabled));
            Add(i, "period", periodSchema, ControlValue.Int64(row.PeriodTicks));
            Add(i, "offset", offsetSchema, ControlValue.Int64(row.StartOffset));
        }
        void Add(int row, string field, ControlValueSchema schema, ControlValue value)
        {
            var id = new ControlId(_bindings[row].Id.Value + "." + field);
            _controls.Add(id, (row, field));
            descriptors.Add(new(id, new("ECS/Systems"), _bindings[row].Definition.Key + " " + field,
                "Applies only at the idle ECS owner boundary; graph facts remain read-only.", schema, value,
                Read, Write, ControlApplyMode.SafePoint, ControlPersistenceClass.OwnerManaged));
        }
        descriptors.Add(new(new("run-mode"), new("ECS/Scheduler"), "Scheduler run mode", "Parallel requires reserved workers.",
            new(ControlValueKind.Option, options: [new("serial"), new("parallel")]),
            ControlValue.Option(new("serial")), Read, Write, ControlApplyMode.SafePoint));
        void Diagnostic(string name, Func<SchedulerRuntimeSnapshot, long> get) =>
            _diagnostics.Add(new(name), s => ControlValue.Int64(get(s)));
        Diagnostic("workers", s => s.BackgroundWorkerCount);
        Diagnostic("concurrency", s => s.ExecutionConcurrency);
        Diagnostic("dispatches", s => s.DispatchCount);
        Diagnostic("wakes", s => s.WorkerWakeCount);
        Diagnostic("parks", s => s.WorkerParkCount);
        Diagnostic("merged-commands", s => s.MergedCommandCount);
        Diagnostic("merged-payload", s => s.MergedPayloadBytes);
        Diagnostic("identity-resolutions", s => s.FullEntityResolutionCount);
        Diagnostic("reserved-bytes", s => s.ReservedPayloadBytes);
        Diagnostic("worker-allocation", s => s.WorkerManagedAllocationBytes);
        Diagnostic("graph-ticks", s => s.GraphTimestampTicks);
        Diagnostic("pre-barrier-ticks", s => s.PreBarrierTimestampTicks);
        Diagnostic("merge-ticks", s => s.StructuralMergeTimestampTicks);
        Diagnostic("post-barrier-ticks", s => s.PostBarrierTimestampTicks);
        string[] names = ["key", "stage", "enabled", "manual", "period", "offset", "policy", "wave", "parallel",
            "last-tick", "runs", "last-us", "average-us", "peak-us", "rows", "chunks", "work-items",
            "requires", "reads", "writes", "structural", "parallel-threshold", "minimum-partition", "random-access-policy"];
        ControlValueKind[] kinds = [ControlValueKind.Text, ControlValueKind.Text, ControlValueKind.Bool, ControlValueKind.Bool,
            ControlValueKind.Int64, ControlValueKind.Int64, ControlValueKind.Text, ControlValueKind.Int64, ControlValueKind.Bool,
            ControlValueKind.UInt64, ControlValueKind.UInt64, ControlValueKind.Float64, ControlValueKind.Float64,
            ControlValueKind.Float64, ControlValueKind.Int64, ControlValueKind.Int64, ControlValueKind.Int64,
            ControlValueKind.Text, ControlValueKind.Text, ControlValueKind.Text, ControlValueKind.Text,
            ControlValueKind.Int64, ControlValueKind.Int64, ControlValueKind.Text];
        Definition = new(new("ecs.runtime"), descriptors,
            _diagnostics.Keys.Select(id => new DiagnosticDescriptor(id, new("ECS/Scheduler"), id.Value!, "",
                new(ControlValueKind.Int64), Read)),
            collections: [new(new("systems"), new("ECS/Systems"), "Systems", "Cold graph facts and current public runtime observations.",
                120, names.Select((n, i) => new ControlCollectionColumn(new(n), n, new(kinds[i]))), Read)]);
    }
    private SystemRuntimeSnapshot Row(int index) => _rows.First(r => r.Id == _bindings[index].System);
    private void Refresh()
    {
        // During graph execution the last idle snapshot remains readable, but no live scheduler
        // capture or management mutation may cross that boundary.
        if (_world.IsSystemExecutionActive) return;
        Observations++;
        _world.CopySystemRuntimeSnapshots(_rows);
        var scheduler = _world.GetSchedulerRuntimeSnapshot();
        var mode = _world.SchedulerRunMode; var tick = _world.TickCount; var state = _world.State;
        if (_previous is null || !_rows.SequenceEqual(_previous) || !_previousScheduler.Equals(scheduler) ||
            mode != _previousMode || tick != _previousTick || state != _previousState)
        {
            _revision = checked(_revision + 1);
            _previous ??= new SystemRuntimeSnapshot[_rows.Length];
            _rows.CopyTo(_previous, 0);
            _previousScheduler = scheduler; _previousMode = mode; _previousTick = tick; _previousState = state;
        }
    }
    public ControlProviderDefinition Definition { get; }
    public ulong Revision { get { Refresh(); return _revision; } }
    public ControlState Capture(ControlId id)
    {
        Refresh();
        ControlValue value;
        if (id.Value == "run-mode") value = ControlValue.Option(new(_previousMode == SchedulerRunMode.Serial ? "serial" : "parallel"));
        else
        {
            var c = _controls[id]; var row = Row(c.Row);
            value = c.Field switch { "enabled" => ControlValue.Bool(row.Enabled),
                "period" => ControlValue.Int64(row.PeriodTicks), _ => ControlValue.Int64(row.StartOffset) };
        }
        bool executing = _world.IsSystemExecutionActive;
        bool writable = !executing && _world.IsRunning;
        var provenance = new ControlProvenance(_overrides.Contains(id)
            ? ControlProvenanceKind.AdminRuntimeOverride : ControlProvenanceKind.ServerColdConfig);
        return new(value, value, null, provenance, provenance, null, writable,
            writable ? ControlResultCode.Accepted : ControlResultCode.Disabled,
            writable ? "" : executing ? "ECS graph is executing." : "ECS world is not running.");
    }
    public ControlValue Capture(DiagnosticId id) { Refresh(); return _diagnostics[id](_previousScheduler); }
    public IEnumerable<ControlCollectionRow> Capture(ControlCollectionId id)
    {
        Refresh();
        if (id.Value != "systems") throw new ArgumentException("Unknown collection.");
        return _bindings.Select((b, i) =>
        {
            var s = Row(i); var cold = _coldMetadata[i];
            return new ControlCollectionRow(new(b.Id.Value!), [
                ControlValue.Text(s.Key), ControlValue.Text(s.Stage.ToString()), ControlValue.Bool(s.Enabled), ControlValue.Bool(s.Manual),
                ControlValue.Int64(s.PeriodTicks), ControlValue.Int64(s.StartOffset), ControlValue.Text(s.ExecutionPolicy.ToString()),
                ControlValue.Int64(s.CompiledWave), ControlValue.Bool(s.LastExecutionWasParallel), ControlValue.UInt64(s.LastRunTick),
                ControlValue.UInt64(s.RunCount), ControlValue.Float64(s.LastMicroseconds), ControlValue.Float64(s.AverageMicroseconds),
                ControlValue.Float64(s.PeakMicroseconds), ControlValue.Int64(s.LastRows), ControlValue.Int64(s.LastChunks),
                ControlValue.Int64(s.LastWorkItems), ControlValue.Text(cold.Requires), ControlValue.Text(cold.Reads),
                ControlValue.Text(cold.Writes), ControlValue.Text(cold.Structural), ControlValue.Int64(cold.ParallelThreshold),
                ControlValue.Int64(cold.MinimumPartition), ControlValue.Text(cold.RandomAccessPolicy)]);
        }).ToArray();
    }
    public ControlOwnerResult Set(ControlRequestContext context, IReadOnlyList<ControlMutation> mutations)
    {
        Refresh();
        if (_world.IsSystemExecutionActive)
            return ControlOwnerResult.Reject(ControlResultCode.Disabled, "ECS graph is executing.");
        if (!_world.IsRunning) return ControlOwnerResult.Reject(ControlResultCode.Disabled, "ECS is not running.");
        // Validate all coupled schedules and worker policy before any setter is invoked.
        var schedules = _bindings.Select((b, i) => (Period: Row(i).PeriodTicks, Offset: Row(i).StartOffset)).ToArray();
        foreach (var m in mutations)
        {
            if (m.Id.Value == "run-mode")
            {
                if (m.Value.AsOption().Value == "parallel" && _world.GetSchedulerRuntimeSnapshot().BackgroundWorkerCount == 0)
                    return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "No background workers reserved.");
                continue;
            }
            var c = _controls[m.Id];
            if (c.Field == "period") schedules[c.Row].Period = checked((int)m.Value.AsInt64());
            if (c.Field == "offset") schedules[c.Row].Offset = checked((int)m.Value.AsInt64());
        }
        if (schedules.Any(s => s.Offset >= s.Period))
            return ControlOwnerResult.Reject(ControlResultCode.OwnerRejected, "Offset must be less than period.");
        foreach (var m in mutations)
        {
            if (m.Id.Value == "run-mode")
                _world.SchedulerRunMode = m.Value.AsOption().Value == "parallel" ? SchedulerRunMode.Parallel : SchedulerRunMode.Serial;
            else
            {
                var c = _controls[m.Id];
                if (c.Field == "enabled") _world.SetSystemEnabled(_bindings[c.Row].System, m.Value.AsBool());
                else _world.SetSystemSchedule(_bindings[c.Row].System, schedules[c.Row].Period, schedules[c.Row].Offset);
            }
            _overrides.Add(m.Id);
        }
        _revision = checked(_revision + 1); Refresh(); return ControlOwnerResult.Accepted;
    }
    public ControlOwnerResult Invoke(ControlRequestContext context, ControlActionId id, IReadOnlyList<ControlValue> arguments) =>
        ControlOwnerResult.Reject(ControlResultCode.UnknownPublication);
}
