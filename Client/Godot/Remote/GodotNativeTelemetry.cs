#if ULTRASIM_ONLINE || ULTRASIM_COMPLETE
using System.Diagnostics;
using System.Numerics;
using Godot;
using UltraSim.Network;

namespace UltraSim.Client.Godot;

public readonly record struct GodotNativeTimingDistribution(
    ulong Samples,
    long P50Microseconds,
    long P95Microseconds,
    long P99Microseconds,
    long MaximumMicroseconds,
    int RollingSamples,
    long WholeWindowP50Microseconds,
    long WholeWindowP95Microseconds,
    long WholeWindowP99Microseconds);

/// <summary>
/// Cold native-client diagnostic projection. Counts describe presentation work,
/// not Game truth. Process/resource facts are sampled only when this snapshot is
/// requested.
/// </summary>
public readonly record struct GodotNativeTelemetrySnapshot(
    NetworkTelemetryLevel Level,
    string DisplayBackend,
    bool? Headless,
    string WindowMode,
    string VSyncMode,
    int MaximumFramesPerSecond,
    double? DisplayRefreshRateHz,
    ulong RenderFrames,
    GodotNativeTimingDistribution FrameIntervals,
    GodotNativeTimingDistribution FrameDurations,
    ulong PhysicalInputEvents,
    ulong PhysicalInputSamples,
    ulong SemanticInputResolutions,
    ulong CommandsConsidered,
    ulong CommandsSubmitted,
    ulong CommandsSuppressed,
    ulong ContinuousCommandsConsidered,
    ulong ActionEdgesConsidered,
    GodotNativeTimingDistribution PresentationUpdates,
    int ActorRootsCurrent,
    int ActorRootsHighWater,
    int NameplatesCurrent,
    int NameplatesHighWater,
    int OverheadLabelsCurrent,
    int OverheadLabelsHighWater,
    int RenderedChunksCurrent,
    int RenderedChunksHighWater,
    ulong ActorCreates,
    ulong ActorUpdates,
    ulong ActorRetires,
    ulong ActorVisualReplacements,
    double ProcessCpuMilliseconds,
    long ProcessWorkingSetBytes,
    long ProcessWorkingSetHighWaterBytes,
    long ManagedHeapBytes,
    long ManagedHeapHighWaterBytes,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ThreadCount,
    int ThreadHighWater);

internal sealed class GodotNativeTelemetry : IDisposable
{
    private readonly NativeTimingWindow _frameIntervals = new();
    private readonly NativeTimingWindow _frameDurations = new();
    private readonly NativeTimingWindow _presentationUpdates = new();
    private readonly Process _process = Process.GetCurrentProcess();
    private NetworkTelemetryLevel _level;
    private long _lastFrame;
    private ulong _frames;
    private ulong _inputEvents;
    private ulong _inputSamples;
    private ulong _semanticResolutions;
    private ulong _commandsConsidered;
    private ulong _commandsSubmitted;
    private ulong _continuousCommands;
    private ulong _actionEdges;
    private int _actorHighWater;
    private int _nameplateHighWater;
    private int _overheadHighWater;
    private int _chunkHighWater;
    private long _nextResourceSample;
    private long _allocatedStart;
    private TimeSpan _cpuStart;
    private int _gen0Start;
    private int _gen1Start;
    private int _gen2Start;
    private long _workingSetHighWater;
    private long _heapHighWater;
    private int _threadHighWater;

    internal void Configure(NetworkTelemetryLevel level)
    {
        if (!Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        _level = level;
        _lastFrame = 0;
        _frames = _inputEvents = _inputSamples = _semanticResolutions = 0;
        _commandsConsidered = _commandsSubmitted = 0;
        _continuousCommands = _actionEdges = 0;
        _actorHighWater = _nameplateHighWater = _overheadHighWater = _chunkHighWater = 0;
        _frameIntervals.Clear();
        _frameDurations.Clear();
        _presentationUpdates.Clear();
        _process.Refresh();
        _nextResourceSample = Stopwatch.GetTimestamp();
        _allocatedStart = GC.GetTotalAllocatedBytes(precise: false);
        _cpuStart = _process.TotalProcessorTime;
        _gen0Start = GC.CollectionCount(0);
        _gen1Start = GC.CollectionCount(1);
        _gen2Start = GC.CollectionCount(2);
        _workingSetHighWater = _process.WorkingSet64;
        _heapHighWater = GC.GetGCMemoryInfo().HeapSizeBytes;
        _threadHighWater = _process.Threads.Count;
    }

    internal long BeginFrame()
    {
        if (_level == NetworkTelemetryLevel.Off) return 0;
        long now = Stopwatch.GetTimestamp();
        if (_lastFrame != 0) _frameIntervals.Record(_lastFrame, now);
        _lastFrame = now;
        return now;
    }

    internal void EndFrame(long started)
    {
        if (_level == NetworkTelemetryLevel.Off) return;
        SaturatingIncrement(ref _frames);
        _frameDurations.Record(started, Stopwatch.GetTimestamp());
        SampleResources();
    }

    internal long BeginPresentation() =>
        _level == NetworkTelemetryLevel.Detailed ? Stopwatch.GetTimestamp() : 0;

    internal void EndPresentation(long started)
    {
        if (_level == NetworkTelemetryLevel.Detailed)
            _presentationUpdates.Record(started, Stopwatch.GetTimestamp());
    }

    internal void RecordInputEvent()
    {
        if (_level != NetworkTelemetryLevel.Off) SaturatingIncrement(ref _inputEvents);
    }

    internal void RecordCommandSample(bool continuous, bool actionEdge)
    {
        if (_level == NetworkTelemetryLevel.Off) return;
        SaturatingIncrement(ref _inputSamples);
        SaturatingIncrement(ref _semanticResolutions);
        SaturatingIncrement(ref _commandsConsidered);
        if (continuous) SaturatingIncrement(ref _continuousCommands);
        if (actionEdge) SaturatingIncrement(ref _actionEdges);
    }

    internal void RecordCommandSubmission()
    {
        if (_level != NetworkTelemetryLevel.Off)
            SaturatingIncrement(ref _commandsSubmitted);
    }

    internal GodotNativeTelemetrySnapshot Capture(
        in GodotActorPresenterSnapshot actors,
        in GodotWorldRendererSnapshot renderer)
    {
        _actorHighWater = Math.Max(_actorHighWater, actors.ActiveActors);
        _nameplateHighWater = Math.Max(_nameplateHighWater, actors.VisibleNameplates);
        _overheadHighWater = Math.Max(_overheadHighWater, actors.VisibleOverheadLabels);
        _chunkHighWater = Math.Max(_chunkHighWater, renderer.ActiveRenderChunks);
        SampleResources(force: true);
        GCMemoryInfo memory = GC.GetGCMemoryInfo();
        string displayBackend = SafeDisplayBackend();
        return new GodotNativeTelemetrySnapshot(
            _level,
            displayBackend,
            displayBackend.StartsWith("unavailable:", StringComparison.Ordinal)
                ? null
                : displayBackend.Contains("headless", StringComparison.OrdinalIgnoreCase),
            SafeWindowMode(),
            SafeVSyncMode(),
            Engine.MaxFps,
            SafeRefreshRate(),
            _frames, _frameIntervals.Capture(), _frameDurations.Capture(),
            _inputEvents, _inputSamples, _semanticResolutions,
            _commandsConsidered, _commandsSubmitted,
            _commandsConsidered >= _commandsSubmitted
                ? _commandsConsidered - _commandsSubmitted
                : 0,
            _continuousCommands, _actionEdges, _presentationUpdates.Capture(),
            actors.ActiveActors, _actorHighWater,
            actors.VisibleNameplates, _nameplateHighWater,
            actors.VisibleOverheadLabels, _overheadHighWater,
            renderer.ActiveRenderChunks, _chunkHighWater,
            actors.Added, actors.Updated, actors.Removed, actors.VisualReplacements,
            Math.Max(0, (_process.TotalProcessorTime - _cpuStart).TotalMilliseconds),
            _process.WorkingSet64, _workingSetHighWater, memory.HeapSizeBytes,
            _heapHighWater,
            Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - _allocatedStart),
            Math.Max(0, GC.CollectionCount(0) - _gen0Start),
            Math.Max(0, GC.CollectionCount(1) - _gen1Start),
            Math.Max(0, GC.CollectionCount(2) - _gen2Start),
            _process.Threads.Count, _threadHighWater);
    }

    public void Dispose() => _process.Dispose();

    private static string SafeDisplayBackend()
    {
        try { return DisplayServer.GetName(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return $"unavailable:{exception.GetType().Name}";
        }
    }

    private static string SafeWindowMode()
    {
        try { return DisplayServer.WindowGetMode().ToString(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return $"unavailable:{exception.GetType().Name}";
        }
    }

    private static string SafeVSyncMode()
    {
        try { return DisplayServer.WindowGetVsyncMode().ToString(); }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return $"unavailable:{exception.GetType().Name}";
        }
    }

    private static double? SafeRefreshRate()
    {
        try
        {
            double value = DisplayServer.ScreenGetRefreshRate();
            return double.IsFinite(value) && value > 0 ? value : null;
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return null;
        }
    }

    private void SampleResources(bool force = false)
    {
        if (_level == NetworkTelemetryLevel.Off) return;
        long now = Stopwatch.GetTimestamp();
        if (!force && now < _nextResourceSample) return;
        _process.Refresh();
        _workingSetHighWater = Math.Max(_workingSetHighWater, _process.WorkingSet64);
        _heapHighWater = Math.Max(_heapHighWater, GC.GetGCMemoryInfo().HeapSizeBytes);
        _threadHighWater = Math.Max(_threadHighWater, _process.Threads.Count);
        _nextResourceSample = checked(now + Math.Max(1, Stopwatch.Frequency / 4));
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private sealed class NativeTimingWindow
    {
        private const int Capacity = 256;
        private readonly long[] _samples = new long[Capacity];
        private readonly ulong[] _wholeBuckets = new ulong[64];
        private int _count;
        private int _next;
        private ulong _observations;
        private long _maximum;

        internal void Record(long start, long end)
        {
            long ticks = Math.Max(0, end - start);
            long microseconds = checked((long)((Int128)ticks * 1_000_000 / Stopwatch.Frequency));
            _samples[_next] = microseconds;
            _next = (_next + 1) % Capacity;
            if (_count < Capacity) _count++;
            if (_observations != ulong.MaxValue) _observations++;
            _maximum = Math.Max(_maximum, microseconds);
            int bucket = microseconds == 0 ? 0 : Math.Min(63, BitOperations.Log2(
                checked((ulong)microseconds)) + 1);
            if (_wholeBuckets[bucket] != ulong.MaxValue) _wholeBuckets[bucket]++;
        }

        internal GodotNativeTimingDistribution Capture()
        {
            if (_count == 0) return default;
            Span<long> ordered = stackalloc long[Capacity];
            _samples.AsSpan(0, _count).CopyTo(ordered);
            ordered[.._count].Sort();
            return new(_observations, ordered[Index(.50)], ordered[Index(.95)],
                ordered[Index(.99)], _maximum, _count,
                WholePercentile(50), WholePercentile(95), WholePercentile(99));
        }

        internal void Clear()
        {
            Array.Clear(_samples);
            Array.Clear(_wholeBuckets);
            _count = _next = 0;
            _observations = 0;
            _maximum = 0;
        }

        private int Index(double percentile) => Math.Clamp(
            (int)Math.Ceiling(_count * percentile) - 1, 0, _count - 1);

        private long WholePercentile(int percentage)
        {
            if (_observations == 0) return 0;
            ulong target = (ulong)(((UInt128)_observations * (uint)percentage + 99) / 100);
            ulong cumulative = 0;
            for (int index = 0; index < _wholeBuckets.Length; index++)
            {
                cumulative = ulong.MaxValue - cumulative < _wholeBuckets[index]
                    ? ulong.MaxValue
                    : cumulative + _wholeBuckets[index];
                if (cumulative < target) continue;
                if (index == 0) return 0;
                ulong upper = index == 63 ? ulong.MaxValue : (1UL << index) - 1;
                return checked((long)Math.Min(upper, checked((ulong)_maximum)));
            }
            return _maximum;
        }
    }
}
#endif
