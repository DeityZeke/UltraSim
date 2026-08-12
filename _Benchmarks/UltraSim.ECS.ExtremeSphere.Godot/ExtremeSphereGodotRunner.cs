using System.Diagnostics;
using System.Text.Json;
using Godot;
using UltraSim.ECS;
using UltraSim.ECS.ExtremeSphere;
using UltraSim.ECS.ExtremeSphere.V3;

public partial class ExtremeSphereGodotRunner : Node3D
{
    private const int FloatsPerTransform = 12;

    private GodotRunOptions _options = null!;
    private EcsWorld _world = null!;
    private ReadReadJobPlan<SpherePosition, SphereRenderVisible> _renderPlan;
    private ReadReadReadJobPlan<SpherePosition, SphereVelocity, SpherePulse> _canonicalPlan;
    private MultiMesh? _multiMesh;
    private float[] _transformBuffer = [];
    private long[] _frameTicks = [];
    private long[] _simulationTicks = [];
    private long[] _packTicks = [];
    private long[] _uploadTicks = [];
    private long[] _renderSetupTicks = [];
    private long[] _renderCpuTicks = [];
    private long[] _renderGpuTicks = [];
    private double[] _engineFps = [];
    private int _frame;
    private MeasurementBaseline _measurementBaseline;
    private MemorySnapshot _beforeWorld;
    private MemorySnapshot _afterSeed;
    private SpawnMetrics _spawn;
    private long _workerAllocationBefore;
    private bool _worldDisposed;

    public override void _Ready()
    {
        _options = GodotRunOptions.Parse(OS.GetCmdlineUserArgs());
        _beforeWorld = MemorySnapshot.Capture();
        BuildWorld();
        BuildRenderer();
        _frameTicks = GC.AllocateUninitializedArray<long>(_options.MeasuredFrames);
        _simulationTicks = GC.AllocateUninitializedArray<long>(_options.MeasuredFrames);
        _packTicks = GC.AllocateUninitializedArray<long>(_options.MeasuredFrames);
        _uploadTicks = GC.AllocateUninitializedArray<long>(_options.MeasuredFrames);
        _renderSetupTicks = GC.AllocateUninitializedArray<long>(_options.MeasuredFrames);
        _renderCpuTicks = GC.AllocateUninitializedArray<long>(_options.MeasuredFrames);
        _renderGpuTicks = GC.AllocateUninitializedArray<long>(_options.MeasuredFrames);
        _engineFps = GC.AllocateUninitializedArray<double>(_options.MeasuredFrames);
        RenderingServer.ViewportSetMeasureRenderTime(GetViewport().GetViewportRid(), true);
        GD.Print($"[ExtremeSphere] V3 ready: entities={_options.Entities:N0}, visible={_options.Visible:N0}, " +
            $"mode={(_options.Serial ? "serial" : "parallel")}, upload={_options.Upload}, gpu_sync={_options.SyncGpu}");
    }

    public override void _Process(double delta)
    {
        if (_frame == _options.WarmupFrames)
        {
            _measurementBaseline = MeasurementBaseline.Capture();
            _workerAllocationBefore = _world.GetSchedulerRuntimeSnapshot().WorkerManagedAllocationBytes;
        }
        long frameStart = Stopwatch.GetTimestamp();
        long start = Stopwatch.GetTimestamp();
        _world.Tick(_options.DeltaSeconds);
        long simulation = Stopwatch.GetTimestamp() - start;

        start = Stopwatch.GetTimestamp();
        if (_options.Visible > 0)
        {
            var pack = new TransformPackJob(_transformBuffer, _options.Visible);
            int packed = _world.ExecuteJob(_renderPlan, ref pack);
            if (packed != _options.Visible || pack.Packed != _options.Visible)
                throw new InvalidOperationException($"Render packing visited {packed:N0}/{_options.Visible:N0} visible rows.");
        }
        long packing = Stopwatch.GetTimestamp() - start;

        start = Stopwatch.GetTimestamp();
        if (_options.Upload && _options.Visible > 0)
        {
            RenderingServer.MultimeshSetBuffer(_multiMesh!.GetRid(), _transformBuffer);
            if (_options.SyncGpu)
                RenderingServer.ForceSync();
        }
        long upload = Stopwatch.GetTimestamp() - start;
        long frameWork = Stopwatch.GetTimestamp() - frameStart;

        if (_frame >= _options.WarmupFrames)
        {
            int sample = _frame - _options.WarmupFrames;
            _frameTicks[sample] = frameWork;
            _simulationTicks[sample] = simulation;
            _packTicks[sample] = packing;
            _uploadTicks[sample] = upload;
            _renderSetupTicks[sample] = MillisecondsToStopwatchTicks(RenderingServer.GetFrameSetupTimeCpu());
            _renderCpuTicks[sample] = MillisecondsToStopwatchTicks(
                RenderingServer.ViewportGetMeasuredRenderTimeCpu(GetViewport().GetViewportRid()));
            _renderGpuTicks[sample] = MillisecondsToStopwatchTicks(
                RenderingServer.ViewportGetMeasuredRenderTimeGpu(GetViewport().GetViewportRid()));
            _engineFps[sample] = Engine.GetFramesPerSecond();
        }

        _frame++;
        if (_frame < _options.WarmupFrames + _options.MeasuredFrames)
            return;

        WriteReportAndQuit();
    }

    public override void _ExitTree()
    {
        DisposeWorld();
    }

    private void BuildWorld()
    {
        ComponentSchema schema = ExtremeSphereSchema.CreateSchema();
        ComponentId positionId = ExtremeSphereSchema.GetComponentId<SpherePosition>();
        ComponentId velocityId = ExtremeSphereSchema.GetComponentId<SphereVelocity>();
        ComponentId pulseId = ExtremeSphereSchema.GetComponentId<SpherePulse>();
        ComponentId visibleId = ExtremeSphereSchema.GetComponentId<SphereRenderVisible>();
        ComponentId[] hiddenSignature = [positionId, velocityId, pulseId];
        ComponentId[] visibleSignature = [positionId, velocityId, pulseId, visibleId];
        Array.Sort(hiddenSignature, static (left, right) => left.Value.CompareTo(right.Value));
        Array.Sort(visibleSignature, static (left, right) => left.Value.CompareTo(right.Value));
        ChunkLayoutOptions layout = new(_options.ChunkKiB * 1024);
        int hiddenPopulation = _options.Entities - _options.Visible;
        int hiddenChunks = MaximumChunks(schema, hiddenSignature, hiddenPopulation, layout);
        int visibleChunks = MaximumChunks(schema, visibleSignature, _options.Visible, layout);
        bool parallel = !_options.Serial;
        int participants = parallel ? _options.Workers + 1 : 1;
        int maximumPartitions = Math.Max(1, participants * 4);
        var scheduler = new EcsSchedulerOptions(
            maximumSystems: 3,
            maximumDependencies: 2,
            maximumWorkItems: maximumPartitions * 3,
            maximumStructuralSegments: maximumPartitions * 3,
            backgroundWorkerCount: parallel ? _options.Workers : 0,
            callerParticipates: true,
            structuralCommandsPerProducer: 0,
            structuralPayloadBytesPerProducer: 0,
            maximumPartitionsPerSystem: maximumPartitions,
            phaseStaggeringEnabled: false);
        _world = new EcsWorld(
            schema,
            new EcsWorldOptions(
                new EntityCapacity(_options.Entities),
                maximumArchetypes: 2,
                maximumPreparedPlans: 8,
                scheduler: scheduler));
        ArchetypeHandle visibleArchetype = _world.RegisterArchetype(visibleSignature, layout, visibleChunks);
        ArchetypeHandle hiddenArchetype = _world.RegisterArchetype(hiddenSignature, layout, hiddenChunks);
        ComponentLookup<SpherePosition> positions = _world.PrepareLookup<SpherePosition>();
        ComponentLookup<SphereVelocity> velocities = _world.PrepareLookup<SphereVelocity>();
        ComponentLookup<SpherePulse> pulses = _world.PrepareLookup<SpherePulse>();
        WriteJobPlan<SpherePulse> phasePlan = _world.PrepareWriteJob<SpherePulse>();
        ReadReadWriteJobPlan<SpherePosition, SpherePulse, SphereVelocity> velocityPlan =
            _world.PrepareReadReadWriteJob<SpherePosition, SpherePulse, SphereVelocity>();
        ReadWriteJobPlan<SphereVelocity, SpherePosition> movementPlan =
            _world.PrepareReadWriteJob<SphereVelocity, SpherePosition>();
        _renderPlan = _world.PrepareReadReadJob<SpherePosition, SphereRenderVisible>();
        _canonicalPlan = _world.PrepareReadReadReadJob<SpherePosition, SphereVelocity, SpherePulse>();
        SystemExecutionPolicy policy = parallel ? SystemExecutionPolicy.Parallel : SystemExecutionPolicy.Serial;
        SystemId phase = _world.RegisterWriteSystem<SpherePulse, PhaseRuntime, PhaseJob>(
            Definition("extreme-sphere.phase", SystemStage.Movement, policy), phasePlan, new PhaseRuntime());
        SystemId velocity = _world.RegisterReadReadWriteSystem<
            SpherePosition, SpherePulse, SphereVelocity, VelocityRuntime, VelocityJob>(
            Definition("extreme-sphere.velocity", SystemStage.SpatialIndex, policy, [phase]),
            velocityPlan,
            new VelocityRuntime());
        _world.RegisterReadWriteSystem<SphereVelocity, SpherePosition, MovementRuntime, MovementJob>(
            Definition("extreme-sphere.movement", SystemStage.Perception, policy, [velocity]),
            movementPlan,
            new MovementRuntime());
        _world.Seal();
        _world.Initialize();

        MeasurementBaseline spawnBaseline = MeasurementBaseline.Capture();
        long spawnStart = Stopwatch.GetTimestamp();
        for (int ordinal = 0; ordinal < _options.Entities; ordinal++)
        {
            SphereWorkload.Initialize(
                ordinal, _options.Seed, _options.Radius,
                out float px, out float py, out float pz,
                out float speed, out float frequency, out float phaseValue);
            ArchetypeHandle archetype = ordinal < _options.Visible ? visibleArchetype : hiddenArchetype;
            if (!_world.TryCreate(archetype, out Entity entity) ||
                !_world.TryWrite(entity, positions, new SpherePosition { X = px, Y = py, Z = pz }) ||
                !_world.TryWrite(entity, velocities, default) ||
                !_world.TryWrite(entity, pulses, new SpherePulse { Speed = speed, Frequency = frequency, Phase = phaseValue }))
                throw new InvalidOperationException($"Godot V3 seed failed at ordinal {ordinal:N0}.");
        }
        long spawnTicks = Stopwatch.GetTimestamp() - spawnStart;
        AllocationMetrics spawnAllocation = spawnBaseline.Complete();
        _spawn = new SpawnMetrics(
            spawnTicks * 1_000d / Stopwatch.Frequency,
            spawnTicks * 1_000_000_000d / Stopwatch.Frequency / _options.Entities,
            spawnAllocation.AllThreadsBytes,
            spawnAllocation.CallerThreadBytes,
            spawnAllocation.Gen0Collections,
            spawnAllocation.Gen1Collections,
            spawnAllocation.Gen2Collections);
        WorldReservedPayloadSnapshot reserved = _world.GetReservedPayloadSnapshot();
        _afterSeed = MemorySnapshot.Capture(reserved.TotalKnownPayloadBytes, reserved.NativeChunkPayloadBytes);
        _world.BeginRun();
    }

    private void BuildRenderer()
    {
        if (_options.Visible == 0)
            return;
        _transformBuffer = GC.AllocateUninitializedArray<float>(checked(_options.Visible * FloatsPerTransform));
        _multiMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            UseColors = false,
            UseCustomData = false,
            Mesh = new SphereMesh
            {
                Radius = 0.08f,
                Height = 0.16f,
                RadialSegments = 6,
                Rings = 3,
            },
            InstanceCount = _options.Visible,
            VisibleInstanceCount = _options.Visible,
            CustomAabb = new Aabb(
                new Vector3(-_options.Radius - 1f, -_options.Radius - 1f, -_options.Radius - 1f),
                Vector3.One * ((_options.Radius + 1f) * 2f)),
        };
        var instance = new MultiMeshInstance3D
        {
            Multimesh = _multiMesh,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            GIMode = GeometryInstance3D.GIModeEnum.Disabled,
            IgnoreOcclusionCulling = true,
        };
        AddChild(instance);
        var camera = new Camera3D
        {
            Position = new Vector3(0, 0, _options.Radius * 2.5f),
            Far = Math.Max(1_000f, _options.Radius * 8f),
            Current = true,
        };
        AddChild(camera);
        camera.LookAt(Vector3.Zero);
        AddChild(new DirectionalLight3D { RotationDegrees = new Vector3(-35, -30, 0), ShadowEnabled = false });
    }

    private void WriteReportAndQuit()
    {
        AllocationMetrics allocation = _measurementBaseline.Complete(
            _world.GetSchedulerRuntimeSnapshot().WorkerManagedAllocationBytes - _workerAllocationBefore);
        WorldReservedPayloadSnapshot reserved = _world.GetReservedPayloadSnapshot();
        var canonical = new CanonicalJob { Hasher = new CanonicalSphereHasher() };
        _world.ExecuteJob(_canonicalPlan, ref canonical);
        var report = new GodotExtremeSphereReport
        {
            ProtocolVersion = SphereWorkload.ProtocolVersion,
            GodotVersion = Engine.GetVersionInfo().ToString(),
            RenderingMethod = RenderingServer.GetCurrentRenderingMethod(),
            RenderingDevice = RenderingServer.GetVideoAdapterName(),
            Options = _options,
            Spawn = _spawn,
            FrameWork = TimingDistribution.FromStopwatchTicks(_frameTicks),
            Simulation = TimingDistribution.FromStopwatchTicks(_simulationTicks),
            TransformPack = TimingDistribution.FromStopwatchTicks(_packTicks),
            BufferUpload = TimingDistribution.FromStopwatchTicks(_uploadTicks),
            RenderSetupCpu = TimingDistribution.FromStopwatchTicks(_renderSetupTicks),
            ViewportRenderCpu = TimingDistribution.FromStopwatchTicks(_renderCpuTicks),
            ViewportRenderGpu = TimingDistribution.FromStopwatchTicks(_renderGpuTicks),
            EngineFpsMean = _engineFps.Average(),
            EngineFpsMin = _engineFps.Min(),
            EngineFpsP95 = Percentile(_engineFps, 0.95),
            BufferBytes = _transformBuffer.LongLength * sizeof(float),
            MeasurementAllocations = allocation,
            BeforeWorldMemory = _beforeWorld,
            AfterSeedMemory = _afterSeed,
            AfterMeasurementMemory = MemorySnapshot.Capture(reserved.TotalKnownPayloadBytes, reserved.NativeChunkPayloadBytes),
            CanonicalState = canonical.Hasher.Finish(),
        };
        string output = Path.GetFullPath(_options.Output);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.WriteAllText(output, JsonSerializer.Serialize(report, ExtremeSphereJson.Options));
        GD.Print($"[ExtremeSphere] complete: sim={report.Simulation.MeanMicroseconds / 1000d:F3} ms " +
            $"pack={report.TransformPack.MeanMicroseconds / 1000d:F3} ms " +
            $"upload={report.BufferUpload.MeanMicroseconds / 1000d:F3} ms " +
            $"work={report.FrameWork.MeanMicroseconds / 1000d:F3} ms output={output}");
        SetProcess(false);
        DisposeWorld();
        GetTree().Quit();
    }

    private void DisposeWorld()
    {
        if (_worldDisposed)
            return;
        _worldDisposed = true;
        _world?.Dispose();
    }

    private static SystemDefinition Definition(
        string key, SystemStage stage, SystemExecutionPolicy policy, IReadOnlyList<SystemId>? requires = null) =>
        new(key, stage, executionPolicy: policy, requires: requires,
            parallelRowThreshold: 1, minimumRowsPerPartition: 4_096, automaticallyStagger: false);

    private static int MaximumChunks(
        ComponentSchema schema, ComponentId[] signature, int population, ChunkLayoutOptions layout)
    {
        Span<ComponentColumnLayout> columns = stackalloc ComponentColumnLayout[signature.Length];
        ChunkLayoutPlan plan = ChunkLayoutPlanner.Plan(schema, signature, layout, columns);
        return Math.Max(1, (population + plan.RowCapacity - 1) / plan.RowCapacity + 2);
    }

    private static double Percentile(double[] values, double percentile)
    {
        double[] copy = (double[])values.Clone();
        Array.Sort(copy);
        int index = (int)Math.Round((copy.Length - 1) * percentile);
        return copy[index];
    }

    private static long MillisecondsToStopwatchTicks(double milliseconds) =>
        (long)Math.Round(milliseconds * Stopwatch.Frequency / 1_000d);

    private struct TransformPackJob : IReadReadChunkJob<SpherePosition, SphereRenderVisible>
    {
        private readonly float[] _buffer;
        private readonly int _visible;
        internal int Packed;

        internal TransformPackJob(float[] buffer, int visible) => (_buffer, _visible, Packed) = (buffer, visible, 0);

        public void Execute(in ReadReadComponentChunk<SpherePosition, SphereRenderVisible> chunk)
        {
            for (int row = 0; row < chunk.Count; row++)
            {
                int instance = checked((int)chunk.EntityIndices[row]);
                if ((uint)instance >= (uint)_visible)
                    throw new InvalidOperationException("Visible archetype contains an out-of-range instance index.");
                SpherePosition position = chunk.ReadComponents1[row];
                _ = chunk.ReadComponents2[row].Value;
                int offset = instance * FloatsPerTransform;
                _buffer[offset] = 1f; _buffer[offset + 1] = 0f; _buffer[offset + 2] = 0f; _buffer[offset + 3] = position.X;
                _buffer[offset + 4] = 0f; _buffer[offset + 5] = 1f; _buffer[offset + 6] = 0f; _buffer[offset + 7] = position.Y;
                _buffer[offset + 8] = 0f; _buffer[offset + 9] = 0f; _buffer[offset + 10] = 1f; _buffer[offset + 11] = position.Z;
                Packed++;
            }
        }
    }
}

public sealed record GodotRunOptions(
    int Entities,
    int Visible,
    int WarmupFrames,
    int MeasuredFrames,
    int Workers,
    bool Serial,
    bool Upload,
    bool SyncGpu,
    uint Seed,
    float Radius,
    float DeltaSeconds,
    int ChunkKiB,
    string Output)
{
    public static GodotRunOptions Parse(string[] args)
    {
        int entities = Integer(args, "--entities=", 100_000);
        int visible = Math.Clamp(Integer(args, "--visible=", Math.Min(entities, 1_000_000)), 0, entities);
        return new GodotRunOptions(
            entities,
            visible,
            Integer(args, "--warmup=", 120),
            Integer(args, "--frames=", 600),
            Integer(args, "--workers=", Math.Max(1, Math.Min(7, System.Environment.ProcessorCount - 1))),
            args.Contains("--serial"),
            !args.Contains("--no-upload"),
            args.Contains("--sync-gpu"),
            (uint)Integer(args, "--seed=", 0xC0FFEE),
            Float(args, "--radius=", 50f),
            Float(args, "--delta=", 1f / 60f),
            Integer(args, "--chunk-kib=", 64),
            Text(args, "--output=") ?? Path.Combine("results", "godot-extreme-sphere.json"));
    }

    private static string? Text(string[] args, string prefix) =>
        args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    private static int Integer(string[] args, string prefix, int fallback) =>
        int.TryParse(Text(args, prefix), out int value) ? value : fallback;
    private static float Float(string[] args, string prefix, float fallback) =>
        float.TryParse(Text(args, prefix), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float value) ? value : fallback;
}

public sealed class GodotExtremeSphereReport
{
    public required string ProtocolVersion { get; init; }
    public required string GodotVersion { get; init; }
    public required string RenderingMethod { get; init; }
    public required string RenderingDevice { get; init; }
    public required GodotRunOptions Options { get; init; }
    public required SpawnMetrics Spawn { get; init; }
    public required TimingDistribution FrameWork { get; init; }
    public required TimingDistribution Simulation { get; init; }
    public required TimingDistribution TransformPack { get; init; }
    public required TimingDistribution BufferUpload { get; init; }
    public required TimingDistribution RenderSetupCpu { get; init; }
    public required TimingDistribution ViewportRenderCpu { get; init; }
    public required TimingDistribution ViewportRenderGpu { get; init; }
    public required double EngineFpsMean { get; init; }
    public required double EngineFpsMin { get; init; }
    public required double EngineFpsP95 { get; init; }
    public required long BufferBytes { get; init; }
    public required AllocationMetrics MeasurementAllocations { get; init; }
    public required MemorySnapshot BeforeWorldMemory { get; init; }
    public required MemorySnapshot AfterSeedMemory { get; init; }
    public required MemorySnapshot AfterMeasurementMemory { get; init; }
    public required CanonicalSphereState CanonicalState { get; init; }
}
