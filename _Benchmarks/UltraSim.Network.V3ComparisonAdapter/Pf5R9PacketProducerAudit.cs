using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using UltraSim.Game.Protocol;
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
        bool diagnosticOnly = args.Contains("--diagnostic-only", StringComparer.Ordinal);
        bool explicitlyUnique = args.Contains("--unique-epochs", StringComparer.Ordinal);
        bool sharedEpochControl = args.Contains("--shared-epoch-control", StringComparer.Ordinal);
        if (explicitlyUnique && sharedEpochControl)
            throw new ArgumentException(
                "--unique-epochs and --shared-epoch-control are mutually exclusive.", nameof(args));
        // Independent application lifetimes are the production-shaped default. A shared epoch is
        // retained only as an explicitly labelled synthetic compatibility control.
        bool uniqueApplicationEpochs = !sharedEpochControl;
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
        (string Label, int Population, bool High)[] cases = diagnosticOnly
            ? []
            : focused
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
                        backgroundTiming: timingEnabled, retainRaw: true,
                        uniqueApplicationEpochs: uniqueApplicationEpochs)));
            }
        }
        if (!diagnosticOnly) RequireTimingParity(timing);

        var attribution = new List<Pf5R9AttributionRow>();
        (string Label, int Population, bool High)[] attributionCases = focused
            ? [("r32-high", 32, true)]
            : [("r128-high", 128, true), ("r300-high", 300, true)];
        foreach ((string label, int population, bool high) in attributionCases)
        {
            Console.WriteLine($"PF5-R9 {label} diagnostic attribution");
            var compressionInputs = new Pf5R9CompressionInputCensus();
            P03WholeFlowRow row = RunHarness(
                CreateRated(population, high), Math.Min(warmup, 12), diagnosticTicks,
                GameTelemetryLevel.Detailed, networkStageTiming: true,
                backgroundTiming: false, retainRaw: true,
                compressionInputObserver: compressionInputs,
                uniqueApplicationEpochs: uniqueApplicationEpochs);
            attribution.Add(new(label, row, BuildAttribution(row), compressionInputs.Capture()));
        }

        var sparseAttribution = new List<Pf5R9SparseAttributionRow>();
        if (!diagnosticOnly)
            foreach ((string label, int population, bool high) in attributionCases)
            {
                Console.WriteLine($"PF5-R9 {label} sparse network attribution");
                P03WholeFlowRow networkOnly = RunHarness(
                    CreateRated(population, high), Math.Min(warmup, 12), diagnosticTicks,
                    GameTelemetryLevel.Off, networkStageTiming: true,
                    backgroundTiming: false, retainRaw: true,
                    uniqueApplicationEpochs: uniqueApplicationEpochs);
                Console.WriteLine($"PF5-R9 {label} sparse Game attribution control");
                P03WholeFlowRow gameOff = RunHarness(
                    CreateRated(population, high), Math.Min(warmup, 12), diagnosticTicks,
                    GameTelemetryLevel.Off, networkStageTiming: false,
                    backgroundTiming: false, retainRaw: true,
                    uniqueApplicationEpochs: uniqueApplicationEpochs);
                P03WholeFlowRow gameDetailed = RunHarness(
                    CreateRated(population, high), Math.Min(warmup, 12), diagnosticTicks,
                    GameTelemetryLevel.Detailed, networkStageTiming: false,
                    backgroundTiming: false, retainRaw: true,
                    uniqueApplicationEpochs: uniqueApplicationEpochs);
                sparseAttribution.Add(new(
                    label,
                    networkOnly,
                    BuildAttribution(networkOnly),
                    gameOff,
                    gameDetailed));
            }

        int relationships = focused ? 16 : 512;
        int[] groups = diagnosticOnly ? [] : focused ? [1, 4, 16] : [1, 8, 64, 512];
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
                backgroundTiming: false, retainRaw: true,
                uniqueApplicationEpochs: uniqueApplicationEpochs);
            topology.Add(new(groupCount, relationships / groupCount, row));
        }
        if (!diagnosticOnly) RequireFixedDelivery(topology);

        Pf5R9PrimitiveResult[] primitives = diagnosticOnly
            ? []
            : RunPrimitives(primitiveIterations);
        Pf5R9HandoffPrimitiveResult[] handoffPrimitives = diagnosticOnly
            ? []
            : RunConcurrentHandoffPrimitives(Math.Min(primitiveIterations, 2_048));
        var report = new Pf5R9Report(
            "ultrasim-concept-demo-pf5-r9-packet-producer-audit-v2",
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
            new Pf5R9TimestampConversionProbe(
                450, 1_000_000_000, StopwatchTicksToNanoseconds(450, 1_000_000_000)),
            Seed,
            focused,
            uniqueApplicationEpochs,
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
            timing.ToArray(), attribution.ToArray(), sparseAttribution.ToArray(),
            topology.ToArray(), primitives,
            handoffPrimitives,
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
        bool retainRaw,
        IRemoteChunkCompressionInputObserver? compressionInputObserver = null,
        bool uniqueApplicationEpochs = false)
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
            gameTelemetryLevel: gameTelemetry,
            compressionInputObserver: compressionInputObserver,
            uniqueApplicationEpochs: uniqueApplicationEpochs);
        P03WholeFlowRow row = harness.Run(warmup, ticks);
        Require(row.Converged, $"PF5-R9 row did not converge: {workload.Id}.");
        Require(row.Transport.CapacityDrops == 0,
            $"PF5-R9 ordinary row reached capacity: {workload.Id}.");
        Require(row.ProfileCounters.PreparedPublication.HistoryDerivedSteadyStateCalls == 0,
            "PF5-R9 left the accepted prepared latest-state path.");
        Require(row.ApplicationEpochs.Applicable &&
            row.ApplicationEpochs.RecipientCount == workload.Recipients &&
            row.ApplicationEpochs.DistinctEpochCount ==
                (uniqueApplicationEpochs ? workload.Recipients : 1),
            $"PF5-R9 did not realize the requested application-epoch shape: " +
            $"requestedUnique={uniqueApplicationEpochs} realized={row.ApplicationEpochs}.");
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
                    second.ProfileCounters.GameMessagesQueued,
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
            samples[index] = StopwatchTicksToNanoseconds(Stopwatch.GetTimestamp() - started);
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
            samples[index] = StopwatchTicksToNanoseconds(Stopwatch.GetTimestamp() - started);
        }
        long allocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        NetworkProtocolSnapshot snapshot = peer.GetSnapshot();
        return new("NetworkProtocolPeer.TryQueue+Emit", payloadBytes, iterations,
            P03Metric.From(samples), allocation, snapshot.EncryptionInvocations,
            checked((long)snapshot.EncryptionPlaintextBytesAttempted),
            "Actual lane admission, selection, frame/plaintext assembly, secure packet construction, " +
                "AES-GCM, and accepted endpoint call; excludes shared-listener background handoff.");
    }

    private static Pf5R9HandoffPrimitiveResult[] RunConcurrentHandoffPrimitives(int iterations)
    {
        int[] sizes = [64, 512, UdpProtocolLimits.Default.MaximumDatagramBytes];
        var results = new List<Pf5R9HandoffPrimitiveResult>(sizes.Length * 2);
        foreach (int size in sizes)
        {
            results.Add(BenchmarkConcurrentHandoff(size, iterations, sustained: false));
            results.Add(BenchmarkConcurrentHandoff(size, iterations, sustained: true));
        }
        return results.ToArray();
    }

    private static Pf5R9HandoffPrimitiveResult BenchmarkConcurrentHandoff(
        int datagramBytes,
        int iterations,
        bool sustained)
    {
        const int maximumSessions = 16;
        const int queuedPerSession = 1_024;
        int capacity = checked(maximumSessions * queuedPerSession);
        int preloaded = sustained ? Math.Min(8_192, capacity - iterations - 1) : 0;
        Require(preloaded >= 0, "PF5-R9 concurrent handoff capacity is insufficient.");

        var receiver = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        receiver.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        IPEndPoint remote = (IPEndPoint)receiver.LocalEndPoint!;
        using var receivePump = new Pf5R9UdpReceivePump(receiver);
        using var gate = sustained ? new Pf5R9FirstPhysicalSendGate() : null;
        using var listener = new SharedUdpServerListener(
            UdpProtocolLimits.Default,
            new SharedUdpProtocolLimits(
                maximumSessions,
                queuedPerSession,
                MaximumInboundDatagramsPerPump: 64,
                SocketIoMode: SharedUdpSocketIoMode.BackgroundPlatformBatch,
                MaximumNativeSendBatchDatagrams: 32,
                BackgroundEgressTimingEnabled: false),
            new IPEndPoint(IPAddress.Loopback, 0),
            unknownDatagramHandler: null,
            backgroundEgressTestHook: gate);
        Require(listener.TryRegister(remote, out SharedUdpServerEndpoint? created),
            "PF5-R9 concurrent handoff endpoint registration failed.");
        using SharedUdpServerEndpoint endpoint = created!;
        endpoint.BindConnection(new ConnectionId(1, 1));
        byte[] payload = new byte[datagramBytes];
        var samples = new double[iterations];

        // Warm the copy/accounting/socket path before the measured cohort. The sustained case uses
        // a deliberately retained prefix so the producer and real sender worker overlap without
        // measuring an artificial per-message worker wake.
        if (sustained)
        {
            Require(endpoint.TrySend(payload) == DatagramSendStatus.Accepted,
                "PF5-R9 sustained handoff did not accept its first preload.");
            Require(gate!.Entered.Wait(TimeSpan.FromSeconds(2)),
                "PF5-R9 sustained handoff worker did not reach the controlled send boundary.");
            for (int index = 1; index < preloaded; index++)
                Require(endpoint.TrySend(payload) == DatagramSendStatus.Accepted,
                    "PF5-R9 sustained handoff preload was refused.");
        }
        else
        {
            for (int index = 0; index < 64; index++)
            {
                SharedUdpBackgroundEgressSnapshot before =
                    listener.GetSnapshot().BackgroundEgress;
                Require(endpoint.TrySend(payload) == DatagramSendStatus.Accepted,
                    "PF5-R9 empty-wake handoff warmup was refused.");
                WaitForPhysical(listener, checked(before.PhysicallySentDatagrams + 1));
            }
        }

        SharedUdpBackgroundEgressSnapshot cohortBefore =
            listener.GetSnapshot().BackgroundEgress;
        if (sustained) gate!.Release.Set();
        long allocationBefore = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < iterations; index++)
        {
            if (!sustained)
                WaitForQueueEmpty(listener);
            payload[0] = unchecked((byte)index);
            long started = Stopwatch.GetTimestamp();
            DatagramSendStatus status = endpoint.TrySend(payload);
            samples[index] = StopwatchTicksToNanoseconds(Stopwatch.GetTimestamp() - started);
            Require(status == DatagramSendStatus.Accepted,
                "PF5-R9 concurrent handoff measured enqueue was refused.");
            if (!sustained)
                WaitForPhysical(listener,
                    checked(cohortBefore.PhysicallySentDatagrams + (ulong)index + 1));
        }
        long allocation = GC.GetAllocatedBytesForCurrentThread() - allocationBefore;
        SharedUdpBackgroundEgressSnapshot producerAfter =
            listener.GetSnapshot().BackgroundEgress;
        ulong cohortAccepted = checked((ulong)preloaded + (ulong)iterations);
        WaitForPhysical(listener, checked(cohortBefore.PhysicallySentDatagrams + cohortAccepted));
        SharedUdpBackgroundEgressSnapshot final = listener.GetSnapshot().BackgroundEgress;
        Require(final.AcceptedDatagrams - cohortBefore.AcceptedDatagrams == (ulong)iterations,
            "PF5-R9 handoff producer admission count did not reconcile.");
        Require(final.PhysicallySentDatagrams - cohortBefore.PhysicallySentDatagrams ==
                cohortAccepted,
            "PF5-R9 handoff physical completion cohort did not reconcile.");
        Require(final.CapacityRefusals == cohortBefore.CapacityRefusals,
            "PF5-R9 concurrent handoff reached bounded capacity.");
        bool noAdditionalWake = producerAfter.WorkerWakeups == cohortBefore.WorkerWakeups;
        if (sustained)
            Require(noAdditionalWake,
                "PF5-R9 sustained handoff emptied and re-woke during the producer window.");
        return new(
            sustained ? "sustained-nonempty" : "empty-to-nonempty",
            datagramBytes,
            iterations,
            preloaded,
            P03Metric.From(samples),
            allocation,
            checked(final.AcceptedDatagrams - cohortBefore.AcceptedDatagrams),
            checked(final.PhysicallySentDatagrams - cohortBefore.PhysicallySentDatagrams),
            checked(producerAfter.WorkerWakeups - cohortBefore.WorkerWakeups),
            checked(final.NativeSendCalls - cohortBefore.NativeSendCalls),
            checked(final.ScalarSendCalls - cohortBefore.ScalarSendCalls),
            checked(final.CapacityRefusals - cohortBefore.CapacityRefusals),
            final.HighWaterDatagrams,
            noAdditionalWake,
            "Actual SharedUdpServerEndpoint -> SharedUdpServerListener -> bounded " +
                "SharedUdpBackgroundEgress copy/address/accounting/synchronization path with its " +
                "real sender worker and loopback UDP syscall; producer timing ends at admission " +
                "and excludes asynchronous worker service.");
    }

    private static void WaitForQueueEmpty(SharedUdpServerListener listener)
    {
        long started = Stopwatch.GetTimestamp();
        while (listener.GetSnapshot().BackgroundEgress.CurrentDatagrams != 0)
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(2))
                throw new TimeoutException("PF5-R9 background handoff queue did not empty.");
            Thread.SpinWait(32);
        }
    }

    private static void WaitForPhysical(SharedUdpServerListener listener, ulong target)
    {
        long started = Stopwatch.GetTimestamp();
        while (listener.GetSnapshot().BackgroundEgress.PhysicallySentDatagrams < target)
        {
            if (Stopwatch.GetElapsedTime(started) > TimeSpan.FromSeconds(2))
                throw new TimeoutException("PF5-R9 background handoff did not complete physically.");
            Thread.SpinWait(32);
        }
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

    internal static double StopwatchTicksToNanoseconds(long ticks) =>
        StopwatchTicksToNanoseconds(ticks, Stopwatch.Frequency);

    internal static double StopwatchTicksToNanoseconds(long ticks, long frequency)
    {
        if (ticks < 0) throw new ArgumentOutOfRangeException(nameof(ticks));
        if (frequency <= 0) throw new ArgumentOutOfRangeException(nameof(frequency));
        return ticks * 1_000_000_000d / frequency;
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

    private sealed class Pf5R9FirstPhysicalSendGate :
        ISharedUdpBackgroundEgressTestHook,
        IDisposable
    {
        internal ManualResetEventSlim Entered { get; } = new(false);
        internal ManualResetEventSlim Release { get; } = new(false);

        public void BeforePhysicalSend()
        {
            Entered.Set();
            Release.Wait();
        }

        public bool TryOverrideSend(int requested, out int sent, out int error)
        {
            sent = error = 0;
            return false;
        }

        public void Dispose()
        {
            Release.Set();
            Entered.Dispose();
            Release.Dispose();
        }
    }

    private sealed class Pf5R9UdpReceivePump : IDisposable
    {
        private readonly Socket _socket;
        private readonly Thread _thread;
        private int _stopping;

        internal Pf5R9UdpReceivePump(Socket socket)
        {
            _socket = socket;
            _socket.ReceiveTimeout = 100;
            _thread = new Thread(Run)
            {
                IsBackground = true,
                Name = "PF5-R9 UDP receive pump",
            };
            _thread.Start();
        }

        private void Run()
        {
            byte[] scratch = new byte[ushort.MaxValue];
            while (Volatile.Read(ref _stopping) == 0)
                try
                {
                    _ = _socket.Receive(scratch);
                }
                catch (SocketException exception) when (exception.SocketErrorCode is
                    SocketError.TimedOut or SocketError.WouldBlock or SocketError.Interrupted)
                {
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
            _socket.Dispose();
            if (!_thread.Join(TimeSpan.FromSeconds(2)))
                throw new TimeoutException("PF5-R9 UDP receive pump did not retire.");
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
    Pf5R9TimestampConversionProbe TimestampConversionProbe,
    int Seed,
    bool Focused,
    bool UniqueApplicationEpochs,
    int WarmupTicks,
    int MeasuredTicks,
    int Repetitions,
    int DiagnosticTicks,
    string ProductionPolicy,
    string TimingBoundary,
    string EgressTimingBoundary,
    Pf5R9TimingRow[] TimingToggle,
    Pf5R9AttributionRow[] Attribution,
    Pf5R9SparseAttributionRow[] SparseAttribution,
    Pf5R9TopologyRow[] FixedDeliveryTopology,
    Pf5R9PrimitiveResult[] Primitives,
    Pf5R9HandoffPrimitiveResult[] HandoffPrimitives,
    string[] Dispositions);

internal readonly record struct Pf5R9TimestampConversionProbe(
    long StopwatchTicks,
    long Frequency,
    double Nanoseconds);

internal readonly record struct Pf5R9TimingRow(
    string Label,
    int Repetition,
    bool BackgroundTimingEnabled,
    P03WholeFlowRow Row);

internal readonly record struct Pf5R9AttributionRow(
    string Label,
    P03WholeFlowRow Row,
    Pf5R9Attribution Attribution,
    Pf5R9CompressionInputCensusSnapshot CompressionInputs);

internal readonly record struct Pf5R9SparseAttributionRow(
    string Label,
    P03WholeFlowRow NetworkOnly,
    Pf5R9Attribution NetworkOnlyAttribution,
    P03WholeFlowRow GameOff,
    P03WholeFlowRow GameDetailed);

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

internal readonly record struct Pf5R9HandoffPrimitiveResult(
    string Mode,
    int DatagramBytes,
    int Iterations,
    int PreloadedDatagrams,
    P03Metric ProducerNanoseconds,
    long WarmedManagedBytes,
    ulong AcceptedDatagrams,
    ulong PhysicallySentDatagrams,
    ulong WorkerWakeups,
    ulong NativeSendCalls,
    ulong ScalarSendCalls,
    ulong CapacityRefusals,
    int QueueHighWater,
    bool NoAdditionalWorkerWakeDuringProducerWindow,
    string Boundary);

internal readonly record struct Pf5R9CompressionInputCensusSnapshot(
    int Capacity,
    int MaximumCanonicalBytes,
    ulong Attempts,
    ulong DistinctInputs,
    ulong ExactRepeatedInputs,
    ulong InputBytes,
    ulong ExactRepeatedBytes,
    ulong HashCollisions,
    ulong SaturatedInputs)
{
    public double ExactRepeatPercent => Attempts == 0
        ? 0
        : ExactRepeatedInputs * 100d / Attempts;
}

/// <summary>
/// Bounded diagnostic oracle for complete canonical message-129 inputs. Hashes select candidate
/// slots only; equality is confirmed with length plus byte-for-byte comparison. It never supplies
/// production output and is reset at the measured-window boundary by the adapter.
/// </summary>
internal sealed class Pf5R9CompressionInputCensus : IRemoteChunkCompressionInputObserver
{
    private const int Capacity = 4_096;
    private const int MaximumCanonicalBytes =
        UltraSimGameProtocol.MaximumCompressedDirtyChunkBundleDecodedBytes;
    private readonly ulong[] _hashes = new ulong[Capacity];
    private readonly ushort[] _lengths = new ushort[Capacity];
    private readonly byte[] _bytes = new byte[checked(Capacity * MaximumCanonicalBytes)];
    private ulong _attempts;
    private ulong _distinct;
    private ulong _repeated;
    private ulong _inputBytes;
    private ulong _repeatedBytes;
    private ulong _hashCollisions;
    private ulong _saturated;
    private bool _measurementFrozen;
    private Pf5R9CompressionInputCensusSnapshot _frozen;

    public void Observe(ReadOnlySpan<byte> canonicalBundle)
    {
        if (_measurementFrozen) return;
        if (canonicalBundle.IsEmpty || canonicalBundle.Length > MaximumCanonicalBytes)
            throw new InvalidOperationException("PF5-R9 observed an invalid compression input.");
        _attempts++;
        _inputBytes += checked((ulong)canonicalBundle.Length);
        ulong hash = Hash(canonicalBundle);
        int slot = checked((int)(hash & (Capacity - 1)));
        for (int probe = 0; probe < Capacity; probe++)
        {
            int length = _lengths[slot];
            if (length == 0)
            {
                if (_distinct == Capacity)
                {
                    _saturated++;
                    return;
                }
                _hashes[slot] = hash;
                _lengths[slot] = checked((ushort)canonicalBundle.Length);
                canonicalBundle.CopyTo(_bytes.AsSpan(
                    checked(slot * MaximumCanonicalBytes), canonicalBundle.Length));
                _distinct++;
                return;
            }
            if (_hashes[slot] == hash)
            {
                if (length == canonicalBundle.Length && canonicalBundle.SequenceEqual(
                        _bytes.AsSpan(checked(slot * MaximumCanonicalBytes), length)))
                {
                    _repeated++;
                    _repeatedBytes += checked((ulong)canonicalBundle.Length);
                    return;
                }
                _hashCollisions++;
            }
            slot = (slot + 1) & (Capacity - 1);
        }
        _saturated++;
    }

    public void Reset()
    {
        Array.Clear(_lengths);
        _attempts = _distinct = _repeated = _inputBytes = _repeatedBytes = 0;
        _hashCollisions = _saturated = 0;
        _measurementFrozen = false;
        _frozen = default;
    }

    public void FreezeMeasurement()
    {
        if (_measurementFrozen) return;
        _frozen = Current();
        _measurementFrozen = true;
    }

    internal Pf5R9CompressionInputCensusSnapshot Capture() =>
        _measurementFrozen ? _frozen : Current();

    private Pf5R9CompressionInputCensusSnapshot Current() => new(
        Capacity, MaximumCanonicalBytes, _attempts, _distinct, _repeated,
        _inputBytes, _repeatedBytes, _hashCollisions, _saturated);

    private static ulong Hash(ReadOnlySpan<byte> value)
    {
        const ulong offset = 14_695_981_039_346_656_037UL;
        const ulong prime = 1_099_511_628_211UL;
        ulong hash = offset;
        for (int index = 0; index < value.Length; index++)
        {
            hash ^= value[index];
            hash = unchecked(hash * prime);
        }
        return hash;
    }
}
