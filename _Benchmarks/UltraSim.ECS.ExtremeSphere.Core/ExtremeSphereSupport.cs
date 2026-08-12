using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS.ExtremeSphere;

public readonly record struct MeasurementBaseline(
    long AllThreadsAllocated,
    long CallerThreadAllocated,
    int Gen0,
    int Gen1,
    int Gen2)
{
    public static MeasurementBaseline Capture() => new(
        GC.GetTotalAllocatedBytes(precise: true),
        GC.GetAllocatedBytesForCurrentThread(),
        GC.CollectionCount(0),
        GC.CollectionCount(1),
        GC.CollectionCount(2));

    public AllocationMetrics Complete(long instrumentedWorkerBytes = 0) => new(
        GC.GetTotalAllocatedBytes(precise: true) - AllThreadsAllocated,
        GC.GetAllocatedBytesForCurrentThread() - CallerThreadAllocated,
        instrumentedWorkerBytes,
        GC.CollectionCount(0) - Gen0,
        GC.CollectionCount(1) - Gen1,
        GC.CollectionCount(2) - Gen2);
}

public sealed record ExtremeSphereAdapterOptions(
    string OutputPath,
    SphereRunConfiguration Configuration)
{
    public static ExtremeSphereAdapterOptions Parse(string[] args, string defaultOutput)
    {
        int entities = ParseInt(Value(args, "--entities="), 100_000, minimum: 1);
        int warmup = ParseInt(Value(args, "--warmup-ticks="), 120, minimum: 0);
        int ticks = ParseInt(Value(args, "--ticks="), 600, minimum: 1);
        int workers = ParseInt(Value(args, "--workers="), Math.Max(1, Math.Min(7, Environment.ProcessorCount - 1)), minimum: 1);
        int chunkKiB = ParseInt(Value(args, "--chunk-kib="), 64, minimum: 16);
        uint seed = uint.TryParse(Value(args, "--seed="), out uint parsedSeed) ? parsedSeed : 0xC0FFEEu;
        float radius = float.TryParse(Value(args, "--radius="), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float parsedRadius) ? parsedRadius : 50f;
        float delta = float.TryParse(Value(args, "--delta="), System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture, out float parsedDelta) ? parsedDelta : 1f / 60f;
        SphereExecutionMode mode = string.Equals(Value(args, "--mode="), "serial", StringComparison.OrdinalIgnoreCase)
            ? SphereExecutionMode.Serial
            : SphereExecutionMode.Parallel;
        SpherePipeline pipeline = string.Equals(Value(args, "--pipeline="), "fused2", StringComparison.OrdinalIgnoreCase)
            ? SpherePipeline.Fused2
            : SpherePipeline.Split3;
        string output = Path.GetFullPath(Value(args, "--output=") ?? defaultOutput);
        return new ExtremeSphereAdapterOptions(output, new SphereRunConfiguration(
            entities, warmup, ticks, mode, pipeline, workers, seed, radius, delta, chunkKiB));
    }

    private static string? Value(string[] args, string prefix) =>
        args.FirstOrDefault(argument => argument.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

    private static int ParseInt(string? value, int fallback, int minimum)
    {
        int parsed = int.TryParse(value, out int candidate) ? candidate : fallback;
        if (parsed < minimum)
            throw new ArgumentOutOfRangeException(nameof(value), parsed, $"Value must be at least {minimum}.");
        return parsed;
    }
}

public struct CanonicalSphereHasher
{
    private ulong _hash0;
    private ulong _hash1;
    private long _checksum;
    private int _rows;
    private long _nonFinite;
    private double _radiusTotal;
    private double _radiusMin;
    private double _radiusMax;

    public CanonicalSphereHasher()
    {
        _hash0 = 0;
        _hash1 = 0;
        _checksum = 0;
        _rows = 0;
        _nonFinite = 0;
        _radiusTotal = 0;
        _radiusMin = double.MaxValue;
        _radiusMax = 0;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Add(
        uint ordinal,
        float px, float py, float pz,
        float vx, float vy, float vz,
        float speed, float frequency, float phase)
    {
        ulong row0 = unchecked(14695981039346656037UL ^ ordinal);
        ulong row1 = unchecked(0x9E3779B97F4A7C15UL + ordinal);
        AddBits(ref row0, ref row1, BitConverter.SingleToInt32Bits(px));
        AddBits(ref row0, ref row1, BitConverter.SingleToInt32Bits(py));
        AddBits(ref row0, ref row1, BitConverter.SingleToInt32Bits(pz));
        AddBits(ref row0, ref row1, BitConverter.SingleToInt32Bits(vx));
        AddBits(ref row0, ref row1, BitConverter.SingleToInt32Bits(vy));
        AddBits(ref row0, ref row1, BitConverter.SingleToInt32Bits(vz));
        AddBits(ref row0, ref row1, BitConverter.SingleToInt32Bits(speed));
        AddBits(ref row0, ref row1, BitConverter.SingleToInt32Bits(frequency));
        AddBits(ref row0, ref row1, BitConverter.SingleToInt32Bits(phase));

        // Canonical state is intentionally independent of physical archetype,
        // chunk, and row order. Entity index is the test-stable logical ordinal.
        _hash0 = unchecked(_hash0 + Avalanche(row0));
        _hash1 = unchecked(_hash1 + Avalanche(row1));
        _checksum = unchecked(_checksum + (long)Avalanche(row0 ^ row1));

        if (!float.IsFinite(px) || !float.IsFinite(py) || !float.IsFinite(pz) ||
            !float.IsFinite(vx) || !float.IsFinite(vy) || !float.IsFinite(vz) ||
            !float.IsFinite(speed) || !float.IsFinite(frequency) || !float.IsFinite(phase))
            _nonFinite++;

        double radius = Math.Sqrt((double)px * px + (double)py * py + (double)pz * pz);
        _radiusTotal += radius;
        _radiusMin = Math.Min(_radiusMin, radius);
        _radiusMax = Math.Max(_radiusMax, radius);
        _rows++;
    }

    public CanonicalSphereState Finish() => new(
        $"{Avalanche(_hash0):X16}{Avalanche(_hash1):X16}",
        _rows,
        _nonFinite,
        _rows == 0 ? 0 : _radiusTotal / _rows,
        _rows == 0 ? 0 : _radiusMin,
        _rows == 0 ? 0 : _radiusMax,
        _checksum);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddBits(ref ulong row0, ref ulong row1, int bits)
    {
        uint value = unchecked((uint)bits);
        row0 = unchecked((row0 ^ value) * 1099511628211UL);
        row1 = unchecked((row1 + value + 0x9E3779B9u) * 0xD6E8FEB86659FD93UL);
    }

    private static ulong Avalanche(ulong value)
    {
        value ^= value >> 32;
        value *= 0xD6E8FEB86659FD93UL;
        value ^= value >> 32;
        value *= 0xD6E8FEB86659FD93UL;
        return value ^ (value >> 32);
    }
}

public static class SphereReportMath
{
    public static SphereSystemTiming BuildSystemTiming(
        string name,
        long rowsPerTick,
        long bytesPerEntity,
        ReadOnlySpan<long> stopwatchTicks)
    {
        TimingDistribution timing = TimingDistribution.FromStopwatchTicks(stopwatchTicks);
        double nsPerRow = rowsPerTick == 0 ? 0 : timing.MeanMicroseconds * 1_000d / rowsPerTick;
        double bytesPerSecond = timing.MeanMicroseconds <= 0
            ? 0
            : rowsPerTick * bytesPerEntity * 1_000_000d / timing.MeanMicroseconds;
        return new SphereSystemTiming(
            name, rowsPerTick, bytesPerEntity, timing, nsPerRow,
            bytesPerSecond / (1024d * 1024d * 1024d));
    }
}
