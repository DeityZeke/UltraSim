using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;

internal static class Pf5R9PacketProducerAudit
{
    private const int Seed = 0x5F90_9009;
    private const string StartingHead = "1cf51fc833b7c12b9b7ea971a8201ba2fd0663d3";
    private const string ReplicationSemanticCandidate =
        "14e729a6ce69179cfbe4b9a3a4c4cdfa21797fa9";
    private const string NonceRepairCommit = "c95c77bf";
    private const string EnqueueMeasurementCommit = "f5e5bf64";
    private const string SlabRepairCommit = "e9997ed5";

    internal static void Run(string[] args)
    {
        bool focused = args.Contains("--focused", StringComparer.Ordinal);
        int warmup = ReadInt(args, "--warmup", focused ? 6 : 60);
        int ticks = ReadInt(args, "--ticks", focused ? 8 : 270);
        int repetitions = ReadInt(args, "--repetitions", focused ? 1 : 3);
        int diagnosticTicks = ReadInt(args, "--diagnostic-ticks", focused ? 8 : 30);
        int primitiveIterations = ReadInt(
            args, "--primitive-iterations", focused ? 256 : 20_000);
        string output = ReadText(args, "--output") ??
            "_Benchmarks/results/pf5-r9-packet-producer-audit.raw.json";
        if (warmup < 1 || ticks < 8 || repetitions < 1 || diagnosticTicks < 4 ||
            primitiveIterations < 64)
            throw new ArgumentOutOfRangeException(nameof(args));

        Prime();
        var timing = new List<Pf5R9TimingRow>();
        (string Label, int Population, bool High)[] cases = focused
            ? [("r32-high", 32, true)]
            :
            [
                ("r128-high", 128, true),
                ("r300-low", 300, false),
                ("r300-high", 300, true),
                ("r350-high", 350, true),
            ];
        for (int repetition = 0; repetition < repetitions; repetition++)
        {
            bool onFirst = (repetition & 1) != 0;
            foreach ((string label, int population, bool high) in cases)
            foreach (bool timingEnabled in onFirst ? new[] { true, false } : new[] { false, true })
            {
                Console.WriteLine($"PF5-R9 {label} timing={(timingEnabled ? "on" : "off")} " +
                    $"repeat={repetition + 1}");
                timing.Add(new(label, repetition, timingEnabled,
                    RunHarness(CreateRated(population, high), warmup, ticks,
                        GameTelemetryLevel.Off, networkStageTiming: false,
                        backgroundTiming: timingEnabled, retainRaw: true)));
            }
        }
        RequireTimingParity(timing);

        var attribution = new List<Pf5R9AttributionRow>();
        (string Label, int Population, bool High)[] attributionCases = focused
            ? [("r32-high", 32, true)]
            : [("r128-high", 128, true), ("r300-high", 300, true)];
        foreach ((string label, int population, bool high) in attributionCases)
        {
            Console.WriteLine($"PF5-R9 {label} diagnostic attribution");
            P03WholeFlowRow row = RunHarness(
                CreateRated(population, high), Math.Min(warmup, 12), diagnosticTicks,
                GameTelemetryLevel.Detailed, networkStageTiming: true,
                backgroundTiming: false, retainRaw: true);
            attribution.Add(new(label, row, BuildAttribution(row)));
        }

        int relationships = focused ? 16 : 512;
        int[] groups = focused ? [1, 4, 16] : [1, 8, 64, 512];
        var topology = new List<Pf5R9TopologyRow>(groups.Length);
        foreach (int groupCount in groups)
        {
            Console.WriteLine($"PF5-R9 fixed-delivery topology {groupCount}x" +
                $"{relationships / groupCount}");
            P03NetworkWorkload workload = P03NetworkWorkload.
                ChunkFixedDeliveryTopology(relationships, groupCount).
                WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
            P03WholeFlowRow row = RunHarness(
                workload, Math.Min(warmup, 20), focused ? ticks : 60,
                GameTelemetryLevel.Off, networkStageTiming: false,
                backgroundTiming: false, retainRaw: true);
            topology.Add(new(groupCount, relationships / groupCount, row));
        }
        RequireFixedDelivery(topology);

        Pf5R9PrimitiveResult[] primitives = RunPrimitives(primitiveIterations);
        var report = new Pf5R9Report(
            "ultrasim-concept-demo-pf5-r9-packet-producer-audit-v1",
            DateTimeOffset.UtcNow,
            StartingHead,
            NonceRepairCommit,
            EnqueueMeasurementCommit,
            SlabRepairCommit,
            ReadGitSha(),
            ReplicationSemanticCandidate,
            Environment.Version.ToString(),
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            Environment.ProcessorCount,
            ReadCpuModel(),
            Stopwatch.Frequency,
            Seed,
            focused,
            warmup,
            ticks,
            repetitions,
            diagnosticTicks,
            "ChunkPublication; compatible canonical-body sharing; CurrentBundled; " +
                "multi-entry message-148 LZ4; singleton compression disabled; cadence 1/2/4; " +
                "CrossLane cap two; BackgroundPlatformBatch; manifest v29; transport revision 2; " +
                "60 Hz; product cap unchanged at 64",
            "ServerOwnedRemoteReplicationBudget is the paired same-tick sum of source " +
                "publication preparation, recipient commit observation, and Network plus remote " +
                "replication. Detailed decomposition retains signed parent-minus-child residuals.",
            "ProducerEnqueue is the accepted queue-mutation core and excludes wake/recording. " +
                "ProducerEnqueueComplete is an enclosing caller timer through the worker wake; " +
                "its count/total are measurement-window deltas while histogram percentiles are " +
                "lifetime-scoped bounded values.",
            timing.ToArray(), attribution.ToArray(), topology.ToArray(), primitives,
            [
                "Confirmed correctness defect repaired: each AES-GCM invocation now irrevocably " +
                    "consumes its epoch/packet nonce before endpoint admission.",
                "Confirmed measurement omission repaired: complete enqueue includes fault check, " +
                    "refusal, accepted-core diagnostics, and worker wake, with outcome splits.",
                "Confirmed performance defect repaired: Off/Basic skip Detailed-only typed bundle " +
                    "record slab clearing.",
                "No speculative queue reservation, ciphertext sharing, proof weakening, cadence, " +
                    "AOI, compression, aggregation, or participant-policy change was adopted.",
            ]);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, JsonSerializer.Serialize(report,
            new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);
        Console.WriteLine(Path.GetFullPath(output));
    }

    private static P03NetworkWorkload CreateRated(int recipients, bool high)
    {
        int dirty = high
            ? Math.Max(20, (int)Math.Round(recipients * .35, MidpointRounding.AwayFromZero))
            : 20;
        return P03NetworkWorkload.ChunkMatchedControl(recipients, 1_000, dirty)
            .WithServerTickAndAoICadence(16, P03AoICadencePolicy.Selected124);
    }

    private static P03WholeFlowRow RunHarness(
        P03NetworkWorkload workload,
        int warmup,
        int ticks,
        GameTelemetryLevel gameTelemetry,
        bool networkStageTiming,
        bool backgroundTiming,
        bool retainRaw)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        using var harness = new P03WholeFlowHarness(
            workload, P03NetworkProfile.ChunkPublication, 2,
            stageTimingEnabled: networkStageTiming,
            compression: RemoteChunkBundleCompressionPolicy.Selected,
            singletonDeltaCompression: RemoteChunkSingletonDeltaCompression.Disabled,
            bodySharing: RemoteChunkDeltaBodySharingMode.Enabled,
            historyAudit: false,
            legacyRangeCensus: false,
            authorityThroughNetworkTimingEnabled: true,
            serverOwnedRemoteTimingEnabled: true,
            chunkReadModel: PreparedChunkPublicationReadModel.PreparedLatestState,
            socketIoMode: SharedUdpSocketIoMode.BackgroundPlatformBatch,
            backgroundEgressTimingEnabled: backgroundTiming,
            retainRawSamples: retainRaw,
            gameTelemetryLevel: gameTelemetry);
        P03WholeFlowRow row = harness.Run(warmup, ticks);
        Require(row.Converged, $"PF5-R9 row did not converge: {workload.Id}.");
        Require(row.Transport.CapacityDrops == 0,
            $"PF5-R9 ordinary row reached capacity: {workload.Id}.");
        Require(row.ProfileCounters.PreparedPublication.HistoryDerivedSteadyStateCalls == 0,
            "PF5-R9 left the accepted prepared latest-state path.");
        return row;
    }

    private static Pf5R9Attribution BuildAttribution(P03WholeFlowRow row)
    {
        P03WholeFlowRawSamples samples = row.RawSamples ??
            throw new InvalidOperationException("PF5-R9 attribution requires paired raw samples.");
        double parent = samples.NetworkAndRemoteReplicationMilliseconds.Sum();
        double schedule = samples.GameScheduleAndCodecQueueMilliseconds.Sum();
        double proof = samples.ServerGameProofApplyMilliseconds.Sum();
        double outbound = samples.ServerOutboundProtocolMilliseconds.Sum();
        double inbound = samples.ServerInboundProtocolMilliseconds.Sum();
        double residual = samples.NetworkProtocolAndOtherMilliseconds.Sum();
        double reconciled = schedule + proof + outbound + inbound + residual;
        Require(Math.Abs(parent - reconciled) < 0.000_001,
            "PF5-R9 exclusive attribution did not reconcile with its parent.");
        return new(parent, schedule, proof, outbound, inbound, residual,
            samples.NetworkProtocolAndOtherMilliseconds.Min(),
            samples.NetworkProtocolAndOtherMilliseconds.Max(),
            row.Timing.Protocol.Server,
            row.BackgroundEgress);
    }

    private static void RequireTimingParity(List<Pf5R9TimingRow> rows)
    {
        foreach (IGrouping<(string Label, int Repetition), Pf5R9TimingRow> group in
                 rows.GroupBy(static row => (row.Label, row.Repetition)))
        {
            Pf5R9TimingRow[] pair = group.ToArray();
            Require(pair.Length == 2 && pair.Any(static row => row.BackgroundTimingEnabled) &&
                pair.Any(static row => !row.BackgroundTimingEnabled),
                "PF5-R9 timing toggle did not produce a matched pair.");
            P03WholeFlowRow first = pair[0].Row;
            P03WholeFlowRow second = pair[1].Row;
            Require(StringComparer.Ordinal.Equals(first.SemanticDigest, second.SemanticDigest) &&
                first.ProfileCounters.SuccessfulSemanticPublications ==
                    second.ProfileCounters.SuccessfulSemanticPublications &&
                first.ProfileCounters.ProofEntries == second.ProfileCounters.ProofEntries &&
                first.ProfileCounters.GameMessagesQueued ==
                    second.ProfileCounters.GameMessagesQueued &&
                first.Transport.GamePayloadBytes == second.Transport.GamePayloadBytes,
                $"PF5-R9 timing toggle changed semantic work for {group.Key}.");
        }
    }

    private static void RequireFixedDelivery(List<Pf5R9TopologyRow> rows)
    {
        Require(rows.Count > 0, "PF5-R9 fixed-delivery matrix is empty.");
        P03WholeFlowRow expected = rows[0].Row;
        foreach (Pf5R9TopologyRow candidate in rows)
        {
            P03WholeFlowRow row = candidate.Row;
            Require(row.ProfileCounters.SuccessfulSemanticPublications ==
                    expected.ProfileCounters.SuccessfulSemanticPublications &&
                row.ProfileCounters.ProofEntries == expected.ProfileCounters.ProofEntries &&
                row.ProfileCounters.GameMessagesQueued ==
                    expected.ProfileCounters.GameMessagesQueued &&
                row.Transport.GamePayloadBytes == expected.Transport.GamePayloadBytes,
                $"PF5-R9 fixed-delivery work changed at {candidate.Groups} groups.");
        }
    }

    private static Pf5R9PrimitiveResult[] RunPrimitives(int iterations)
    {
        int[] sizes = [64, 128, 256, 512, 1_024,
            NetworkSecureDatagramFraming.MaximumPlaintextBytes(1_472)];
        var results = new List<Pf5R9PrimitiveResult>(sizes.Length * 2);
        foreach (int size in sizes)
        {
            results.Add(BenchmarkEncryption(size, iterations));
            results.Add(BenchmarkProtocolProducer(size, iterations));
        }
        return results.ToArray();
    }

    private static Pf5R9PrimitiveResult BenchmarkEncryption(int bytes, int iterations)
    {
        byte[] secret = Enumerable.Range(1, 32).Select(static value => (byte)value).ToArray();
        byte[] plaintext = new byte[bytes];
        byte[] ciphertext = new byte[bytes];
        byte[] tag = new byte[NetworkSecureDatagramFraming.TagBytes];
        byte[] associated = new byte[NetworkSecureDatagramFraming.HeaderBytes];
        var samples = new double[iterations];
        using var cryptography = new NetworkDirectionalCryptography(
            secret, clientToServer: false, new SecurityEpoch(1));
        CryptographicOperations.ZeroMemory(secret);
        for (int index = 0; index < 64; index++)
            cryptography.Encrypt(new PacketNumber(checked((ulong)index)), plaintext,
                ciphertext, tag, associated);
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < iterations; index++)
        {
            long started = Stopwatch.GetTimestamp();
            cryptography.Encrypt(new PacketNumber(checked((ulong)index + 64)), plaintext,
                ciphertext, tag, associated);
            samples[index] = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
        }
        long allocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        return new("NetworkDirectionalCryptography.Encrypt", bytes, iterations,
            P03Metric.From(samples), allocation, checked((ulong)iterations),
            bytes * (long)iterations,
            "Prepared directional AES-GCM instance; unique packet nonce for every warmup and " +
                "measured invocation; excludes framing, queueing, and endpoint submission.");
    }

    private static Pf5R9PrimitiveResult BenchmarkProtocolProducer(int bytes, int iterations)
    {
        int maximumPayload = NetworkSecureDatagramFraming.MaximumPlaintextBytes(1_472) -
            NetworkProtocolDataHeader.EncodedBytes;
        int payloadBytes = Math.Min(bytes, maximumPayload);
        var manifest = NetworkProtocolManifest.Create(91,
        [
            new NetworkMessageDescriptor(new NetworkMessageId(1), "pf5-r9-world", 1,
                NetworkLane.WorldState, maximumPayload, 0x5F90_9009),
        ]);
        var limits = new NetworkProtocolLimits(
            4, 4, maximumPayload, 2, 16, 4, 4, 2, 16, 4, 8, 4,
            MaximumUnreliableFramesPerDatagram: 2);
        var datagrams = new DeterministicDatagramLimits(16, 1_472);
        using var endpoint = new AcceptingDatagramEndpoint();
        byte[] secret = Enumerable.Range(1, 32).Select(static value => (byte)(value * 3)).ToArray();
        using var peer = new NetworkProtocolPeer(
            endpoint, manifest, limits, datagrams, new ConnectionId(1, 1),
            new SecurityEpoch(1), secret, sendsClientToServer: false);
        CryptographicOperations.ZeroMemory(secret);
        byte[] payload = new byte[payloadBytes];
        var samples = new double[iterations];
        for (int index = 0; index < 64; index++)
        {
            Require(peer.TryQueue(new NetworkMessageId(1), payload) == NetworkQueueStatus.Accepted,
                "PF5-R9 producer warmup queue refused.");
            peer.Emit(checked((ulong)index));
        }
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < iterations; index++)
        {
            payload[0] = unchecked((byte)index);
            long started = Stopwatch.GetTimestamp();
            Require(peer.TryQueue(new NetworkMessageId(1), payload) == NetworkQueueStatus.Accepted,
                "PF5-R9 producer queue refused.");
            peer.Emit(checked((ulong)index + 64));
            samples[index] = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
        }
        long allocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        NetworkProtocolSnapshot snapshot = peer.GetSnapshot();
        return new("NetworkProtocolPeer.TryQueue+Emit", payloadBytes, iterations,
            P03Metric.From(samples), allocation, snapshot.EncryptionInvocations,
            checked((long)snapshot.EncryptionPlaintextBytesAttempted),
            "Actual lane admission, selection, frame/plaintext assembly, secure packet construction, " +
                "AES-GCM, and accepted endpoint call; excludes shared-listener background handoff.");
    }

    private static void Prime()
    {
        using var harness = new P03WholeFlowHarness(
            CreateRated(32, high: false), P03NetworkProfile.ChunkPublication, 2,
            stageTimingEnabled: false,
            compression: RemoteChunkBundleCompressionPolicy.Selected,
            bodySharing: RemoteChunkDeltaBodySharingMode.Enabled,
            serverOwnedRemoteTimingEnabled: true,
            socketIoMode: SharedUdpSocketIoMode.BackgroundPlatformBatch,
            backgroundEgressTimingEnabled: false);
        _ = harness.Run(4, 4);
    }

    private static int ReadInt(string[] args, string name, int fallback)
    {
        string? text = ReadText(args, name);
        return text is null ? fallback : int.Parse(
            text, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static string? ReadText(string[] args, string name)
    {
        for (int index = 0; index + 1 < args.Length; index++)
            if (StringComparer.Ordinal.Equals(args[index], name)) return args[index + 1];
        return null;
    }

    private static string ReadGitSha()
    {
        var info = new ProcessStartInfo("git", "rev-parse HEAD")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
        };
        using Process process = Process.Start(info) ??
            throw new InvalidOperationException("Could not start git.");
        string value = process.StandardOutput.ReadToEnd().Trim();
        process.WaitForExit();
        return value;
    }

    private static string ReadCpuModel()
    {
        const string path = "/proc/cpuinfo";
        if (!File.Exists(path)) return "unknown";
        string? line = File.ReadLines(path).FirstOrDefault(
            static value => value.StartsWith("model name", StringComparison.Ordinal));
        return line is null ? "unknown" : line[(line.IndexOf(':') + 1)..].Trim();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class AcceptingDatagramEndpoint : INetworkDatagramEndpoint, IDisposable
    {
        public DatagramSendStatus TrySend(ReadOnlySpan<byte> datagram) =>
            DatagramSendStatus.Accepted;

        public bool TryReceive(Span<byte> destination, out int length)
        {
            length = 0;
            return false;
        }

        public void Dispose()
        {
        }
    }
}

internal sealed record Pf5R9Report(
    string Schema,
    DateTimeOffset CapturedAtUtc,
    string StartingHead,
    string NonceRepairCommit,
    string EnqueueMeasurementCommit,
    string SlabRepairCommit,
    string MeasurementHead,
    string ReplicationSemanticCandidate,
    string Runtime,
    string OperatingSystem,
    string Architecture,
    int ProcessorCount,
    string CpuModel,
    long StopwatchFrequency,
    int Seed,
    bool Focused,
    int WarmupTicks,
    int MeasuredTicks,
    int Repetitions,
    int DiagnosticTicks,
    string ProductionPolicy,
    string TimingBoundary,
    string EgressTimingBoundary,
    Pf5R9TimingRow[] TimingToggle,
    Pf5R9AttributionRow[] Attribution,
    Pf5R9TopologyRow[] FixedDeliveryTopology,
    Pf5R9PrimitiveResult[] Primitives,
    string[] Dispositions);

internal readonly record struct Pf5R9TimingRow(
    string Label,
    int Repetition,
    bool BackgroundTimingEnabled,
    P03WholeFlowRow Row);

internal readonly record struct Pf5R9AttributionRow(
    string Label,
    P03WholeFlowRow Row,
    Pf5R9Attribution Attribution);

internal readonly record struct Pf5R9Attribution(
    double ParentMilliseconds,
    double ScheduleCodecQueueMilliseconds,
    double ProofApplicationMilliseconds,
    double ServerOutboundProtocolMilliseconds,
    double ServerInboundProtocolMilliseconds,
    double SignedResidualMilliseconds,
    double MinimumPerTickResidualMilliseconds,
    double MaximumPerTickResidualMilliseconds,
    P03ProtocolStage ProtocolOperations,
    P03BackgroundEgressSummary BackgroundEgress);

internal readonly record struct Pf5R9TopologyRow(
    int Groups,
    int RecipientsPerGroup,
    P03WholeFlowRow Row);

internal readonly record struct Pf5R9PrimitiveResult(
    string Operation,
    int PayloadBytes,
    int Iterations,
    P03Metric Nanoseconds,
    long WarmedManagedBytes,
    ulong EncryptionInvocations,
    long PlaintextBytesAttempted,
    string Boundary);
