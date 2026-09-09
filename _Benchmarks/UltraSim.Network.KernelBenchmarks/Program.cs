using System.Diagnostics;
using System.Text.Json;
using UltraSim.Network;

int warmups = ReadInt(args, "--warmups=", 4);
int iterations = ReadInt(args, "--iterations=", 25);
int operations = ReadInt(args, "--operations=", 100_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/network/network-n0.2-kernel.json"));

var scenarios = new List<ScenarioResult>
{
    Measure("generated-codec-round-trip", warmups, iterations, operations, new CodecScenario()),
    Measure("packet-pool-owner-cycle", warmups, iterations, operations, new PacketPoolScenario()),
    Measure("producer-ring-cycle", warmups, iterations, operations, new RingScenario()),
    Measure("deterministic-datagram-cycle", warmups, iterations, operations, new DatagramScenario()),
    Measure("connection-open-close-reuse", warmups, iterations, operations, new ConnectionScenario()),
};
var report = new KernelBenchmarkReport(
    "network-n0.2-kernel-foundation-v1",
    DateTimeOffset.UtcNow,
    Environment.Version.ToString(),
    Environment.ProcessorCount,
    warmups,
    iterations,
    operations,
    CoreNetworkProtocol.Manifest.Fingerprint,
    scenarios);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
foreach (ScenarioResult result in scenarios)
    Console.WriteLine($"{result.Name}: mean {result.MeanNanosecondsPerOperation:F3} ns/op, p95 {result.P95NanosecondsPerOperation:F3}, allocated {result.MaximumAllocatedBytes} B");
Console.WriteLine(Path.GetFullPath(output));

static ScenarioResult Measure(
    string name,
    int warmups,
    int iterations,
    int operations,
    IKernelBenchmarkScenario action)
{
    for (int index = 0; index < warmups; index++) _ = action.Run(operations);
    var elapsed = new double[iterations];
    var allocated = new long[iterations];
    ulong checksum = 0;
    for (int index = 0; index < iterations; index++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        checksum ^= action.Run(operations);
        long end = Stopwatch.GetTimestamp();
        allocated[index] = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        elapsed[index] = (end - start) * 1_000_000_000.0 / Stopwatch.Frequency / operations;
    }
    Array.Sort(elapsed);
    ScenarioResult result = new(
        name,
        elapsed.Average(),
        elapsed[PercentileIndex(elapsed.Length, 0.50)],
        elapsed[PercentileIndex(elapsed.Length, 0.95)],
        elapsed[^1],
        allocated.Max(),
        checksum);
    action.Dispose();
    return result;
}

static int PercentileIndex(int count, double percentile) =>
    Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);

static int ReadInt(string[] args, string prefix, int fallback) =>
    int.TryParse(args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..], out int value)
        ? value : fallback;

static string? ReadString(string[] args, string prefix) =>
    args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

internal sealed record KernelBenchmarkReport(
    string Workload,
    DateTimeOffset TimestampUtc,
    string RuntimeVersion,
    int ProcessorCount,
    int Warmups,
    int Iterations,
    int OperationsPerIteration,
    ulong ProtocolFingerprint,
    IReadOnlyList<ScenarioResult> Scenarios);

internal sealed record ScenarioResult(
    string Name,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);

internal interface IKernelBenchmarkScenario : IDisposable
{
    ulong Run(int operations);
}

internal sealed class CodecScenario : IKernelBenchmarkScenario
{
    private readonly byte[] _bytes = new byte[ProtocolProbeMessageCodec.EncodedSize];
    private ProtocolProbeMessage _value = new()
    {
        Nonce = 7,
        CapabilityHash = 11,
        RequestedVersion = 1,
        RequestManifest = true,
        PreferredLane = NetworkLane.Control,
        SignedDelta = -1,
        SignedMode = SignedProtocolProbe.Negative,
    };

    public ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            _value.Nonce = unchecked((ulong)index + 1);
            if (!ProtocolProbeMessageCodec.TryWriteExact(_bytes, in _value) ||
                !ProtocolProbeMessageCodec.TryReadExact(_bytes, out ProtocolProbeMessage decoded))
                throw new InvalidOperationException("Codec benchmark failed.");
            checksum += decoded.Nonce;
        }
        return checksum;
    }
    public void Dispose() { }
}

internal sealed class PacketPoolScenario : IKernelBenchmarkScenario
{
    private readonly PacketBufferPool _pool = new(1, 256);
    public ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            if (!_pool.TryRent(PacketBufferOwner.Application, out PacketBufferToken token))
                throw new InvalidOperationException("Pool rent failed.");
            _pool.GetWritableSpan(token, PacketBufferOwner.Application)[0] = unchecked((byte)index);
            if (!_pool.TryTransfer(token, PacketBufferOwner.Application, PacketBufferOwner.Transport))
                throw new InvalidOperationException("Pool transfer failed.");
            checksum += _pool.GetReadOnlySpan(token, PacketBufferOwner.Transport, 1)[0];
            if (!_pool.TryReturn(token, PacketBufferOwner.Transport))
                throw new InvalidOperationException("Pool return failed.");
        }
        return checksum;
    }
    public void Dispose() => _pool.Dispose();
}

internal sealed class RingScenario : IKernelBenchmarkScenario
{
    private readonly ProducerOwnedRing<PacketBufferToken> _ring = new(
        1, Environment.CurrentManagedThreadId, Environment.CurrentManagedThreadId);
    public ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            var token = new PacketBufferToken(1, 0, unchecked((uint)index + 1));
            if (!_ring.TryEnqueue(token) || !_ring.TryDequeue(out PacketBufferToken read))
                throw new InvalidOperationException("Ring benchmark failed.");
            checksum += read.Generation;
        }
        return checksum;
    }
    public void Dispose() { }
}

internal sealed class DatagramScenario : IKernelBenchmarkScenario
{
    private readonly DeterministicDatagramLink _link = new(1, 64);
    private readonly byte[] _payload = new byte[32];
    private readonly byte[] _receive = new byte[64];
    public ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            _payload[0] = unchecked((byte)index);
            if (_link.A.TrySend(_payload) != DatagramSendStatus.Accepted ||
                !_link.B.TryReceive(_receive, out int length))
                throw new InvalidOperationException("Datagram benchmark failed.");
            checksum += _receive[0] + unchecked((ulong)length);
        }
        return checksum;
    }
    public void Dispose() { }
}

internal sealed class ConnectionScenario : IKernelBenchmarkScenario
{
    private readonly NetworkConnectionTable _connections = new(1);
    public ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int index = 0; index < operations; index++)
        {
            if (!_connections.TryOpenValidated(out ConnectionId connection) ||
                !_connections.TryAdvance(connection, NetworkConnectionState.Closed) ||
                !_connections.TryReleaseClosed(connection))
                throw new InvalidOperationException("Connection benchmark failed.");
            checksum += connection.Generation;
        }
        return checksum;
    }
    public void Dispose() { }
}
