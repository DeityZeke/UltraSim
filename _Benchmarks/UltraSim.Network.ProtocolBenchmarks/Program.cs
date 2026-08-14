using System.Diagnostics;
using System.Text.Json;
using UltraSim.Network;

int warmups = ReadInt(args, "--warmups=", 4);
int iterations = ReadInt(args, "--iterations=", 25);
int operations = ReadInt(args, "--operations=", 10_000);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/network/network-n0.3-protocol.json"));

NetworkProtocolManifest manifest = CreateManifest();
var scenarios = new List<ProtocolScenarioResult>
{
    Measure("secure-unreliable-state-round-trip", warmups, iterations, operations,
        new DeliveryScenario(manifest, reliable: false, payloadBytes: 32)),
    Measure("secure-reliable-control-round-trip", warmups, iterations, operations,
        new DeliveryScenario(manifest, reliable: true, payloadBytes: 32)),
    Measure("secure-reliable-bulk-4096", warmups, iterations, Math.Max(100, operations / 10),
        new DeliveryScenario(manifest, reliable: true, payloadBytes: 4_096, bulk: true)),
};
ImpairmentEvidence impairment = MeasureImpairment(manifest);
var report = new ProtocolBenchmarkReport(
    "network-n0.3-secure-reliable-protocol-v1",
    DateTimeOffset.UtcNow,
    Environment.Version.ToString(),
    Environment.ProcessorCount,
    warmups,
    iterations,
    operations,
    manifest.Fingerprint,
    scenarios,
    impairment);
Directory.CreateDirectory(Path.GetDirectoryName(output)!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
foreach (ProtocolScenarioResult result in scenarios)
    Console.WriteLine($"{result.Name}: mean {result.MeanNanosecondsPerOperation:F3} ns/op, " +
        $"p95 {result.P95NanosecondsPerOperation:F3}, {result.PayloadBytes} payload B, " +
        $"allocated {result.MaximumAllocatedBytes} B");
Console.WriteLine($"impairment: {impairment.Messages} reliable messages in {impairment.ProtocolSteps} steps, " +
    $"{impairment.Retransmissions} retransmissions, {impairment.AuthenticationFailures} authentication failures");
Console.WriteLine(Path.GetFullPath(output));

static ProtocolScenarioResult Measure(
    string name,
    int warmups,
    int iterations,
    int operations,
    IDeliveryScenario scenario)
{
    for (int index = 0; index < warmups; index++) _ = scenario.Run(operations);
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
        checksum ^= scenario.Run(operations);
        long end = Stopwatch.GetTimestamp();
        allocated[index] = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        elapsed[index] = (end - start) * 1_000_000_000.0 / Stopwatch.Frequency / operations;
    }
    Array.Sort(elapsed);
    ProtocolScenarioResult result = new(
        name,
        scenario.PayloadBytes,
        elapsed.Average(),
        elapsed[PercentileIndex(elapsed.Length, 0.50)],
        elapsed[PercentileIndex(elapsed.Length, 0.95)],
        elapsed[^1],
        allocated.Max(),
        checksum);
    scenario.Dispose();
    return result;
}

static ImpairmentEvidence MeasureImpairment(NetworkProtocolManifest manifest)
{
    ProtocolLoopback loopback = ProtocolBenchmarkFixture.Create(manifest, new(0xA11CE, 200, 100, 75, 5));
    using (loopback)
    {
        Span<byte> payload = stackalloc byte[16];
        const int messages = 100;
        ulong start = loopback.GetSnapshot().Step;
        for (int message = 0; message < messages; message++)
        {
            var writer = new NetworkPacketWriter(payload);
            if (!writer.TryWriteUInt64((ulong)message) ||
                loopback.Client.TryQueue(new NetworkMessageId(1), payload) != NetworkQueueStatus.Accepted)
                throw new InvalidOperationException("The impairment fixture could not queue its message.");
            Drive(loopback, () => loopback.Server.ReceivedQueued != 0, 20_000);
            if (!loopback.Server.TryConsumeReceived()) throw new InvalidOperationException();
            Drive(loopback, () => loopback.Client.ReliableQueued == 0, 20_000);
        }
        NetworkProtocolSnapshot client = loopback.Client.GetSnapshot();
        NetworkProtocolSnapshot server = loopback.Server.GetSnapshot();
        return new(messages, loopback.GetSnapshot().Step - start,
            client.ReliableRetransmissions, server.AuthenticationFailures, server.ReplayRejections);
    }
}

static NetworkProtocolManifest CreateManifest() => NetworkProtocolManifest.Create(3, new[]
{
    new NetworkMessageDescriptor(new NetworkMessageId(1), "benchmark.control", 1,
        NetworkLane.Control, 8_192, 1),
    new NetworkMessageDescriptor(new NetworkMessageId(2), "benchmark.state", 1,
        NetworkLane.OwnerState, 8_192, 2),
    new NetworkMessageDescriptor(new NetworkMessageId(3), "benchmark.bulk", 1,
        NetworkLane.Bulk, 8_192, 3),
});

static void Drive(ProtocolLoopback loopback, Func<bool> complete, int maximumSteps)
{
    for (int step = 0; step < maximumSteps; step++)
    {
        if (complete()) return;
        loopback.Pump();
    }
    throw new InvalidOperationException("The protocol benchmark did not converge.");
}

static int PercentileIndex(int count, double percentile) =>
    Math.Clamp((int)Math.Ceiling(count * percentile) - 1, 0, count - 1);

static int ReadInt(string[] args, string prefix, int fallback) =>
    int.TryParse(args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..], out int value)
        ? value : fallback;

static string? ReadString(string[] args, string prefix) =>
    args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

internal sealed record ProtocolBenchmarkReport(
    string Workload,
    DateTimeOffset TimestampUtc,
    string RuntimeVersion,
    int ProcessorCount,
    int Warmups,
    int Iterations,
    int DefaultOperationsPerIteration,
    ulong ProtocolFingerprint,
    IReadOnlyList<ProtocolScenarioResult> Scenarios,
    ImpairmentEvidence ImpairmentEvidence);

internal sealed record ProtocolScenarioResult(
    string Name,
    int PayloadBytes,
    double MeanNanosecondsPerOperation,
    double P50NanosecondsPerOperation,
    double P95NanosecondsPerOperation,
    double MaximumNanosecondsPerOperation,
    long MaximumAllocatedBytes,
    ulong Checksum);

internal sealed record ImpairmentEvidence(
    int Messages,
    ulong ProtocolSteps,
    ulong Retransmissions,
    ulong AuthenticationFailures,
    ulong ReplayRejections);

internal interface IDeliveryScenario : IDisposable
{
    int PayloadBytes { get; }
    ulong Run(int operations);
}

internal sealed class DeliveryScenario : IDeliveryScenario
{
    private readonly ProtocolLoopback _loopback;
    private readonly byte[] _payload;
    private readonly NetworkMessageId _messageId;
    private readonly bool _reliable;

    internal DeliveryScenario(NetworkProtocolManifest manifest, bool reliable, int payloadBytes, bool bulk = false)
    {
        _loopback = ProtocolBenchmarkFixture.Create(manifest);
        _payload = new byte[payloadBytes];
        _messageId = new NetworkMessageId(bulk ? (ushort)3 : reliable ? (ushort)1 : (ushort)2);
        _reliable = reliable;
    }

    public int PayloadBytes => _payload.Length;

    public ulong Run(int operations)
    {
        ulong checksum = 0;
        for (int operation = 0; operation < operations; operation++)
        {
            _payload[0] = unchecked((byte)operation);
            if (_loopback.Client.TryQueue(_messageId, _payload) != NetworkQueueStatus.Accepted)
                throw new InvalidOperationException("The timed protocol message could not be queued.");
            DriveUntilReceived();
            if (!_loopback.Server.TryPeekReceived(out NetworkDeliveredMessage delivered, out ReadOnlySpan<byte> payload) ||
                payload.Length != _payload.Length || payload[0] != _payload[0])
                throw new InvalidOperationException("The timed protocol message was not delivered exactly.");
            byte first = payload[0];
            if (!_loopback.Server.TryConsumeReceived()) throw new InvalidOperationException();
            if (_reliable)
                DriveUntilReliableClear();
            checksum += first + unchecked((ulong)delivered.PayloadLength);
        }
        return checksum;
    }

    private void DriveUntilReceived()
    {
        for (int step = 0; step < 1_000; step++)
        {
            if (_loopback.Server.ReceivedQueued != 0) return;
            _loopback.Pump();
        }
        throw new InvalidOperationException("The timed protocol receive did not converge.");
    }

    private void DriveUntilReliableClear()
    {
        for (int step = 0; step < 1_000; step++)
        {
            if (_loopback.Client.ReliableQueued == 0) return;
            _loopback.Pump();
        }
        throw new InvalidOperationException("The timed reliable acknowledgement did not converge.");
    }

    public void Dispose() => _loopback.Dispose();
}

internal static class ProtocolBenchmarkFixture
{
    internal static ProtocolLoopback Create(
        NetworkProtocolManifest manifest,
        NetworkImpairmentOptions impairment = default)
    {
        var limits = new NetworkProtocolLimits(32, 32, 8_192, 64, 512, 64, 2, 128, 8, 64);
        var datagrams = new DeterministicDatagramLimits(64, 1_200);
        if (!ProtocolLoopback.TryCreate(limits, datagrams, impairment,
                out ProtocolLoopback? loopback, out _, manifest))
            throw new InvalidOperationException("The protocol benchmark handshake did not converge.");
        return loopback!;
    }
}
