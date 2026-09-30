using System.Diagnostics;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

string root = FindRepositoryRoot();
VerificationOptions options = VerificationOptions.Parse(args);

VerifyArchitecture(root);
Console.WriteLine("PASS active project dependency boundaries");
VerifyClientPresentationAssets(root);
Console.WriteLine("PASS built-in client presentation assets");
VerifyClientExportComposition(root);
Console.WriteLine("PASS single-project client export composition");
VerifyDocumentation(root);
Console.WriteLine("PASS documentation architecture and links");

if (options.ArchitectureOnly)
    return 0;

if (options.F112R1Only)
{
    string godot = ResolveGodot(options.GodotPath);
    string directory = Path.Combine(root, ".output", "f1.12-r1-verification", Environment.ProcessId.ToString());
    Directory.CreateDirectory(directory);
    string executable = Path.Combine(directory, "UltraSimComplete.x86_64");
    ExportClient(root, godot, "Complete", executable);
    string head = RunCaptured(root, "git", ["rev-parse", "HEAD"], 5_000).Trim();
    foreach (string label in new[] { "probe", "cabin-installed", "cabin-primitive", "pointer-tiers", "memory" })
    {
        string output = Path.Combine(directory, label); Directory.CreateDirectory(output);
        var arguments = new List<string>
        {
            "--", "--expect-edition=Complete", $"--f1-12-head={head}",
            $"--f1-12-save-root={Path.Combine(output, "saves")}",
            $"--content-cache={Path.Combine(output, "content")}",
            $"--f1-12-cabin-evidence={output}", $"--f1-12-evidence={Path.Combine(output, "evidence.json")}",
        };
        arguments.Add(label switch { "probe" => "--f1-12-render-probe", "pointer-tiers" => "--f1-12-native-product",
            "memory" => "--f1-12-memory", _ => "--f1-12-cabin-preview" });
        if (label == "cabin-primitive") arguments.Add("--f1-12-art=primitive");
        if (label == "memory") { arguments.Add("--f1-12-lifetimes=8"); arguments.Add("--f1-12-seconds=15"); }
        string result = RunCaptured(root, executable, arguments, 240_000);
        File.WriteAllText(Path.Combine(output, "run.log"), result);
        if (label == "probe")
        {
            if (!File.Exists(Path.Combine(output, "probe.png"))) throw new InvalidOperationException("Native probe PNG missing.");
        }
        else if (!File.Exists(Path.Combine(output, "evidence.json")))
            throw new InvalidOperationException($"Missing R1 native evidence: {label}");
        Console.WriteLine($"PASS F1.12-R1 native {label} evidence={output}");
    }
    VerifyMixedSessionLifecycleNative(root, options.Configuration, godot, executable, rendered: true);
    Console.WriteLine("PASS F1.12-R1 focused native product closure controls");
    return 0;
}

if (options.F112MixedOnly)
{
    string godot = ResolveGodot(options.GodotPath);
    string directory = Path.Combine(root, ".output", "f1.12-products", Environment.ProcessId.ToString());
    string executable = Path.Combine(directory, "UltraSimComplete.x86_64");
    Directory.CreateDirectory(directory);
    ExportClient(root, godot, "Complete", executable);
    VerifyMixedSessionLifecycleNative(root, options.Configuration, godot, executable);
    Console.WriteLine("PASS F1.12 focused exported Complete mixed journey");
    return 0;
}

if (options.F112Only)
{
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        FinalClientProductExports local = VerifyFinalClientProductExports(root, godot, product.Scope);
        VerifyContentlessOnlineNative(root, options.Configuration, product.Executable, ["--headless", "--"]);
        VerifyMixedSessionLifecycleNative(root, options.Configuration, godot, local.CompleteExecutable);
    }
    finally
    {
        if (Directory.Exists(product.Scope)) Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS F1.12 focused actual-exported-product journeys");
    return 0;
}

if (options.F17Only)
{
    VerifyF17ClientWorkspace(root, options);
    Console.WriteLine("PASS F1.7 focused workspace/gump verification");
    return 0;
}

if (options.Pf5R6Only)
{
    VerifyPf5R6CompatibleBodySharing(root, options.Configuration);
    Console.WriteLine("PASS PF5-R6 focused compatible-body-sharing verification");
    return 0;
}

static void VerifyF17ClientWorkspace(string root, VerificationOptions options)
{
    Run(root, "dotnet",
    [
        "run", "--project", "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
        "-c", options.Configuration,
    ]);
    const string godotProject =
        "_Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj";
    Run(root, "dotnet", ["build", godotProject, "-c", options.Configuration]);
    string godot = ResolveGodot(options.GodotPath);
    Run(root, godot,
    [
        "--headless", "--path", "_Tests/UltraSim.S0.Godot.Tests",
        "--scene", "res://S08HeadlessAcceptance.tscn",
    ]);
    BuildClientApplication(root, options.Configuration, "SinglePlayer");
    Run(root, godot,
    [
        "--headless", "--editor", "--path", "Client/Godot", "--quit-after", "2",
    ]);
    Run(root, godot,
    [
        "--headless", "--path", "Client/Godot", "--", "--f1-7-ui-smoke",
        "--expect-edition=SinglePlayer",
    ]);
    Run(root, godot,
    [
        "--headless", "--path", "Client/Godot", "--", "--f1-9a-display-smoke",
        "--expect-edition=SinglePlayer",
    ]);
}

if (options.Pf5R7Only)
{
    VerifyPf5R7Attribution(root, options.Configuration);
    Console.WriteLine("PASS PF5-R7 focused topology/server-cost attribution verification");
    return 0;
}

if (options.Pf5R8Only)
{
    VerifyPf5R8RequiredFanout(root, options.Configuration);
    Console.WriteLine("PASS PF5-R8 focused required-fanout attribution verification");
    return 0;
}

if (options.Pf5R9Only)
{
    VerifyPf5R9PacketProducer(root, options.Configuration);
    Console.WriteLine("PASS PF5-R9 focused packet-producer assurance verification");
    return 0;
}

if (options.Pf5R12Only)
{
    VerifyPf5R12PresentationScheduling(root, options.Configuration);
    Console.WriteLine("PASS PF5-R12 focused presentation scheduling/allocation verification");
    return 0;
}

static void VerifyPf5R12PresentationScheduling(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
        "-c", configuration,
    ]);
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
        "-c", configuration,
    ]);
}

static void VerifyPf5R9PacketProducer(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "run", "--project", "_Tests/UltraSim.Network.Tests/UltraSim.Network.Tests.csproj",
        "-c", configuration,
    ]);
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
        "-c", configuration,
    ]);

    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r9");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    try
    {
        string output = Path.Combine(scope, "packet-producer.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--pf5-r9-packet-producer", "--focused",
            "--output", output,
        ]);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement report = document.RootElement;
        if (report.GetProperty("Schema").GetString() !=
                "ultrasim-concept-demo-pf5-r9-packet-producer-audit-v2")
            throw new InvalidOperationException("PF5-R9 emitted an unexpected report schema.");
        JsonElement conversion = report.GetProperty("TimestampConversionProbe");
        if (conversion.GetProperty("StopwatchTicks").GetInt64() != 450 ||
            conversion.GetProperty("Frequency").GetInt64() != 1_000_000_000 ||
            conversion.GetProperty("Nanoseconds").GetDouble() != 450d)
            throw new InvalidOperationException(
                "PF5-R9 primitive timestamp conversion discarded sub-100ns precision.");
        JsonElement[] timing = report.GetProperty("TimingToggle").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        if (timing.Length != 2 || !timing.Any(static row =>
                row.GetProperty("BackgroundTimingEnabled").GetBoolean()) ||
            !timing.Any(static row =>
                !row.GetProperty("BackgroundTimingEnabled").GetBoolean()) ||
            timing.Any(static row => !row.GetProperty("Row")
                .GetProperty("Converged").GetBoolean()))
            throw new InvalidOperationException("PF5-R9 timing toggle was incomplete.");
        JsonElement attribution = report.GetProperty("Attribution")[0]
            .GetProperty("Attribution");
        double parent = attribution.GetProperty("ParentMilliseconds").GetDouble();
        double reconciled = attribution.GetProperty("ScheduleCodecQueueMilliseconds").GetDouble() +
            attribution.GetProperty("ProofApplicationMilliseconds").GetDouble() +
            attribution.GetProperty("ServerOutboundProtocolMilliseconds").GetDouble() +
            attribution.GetProperty("ServerInboundProtocolMilliseconds").GetDouble() +
            attribution.GetProperty("SignedResidualMilliseconds").GetDouble();
        if (Math.Abs(parent - reconciled) >= 0.000_001)
            throw new InvalidOperationException("PF5-R9 attribution did not reconcile.");
        if (report.GetProperty("Primitives").EnumerateArray().Any(static row =>
                row.GetProperty("WarmedManagedBytes").GetInt64() != 0))
            throw new InvalidOperationException("PF5-R9 primitive path allocated after warmup.");
        JsonElement[] handoffs = report.GetProperty("HandoffPrimitives").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        if (handoffs.Length == 0 || handoffs.Any(static row =>
                row.GetProperty("WarmedManagedBytes").GetInt64() != 0 ||
                row.GetProperty("AcceptedDatagrams").GetUInt64() !=
                    (ulong)row.GetProperty("Iterations").GetInt32() ||
                row.GetProperty("PhysicallySentDatagrams").GetUInt64() !=
                    checked((ulong)(row.GetProperty("Iterations").GetInt32() +
                        row.GetProperty("PreloadedDatagrams").GetInt32())) ||
                row.GetProperty("CapacityRefusals").GetUInt64() != 0))
            throw new InvalidOperationException(
                "PF5-R9 concurrent handoff primitive did not reconcile.");
        if (handoffs.Where(static row => row.GetProperty("Mode").GetString() ==
                "sustained-nonempty").Any(static row =>
                !row.GetProperty("NoAdditionalWorkerWakeDuringProducerWindow").GetBoolean()))
            throw new InvalidOperationException(
                "PF5-R9 sustained handoff did not remain continuously serviceable.");

        AssertPf5R9EpochShape(timing.Select(static row => row.GetProperty("Row")),
            requireDistinct: true);
        AssertPf5R9EpochShape(report.GetProperty("FixedDeliveryTopology").EnumerateArray()
            .Select(static row => row.GetProperty("Row")), requireDistinct: true);
        AssertPf5R9EpochShape(report.GetProperty("Attribution").EnumerateArray()
            .Select(static row => row.GetProperty("Row")), requireDistinct: true);
        JsonElement[] sparse = report.GetProperty("SparseAttribution").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        if (sparse.Length == 0)
            throw new InvalidOperationException("PF5-R9 sparse attribution was not exercised.");
        AssertPf5R9EpochShape(sparse.Select(static row => row.GetProperty("NetworkOnly")),
            requireDistinct: true);
        AssertPf5R9EpochShape(sparse.Select(static row => row.GetProperty("GameOff")),
            requireDistinct: true);
        AssertPf5R9EpochShape(sparse.Select(static row => row.GetProperty("GameDetailed")),
            requireDistinct: true);
        foreach (JsonElement sparseRow in sparse)
        {
            JsonElement sparseTiming = sparseRow.GetProperty("NetworkOnlyAttribution");
            double sparseParent = sparseTiming.GetProperty("ParentMilliseconds").GetDouble();
            double sparseChildren =
                sparseTiming.GetProperty("ScheduleCodecQueueMilliseconds").GetDouble() +
                sparseTiming.GetProperty("ProofApplicationMilliseconds").GetDouble() +
                sparseTiming.GetProperty("ServerOutboundProtocolMilliseconds").GetDouble() +
                sparseTiming.GetProperty("ServerInboundProtocolMilliseconds").GetDouble() +
                sparseTiming.GetProperty("SignedResidualMilliseconds").GetDouble();
            if (Math.Abs(sparseParent - sparseChildren) >= 0.000_001)
                throw new InvalidOperationException(
                    "PF5-R9 sparse Network attribution did not reconcile.");
            JsonElement offCpu = sparseRow.GetProperty("GameOff")
                .GetProperty("ProfileCounters").GetProperty("ChunkCpu");
            JsonElement detailedCpu = sparseRow.GetProperty("GameDetailed")
                .GetProperty("ProfileCounters").GetProperty("ChunkCpu");
            if (offCpu.GetProperty("Enabled").GetBoolean() ||
                !detailedCpu.GetProperty("Enabled").GetBoolean())
                throw new InvalidOperationException(
                    "PF5-R9 sparse Game attribution did not isolate Detailed-only work.");
        }

        JsonElement compressionAttribution = report.GetProperty("Attribution")[0];
        AssertPf5R9CompressionCensus(
            compressionAttribution, requireDistinct: true, requireRepeated: false);

        string sharedEpochOutput = Path.Combine(scope, "compression-shared-epoch.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--pf5-r9-packet-producer", "--focused",
            "--diagnostic-only", "--shared-epoch-control", "--output", sharedEpochOutput,
        ]);
        using JsonDocument sharedDocument = JsonDocument.Parse(File.ReadAllText(sharedEpochOutput));
        JsonElement sharedReport = sharedDocument.RootElement;
        if (sharedReport.GetProperty("UniqueApplicationEpochs").GetBoolean())
            throw new InvalidOperationException("PF5-R9 shared-epoch control was not enabled.");
        JsonElement sharedAttribution = sharedReport.GetProperty("Attribution")[0];
        AssertPf5R9EpochShape([sharedAttribution.GetProperty("Row")], requireDistinct: false);
        AssertPf5R9CompressionCensus(
            sharedAttribution, requireDistinct: false, requireRepeated: true);
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }

    static void AssertPf5R9EpochShape(
        IEnumerable<JsonElement> rows,
        bool requireDistinct)
    {
        foreach (JsonElement row in rows)
        {
            JsonElement epochs = row.GetProperty("ApplicationEpochs");
            int recipients = row.GetProperty("Recipients").GetInt32();
            int expected = requireDistinct ? recipients : 1;
            if (!epochs.GetProperty("Applicable").GetBoolean() ||
                epochs.GetProperty("RecipientCount").GetInt32() != recipients ||
                epochs.GetProperty("DistinctEpochCount").GetInt32() != expected)
                throw new InvalidOperationException(
                    "PF5-R9 did not realize the requested application-epoch shape.");
        }
    }

    static void AssertPf5R9CompressionCensus(
        JsonElement attribution,
        bool requireDistinct,
        bool requireRepeated)
    {
        JsonElement census = attribution.GetProperty("CompressionInputs");
        ulong attempts = census.GetProperty("Attempts").GetUInt64();
        ulong expectedAttempts = attribution.GetProperty("Row").GetProperty("ProfileCounters")
            .GetProperty("Compression").GetProperty("Attempts").GetUInt64();
        if (attempts != expectedAttempts ||
            census.GetProperty("SaturatedInputs").GetUInt64() != 0)
            throw new InvalidOperationException(
                "PF5-R9 compression-input census did not match the measured window.");
        if (requireDistinct &&
            (census.GetProperty("ExactRepeatedInputs").GetUInt64() != 0 ||
             census.GetProperty("DistinctInputs").GetUInt64() != attempts))
            throw new InvalidOperationException(
                "PF5-R9 independent application lifetimes shared a compression input.");
        if (requireRepeated && census.GetProperty("ExactRepeatedInputs").GetUInt64() == 0)
            throw new InvalidOperationException(
                "PF5-R9 shared-epoch compatibility control produced no repeated input.");
    }
}

static void VerifyPf5R8RequiredFanout(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
        "-c", configuration,
    ]);

    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r8");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    try
    {
        string output = Path.Combine(scope, "required-fanout.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--pf5-r8-required-fanout", "--focused",
            "--output", output,
        ]);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement report = document.RootElement;
        if (report.GetProperty("Schema").GetString() !=
                "ultrasim-concept-demo-pf5-r8-required-fanout-v3")
            throw new InvalidOperationException("PF5-R8 emitted an unexpected report schema.");
        JsonElement[] fanout = report.GetProperty("FixedSourceFanout").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        if (fanout.Length != 3 || fanout.Any(static row =>
                !row.GetProperty("Rated").GetProperty("Converged").GetBoolean() ||
                row.GetProperty("Rated").GetProperty("RawSamples").ValueKind ==
                    JsonValueKind.Null ||
                !row.GetProperty("Detailed").GetProperty("ProfileCounters")
                    .GetProperty("ChunkCpu").GetProperty("Enabled").GetBoolean()))
            throw new InvalidOperationException("PF5-R8 fanout census was incomplete.");
        ulong sourcePreparations = fanout[0].GetProperty("Detailed")
            .GetProperty("ProfileCounters").GetProperty("PreparedPublication")
            .GetProperty("PreparedSourcePublications").GetUInt64();
        if (fanout.Any(row => row.GetProperty("Detailed").GetProperty("ProfileCounters")
                .GetProperty("PreparedPublication").GetProperty("PreparedSourcePublications")
                .GetUInt64() != sourcePreparations))
            throw new InvalidOperationException(
                "PF5-R8 fixed-source fanout changed shared source preparation work.");
        JsonElement first = fanout[0].GetProperty("Detailed").GetProperty("ProfileCounters");
        JsonElement last = fanout[^1].GetProperty("Detailed").GetProperty("ProfileCounters");
        if (last.GetProperty("Lifecycle").GetProperty("DeliveriesQueued").GetUInt64() <=
                first.GetProperty("Lifecycle").GetProperty("DeliveriesQueued").GetUInt64() ||
            last.GetProperty("DeltaBodies").GetProperty("BodyHits").GetUInt64() == 0)
            throw new InvalidOperationException(
                "PF5-R8 did not separate fixed source work from recipient reuse.");
        JsonElement prepared = last.GetProperty("PreparedPublication");
        if (prepared.GetProperty("RangeIdentityInspections").GetUInt64() == 0 ||
            prepared.GetProperty("CoverageValidationCalls").GetUInt64() == 0 ||
            prepared.GetProperty("HistoryDerivedSteadyStateCalls").GetUInt64() != 0)
            throw new InvalidOperationException(
                "PF5-R8 coverage attribution was absent or left the selected read model.");
        if (report.GetProperty("TelemetryControls").GetArrayLength() != 3 ||
            report.GetProperty("ProofDebt").GetArrayLength() != 4 ||
            report.GetProperty("RecipientDivergencePotential").GetArrayLength() != 3 ||
            report.GetProperty("RecipientDivergenceEnabled").GetArrayLength() != 3 ||
            report.GetProperty("RatedAnchors").GetArrayLength() != 3)
            throw new InvalidOperationException("PF5-R8 focused controls were incomplete.");
        JsonElement[] fixedDelivery = report.GetProperty("FixedDeliveryTopology")
            .EnumerateArray().Select(static value => value.Clone()).ToArray();
        if (fixedDelivery.Length != 3)
            throw new InvalidOperationException("PF5-R8 fixed-delivery topology was absent.");
        JsonElement fixedExpected = fixedDelivery[0].GetProperty("Detailed")
            .GetProperty("ProfileCounters");
        ulong expectedDeliveries = fixedExpected.GetProperty("Lifecycle")
            .GetProperty("DeliveriesQueued").GetUInt64();
        ulong expectedRecords = fixedExpected.GetProperty("DeltaBodies")
            .GetProperty("BodyRecordsRequested").GetUInt64() +
            fixedExpected.GetProperty("DeltaBodies").GetProperty("BodyRecordsBuilt").GetUInt64();
        ulong expectedBytes = fixedExpected.GetProperty("ApplicationBytesQueued").GetUInt64();
        ulong previousPreparations = 0;
        foreach (JsonElement row in fixedDelivery)
        {
            JsonElement counters = row.GetProperty("Detailed").GetProperty("ProfileCounters");
            JsonElement bodies = counters.GetProperty("DeltaBodies");
            ulong preparations = counters.GetProperty("PreparedPublication")
                .GetProperty("PreparedSourcePublications").GetUInt64();
            if (counters.GetProperty("Lifecycle").GetProperty("DeliveriesQueued").GetUInt64() !=
                    expectedDeliveries ||
                bodies.GetProperty("BodyRecordsRequested").GetUInt64() +
                    bodies.GetProperty("BodyRecordsBuilt").GetUInt64() != expectedRecords ||
                counters.GetProperty("ApplicationBytesQueued").GetUInt64() != expectedBytes ||
                preparations <= previousPreparations)
                throw new InvalidOperationException(
                    "PF5-R8 fixed-delivery topology did not isolate unique source preparation.");
            previousPreparations = preparations;
        }
        JsonElement[] enabledDivergence = report.GetProperty("RecipientDivergenceEnabled")
            .EnumerateArray().Select(static value => value.Clone()).ToArray();
        JsonElement enabledSynchronized = enabledDivergence.Single(static row =>
            row.GetProperty("Scenario").GetString() == "synchronized");
        JsonElement enabledBodies = enabledSynchronized.GetProperty("CompatibleBodies");
        if (enabledBodies.GetProperty("Mode").GetInt32() != 2 ||
            enabledBodies.GetProperty("BodyCapacity").GetInt32() != 1024 ||
            enabledBodies.GetProperty("EncodedBodyCopies").GetUInt64() == 0 ||
            enabledBodies.GetProperty("MaterializationsAvoided").GetUInt64() == 0)
            throw new InvalidOperationException(
                "PF5-R8 production-capacity sharing did not serve divergent recipients.");
        JsonElement[] divergenceVerification = report.GetProperty("DivergenceVerification")
            .EnumerateArray().Select(static value => value.Clone()).ToArray();
        if (divergenceVerification.Length != enabledDivergence.Length ||
            divergenceVerification.Any(static row =>
                row.GetProperty("KnowledgeRowsCompared").GetInt32() <= 0 ||
                !row.GetProperty("ExactNormalizedKnowledgeEqual").GetBoolean() ||
                !row.GetProperty("LogicalPublicationsEqual").GetBoolean() ||
                !row.GetProperty("CanonicalApplicationBytesEqual").GetBoolean() ||
                !row.GetProperty("ProofOutcomesEqual").GetBoolean() ||
                !row.GetProperty("SnapshotAndReconnectOutcomesEqual").GetBoolean()))
            throw new InvalidOperationException(
                "PF5-R8 divergence equality verification was absent or failed.");
        JsonElement window = report.GetProperty("MeasurementWindowRegression");
        JsonElement ordinary = window.GetProperty("OrdinaryDrain");
        JsonElement extra = window.GetProperty("ExtraDrain");
        if (!ordinary.GetProperty("MeasurementWindow")
                .GetProperty("CountersCapturedBeforeDrain").GetBoolean() ||
            extra.GetProperty("Drain").GetProperty("RequestedRepetitions").GetInt32() != 8 ||
            ordinary.GetProperty("ProfileCounters").GetProperty("Lifecycle")
                .GetProperty("DeliveriesQueued").GetUInt64() !=
            extra.GetProperty("ProfileCounters").GetProperty("Lifecycle")
                .GetProperty("DeliveriesQueued").GetUInt64() ||
            ordinary.GetProperty("Timing").GetProperty("Protocol").GetProperty("Server")
                .GetProperty("EmitCalls").GetUInt64() !=
            extra.GetProperty("Timing").GetProperty("Protocol").GetProperty("Server")
                .GetProperty("EmitCalls").GetUInt64() ||
            extra.GetProperty("Drain").GetProperty("ServerEmitCalls").GetUInt64() <=
            ordinary.GetProperty("Drain").GetProperty("ServerEmitCalls").GetUInt64())
            throw new InvalidOperationException(
                "PF5-R8 measured-window counters leaked convergence-drain work.");
        if (report.GetProperty("ProofDebt").EnumerateArray().Any(static row =>
                !row.GetProperty("Converged").GetBoolean() ||
                row.GetProperty("DeliveryLookupCalls").GetUInt64() == 0 ||
                row.GetProperty("SubscriptionLookupCalls").GetUInt64() == 0))
            throw new InvalidOperationException("PF5-R8 proof lookup census was incomplete.");
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

static void VerifyPf5R7Attribution(string root, string configuration)
{
    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r7");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    try
    {
        string output = Path.Combine(scope, "attribution.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--pf5-r7-attribution", "--focused",
            "--warmup", "8", "--ticks", "12", "--census-ticks", "8",
            "--output", output,
        ]);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement report = document.RootElement;
        if (report.GetProperty("Checkpoint").GetString() !=
                "ultrasim-concept-demo-pf5-r7-spatial-topology-v1")
            throw new InvalidOperationException("PF5-R7 emitted an unexpected report schema.");
        JsonElement[] topology = report.GetProperty("TopologyRows").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        if (topology.Length != 4 || topology.Any(static row =>
                !row.GetProperty("Rated").GetProperty("Converged").GetBoolean() ||
                row.GetProperty("Rated").GetProperty("Transport")
                    .GetProperty("CapacityDrops").GetUInt64() != 0 ||
                row.GetProperty("DiagnosticCensus").GetProperty("CurrentMemberships")
                    .GetInt32() == 0))
            throw new InvalidOperationException("PF5-R7 topology canary was incomplete.");
        JsonElement denseBodies = topology.Single(static row =>
                row.GetProperty("Groups").GetInt32() == 1)
            .GetProperty("DiagnosticCensus").GetProperty("DeltaBodies");
        JsonElement sparseBodies = topology.Single(static row =>
                row.GetProperty("Groups").GetInt32() == 16)
            .GetProperty("DiagnosticCensus").GetProperty("DeltaBodies");
        if (denseBodies.GetProperty("BodyHits").GetUInt64() == 0 ||
            sparseBodies.GetProperty("DistinctBodyIdentities").GetUInt64() <=
                denseBodies.GetProperty("DistinctBodyIdentities").GetUInt64())
            throw new InvalidOperationException(
                "PF5-R7 did not distinguish dense reuse from sparse source bodies.");
        JsonElement[] whole = report.GetProperty("WholeServerRows").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        if (whole.Length != 8 || whole.Any(static row =>
                row.GetProperty("Flow").GetProperty("Timing")
                    .GetProperty("ServerOwnedTick").GetProperty("Samples").GetInt32() != 12 ||
                row.GetProperty("Flow").GetProperty("Timing")
                    .GetProperty("UnattributedResidual").GetProperty("Samples").GetInt32() != 12))
            throw new InvalidOperationException("PF5-R7 whole-server phase clocks were incomplete.");
        if (whole.Where(static row => row.GetProperty("MovementAndPhysics").GetBoolean())
                .Any(static row => row.GetProperty("Game").GetProperty("InputAccepted")
                    .GetUInt64() == 0) ||
            whole.Where(static row => row.GetProperty("Chat").GetBoolean())
                .Any(static row => row.GetProperty("Game").GetProperty("ChatAccepted")
                    .GetUInt64() == 0))
            throw new InvalidOperationException("PF5-R7 did not exercise real Game input/Chat.");
        if (report.GetProperty("AuthorityOnlyRows").GetArrayLength() != 1 ||
            report.GetProperty("PhysicsRows").GetArrayLength() != 1)
            throw new InvalidOperationException("PF5-R7 baseline/physics controls were absent.");
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

static void VerifyPf5R6CompatibleBodySharing(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
        "-c", configuration,
    ]);

    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r6");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    try
    {
        string udpOutput = Path.Combine(scope, "real-udp.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow",
            "--pf5-r6-body-sharing", "--populations=64", "--include-census",
            "--include-divergence", "--divergence-recipients=16", "--divergence-ticks=30",
            "--warmup=6", "--ticks=30", $"--output={udpOutput}",
        ]);
        using JsonDocument udp = JsonDocument.Parse(File.ReadAllText(udpOutput));
        if (udp.RootElement.GetProperty("Schema").GetString() !=
                "ultrasim-network-pf5-r6-compatible-body-sharing-v1")
            throw new InvalidOperationException("PF5-R6 report schema was not canonical.");
        JsonElement[] rows = udp.RootElement.GetProperty("Rows").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        if (rows.Length != 6 || rows.Any(static row =>
                !row.GetProperty("Converged").GetBoolean() ||
                row.GetProperty("SocketIoMode").GetString() !=
                    "BackgroundPlatformBatch" ||
                row.GetProperty("Transport").GetProperty("CapacityDrops").GetUInt64() != 0 ||
                row.GetProperty("ProfileCounters").GetProperty("Compression")
                    .GetProperty("SingletonSelected").GetUInt64() != 0))
            throw new InvalidOperationException("PF5-R6 real-UDP matrix was incomplete.");
        JsonElement[] divergence = udp.RootElement.GetProperty("DivergenceScenarios")
            .EnumerateArray().Select(static value => value.Clone()).ToArray();
        if (divergence.Length != 11 || divergence.Any(static row =>
                row.GetProperty("CompatibleBodies").GetProperty("BodyRequests").GetUInt64() == 0 ||
                row.GetProperty("CompatibleBodies").GetProperty("BodyIdentityCollisions")
                    .GetUInt64() != 0))
            throw new InvalidOperationException(
                "PF5-R6 divergence census was incomplete or found incompatible bodies.");
        foreach (IGrouping<int, JsonElement> workload in rows.GroupBy(static row =>
                     row.GetProperty("DirtyEntitiesPerTick").GetInt32()))
        {
            JsonElement[] triple = workload.ToArray();
            if (triple.Length != 3)
                throw new InvalidOperationException("PF5-R6 OFF/Census/ON triple was incomplete.");
            JsonElement off = triple.Single(static row => row.GetProperty("Curve").GetString()!
                .EndsWith("-Disabled", StringComparison.Ordinal));
            JsonElement census = triple.Single(static row => row.GetProperty("Curve").GetString()!
                .EndsWith("-CensusOnly", StringComparison.Ordinal));
            JsonElement on = triple.Single(static row => row.GetProperty("Curve").GetString()!
                .EndsWith("-Enabled", StringComparison.Ordinal));
            JsonElement offCounters = off.GetProperty("ProfileCounters");
            JsonElement censusBodies = census.GetProperty("ProfileCounters")
                .GetProperty("DeltaBodies");
            JsonElement onCounters = on.GetProperty("ProfileCounters");
            JsonElement onBodies = onCounters.GetProperty("DeltaBodies");
            if (onBodies.GetProperty("BodyRequests").GetUInt64() == 0 ||
                onBodies.GetProperty("BodyHits").GetUInt64() == 0 ||
                onBodies.GetProperty("BodyFills").GetUInt64() == 0 ||
                onBodies.GetProperty("MaterializationsAvoided").GetUInt64() == 0 ||
                onBodies.GetProperty("BodyIdentityCollisions").GetUInt64() != 0 ||
                censusBodies.GetProperty("BodyRequests").GetUInt64() == 0 ||
                censusBodies.GetProperty("MaterializationsAvoided").GetUInt64() != 0 ||
                off.GetProperty("SemanticDigest").GetString() !=
                    on.GetProperty("SemanticDigest").GetString() ||
                offCounters.GetProperty("SuccessfulSemanticPublications").GetUInt64() !=
                    onCounters.GetProperty("SuccessfulSemanticPublications").GetUInt64() ||
                offCounters.GetProperty("ProofEntries").GetUInt64() !=
                    onCounters.GetProperty("ProofEntries").GetUInt64() ||
                offCounters.GetProperty("GameMessagesQueued").GetUInt64() !=
                    onCounters.GetProperty("GameMessagesQueued").GetUInt64() ||
                CanonicalApplicationBytes(offCounters) != CanonicalApplicationBytes(onCounters))
                throw new InvalidOperationException(
                    "PF5-R6 changed canonical work or failed to reuse compatible bodies.");
        }
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }

    static ulong CanonicalApplicationBytes(JsonElement counters)
    {
        JsonElement compression = counters.GetProperty("Compression");
        return checked(counters.GetProperty("ApplicationBytesQueued").GetUInt64() -
            compression.GetProperty("CompressedRepresentationBytes").GetUInt64() +
            compression.GetProperty("CanonicalBytesSelected").GetUInt64());
    }
}

if (options.Pf5R5Only)
{
    VerifyPf5R5SingletonCompression(root, options.Configuration);
    Console.WriteLine("PASS PF5-R5 focused singleton-compression verification");
    return 0;
}

static void VerifyPf5R5SingletonCompression(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
        "-c", configuration,
    ]);

    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r5");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    try
    {
        string udpOutput = Path.Combine(scope, "real-udp.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow",
            "--pf5-r5-singleton-compression", "--populations=64",
            "--warmup=6", "--ticks=30", "--egress-timing=off",
            $"--output={udpOutput}",
        ]);
        using JsonDocument udp = JsonDocument.Parse(File.ReadAllText(udpOutput));
        if (udp.RootElement.GetProperty("Schema").GetString() !=
                "ultrasim-network-pf5-r5-singleton-compression-v1")
            throw new InvalidOperationException("PF5-R5 report schema was not canonical.");
        JsonElement[] rows = udp.RootElement.GetProperty("Rows").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        if (rows.Length != 4 || rows.Any(static row =>
                !row.GetProperty("Converged").GetBoolean() ||
                row.GetProperty("SocketIoMode").GetString() !=
                    "BackgroundPlatformBatch" ||
                row.GetProperty("Transport").GetProperty("CapacityDrops").GetUInt64() != 0))
            throw new InvalidOperationException("PF5-R5 real-UDP matrix was incomplete.");
        foreach (IGrouping<int, JsonElement> workload in rows.GroupBy(static row =>
                     row.GetProperty("DirtyEntitiesPerTick").GetInt32()))
        {
            JsonElement[] pair = workload.ToArray();
            if (pair.Length != 2)
                throw new InvalidOperationException("PF5-R5 OFF/ON pair was incomplete.");
            JsonElement off = pair.Single(static row => row.GetProperty("Curve").GetString()!
                .EndsWith("-Disabled", StringComparison.Ordinal));
            JsonElement on = pair.Single(static row => row.GetProperty("Curve").GetString()!
                .EndsWith("-WhenBeneficial", StringComparison.Ordinal));
            JsonElement offCounters = off.GetProperty("ProfileCounters");
            JsonElement onCounters = on.GetProperty("ProfileCounters");
            JsonElement compression = onCounters.GetProperty("Compression");
            if (off.GetProperty("SemanticDigest").GetString() !=
                    on.GetProperty("SemanticDigest").GetString() ||
                offCounters.GetProperty("SuccessfulSemanticPublications").GetUInt64() !=
                    onCounters.GetProperty("SuccessfulSemanticPublications").GetUInt64() ||
                offCounters.GetProperty("ProofEntries").GetUInt64() !=
                    onCounters.GetProperty("ProofEntries").GetUInt64() ||
                compression.GetProperty("SingletonAttempts").GetUInt64() == 0 ||
                compression.GetProperty("SingletonSelected").GetUInt64() == 0 ||
                onCounters.GetProperty("ApplicationBytesQueued").GetUInt64() >=
                    offCounters.GetProperty("ApplicationBytesQueued").GetUInt64())
                throw new InvalidOperationException(
                    "PF5-R5 changed semantic work or failed to select a beneficial singleton.");
        }
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

if (options.Pf5R4Only)
{
    VerifyPf5R4RecipientFlattening(root, options.Configuration);
    Console.WriteLine("PASS PF5-R4 focused recipient-preparation verification");
    return 0;
}

static void VerifyPf5R4RecipientFlattening(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
        "-c", configuration,
    ]);

    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r4");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    try
    {
        string microOutput = Path.Combine(scope, "sort-micro.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--pf5-r4-sort-micro",
            "--iterations=2000", "--samples=7", $"--output={microOutput}",
        ]);
        using (JsonDocument micro = JsonDocument.Parse(File.ReadAllText(microOutput)))
        {
            JsonElement[] rows = micro.RootElement.GetProperty("Rows").EnumerateArray()
                .Select(static value => value.Clone()).ToArray();
            if (micro.RootElement.GetProperty("Schema").GetString() !=
                    "ultrasim-pf5-r4-index-sort-v1" ||
                rows.Length != 5 ||
                rows.Any(static row => row.GetProperty("OldAllocationBytes").GetInt64() != 0 ||
                    row.GetProperty("NewAllocationBytes").GetInt64() != 0))
                throw new InvalidOperationException(
                    "PF5-R4 compact-index sort microbenchmark was incomplete or allocated.");
        }

        string matchedOutput = Path.Combine(scope, "matched.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--pf5-r2-r2-scaling", "--matched-only",
            "--matched-detailed", "--populations=128", "--warmup=8", "--ticks=24",
            "--attribution-ticks=16", $"--output={matchedOutput}",
        ]);
        using (JsonDocument matched = JsonDocument.Parse(File.ReadAllText(matchedOutput)))
        {
            JsonElement[] rows = matched.RootElement.GetProperty("MatchedControlRows")
                .EnumerateArray().Where(static value => value.GetProperty("Flow")
                    .GetProperty("Entities").GetInt32() == 1_000)
                .Select(static value => value.Clone()).ToArray();
            if (rows.Length != 2)
                throw new InvalidOperationException("PF5-R4 matched controls were incomplete.");
            JsonElement high = rows.MaxBy(static row => row.GetProperty("Flow")
                .GetProperty("DirtyEntitiesPerTick").GetInt32());
            JsonElement flow = high.GetProperty("Flow");
            JsonElement cpu = flow.GetProperty("ProfileCounters").GetProperty("ChunkCpu");
            JsonElement prepared = flow.GetProperty("ProfileCounters")
                .GetProperty("PreparedPublication");
            ulong requests = cpu.GetProperty("PreparedCandidateRequests").GetUInt64();
            ulong builds = cpu.GetProperty("PreparedCandidateBuilds").GetUInt64();
            if (!flow.GetProperty("Converged").GetBoolean() ||
                !cpu.GetProperty("Enabled").GetBoolean() || requests == 0 || builds != requests ||
                cpu.GetProperty("PreparedCandidatePacketBoundaryCarries").GetUInt64() == 0 ||
                cpu.GetProperty("PreparedCandidateLocalReuses").GetUInt64() == 0 ||
                cpu.GetProperty("PreparedCandidateInvalidations").GetUInt64() != 0 ||
                cpu.GetProperty("PreparedValidationHashCalls").GetUInt64() != builds ||
                prepared.GetProperty("RecipientSortCalls").GetUInt64() == 0 ||
                prepared.GetProperty("RecipientIndicesSorted").GetUInt64() !=
                    prepared.GetProperty("RecipientRecordsConstructed").GetUInt64())
                throw new InvalidOperationException(
                    "PF5-R4 matched control retained redundant candidate, validation, or sort work.");
        }

        string udpOutput = Path.Combine(scope, "real-udp.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow",
            "--pf5-r3-background-egress", "--socket-modes=platform-batch,background-platform-batch",
            "--populations=16", "--warmup=6", "--ticks=30", "--egress-timing=off",
            $"--output={udpOutput}",
        ]);
        using (JsonDocument udp = JsonDocument.Parse(File.ReadAllText(udpOutput)))
        {
            JsonElement[] rows = udp.RootElement.GetProperty("Rows").EnumerateArray()
                .Select(static value => value.Clone()).ToArray();
            if (rows.Length != 2 || rows[0].GetProperty("SocketIoMode").GetString() !=
                    "PlatformBatch" ||
                rows[1].GetProperty("SocketIoMode").GetString() != "BackgroundPlatformBatch" ||
                rows.Any(static row => !row.GetProperty("Converged").GetBoolean()) ||
                rows[0].GetProperty("SemanticDigest").GetString() !=
                    rows[1].GetProperty("SemanticDigest").GetString() ||
                rows[0].GetProperty("ProfileCounters").GetProperty("SuccessfulSemanticPublications")
                    .GetUInt64() != rows[1].GetProperty("ProfileCounters")
                    .GetProperty("SuccessfulSemanticPublications").GetUInt64())
                throw new InvalidOperationException(
                    "PF5-R4 Inline/Background PlatformBatch real-UDP parity failed.");
        }
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

if (options.Pf5R3Only)
{
    VerifyPf5R3BackgroundEgress(root, options.Configuration);
    Console.WriteLine("PASS PF5-R3 focused background-egress verification");
    return 0;
}

if (options.Pf5R2R2Only)
{
    VerifyPf5R2R2ScalingBoundary(root, options.Configuration);
    Console.WriteLine("PASS PF5-R2-R2 focused scaling-boundary verification");
    return 0;
}

static void VerifyPf5R3BackgroundEgress(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Network.Tests/UltraSim.Network.Tests.csproj",
        "-c", configuration,
    ]);

    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r3");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
        string[] modes = ["scalar", "platform-batch", "background-scalar",
            "background-platform-batch"];
    try
    {
        ulong? constructions = null;
        ulong? eventsPublished = null;
        foreach (string mode in modes)
        {
            string output = Path.Combine(scope, $"dense-{mode}.json");
            Run(root, "dotnet",
            [
                "run", "--project",
                "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
                "-c", configuration, "--", "--replication-suite",
                "--replication-profile=dense_mix", "--replication-traffic=full",
                "--clients=16", "--ticks=30", "--warmup=6", "--dirty-percent=2",
                $"--shared-socket-io={mode}", $"--output={output}",
            ]);
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
            JsonElement result = document.RootElement.GetProperty("results")[0];
            if (!result.GetProperty("converged").GetBoolean() ||
                result.GetProperty("capacity_drops").GetUInt64() != 0 ||
                result.GetProperty("wire_datagrams").GetUInt64() == 0)
                throw new InvalidOperationException(
                    $"PF5-R3 {mode} real-UDP control did not converge cleanly.");
            ulong currentConstructions = result.GetProperty("semantic_constructions").GetUInt64();
            ulong currentEvents = result.GetProperty("application_events_published").GetUInt64();
            constructions ??= currentConstructions;
            eventsPublished ??= currentEvents;
            if (constructions != currentConstructions || eventsPublished != currentEvents)
                throw new InvalidOperationException(
                    "PF5-R3 socket modes changed authoritative Game semantic work.");

            JsonElement background = result.GetProperty("listener_after")
                .GetProperty("background_egress");
            bool expectedBackground = mode.StartsWith("background-", StringComparison.Ordinal);
            if (expectedBackground !=
                    (background.GetProperty("sender_threads").GetInt32() == 1) ||
                background.GetProperty("current_datagrams").GetInt32() != 0 ||
                background.GetProperty("capacity_refusals").GetUInt64() != 0 ||
                background.GetProperty("has_worker_failure").GetBoolean())
                throw new InvalidOperationException(
                    $"PF5-R3 {mode} background ownership was not bounded and quiescent.");
            if (expectedBackground &&
                (background.GetProperty("accepted_datagrams").GetUInt64() == 0 ||
                 background.GetProperty("physically_sent_datagrams").GetUInt64() == 0 ||
                 background.GetProperty("reserved_bytes").GetInt64() <
                    background.GetProperty("capacity_bytes").GetInt64()))
                throw new InvalidOperationException(
                    $"PF5-R3 {mode} did not expose accepted physical egress and reservation evidence.");
        }

        string currentPathOutput = Path.Combine(scope, "current-chunk-publication.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow",
            "--pf5-r3-background-egress", "--populations=16", "--warmup=6", "--ticks=30",
            $"--output={currentPathOutput}",
        ]);
        using (JsonDocument currentPath = JsonDocument.Parse(
                   File.ReadAllText(currentPathOutput)))
        {
            JsonElement.ArrayEnumerator rows = currentPath.RootElement
                .GetProperty("Rows").EnumerateArray();
            int rowCount = 0;
            string? semanticDigest = null;
            ulong? publications = null;
            foreach (JsonElement row in rows)
            {
                rowCount++;
                if (row.GetProperty("Profile").GetString() != "ChunkPublication" ||
                    !row.GetProperty("Converged").GetBoolean() ||
                    row.GetProperty("Transport").GetProperty("WireDatagrams").GetUInt64() == 0 ||
                    row.GetProperty("ProfileCounters").GetProperty("Compression")
                        .GetProperty("Selected").GetUInt64() == 0)
                    throw new InvalidOperationException(
                        "PF5-R3 current ChunkPublication real-UDP control was incomplete.");
                string currentDigest = row.GetProperty("SemanticDigest").GetString() ?? "";
                ulong currentPublications = row.GetProperty("ProfileCounters")
                    .GetProperty("SuccessfulSemanticPublications").GetUInt64();
                semanticDigest ??= currentDigest;
                publications ??= currentPublications;
                if (semanticDigest != currentDigest || publications != currentPublications)
                    throw new InvalidOperationException(
                        "PF5-R3 egress modes changed selected-profile semantic work.");
                JsonElement background = row.GetProperty("BackgroundEgress");
                bool expectedBackground = row.GetProperty("SocketIoMode").GetString()?
                    .StartsWith("Background", StringComparison.Ordinal) == true;
                if (expectedBackground != (background.GetProperty("SenderThreads").GetInt32() == 1) ||
                    background.GetProperty("CurrentDatagrams").GetInt32() != 0 ||
                    background.GetProperty("CapacityRefusals").GetUInt64() != 0 ||
                    background.GetProperty("HasWorkerFailure").GetBoolean())
                    throw new InvalidOperationException(
                        "PF5-R3 selected-profile background egress did not retire bounded debt.");
            }
            if (rowCount != modes.Length)
                throw new InvalidOperationException(
                    "PF5-R3 selected-profile four-mode matrix was incomplete.");
        }

        string hostedOutput = Path.Combine(scope, "hosted-background.json");
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.HostedH05/UltraSim.Network.HostedH05.csproj",
            "-c", configuration, "--", "--clients=4", "--owners=2", "--ticks=12",
            "--reconnects=1", "--socket-io=background-platform-batch",
            $"--output={hostedOutput}",
        ]);
        using JsonDocument hosted = JsonDocument.Parse(File.ReadAllText(hostedOutput));
        JsonElement report = hosted.RootElement;
        JsonElement server = report.GetProperty("Server");
        JsonElement hostedBackground = server.GetProperty("Listener")
            .GetProperty("BackgroundEgress");
        if (report.GetProperty("SocketIoMode").GetInt32() != 4 ||
            server.GetProperty("CompletedApplications").GetInt32() != 8 ||
            server.GetProperty("DisclosureViolations").GetUInt64() != 0 ||
            hostedBackground.GetProperty("PhysicallySentDatagrams").GetUInt64() == 0 ||
            hostedBackground.GetProperty("HasWorkerFailure").GetBoolean())
            throw new InvalidOperationException(
                "PF5-R3 external Hosted UDP background-egress control failed.");
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

if (options.Pf5R2R1Only)
{
    VerifyPf5R2R1TimingBoundary(root, options.Configuration);
    Console.WriteLine("PASS PF5-R2-R1 focused timing-boundary verification");
    return 0;
}

static void VerifyPf5R2R2ScalingBoundary(string root, string configuration)
{
    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r2-r2");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string output = Path.Combine(scope, "v2-v3-scaling.json");
    try
    {
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--pf5-r2-r2-scaling", "--focused",
            $"--output={output}",
        ]);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement report = document.RootElement;
        if (report.GetProperty("Schema").GetString() !=
            "ultrasim-concept-demo-pf5-r2-r2-v2-v3-scaling-v2")
            throw new InvalidOperationException("PF5-R2-R2 scaling schema was not canonical.");
        JsonElement[] rows = report.GetProperty("RatedRows").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        JsonElement[] detailed = report.GetProperty("DetailedAttributionRows").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        JsonElement[] matched = report.GetProperty("MatchedControlRows").EnumerateArray()
            .Select(static value => value.Clone()).ToArray();
        if (rows.Length != 4 || detailed.Length != 4 || matched.Length != 3)
            throw new InvalidOperationException("PF5-R2-R2 focused workload matrix was incomplete.");
        foreach (JsonElement row in rows.Concat(detailed).Concat(matched))
        {
            JsonElement flow = row.GetProperty("Flow");
            JsonElement timing = flow.GetProperty("Timing");
            JsonElement preparation = timing.GetProperty("RemotePublicationPreparation");
            JsonElement wakeup = timing.GetProperty("RecipientCommitObservation");
            JsonElement network = timing.GetProperty("NetworkAndRemoteReplication");
            JsonElement budget = timing.GetProperty("ServerOwnedRemoteReplicationBudget");
            int expected = row.GetProperty("DetailedAttribution").GetBoolean() ? 16 : 24;
            if (preparation.GetProperty("Samples").GetInt32() != expected ||
                wakeup.GetProperty("Samples").GetInt32() != expected ||
                network.GetProperty("Samples").GetInt32() != expected ||
                budget.GetProperty("Samples").GetInt32() != expected ||
                Math.Abs(preparation.GetProperty("Mean").GetDouble() +
                    wakeup.GetProperty("Mean").GetDouble() +
                    network.GetProperty("Mean").GetDouble() -
                    budget.GetProperty("Mean").GetDouble()) > 0.000_001 ||
                wakeup.GetProperty("Mean").GetDouble() <= 0 ||
                !flow.GetProperty("Converged").GetBoolean())
                throw new InvalidOperationException(
                    "PF5-R2-R2 per-tick server-owned timing did not reconcile.");
            JsonElement prepared = flow.GetProperty("ProfileCounters")
                .GetProperty("PreparedPublication");
            if (!prepared.GetProperty("Selected").GetBoolean() ||
                prepared.GetProperty("HistoryDerivedSteadyStateCalls").GetUInt64() != 0)
                throw new InvalidOperationException(
                    "PF5-R2-R2 left the accepted shared-publication model.");
            if (row.GetProperty("DetailedAttribution").GetBoolean() &&
                (prepared.GetProperty("SourcePreparationTicks").GetUInt64() == 0 ||
                 !flow.GetProperty("ProfileCounters").GetProperty("ChunkCpu")
                     .GetProperty("Enabled").GetBoolean()))
                throw new InvalidOperationException(
                    "PF5-R2-R2 detailed stage attribution was not active.");
            JsonElement boundary = flow.GetProperty("TimingBoundary");
            if (boundary.GetProperty("RecipientWakeupObservedOrder").GetInt32() <=
                    boundary.GetProperty("PreparedSourceObservedOrder").GetInt32() ||
                boundary.GetProperty("RecipientWakeupObservedOrder").GetInt32() >=
                    boundary.GetProperty("AuthorityEndOrder").GetInt32())
                throw new InvalidOperationException(
                    "PF5-R2-R2 recipient wakeup escaped the authority-side budget boundary.");
        }
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

if (options.Pf5R2Only)
{
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
        "-c", options.Configuration,
    ]);
    VerifyPf5R2SharedPublication(root, options.Configuration);
    Console.WriteLine("PASS PF5-R2 focused verification");
    return 0;
}

static void VerifyPf5R2R1TimingBoundary(string root, string configuration)
{
    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r2-r1");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string output = Path.Combine(scope, "timing-boundary.json");
    try
    {
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow",
            "--pf5-r2-r1-timing-boundary", "--focused", "--warmup=12", "--ticks=45",
            $"--output={output}",
        ]);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement report = document.RootElement;
        if (report.GetProperty("Schema").GetString() !=
            "ultrasim-concept-demo-pf5-r2-r1-timing-boundary-v2")
            throw new InvalidOperationException("PF5-R2-R1 timing schema was not canonical.");
        JsonElement boundary = report.GetProperty("BoundaryRegression");
        int combinedStart = boundary.GetProperty("CombinedStartOrder").GetInt32();
        int authorityStart = boundary.GetProperty("AuthorityStartOrder").GetInt32();
        int prepared = boundary.GetProperty("PreparedSourceObservedOrder").GetInt32();
        int authorityEnd = boundary.GetProperty("AuthorityEndOrder").GetInt32();
        int networkStart = boundary.GetProperty("NetworkStartOrder").GetInt32();
        int networkEnd = boundary.GetProperty("NetworkEndOrder").GetInt32();
        int combinedEnd = boundary.GetProperty("CombinedEndOrder").GetInt32();
        if (!boundary.GetProperty("Captured").GetBoolean() ||
            !(combinedStart < authorityStart && authorityStart < prepared &&
              prepared < authorityEnd && authorityEnd < networkStart &&
              networkStart < networkEnd && networkEnd < combinedEnd) ||
            boundary.GetProperty("PreparedSourcePublicationsObserved").GetUInt64() == 0 ||
            !boundary.GetProperty("CombinedFromSingleElapsedInterval").GetBoolean() ||
            boundary.GetProperty("CombinedFromPhaseDurations").GetBoolean())
            throw new InvalidOperationException(
                "PF5-R2-R1 did not structurally retain source preparation inside its combined boundary.");
        foreach (IGrouping<int, JsonElement> population in report.GetProperty("Rows")
                     .EnumerateArray().Select(static value => value.Clone()).GroupBy(static value =>
                         value.GetProperty("Population").GetInt32()))
        {
            JsonElement oldRow = population.Single(static value =>
                !value.GetProperty("Prepared").GetBoolean());
            JsonElement newRow = population.Single(static value =>
                value.GetProperty("Prepared").GetBoolean());
            JsonElement oldFlow = oldRow.GetProperty("Flow");
            JsonElement newFlow = newRow.GetProperty("Flow");
            JsonElement oldTiming = oldFlow.GetProperty("Timing");
            JsonElement newTiming = newFlow.GetProperty("Timing");
            if (oldFlow.GetProperty("SemanticDigest").GetString() !=
                    newFlow.GetProperty("SemanticDigest").GetString() ||
                oldTiming
                    .GetProperty("AuthorityThroughNetworkAndRemoteReplication")
                    .GetProperty("Samples").GetInt32() != 45 ||
                newTiming
                    .GetProperty("AuthorityThroughNetworkAndRemoteReplication")
                    .GetProperty("Samples").GetInt32() != 45 ||
                Math.Abs(oldTiming.GetProperty("Tick").GetProperty("Mean").GetDouble() -
                    oldTiming.GetProperty("AuthorityThroughNetworkAndRemoteReplication")
                        .GetProperty("Mean").GetDouble()) > 0.000_001 ||
                Math.Abs(newTiming.GetProperty("Tick").GetProperty("Mean").GetDouble() -
                    newTiming.GetProperty("AuthorityThroughNetworkAndRemoteReplication")
                        .GetProperty("Mean").GetDouble()) > 0.000_001)
                throw new InvalidOperationException(
                    "PF5-R2-R1 focused semantic/timing samples did not reconcile.");
        }
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

if (options.Pf5R1Only)
{
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
        "-c", options.Configuration,
    ]);
    VerifyPf5R1HistoryAudit(root, options.Configuration);
    Console.WriteLine("PASS PF5-R1 focused verification");
    return 0;
}

static void VerifyPf5R2SharedPublication(string root, string configuration)
{
    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r2");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string output = Path.Combine(scope, "shared-publication.json");
    try
    {
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow",
            "--pf5-r2-shared-publication", "--focused", "--warmup=12", "--ticks=45",
            $"--output={output}",
        ]);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement report = document.RootElement;
        if (report.GetProperty("Schema").GetString() !=
            "ultrasim-concept-demo-pf5-r2-shared-publication-v1")
            throw new InvalidOperationException("PF5-R2 audit schema was not canonical.");
        JsonElement[] divergence = report.GetProperty("DivergenceScenarios")
            .EnumerateArray().Select(static value => value.Clone()).ToArray();
        if (divergence.Length != 3)
            throw new InvalidOperationException("PF5-R2 focused divergence matrix was incomplete.");
        foreach (JsonElement scenario in divergence)
        {
            JsonElement source = scenario.GetProperty("PreparedSource");
            if (!source.GetProperty("Selected").GetBoolean() ||
                source.GetProperty("HistoryDerivedSteadyStateCalls").GetUInt64() != 0 ||
                source.GetProperty("PreparedSourcePublications").GetUInt64() == 0 ||
                source.GetProperty("ReferencedSourcePublications").GetUInt64() == 0)
                throw new InvalidOperationException(
                    "PF5-R2 divergence did not remain on shared source publications.");
        }
        JsonElement[] rows = report.GetProperty("Rows")
            .EnumerateArray().Select(static value => value.Clone()).ToArray();
        foreach (IGrouping<int, JsonElement> population in rows.GroupBy(static value =>
                     value.GetProperty("Population").GetInt32()))
        {
            JsonElement oldRow = population.Single(static value =>
                !value.GetProperty("Prepared").GetBoolean());
            JsonElement newRow = population.Single(static value =>
                value.GetProperty("Prepared").GetBoolean());
            if (oldRow.GetProperty("Flow").GetProperty("SemanticDigest").GetString() !=
                    newRow.GetProperty("Flow").GetProperty("SemanticDigest").GetString() ||
                oldRow.GetProperty("Flow").GetProperty("ProfileCounters")
                    .GetProperty("PreparedPublication")
                    .GetProperty("HistoryDerivedSteadyStateCalls").GetUInt64() == 0 ||
                newRow.GetProperty("Flow").GetProperty("ProfileCounters")
                    .GetProperty("PreparedPublication")
                    .GetProperty("HistoryDerivedSteadyStateCalls").GetUInt64() != 0)
                throw new InvalidOperationException("PF5-R2 old/new selection or semantics diverged.");
            if (population.Key == 128 && newRow.GetProperty("Flow").GetProperty("Timing")
                    .GetProperty("NetworkAndRemoteReplication").GetProperty("P95")
                    .GetDouble() > 4.0)
                throw new InvalidOperationException(
                    "PF5-R2 prepared 128-recipient path exceeded the 4 ms subsystem budget.");
        }
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

if (options.Pf5Only)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf5-only requires the actual exported Online product.");
    foreach (string project in new[]
             {
                 "_Tests/UltraSim.Network.Tests/UltraSim.Network.Tests.csproj",
                 "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
                 "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
                 "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
                 "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
             })
    {
        var command = new List<string> { "run", "--project", project, "-c", options.Configuration };
        if (project.Contains("NetworkBots", StringComparison.Ordinal))
            command.Add("-p:UltraSimNetworkFlavor=ClientOnly");
        Run(root, "dotnet", command);
    }
    VerifyPf5HeadlessImpairmentSwarm(root, options.Configuration);
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        string? pf5EvidenceRoot = options.Pf3NativeEvidenceOutputDirectory is null
            ? null
            : Path.GetFullPath(options.Pf3NativeEvidenceOutputDirectory);
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: options.Pf3NativeWarmupSeconds,
            measurementSeconds: options.Pf3NativeMeasurementSeconds,
            evidenceOutputDirectory: pf5EvidenceRoot is null
                ? null : Path.Combine(pf5EvidenceRoot, "population-10-impaired"),
            telemetryCheckpoint: "PF5",
            externalImpairment: true);
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: Math.Min(options.Pf3NativeWarmupSeconds, 10),
            measurementSeconds: Math.Min(options.Pf3NativeMeasurementSeconds, 60),
            evidenceOutputDirectory: pf5EvidenceRoot is null
                ? null : Path.Combine(pf5EvidenceRoot, "population-18"),
            telemetryCheckpoint: "PF5",
            externalImpairment: false,
            botCount: 16);
    }
    finally
    {
        if (Directory.Exists(product.Scope)) Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF5 focused verification");
    return 0;
}

static void VerifyPf5R1HistoryAudit(string root, string configuration)
{
    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf5-r1");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string output = Path.Combine(scope, "history-audit.json");
    try
    {
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow", "--pf5-r1-history-audit",
            "--focused", "--recipients=32", "--rated-recipients=32", "--ticks=120",
            "--full-flow-warmup=10", "--full-flow-ticks=60", $"--output={output}",
        ]);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement report = document.RootElement;
        if (report.GetProperty("Schema").GetString() !=
            "ultrasim-concept-demo-pf5-r1-history-audit-v1")
            throw new InvalidOperationException("PF5-R1 audit schema was not canonical.");
        JsonElement[] scenarios = report.GetProperty("SemanticScenarios")
            .EnumerateArray().Select(static value => value.Clone()).ToArray();
        JsonElement synchronized = scenarios.Single(static value =>
            value.GetProperty("Scenario").GetString() == "synchronized");
        JsonElement divergence = scenarios.Single(static value =>
            value.GetProperty("Scenario").GetString() == "combined-divergence");
        JsonElement pressure = scenarios.Single(static value =>
            value.GetProperty("Scenario").GetString() == "history-pressure");
        if (synchronized.GetProperty("RepeatRangePercent").GetDouble() <= 90 ||
            synchronized.GetProperty("HistoryNodesPerRequest").GetDouble() <=
                synchronized.GetProperty("RecordsPerRequest").GetDouble() ||
            divergence.GetProperty("RepeatRangePercent").GetDouble() >=
                synchronized.GetProperty("RepeatRangePercent").GetDouble() ||
            pressure.GetProperty("SnapshotReasons").EnumerateArray().All(static value =>
                value.GetProperty("Reason").GetString() != "HistoryFloorLost"))
            throw new InvalidOperationException("PF5-R1 divergence/history invariants were absent.");
        foreach (JsonElement scenario in scenarios)
        {
            JsonElement history = scenario.GetProperty("SourceHistory");
            if (history.GetProperty("ShadowContentMismatches").GetUInt64() != 0 ||
                history.GetProperty("RangeTableOverflowed").GetBoolean())
                throw new InvalidOperationException("PF5-R1 shadow oracle diverged or overflowed.");
        }
        JsonElement[] topology = report.GetProperty("FixedTotalTopology")
            .EnumerateArray().Select(static value => value.Clone()).ToArray();
        if (topology.Length != 2 ||
            topology.Single(static value => value.GetProperty("HotChunks").GetInt32() == 1)
                .GetProperty("UniqueRanges").GetInt32() != 1 ||
            topology.Single(static value => value.GetProperty("HotChunks").GetInt32() == 512)
                .GetProperty("UniqueRanges").GetInt32() != 512 ||
            report.GetProperty("CrossChunkHistoryPressure").GetProperty("ReadStatus")
                .GetString() != "SnapshotRequired")
            throw new InvalidOperationException("PF5-R1 topology/history-pressure control diverged.");
        JsonElement udp = report.GetProperty("RealUdpProductionShaped");
        if (!udp.GetProperty("UninstrumentedBefore").GetProperty("Converged").GetBoolean() ||
            !udp.GetProperty("Instrumented").GetProperty("Converged").GetBoolean() ||
            !udp.GetProperty("UninstrumentedAfter").GetProperty("Converged").GetBoolean() ||
            udp.GetProperty("InstrumentedSourceHistory").GetProperty("DeltaRangeRequests")
                .GetUInt64() == 0)
            throw new InvalidOperationException("PF5-R1 real-UDP control did not converge.");
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

if (options.Pf4eR2Only)
{
    VerifyPf4eR2ChunkCpuEfficiency(root, options.Configuration);
    Console.WriteLine("PASS PF4E-R2 focused verification");
    return 0;
}

if (options.Pf4eR1Only)
{
    VerifyPf4eR1Reconciliation(root, options.Configuration);
    Console.WriteLine("PASS PF4E-R1 focused verification");
    return 0;
}

if (options.Pf4fOnly)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf4f-only requires the actual exported Online product.");
    foreach (string project in new[]
             {
                 "_Tests/UltraSim.Network.Tests/UltraSim.Network.Tests.csproj",
                 "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
                 "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
                 "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
                 "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
             })
    {
        var command = new List<string> { "run", "--project", project, "-c", options.Configuration };
        if (project.Contains("NetworkBots", StringComparison.Ordinal))
            command.Add("-p:UltraSimNetworkFlavor=ClientOnly");
        Run(root, "dotnet", command);
    }
    VerifyPf4fSelectiveCompression(root, options.Configuration);
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: options.Pf3NativeWarmupSeconds,
            measurementSeconds: options.Pf3NativeMeasurementSeconds,
            evidenceOutputDirectory: options.Pf3NativeEvidenceOutputDirectory,
            telemetryCheckpoint: "PF4F",
            externalImpairment: true);
    }
    finally
    {
        if (Directory.Exists(product.Scope)) Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF4F focused verification");
    return 0;
}

static void VerifyPf4fSelectiveCompression(string root, string configuration)
{
    string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf4f");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string oracle = Path.Combine(scope, "oracle.json");
    string canonical = Path.Combine(scope, "canonical.json");
    string compressed = Path.Combine(scope, "compressed.json");
    try
    {
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow", "--pf4f-compression-oracle",
            "--iterations=2", $"--output={oracle}",
        ]);
        using (JsonDocument oracleDocument = JsonDocument.Parse(File.ReadAllText(oracle)))
        {
            JsonElement report = oracleDocument.RootElement;
            if (report.GetProperty("Schema").GetString() !=
                    "ultrasim-network-pf4f-compression-oracle-v1" ||
                report.GetProperty("WrapperBytes").GetInt32() != 5 ||
                report.GetProperty("Payloads").GetInt32() != 1_024 ||
                report.GetProperty("Workloads").GetArrayLength() != 4)
                throw new InvalidOperationException("PF4F oracle corpus/schema is incomplete.");
            JsonElement lz4 = report.GetProperty("Candidates").EnumerateArray().Single(
                static item => item.GetProperty("Codec").GetString() == "ultrasim-lz4-block-v1");
            if (lz4.GetProperty("CompleteRatio").GetDouble() >= 0.8 ||
                lz4.GetProperty("EncodeAllocatedBytes").GetInt64() != 0 ||
                lz4.GetProperty("DecodeAllocatedBytes").GetInt64() != 0 ||
                lz4.GetProperty("ProfitableMessages").GetInt32() == 0)
                throw new InvalidOperationException("PF4F bounded codec lost its oracle contract.");
            bool selectedCurve = report.GetProperty("Thresholds").EnumerateArray().Any(
                static item => item.GetProperty("MinimumCanonicalBytes").GetInt32() == 384 &&
                    item.GetProperty("MinimumNetSavingsBytes").GetInt32() == 32 &&
                    item.GetProperty("SelectedMessages").GetInt32() != 0);
            if (!selectedCurve)
                throw new InvalidOperationException("PF4F omitted its selected threshold curve.");
        }

        string[] common =
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow",
            "--pf4e-r1-rated-reconciliation",
            "--curves=chunk-production-localized-multichunk", "--populations=8,16",
            "--warmup=3", "--ticks=20", "--retain-full-ladder", "--probes-off",
        ];
        Run(root, "dotnet", [.. common, "--compression=off", $"--output={canonical}"]);
        Run(root, "dotnet", [.. common, "--compression=selected", $"--output={compressed}"]);
        using JsonDocument canonicalDocument = JsonDocument.Parse(File.ReadAllText(canonical));
        using JsonDocument compressedDocument = JsonDocument.Parse(File.ReadAllText(compressed));
        JsonElement[] before = canonicalDocument.RootElement.GetProperty("Rows")
            .EnumerateArray().Select(static row => row.Clone()).ToArray();
        JsonElement[] after = compressedDocument.RootElement.GetProperty("Rows")
            .EnumerateArray().Select(static row => row.Clone()).ToArray();
        if (before.Length != 2 || after.Length != 2)
            throw new InvalidOperationException("PF4F rated smoke omitted a population.");
        foreach (JsonElement compressedRow in after)
        {
            int recipients = compressedRow.GetProperty("Recipients").GetInt32();
            JsonElement canonicalRow = before.Single(
                row => row.GetProperty("Recipients").GetInt32() == recipients);
            JsonElement census = compressedRow.GetProperty("ProfileCounters").GetProperty("Compression");
            if (!canonicalRow.GetProperty("Converged").GetBoolean() ||
                !compressedRow.GetProperty("Converged").GetBoolean() ||
                canonicalRow.GetProperty("SemanticDigest").GetString() !=
                    compressedRow.GetProperty("SemanticDigest").GetString() ||
                census.GetProperty("Selected").GetUInt64() == 0 ||
                census.GetProperty("CodecFailures").GetUInt64() != 0 ||
                census.GetProperty("CanonicalBytesSelected").GetUInt64() -
                    census.GetProperty("CompressedRepresentationBytes").GetUInt64() !=
                    census.GetProperty("DirectBytesSaved").GetUInt64() ||
                compressedRow.GetProperty("Transport").GetProperty("WireBytes").GetUInt64() >=
                    canonicalRow.GetProperty("Transport").GetProperty("WireBytes").GetUInt64() ||
                compressedRow.GetProperty("Transport").GetProperty("CapacityDrops").GetUInt64() != 0)
                throw new InvalidOperationException(
                    $"PF4F semantic/transactional rated smoke failed at {recipients} recipients.");
        }
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

static void VerifyPf4eR2ChunkCpuEfficiency(string root, string configuration)
{
    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf4e-r2");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string micro = Path.Combine(scope, "staging-micro.json");
    string attribution = Path.Combine(scope, "attribution.json");
    try
    {
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
            "-c", configuration,
        ]);
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow",
            "--pf4e-r2-staging-microbench", "--micro-warmup=1000",
            "--micro-iterations=5000", "--micro-samples=7", $"--output={micro}",
        ]);
        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(micro)))
        {
            JsonElement report = document.RootElement;
            if (report.GetProperty("Schema").GetString() !=
                "ultrasim-network-pf4e-r2-staging-microbench-v1" ||
                report.GetProperty("Rows").GetArrayLength() != 6)
                throw new InvalidOperationException("PF4E-R2 staging microbench coverage is incomplete.");
            foreach (JsonElement row in report.GetProperty("Rows").EnumerateArray())
                if (row.GetProperty("OldAllocatedBytes").GetInt64() != 0 ||
                    row.GetProperty("DirectAllocatedBytes").GetInt64() != 0 ||
                    row.GetProperty("EncodedBytes").GetInt32() <= 0)
                    throw new InvalidOperationException(
                        "PF4E-R2 staging microbench lost canonical/zero-allocation behavior.");
        }
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow", "--pf4e-r2-chunk-cpu",
            "--implementation-label=verification", "--warmup=3", "--ticks=20",
            "--populations=8,16", $"--output={attribution}",
        ]);
        using JsonDocument attributionDocument = JsonDocument.Parse(File.ReadAllText(attribution));
        JsonElement rootElement = attributionDocument.RootElement;
        if (rootElement.GetProperty("Schema").GetString() !=
            "ultrasim-network-pf4e-r2-chunk-cpu-attribution-v1" ||
            rootElement.GetProperty("Rows").GetArrayLength() != 8)
            throw new InvalidOperationException("PF4E-R2 attribution coverage is incomplete.");
        int chunkRows = 0;
        bool bundled = false;
        bool reuse = false;
        foreach (JsonElement row in rootElement.GetProperty("Rows").EnumerateArray())
        {
            if (!row.GetProperty("Converged").GetBoolean() ||
                row.GetProperty("Transport").GetProperty("CapacityDrops").GetUInt64() != 0 ||
                row.GetProperty("Transport").GetProperty("Retransmissions").GetUInt64() != 0)
                throw new InvalidOperationException(
                    "PF4E-R2 attribution row failed convergence/drop/retransmission gates.");
            if (row.GetProperty("Profile").GetString() != "ChunkPublication") continue;
            chunkRows++;
            JsonElement counters = row.GetProperty("ProfileCounters");
            JsonElement cpu = counters.GetProperty("ChunkCpu");
            if (!cpu.GetProperty("Enabled").GetBoolean() ||
                cpu.GetProperty("CanonicalReparseCalls").GetUInt64() != 0 ||
                cpu.GetProperty("CanonicalReparseTicks").GetUInt64() != 0 ||
                cpu.GetProperty("SourceReadCalls").GetUInt64() == 0 ||
                cpu.GetProperty("NetworkQueueCalls").GetUInt64() == 0 ||
                cpu.GetProperty("ProofRetirementCalls").GetUInt64() == 0)
                throw new InvalidOperationException(
                    "PF4E-R2 direct staging or attribution invariants failed.");
            JsonElement ranges = counters.GetProperty("DeltaRangeReuse");
            reuse |= ranges.GetProperty("ReusableRequests").GetUInt64() != 0 &&
                ranges.GetProperty("ReusableRecords").GetUInt64() != 0;
            bundled |= counters.GetProperty("ChunkBundleMessages").GetUInt64() != 0 &&
                cpu.GetProperty("BundleEncodeCalls").GetUInt64() != 0;
        }
        if (chunkRows != 6 || !bundled || !reuse)
            throw new InvalidOperationException(
                "PF4E-R2 did not exercise all Chunk rows, bundling, and range reuse.");
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

if (options.Pf1Only)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf1-only requires the actual exported Online product.");
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
        "-c", options.Configuration,
        "-p:UltraSimNetworkFlavor=ClientOnly",
    ]);
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf1MixedExportedSwarm(root, options.Configuration, product.Executable);
    }
    finally
    {
        if (Directory.Exists(product.Scope))
            Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF1 focused verification");
    return 0;
}

static void VerifyPf4eR1Reconciliation(string root, string configuration)
{
    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf4e-r1");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string output = Path.Combine(scope, "reconciliation.json");
    try
    {
        Run(root, "dotnet",
        [
            "run", "--project",
            "_Benchmarks/UltraSim.Network.V3ComparisonAdapter/UltraSim.Network.V3ComparisonAdapter.csproj",
            "-c", configuration, "--", "--p03-whole-flow",
            "--pf4e-r1-rated-reconciliation", "--warmup=3", "--ticks=20",
            "--populations=8,16", "--retain-full-ladder", "--probes-off",
            $"--output={output}",
        ]);
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(output));
        JsonElement rootElement = document.RootElement;
        if (rootElement.GetProperty("Schema").GetString() !=
            "ultrasim-network-pf4e-r1-rated-replication-scaling-reconciliation-v1")
            throw new InvalidOperationException("PF4E-R1 emitted an unexpected report schema.");
        JsonElement rows = rootElement.GetProperty("Rows");
        if (rows.GetArrayLength() != 10)
            throw new InvalidOperationException("PF4E-R1 did not emit two rows for all five curves.");
        var byCurve = new Dictionary<string, List<JsonElement>>(StringComparer.Ordinal);
        foreach (JsonElement row in rows.EnumerateArray())
        {
            if (!row.GetProperty("Converged").GetBoolean() ||
                row.GetProperty("Transport").GetProperty("CapacityDrops").GetUInt64() != 0 ||
                row.GetProperty("Transport").GetProperty("Retransmissions").GetUInt64() != 0)
                throw new InvalidOperationException(
                    "PF4E-R1 smoke row failed convergence/drop/retransmission gates.");
            string curve = row.GetProperty("Curve").GetString() ?? "";
            if (!byCurve.TryGetValue(curve, out List<JsonElement>? curveRows))
                byCurve.Add(curve, curveRows = []);
            curveRows.Add(row.Clone());
            JsonElement normalized = row.GetProperty("Normalized");
            if (normalized.GetProperty("SemanticPublicationsPerRecipientTick").GetDouble() <= 0 ||
                normalized.GetProperty("WireBytesPerRecipientTick").GetDouble() <= 0)
                throw new InvalidOperationException("PF4E-R1 omitted normalized useful work.");
        }
        string[] expected =
        [
            "current-dirty-entity-dense",
            "current-maintained-entity-sparse",
            "pf4e-existing-chunk-dense",
            "chunk-unbatchable-single-dirty-chunk",
            "chunk-production-localized-multichunk",
        ];
        if (expected.Any(curve => !byCurve.TryGetValue(curve, out List<JsonElement>? values) ||
                values.Count != 2))
            throw new InvalidOperationException("PF4E-R1 curve coverage is incomplete.");
        ulong Sum(string curve, string counter) => checked(byCurve[curve].Aggregate(
            0UL, (sum, row) => sum + row.GetProperty("ProfileCounters")
                .GetProperty(counter).GetUInt64()));
        if (Sum("pf4e-existing-chunk-dense", "ChunkBundleMessages") == 0 ||
            Sum("chunk-production-localized-multichunk", "ChunkBundleMessages") == 0 ||
            Sum("chunk-production-localized-multichunk", "SimultaneouslyDuePublications") == 0)
            throw new InvalidOperationException(
                "PF4E-R1 failed to engage natural selected-profile multi-chunk bundling.");
        if (Sum("chunk-unbatchable-single-dirty-chunk", "ChunkBundleMessages") != 0)
            throw new InvalidOperationException(
                "PF4E-R1 unbatchable control unexpectedly emitted a message-129 bundle.");
    }
    finally
    {
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

if (options.Pf2Only)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf2-only requires the actual exported Online product.");
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Network.Tests/UltraSim.Network.Tests.csproj",
        "-c", options.Configuration,
    ]);
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
        "-c", options.Configuration,
    ]);
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
        "-c", options.Configuration,
        "-p:UltraSimNetworkFlavor=ClientOnly",
    ]);
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable);
    }
    finally
    {
        if (Directory.Exists(product.Scope))
            Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF2 focused verification");
    return 0;
}

if (options.Pf25Only)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf2.5-only requires the actual exported Online product.");
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Network.Tests/UltraSim.Network.Tests.csproj",
        "-c", options.Configuration,
    ]);
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
        "-c", options.Configuration,
    ]);
    Run(root, "dotnet",
    [
        "run", "--project",
        "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
        "-c", options.Configuration,
        "-p:UltraSimNetworkFlavor=ClientOnly",
    ]);
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable, detailedTelemetry: true,
            warmupSeconds: options.Pf25LongNative ? 60 : 5,
            measurementSeconds: options.Pf25LongNative ? 300 : 20,
            evidenceOutputDirectory: options.Pf25EvidenceOutputDirectory);
    }
    finally
    {
        if (Directory.Exists(product.Scope))
            Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF2.5 focused observability verification");
    return 0;
}

if (options.Pf3Only || options.Pf3NativeOnly)
{
    if (options.SkipGodot)
        throw new ArgumentException("PF3 verification requires the actual exported Online product.");
    if (!options.Pf3NativeOnly)
    {
        foreach (string project in new[]
                 {
                     "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
                     "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
                     "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
                 })
        {
            var command = new List<string>
            {
                "run", "--project", project, "-c", options.Configuration,
            };
            if (project.Contains("NetworkBots", StringComparison.Ordinal))
                command.Add("-p:UltraSimNetworkFlavor=ClientOnly");
            Run(root, "dotnet", command);
        }
        VerifyPf3InputCadenceMatrix(root, options.Configuration);
    }
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: options.Pf3NativeWarmupSeconds,
            measurementSeconds: options.Pf3NativeMeasurementSeconds,
            evidenceOutputDirectory: options.Pf3NativeEvidenceOutputDirectory,
            telemetryCheckpoint: "PF3",
            aliceRenderMode: options.Pf3NativeAliceRenderMode,
            bobRenderMode: options.Pf3NativeBobRenderMode);
    }
    finally
    {
        if (Directory.Exists(product.Scope))
            Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine(options.Pf3NativeOnly
        ? "PASS PF3 native-only verification"
        : "PASS PF3 focused verification");
    return 0;
}

if (options.Pf4aOnly)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf4a-only requires the actual exported Online product.");
    foreach (string project in new[]
             {
                 "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
                 "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
                 "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
                 "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
             })
    {
        var command = new List<string>
        {
            "run", "--project", project, "-c", options.Configuration,
        };
        if (project.Contains("NetworkBots", StringComparison.Ordinal))
            command.Add("-p:UltraSimNetworkFlavor=ClientOnly");
        Run(root, "dotnet", command);
    }
    VerifyPf4aQuietReadiness(root, options.Configuration);
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: options.Pf3NativeWarmupSeconds,
            measurementSeconds: options.Pf3NativeMeasurementSeconds,
            evidenceOutputDirectory: options.Pf3NativeEvidenceOutputDirectory,
            telemetryCheckpoint: "PF4A");
    }
    finally
    {
        if (Directory.Exists(product.Scope))
            Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF4A focused verification");
    return 0;
}

if (options.Pf4bOnly)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf4b-only requires the actual exported Online product.");
    foreach (string project in new[]
             {
                 "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
                 "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
                 "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
                 "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
             })
    {
        var command = new List<string>
        {
            "run", "--project", project, "-c", options.Configuration,
        };
        if (project.Contains("NetworkBots", StringComparison.Ordinal))
            command.Add("-p:UltraSimNetworkFlavor=ClientOnly");
        Run(root, "dotnet", command);
    }
    VerifyPf4bKnowledgeCensus(root, options.Configuration);
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: options.Pf3NativeWarmupSeconds,
            measurementSeconds: options.Pf3NativeMeasurementSeconds,
            evidenceOutputDirectory: options.Pf3NativeEvidenceOutputDirectory,
            telemetryCheckpoint: "PF4B");
    }
    finally
    {
        if (Directory.Exists(product.Scope))
            Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF4B focused verification");
    return 0;
}

if (options.Pf4cOnly)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf4c-only requires the actual exported Online product.");
    foreach (string project in new[]
             {
                 "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
                 "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
                 "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
                 "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
             })
    {
        var command = new List<string>
        {
            "run", "--project", project, "-c", options.Configuration,
        };
        if (project.Contains("NetworkBots", StringComparison.Ordinal))
            command.Add("-p:UltraSimNetworkFlavor=ClientOnly");
        Run(root, "dotnet", command);
    }
    VerifyPf4cSelectedBundling(root, options.Configuration);
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: options.Pf3NativeWarmupSeconds,
            measurementSeconds: options.Pf3NativeMeasurementSeconds,
            evidenceOutputDirectory: options.Pf3NativeEvidenceOutputDirectory,
            telemetryCheckpoint: "PF4C");
    }
    finally
    {
        if (Directory.Exists(product.Scope))
            Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF4C focused verification");
    return 0;
}

if (options.Pf4dOnly)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf4d-only requires the actual exported Online product.");
    foreach (string project in new[]
             {
                 "_Tests/UltraSim.Network.Tests/UltraSim.Network.Tests.csproj",
                 "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
                 "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
                 "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
                 "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
             })
    {
        var command = new List<string>
        {
            "run", "--project", project, "-c", options.Configuration,
        };
        if (project.Contains("NetworkBots", StringComparison.Ordinal))
            command.Add("-p:UltraSimNetworkFlavor=ClientOnly");
        Run(root, "dotnet", command);
    }
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: options.Pf3NativeWarmupSeconds,
            measurementSeconds: options.Pf3NativeMeasurementSeconds,
            evidenceOutputDirectory: options.Pf3NativeEvidenceOutputDirectory,
            telemetryCheckpoint: "PF4D");
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: 5,
            measurementSeconds: 30,
            evidenceOutputDirectory: options.Pf3NativeEvidenceOutputDirectory is null
                ? null
                : options.Pf3NativeEvidenceOutputDirectory + "-impaired",
            telemetryCheckpoint: "PF4D",
            externalImpairment: true);
    }
    finally
    {
        if (Directory.Exists(product.Scope))
            Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF4D focused verification");
    return 0;
}

if (options.Pf4eOnly)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf4e-only requires the actual exported Online product.");
    foreach (string project in new[]
             {
                 "_Tests/UltraSim.Network.Tests/UltraSim.Network.Tests.csproj",
                 "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
                 "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
                 "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
                 "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
             })
    {
        var command = new List<string>
        {
            "run", "--project", project, "-c", options.Configuration,
        };
        if (project.Contains("NetworkBots", StringComparison.Ordinal))
            command.Add("-p:UltraSimNetworkFlavor=ClientOnly");
        Run(root, "dotnet", command);
    }
    string godot = ResolveGodot(options.GodotPath);
    ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
    try
    {
        VerifyPf2MixedExportedAccountingSwarm(
            root, options.Configuration, product.Executable,
            detailedTelemetry: true,
            warmupSeconds: options.Pf3NativeWarmupSeconds,
            measurementSeconds: options.Pf3NativeMeasurementSeconds,
            evidenceOutputDirectory: options.Pf3NativeEvidenceOutputDirectory,
            telemetryCheckpoint: "PF4E",
            externalImpairment: true);
    }
    finally
    {
        if (Directory.Exists(product.Scope))
            Directory.Delete(product.Scope, recursive: true);
    }
    Console.WriteLine("PASS PF4E focused verification");
    return 0;
}

static void VerifyPf4cSelectedBundling(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "build", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ServerOnly",
    ]);
    Run(root, "dotnet",
    [
        "build", "Tools/NetworkBots/UltraSim.NetworkBots.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ClientOnly",
    ]);
    string parent = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf4c-selected-bundling");
    if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    Directory.CreateDirectory(parent);
    string output = Path.Combine(parent, "run");
    string cache = Path.Combine(parent, "cache");
    try
    {
        _ = RunCaptured(
            root,
            Path.Combine(root, "Tools", "NetworkBots", "run-pf4c-baseline.sh"),
            ["multi-hot", "15s", output, "50", "400604", "60s", cache],
            240_000);
        using JsonDocument botDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output, "bot-summary.json")));
        JsonElement bot = botDocument.RootElement;
        if (!bot.GetProperty("passed").GetBoolean() ||
            bot.GetProperty("checkpoint").GetString() != "PF4C" ||
            bot.GetProperty("schemaVersion").GetInt32() != 8 ||
            !bot.GetProperty("cleanRetirement").GetBoolean() ||
            bot.GetProperty("actionOutcomes").EnumerateArray().Any(static outcome =>
                !outcome.GetProperty("conserved").GetBoolean() ||
                !outcome.GetProperty("terminal").GetBoolean() ||
                outcome.GetProperty("unexplained").GetUInt64() != 0))
            throw new InvalidOperationException(
                "The PF4C selected-profile product fixture did not retire cleanly.");

        using JsonDocument accountingDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output, "accounting-summary.json")));
        JsonElement accounting = accountingDocument.RootElement;
        ulong bundles = 0, entries = 0, messagesSaved = 0, applied = 0;
        ulong contradictory = 0, superseded = 0, unknownAfterSupersession = 0;
        foreach (JsonElement application in
                 accounting.GetProperty("RemoteApplications").EnumerateArray())
        {
            JsonElement replication = application.GetProperty("Timing")
                .GetProperty("Replication");
            JsonElement readiness = application.GetProperty("Timing")
                .GetProperty("ReplicationReadiness");
            JsonElement knowledge = replication.GetProperty("Knowledge");
            JsonElement bundle = knowledge.GetProperty("Bundle");
            JsonElement proof = knowledge.GetProperty("Proof");
            bundles += bundle.GetProperty("Bundles").GetUInt64();
            entries += bundle.GetProperty("Entries").GetUInt64();
            messagesSaved += bundle.GetProperty("GameMessagesSaved").GetUInt64();
            ulong received = proof.GetProperty("EntriesReceived").GetUInt64();
            ulong appliedEntries = proof.GetProperty("AppliedEntries").GetUInt64();
            ulong rejected = proof.GetProperty("RejectedEntries").GetUInt64();
            ulong capacity = proof.GetProperty("CapacityExceededEntries").GetUInt64();
            ulong contradictoryEntries =
                proof.GetProperty("ContradictoryEntries").GetUInt64();
            ulong malformedOrUnknown =
                proof.GetProperty("MalformedOrUnknownEntries").GetUInt64();
            ulong advancing = proof.GetProperty("KnowledgeAdvancingEntries").GetUInt64();
            ulong nonAdvancing = proof.GetProperty("NonAdvancingAppliedEntries").GetUInt64();
            ulong supersededEntries =
                proof.GetProperty("SupersededOutstandingRetired").GetUInt64();
            applied += advancing;
            contradictory += contradictoryEntries;
            superseded += supersededEntries;
            unknownAfterSupersession += malformedOrUnknown;
            if (knowledge.GetProperty("Outstanding").GetProperty("HighWater").GetInt32() > 128 ||
                rejected != 0 || capacity != 0 || nonAdvancing != 0 ||
                appliedEntries != advancing + nonAdvancing ||
                received != appliedEntries + rejected + capacity + contradictoryEntries +
                    malformedOrUnknown ||
                malformedOrUnknown > supersededEntries)
                throw new InvalidOperationException(
                    "PF4C selected-profile debt or proof outcomes diverged.");
            JsonElement perStep = readiness.GetProperty("OrdinaryPublicationsPerAuthorityStep");
            JsonElement messagesPerStep = readiness.GetProperty("GameMessagesPerAuthorityStep");
            JsonElement bundlesPerStep = readiness.GetProperty("BundlesPerAuthorityStep");
            JsonElement bytesPerStep = readiness.GetProperty("ApplicationBytesPerAuthorityStep");
            if (perStep.GetProperty("Count").GetUInt64() == 0 ||
                perStep.GetProperty("Total").GetUInt64() == 0 ||
                messagesPerStep.GetProperty("Count").GetUInt64() !=
                    perStep.GetProperty("Count").GetUInt64() ||
                bundlesPerStep.GetProperty("Count").GetUInt64() !=
                    perStep.GetProperty("Count").GetUInt64() ||
                bytesPerStep.GetProperty("Count").GetUInt64() !=
                    perStep.GetProperty("Count").GetUInt64() ||
                messagesPerStep.GetProperty("Total").GetUInt64() >
                    perStep.GetProperty("Total").GetUInt64() ||
                bundlesPerStep.GetProperty("Total").GetUInt64() == 0 ||
                bytesPerStep.GetProperty("Total").GetUInt64() == 0)
                throw new InvalidOperationException(
                    "PF4C per-authority-step publication/bundle burst telemetry did not reconcile.");
            JsonElement freshness = knowledge.GetProperty("Freshness");
            if (freshness.GetProperty("Immediate").GetProperty("Publications").GetUInt64() == 0)
                throw new InvalidOperationException(
                    "PF4C omitted Immediate source-to-publication freshness observations.");
        }
        if (bundles == 0 || entries < bundles * 2 || messagesSaved == 0 || applied == 0 ||
            !accounting.GetProperty("PhysicalTransportReconciled").GetBoolean() ||
            !accounting.GetProperty("LogicalMessageReconciled").GetBoolean())
            throw new InvalidOperationException(
                "PF4C did not exercise beneficial message-129 selected-profile bundling.");
        Console.WriteLine(
            $"PASS PF4C_SelectedBundling clients=50 bundles={bundles} entries={entries} " +
            $"game_messages_saved={messagesSaved} knowledge_advancements={applied} " +
            $"contradictory={contradictory} superseded={superseded} " +
            $"unknown_after_supersession={unknownAfterSupersession}");
    }
    finally
    {
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }
}

static void VerifyPf4bKnowledgeCensus(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "build", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ServerOnly",
    ]);
    Run(root, "dotnet",
    [
        "build", "Tools/NetworkBots/UltraSim.NetworkBots.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ClientOnly",
    ]);
    string parent = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf4b-knowledge-census");
    if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    Directory.CreateDirectory(parent);
    string output = Path.Combine(parent, "run");
    string cache = Path.Combine(parent, "cache");
    try
    {
        _ = RunCaptured(
            root,
            Path.Combine(root, "Tools", "NetworkBots", "run-pf4b-baseline.sh"),
            // The focused window must begin after passive spawn/fall recovery has quiesced;
            // otherwise those legitimate establishment changes contaminate the one-hot census.
            ["single-hot", "15s", output, "50", "400504", "60s", cache],
            240_000);
        using JsonDocument botDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output, "bot-summary.json")));
        JsonElement bot = botDocument.RootElement;
        if (!bot.GetProperty("passed").GetBoolean() ||
            bot.GetProperty("checkpoint").GetString() != "PF4B" ||
            bot.GetProperty("schemaVersion").GetInt32() != 7 ||
            !bot.GetProperty("cleanRetirement").GetBoolean())
            throw new InvalidOperationException("The PF4B client census did not retire cleanly.");

        ulong generated = 0, queued = 0, envelopes = 0;
        foreach (JsonElement client in bot.GetProperty("clientTelemetry").EnumerateArray())
        {
            JsonElement proof = client.GetProperty("client").GetProperty("world")
                .GetProperty("proofCensus");
            generated += proof.GetProperty("entriesGenerated").GetUInt64();
            queued += proof.GetProperty("entriesQueued").GetUInt64();
            envelopes += proof.GetProperty("envelopesQueued").GetUInt64();
            if (proof.GetProperty("queueCurrent").GetInt32() != 0 ||
                proof.GetProperty("queueHighWater").GetInt32() >
                    proof.GetProperty("queueCapacity").GetInt32())
                throw new InvalidOperationException("A PF4B proof queue did not remain bounded.");
        }
        if (generated == 0 || generated != queued || envelopes == 0 || envelopes > queued)
            throw new InvalidOperationException("PF4B client proof conservation failed.");

        using JsonDocument accountingDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output, "accounting-summary.json")));
        JsonElement accounting = accountingDocument.RootElement;
        ulong serverEntries = 0, advancements = 0, deltaMessages = 0;
        foreach (JsonElement application in
                 accounting.GetProperty("RemoteApplications").EnumerateArray())
        {
            JsonElement knowledge = application.GetProperty("Timing")
                .GetProperty("Replication").GetProperty("Knowledge");
            JsonElement proof = knowledge.GetProperty("Proof");
            JsonElement delta = knowledge.GetProperty("Delta");
            ulong entries = proof.GetProperty("EntriesReceived").GetUInt64();
            ulong applied = proof.GetProperty("AppliedEntries").GetUInt64();
            ulong nonApplied = proof.GetProperty("RejectedEntries").GetUInt64() +
                proof.GetProperty("CapacityExceededEntries").GetUInt64() +
                proof.GetProperty("ContradictoryEntries").GetUInt64();
            ulong malformed = proof.GetProperty("MalformedOrUnknownEntries").GetUInt64();
            ulong advancing = proof.GetProperty("KnowledgeAdvancingEntries").GetUInt64();
            ulong nonAdvancing = proof.GetProperty("NonAdvancingAppliedEntries").GetUInt64();
            if (entries != applied + nonApplied + malformed ||
                applied != advancing + nonAdvancing)
                throw new InvalidOperationException("PF4B server proof census did not reconcile.");
            serverEntries += entries;
            advancements += advancing;
            deltaMessages += delta.GetProperty("Messages").GetUInt64();
            if (delta.GetProperty("UniqueChunkCensusOverflowed").GetBoolean())
                throw new InvalidOperationException("The PF4B unique-chunk census saturated.");
            if (delta.GetProperty("UniqueChunks").GetUInt64() != 1)
                throw new InvalidOperationException(
                    "The PF4B single-hot fixture produced more than one Delta chunk.");
            if (knowledge.GetProperty("Outstanding").GetProperty("HighWater").GetInt32() > 128)
                throw new InvalidOperationException("The PF4B delivery ledger exceeded its bound.");
        }
        if (serverEntries == 0 || advancements != serverEntries ||
            accounting.GetProperty("AuthoritativePublications")
                .GetProperty("PublishedChunkRevisions").GetUInt64() == 0)
            throw new InvalidOperationException("PF4B server knowledge telemetry was incomplete.");
        Console.WriteLine(
            $"PASS PF4B_KnowledgeCensus clients=50 generated={generated} " +
            $"envelopes={envelopes} advancements={advancements} deltas={deltaMessages}");
    }
    finally
    {
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }
}

static void VerifyPf4aQuietReadiness(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "build", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ServerOnly",
    ]);
    Run(root, "dotnet",
    [
        "build", "Tools/NetworkBots/UltraSim.NetworkBots.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ClientOnly",
    ]);

    string parent = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf4a-quiet-readiness");
    if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    Directory.CreateDirectory(parent);
    string output = Path.Combine(parent, "run");
    string cache = Path.Combine(parent, "cache");
    try
    {
        _ = RunCaptured(
            root,
            Path.Combine(root, "Tools", "NetworkBots", "run-pf4a-baseline.sh"),
            ["quiet", "10s", output, "8", "400401", "5s", cache],
            180_000);

        using JsonDocument botDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output, "bot-summary.json")));
        JsonElement bot = botDocument.RootElement;
        if (!bot.GetProperty("passed").GetBoolean() ||
            bot.GetProperty("checkpoint").GetString() != "PF4A" ||
            bot.GetProperty("schemaVersion").GetInt32() != 6 ||
            !bot.GetProperty("cleanRetirement").GetBoolean() ||
            bot.GetProperty("actionOutcomes").EnumerateArray().Any(static outcome =>
                !outcome.GetProperty("conserved").GetBoolean() ||
                !outcome.GetProperty("terminal").GetBoolean() ||
                outcome.GetProperty("unexplained").GetUInt64() != 0))
            throw new InvalidOperationException(
                "The PF4A quiet bot lifetime did not conserve and retire cleanly.");

        using JsonDocument accountingDocument = JsonDocument.Parse(
            File.ReadAllText(Path.Combine(output, "accounting-summary.json")));
        JsonElement accounting = accountingDocument.RootElement;
        if (accounting.GetProperty("ParticipantsAtStart").GetInt32() != 8 ||
            accounting.GetProperty("ParticipantsAtEnd").GetInt32() != 8 ||
            accounting.GetProperty("RemoteApplications").GetArrayLength() != 8)
            throw new InvalidOperationException(
                "The PF4A quiet accounting window did not retain all eight participants.");

        ulong drivePasses = 0;
        ulong emptyCommits = 0;
        foreach (JsonElement application in
                 accounting.GetProperty("RemoteApplications").EnumerateArray())
        {
            JsonElement timing = application.GetProperty("Timing");
            JsonElement readiness = timing.GetProperty("ReplicationReadiness");
            JsonElement scheduler = readiness.GetProperty("Scheduler");
            JsonElement work = timing.GetProperty("Replication").GetProperty("Work");
            ulong applicationDrivePasses = readiness.GetProperty("DrivePasses").GetUInt64();
            ulong applicationEmptyCommits = scheduler.GetProperty("EmptyCommitsObserved").GetUInt64();
            if (applicationDrivePasses == 0 ||
                readiness.GetProperty("CleanPasses").GetUInt64() != applicationDrivePasses ||
                scheduler.GetProperty("CommitsObserved").GetUInt64() != applicationEmptyCommits ||
                scheduler.GetProperty("DirtyCommitsObserved").GetUInt64() != 0 ||
                scheduler.GetProperty("PendingMembershipsExamined").GetUInt64() != 0 ||
                scheduler.GetProperty("DueWorkDequeued").GetUInt64() != 0 ||
                scheduler.GetProperty("PendingWorkCount").GetInt32() != 0 ||
                work.GetProperty("ScheduleAttempts").GetUInt64() != 0 ||
                work.GetProperty("DeliveriesQueued").GetUInt64() != 0)
                throw new InvalidOperationException(
                    "A PF4A quiet recipient entered ChunkPublication scheduling work.");
            drivePasses = checked(drivePasses + applicationDrivePasses);
            emptyCommits = checked(emptyCommits + applicationEmptyCommits);
        }
        if (drivePasses == 0 || emptyCommits != drivePasses)
            throw new InvalidOperationException(
                "PF4A quiet readiness did not observe one empty source commit per drive pass.");
        Console.WriteLine(
            $"PASS PF4A_QuietReadiness recipients=8 passes={drivePasses} " +
            "examined=0 dequeued=0 deliveries=0");
    }
    finally
    {
        if (Directory.Exists(parent)) Directory.Delete(parent, recursive: true);
    }
}

string[] configurations = options.AllConfigurations ? ["Debug", "Release"] : [options.Configuration];
bool contentlessOnlineVerified = false;
bool finalClientExportsVerified = false;
bool embeddedCompleteHostedVerified = false;
bool mixedSessionLifecycleVerified = false;
bool pf1MixedSwarmVerified = false;
bool pf2AccountingSwarmVerified = false;
string[] testProjects =
[
    "_Tests/UltraSim.ControlPlane.Tests/UltraSim.ControlPlane.Tests.csproj",
    "_Tests/UltraSim.Framework.RuntimeHost.Tests/UltraSim.Framework.RuntimeHost.Tests.csproj",
    "_Tests/UltraSim.Framework.Definitions.Tests/UltraSim.Framework.Definitions.Tests.csproj",
    "_Tests/UltraSim.Framework.Persistence.Tests/UltraSim.Framework.Persistence.Tests.csproj",
    "_Tests/UltraSim.Server.Authoring.Tests/UltraSim.Server.Authoring.Tests.csproj",
    "_Tests/UltraSim.ECS.Tests/UltraSim.ECS.Tests.csproj",
    "_Tests/UltraSim.ECS.Generator.Tests/UltraSim.ECS.Generator.Tests.csproj",
    "_Tests/UltraSim.ECS.Comparison.Tests/UltraSim.ECS.Comparison.Tests.csproj",
    "_Tests/UltraSim.ECS.BitmapExperiment.Tests/UltraSim.ECS.BitmapExperiment.Tests.csproj",
    "_Tests/UltraSim.Server.Persistence.Tests/UltraSim.Server.Persistence.Tests.csproj",
    "_Tests/UltraSim.Server.Runtime.Tests/UltraSim.Server.Runtime.Tests.csproj",
    "_Tests/UltraSim.Server.Hosting.Tests/UltraSim.Server.Hosting.Tests.csproj",
    "_Tests/UltraSim.UI.Tests/UltraSim.UI.Tests.csproj",
    "_Tests/UltraSim.Network.Tests/UltraSim.Network.Tests.csproj",
    "_Tests/UltraSim.Network.Generator.Tests/UltraSim.Network.Generator.Tests.csproj",
    "_Tests/UltraSim.Network.Comparison.Tests/UltraSim.Network.Comparison.Tests.csproj",
    "_Tests/UltraSim.Network.Application.Tests/UltraSim.Network.Application.Tests.csproj",
    "_Tests/UltraSim.Game.Replication.Tests/UltraSim.Game.Replication.Tests.csproj",
    "_Tests/UltraSim.Game.World.Tests/UltraSim.Game.World.Tests.csproj",
    "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
    "_Tests/UltraSim.Client.Content.Tests/UltraSim.Client.Content.Tests.csproj",
    "_Tests/UltraSim.Client.BuiltInTestWorlds.Tests/UltraSim.Client.BuiltInTestWorlds.Tests.csproj",
    "_Tests/UltraSim.Game.Persistence.Tests/UltraSim.Game.Persistence.Tests.csproj",
    "_Tests/UltraSim.Game.Admission.Tests/UltraSim.Game.Admission.Tests.csproj",
    "_Tests/UltraSim.Game.Chat.Tests/UltraSim.Game.Chat.Tests.csproj",
    "_Tests/UltraSim.Game.Inventory.Tests/UltraSim.Game.Inventory.Tests.csproj",
    "_Tests/UltraSim.Client.Local.Tests/UltraSim.Client.Local.Tests.csproj",
    "_Tests/UltraSim.NetworkBots.Tests/UltraSim.NetworkBots.Tests.csproj",
    "_Tests/UltraSim.S0.Integration.Tests/UltraSim.S0.Integration.Tests.csproj",
];

foreach (string configuration in configurations)
{
    Console.WriteLine($"VERIFY configuration={configuration}");
    VerifyNetworkCapabilityArtifacts(root, configuration);
    foreach (string project in testProjects)
    {
        var command = new List<string> { "run", "--project", project, "-c", configuration };
        if (project.Contains("NetworkBots", StringComparison.Ordinal))
            command.Add("-p:UltraSimNetworkFlavor=ClientOnly");
        if (options.NoRestore) command.Add("--no-restore");
        Run(root, "dotnet", command);
    }

    Run(root, "dotnet",
    [
        "run", "--project", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ServerOnly",
        "--", "--ephemeral-smoke",
    ]);
    VerifyDedicatedNetworkAssembly(root, configuration);

    VerifyClientCompositionArtifacts(root, configuration);

    if (!options.SkipGodot)
    {
        const string godotProject = "_Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj";
        var build = new List<string> { "build", godotProject, "-c", configuration };
        if (options.NoRestore) build.Add("--no-restore");
        Run(root, "dotnet", build);
        // Godot's editor executable loads Debug, not Release (as for BuildClientApplication).
        // Rebuild the current acceptance sources explicitly; never use a stale development DLL.
        // Optimize native Release-campaign controls without pretending this is an exported player.
        if (!string.Equals(configuration, "Debug", StringComparison.Ordinal))
            Run(root, "dotnet", ["build", godotProject, "-c", "Debug", "-p:Optimize=true"]);
        string godot = ResolveGodot(options.GodotPath);
        Run(root, godot,
        [
            "--headless",
            "--path", "_Tests/UltraSim.S0.Godot.Tests",
            "--scene", "res://S08HeadlessAcceptance.tscn",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "_Tests/UltraSim.S0.Godot.Tests",
            "--scene", "res://T08WorldHeadlessAcceptance.tscn",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "_Tests/UltraSim.S0.Godot.Tests",
            "--scene", "res://F19bWorldBackendAcceptance.tscn",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "_Tests/UltraSim.S0.Godot.Tests",
            "--scene", "res://F110ActorTierAcceptance.tscn",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "_Tests/UltraSim.S0.Godot.Tests",
            "--scene", "res://F111RendererStressAcceptance.tscn",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "_Tests/UltraSim.S0.Godot.Tests",
            "--scene", "res://W5TwoPackHeadlessAcceptance.tscn",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "_Tests/UltraSim.S0.Godot.Tests",
            "--scene", "res://T010PlayableWorldAcceptance.tscn",
        ]);
        BuildClientApplication(root, configuration, "Complete");
        Run(root, godot,
        [
            "--headless",
            "--editor",
            "--path", "Client/Godot",
            "--quit",
        ]);
        BuildClientApplication(root, configuration, "SinglePlayer");
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--composition-smoke", "--expect-edition=SinglePlayer",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--shell-ui-smoke", "--expect-edition=SinglePlayer",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--p5-management-smoke", "--expect-edition=SinglePlayer",
        ]);
        foreach ((string variant, string human) in new[]
        {
            ("default", "x"),
            ("replace", "y"),
            ("add", "x"),
        })
        {
            Run(root, godot,
            [
                "--headless",
                "--path", "Client/Godot",
                "--", "--f0-2-smoke", "--expect-edition=SinglePlayer",
                $"--cabin-content={variant}", $"--human={human}",
            ]);
        }
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--d0-0-smoke", "--expect-edition=SinglePlayer",
        ]);
        Run(root, godot,
        [
            "--headless", "--path", "Client/Godot", "--", "--f1-9a-display-smoke",
            "--expect-edition=SinglePlayer",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--d0-1-smoke", "--expect-edition=SinglePlayer",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--d0-2-smoke", "--expect-edition=SinglePlayer",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--d0-3-smoke", "--expect-edition=SinglePlayer",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--d0-4-smoke", "--expect-edition=SinglePlayer",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--f1-3-primitive-smoke", "--expect-edition=SinglePlayer",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--d0-5-smoke", "--f1-11-vegetation", "--expect-edition=SinglePlayer",
        ]);

        BuildClientApplication(root, configuration, "Online");
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--composition-smoke", "--expect-edition=Online",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--shell-ui-smoke", "--expect-edition=Online",
        ]);
        if (!contentlessOnlineVerified)
        {
            ContentlessOnlineExport product = VerifyContentlessOnlineExport(root, godot);
            try
            {
                FinalClientProductExports finalProducts = VerifyFinalClientProductExports(
                    root, godot, product.Scope);
                finalClientExportsVerified = true;
                VerifyContentlessOnlineNative(
                    root, configuration, product.Executable,
                    ["--headless", "--"]);
                VerifyEmbeddedCompleteHostedNative(
                    root, product.Executable, finalProducts.CompleteExecutable);
                VerifyMixedSessionLifecycleNative(root, configuration, godot,
                    finalProducts.CompleteExecutable);
                mixedSessionLifecycleVerified = true;
                embeddedCompleteHostedVerified = true;
                VerifyPf1MixedExportedSwarm(
                    root, configuration, product.Executable);
                pf1MixedSwarmVerified = true;
                VerifyPf2MixedExportedAccountingSwarm(
                    root, configuration, product.Executable);
                pf2AccountingSwarmVerified = true;
                contentlessOnlineVerified = true;
            }
            finally
            {
                if (Directory.Exists(product.Scope))
                    Directory.Delete(product.Scope, recursive: true);
            }
        }
        RunExpectFailure(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--composition-smoke", "--expect-edition=SinglePlayer",
        ], "ClientProductMismatchFailsClosed");

        BuildClientApplication(root, configuration, "Complete");
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--composition-smoke", "--expect-edition=Complete",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--", "--f0-2-smoke", "--expect-edition=Complete",
            "--cabin-content=default", "--human=x",
        ]);
        if (!mixedSessionLifecycleVerified)
        {
            VerifyMixedSessionLifecycleNative(root, configuration, godot);
            mixedSessionLifecycleVerified = true;
        }
    }
}

if (!options.SkipGodot)
{
    if (!contentlessOnlineVerified || !finalClientExportsVerified ||
        !embeddedCompleteHostedVerified || !mixedSessionLifecycleVerified ||
        !pf1MixedSwarmVerified || !pf2AccountingSwarmVerified)
        throw new InvalidOperationException(
            "The final client composition/parity matrix did not exercise every required native gate.");
    Console.WriteLine(
        "PASS F0.8_FinalCompositionParity " +
        "exports=SinglePlayer/Online/Complete online=contentless " +
        "completeHost=exported localPath=packet-free remoteClients=exported " +
        "differentFeel=same-online-executable lifecycle=clean");
}

Run(root, "git", ["diff", "--check"]);
Console.WriteLine("PASS repository verification");
return 0;

static void VerifyNetworkCapabilityArtifacts(string root, string configuration)
{
    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), configuration, "network-capabilities");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string networkSource = Path.Combine(root, "Kernels", "Network");
    string[] conditionalSources = Directory.EnumerateFiles(
            networkSource, "*.cs", SearchOption.AllDirectories)
        .Where(static path => File.ReadLines(path).Any(static line =>
            Regex.IsMatch(line, "^\\s*#(if|elif|else|endif)\\b",
                RegexOptions.CultureInvariant)))
        .Select(path => Relative(root, path))
        .ToArray();
    if (conditionalSources.Length != 0)
        throw new InvalidOperationException(
            "Network capability selection must use physical source inclusion, not #if branches: " +
            string.Join(", ", conditionalSources));
    string[] roleProtocolDeclarations = Directory.EnumerateFiles(
            Path.Combine(networkSource, "Hosted"), "*.cs", SearchOption.AllDirectories)
        .Where(static path => File.ReadAllText(path).Contains(
            "[NetworkProtocol", StringComparison.Ordinal) ||
            File.ReadAllText(path).Contains("[NetworkMessage", StringComparison.Ordinal))
        .Select(path => Relative(root, path))
        .ToArray();
    if (roleProtocolDeclarations.Length != 0)
        throw new InvalidOperationException(
            "Shared protocol declarations must remain in Network/Core: " +
            string.Join(", ", roleProtocolDeclarations));

    NetworkFlavorProbeEvidence full = RunProbe("Full", explicitSelection: false);
    NetworkFlavorProbeEvidence client = RunProbe("ClientOnly", explicitSelection: true);
    NetworkFlavorProbeEvidence server = RunProbe("ServerOnly", explicitSelection: true);

    foreach (NetworkFlavorProbeEvidence evidence in new[] { full, client, server })
    {
        if (!string.Equals(evidence.AssemblyName, "UltraSim.Network", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{evidence.Flavor} changed the canonical UltraSim.Network assembly identity.");
        if (evidence.AllTypes.Any(IsNetworkTestingType))
            throw new InvalidOperationException(
                $"{evidence.Flavor} contains non-production Network testing machinery.");
    }

    if (full.ProtocolVersion != client.ProtocolVersion ||
        full.ProtocolVersion != server.ProtocolVersion ||
        full.ProtocolFingerprint != client.ProtocolFingerprint ||
        full.ProtocolFingerprint != server.ProtocolFingerprint ||
        !string.Equals(full.CanonicalPacketHex, client.CanonicalPacketHex, StringComparison.Ordinal) ||
        !string.Equals(full.CanonicalPacketHex, server.CanonicalPacketHex, StringComparison.Ordinal))
        throw new InvalidOperationException(
            "Network capability selection changed shared protocol identity or canonical wire bytes.");

    var expectedFullApi = client.PublicApi
        .Concat(server.PublicApi)
        .ToHashSet(StringComparer.Ordinal);
    if (!expectedFullApi.SetEquals(full.PublicApi))
        throw new InvalidOperationException(
            "The canonical Full Network public API is not the exact union of ClientOnly and ServerOnly.");

    RequireType(client.AllTypes, "UltraSim.Network.HostedUdpProtocolClient", "ClientOnly");
    RequireType(client.AllTypes, "UltraSim.Network.HostedUdpDeploymentDiscoveryClient", "ClientOnly");
    RejectType(client.AllTypes, "UltraSim.Network.HostedUdpAdmissionHost", "ClientOnly");
    RejectType(client.AllTypes, "UltraSim.Network.NetworkServerIdentity", "ClientOnly");
    RejectType(client.AllTypes, "UltraSim.Network.HostedUdpConnectionOwnerRuntime", "ClientOnly");
    RejectType(client.AllTypes, "UltraSim.Network.SharedUdpServerListener", "ClientOnly");

    RequireType(server.AllTypes, "UltraSim.Network.HostedUdpAdmissionHost", "ServerOnly");
    RequireType(server.AllTypes, "UltraSim.Network.NetworkServerIdentity", "ServerOnly");
    RequireType(server.AllTypes, "UltraSim.Network.HostedUdpConnectionOwnerRuntime", "ServerOnly");
    RejectType(server.AllTypes, "UltraSim.Network.HostedUdpProtocolClient", "ServerOnly");
    RejectType(server.AllTypes, "UltraSim.Network.HostedUdpDeploymentDiscoveryClient", "ServerOnly");

    RequireType(full.AllTypes, "UltraSim.Network.HostedUdpProtocolClient", "Full");
    RequireType(full.AllTypes, "UltraSim.Network.HostedUdpAdmissionHost", "Full");
    RequireType(full.AllTypes, "UltraSim.Network.NetworkServerIdentity", "Full");
    if (client.AssemblyBytes >= full.AssemblyBytes || server.AssemblyBytes >= full.AssemblyBytes)
        throw new InvalidOperationException(
            "A capability-stripped Network assembly is not physically smaller than Full.");

    string packages = Path.Combine(scope, "packages");
    Run(root, "dotnet",
    [
        "pack", "Kernels/Network/UltraSim.Network.csproj",
        "-c", configuration,
        "--output", packages,
    ]);
    string package = Directory.EnumerateFiles(packages, "UltraSim.Network.*.nupkg")
        .Single(static path => !path.EndsWith(".symbols.nupkg", StringComparison.Ordinal));
    using (ZipArchive archive = ZipFile.OpenRead(package))
    {
        if (!archive.Entries.Any(static entry =>
                string.Equals(entry.FullName, "lib/net8.0/UltraSim.Network.dll",
                    StringComparison.Ordinal)) ||
            archive.Entries.Any(static entry =>
                entry.FullName.Contains("UltraSim.Network.Testing", StringComparison.Ordinal)))
            throw new InvalidOperationException(
                "The canonical UltraSim.Network package is not the Full production assembly alone.");
    }
    RunExpectFailure(root, "dotnet",
    [
        "pack", "Kernels/Network/UltraSim.Network.csproj",
        "-c", configuration,
        "--output", Path.Combine(scope, "invalid-client-package"),
        "-p:UltraSimNetworkFlavor=ClientOnly",
    ], "Network_StrippedBuildCannotPublishCanonicalPackage");
    RunExpectFailure(root, "dotnet",
    [
        "build", "Kernels/Network/UltraSim.Network.csproj",
        "-c", configuration,
        "-p:UltraSimNetworkFlavor=Unknown",
    ], "Network_UnknownCapabilitySetFailsClosed");

    Console.WriteLine(
        $"PASS NetworkCapabilityBuilds assembly=UltraSim.Network protocol=" +
        $"{full.ProtocolVersion}/{full.ProtocolFingerprint:x16} " +
        $"bytes=ClientOnly:{client.AssemblyBytes},ServerOnly:{server.AssemblyBytes},Full:{full.AssemblyBytes} " +
        $"publicApi=ClientOnly:{client.PublicApi.Length},ServerOnly:{server.PublicApi.Length},Full:{full.PublicApi.Length}");
    Directory.Delete(scope, recursive: true);
    return;

    NetworkFlavorProbeEvidence RunProbe(string flavor, bool explicitSelection)
    {
        string evidencePath = Path.Combine(scope, $"{flavor}.json");
        var arguments = new List<string>
        {
            "run", "--project",
            "_Tools/NetworkFlavorProbe/UltraSim.NetworkFlavorProbe.csproj",
            "-c", configuration,
        };
        if (explicitSelection)
            arguments.Add($"-p:UltraSimNetworkFlavor={flavor}");
        arguments.Add("--");
        arguments.Add(evidencePath);
        Run(root, "dotnet", arguments);
        NetworkFlavorProbeEvidence? evidence =
            JsonSerializer.Deserialize<NetworkFlavorProbeEvidence>(
                File.ReadAllText(evidencePath));
        if (evidence is null || !string.Equals(evidence.Flavor, flavor, StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"Network flavor probe expected {flavor} but observed {evidence?.Flavor ?? "none"}.");
        return evidence;
    }
}

static void VerifyDedicatedNetworkAssembly(string root, string configuration)
{
    string path = Path.Combine(
        root, "Server", "DirectConnect", "bin", configuration, "net8.0",
        "UltraSim.Network.dll");
    HashSet<string> types = ReadDefinedTypeNames(path);
    RequireType(types, "UltraSim.Network.HostedUdpAdmissionHost", "Dedicated");
    RequireType(types, "UltraSim.Network.NetworkServerIdentity", "Dedicated");
    RejectType(types, "UltraSim.Network.HostedUdpProtocolClient", "Dedicated");
    RejectType(types, "UltraSim.Network.HostedUdpDeploymentDiscoveryClient", "Dedicated");
    Console.WriteLine("PASS Dedicated_ServerOnlyNetworkAssembly");
}

static bool IsNetworkTestingType(string name)
{
    string[] testingTypes =
    [
        "UltraSim.Network.ProtocolLoopback",
        "UltraSim.Network.DeterministicDatagramLink",
        "UltraSim.Network.DeterministicDatagramEndpoint",
        "UltraSim.Network.DeterministicNetworkImpairment",
        "UltraSim.Network.SharedUdpProtocolHost",
        "UltraSim.Network.UdpProtocolSession",
    ];
    return testingTypes.Any(type =>
        string.Equals(name, type, StringComparison.Ordinal) ||
        name.StartsWith(type + "+", StringComparison.Ordinal));
}

static HashSet<string> ReadDefinedTypeNames(string assemblyPath)
{
    if (!File.Exists(assemblyPath))
        throw new FileNotFoundException("Expected Network assembly was not published.", assemblyPath);
    using FileStream stream = File.OpenRead(assemblyPath);
    using var executable = new PEReader(stream);
    MetadataReader metadata = executable.GetMetadataReader();
    var types = new HashSet<string>(StringComparer.Ordinal);
    foreach (TypeDefinitionHandle handle in metadata.TypeDefinitions)
    {
        TypeDefinition definition = metadata.GetTypeDefinition(handle);
        string name = metadata.GetString(definition.Name);
        if (name == "<Module>") continue;
        string @namespace = metadata.GetString(definition.Namespace);
        types.Add(string.IsNullOrEmpty(@namespace) ? name : @namespace + "." + name);
    }
    return types;
}

static void RequireType(IEnumerable<string> types, string required, string product)
{
    if (!types.Contains(required, StringComparer.Ordinal))
        throw new InvalidOperationException(
            $"{product} Network assembly is missing required type '{required}'.");
}

static void RejectType(IEnumerable<string> types, string forbidden, string product)
{
    if (types.Contains(forbidden, StringComparer.Ordinal))
        throw new InvalidOperationException(
            $"{product} Network assembly contains forbidden type '{forbidden}'.");
}

static void VerifyClientCompositionArtifacts(string root, string configuration)
{
    ClientCompositionGate[] gates =
    [
        new(
            "SinglePlayer",
            ["UltraSim.Client.Application.Godot", "UltraSim.Client.BuiltInTestWorlds",
                "UltraSim.Client.Content", "UltraSim.Client.Local",
                "UltraSim.Client.StandardUi", "UltraSim.Client.ControlPlane",
                "UltraSim.Framework.ControlPlane",
                "UltraSim.Framework.Persistence",
                "UltraSim.Framework.Definitions",
                "UltraSim.Game.Composition", "UltraSim.Game.Replication",
                "UltraSim.Server.Runtime"],
            ["UltraSim.Client.Network", "UltraSim.Network", "UltraSim.Game.Protocol",
                "UltraSim.Game.Replication.Remote", "UltraSim.Server.Hosting",
                "UltraSim.Framework.Http.Endpoint", "UltraSim.Bridges.Http.Kestrel",
                "UltraSim.Server.ConceptDemo", "UltraSim.Server.ConceptDemo.LocalHost"]),
        new(
            "Online",
            ["UltraSim.Client.Application.Godot", "UltraSim.Client.Content", "UltraSim.Client.Network",
                "UltraSim.Client.StandardUi", "UltraSim.Client.ControlPlane", "UltraSim.Framework.ControlPlane",
                "UltraSim.Framework.Definitions", "UltraSim.Game.Presentation.Contracts",
                "UltraSim.Game.Protocol", "UltraSim.Network"],
            ["UltraSim.Client.BuiltInTestWorlds", "UltraSim.Client.Local",
                "UltraSim.Game.Composition", "UltraSim.Game.Inventory", "UltraSim.Game.Persistence",
                "UltraSim.Game.Replication",
                "UltraSim.Game.Replication.Remote", "UltraSim.Game.UI", "UltraSim.Server.Hosting",
                "UltraSim.Game.Items.Contracts", "UltraSim.Game.Combat.Contracts",
                "UltraSim.Game.Skills.Contracts",
                "UltraSim.Framework.Http.Endpoint", "UltraSim.Bridges.Http.Kestrel",
                "UltraSim.Framework.Persistence",
                "UltraSim.Server.Persistence", "UltraSim.Server.Runtime",
                "UltraSim.Server.ConceptDemo", "UltraSim.Server.ConceptDemo.LocalHost"]),
        new(
            "Complete",
            ["UltraSim.Client.Application.Godot", "UltraSim.Client.BuiltInTestWorlds",
                "UltraSim.Client.Local", "UltraSim.Client.StandardUi", "UltraSim.Client.ControlPlane",
                "UltraSim.Client.Network", "UltraSim.Game.Composition", "UltraSim.Game.Protocol",
                "UltraSim.Framework.ControlPlane",
                "UltraSim.Framework.Persistence",
                "UltraSim.Game.Replication", "UltraSim.Network", "UltraSim.Server.Runtime",
                "UltraSim.Server.ConceptDemo", "UltraSim.Server.ConceptDemo.LocalHost",
                "UltraSim.Framework.Http.Endpoint", "UltraSim.Bridges.Http.Kestrel"],
            []),
    ];

    foreach (ClientCompositionGate gate in gates)
    {
        string networkFlavor = gate.Name == "Online" ? "ClientOnly" : "Full";
        string output = Path.Combine(
            Path.GetTempPath(), "ultrasim-repository-verification", Environment.ProcessId.ToString(),
            configuration, gate.Name);
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);
        // Edition changes alter conditional ProjectReferences. NuGet's shared Godot project
        // assets may otherwise retain the preceding SinglePlayer/Complete graph even when
        // publish is passed Online properties. Match the physical export's exact restore seam.
        Run(root, "dotnet",
        [
            "restore",
            "Client/Godot/UltraSim.Client.Application.Godot.csproj",
            "--force",
            $"-p:UltraSimEdition={gate.Name}",
            $"-p:UltraSimNetworkFlavor={networkFlavor}",
        ]);
        Run(root, "dotnet",
        [
            "publish",
            "Client/Godot/UltraSim.Client.Application.Godot.csproj",
            "-c", configuration,
            $"-p:UltraSimEdition={gate.Name}",
            $"-p:UltraSimNetworkFlavor={networkFlavor}",
            "--no-restore",
            "--output", output,
        ]);

        string dependenciesPath = Path.Combine(output, "UltraSim.Client.Application.Godot.deps.json");
        if (!File.Exists(dependenciesPath))
            throw new InvalidOperationException(
                $"{gate.Name} did not publish its expected .deps.json: {dependenciesPath}");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(dependenciesPath));
        var dependencies = document.RootElement.GetProperty("libraries").EnumerateObject()
            .Select(static property => property.Name.Split('/')[0])
            .ToHashSet(StringComparer.Ordinal);
        int declaredNativeAssets = 0;
        foreach (JsonProperty target in document.RootElement.GetProperty("targets").EnumerateObject())
        foreach (JsonProperty library in target.Value.EnumerateObject())
        {
            if (library.Value.TryGetProperty("native", out JsonElement native))
                declaredNativeAssets += native.EnumerateObject().Count();
            if (!library.Value.TryGetProperty("runtimeTargets", out JsonElement runtimeTargets)) continue;
            declaredNativeAssets += runtimeTargets.EnumerateObject().Count(static asset =>
                asset.Value.TryGetProperty("assetType", out JsonElement kind) &&
                string.Equals(kind.GetString(), "native", StringComparison.Ordinal));
        }
        var outputAssemblies = Directory.EnumerateFiles(output, "UltraSim*.dll", SearchOption.TopDirectoryOnly)
            .Select(static path => Path.GetFileNameWithoutExtension(path))
            .ToHashSet(StringComparer.Ordinal);
        foreach (string managementProof in new[] { "UltraSim.Bridges.ControlPlane.ECS",
            "UltraSim.Bridges.ControlPlane.ServerRuntime" })
            if (outputAssemblies.Contains(managementProof) || dependencies.Contains(managementProof))
                throw new InvalidOperationException($"{gate.Name} acquired an opt-in P1 management proof assembly.");

        string[] scriptAssemblies = outputAssemblies
            .Where(static assembly => assembly.StartsWith("UltraSim.Client.", StringComparison.Ordinal) &&
                assembly.EndsWith(".Godot", StringComparison.Ordinal))
            .OrderBy(static assembly => assembly, StringComparer.Ordinal)
            .ToArray();
        if (!scriptAssemblies.SequenceEqual(["UltraSim.Client.Application.Godot"], StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"{gate.Name} resolved multiple client Godot script assemblies: " +
                string.Join(", ", scriptAssemblies));

        foreach (string required in gate.RequiredAssemblies)
        {
            if (!dependencies.Contains(required) || !outputAssemblies.Contains(required))
                throw new InvalidOperationException(
                    $"{gate.Name} composition is missing required capability assembly '{required}'.");
        }
        foreach (string forbidden in gate.ForbiddenAssemblies)
        {
            if (dependencies.Contains(forbidden) || outputAssemblies.Contains(forbidden))
                throw new InvalidOperationException(
                    $"{gate.Name} composition resolved or copied forbidden assembly '{forbidden}'.");
        }
        bool hasKestrelPayload = Directory.EnumerateFiles(output, "Microsoft.AspNetCore*.dll").Any();
        if (hasKestrelPayload != (gate.Name == "Complete"))
            throw new InvalidOperationException($"{gate.Name} private HTTP runtime payload violates its edition boundary.");
        string networkAssembly = Path.Combine(output, "UltraSim.Network.dll");
        if (string.Equals(gate.Name, "Online", StringComparison.Ordinal))
        {
            HashSet<string> types = ReadDefinedTypeNames(networkAssembly);
            RequireType(types, "UltraSim.Network.HostedUdpProtocolClient", gate.Name);
            RejectType(types, "UltraSim.Network.HostedUdpAdmissionHost", gate.Name);
            RejectType(types, "UltraSim.Network.NetworkServerIdentity", gate.Name);
            RejectType(types, "UltraSim.Network.HostedUdpConnectionOwnerRuntime", gate.Name);
            RejectType(types, "UltraSim.Network.SharedUdpServerListener", gate.Name);
        }
        else if (string.Equals(gate.Name, "Complete", StringComparison.Ordinal))
        {
            HashSet<string> types = ReadDefinedTypeNames(networkAssembly);
            RequireType(types, "UltraSim.Network.HostedUdpProtocolClient", gate.Name);
            RequireType(types, "UltraSim.Network.HostedUdpAdmissionHost", gate.Name);
            RequireType(types, "UltraSim.Network.NetworkServerIdentity", gate.Name);
        }

        long managedBytes = Directory.EnumerateFiles(output, "UltraSim*.dll", SearchOption.TopDirectoryOnly)
            .Sum(static path => new FileInfo(path).Length);
        if (string.Equals(gate.Name, "Online", StringComparison.Ordinal))
        {
            string[] forbiddenPayloads = Directory.EnumerateFiles(
                    output, "*", SearchOption.AllDirectories)
                .Where(static path => Path.GetExtension(path) is ".usworldpack" or ".uspkg")
                .Select(path => Relative(output, path))
                .ToArray();
            if (forbiddenPayloads.Length != 0)
                throw new InvalidOperationException(
                    "The Online product physically contains bundled playable content: " +
                    string.Join(", ", forbiddenPayloads));
        }
        int copiedNativeAssets = Directory.EnumerateFiles(output, "*", SearchOption.TopDirectoryOnly)
            .Count(static path => Path.GetExtension(path) is ".so" or ".dylib");
        Console.WriteLine(
            $"PASS {gate.Name}_PublishedOutput dependencies={dependencies.Count} " +
            $"ultrasimAssemblies={outputAssemblies.Count} managedBytes={managedBytes} " +
            $"declaredNativeAssets={declaredNativeAssets} copiedNativeAssets={copiedNativeAssets}");
        Directory.Delete(output, recursive: true);
    }

    Console.WriteLine("PASS SinglePlayer_NoClientNetwork");
    Console.WriteLine("PASS SinglePlayer_NoNetworkKernel");
    Console.WriteLine("PASS SinglePlayer_UsesLocalAuthorityBridge");
    Console.WriteLine("PASS Online_NoLocalServerRuntime");
    Console.WriteLine("PASS Online_NoLocalAuthorityComposition");
    Console.WriteLine("PASS Online_NoLocalAuthorityBridge");
}

static void BuildClientApplication(string root, string configuration, string edition)
{
    string networkFlavor = edition == "Online" ? "ClientOnly" : "Full";
    Run(root, "dotnet",
    [
        "build",
        "Client/Godot/UltraSim.Client.Application.Godot.csproj",
        "-c", configuration,
        $"-p:UltraSimEdition={edition}",
        $"-p:UltraSimNetworkFlavor={networkFlavor}",
    ]);

    // The editor host always loads the project's Debug assembly. Release is still compiled and
    // independently published above; build the same edition as Debug before a headless editor run
    // so the launch cannot consume a stale, wider product from an earlier gate.
    if (!string.Equals(configuration, "Debug", StringComparison.Ordinal))
    {
        Run(root, "dotnet",
        [
            "build",
            "Client/Godot/UltraSim.Client.Application.Godot.csproj",
            "-c", "Debug",
            $"-p:UltraSimEdition={edition}",
            $"-p:UltraSimNetworkFlavor={networkFlavor}",
        ]);
    }

    string liveOutput = Path.Combine(
        root, "Client", "Godot", ".godot", "mono", "temp", "bin");
    string[] liveScriptAssemblies = Directory.Exists(liveOutput)
        ? Directory.EnumerateFiles(
                liveOutput,
                "UltraSim.Client.Application.Godot.dll",
                SearchOption.AllDirectories)
            .Select(path => Relative(root, path))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray()
        : [];
    if (liveScriptAssemblies.Length != 1)
        throw new InvalidOperationException(
            "The live Godot development tree must contain exactly one active configuration of " +
            $"the sole script assembly; found: {string.Join(", ", liveScriptAssemblies)}");
}

static ContentlessOnlineExport VerifyContentlessOnlineExport(string root, string godot)
{
    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "a0.8-export");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string executable = Path.Combine(scope, "UltraSimOnline.x86_64");
    Run(
        root,
        Path.Combine(root, "Client", "Godot", "export-client.sh"),
        ["Online", executable],
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ULTRASIM_GODOT"] = godot,
        });

    string data = Path.Combine(
        scope, "data_UltraSim.Client.Application.Godot_linuxbsd_x86_64");
    string dependenciesPath = Path.Combine(
        data, "UltraSim.Client.Application.Godot.deps.json");
    if (!File.Exists(executable) || !File.Exists(dependenciesPath))
        throw new InvalidOperationException(
            "The Online Godot export omitted its executable or managed dependency manifest.");

    string[] expectedAssemblies =
    [
        "UltraSim.Client.Application.Godot",
        "UltraSim.Client.Content",
        "UltraSim.Client.ControlPlane",
        "UltraSim.Client.Core",
        "UltraSim.Client.Network",
        "UltraSim.Client.StandardUi",
        "UltraSim.ECS",
        "UltraSim.Framework.ControlPlane",
        "UltraSim.Framework.Definitions",
        "UltraSim.Game.Accounting",
        "UltraSim.Game.Contracts",
        "UltraSim.Game.Presentation.Contracts",
        "UltraSim.Game.Protocol",
        "UltraSim.Game.World",
        "UltraSim.Network",
        "UltraSim.Spatial",
        "UltraSim.UI",
    ];
    string[] actualAssemblies = Directory.EnumerateFiles(
            data, "UltraSim*.dll", SearchOption.TopDirectoryOnly)
        .Select(static path => Path.GetFileNameWithoutExtension(path))
        .OrderBy(static value => value, StringComparer.Ordinal)
        .ToArray();
    if (!actualAssemblies.SequenceEqual(expectedAssemblies, StringComparer.Ordinal))
        throw new InvalidOperationException(
            "The actual Online Godot export has an unexpected UltraSim assembly closure.\n" +
            $"Expected: {string.Join(", ", expectedAssemblies)}\n" +
            $"Actual: {string.Join(", ", actualAssemblies)}");

    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(dependenciesPath));
    var dependencies = document.RootElement.GetProperty("libraries").EnumerateObject()
        .Select(static property => property.Name.Split('/')[0])
        .ToHashSet(StringComparer.Ordinal);
    string[] forbidden =
    [
        "UltraSim.Client.BuiltInTestWorlds",
        "UltraSim.Client.Local",
        "UltraSim.Game.Composition",
        "UltraSim.Game.Persistence",
        "UltraSim.Game.Replication",
        "UltraSim.Game.Replication.Remote",
        "UltraSim.Framework.Persistence",
        "UltraSim.Game.Items.Contracts",
        "UltraSim.Game.Combat.Contracts",
        "UltraSim.Game.Skills.Contracts",
        "UltraSim.Server.Hosting",
        "UltraSim.Server.Persistence",
        "UltraSim.Server.Runtime",
    ];
    string[] leakedDependencies = forbidden.Where(dependencies.Contains).ToArray();
    string[] forbiddenPayloads = Directory.EnumerateFiles(scope, "*", SearchOption.AllDirectories)
        .Where(static path => Path.GetExtension(path) is ".usworldpack" or ".uspkg")
        .Select(path => Relative(scope, path))
        .ToArray();
    if (leakedDependencies.Length != 0 || forbiddenPayloads.Length != 0)
        throw new InvalidOperationException(
            "The actual Online Godot export contains forbidden local authority/content: " +
            string.Join(", ", leakedDependencies.Concat(forbiddenPayloads)));
    HashSet<string> networkTypes = ReadDefinedTypeNames(
        Path.Combine(data, "UltraSim.Network.dll"));
    RequireType(networkTypes, "UltraSim.Network.HostedUdpProtocolClient", "exported Online");
    RejectType(networkTypes, "UltraSim.Network.HostedUdpAdmissionHost", "exported Online");
    RejectType(networkTypes, "UltraSim.Network.NetworkServerIdentity", "exported Online");
    RejectType(
        networkTypes, "UltraSim.Network.HostedUdpConnectionOwnerRuntime", "exported Online");

    long managedBytes = Directory.EnumerateFiles(
            data, "UltraSim*.dll", SearchOption.TopDirectoryOnly)
        .Sum(static path => new FileInfo(path).Length);
    long exportedBytes = Directory.EnumerateFiles(scope, "*", SearchOption.AllDirectories)
        .Sum(static path => new FileInfo(path).Length);
    Console.WriteLine(
        $"PASS A0.8_ContentlessOnlineExport ultrasimAssemblies={actualAssemblies.Length} " +
        $"managedBytes={managedBytes} executableBytes={new FileInfo(executable).Length} " +
        $"files={Directory.EnumerateFiles(scope, "*", SearchOption.AllDirectories).Count()} " +
        $"totalBytes={exportedBytes} bundledWorldPacks=0 bundledPackages=0");
    return new ContentlessOnlineExport(scope, executable);
}

static FinalClientProductExports VerifyFinalClientProductExports(
    string root,
    string godot,
    string exportScope)
{
    string singlePlayerDirectory = Path.Combine(exportScope, "single-player");
    string singlePlayerExecutable = Path.Combine(
        singlePlayerDirectory, "UltraSimSinglePlayer.x86_64");
    string completeDirectory = Path.Combine(exportScope, "complete");
    string completeExecutable = Path.Combine(completeDirectory, "UltraSimComplete.x86_64");

    ExportClient(root, godot, "SinglePlayer", singlePlayerExecutable);
    string[] singlePlayerAssemblies =
    [
        "UltraSim.Client.Application.Godot",
        "UltraSim.Client.BuiltInTestWorlds",
        "UltraSim.Client.Content",
        "UltraSim.Client.ControlPlane",
        "UltraSim.Client.Core",
        "UltraSim.Client.Local",
        "UltraSim.Client.StandardUi",
        "UltraSim.ECS",
        "UltraSim.Framework.ControlPlane",
        "UltraSim.Framework.Definitions",
        "UltraSim.Framework.Persistence",
        "UltraSim.Game.Accounting",
        "UltraSim.Game.Admission",
        "UltraSim.Game.Chat",
        "UltraSim.Game.Combat.Contracts",
        "UltraSim.Game.Composition",
        "UltraSim.Game.Contracts",
        "UltraSim.Game.Inventory",
        "UltraSim.Game.Items.Contracts",
        "UltraSim.Game.Persistence",
        "UltraSim.Game.Physics",
        "UltraSim.Game.Presentation.Contracts",
        "UltraSim.Game.Replication",
        "UltraSim.Game.Skills.Contracts",
        "UltraSim.Game.UI",
        "UltraSim.Game.World",
        "UltraSim.Server.Persistence",
        "UltraSim.Server.Runtime",
        "UltraSim.Spatial",
        "UltraSim.UI",
    ];
    RequireExactExportAssemblies(
        singlePlayerDirectory, singlePlayerAssemblies, "Single Player");
    RequireExternalOfficialPresentation(singlePlayerDirectory, singlePlayerExecutable,
        "Single Player");
    if (File.Exists(Path.Combine(ExportDataDirectory(singlePlayerDirectory), "UltraSim.Network.dll")))
        throw new InvalidOperationException(
            "The actual Single Player export physically contains the Network kernel.");
    Run(root, singlePlayerExecutable,
    [
        "--headless", "--", "--composition-smoke", "--expect-edition=SinglePlayer",
    ]);
    Run(root, singlePlayerExecutable,
    [
        "--headless", "--", "--f0-2-smoke", "--expect-edition=SinglePlayer",
        "--cabin-content=default", "--human=x",
    ]);
    string singleInstalledCache = Path.Combine(exportScope, "single-player-official-cache");
    Run(root, singlePlayerExecutable,
    [
        "--headless", "--", "--f1-4-official-smoke=installed",
        "--expect-edition=SinglePlayer", $"--content-cache={singleInstalledCache}",
    ]);
    Run(root, singlePlayerExecutable,
    [
        "--headless", "--", "--f1-4-official-smoke=installed",
        "--expect-edition=SinglePlayer", $"--content-cache={singleInstalledCache}",
    ]);
    string singleAbsentDirectory = Path.Combine(exportScope, "single-player-absent");
    CopyProductWithoutOfficialContent(singlePlayerDirectory, singleAbsentDirectory);
    string singleAbsentExecutable = Path.Combine(
        singleAbsentDirectory, Path.GetFileName(singlePlayerExecutable));
    RequireSameFileHash(singlePlayerExecutable, singleAbsentExecutable,
        "Single Player installed/absent executable");
    Run(root, singleAbsentExecutable,
    [
        "--headless", "--", "--f1-4-official-smoke=absent",
        "--expect-edition=SinglePlayer",
        $"--content-cache={Path.Combine(exportScope, "single-player-absent-cache")}",
    ]);
    Run(root, singlePlayerExecutable,
    [
        "--headless", "--", "--f1-4-official-smoke=installed",
        "--expect-edition=SinglePlayer",
        $"--content-cache={Path.Combine(exportScope, "single-player-restored-cache")}",
    ]);

    ExportClient(root, godot, "Complete", completeExecutable);
    string[] completeAssemblies =
    [
        "UltraSim.Bridges.Http.Kestrel",
        "UltraSim.Client.Application.Godot",
        "UltraSim.Client.BuiltInTestWorlds",
        "UltraSim.Client.Content",
        "UltraSim.Client.ControlPlane",
        "UltraSim.Client.Core",
        "UltraSim.Client.Local",
        "UltraSim.Client.Network",
        "UltraSim.Client.StandardUi",
        "UltraSim.ECS",
        "UltraSim.Framework.ControlPlane",
        "UltraSim.Framework.Definitions",
        "UltraSim.Framework.Http.Endpoint",
        "UltraSim.Framework.Persistence",
        "UltraSim.Game.Accounting",
        "UltraSim.Game.Admission",
        "UltraSim.Game.Chat",
        "UltraSim.Game.Combat.Contracts",
        "UltraSim.Game.Composition",
        "UltraSim.Game.Contracts",
        "UltraSim.Game.Inventory",
        "UltraSim.Game.Items.Contracts",
        "UltraSim.Game.Persistence",
        "UltraSim.Game.Physics",
        "UltraSim.Game.Presentation.Contracts",
        "UltraSim.Game.Protocol",
        "UltraSim.Game.Replication",
        "UltraSim.Game.Replication.Remote",
        "UltraSim.Game.Skills.Contracts",
        "UltraSim.Game.UI",
        "UltraSim.Game.World",
        "UltraSim.Network",
        "UltraSim.Server.ConceptDemo",
        "UltraSim.Server.ConceptDemo.LocalHost",
        "UltraSim.Server.Hosting",
        "UltraSim.Server.Persistence",
        "UltraSim.Server.Runtime",
        "UltraSim.Spatial",
        "UltraSim.UI",
    ];
    RequireExactExportAssemblies(completeDirectory, completeAssemblies, "Complete");
    if (!File.Exists(Path.Combine(ExportDataDirectory(completeDirectory), "Microsoft.AspNetCore.Server.Kestrel.Core.dll")))
        throw new InvalidOperationException("Complete export omitted its supported private Kestrel runtime.");
    RequireExternalOfficialPresentation(completeDirectory, completeExecutable, "Complete");
    HashSet<string> completeNetworkTypes = ReadDefinedTypeNames(
        Path.Combine(ExportDataDirectory(completeDirectory), "UltraSim.Network.dll"));
    RequireType(
        completeNetworkTypes, "UltraSim.Network.HostedUdpProtocolClient", "exported Complete");
    RequireType(
        completeNetworkTypes, "UltraSim.Network.HostedUdpAdmissionHost", "exported Complete");
    RequireType(
        completeNetworkTypes, "UltraSim.Network.NetworkServerIdentity", "exported Complete");
    Run(root, completeExecutable,
    [
        "--headless", "--", "--composition-smoke", "--expect-edition=Complete",
    ]);
    Run(root, completeExecutable,
    [
        "--headless", "--", "--f0-2-smoke", "--expect-edition=Complete",
        "--cabin-content=default", "--human=x",
    ]);
    Run(root, completeExecutable,
    [
        "--headless", "--", "--f1-4-official-smoke=installed",
        "--expect-edition=Complete",
        $"--content-cache={Path.Combine(exportScope, "complete-official-cache")}",
    ]);
    string completeAbsentDirectory = Path.Combine(exportScope, "complete-absent");
    CopyProductWithoutOfficialContent(completeDirectory, completeAbsentDirectory);
    string completeAbsentExecutable = Path.Combine(
        completeAbsentDirectory, Path.GetFileName(completeExecutable));
    RequireSameFileHash(completeExecutable, completeAbsentExecutable,
        "Complete installed/absent executable");
    Run(root, completeAbsentExecutable,
    [
        "--headless", "--", "--f1-4-official-smoke=absent",
        "--expect-edition=Complete",
        $"--content-cache={Path.Combine(exportScope, "complete-absent-cache")}",
    ]);

    Console.WriteLine(
        $"PASS F0.8_PhysicalClientExports " +
        $"singlePlayerAssemblies={singlePlayerAssemblies.Length} singlePlayerNetwork=absent " +
        $"onlineAssemblies=13 onlineNetwork=ClientOnly onlineAuthority=absent " +
        $"completeAssemblies={completeAssemblies.Length} completeNetwork=Full " +
        "singlePlayerNative=pass completeLocalNative=pass " +
        "officialExternal=true installedAbsentRestored=true binaryIdentity=true");
    VerifyF112LocalExport(root, singlePlayerExecutable, "SinglePlayer", "installed", 8);
    VerifyF112LocalExport(root, singleAbsentExecutable, "SinglePlayer", "absent", 4);
    VerifyF112LocalExport(root, singlePlayerExecutable, "SinglePlayer", "primitive", 4);
    VerifyF112LocalExport(root, completeExecutable, "Complete", "installed", 4,
        vegetation: true);
    return new FinalClientProductExports(singlePlayerExecutable, completeExecutable);
}

static void VerifyF112LocalExport(string root, string executable, string edition,
    string art, int seconds, bool vegetation = false)
{
    string scope = Path.Combine(root, ".output", "f1.12-verification", Environment.ProcessId.ToString());
    Directory.CreateDirectory(scope);
    string label = $"{edition}-{art}-{(vegetation ? "vegetation" : "default")}";
    string evidence = Path.Combine(scope, label + ".json");
    string head = RunCaptured(root, "git", ["rev-parse", "HEAD"], 5_000).Trim();
    var args = new List<string>
    {
        "--headless", "--", "--f1-12-dogfood", $"--expect-edition={edition}",
        $"--f1-12-art={art}", $"--f1-12-seconds={seconds}", $"--f1-12-head={head}",
        $"--f1-12-save-root={Path.Combine(scope, label + "-saves")}",
        $"--content-cache={Path.Combine(scope, label + "-content")}",
        $"--f1-12-evidence={evidence}",
    };
    if (vegetation) args.Add("--f1-11-vegetation");
    string output = RunCaptured(root, executable, args, 240_000);
    if (!output.Contains("F1.12 PRODUCT DOGFOOD PASS", StringComparison.Ordinal) || !File.Exists(evidence))
        throw new InvalidOperationException("Actual exported F1.12 journey omitted its evidence.");
    using JsonDocument recorded = JsonDocument.Parse(File.ReadAllBytes(evidence));
    if (recorded.RootElement.GetProperty("lifetimes").GetArrayLength() != 3 ||
        recorded.RootElement.GetProperty("samples").GetArrayLength() != 3 * seconds ||
        recorded.RootElement.GetProperty("HumanFeelRated").GetBoolean())
        throw new InvalidOperationException("F1.12 bounded resource/journey evidence is incomplete.");
    Console.WriteLine($"PASS F1.12_ExportedProductDogfood edition={edition} art={art} " +
        $"vegetation={vegetation} evidence={evidence}");
}

static void RequireExternalOfficialPresentation(
    string productDirectory,
    string executable,
    string product)
{
    string archive = Path.Combine(productDirectory, "Content", "Official",
        "ultrasim-official-actors-v1.uspkg");
    const string expectedArchiveHash =
        "03f77dd361bdf7c9edf564fc92c95ced31aa71378a5d14555cc670b1cda6c554";
    if (!File.Exists(archive) || new FileInfo(archive).Length != 4_723_829 ||
        !string.Equals(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant(),
            expectedArchiveHash, StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"The {product} product omitted its external Official package.");
    byte[] executableBytes = File.ReadAllBytes(executable);
    foreach (byte[] forbidden in new[]
        {
            "x_bot_animated.glb"u8.ToArray(),
            "y_bot_animated.glb"u8.ToArray(),
            "x_bot_visual.tscn"u8.ToArray(),
            "y_bot_visual.tscn"u8.ToArray(),
        })
        if (executableBytes.AsSpan().IndexOf(forbidden) >= 0)
            throw new InvalidOperationException(
                $"The {product} executable/PCK still names bundled high-quality actor art.");
    foreach (string assembly in Directory.EnumerateFiles(
        ExportDataDirectory(productDirectory), "*.dll", SearchOption.TopDirectoryOnly))
    {
        byte[] bytes = File.ReadAllBytes(assembly);
        if (bytes.AsSpan().IndexOf("UltraSim.ConceptDemo.ActorX.glb"u8) >= 0 ||
            bytes.AsSpan().IndexOf("UltraSim.ConceptDemo.ActorY.glb"u8) >= 0)
            throw new InvalidOperationException(
                $"The {product} managed closure embeds the retired actor byte source: {assembly}.");
    }
}

static void CopyProductWithoutOfficialContent(string source, string destination)
{
    if (Directory.Exists(destination)) Directory.Delete(destination, recursive: true);
    Directory.CreateDirectory(destination);
    foreach (string directory in Directory.EnumerateDirectories(
                 source, "*", SearchOption.AllDirectories))
    {
        string relative = Relative(source, directory);
        if (relative.Equals("Content", StringComparison.Ordinal) ||
            relative.StartsWith("Content/", StringComparison.Ordinal))
            continue;
        Directory.CreateDirectory(Path.Combine(destination,
            relative.Replace('/', Path.DirectorySeparatorChar)));
    }
    foreach (string file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
    {
        string relative = Relative(source, file);
        if (relative.Equals("Content", StringComparison.Ordinal) ||
            relative.StartsWith("Content/", StringComparison.Ordinal))
            continue;
        string output = Path.Combine(destination, relative.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        File.Copy(file, output, overwrite: true);
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(output, File.GetUnixFileMode(file));
    }
}

static void RequireSameFileHash(string first, string second, string label)
{
    byte[] firstHash = SHA256.HashData(File.ReadAllBytes(first));
    byte[] secondHash = SHA256.HashData(File.ReadAllBytes(second));
    if (!firstHash.AsSpan().SequenceEqual(secondHash))
        throw new InvalidOperationException($"{label} changed across content layouts.");
}

static void ExportClient(
    string root,
    string godot,
    string edition,
    string executable)
{
    Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
    Run(
        root,
        Path.Combine(root, "Client", "Godot", "export-client.sh"),
        [edition, executable],
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ULTRASIM_GODOT"] = godot,
        });
}

static void RequireExactExportAssemblies(
    string exportDirectory,
    string[] expectedAssemblies,
    string product)
{
    string data = ExportDataDirectory(exportDirectory);
    string dependenciesPath = Path.Combine(
        data, "UltraSim.Client.Application.Godot.deps.json");
    if (!File.Exists(dependenciesPath))
        throw new InvalidOperationException(
            $"The actual {product} export omitted its managed dependency manifest.");
    string[] actualAssemblies = Directory.EnumerateFiles(
            data, "UltraSim*.dll", SearchOption.TopDirectoryOnly)
        .Select(static path => Path.GetFileNameWithoutExtension(path))
        .OrderBy(static value => value, StringComparer.Ordinal)
        .ToArray();
    if (!actualAssemblies.SequenceEqual(expectedAssemblies, StringComparer.Ordinal))
        throw new InvalidOperationException(
            $"The actual {product} Godot export has an unexpected UltraSim assembly closure.\n" +
            $"Expected: {string.Join(", ", expectedAssemblies)}\n" +
            $"Actual: {string.Join(", ", actualAssemblies)}");
}

static string ExportDataDirectory(string exportDirectory) => Path.Combine(
    exportDirectory, "data_UltraSim.Client.Application.Godot_linuxbsd_x86_64");

static void VerifyContentlessOnlineNative(
    string root,
    string configuration,
    string clientExecutable,
    IReadOnlyList<string> clientArgumentPrefix)
{
    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "a0.8-native");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    string identity = Path.Combine(scope, "deployment");
    string cache = Path.Combine(scope, "content-cache");
    string trust = Path.Combine(scope, "client", "trusted-servers.json");
    string profileDecisions = Path.Combine(scope, "client", "server-profiles.json");
    Directory.CreateDirectory(scope);

    NativeDeploymentReady coldReady;
    using (Process coldServer = StartNativeDeployment(
               root, configuration, identity, Path.Combine(scope, "cold-ready.json"),
               ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
               ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp), out coldReady))
    {
        try
        {
            string cold = RunCaptured(root, clientExecutable,
            clientArgumentPrefix.Concat(
            [
                "--a0-8-smoke",
                $"--a0-8-endpoint={coldReady.Endpoint}",
                $"--a0-8-fingerprint={coldReady.Fingerprint}",
                $"--a0-8-cache={cache}",
                $"--a0-8-trust={trust}",
                $"--f0-3c-profile-decisions={profileDecisions}",
                "--a0-8-allow-private-content",
                "--a0-8-expect=cold",
                "--expect-edition=Online",
            ]), 45_000);
            RequireNativeA08Result(cold, "cold", coldReady.StructuralManifestFingerprint,
                expectedWarm: false, expectedRememberedProfile: false,
                expectedProfile: "grammar=ThirdPersonDirect camera=Perspective/Free/Free " +
                    "distance=8.5 canonicalFov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT");
        }
        finally { StopOwnedProcess(coldServer); }
    }

    NativeDeploymentReady warmReady;
    using (Process warmServer = StartNativeDeployment(
               root, configuration, identity, Path.Combine(scope, "warm-ready.json"),
               ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
               ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp), out warmReady))
    {
        try
        {
            if (warmReady.Realm != coldReady.Realm ||
                warmReady.Fingerprint != coldReady.Fingerprint ||
                warmReady.CatalogFingerprint != coldReady.CatalogFingerprint ||
                warmReady.WorldPackFingerprint != coldReady.WorldPackFingerprint ||
                warmReady.ClientProfile != coldReady.ClientProfile ||
                warmReady.ClientProfileRevision != coldReady.ClientProfileRevision ||
                warmReady.ClientProfileHash != coldReady.ClientProfileHash)
                throw new InvalidOperationException(
                    "The restarted Direct Connect deployment did not retain its realm/key/content/profile identity.");
            string warm = RunCaptured(root, clientExecutable,
            clientArgumentPrefix.Concat(
            [
                "--a0-8-smoke",
                $"--a0-8-endpoint={warmReady.Endpoint}",
                $"--a0-8-fingerprint={warmReady.Fingerprint}",
                $"--a0-8-cache={cache}",
                $"--a0-8-trust={trust}",
                $"--f0-3c-profile-decisions={profileDecisions}",
                $"--official-content-root={Path.Combine(root, "Client", "OfficialContent")}",
                "--f1-4-expect-local-fallback",
                "--a0-8-allow-private-content",
                "--a0-8-expect=warm",
                "--expect-edition=Online",
            ]), 45_000);
            RequireNativeA08Result(warm, "warm", warmReady.StructuralManifestFingerprint,
                expectedWarm: true, expectedRememberedProfile: true,
                expectedProfile: "grammar=ThirdPersonDirect camera=Perspective/Free/Free " +
                    "distance=8.5 canonicalFov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT");
        }
        finally { StopOwnedProcess(warmServer); }
    }

    NativeDeploymentReady changedReady;
    using (Process changedServer = StartNativeDeployment(
               root, configuration, identity, Path.Combine(scope, "changed-ready.json"),
               ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
               ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp), out changedReady,
               clientProfile: "orbit"))
    {
        try
        {
            if (changedReady.Realm != coldReady.Realm ||
                changedReady.Fingerprint != coldReady.Fingerprint ||
                changedReady.CatalogFingerprint != coldReady.CatalogFingerprint ||
                changedReady.WorldPackFingerprint != coldReady.WorldPackFingerprint ||
                changedReady.ActorPresentationBytes != coldReady.ActorPresentationBytes ||
                changedReady.ClientProfileRevision != coldReady.ClientProfileRevision ||
                changedReady.ClientProfileHash == coldReady.ClientProfileHash ||
                changedReady.ClientProfile == coldReady.ClientProfile)
                throw new InvalidOperationException(
                    "The changed-profile deployment did not preserve authority/content identity " +
                    "while changing only the exact profile hash/selection.");
            string changed = RunCaptured(root, clientExecutable,
            clientArgumentPrefix.Concat(
            [
                "--a0-8-smoke",
                $"--a0-8-endpoint={changedReady.Endpoint}",
                $"--a0-8-fingerprint={changedReady.Fingerprint}",
                $"--a0-8-cache={cache}",
                $"--a0-8-trust={trust}",
                $"--f0-3c-profile-decisions={profileDecisions}",
                "--a0-8-allow-private-content",
                "--a0-8-expect=warm",
                "--expect-edition=Online",
            ]), 45_000);
            RequireNativeA08Result(changed, "warm", changedReady.StructuralManifestFingerprint,
                expectedWarm: true, expectedRememberedProfile: false,
                expectedProfile: "grammar=OrbitCommand camera=Perspective/Free/Free " +
                    "distance=14.0 canonicalFov=70 select=MouseButton:LEFT primary=MouseButton:MIDDLE");
        }
        finally { StopOwnedProcess(changedServer); }
    }

    // The exported client begins with one prepared Pack, then the test-only server setup
    // places its admitted guest at the real central Gate. Interaction, Far Pack demand,
    // ordinary seam walking, retirement and warm reprepare use the production paths.
    string w7Trigger = Path.Combine(scope, "w7-gate-trigger");
    NativeDeploymentReady w7Ready;
    using (Process w7Server = StartNativeDeployment(
               root, configuration, identity, Path.Combine(scope, "w7-ready.json"),
               ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
               ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp), out w7Ready,
               nativeGateTriggerFile: w7Trigger))
    {
        try
        {
            string journey = RunCaptured(root, clientExecutable,
                clientArgumentPrefix.Concat(
                [
                    "--a0-8-smoke",
                    $"--a0-8-endpoint={w7Ready.Endpoint}",
                    $"--a0-8-fingerprint={w7Ready.Fingerprint}",
                    $"--a0-8-cache={Path.Combine(scope, "w7-cache")}",
                    $"--a0-8-trust={trust}",
                    $"--f0-3c-profile-decisions={profileDecisions}",
                    "--a0-8-allow-private-content",
                    "--a0-8-expect=cold",
                    "--f1-9a-scaled-output",
                    "--f1-11-vegetation",
                    $"--w7-native-gate-trigger-file={w7Trigger}",
                    "--expect-edition=Online",
                ]), 90_000);
            string? passLine = journey.Split('\n').FirstOrDefault(line =>
                line.StartsWith("W7 NATIVE PACK JOURNEY PASS", StringComparison.Ordinal));
            // A bounded number of already-in-flight publications can address semantic chunks
            // retired while walking 127/128/127. They must all name still-prepared Pack truth;
            // none may be caused by the Far Pack's acquisition/readiness interval.
            if (passLine is null ||
                !journey.Contains("F1.9A SCALED OUTPUT PASS app=1920x1080 " +
                    "frame=1920x1080 output=1280x720 internal3D=960x540 " +
                    "world=SubViewport", StringComparison.Ordinal) ||
                !journey.Contains("W7 NATIVE DELIVERY CHECK farArrivalRejected=0 " +
                    "farReadyRejected=0", StringComparison.Ordinal) ||
                !journey.Contains("W7 NATIVE PACK JOURNEY PASS gate=real " +
                    "seam=127/128/127 retirement=guarded warmBytes=0 " +
                    "totalPackBytes=731048", StringComparison.Ordinal) ||
                !passLine.Contains("rejectedAtFar=0", StringComparison.Ordinal) ||
                !TryReadNamedCount(passLine, "rejectedPublic=", out ulong rejected) ||
                !TryReadNamedCount(passLine, "missingResident=", out ulong missing) ||
                !TryReadNamedCount(passLine, "missingPrepared=", out ulong prepared) ||
                !TryReadNamedCount(passLine, "missingSnapshotBegin=", out ulong missingSnapshotBegin) ||
                rejected + missingSnapshotBegin > 4 || rejected + missingSnapshotBegin != missing || missing != prepared ||
                !passLine.Contains("contradictory=0 malformed=0 capacity=0", StringComparison.Ordinal) ||
                !ContainsPositiveNamedCount(journey, "rendererChunks=") ||
                !journey.Contains(" actors=1 publicActor=", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"The exported W7 journey did not complete:\n{Tail(journey, 8_000)}");
            Console.WriteLine($"PASS W7_ExportedOnlinePackJourney " +
                $"stalePreparedWindowDeliveries={rejected}");
        }
        finally { StopOwnedProcess(w7Server); }
    }

    Console.WriteLine(
        $"PASS A0.8_ContentlessOnlineNative coldWarm=true profileChange=true realm={coldReady.Realm} " +
        $"world={coldReady.WorldPackFingerprint} chunks={coldReady.WorldChunks} " +
        $"archiveBytes={coldReady.ArchiveBytes} actorBytes={coldReady.ActorPresentationBytes} " +
        $"profileRevision={coldReady.ClientProfileRevision} " +
        $"thirdPersonHash={coldReady.ClientProfileHash} orbitHash={changedReady.ClientProfileHash}");
    Console.WriteLine(
        "PASS F0.8_DifferentServerFeel executable=same " +
        "thirdPerson=ThirdPersonDirect/ThirdPersonFollow/8.5m/76deg/MIDDLE-select/LEFT-primary " +
        "orbit=OrbitCommand/Orbit/14m/70deg/LEFT-select/MIDDLE-primary " +
        "realm=same content=same warmRefetchBytes=0 newConsent=true");
    Directory.Delete(scope, recursive: true);
}

static void VerifyEmbeddedCompleteHostedNative(
    string root,
    string onlineExecutable,
    string completeExecutable)
{
    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "m0.0-complete-host");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string readyFile = Path.Combine(scope, "host-ready.json");
    int gameplayPort = ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp);
    int contentPort = ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp);
    var start = new ProcessStartInfo(completeExecutable)
    {
        WorkingDirectory = root,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (string argument in new[]
    {
        "--headless", "--",
        "--m0-0-host-smoke", "--expect-edition=Complete",
        $"--m0-0-gameplay-port={gameplayPort}",
        $"--m0-0-content-port={contentPort}",
        $"--m0-0-ready={readyFile}",
    }) start.ArgumentList.Add(argument);
    Console.WriteLine(
        $"> {completeExecutable} {string.Join(' ', start.ArgumentList.Select(Quote))}");
    using Process host = Process.Start(start) ??
        throw new InvalidOperationException("Unable to start the Complete embedded host.");
    Task<string> hostStandardOutput = host.StandardOutput.ReadToEndAsync();
    Task<string> hostStandardError = host.StandardError.ReadToEndAsync();
    try
    {
        long readyDeadline = Environment.TickCount64 + 30_000;
        while (!File.Exists(readyFile) && Environment.TickCount64 < readyDeadline)
        {
            if (host.HasExited)
                throw new InvalidOperationException(
                    $"The Complete host exited before readiness with code {host.ExitCode}:\n" +
                    hostStandardOutput.GetAwaiter().GetResult() +
                    hostStandardError.GetAwaiter().GetResult());
            Thread.Sleep(10);
        }
        if (!File.Exists(readyFile))
            throw new TimeoutException("The Complete host did not publish readiness in 30 seconds.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(readyFile));
        string endpoint = RequiredString(document.RootElement, "Endpoint");
        string fingerprint = RequiredString(document.RootElement, "Fingerprint");
        ulong generation = document.RootElement.GetProperty("Generation").GetUInt64();
        string release = Path.Combine(scope, "clients-release");
        string bobReady = Path.Combine(scope, "bob", "ready");
        string carolReady = Path.Combine(scope, "carol", "ready");

        Task<string> bob = Task.Run(() => RunCaptured(
            root, onlineExecutable,
            NativeSharedClientArguments(
                scope, "bob", endpoint, fingerprint, bobReady, release), 120_000));
        Task<string> carol = Task.Run(() => RunCaptured(
            root, onlineExecutable,
            NativeSharedClientArguments(
                scope, "carol", endpoint, fingerprint, carolReady, release), 120_000));
        long clientReadyDeadline = Environment.TickCount64 + 90_000;
        while ((!File.Exists(bobReady) || !File.Exists(carolReady)) &&
               Environment.TickCount64 < clientReadyDeadline)
        {
            if (host.HasExited)
                throw new InvalidOperationException(
                    "The Complete host exited before both remote clients reached the barrier.");
            if (bob.IsFaulted || carol.IsFaulted)
                throw new InvalidOperationException(
                    "An exported Online client failed before the shared-ready barrier.",
                    bob.Exception ?? carol.Exception);
            Thread.Sleep(10);
        }
        if (!File.Exists(bobReady) || !File.Exists(carolReady))
            throw new TimeoutException(
                "Both exported Online clients did not concurrently reach shared presentation.");
        File.WriteAllText(release, "release" + Environment.NewLine);
        string bobOutput = bob.GetAwaiter().GetResult();
        string carolOutput = carol.GetAwaiter().GetResult();
        RequireNativeA08Result(
            bobOutput, "cold", expectedManifestFingerprint: string.Empty,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: "grammar=ThirdPersonDirect camera=Perspective/Free/Free " +
                "distance=8.5 canonicalFov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT",
            requireManifestFingerprint: false);
        RequireNativeA08Result(
            carolOutput, "cold", expectedManifestFingerprint: string.Empty,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: "grammar=ThirdPersonDirect camera=Perspective/Free/Free " +
                "distance=8.5 canonicalFov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT",
            requireManifestFingerprint: false);
        // Three humans plus the existing woodland animal must be concurrently presented.
        if (!bobOutput.Contains("actors=4", StringComparison.Ordinal) ||
            !carolOutput.Contains("actors=4", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The two Online clients did not simultaneously realize all three shared actors.");

        if (!host.WaitForExit(30_000))
            throw new TimeoutException(
                "The Complete host did not observe both clean remote detachments.");
        string hostOutput = hostStandardOutput.GetAwaiter().GetResult() +
            hostStandardError.GetAwaiter().GetResult();
        Console.Write(hostOutput);
        if (host.ExitCode != 0 ||
            !hostOutput.Contains("M0.0 COMPLETE HOST PASS", StringComparison.Ordinal) ||
            !hostOutput.Contains("remoteParticipants=2", StringComparison.Ordinal) ||
            !hostOutput.Contains($"generation={generation}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The Complete hosted native evidence was incomplete:\n{Tail(hostOutput, 8_000)}");
        Console.WriteLine(
            $"PASS M0.0_CompleteHostedNative generation={generation} " +
            "hostProduct=exported localPath=packet-free remoteClients=2 " +
            "remoteProducts=exported humansPerClient=3 actorsPerClient=4 detach=clean");
    }
    finally
    {
        StopOwnedProcess(host);
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

static void VerifyPf2MixedExportedAccountingSwarm(
    string root,
    string configuration,
    string onlineExecutable,
    bool detailedTelemetry = false,
    int warmupSeconds = 5,
    int measurementSeconds = 20,
    string? evidenceOutputDirectory = null,
    string telemetryCheckpoint = "PF2.5",
    string aliceRenderMode = "headless",
    string bobRenderMode = "headless",
    bool externalImpairment = false,
    int botCount = 8)
{
    if (botCount is < 1 or > 62)
        throw new ArgumentOutOfRangeException(nameof(botCount));
    const int nativeClientCount = 2;
    int participantCapacity = botCount + nativeClientCount;
    int expectedPresentedActors = participantCapacity + 1;
    const ulong seed = 130_363;
    const string expectedProfile =
        "grammar=ThirdPersonDirect camera=Perspective/Free/Free " +
        "distance=8.5 canonicalFov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT";
    int clientTimeoutMilliseconds = checked(Math.Max(
        300_000, (warmupSeconds + measurementSeconds + 300) * 1_000));

    VerifyPf1BotArtifact(root, configuration);
    Run(root, "dotnet",
    [
        "build", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration,
        "-p:UltraSimNetworkFlavor=ServerOnly",
    ]);

    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf2-accounting-swarm");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    string serverStop = Path.Combine(scope, "server-stop");
    string serverSummary = Path.Combine(scope, "server-summary.json");
    string accountingStart = Path.Combine(scope, "accounting-start");
    string accountingReady = Path.Combine(scope, "accounting-ready.json");
    string accountingStop = Path.Combine(scope, "accounting-stop");
    string accountingSummary = Path.Combine(scope, "accounting-summary.json");
    string aliceTelemetryReady = Path.Combine(scope, "alice", "telemetry-ready");
    string bobTelemetryReady = Path.Combine(scope, "bob", "telemetry-ready");
    string aliceTelemetrySummary = Path.Combine(scope, "alice", "telemetry-summary.json");
    string bobTelemetrySummary = Path.Combine(scope, "bob", "telemetry-summary.json");
    string impairmentSummary = Path.Combine(scope, "external-udp-impairment.json");
    string botStartReady = Path.Combine(scope, "bots-start-ready");
    string botStartRelease = Path.Combine(scope, "bots-start-release");
    string measurementReady = Path.Combine(scope, "measurement-ready");
    string measurementRelease = Path.Combine(scope, "measurement-release");
    string measurementComplete = Path.Combine(scope, "measurement-complete");
    string botReady = Path.Combine(scope, "bots-ready");
    string botRelease = Path.Combine(scope, "bots-release");
    string botSummary = Path.Combine(scope, "bots-summary.json");
    string nativeRelease = Path.Combine(scope, "native-release");
    string nativeRetireRelease = Path.Combine(scope, "native-retire-release");
    string aliceReady = Path.Combine(scope, "alice", "ready");
    string bobReady = Path.Combine(scope, "bob", "ready");
    string aliceComplete = Path.Combine(scope, "alice", "complete");
    string bobComplete = Path.Combine(scope, "bob", "complete");

    Process? server = null;
    ExternalUdpImpairmentProxy? aliceProxy = null;
    ExternalUdpImpairmentProxy? bobProxy = null;
    Task<string>? botsTask = null;
    Task<string>? aliceTask = null;
    Task<string>? bobTask = null;
    try
    {
        server = StartNativeDeployment(
            root, configuration, Path.Combine(scope, "deployment"),
            Path.Combine(scope, "server-ready.json"),
            ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
            ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp),
            out NativeDeploymentReady ready,
            maximumParticipants: participantCapacity,
            admissionBurstPerAddress: 128,
            stopFile: serverStop,
            summaryFile: serverSummary,
            accountingStartFile: accountingStart,
            accountingReadyFile: accountingReady,
            accountingStopFile: accountingStop,
            accountingSummaryFile: accountingSummary,
            telemetryLevel: detailedTelemetry ? "Detailed" : "Off");

        string aliceEndpoint = ready.Endpoint;
        string bobEndpoint = ready.Endpoint;
        if (externalImpairment)
        {
            IPEndPoint serverEndpoint = ParseUdpEndpoint(ready.Endpoint);
            aliceProxy = new ExternalUdpImpairmentProxy(serverEndpoint);
            bobProxy = new ExternalUdpImpairmentProxy(serverEndpoint);
            aliceEndpoint = aliceProxy.Endpoint;
            bobEndpoint = bobProxy.Endpoint;
        }

        botsTask = Task.Run(() => RunCaptured(root, "dotnet",
        [
            "run", "--project", "Tools/NetworkBots/UltraSim.NetworkBots.csproj",
            "-c", configuration, "-p:UltraSimNetworkFlavor=ClientOnly", "--no-build", "--",
            $"--endpoint={ready.Endpoint}", $"--fingerprint={ready.Fingerprint}",
            $"--bots={botCount}", $"--seed={seed}", $"--duration={measurementSeconds}s",
            $"--warmup={warmupSeconds}s",
            "--scenario=mixed-chaos", $"--cache={Path.Combine(scope, "bots")}",
            $"--start-ready-file={botStartReady}",
            $"--start-release-file={botStartRelease}",
            $"--measurement-ready-file={measurementReady}",
            $"--measurement-release-file={measurementRelease}",
            $"--measurement-complete-file={measurementComplete}",
            $"--ready-file={botReady}", $"--release-file={botRelease}",
            $"--summary-file={botSummary}", "--admission-stagger-ms=100",
            $"--telemetry={(detailedTelemetry ? "Detailed" : "Off")}",
            telemetryCheckpoint is "PF3" or "PF4A" or "PF4B" or "PF4C" or "PF4D" or "PF4E" or "PF4F" or "PF5"
                ? $"--checkpoint={telemetryCheckpoint}" :
                detailedTelemetry ? "--checkpoint=PF2.5" : "--checkpoint=PF2",
        ], clientTimeoutMilliseconds));
        aliceTask = Task.Run(() => RunCaptured(
            root, onlineExecutable,
            NativeSharedClientArguments(
                scope, "alice", aliceEndpoint, ready.Fingerprint,
                aliceReady, nativeRelease, expectedPresentedActors,
                "PF2 Native Alice", "x", aliceComplete, nativeRetireRelease,
                detailedTelemetry ? "Detailed" : null,
                detailedTelemetry ? accountingStart : null,
                detailedTelemetry ? aliceTelemetryReady : null,
                detailedTelemetry ? aliceTelemetrySummary : null,
                detailedTelemetry ? telemetryCheckpoint : null,
                aliceRenderMode), clientTimeoutMilliseconds));
        bobTask = Task.Run(() => RunCaptured(
            root, onlineExecutable,
            NativeSharedClientArguments(
                scope, "bob", bobEndpoint, ready.Fingerprint,
                bobReady, nativeRelease, expectedPresentedActors,
                "PF2 Native Bob", "y", bobComplete, nativeRetireRelease,
                detailedTelemetry ? "Detailed" : null,
                detailedTelemetry ? accountingStart : null,
                detailedTelemetry ? bobTelemetryReady : null,
                detailedTelemetry ? bobTelemetrySummary : null,
                detailedTelemetry ? telemetryCheckpoint : null,
                bobRenderMode), clientTimeoutMilliseconds));

        WaitForPf1Files([botStartReady, aliceReady, bobReady], 120_000, server,
            [botsTask, aliceTask, bobTask], "PF2 clients to complete admission");
        aliceProxy?.EnableImpairment();
        bobProxy?.EnableImpairment();
        File.WriteAllText(botStartRelease, "start\n");
        WaitForPf1Files([measurementReady], checked((warmupSeconds + 60) * 1_000), server,
            [botsTask, aliceTask, bobTask], "PF2 bot warmup to complete");
        File.WriteAllText(accountingStart, "start\n");
        string[] telemetryReady = detailedTelemetry
            ? [accountingReady, aliceTelemetryReady, bobTelemetryReady]
            : [accountingReady];
        WaitForPf1Files(telemetryReady, 10_000, server,
            [botsTask, aliceTask, bobTask], "PF2 server accounting to start");
        File.WriteAllText(measurementRelease, "measure\n");

        WaitForPf1Files([measurementComplete], checked((measurementSeconds + 60) * 1_000), server,
            [botsTask, aliceTask, bobTask], "PF2 measured window to complete");
        if (aliceTask.IsCompleted || bobTask.IsCompleted)
            throw new InvalidOperationException(
                "An exported Online client did not remain healthy throughout PF2 accounting.");
        File.WriteAllText(accountingStop, "stop\n");
        WaitForPf1Files([accountingSummary], 10_000, server,
            [botsTask, aliceTask, bobTask], "PF2 server accounting to finalize");
        WaitForPf1Files([botReady], 30_000, server,
            [botsTask, aliceTask, bobTask], "PF2 accepted work to quiesce");

        File.WriteAllText(nativeRelease, "release\n");
        WaitForPf1Files([aliceComplete, bobComplete], 30_000, server,
            [aliceTask, bobTask], "PF2 native public presentation to converge");
        File.WriteAllText(nativeRetireRelease, "retire\n");
        string aliceOutput = aliceTask.GetAwaiter().GetResult();
        string bobOutput = bobTask.GetAwaiter().GetResult();
        RequireNativeA08Result(aliceOutput, "cold", ready.StructuralManifestFingerprint,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: expectedProfile);
        RequireNativeA08Result(bobOutput, "cold", ready.StructuralManifestFingerprint,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: expectedProfile);

        File.WriteAllText(botRelease, "release\n");
        string botOutput = botsTask.GetAwaiter().GetResult();
        string expectedBotCheckpoint = telemetryCheckpoint is "PF3" or "PF4A" or "PF4B" or "PF4C" or "PF4D" or "PF4E" or "PF4F" or "PF5"
            ? telemetryCheckpoint
            : detailedTelemetry ? "PF2.5" : "PF2";
        int expectedBotSchema = expectedBotCheckpoint switch
        {
            "PF3" => 5,
            "PF4A" => 6,
            "PF4B" => 7,
            "PF4C" => 8,
            "PF4D" => 9,
            "PF4E" => 10,
            "PF4F" => 11,
            "PF5" => 12,
            "PF2.5" => 4,
            _ => 3,
        };
        if (!botOutput.Contains(
                $"{expectedBotCheckpoint} BOT HARNESS PASS",
                StringComparison.Ordinal) ||
            !File.Exists(botSummary))
            throw new InvalidOperationException(
                $"The PF2 bot harness omitted its accepted accounting summary:\n{Tail(botOutput, 8_000)}");
        using JsonDocument botDocument = JsonDocument.Parse(File.ReadAllText(botSummary));
        JsonElement bot = botDocument.RootElement;
        JsonElement wire = bot.GetProperty("wire");
        JsonElement measuredPump = bot.GetProperty("measuredPump");
        if (!bot.GetProperty("passed").GetBoolean() ||
            bot.GetProperty("schemaVersion").GetInt32() != expectedBotSchema ||
            bot.GetProperty("checkpoint").GetString() != expectedBotCheckpoint ||
            bot.GetProperty("successfulReconnects").GetInt32() < 1 ||
            bot.GetProperty("freshProofLifetimes").GetInt32() <= botCount ||
            (detailedTelemetry &&
                (bot.GetProperty("syntheticInputSamples").GetInt32() == 0 ||
                 bot.GetProperty("syntheticInputSamplesPerSecond").GetDouble() <= 0)) ||
            wire.GetProperty("clientToServerUdpPayloadBytes").GetUInt64() == 0 ||
            wire.GetProperty("serverToClientUdpPayloadBytes").GetUInt64() == 0 ||
            !wire.GetProperty("physicalTransportReconciled").GetBoolean() ||
            !wire.GetProperty("logicalMessageReconciled").GetBoolean() ||
            wire.GetProperty("lanes").GetArrayLength() != 7 ||
            wire.GetProperty("families").GetArrayLength() != 13 ||
            bot.GetProperty("lifetimeHttpBody").GetProperty("archiveBodyBytes").GetInt64() == 0 ||
            measuredPump.GetProperty("samples").GetUInt64() == 0 ||
            bot.GetProperty("faultedBots").GetInt32() != 0 ||
            !bot.GetProperty("cleanRetirement").GetBoolean())
            throw new InvalidOperationException(
                "The PF2 client accounting/reconnect/retirement contract did not hold.");
        if (expectedBotCheckpoint is "PF4A" or "PF4B" or "PF4C" or "PF4D" or "PF4E" or "PF4F" or "PF5" &&
            bot.GetProperty("actionOutcomes").EnumerateArray().Any(static outcome =>
                !outcome.GetProperty("conserved").GetBoolean() ||
                !outcome.GetProperty("terminal").GetBoolean() ||
                outcome.GetProperty("unexplained").GetUInt64() != 0))
            throw new InvalidOperationException(
                "The PF4A bot action lifecycle contained unexplained or terminal debt.");

        if (expectedBotCheckpoint is "PF4D" or "PF4E" or "PF4F" or "PF5")
        {
            JsonElement aggregation = wire.GetProperty("unreliableAggregation");
            if (bot.GetProperty("unreliableFrameCap").GetInt32() <= 1 ||
                aggregation.GetProperty("receivedDatagramsWithTwoOrMoreFrames").GetUInt64() == 0)
                throw new InvalidOperationException(
                    "The PF4D ClientOnly product did not receive an ordinary aggregate datagram.");
        }

        using JsonDocument accountingDocument = JsonDocument.Parse(
            File.ReadAllText(accountingSummary));
        JsonElement accounting = accountingDocument.RootElement;
        if (!accounting.GetProperty("PhysicalTransportReconciled").GetBoolean() ||
            !accounting.GetProperty("LogicalMessageReconciled").GetBoolean() ||
            accounting.GetProperty("ParticipantsAtStart").GetInt32() != participantCapacity ||
            accounting.GetProperty("ConnectionsAtStart").GetInt32() != participantCapacity ||
            accounting.GetProperty("ConnectionLifetimes").GetInt32() < participantCapacity + 1 ||
            accounting.GetProperty("ListenerUdpPayload").GetProperty("PayloadBytesSent").GetUInt64() == 0 ||
            accounting.GetProperty("ListenerUdpPayload").GetProperty("PayloadBytesReceived").GetUInt64() == 0 ||
            accounting.GetProperty("Lanes").GetArrayLength() != 7 ||
            accounting.GetProperty("Families").GetArrayLength() != 13)
            throw new InvalidOperationException(
                "The dedicated authority did not reconcile ordinary bot/native PF2 traffic.");
        if (expectedBotCheckpoint is "PF4D" or "PF4E" or "PF4F" or "PF5" &&
            accounting.GetProperty("UnreliableAggregation")
                .GetProperty("SentDatagramsWithTwoOrMoreFrames").GetUInt64() == 0)
            throw new InvalidOperationException(
                "The PF4D ServerOnly product did not send an ordinary aggregate datagram.");
        if (externalImpairment &&
            (aliceProxy is null || bobProxy is null ||
             aliceProxy.DroppedDatagrams == 0 || bobProxy.DroppedDatagrams == 0 ||
             aliceProxy.ReorderedDatagrams == 0 || bobProxy.ReorderedDatagrams == 0))
            throw new InvalidOperationException(
                $"The {expectedBotCheckpoint} exported clients did not remain active through external UDP loss and reorder.");
        if (externalImpairment)
            File.WriteAllText(impairmentSummary, JsonSerializer.Serialize(new
            {
                Schema = expectedBotCheckpoint switch
                {
                    "PF4F" => "ultrasim-pf4f-external-udp-impairment-v1",
                    "PF5" => "ultrasim-pf5-external-udp-impairment-v1",
                    "PF4E" => "ultrasim-pf4e-external-udp-impairment-v1",
                    _ => "ultrasim-pf4d-external-udp-impairment-v1",
                },
                DropPeriod = ExternalUdpImpairmentProxy.DropPeriod,
                ReorderPeriod = ExternalUdpImpairmentProxy.ReorderPeriod,
                AliceDropped = aliceProxy!.DroppedDatagrams,
                AliceReordered = aliceProxy.ReorderedDatagrams,
                BobDropped = bobProxy!.DroppedDatagrams,
                BobReordered = bobProxy.ReorderedDatagrams,
            }, new JsonSerializerOptions { WriteIndented = true }) + Environment.NewLine);

        if (detailedTelemetry)
        {
            RequireNativeTelemetry(
                aliceTelemetrySummary, telemetryCheckpoint, measurementSeconds,
                aliceRenderMode);
            RequireNativeTelemetry(
                bobTelemetrySummary, telemetryCheckpoint, measurementSeconds,
                bobRenderMode);
            if (accounting.GetProperty("NetworkTiming").GetProperty("Level").GetByte() != 2 ||
                accounting.GetProperty("AuthorityTiming").GetProperty("Level").GetByte() != 2 ||
                accounting.GetProperty("AuthorityTiming").GetProperty("SampleCount").GetUInt64() == 0 ||
                accounting.GetProperty("RemoteApplications").GetArrayLength() != participantCapacity)
                throw new InvalidOperationException(
                    "The PF2.5 authority timing projection was absent or incomplete.");

            if (telemetryCheckpoint is "PF4E" or "PF4F" or "PF5")
            {
                JsonElement remote = accounting.GetProperty("RemoteReplication");
                JsonElement publication = accounting.GetProperty("AuthoritativePublications");
                if (remote.GetProperty("Level").GetByte() != 2 ||
                    remote.GetProperty("CurrentRemoteRecipients").GetInt32() != participantCapacity ||
                    remote.GetProperty("CurrentMemberships").GetInt32() == 0 ||
                    remote.GetProperty("RecipientsPerDirtyChunk").GetProperty("Count").GetUInt64() == 0 ||
                    publication.GetProperty("TransformChanges").GetUInt64() == 0 ||
                    publication.GetProperty("PartialRecords").GetUInt64() == 0)
                    throw new InvalidOperationException(
                        "PF4E omitted its shared fanout/topology/source-record census.");
            }
            if (telemetryCheckpoint == "PF5")
            {
                JsonElement samples = accounting.GetProperty("TopologySamples");
                if (accounting.GetProperty("TopologySamplesSaturated").GetBoolean() ||
                    samples.GetArrayLength() < Math.Min(2, measurementSeconds) ||
                    !samples.EnumerateArray().Any(static sample =>
                        sample.GetProperty("RemoteRecipients").GetInt32() != 0 &&
                        sample.GetProperty("OccupiedChunks").GetInt32() != 0 &&
                        sample.GetProperty("Memberships").GetInt32() != 0))
                    throw new InvalidOperationException(
                        "PF5 omitted its bounded realized-topology time series.");
            }

            ulong serverWorldActions = 0;
            ulong serverScheduleAttempts = 0;
            ulong serverDeliveries = 0;
            foreach (JsonElement application in
                     accounting.GetProperty("RemoteApplications").EnumerateArray())
            {
                JsonElement timing = application.GetProperty("Timing");
                JsonElement replication = timing.GetProperty("Replication");
                JsonElement work = replication.GetProperty("Work");
                if (timing.GetProperty("Level").GetByte() != 2 ||
                    replication.GetProperty("Level").GetByte() != 2)
                    throw new InvalidOperationException(
                        "A PF2.5 application lifetime omitted Detailed Game/replication timing.");
                serverWorldActions += timing.GetProperty("WorldActionAndPhysics")
                    .GetProperty("Count").GetUInt64();
                serverScheduleAttempts += work.GetProperty("ScheduleAttempts").GetUInt64();
                serverDeliveries += work.GetProperty("DeliveriesQueued").GetUInt64();
                if (telemetryCheckpoint == "PF4A")
                {
                    JsonElement readiness = timing.GetProperty("ReplicationReadiness");
                    JsonElement scheduler = readiness.GetProperty("Scheduler");
                    if (readiness.GetProperty("DrivePasses").GetUInt64() == 0 ||
                        scheduler.GetProperty("RecipientCount").GetInt32() != 1 ||
                        scheduler.GetProperty("MembershipCount").GetInt32() == 0 ||
                        scheduler.GetProperty("CommitsObserved").GetUInt64() == 0)
                        throw new InvalidOperationException(
                            "A PF4A application omitted bounded replication wake attribution.");
                }
                if (telemetryCheckpoint is "PF4E" or "PF4F" or "PF5")
                {
                    JsonElement publication = application.GetProperty("World")
                        .GetProperty("Timing").GetProperty("Knowledge")
                        .GetProperty("Publication");
                    if (publication.GetProperty("DeltaRecords").GetUInt64() == 0 ||
                        publication.GetProperty("UniquePublicEntities").GetInt32() == 0 ||
                        publication.GetProperty("DeltaPayloadBytes")
                            .GetProperty("Count").GetUInt64() == 0)
                        throw new InvalidOperationException(
                            "A PF4E application omitted record/payload/self-public attribution.");
                }
                if (telemetryCheckpoint is "PF4F" or "PF5")
                {
                    JsonElement compression = application.GetProperty("World")
                        .GetProperty("Timing").GetProperty("Knowledge")
                        .GetProperty("Compression");
                    ulong canonicalBytes = compression.GetProperty("CanonicalBytesSelected").GetUInt64();
                    ulong compressedBytes = compression.GetProperty("CompressedRepresentationBytes").GetUInt64();
                    if (!compression.GetProperty("Enabled").GetBoolean() ||
                        compression.GetProperty("Selected").GetUInt64() == 0 ||
                        compression.GetProperty("CodecFailures").GetUInt64() != 0 ||
                        canonicalBytes <= compressedBytes ||
                        canonicalBytes - compressedBytes !=
                            compression.GetProperty("DirectBytesSaved").GetUInt64())
                        throw new InvalidOperationException(
                            "A PF4F application omitted coherent selected compression accounting.");
                }
            }
            if (serverWorldActions == 0 || serverScheduleAttempts == 0 || serverDeliveries == 0)
                throw new InvalidOperationException(
                    "PF2.5 did not observe authoritative world actions and public delivery work.");

            ulong ownerStates = 0;
            ulong worldApplies = 0;
            ulong clientProcesses = 0;
            JsonElement clients = bot.GetProperty("clientTelemetry");
            if (clients.GetArrayLength() != botCount)
                throw new InvalidOperationException(
                    "PF2.5 omitted per-client timing for a headless client.");
            foreach (JsonElement clientTelemetry in clients.EnumerateArray())
            {
                JsonElement client = clientTelemetry.GetProperty("client");
                if (client.GetProperty("level").GetByte() != 2 ||
                    clientTelemetry.GetProperty("network").GetProperty("level").GetByte() != 2)
                    throw new InvalidOperationException(
                        "A PF2.5 bot lifetime omitted Detailed client/Network timing.");
                clientProcesses += client.GetProperty("processReceived")
                    .GetProperty("count").GetUInt64();
                ownerStates += client.GetProperty("playable")
                    .GetProperty("ownerStatesApplied").GetUInt64();
                worldApplies += client.GetProperty("world")
                    .GetProperty("decodeAndApply").GetProperty("count").GetUInt64();
                if (telemetryCheckpoint is "PF4F" or "PF5")
                {
                    JsonElement compression = client.GetProperty("world")
                        .GetProperty("compressionCensus");
                    if (compression.GetProperty("messages").GetUInt64() == 0 ||
                        compression.GetProperty("malformed").GetUInt64() != 0 ||
                        compression.GetProperty("canonicalDecodedBytes").GetUInt64() <=
                            compression.GetProperty("compressedRepresentationBytes").GetUInt64())
                        throw new InvalidOperationException(
                            "A PF4F ClientOnly bot omitted coherent decompression accounting.");
                }
            }
            if (clientProcesses == 0 || ownerStates == 0 || worldApplies == 0)
                throw new InvalidOperationException(
                    "PF2.5 did not observe client receive, OwnerState, and public-world apply work.");
        }

        File.WriteAllText(serverStop, "stop\n");
        if (!server.WaitForExit(30_000) || server.ExitCode != 0 || !File.Exists(serverSummary))
            throw new InvalidOperationException("The PF2 authority did not retire cleanly.");
        if (!string.IsNullOrWhiteSpace(evidenceOutputDirectory))
        {
            string output = Path.GetFullPath(evidenceOutputDirectory);
            if (Directory.Exists(output))
                throw new InvalidOperationException(
                    $"PF2.5 evidence output already exists: '{output}'.");
            Directory.CreateDirectory(output);
            foreach ((string Source, string Name) file in new[]
                     {
                         (serverSummary, "server-summary.json"),
                         (accountingSummary, "accounting-summary.json"),
                         (botSummary, "bot-summary.json"),
                         (aliceTelemetrySummary, "native-alice-telemetry.json"),
                         (bobTelemetrySummary, "native-bob-telemetry.json"),
                         (impairmentSummary, "external-udp-impairment.json"),
                     })
                if (File.Exists(file.Source)) File.Copy(file.Source, Path.Combine(output, file.Name));
        }
        Console.WriteLine(
            $"PASS {(detailedTelemetry ? "PF2.5_Observability" : "PF2_MixedExportedClientAccounting")} " +
            $"generation={ready.ServerGeneration} " +
            $"bots={botCount} nativeClients={nativeClientCount} seed={seed} " +
            $"udp={wire.GetProperty("clientToServerUdpPayloadBytes").GetUInt64()}/" +
            $"{wire.GetProperty("serverToClientUdpPayloadBytes").GetUInt64()} " +
            $"lifetimes={accounting.GetProperty("ConnectionLifetimes").GetInt32()} " +
            $"nativeModes={aliceRenderMode}/{bobRenderMode} reconciled=true retired=true");
        if (externalImpairment)
            Console.WriteLine(
                $"PASS {expectedBotCheckpoint}_ExternalNativeImpairment " +
                $"aliceDrop/Reorder={aliceProxy!.DroppedDatagrams}/{aliceProxy.ReorderedDatagrams} " +
                $"bobDrop/Reorder={bobProxy!.DroppedDatagrams}/{bobProxy.ReorderedDatagrams}");
    }
    finally
    {
        Directory.CreateDirectory(scope);
        foreach (string marker in new[]
                 { botStartRelease, measurementRelease, nativeRelease,
                   nativeRetireRelease, botRelease, accountingStop, serverStop })
            if (!File.Exists(marker)) File.WriteAllText(marker, "release\n");
        foreach (Task<string>? task in new[] { aliceTask, bobTask, botsTask })
        {
            if (task is null) continue;
            try { _ = task.Wait(15_000); }
            catch (AggregateException) { }
        }
        if (server is not null)
        {
            StopOwnedProcess(server);
            server.Dispose();
        }
        aliceProxy?.Dispose();
        bobProxy?.Dispose();
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

static IPEndPoint ParseUdpEndpoint(string text)
{
    int separator = text.LastIndexOf(':');
    if (separator <= 0 ||
        !IPAddress.TryParse(text.AsSpan(0, separator), out IPAddress? address) ||
        !int.TryParse(text.AsSpan(separator + 1), out int port) ||
        port is <= IPEndPoint.MinPort or > IPEndPoint.MaxPort)
        throw new InvalidOperationException($"Could not parse UDP endpoint '{text}'.");
    return new IPEndPoint(address, port);
}

static void VerifyPf5HeadlessImpairmentSwarm(string root, string configuration)
{
    const int botCount = 16;
    const ulong seed = 505_517;
    var profiles = new (string Name, int DropPeriod, int ReorderPeriod, int HoldMilliseconds)[]
    {
        ("light", 101, 67, 10),
        ("moderate", 53, 37, 20),
        ("heavy-plausible", 29, 23, 35),
    };
    VerifyPf1BotArtifact(root, configuration);
    Run(root, "dotnet",
    [
        "build", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ServerOnly",
    ]);

    foreach (var profile in profiles)
    {
        string scope = Path.Combine(Path.GetTempPath(), "ultrasim-repository-verification",
            Environment.ProcessId.ToString(), "pf5-impairment", profile.Name);
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
        Directory.CreateDirectory(scope);
        string serverStop = Path.Combine(scope, "server-stop");
        string serverSummary = Path.Combine(scope, "server-summary.json");
        string accountingStart = Path.Combine(scope, "accounting-start");
        string accountingReady = Path.Combine(scope, "accounting-ready.json");
        string accountingStop = Path.Combine(scope, "accounting-stop");
        string accountingSummary = Path.Combine(scope, "accounting-summary.json");
        string startReady = Path.Combine(scope, "bot-start-ready");
        string startRelease = Path.Combine(scope, "bot-start-release");
        string botReady = Path.Combine(scope, "bot-ready");
        string botRelease = Path.Combine(scope, "bot-release");
        string botSummary = Path.Combine(scope, "bot-summary.json");
        Process? server = null;
        Task<string>? botTask = null;
        var proxies = new List<ExternalUdpImpairmentProxy>(botCount);
        try
        {
            server = StartNativeDeployment(
                root, configuration, Path.Combine(scope, "deployment"),
                Path.Combine(scope, "server-ready.json"),
                ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
                ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp),
                out NativeDeploymentReady ready,
                maximumParticipants: botCount,
                admissionBurstPerAddress: 128,
                stopFile: serverStop,
                summaryFile: serverSummary,
                accountingStartFile: accountingStart,
                accountingReadyFile: accountingReady,
                accountingStopFile: accountingStop,
                accountingSummaryFile: accountingSummary,
                telemetryLevel: "Detailed");
            IPEndPoint serverEndpoint = ParseUdpEndpoint(ready.Endpoint);
            for (int index = 0; index < botCount; index++)
                proxies.Add(new ExternalUdpImpairmentProxy(
                    serverEndpoint, profile.DropPeriod, profile.ReorderPeriod,
                    profile.HoldMilliseconds));
            string endpointList = string.Join(',', proxies.Select(static value => value.Endpoint));
            botTask = Task.Run(() => RunCaptured(root, "dotnet",
            [
                "run", "--project", "Tools/NetworkBots/UltraSim.NetworkBots.csproj",
                "-c", configuration, "-p:UltraSimNetworkFlavor=ClientOnly", "--no-build", "--",
                $"--endpoint={ready.Endpoint}", $"--endpoint-list={endpointList}",
                $"--fingerprint={ready.Fingerprint}", $"--bots={botCount}",
                $"--seed={seed}", "--duration=20s", "--warmup=0s",
                "--scenario=mixed-chaos", $"--cache={Path.Combine(scope, "bots")}",
                $"--start-ready-file={startReady}", $"--start-release-file={startRelease}",
                $"--ready-file={botReady}", $"--release-file={botRelease}",
                $"--summary-file={botSummary}", "--admission-stagger-ms=100",
                "--telemetry=Detailed", "--checkpoint=PF5",
                "--unreliable-frame-cap=2", "--unreliable-aggregation-scope=cross-lane",
            ], 420_000));

            WaitForPf1Files([startReady], 180_000, server, [botTask],
                $"PF5 {profile.Name} headless swarm admission");
            File.WriteAllText(accountingStart, "start\n");
            WaitForPf1Files([accountingReady], 10_000, server, [botTask],
                $"PF5 {profile.Name} accounting start");
            foreach (ExternalUdpImpairmentProxy proxy in proxies) proxy.EnableImpairment();
            File.WriteAllText(startRelease, "start\n");
            WaitForPf1Files([botReady], 90_000, server, [botTask],
                $"PF5 {profile.Name} mixed-chaos completion");
            File.WriteAllText(accountingStop, "stop\n");
            WaitForPf1Files([accountingSummary], 10_000, server, [botTask],
                $"PF5 {profile.Name} accounting completion");
            File.WriteAllText(botRelease, "release\n");
            string output = botTask.GetAwaiter().GetResult();
            if (!output.Contains("PF5 BOT HARNESS PASS", StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"PF5 {profile.Name} headless impairment failed:\n{Tail(output, 8_000)}");
            using JsonDocument botDocument = JsonDocument.Parse(File.ReadAllText(botSummary));
            JsonElement bot = botDocument.RootElement;
            ulong decompressed = bot.GetProperty("clientTelemetry").EnumerateArray().Aggregate(
                0UL, static (sum, client) => checked(sum + client.GetProperty("client")
                    .GetProperty("world").GetProperty("compressionCensus")
                    .GetProperty("messages").GetUInt64()));
            if (!bot.GetProperty("passed").GetBoolean() ||
                bot.GetProperty("schemaVersion").GetInt32() != 12 ||
                bot.GetProperty("endpointCount").GetInt32() != botCount ||
                bot.GetProperty("successfulReconnects").GetInt32() != 1 ||
                bot.GetProperty("freshProofLifetimes").GetInt32() != botCount + 1 ||
                bot.GetProperty("faultedBots").GetInt32() != 0 || decompressed == 0 ||
                bot.GetProperty("actionOutcomes").EnumerateArray().Any(static outcome =>
                    !outcome.GetProperty("conserved").GetBoolean() ||
                    !outcome.GetProperty("terminal").GetBoolean()))
                throw new InvalidOperationException(
                    $"PF5 {profile.Name} headless semantic/reconnect evidence was incomplete.");
            using JsonDocument accountingDocument = JsonDocument.Parse(
                File.ReadAllText(accountingSummary));
            JsonElement accounting = accountingDocument.RootElement;
            if (!accounting.GetProperty("PhysicalTransportReconciled").GetBoolean() ||
                !accounting.GetProperty("LogicalMessageReconciled").GetBoolean() ||
                accounting.GetProperty("ParticipantsAtStart").GetInt32() != botCount ||
                accounting.GetProperty("ConnectionLifetimes").GetInt32() < botCount + 1 ||
                accounting.GetProperty("TopologySamples").GetArrayLength() < 10 ||
                accounting.GetProperty("TopologySamplesSaturated").GetBoolean() ||
                accounting.GetProperty("RemoteReplication").GetProperty("SourceCommitGaps")
                    .GetUInt64() != 0)
                throw new InvalidOperationException(
                    $"PF5 {profile.Name} server accounting/topology did not reconcile.");
            long dropped = proxies.Sum(static proxy => proxy.DroppedDatagrams);
            long reordered = proxies.Sum(static proxy => proxy.ReorderedDatagrams);
            if (dropped == 0 || reordered == 0)
                throw new InvalidOperationException(
                    $"PF5 {profile.Name} did not exercise both loss and reorder.");

            File.WriteAllText(serverStop, "stop\n");
            if (!server.WaitForExit(30_000) || server.ExitCode != 0 ||
                !File.Exists(serverSummary))
                throw new InvalidOperationException(
                    $"PF5 {profile.Name} authority did not retire cleanly.");
            Console.WriteLine(
                $"PASS PF5_HeadlessImpairment profile={profile.Name} bots={botCount} " +
                $"dropPeriod={profile.DropPeriod} reorderPeriod={profile.ReorderPeriod} " +
                $"holdMs={profile.HoldMilliseconds} dropped={dropped} reordered={reordered} " +
                $"reconnects=1 decompressed={decompressed} topologySamples=" +
                $"{accounting.GetProperty("TopologySamples").GetArrayLength()}");
        }
        finally
        {
            Directory.CreateDirectory(scope);
            foreach (string marker in new[]
                     { startRelease, botRelease, accountingStop, serverStop })
                if (!File.Exists(marker)) File.WriteAllText(marker, "release\n");
            if (botTask is not null)
            {
                try { _ = botTask.Wait(15_000); }
                catch (AggregateException) { }
            }
            if (server is not null)
            {
                StopOwnedProcess(server);
                server.Dispose();
            }
            foreach (ExternalUdpImpairmentProxy proxy in proxies) proxy.Dispose();
            if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
        }
    }
}

static void RequireNativeTelemetry(
    string path,
    string expectedCheckpoint = "PF2.5",
    int measurementSeconds = 0,
    string expectedRenderMode = "headless")
{
    if (!File.Exists(path))
        throw new InvalidOperationException("An exported Online client omitted its PF2.5 telemetry summary.");
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    JsonElement native = root.GetProperty("Native");
    JsonElement client = root.GetProperty("Client");
    JsonElement network = root.GetProperty("Network");
    bool healthy = root.GetProperty("Healthy").GetBoolean();
    string? checkpoint = root.GetProperty("Checkpoint").GetString();
    int expectedSchema = expectedCheckpoint switch
    {
        "PF3" => 2,
        "PF4A" => 3,
        "PF4B" => 4,
        "PF4C" => 5,
        "PF4D" => 6,
        "PF4E" => 7,
        "PF4F" => 8,
        "PF5" => 9,
        _ => 1,
    };
    byte nativeLevel = native.GetProperty("Level").GetByte();
    ulong frames = native.GetProperty("RenderFrames").GetUInt64();
    ulong commands = native.GetProperty("CommandsSubmitted").GetUInt64();
    ulong inputSamples = native.GetProperty("PhysicalInputSamples").GetUInt64();
    ulong semanticResolutions = native.GetProperty("SemanticInputResolutions").GetUInt64();
    ulong frameSamples = native.GetProperty("FrameDurations").GetProperty("Samples").GetUInt64();
    ulong presentationSamples = native.GetProperty("PresentationUpdates").GetProperty("Samples").GetUInt64();
    byte clientLevel = client.GetProperty("Level").GetByte();
    ulong receivedSamples = client.GetProperty("ProcessReceived").GetProperty("Count").GetUInt64();
    byte networkLevel = network.GetProperty("Level").GetByte();
    ulong pumpSamples = network.GetProperty("PumpDurations").GetProperty("Count").GetUInt64();
    bool displayIdentified = !string.IsNullOrWhiteSpace(
        native.GetProperty("DisplayBackend").GetString()) &&
        !string.IsNullOrWhiteSpace(native.GetProperty("VSyncMode").GetString()) &&
        !string.IsNullOrWhiteSpace(native.GetProperty("WindowMode").GetString());
    ulong wholeFrameSamples = native.GetProperty("FrameDurations")
        .GetProperty("Samples").GetUInt64();
    bool sustainedInput = expectedCheckpoint is "PF3" or "PF4E" or "PF4F" or "PF5";
    bool performanceApplied = !sustainedInput ||
        root.GetProperty("PerformanceProfile").GetProperty("State").GetByte() == 2;
    bool schedulerValid = !sustainedInput ||
        root.GetProperty("InputScheduler").GetProperty("SamplesObserved").GetUInt64() != 0 &&
        root.GetProperty("InputScheduler").GetProperty("CommandsSubmitted").GetUInt64() != 0 &&
        root.GetProperty("InputScheduler").GetProperty("CapacityRejections").GetUInt64() == 0 &&
        root.GetProperty("InputScheduler").GetProperty("PendingObligations").GetInt32() == 0 &&
        root.GetProperty("InputScheduler").GetProperty("EdgeObligationsGenerated").GetUInt64() ==
            root.GetProperty("InputScheduler").GetProperty("EdgeObligationsSubmitted").GetUInt64() &&
        root.GetProperty("InputScheduler").GetProperty("ReleaseOrStopGenerated").GetUInt64() ==
            root.GetProperty("InputScheduler").GetProperty("ReleaseOrStopSubmitted").GetUInt64() &&
        checked(root.GetProperty("InputScheduler").GetProperty("EdgeObligationsGenerated").GetUInt64() +
            root.GetProperty("InputScheduler").GetProperty("ReleaseOrStopGenerated").GetUInt64()) ==
        checked(root.GetProperty("InputScheduler").GetProperty("EdgeObligationsCompleted").GetUInt64() +
            root.GetProperty("InputScheduler").GetProperty("ReleaseOrStopCompleted").GetUInt64() +
            checked((ulong)root.GetProperty("InputScheduler")
                .GetProperty("SubmittedCompletionDebt").GetInt32())) &&
        root.GetProperty("InputScheduler").GetProperty("Policy")
            .GetProperty("RecommendedInputHz").GetInt32() == 30 &&
        root.GetProperty("InputScheduler").GetProperty("Policy")
            .GetProperty("MaximumInputHz").GetInt32() == 60;
    bool renderModeValid = !sustainedInput || expectedRenderMode == "headless"
        ? !sustainedInput || native.GetProperty("Headless").ValueKind == JsonValueKind.Null ||
            native.GetProperty("Headless").GetBoolean()
        : native.GetProperty("Headless").ValueKind == JsonValueKind.False;
    double measuredSeconds = Math.Max(1, measurementSeconds);
    double sampleRate = inputSamples / measuredSeconds;
    double submissionRate = commands / measuredSeconds;
    bool highProducer = expectedRenderMode is "headless" or
        "visible-vsync-off-unlimited" or "visible-vsync-off-120" or
        "visible-vsync-off-240";
    bool cadenceIndependent = !sustainedInput ||
        submissionRate <= 36 && submissionRate >= 15 &&
        sampleRate + 2 >= submissionRate &&
        (!highProducer || sampleRate > submissionRate * 1.25);
    bool aggregationValid = expectedCheckpoint is not ("PF4D" or "PF4E" or "PF4F" or "PF5") ||
        root.GetProperty("Protocol").GetProperty("UnreliableAggregation")
            .GetProperty("ConfiguredMaximumFramesPerDatagram").GetInt32() > 1 &&
        root.GetProperty("Protocol").GetProperty("UnreliableAggregation")
            .GetProperty("ReceivedDatagramsWithTwoOrMoreFrames").GetUInt64() > 0;
    bool compressionValid = expectedCheckpoint is not ("PF4F" or "PF5") ||
        client.GetProperty("World").GetProperty("CompressionCensus")
            .GetProperty("Messages").GetUInt64() != 0 &&
        client.GetProperty("World").GetProperty("CompressionCensus")
            .GetProperty("Malformed").GetUInt64() == 0 &&
        client.GetProperty("World").GetProperty("CompressionCensus")
            .GetProperty("CanonicalDecodedBytes").GetUInt64() >
        client.GetProperty("World").GetProperty("CompressionCensus")
            .GetProperty("CompressedRepresentationBytes").GetUInt64();
    if (!healthy || checkpoint != expectedCheckpoint ||
        root.GetProperty("SchemaVersion").GetInt32() != expectedSchema ||
        nativeLevel != 2 || frames == 0 ||
        commands == 0 || inputSamples == 0 || semanticResolutions == 0 ||
        frameSamples == 0 || presentationSamples == 0 ||
        clientLevel != 2 || receivedSamples == 0 || networkLevel != 2 || pumpSamples == 0 ||
        !displayIdentified || wholeFrameSamples == 0 || !performanceApplied || !schedulerValid ||
        !renderModeValid || !cadenceIndependent || !aggregationValid || !compressionValid)
        throw new InvalidOperationException(
            "An exported Online client did not expose the required PF2.5 native/client/Network " +
            $"timing: healthy={healthy} checkpoint={checkpoint} native={nativeLevel} " +
            $"frames={frames}/{frameSamples} input={inputSamples}/{semanticResolutions} " +
            $"commands={commands} presentation={presentationSamples} " +
            $"client={clientLevel}/{receivedSamples} network={networkLevel}/{pumpSamples} " +
            $"display={displayIdentified}/{renderModeValid} performance={performanceApplied} " +
            $"scheduler={schedulerValid} cadence={sampleRate:0.###}/{submissionRate:0.###}Hz " +
            $"aggregation={aggregationValid} compression={compressionValid}.");
}

static void VerifyPf3InputCadenceMatrix(string root, string configuration)
{
    Run(root, "dotnet",
    [
        "build", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ServerOnly",
    ]);
    Run(root, "dotnet",
    [
        "build", "Tools/NetworkBots/UltraSim.NetworkBots.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ClientOnly",
    ]);

    Pf3Case[] cases =
    [
        new("sample120-submit30-authority60", 120, 30, 60, 60, 30, "compliant", false),
        new("sample240-submit60-max75", 240, 60, 60, 75, 60, "compliant", false, 10),
        new("sample60-submit20-authority20", 60, 20, 20, 60, 20, "compliant", false),
        new("hard-limit60-max60", 120, 60, 60, 60, 30, "hard-limit", false, 10),
        new("burst-within", 120, 0, 60, 60, 30, "burst-within", false),
        new("burst-over", 120, 0, 60, 60, 30, "burst-over", true),
        new("noncompliant120-max30", 120, 0, 60, 30, 20, "over-limit", true),
        new("stale-session-replay", 60, 20, 30, 60, 20, "stale-session", false),
    ];
    foreach (Pf3Case item in cases)
    {
        string scope = Path.Combine(
            Path.GetTempPath(), "ultrasim-repository-verification",
            Environment.ProcessId.ToString(), "pf3-input", item.Name);
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
        Directory.CreateDirectory(scope);
        string stop = Path.Combine(scope, "server-stop");
        string serverSummary = Path.Combine(scope, "server-summary.json");
        string botSummary = Path.Combine(scope, "bot-summary.json");
        Process? server = null;
        try
        {
            server = StartNativeDeployment(
                root, configuration, Path.Combine(scope, "deployment"),
                Path.Combine(scope, "server-ready.json"),
                ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
                ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp),
                out NativeDeploymentReady ready,
                maximumParticipants: 2,
                admissionBurstPerAddress: 16,
                stopFile: stop,
                summaryFile: serverSummary,
                telemetryLevel: "Detailed",
                authorityHz: item.AuthorityHz,
                maximumInputHz: item.MaximumInputHz,
                recommendedInputHz: item.RecommendedInputHz,
                inputBurst: item.InputBurstCapacity,
                inputBurstWindowMilliseconds: item.InputBurstWindowMilliseconds);
            if (ready.SimulationFrequencyHz != item.AuthorityHz ||
                ready.MaximumInputHz != item.MaximumInputHz ||
                ready.RecommendedInputHz != item.RecommendedInputHz ||
                ready.ServerPerformanceProfileRevision == 0 ||
                string.IsNullOrWhiteSpace(ready.ServerPerformanceProfileHash))
                throw new InvalidOperationException(
                    $"PF3 deployment '{item.Name}' published the wrong performance profile.");

            var arguments = new List<string>
            {
                "run", "--project", "Tools/NetworkBots/UltraSim.NetworkBots.csproj",
                "-c", configuration, "-p:UltraSimNetworkFlavor=ClientOnly", "--no-build", "--",
                $"--endpoint={ready.Endpoint}", $"--fingerprint={ready.Fingerprint}",
                "--bots=2", "--seed=300301", "--duration=8s", "--warmup=1s",
                "--scenario=sustained-input", $"--input-sample-hz={item.SampleHz}",
                $"--input-mode={item.InputMode}",
                $"--cache={Path.Combine(scope, "cache")}",
                $"--summary-file={botSummary}", "--admission-stagger-ms=50",
                "--telemetry=Detailed",
            };
            if (item.SubmitHz != 0) arguments.Add($"--input-submit-hz={item.SubmitHz}");
            string output = RunCaptured(root, "dotnet", arguments, 180_000);
            if (!output.Contains("PF3 BOT HARNESS PASS", StringComparison.Ordinal) ||
                !File.Exists(botSummary))
                throw new InvalidOperationException(
                    $"PF3 input case '{item.Name}' failed:\n{Tail(output, 8_000)}");
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(botSummary));
            JsonElement value = document.RootElement;
            ulong rateLimited = value.GetProperty("serverRateLimitedCommandsObserved").GetUInt64();
            if (!value.GetProperty("passed").GetBoolean() ||
                value.GetProperty("schemaVersion").GetInt32() != 5 ||
                value.GetProperty("checkpoint").GetString() != "PF3" ||
                value.GetProperty("freshProofLifetimes").GetInt32() !=
                    (item.InputMode == "stale-session" ? 4 : 2) ||
                !value.GetProperty("cleanRetirement").GetBoolean())
                throw new InvalidOperationException(
                    $"PF3 input case '{item.Name}' did not preserve session/proof retirement.");
            if (item.ExpectRateLimit)
            {
                if (rateLimited == 0)
                    throw new InvalidOperationException(
                        $"PF3 input case '{item.Name}' did not observe server rate limiting.");
            }
            else if (item.InputMode == "stale-session")
            {
                if (value.GetProperty("successfulReconnects").GetInt32() != 2 ||
                    value.GetProperty("staleSessionReplayAttempts").GetInt32() != 2 ||
                    value.GetProperty("staleSessionReplaysQueued").GetInt32() != 2 ||
                    value.GetProperty("staleSessionReplaysFenced").GetInt32() != 2 ||
                    rateLimited != 0)
                    throw new InvalidOperationException(
                        "PF3 stale-session commands were not fenced across fresh application lifetimes.");
            }
            else if (item.InputMode is "compliant" or "hard-limit")
            {
                ulong samples = value.GetProperty("schedulerSamplesObserved").GetUInt64();
                ulong submitted = value.GetProperty("schedulerCommandsSubmitted").GetUInt64();
                double sampleRate = samples / 16d;
                double submitRate = submitted / 16d;
                if (sampleRate < item.SampleHz * .65 ||
                    submitRate < item.SubmitHz * .60 || submitRate > item.SubmitHz * 1.20 ||
                    rateLimited != 0 ||
                    value.GetProperty("schedulerContinuousCoalesced").GetUInt64() == 0 ||
                    value.GetProperty("schedulerActionEdgesGenerated").GetUInt64() !=
                        value.GetProperty("schedulerActionEdgesCompleted").GetUInt64() ||
                    value.GetProperty("schedulerReleaseOrStopGenerated").GetUInt64() !=
                        value.GetProperty("schedulerReleaseOrStopCompleted").GetUInt64())
                    throw new InvalidOperationException(
                        $"PF3 input case '{item.Name}' violated sample/submit/edge policy: " +
                        $"sample={sampleRate:0.###}Hz/client submit={submitRate:0.###}Hz/client " +
                        $"rateLimited={rateLimited}.");
            }
            else if (rateLimited != 0 ||
                     value.GetProperty("commandsQueued").GetInt32() == 0)
                throw new InvalidOperationException(
                    $"PF3 input case '{item.Name}' rejected an admitted bounded burst.");

            File.WriteAllText(stop, "stop\n");
            if (!server.WaitForExit(30_000) || server.ExitCode != 0 ||
                !File.Exists(serverSummary))
                throw new InvalidOperationException(
                    $"PF3 deployment '{item.Name}' did not retire cleanly.");
            Console.WriteLine(
                $"PASS PF3_InputCase name={item.Name} sample={item.SampleHz}Hz/client " +
                $"submit={item.SubmitHz}Hz/client authority={item.AuthorityHz}Hz " +
                $"rateLimited={rateLimited}");
        }
        finally
        {
            Directory.CreateDirectory(scope);
            if (!File.Exists(stop)) File.WriteAllText(stop, "stop\n");
            if (server is not null)
            {
                StopOwnedProcess(server);
                server.Dispose();
            }
            if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
        }
    }
}

static void VerifyPf1MixedExportedSwarm(
    string root,
    string configuration,
    string onlineExecutable)
{
    const int botCount = 8;
    const int nativeClientCount = 2;
    int participantCapacity = botCount + nativeClientCount;
    int expectedPresentedActors = participantCapacity + 1; // Woodland animal.
    const ulong seed = 104_729;
    const string scenario = "mixed-chaos";
    const string expectedProfile =
        "grammar=ThirdPersonDirect camera=Perspective/Free/Free " +
        "distance=8.5 canonicalFov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT";

    VerifyPf1BotArtifact(root, configuration);

    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "pf1-mixed-swarm");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);

    string serverStop = Path.Combine(scope, "server-stop");
    string serverSummary = Path.Combine(scope, "server-summary.json");
    string botStartReady = Path.Combine(scope, "bots-start-ready");
    string botStartRelease = Path.Combine(scope, "bots-start-release");
    string botReady = Path.Combine(scope, "bots-ready");
    string botRelease = Path.Combine(scope, "bots-release");
    string botSummary = Path.Combine(scope, "bots-summary.json");
    string nativeRelease = Path.Combine(scope, "native-release");
    string aliceReady = Path.Combine(scope, "alice", "ready");
    string bobReady = Path.Combine(scope, "bob", "ready");
    string aliceComplete = Path.Combine(scope, "alice", "complete");
    string bobComplete = Path.Combine(scope, "bob", "complete");
    string nativeRetireRelease = Path.Combine(scope, "native-retire-release");

    Process? server = null;
    Task<string>? botTask = null;
    Task<string>? aliceTask = null;
    Task<string>? bobTask = null;
    try
    {
        server = StartNativeDeployment(
            root,
            configuration,
            Path.Combine(scope, "deployment"),
            Path.Combine(scope, "server-ready.json"),
            ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
            ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp),
            out NativeDeploymentReady ready,
            maximumParticipants: participantCapacity,
            admissionBurstPerAddress: 128,
            stopFile: serverStop,
            summaryFile: serverSummary);
        if (ready.MaximumParticipants != participantCapacity || ready.ServerGeneration == 0)
            throw new InvalidOperationException(
                "The PF1 authority did not publish its exact bounded capacity/generation.");

        botTask = Task.Run(() => RunCaptured(root, "dotnet",
        [
            "run", "--project", "Tools/NetworkBots/UltraSim.NetworkBots.csproj",
            "-c", configuration, "-p:UltraSimNetworkFlavor=ClientOnly", "--no-build", "--",
            $"--endpoint={ready.Endpoint}",
            $"--fingerprint={ready.Fingerprint}",
            $"--bots={botCount}",
            $"--seed={seed}",
            "--duration=14s",
            $"--scenario={scenario}",
            $"--cache={Path.Combine(scope, "bots")}",
            $"--start-ready-file={botStartReady}",
            $"--start-release-file={botStartRelease}",
            $"--ready-file={botReady}",
            $"--release-file={botRelease}",
            $"--summary-file={botSummary}",
            "--admission-stagger-ms=100",
        ], 210_000));
        aliceTask = Task.Run(() => RunCaptured(
            root, onlineExecutable,
            NativeSharedClientArguments(
                scope, "alice", ready.Endpoint, ready.Fingerprint,
                aliceReady, nativeRelease, expectedPresentedActors,
                "PF1 Native Alice", "x", aliceComplete, nativeRetireRelease), 210_000));
        bobTask = Task.Run(() => RunCaptured(
            root, onlineExecutable,
            NativeSharedClientArguments(
                scope, "bob", ready.Endpoint, ready.Fingerprint,
                bobReady, nativeRelease, expectedPresentedActors,
                "PF1 Native Bob", "y", bobComplete, nativeRetireRelease), 210_000));

        WaitForPf1Files(
            [botStartReady, aliceReady, bobReady],
            120_000,
            server,
            [botTask, aliceTask, bobTask],
            "all bots and both exported Online clients to share one active authority");
        File.WriteAllText(botStartRelease, "start" + Environment.NewLine);

        WaitForPf1Files(
            [botReady],
            90_000,
            server,
            [botTask, aliceTask, bobTask],
            "the deterministic mixed-chaos interval to finish");
        if (aliceTask.IsCompleted || bobTask.IsCompleted)
            throw new InvalidOperationException(
                "An exported Online client did not remain connected throughout the PF1 swarm interval.");

        File.WriteAllText(nativeRelease, "release" + Environment.NewLine);
        WaitForPf1Files(
            [aliceComplete, bobComplete],
            30_000,
            server,
            [aliceTask, bobTask],
            "both exported Online clients to observe post-churn actor convergence");
        if (aliceTask.IsCompleted || bobTask.IsCompleted)
            throw new InvalidOperationException(
                "An exported Online client retired before the PF1 post-churn barrier completed.");
        File.WriteAllText(nativeRetireRelease, "release" + Environment.NewLine);
        string aliceOutput = aliceTask.GetAwaiter().GetResult();
        string bobOutput = bobTask.GetAwaiter().GetResult();
        RequireNativeA08Result(
            aliceOutput, "cold", ready.StructuralManifestFingerprint,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: expectedProfile);
        RequireNativeA08Result(
            bobOutput, "cold", ready.StructuralManifestFingerprint,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: expectedProfile);
        if (!aliceOutput.Contains($"generation={ready.ServerGeneration}", StringComparison.Ordinal) ||
            !bobOutput.Contains($"generation={ready.ServerGeneration}", StringComparison.Ordinal) ||
            !aliceOutput.Contains("guest=PF1 Native Alice", StringComparison.Ordinal) ||
            !aliceOutput.Contains("body=HumanFemale", StringComparison.Ordinal) ||
            !bobOutput.Contains("guest=PF1 Native Bob", StringComparison.Ordinal) ||
            !bobOutput.Contains("body=HumanMale", StringComparison.Ordinal) ||
            !ContainsPositiveNamedCount(aliceOutput, "actors=") ||
            !ContainsPositiveNamedCount(bobOutput, "actors="))
            throw new InvalidOperationException(
                "The exported Online clients did not preserve PF1 generation/body/actor presentation.");

        File.WriteAllText(botRelease, "release" + Environment.NewLine);
        string botOutput = botTask.GetAwaiter().GetResult();
        if (!botOutput.Contains("PF1 BOT HARNESS PASS", StringComparison.Ordinal) ||
            !File.Exists(botSummary))
            throw new InvalidOperationException(
                $"The PF1 bot harness omitted its accepted summary:\n{Tail(botOutput, 8_000)}");
        using JsonDocument botDocument = JsonDocument.Parse(File.ReadAllText(botSummary));
        JsonElement bots = botDocument.RootElement;
        JsonElement botPhases = bots.GetProperty("phaseDurations");
        if (!bots.GetProperty("passed").GetBoolean() ||
            bots.GetProperty("botCount").GetInt32() != botCount ||
            bots.GetProperty("seed").GetUInt64() != seed ||
            bots.GetProperty("serverGeneration").GetUInt64() != ready.ServerGeneration ||
            bots.GetProperty("femaleBodies").GetInt32() != 4 ||
            bots.GetProperty("maleBodies").GetInt32() != 4 ||
            bots.GetProperty("activeHighWater").GetInt32() != botCount ||
            bots.GetProperty("reconnectingHighWater").GetInt32() < 1 ||
            bots.GetProperty("activeBots").GetInt32() != 0 ||
            bots.GetProperty("admittingBots").GetInt32() != 0 ||
            bots.GetProperty("reconnectingBots").GetInt32() != 0 ||
            bots.GetProperty("retiredBots").GetInt32() != botCount ||
            bots.GetProperty("successfulReconnects").GetInt32() < 1 ||
            bots.GetProperty("applicationSessions").GetInt32() < botCount + 1 ||
            bots.GetProperty("freshProofLifetimes").GetInt32() < botCount + 1 ||
            bots.GetProperty("schemaVersion").GetInt32() != 2 ||
            bots.GetProperty("commandsQueued").GetInt32() == 0 ||
            // Movement is latest-state unreliable input and becomes terminal at scheduler
            // submission.  The legacy authoritative-outcome counters may therefore omit the
            // final submitted sample when the acceptance window retires; PF4A's stage-conserving
            // action outcome is the exact contract for that source model.
            bots.GetProperty("actionOutcomes").EnumerateArray().Any(static outcome =>
                !outcome.GetProperty("conserved").GetBoolean() ||
                !outcome.GetProperty("terminal").GetBoolean() ||
                outcome.GetProperty("unexplained").GetUInt64() != 0) ||
            bots.GetProperty("jumpAttempts").GetInt32() !=
                bots.GetProperty("jumpsAccepted").GetInt32() +
                bots.GetProperty("jumpsRejected").GetInt32() +
                bots.GetProperty("jumpCancelled").GetInt32() ||
            bots.GetProperty("primaryAttempts").GetInt32() !=
                bots.GetProperty("primaryAccepted").GetInt32() +
                bots.GetProperty("primaryRejected").GetInt32() +
                bots.GetProperty("primaryCancelled").GetInt32() ||
            bots.GetProperty("interactionCommandsQueued").GetInt32() !=
                bots.GetProperty("interactionAccepted").GetInt32() +
                bots.GetProperty("interactionRejected").GetInt32() +
                bots.GetProperty("interactionCancelled").GetInt32() ||
            bots.GetProperty("uiOpenCommandsQueued").GetInt32() !=
                bots.GetProperty("uiOpenAccepted").GetInt32() +
                bots.GetProperty("uiOpenCancelled").GetInt32() ||
            bots.GetProperty("cancelledCommands").GetInt32() != 0 ||
            bots.GetProperty("chatObservationGaps").GetUInt64() != 0 ||
            bots.GetProperty("chunkCrossings").GetInt32() < 2 ||
            bots.GetProperty("distinctChunksVisited").GetInt32() < 2 ||
            bots.GetProperty("edgeJumpCommandsQueued").GetInt32() == 0 ||
            bots.GetProperty("reconnectIdentityObserversConverged").GetInt32() !=
            bots.GetProperty("reconnectIdentityObservers").GetInt32() ||
            bots.GetProperty("processMetricSamples").GetInt32() < 2 ||
            bots.GetProperty("pumpDurationHighWaterMilliseconds").GetDouble() < 0 ||
            botPhases.GetProperty("scenarioMilliseconds").GetInt64() < 14_000 ||
            botPhases.GetProperty("totalMilliseconds").GetInt64() <
                botPhases.GetProperty("scenarioMilliseconds").GetInt64() ||
            bots.GetProperty("landingRecoveries").GetInt32() == 0 ||
            bots.GetProperty("postRecoveryMovementAccepted").GetInt32() == 0 ||
            bots.GetProperty("nonsenseCommandsCreated").GetInt32() != 0 ||
            bots.GetProperty("faultedBots").GetInt32() != 0 ||
            !bots.GetProperty("cleanRetirement").GetBoolean())
            throw new InvalidOperationException(
                "The PF1 structured bot result violated its identity/proof/retirement contract.");

        File.WriteAllText(serverStop, "stop" + Environment.NewLine);
        if (!server.WaitForExit(30_000) || server.ExitCode != 0 || !File.Exists(serverSummary))
            throw new InvalidOperationException(
                "The PF1 dedicated authority did not publish a clean bounded shutdown.");
        using JsonDocument serverDocument = JsonDocument.Parse(File.ReadAllText(serverSummary));
        JsonElement authority = serverDocument.RootElement;
        ulong joins = authority.GetProperty("Joins").GetUInt64();
        ulong leaves = authority.GetProperty("Leaves").GetUInt64();
        if (authority.GetProperty("ServerGeneration").GetUInt64() != ready.ServerGeneration ||
            joins < (ulong)(participantCapacity + 1) || leaves != joins ||
            authority.GetProperty("Participants").GetInt32() != 0 ||
            authority.GetProperty("EstablishedSessions").GetInt32() != 0 ||
            !authority.GetProperty("CleanRetirement").GetBoolean())
            throw new InvalidOperationException(
                "The PF1 authority retained participant/admission capacity after swarm retirement.");

        Console.WriteLine(
            $"PASS PF1_MixedExportedClientSwarm generation={ready.ServerGeneration} " +
            $"bots={botCount} nativeClients={nativeClientCount} seed={seed} scenario={scenario} " +
            $"sessions={bots.GetProperty("applicationSessions").GetInt32()} " +
            $"proofs={bots.GetProperty("freshProofLifetimes").GetInt32()} " +
            $"reconnects={bots.GetProperty("successfulReconnects").GetInt32()} " +
            $"movement={bots.GetProperty("movementAccepted").GetInt32()} " +
            $"jumps={bots.GetProperty("jumpsAccepted").GetInt32()} " +
            $"chat={bots.GetProperty("localChatSubmitted").GetInt32()}+" +
            $"{bots.GetProperty("globalChatSubmitted").GetInt32()} " +
            $"sword={bots.GetProperty("swordPickupObserved").GetInt32()}/" +
            $"{bots.GetProperty("swordEquipObserved").GetInt32()}/" +
            $"{bots.GetProperty("swordDropObserved").GetInt32()} " +
            $"workingSet={bots.GetProperty("workingSetHighWaterBytes").GetInt64()} " +
            $"cpuMs={bots.GetProperty("cpuMilliseconds").GetDouble():0.###} " +
            $"joins={joins} leaves={leaves} nativeActors={expectedPresentedActors} clean=true");
    }
    finally
    {
        Directory.CreateDirectory(scope);
        foreach (string marker in new[]
                 { botStartRelease, nativeRelease, nativeRetireRelease, botRelease, serverStop })
            if (!File.Exists(marker)) File.WriteAllText(marker, "release" + Environment.NewLine);
        foreach (Task<string>? task in new[] { aliceTask, bobTask, botTask })
        {
            if (task is null) continue;
            try { _ = task.Wait(15_000); }
            catch (AggregateException) { }
        }
        if (server is not null)
        {
            StopOwnedProcess(server);
            server.Dispose();
        }
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

static void VerifyPf1BotArtifact(string root, string configuration)
{
    const string project = "Tools/NetworkBots/UltraSim.NetworkBots.csproj";
    Run(root, "dotnet",
    [
        "build", project, "-c", configuration,
        "-p:UltraSimNetworkFlavor=ClientOnly",
    ]);
    string output = Path.Combine(root, "Tools", "NetworkBots", "bin", configuration, "net8.0");
    string[] expected =
    [
        "UltraSim.Client.Content", "UltraSim.Client.Core", "UltraSim.Client.Network",
        "UltraSim.Client.StandardUi", "UltraSim.ECS", "UltraSim.Framework.Definitions",
        "UltraSim.Game.Accounting", "UltraSim.Game.Contracts",
        "UltraSim.Game.Presentation.Contracts", "UltraSim.Game.Protocol", "UltraSim.Game.World",
        "UltraSim.Network", "UltraSim.NetworkBots", "UltraSim.Spatial", "UltraSim.UI",
    ];
    string[] actual = Directory.EnumerateFiles(output, "UltraSim*.dll", SearchOption.TopDirectoryOnly)
        .Select(static path => Path.GetFileNameWithoutExtension(path))
        .OrderBy(static name => name, StringComparer.Ordinal)
        .ToArray();
    if (!actual.SequenceEqual(expected.OrderBy(static name => name, StringComparer.Ordinal),
            StringComparer.Ordinal))
        throw new InvalidOperationException(
            "The PF1 bot product has an unexpected managed dependency closure.\n" +
            $"Expected: {string.Join(", ", expected)}\nActual: {string.Join(", ", actual)}");

    using JsonDocument deps = JsonDocument.Parse(File.ReadAllText(
        Path.Combine(output, "UltraSim.NetworkBots.deps.json")));
    HashSet<string> dependencies = deps.RootElement.GetProperty("libraries").EnumerateObject()
        .Select(static property => property.Name.Split('/')[0])
        .ToHashSet(StringComparer.Ordinal);
    string[] forbidden =
    [
        "GodotSharp", "UltraSim.Client.Local", "UltraSim.Client.BuiltInTestWorlds",
        "UltraSim.Game.Composition", "UltraSim.Game.Persistence", "UltraSim.Game.Physics",
        "UltraSim.Game.Items.Contracts", "UltraSim.Game.Combat.Contracts", "UltraSim.Game.Skills.Contracts",
        "UltraSim.Game.Replication", "UltraSim.Game.Replication.Remote", "UltraSim.Game.UI",
        "UltraSim.Network.Testing", "UltraSim.Server.Hosting", "UltraSim.Server.Persistence",
        "UltraSim.Framework.Persistence",
        "UltraSim.Server.Runtime",
    ];
    string[] leaked = forbidden.Where(dependencies.Contains).ToArray();
    if (leaked.Length != 0)
        throw new InvalidOperationException(
            $"The PF1 bot product contains privileged/engine/testing dependencies: {string.Join(", ", leaked)}");
    HashSet<string> networkTypes = ReadDefinedTypeNames(Path.Combine(output, "UltraSim.Network.dll"));
    RequireType(networkTypes, "UltraSim.Network.HostedUdpProtocolClient", "PF1 bot product");
    RejectType(networkTypes, "UltraSim.Network.HostedUdpAdmissionHost", "PF1 bot product");
    RejectType(networkTypes, "UltraSim.Network.NetworkServerIdentity", "PF1 bot product");
    Console.WriteLine(
        $"PASS PF1_HeadlessBotProduct assemblies={actual.Length} network=ClientOnly " +
        "godot=absent local=absent authority=absent networkTesting=absent");
}

static void WaitForPf1Files(
    IReadOnlyList<string> paths,
    int timeoutMilliseconds,
    Process server,
    IReadOnlyList<Task<string>> clients,
    string purpose)
{
    long deadline = Environment.TickCount64 + timeoutMilliseconds;
    while (paths.Any(path => !File.Exists(path)) && Environment.TickCount64 < deadline)
    {
        if (server.HasExited)
            throw new InvalidOperationException(
                $"The PF1 dedicated authority exited while waiting for {purpose}.");
        Task<string>? failed = clients.FirstOrDefault(static task => task.IsFaulted);
        if (failed is not null)
            throw new InvalidOperationException(
                $"A PF1 client failed while waiting for {purpose}.", failed.Exception);
        Thread.Sleep(10);
    }
    if (paths.Any(path => !File.Exists(path)))
        throw new TimeoutException($"PF1 timed out waiting for {purpose}.");
}

static void VerifyMixedSessionLifecycleNative(
    string root,
    string configuration,
    string godot,
    string? exportedComplete = null,
    bool rendered = false)
{
    string scope = Path.Combine(
        Path.GetTempPath(), "ultrasim-repository-verification",
        Environment.ProcessId.ToString(), "f0.7-mixed-session");
    if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    Directory.CreateDirectory(scope);
    Process? serverA = null;
    Process? serverB = null;
    try
    {
        serverA = StartNativeDeployment(
            root,
            configuration,
            Path.Combine(scope, "deployment-a"),
            Path.Combine(scope, "deployment-a-ready.json"),
            ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
            ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp),
            out NativeDeploymentReady readyA);
        serverB = StartNativeDeployment(
            root,
            configuration,
            Path.Combine(scope, "deployment-b"),
            Path.Combine(scope, "deployment-b-ready.json"),
            ReserveLoopbackPort(SocketType.Dgram, ProtocolType.Udp),
            ReserveLoopbackPort(SocketType.Stream, ProtocolType.Tcp),
            out NativeDeploymentReady readyB);
        if (readyA.Realm == readyB.Realm || readyA.Fingerprint == readyB.Fingerprint ||
            readyA.CatalogFingerprint == readyB.CatalogFingerprint ||
            readyA.WorldPackFingerprint != readyB.WorldPackFingerprint ||
            readyA.ArchiveBytes != readyB.ArchiveBytes ||
            readyA.ActorPresentationBytes != readyB.ActorPresentationBytes ||
            readyA.WorldChunks != readyB.WorldChunks ||
            readyA.ClientProfileRevision != readyB.ClientProfileRevision ||
            readyA.ClientProfileHash != readyB.ClientProfileHash)
            throw new InvalidOperationException(
                "F0.7 requires distinct realm-bound deployment/catalog identities over the same " +
                "World, actor, and client-profile payload contract.");

        string f10Evidence = Path.Combine(scope, "f1.0-client-baseline.json");
        string f112Evidence = Path.Combine(root, ".output", "f1.12-verification",
            Environment.ProcessId.ToString(), "Complete-mixed.json");
        Directory.CreateDirectory(Path.GetDirectoryName(f112Evidence)!);
        string f112Head = RunCaptured(root, "git", ["rev-parse", "HEAD"], 5_000).Trim();
        string output = RunCaptured(root, exportedComplete ?? godot,
        (exportedComplete is null ? new[] { "--headless", "--path", "Client/Godot", "--" }
            : rendered ? new[] { "--" } : new[] { "--headless", "--" }).Concat(
        [
            "--f0-7-smoke",
            "--f1-12-mixed",
            rendered ? "--f1-12-rendered-mixed" : "--f1-12-headless-mixed",
            $"--f1-12-head={f112Head}",
            $"--f1-12-mixed-evidence={f112Evidence}",
            "--expect-edition=Complete",
            $"--f0-7-a-endpoint={readyA.Endpoint}",
            $"--f0-7-a-fingerprint={readyA.Fingerprint}",
            $"--f0-7-b-endpoint={readyB.Endpoint}",
            $"--f0-7-b-fingerprint={readyB.Fingerprint}",
            $"--a0-8-cache={Path.Combine(scope, "content-cache")}",
            $"--a0-8-trust={Path.Combine(scope, "client", "trusted-servers.json")}",
            $"--f0-3c-profile-decisions={Path.Combine(scope, "client", "server-profiles.json")}",
            $"--f1-0-evidence={f10Evidence}",
        ]), 300_000);
        if (!output.Contains("F1.12 MIXED PRODUCT PASS lifetimes=8", StringComparison.Ordinal))
            throw new InvalidOperationException("Extended same-process mixed lifetime control did not complete.");
        if (!output.Contains("F0.7 MIXED SESSION LIFECYCLE PASS", StringComparison.Ordinal) ||
            !output.Contains("F1.0 CLIENT BASELINE PASS", StringComparison.Ordinal) ||
            !output.Contains($"realms={readyA.Realm}/{readyB.Realm}/{readyA.Realm}",
                StringComparison.Ordinal) ||
            !output.Contains("profile_reuse=False/False/True", StringComparison.Ordinal) ||
            !output.Contains("local_surfaces=1/1/1", StringComparison.Ordinal) ||
            !output.Contains("remote_surfaces=1/1/1", StringComparison.Ordinal) ||
            !output.Contains("boundaries=clean", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The F0.7 native lifecycle evidence was incomplete:\n{Tail(output, 8_000)}");
        VerifyF10ClientBaselineEvidence(f10Evidence);
        string? retainedF10Evidence =
            Environment.GetEnvironmentVariable("ULTRASIM_F10_EVIDENCE_OUTPUT");
        if (!string.IsNullOrWhiteSpace(retainedF10Evidence))
        {
            string full = Path.GetFullPath(retainedF10Evidence);
            string? directory = Path.GetDirectoryName(full);
            if (string.IsNullOrWhiteSpace(directory))
                throw new InvalidOperationException(
                    "ULTRASIM_F10_EVIDENCE_OUTPUT has no parent directory.");
            Directory.CreateDirectory(directory);
            File.Copy(f10Evidence, full, overwrite: true);
        }
        Console.WriteLine(
            $"PASS F0.7_MixedSessionLifecycleNative realms={readyA.Realm}/{readyB.Realm} " +
            "sequence=local-local-remoteA-remoteB-remoteA-local profileReuse=exact " +
            "sessionResidue=none");
    }
    finally
    {
        if (serverB is not null) StopOwnedProcess(serverB);
        if (serverA is not null) StopOwnedProcess(serverA);
        serverB?.Dispose();
        serverA?.Dispose();
        if (Directory.Exists(scope)) Directory.Delete(scope, recursive: true);
    }
}

static void VerifyF10ClientBaselineEvidence(string path)
{
    if (!File.Exists(path))
        throw new InvalidOperationException("The F1.0 native baseline did not write its evidence file.");
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
    JsonElement root = document.RootElement;
    if (root.GetProperty("SchemaVersion").GetInt32() != 1 ||
        root.GetProperty("Checkpoint").GetString() != "F1.0" ||
        root.GetProperty("Product").GetString() != "Complete" ||
        root.GetProperty("RetirementBoundariesChecked").GetInt32() != 6)
        throw new InvalidOperationException("The F1.0 native baseline identity was incomplete.");

    JsonElement persistent = root.GetProperty("PersistentBaseline");
    JsonElement local = root.GetProperty("Local").GetProperty("First");
    JsonElement localSnapshot = local.GetProperty("Snapshot");
    JsonElement localRenderer = localSnapshot.GetProperty("Renderer");
    JsonElement localActors = localSnapshot.GetProperty("Actors");
    JsonElement localUi = localSnapshot.GetProperty("Ui");
    JsonElement localBoundary = local.GetProperty("ActiveBoundary");
    if (localSnapshot.GetProperty("SemanticWorld").GetProperty("ResidentChunks").GetInt32() <= 0 ||
        localRenderer.GetProperty("ActiveRenderChunks").GetInt32() <= 0 ||
        localRenderer.GetProperty("ActiveNodes").GetInt32() <= 0 ||
        localRenderer.GetProperty("ActiveResources").GetInt32() <= 0 ||
        localRenderer.GetProperty("ActiveTerrainSurfaceMeshes").GetInt32() <= 0 ||
        localRenderer.GetProperty("ActiveStaticBatchGroups").GetInt32() <= 0 ||
        localRenderer.GetProperty("ActivePublicBatchGroups").GetInt32() <= 0 ||
        localActors.GetProperty("ActiveActors").GetInt32() < 2 ||
        localUi.GetProperty("OpenSemanticViews").GetInt32() != 0 ||
        localBoundary.GetProperty("WorldUiChildren").GetInt32() !=
            persistent.GetProperty("WorldUiChildren").GetInt32() ||
        localBoundary.GetProperty("HudChildren").GetInt32() !=
            persistent.GetProperty("HudChildren").GetInt32() ||
        localBoundary.GetProperty("WorldHostChildren").GetInt32() !=
            persistent.GetProperty("WorldHostChildren").GetInt32() + 1 ||
        localBoundary.GetProperty("HudHostChildren").GetInt32() !=
            persistent.GetProperty("HudHostChildren").GetInt32() + 1 ||
        !localUi.GetProperty("HudOwnedBySharedHud").GetBoolean() ||
        !localUi.GetProperty("WorldOwnedBySharedWorld").GetBoolean() ||
        !localUi.GetProperty("GumpsOwnedBySharedManager").GetBoolean() ||
        !localUi.GetProperty("BannerOwnedByHud").GetBoolean() ||
        !localUi.GetProperty("TooltipOwnedByContext").GetBoolean())
        throw new InvalidOperationException(
            "The F1.0 local semantic/native/UI ownership census was incomplete.");

    JsonElement remote = root.GetProperty("Remote");
    JsonElement cold = remote.GetProperty("ColdRealmA");
    JsonElement coldSnapshot = cold.GetProperty("Snapshot");
    JsonElement coldAcquisition = coldSnapshot.GetProperty("Session")
        .GetProperty("Content").GetProperty("Acquisition");
    JsonElement coldCacheAtOpen = coldSnapshot.GetProperty("ContentCacheAtOpen");
    JsonElement coldCurrentCache = coldSnapshot.GetProperty("ContentCacheCurrent");
    JsonElement warm = remote.GetProperty("WarmRealmAReconnect");
    JsonElement warmSnapshot = warm.GetProperty("Snapshot");
    JsonElement warmAcquisition = warmSnapshot.GetProperty("Session")
        .GetProperty("Content").GetProperty("Acquisition");
    // The current Local product has two independently authorized world.local packages before the
    // first Remote session: Official actor presentations and the F1.6 Cabin Texture/Material
    // presentation package. Their five distinct source blobs are persistent cache state, while all
    // live pins must still retire before Remote opens.
    if (coldCacheAtOpen.GetProperty("BlobCount").GetInt32() != 5 ||
        coldCacheAtOpen.GetProperty("LocalManifestCount").GetInt32() != 2 ||
        coldCacheAtOpen.GetProperty("RealmManifestCount").GetInt32() != 0 ||
        coldCacheAtOpen.GetProperty("LocalActivationCount").GetInt32() != 2 ||
        coldCacheAtOpen.GetProperty("RealmActivationCount").GetInt32() != 0 ||
        coldCacheAtOpen.GetProperty("LiveBlobPins").GetInt32() != 0 ||
        coldCacheAtOpen.GetProperty("LiveCatalogPins").GetInt32() != 0 ||
        coldAcquisition.GetProperty("PackagesFetched").GetInt32() <= 0 ||
        coldAcquisition.GetProperty("BytesFetched").GetInt64() <= 0 ||
        coldCurrentCache.GetProperty("BlobCount").GetInt32() <= 0 ||
        coldCurrentCache.GetProperty("LiveBlobPins").GetInt32() <= 0 ||
        coldSnapshot.GetProperty("ActorResources").GetProperty("RealizedResources").GetInt32() <= 0 ||
        coldSnapshot.GetProperty("ActorResources").GetProperty("RetainedArtifactBytes").GetInt64() <= 0 ||
        cold.GetProperty("ActiveBoundary").GetProperty("HudChildren").GetInt32() !=
            persistent.GetProperty("HudChildren").GetInt32() ||
        cold.GetProperty("ActiveBoundary").GetProperty("HudHostChildren").GetInt32() !=
            persistent.GetProperty("HudHostChildren").GetInt32() + 1 ||
        !coldSnapshot.GetProperty("UiOwnership").GetProperty(
            "HudOwnedBySharedHud").GetBoolean() ||
        !coldSnapshot.GetProperty("UiOwnership").GetProperty(
            "GumpsOwnedBySharedManager").GetBoolean() ||
        !coldSnapshot.GetProperty("UiOwnership").GetProperty(
            "BannerOwnedByHud").GetBoolean() ||
        !coldSnapshot.GetProperty("UiOwnership").GetProperty(
            "TooltipOwnedByContext").GetBoolean() ||
        warmSnapshot.GetProperty("ContentCacheAtOpen").GetProperty("BlobCount").GetInt32() <= 0 ||
        warmAcquisition.GetProperty("PackagesReused").GetInt32() <= 0 ||
        warmAcquisition.GetProperty("BytesFetched").GetInt64() != 0 ||
        warm.GetProperty("ApplicationSessionEpoch").GetUInt64() ==
            cold.GetProperty("ApplicationSessionEpoch").GetUInt64())
        throw new InvalidOperationException(
            "The F1.0 remote cold/warm content, native-resource, or lineage census was incomplete.");

    JsonElement finalBoundary = root.GetProperty("FinalBoundary");
    if (finalBoundary.GetProperty("ControllerChildren").GetInt32() !=
            persistent.GetProperty("ControllerChildren").GetInt32() ||
        finalBoundary.GetProperty("ContentCatalogChildren").GetInt32() !=
            persistent.GetProperty("ContentCatalogChildren").GetInt32() ||
        finalBoundary.GetProperty("WorldChildren").GetInt32() !=
            persistent.GetProperty("WorldChildren").GetInt32() ||
        finalBoundary.GetProperty("WorldUiChildren").GetInt32() !=
            persistent.GetProperty("WorldUiChildren").GetInt32() ||
        finalBoundary.GetProperty("HudChildren").GetInt32() !=
            persistent.GetProperty("HudChildren").GetInt32() ||
        finalBoundary.GetProperty("MenuChildren").GetInt32() !=
            persistent.GetProperty("MenuChildren").GetInt32() ||
        finalBoundary.GetProperty("DebugChildren").GetInt32() !=
            persistent.GetProperty("DebugChildren").GetInt32() ||
        finalBoundary.GetProperty("ContextChildren").GetInt32() !=
            persistent.GetProperty("ContextChildren").GetInt32() ||
        finalBoundary.GetProperty("WorldHostChildren").GetInt32() !=
            persistent.GetProperty("WorldHostChildren").GetInt32() ||
        finalBoundary.GetProperty("HudHostChildren").GetInt32() !=
            persistent.GetProperty("HudHostChildren").GetInt32() ||
        finalBoundary.GetProperty("GameplayGumpHostChildren").GetInt32() !=
            persistent.GetProperty("GameplayGumpHostChildren").GetInt32() ||
        finalBoundary.GetProperty("ContextHostChildren").GetInt32() !=
            persistent.GetProperty("ContextHostChildren").GetInt32() ||
        finalBoundary.GetProperty("OpenGumpWindows").GetInt32() != 0 ||
        finalBoundary.GetProperty("GumpPointerCaptured").GetBoolean() ||
        finalBoundary.GetProperty("DebugPage").GetInt32() != 0 ||
        finalBoundary.GetProperty("ServerProfileActive").GetBoolean())
        throw new InvalidOperationException(
            "The F1.0 final persistent-root/session-profile boundary was not clean.");

    JsonElement ownership = root.GetProperty("CurrentF17UiOwnership");
    if (!ownership.GetProperty("LocalGameplayUiUsesSharedGumpHost").GetBoolean() ||
        !ownership.GetProperty("LocalTooltipUsesContextHost").GetBoolean() ||
        !ownership.GetProperty("RemoteGameplayUiUsesSharedGumpHost").GetBoolean() ||
        !ownership.GetProperty("RemoteTooltipUsesContextHost").GetBoolean() ||
        !ownership.GetProperty("ContextTooltipLayerPresent").GetBoolean() ||
        !ownership.GetProperty("MultiWindowManagerPresent").GetBoolean())
        throw new InvalidOperationException(
            "The F1.0 semantic workload did not preserve the F1.7 UI ownership contract.");
    Console.WriteLine(
        $"PASS F1.0_ClientBaselineGuardrails localChunks=" +
        $"{localSnapshot.GetProperty("SemanticWorld").GetProperty("ResidentChunks").GetInt32()} " +
        $"remoteColdBytes={coldAcquisition.GetProperty("BytesFetched").GetInt64()} " +
        $"remoteWarmPackages={warmAcquisition.GetProperty("PackagesReused").GetInt32()} " +
        "retirementBoundaries=6");
}

static string[] NativeSharedClientArguments(
    string scope,
    string name,
    string endpoint,
    string fingerprint,
    string ready,
    string release,
    int expectedActors = 4,
    string? guestName = null,
    string? guestBody = null,
    string? completionReady = null,
    string? retirementRelease = null,
    string? telemetry = null,
    string? telemetryStart = null,
    string? telemetryReady = null,
    string? telemetrySummary = null,
    string? telemetryCheckpoint = null,
    string renderMode = "headless")
{
    var arguments = new List<string>();
    switch (renderMode)
    {
        case "headless":
            arguments.Add("--headless");
            break;
        case "visible-default":
            break;
        case "visible-vsync-off-unlimited":
            arguments.Add("--disable-vsync");
            arguments.Add("--max-fps");
            arguments.Add("0");
            break;
        case "visible-vsync-off-30":
        case "visible-vsync-off-60":
        case "visible-vsync-off-120":
        case "visible-vsync-off-240":
            arguments.Add("--disable-vsync");
            arguments.Add("--max-fps");
            arguments.Add(renderMode["visible-vsync-off-".Length..]);
            break;
        case "visible-vsync-on-60":
            arguments.Add("--max-fps");
            arguments.Add("60");
            break;
        default:
            throw new ArgumentException($"Unknown native render mode '{renderMode}'.");
    }
    arguments.Add("--");
    arguments.AddRange(
    [
        "--a0-8-smoke",
        $"--a0-8-endpoint={endpoint}",
        $"--a0-8-fingerprint={fingerprint}",
        $"--a0-8-cache={Path.Combine(scope, name, "content-cache")}",
        $"--a0-8-trust={Path.Combine(scope, name, "trusted-servers.json")}",
        $"--f0-3c-profile-decisions={Path.Combine(scope, name, "server-profiles.json")}",
        "--a0-8-allow-private-content",
        "--a0-8-expect=cold",
        $"--m0-0-expect-actors={expectedActors}",
        $"--m0-0-client-ready={ready}",
        $"--m0-0-client-release={release}",
        "--expect-edition=Online",
    ]);
    if (guestName is not null) arguments.Add($"--a0-8-guest-name={guestName}");
    if (guestBody is not null) arguments.Add($"--a0-8-guest-body={guestBody}");
    if (completionReady is not null)
        arguments.Add($"--pf1-client-complete-ready={completionReady}");
    if (retirementRelease is not null)
        arguments.Add($"--pf1-client-retire-release={retirementRelease}");
    if (telemetry is not null) arguments.Add($"--telemetry={telemetry}");
    if (telemetryStart is not null)
        arguments.Add($"--telemetry-start-file={telemetryStart}");
    if (telemetryReady is not null)
        arguments.Add($"--telemetry-ready-file={telemetryReady}");
    if (telemetrySummary is not null)
        arguments.Add($"--telemetry-summary={telemetrySummary}");
    if (telemetryCheckpoint is not null)
        arguments.Add($"--telemetry-checkpoint={telemetryCheckpoint}");
    if (telemetryCheckpoint is "PF3" or "PF4E" or "PF4F" or "PF5")
        arguments.Add("--pf3-native-sustained-input");
    return arguments.ToArray();
}

static Process StartNativeDeployment(
    string root,
    string configuration,
    string identityDirectory,
    string readyFile,
    int applicationPort,
    int contentPort,
    out NativeDeploymentReady ready,
    string clientProfile = "third-person",
    int maximumParticipants = 8,
    int admissionBurstPerAddress = 4,
    string? stopFile = null,
    string? summaryFile = null,
    string? accountingStartFile = null,
    string? accountingReadyFile = null,
    string? accountingStopFile = null,
    string? accountingSummaryFile = null,
    string telemetryLevel = "Off",
    int authorityHz = 60,
    int maximumInputHz = 60,
    int recommendedInputHz = 30,
    int inputBurst = 10,
    int inputBurstWindowMilliseconds = 100,
    int networkServiceDelayMilliseconds = 1,
    string? nativeGateTriggerFile = null)
{
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = root,
        UseShellExecute = false,
    };
    foreach (string argument in new[]
    {
        "run", "--project", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration, "-p:UltraSimNetworkFlavor=ServerOnly", "--no-build", "--",
        $"--bind=127.0.0.1:{applicationPort}",
        $"--content-bind=127.0.0.1:{contentPort}",
        $"--identity-dir={identityDirectory}",
        $"--ready-file={readyFile}",
        $"--client-profile={clientProfile}",
        $"--maximum-participants={maximumParticipants}",
        $"--same-address-admission-burst={admissionBurstPerAddress}",
        $"--authority-hz={authorityHz}",
        $"--maximum-input-hz={maximumInputHz}",
        $"--recommended-input-hz={recommendedInputHz}",
        $"--input-burst={inputBurst}",
        $"--input-burst-window-ms={inputBurstWindowMilliseconds}",
        $"--network-service-delay-ms={networkServiceDelayMilliseconds}",
    }) start.ArgumentList.Add(argument);
    if (stopFile is not null) start.ArgumentList.Add($"--stop-file={stopFile}");
    if (nativeGateTriggerFile is not null)
        start.ArgumentList.Add($"--w7-native-gate-trigger-file={nativeGateTriggerFile}");
    if (summaryFile is not null) start.ArgumentList.Add($"--summary-file={summaryFile}");
    if (accountingStartFile is not null)
        start.ArgumentList.Add($"--accounting-start-file={accountingStartFile}");
    if (accountingReadyFile is not null)
        start.ArgumentList.Add($"--accounting-ready-file={accountingReadyFile}");
    if (accountingStopFile is not null)
        start.ArgumentList.Add($"--accounting-stop-file={accountingStopFile}");
    if (accountingSummaryFile is not null)
        start.ArgumentList.Add($"--accounting-summary-file={accountingSummaryFile}");
    start.ArgumentList.Add($"--telemetry={telemetryLevel}");
    Console.WriteLine($"> dotnet {string.Join(' ', start.ArgumentList.Select(Quote))}");
    Process process = Process.Start(start) ??
        throw new InvalidOperationException("Unable to start the A0.8 Direct Connect deployment.");
    try
    {
        long deadline = Environment.TickCount64 + 20_000;
        while (!File.Exists(readyFile) && Environment.TickCount64 < deadline)
        {
            if (process.HasExited)
                throw new InvalidOperationException(
                    $"The A0.8 deployment exited before readiness with code {process.ExitCode}.");
            Thread.Sleep(10);
        }
        if (!File.Exists(readyFile))
            throw new TimeoutException("The A0.8 deployment did not publish readiness in 20 seconds.");
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(readyFile));
        JsonElement value = document.RootElement;
        ready = new NativeDeploymentReady(
            RequiredString(value, "Endpoint"),
            RequiredString(value, "Realm"),
            RequiredString(value, "Fingerprint"),
            RequiredString(value, "CatalogFingerprint"),
            RequiredString(value, "WorldPackFingerprint"),
            value.GetProperty("ArchiveBytes").GetInt64(),
            value.GetProperty("ActorPresentationBytes").GetInt64(),
            value.GetProperty("WorldChunks").GetInt32(),
            RequiredString(value, "ClientProfile"),
            value.GetProperty("ClientProfileRevision").GetUInt64(),
            RequiredString(value, "ClientProfileHash"),
            value.GetProperty("ServerPerformanceProfileRevision").GetUInt64(),
            RequiredString(value, "ServerPerformanceProfileHash"),
            value.GetProperty("SimulationFrequencyHz").GetInt32(),
            value.GetProperty("MaximumNetworkServiceIntervalMicroseconds").GetInt32(),
            value.GetProperty("MaximumInputHz").GetInt32(),
            value.GetProperty("RecommendedInputHz").GetInt32(),
            value.GetProperty("InputBurstCapacity").GetInt32(),
            value.GetProperty("InputBurstWindowMilliseconds").GetInt32(),
            value.GetProperty("MaximumParticipants").GetInt32(),
            value.GetProperty("ServerGeneration").GetUInt64(),
            RequiredString(value, "StructuralManifestFingerprint"));
        return process;
    }
    catch
    {
        StopOwnedProcess(process);
        process.Dispose();
        throw;
    }
}

static string RequiredString(JsonElement parent, string property)
{
    string? value = parent.GetProperty(property).GetString();
    return string.IsNullOrWhiteSpace(value)
        ? throw new InvalidOperationException($"A0.8 readiness omitted '{property}'.")
        : value;
}

static void RequireNativeA08Result(
    string output,
    string mode,
    string expectedManifestFingerprint,
    bool expectedWarm,
    bool expectedRememberedProfile,
    string expectedProfile,
    bool requireManifestFingerprint = true)
{
    string pass = $"A0.8 CONTENTLESS ONLINE PASS mode={mode}";
    if (!output.Contains(pass, StringComparison.Ordinal) ||
        !output.Contains("F0.3c CLIENT PROFILE APPLIED", StringComparison.Ordinal) ||
        !output.Contains(expectedRememberedProfile ? "remembered=True" : "remembered=False", StringComparison.Ordinal) ||
        !output.Contains(expectedProfile, StringComparison.Ordinal) ||
        requireManifestFingerprint &&
            !output.Contains($"worldManifest={expectedManifestFingerprint}", StringComparison.Ordinal) ||
        !output.Contains("manifestEntries=2 preparedPacks=1 semanticChunks=25", StringComparison.Ordinal) ||
        expectedWarm && !output.Contains("packBytesFetched=0", StringComparison.Ordinal) ||
        !expectedWarm && output.Contains("packBytesFetched=0", StringComparison.Ordinal) ||
        !output.Contains("chunks=9", StringComparison.Ordinal) ||
        !output.Contains("proofs=1", StringComparison.Ordinal) ||
        !output.Contains("ui=1", StringComparison.Ordinal) ||
        !ContainsPositiveNamedCount(output, "chat=") ||
        !ContainsPositiveNamedCount(output, "animatedActors=") ||
        !ContainsPositiveNamedCount(output, "actorResources=") ||
        !ContainsPositiveNamedCount(output, "actorResourceBytes=") ||
        expectedWarm && !output.Contains("filesFetched=0 bytesFetched=0", StringComparison.Ordinal) ||
        !expectedWarm && output.Contains("bytesFetched=0", StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"The A0.8 {mode} native product output was incomplete:\n{Tail(output, 8_000)}");
}

static bool ContainsPositiveNamedCount(string output, string marker)
{
    int search = 0;
    while (search < output.Length)
    {
        int markerIndex = output.IndexOf(marker, search, StringComparison.Ordinal);
        if (markerIndex < 0) return false;
        int firstDigit = markerIndex + marker.Length;
        int end = firstDigit;
        while (end < output.Length && output[end] is >= '0' and <= '9') end++;
        if (end > firstDigit && ulong.TryParse(
                output.AsSpan(firstDigit, end - firstDigit), out ulong count) && count > 0)
            return true;
        search = firstDigit;
    }
    return false;
}

static bool TryReadNamedCount(string line, string marker, out ulong count)
{
    count = 0;
    int markerIndex = line.IndexOf(marker, StringComparison.Ordinal);
    if (markerIndex < 0) return false;
    int firstDigit = markerIndex + marker.Length;
    int end = firstDigit;
    while (end < line.Length && line[end] is >= '0' and <= '9') end++;
    return end > firstDigit && ulong.TryParse(line.AsSpan(firstDigit, end - firstDigit),
        out count);
}

static string RunCaptured(
    string root,
    string executable,
    IEnumerable<string> arguments,
    int timeoutMilliseconds)
{
    // A source checkout may reside on a noexec mount. Bash can still interpret the exact
    // tracked script; this changes only process launching, not the verification gate.
    bool shellScript = executable.EndsWith(".sh", StringComparison.Ordinal);
    var start = new ProcessStartInfo(shellScript ? "bash" : executable)
    {
        WorkingDirectory = root,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    if (shellScript) start.ArgumentList.Add(executable);
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    Console.WriteLine($"> {start.FileName} {string.Join(' ', start.ArgumentList.Select(Quote))}");
    using Process process = Process.Start(start) ??
        throw new InvalidOperationException($"Unable to start '{executable}'.");
    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
    Task<string> standardError = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(timeoutMilliseconds))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
        string timedOut = standardOutput.GetAwaiter().GetResult() +
            standardError.GetAwaiter().GetResult();
        throw new TimeoutException(
            $"'{executable}' exceeded {timeoutMilliseconds} ms:\n{Tail(timedOut, 8_000)}");
    }
    string output = standardOutput.GetAwaiter().GetResult() + standardError.GetAwaiter().GetResult();
    Console.Write(output);
    if (process.ExitCode != 0)
        throw new InvalidOperationException(
            $"'{executable}' exited with code {process.ExitCode}:\n{Tail(output, 8_000)}");
    return output;
}

static void StopOwnedProcess(Process process)
{
    if (process.HasExited) return;
    process.Kill(entireProcessTree: true);
    if (!process.WaitForExit(5_000))
        throw new TimeoutException("The owned A0.8 deployment process did not stop.");
}

static int ReserveLoopbackPort(SocketType type, ProtocolType protocol)
{
    using var socket = new Socket(AddressFamily.InterNetwork, type, protocol);
    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    return ((IPEndPoint)socket.LocalEndPoint!).Port;
}

static string Tail(string text, int maximumCharacters) =>
    text.Length <= maximumCharacters ? text : text[^maximumCharacters..];

static void VerifyClientPresentationAssets(string root)
{
    var expected = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["Client/PresentationAssets/Avatars/x_bot/x_bot_animated.glb"] =
            "2704d59bee5f8046f335d7e4da05fb8f4f3c7c8f30cdf1e636c4de75ff3b5833",
        ["Client/PresentationAssets/Avatars/y_bot/y_bot_animated.glb"] =
            "553fb71ebe61caede890e582b93a37335ff554f1bb907c034658c5f434e6a567",
    };
    foreach ((string relative, string hash) in expected)
    {
        string path = Path.Combine(root, relative);
        if (!File.Exists(path) ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                hash, StringComparison.Ordinal))
            throw new InvalidOperationException($"The frozen human calibration asset is missing or changed: {relative}.");
    }
    string sourceBuild = Path.Combine(
        root, "Client", "PresentationSource", "Avatars", "build_animated_bodies.py");
    if (!File.Exists(sourceBuild) || Directory.EnumerateFiles(
            Path.Combine(root, "Client", "PresentationAssets", "Avatars"),
            "*.fbx", SearchOption.AllDirectories).Any())
        throw new InvalidOperationException(
            "Animated body authoring inputs must remain reproducible and outside the Godot runtime tree.");
    string archive = Path.Combine(root, "Client", "OfficialContent",
        "ultrasim-official-actors-v1.uspkg");
    const string expectedArchiveHash =
        "03f77dd361bdf7c9edf564fc92c95ced31aa71378a5d14555cc670b1cda6c554";
    if (!File.Exists(archive) || new FileInfo(archive).Length != 4_723_829 ||
        !string.Equals(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(archive))).ToLowerInvariant(),
            expectedArchiveHash, StringComparison.Ordinal))
        throw new InvalidOperationException(
            "The deterministic Official world.local actor package is absent or changed.");
    string godotAssets = Path.Combine(root, "Client", "Godot", "Assets");
    string testAssets = Path.Combine(root, "_Tests", "UltraSim.S0.Godot.Tests", "Assets");
    if (Directory.Exists(godotAssets) || File.Exists(godotAssets) ||
        Directory.Exists(testAssets) || File.Exists(testAssets) ||
        Directory.EnumerateFiles(
            Path.Combine(root, "Client", "PresentationAssets"), "*.tscn",
            SearchOption.AllDirectories).Any() ||
        Directory.EnumerateFiles(
            Path.Combine(root, "Client", "PresentationAssets"), "*.import",
            SearchOption.AllDirectories).Any())
        throw new InvalidOperationException(
            "Official actor GLBs must not be exposed as Godot res://Assets or wrapped as bundled scenes.");
    string[] directReferences = Directory.EnumerateFiles(
            Path.Combine(root, "Client", "Godot"), "*.cs", SearchOption.AllDirectories)
        .Where(path => File.ReadAllText(path).Contains("res://Assets", StringComparison.Ordinal))
        .Select(path => Relative(root, path)).ToArray();
    if (directReferences.Length != 0)
        throw new InvalidOperationException(
            "The maintained Godot client retains a direct actor-asset shortcut: " +
            string.Join(", ", directReferences));
}

static void VerifyClientExportComposition(string root)
{
    const string applicationProject = "Client/Godot/UltraSim.Client.Application.Godot.csproj";
    string projectPath = Path.Combine(root, applicationProject.Replace('/', Path.DirectorySeparatorChar));
    XDocument project = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
    XElement[] references = project.Descendants("ProjectReference").ToArray();
    XElement[] localReferences = references.Where(reference =>
        (reference.Attribute("Include")?.Value ?? "").Contains("BuiltInTestWorlds", StringComparison.Ordinal) ||
        (reference.Attribute("Include")?.Value ?? "").EndsWith(
            "Local/UltraSim.Client.Local.csproj", StringComparison.Ordinal) ||
        (reference.Attribute("Include")?.Value ?? "").Contains("Game/Composition", StringComparison.Ordinal) ||
        (reference.Attribute("Include")?.Value ?? "").Contains("Game/UI", StringComparison.Ordinal)).ToArray();
    XElement sharedGameContracts = references.Single(reference =>
        (reference.Attribute("Include")?.Value ?? "").EndsWith(
            "Game/Contracts/UltraSim.Game.Contracts.csproj", StringComparison.Ordinal));
    XElement online = references.Single(reference =>
        reference.Attribute("Include")?.Value.EndsWith(
            "Network/UltraSim.Client.Network.csproj", StringComparison.Ordinal) == true);
    string onlineCondition = online.Attribute("Condition")?.Value ?? "";
    XElement localHost = references.Single(reference =>
        reference.Attribute("Include")?.Value.EndsWith(
            "ConceptDemo.LocalHost/UltraSim.Server.ConceptDemo.LocalHost.csproj",
            StringComparison.Ordinal) == true);
    string localHostCondition = localHost.Attribute("Condition")?.Value ?? "";
    if (localReferences.Length != 4 || localReferences.Any(reference =>
        !(reference.Attribute("Condition")?.Value ?? "").Contains("SinglePlayer", StringComparison.Ordinal) ||
        !(reference.Attribute("Condition")?.Value ?? "").Contains("Complete", StringComparison.Ordinal) ||
        (reference.Attribute("Condition")?.Value ?? "").Contains("Online", StringComparison.Ordinal)))
        throw new InvalidOperationException("The local client references are not edition-fenced.");
    if (!string.IsNullOrWhiteSpace(sharedGameContracts.Attribute("Condition")?.Value))
        throw new InvalidOperationException(
            "The renderer-neutral Game contract vocabulary must be available to every client edition.");
    if (!onlineCondition.Contains("Online", StringComparison.Ordinal) ||
        !onlineCondition.Contains("Complete", StringComparison.Ordinal) ||
        onlineCondition.Contains("SinglePlayer", StringComparison.Ordinal))
        throw new InvalidOperationException("The remote client reference is not edition-fenced.");
    if (!localHostCondition.Contains("Complete", StringComparison.Ordinal) ||
        localHostCondition.Contains("SinglePlayer", StringComparison.Ordinal) ||
        localHostCondition.Contains("Online", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "The embedded hosted-server adapter must exist only in the Complete composition.");

    string godotProject = File.ReadAllText(Path.Combine(root, "Client", "Godot", "project.godot"));
    if (!godotProject.Contains(
            "project/assembly_name=\"UltraSim.Client.Application.Godot\"", StringComparison.Ordinal))
        throw new InvalidOperationException("The shared Godot project does not name the fixed host assembly.");

    string solutionPath = Path.Combine(
        root, "Client", "Godot", "UltraSim.Client.Application.Godot.sln");
    if (!File.Exists(solutionPath))
        throw new InvalidOperationException(
            "The shared .NET Godot project is missing its adjacent export solution.");
    string solution = File.ReadAllText(solutionPath);
    if (!solution.Contains(
            "\"UltraSim.Client.Application.Godot.csproj\"", StringComparison.Ordinal) ||
        Regex.Matches(solution, "^Project\\(", RegexOptions.Multiline | RegexOptions.CultureInvariant).Count != 1)
        throw new InvalidOperationException(
            "The shared Godot export solution must contain exactly the one application project.");

    foreach (string internalSource in new[] { "Adapter", "Local", "Remote", "Shell" })
    {
        string marker = Path.Combine(root, "Client", "Godot", internalSource, ".gdignore");
        if (!File.Exists(marker))
            throw new InvalidOperationException(
                $"Internal Godot C# source '{internalSource}' is exposed to native script import. " +
                "Only the scene-attached ClientHost may be imported; all sources still compile " +
                "into the one application assembly.");
    }

    string clientRootSource = Path.Combine(
        root, "Client", "Godot", "Shell", "ClientRoot.cs");
    if (!File.Exists(clientRootSource) ||
        File.Exists(Path.Combine(
            root, "Client", "Godot", "Shell", "ClientCompositionBootstrap.cs")) ||
        !File.ReadAllText(clientRootSource).Contains(
            "class ClientRoot : Control", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "The Godot ClientRoot script class must remain in matching ClientRoot.cs source. " +
            "Godot's C# script-path generator uses that pairing during editor hot reload.");

    string presets = File.ReadAllText(Path.Combine(root, "Client", "Godot", "export_presets.cfg"));
    foreach ((string name, string feature) in new[]
    {
        ("UltraSim Single Player Linux", "ultrasim_single_player"),
        ("UltraSim Online Linux", "ultrasim_online"),
        ("UltraSim Complete Linux", "ultrasim_complete"),
    })
    {
        if (!presets.Contains($"name=\"{name}\"", StringComparison.Ordinal) ||
            !presets.Contains($"custom_features=\"{feature}\"", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The shared Godot project is missing export product '{name}' / '{feature}'.");
    }
    if (Regex.Matches(
            presets, "exclude_filter=\"Assets/\\*\\*\"",
            RegexOptions.CultureInvariant).Count != 3)
        throw new InvalidOperationException(
            "Every client export must physically exclude the retired res://Assets presentation tree.");
    string exportScript = File.ReadAllText(Path.Combine(
        root, "Client", "Godot", "export-client.sh"));
    if (!exportScript.Contains("OfficialContentBuilder", StringComparison.Ordinal) ||
        !exportScript.Contains("Content/Official", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "Local-capable exports do not publish the deterministic external Official package.");
    if (!exportScript.Contains("dotnet restore", StringComparison.Ordinal) ||
        !exportScript.Contains("-r linux-x64", StringComparison.Ordinal) ||
        !exportScript.Contains("--force", StringComparison.Ordinal) ||
        !exportScript.Contains("-p:UltraSimEdition=\"$edition\"", StringComparison.Ordinal) ||
        !exportScript.Contains("-p:UltraSimNetworkFlavor=\"$network_flavor\"", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "Edition-switched Godot exports must force an exact RID restore before publish.");

    foreach (string retired in new[]
    {
        "SinglePlayer.Godot", "Online.Godot", "Complete.Godot", "Playable.Godot",
        "Bootstrap.Godot", "Local.Godot", "_Godot",
    })
    {
        if (Directory.Exists(Path.Combine(root, "Client", retired)))
            throw new InvalidOperationException(
                $"Retired Godot project/source root is still active: Client/{retired}.");
    }
}

static void VerifyArchitecture(string root)
{
    var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Framework/ControlPlane/UltraSim.Framework.ControlPlane.csproj"] = [],
        ["Framework/RuntimeHost/UltraSim.Framework.RuntimeHost.csproj"] = [],
        ["Framework/Definitions/UltraSim.Framework.Definitions.csproj"] = [],
        ["Framework/Persistence/UltraSim.Framework.Persistence.csproj"] = [],
        ["Bridges/ControlPlane.ECS/UltraSim.Bridges.ControlPlane.ECS.csproj"] =
            ["Framework/ControlPlane/UltraSim.Framework.ControlPlane.csproj", "Kernels/ECS/UltraSim.ECS.csproj"],
        ["Bridges/ControlPlane.ServerRuntime/UltraSim.Bridges.ControlPlane.ServerRuntime.csproj"] =
            ["Framework/ControlPlane/UltraSim.Framework.ControlPlane.csproj", "Server/Runtime/UltraSim.Server.Runtime.csproj"],
        ["Bridges/ControlPlane.RuntimeHost/UltraSim.Bridges.ControlPlane.RuntimeHost.csproj"] =
            ["Framework/ControlPlane/UltraSim.Framework.ControlPlane.csproj", "Framework/RuntimeHost/UltraSim.Framework.RuntimeHost.csproj"],
        ["Client/ControlPlane/UltraSim.Client.ControlPlane.csproj"] =
            ["Framework/ControlPlane/UltraSim.Framework.ControlPlane.csproj", "Client/Core/UltraSim.Client.Core.csproj"],
        ["Framework/Http/Endpoint/UltraSim.Framework.Http.Endpoint.csproj"] = [],
        ["Bridges/Http.Kestrel/UltraSim.Bridges.Http.Kestrel.csproj"] =
            ["Framework/Http/Endpoint/UltraSim.Framework.Http.Endpoint.csproj"],
        ["Kernels/ECS.Generators/UltraSim.ECS.Generators.csproj"] = [],
        ["Kernels/ECS/UltraSim.ECS.csproj"] =
            ["Kernels/ECS.Generators/UltraSim.ECS.Generators.csproj"],
        ["Kernels/Network.Generators/UltraSim.Network.Generators.csproj"] = [],
        ["Kernels/Network/UltraSim.Network.csproj"] =
            ["Kernels/Network.Generators/UltraSim.Network.Generators.csproj"],
        ["Kernels/Network.Testing/UltraSim.Network.Testing.csproj"] =
            ["Kernels/Network/UltraSim.Network.csproj"],
        ["Kernels/Spatial/UltraSim.Spatial.csproj"] = [],
        ["Kernels/UI/UltraSim.UI.csproj"] = [],
        ["Server/Persistence/UltraSim.Server.Persistence.csproj"] =
            ["Framework/Persistence/UltraSim.Framework.Persistence.csproj", "Kernels/ECS/UltraSim.ECS.csproj"],
        ["Server/Authoring/UltraSim.Server.Authoring.csproj"] =
            ["Framework/RuntimeHost/UltraSim.Framework.RuntimeHost.csproj",
             "Framework/Definitions/UltraSim.Framework.Definitions.csproj",
             "Framework/Persistence/UltraSim.Framework.Persistence.csproj",
             "Framework/ControlPlane/UltraSim.Framework.ControlPlane.csproj"],
        ["Server/Runtime/UltraSim.Server.Runtime.csproj"] =
            ["Kernels/ECS/UltraSim.ECS.csproj"],
        ["Server/Hosting/UltraSim.Server.Hosting.csproj"] =
        [
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
            "Framework/Http/Endpoint/UltraSim.Framework.Http.Endpoint.csproj",
            "Bridges/Http.Kestrel/UltraSim.Bridges.Http.Kestrel.csproj",
        ],
        ["Server/DirectConnect/UltraSim.Server.DirectConnect.csproj"] =
        [
            "Game/Protocol/UltraSim.Game.Protocol.csproj",
            "Game/Replication.Remote/UltraSim.Game.Replication.Remote.csproj",
            "Game/UI/UltraSim.Game.UI.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Server/ConceptDemo/UltraSim.Server.ConceptDemo.csproj",
            "Server/Authoring/UltraSim.Server.Authoring.csproj",
        ],
        ["Server/ConceptDemo/UltraSim.Server.ConceptDemo.csproj"] =
        [
            "Framework/Http/Endpoint/UltraSim.Framework.Http.Endpoint.csproj",
            "Bridges/Http.Kestrel/UltraSim.Bridges.Http.Kestrel.csproj",
            "Client/Content/UltraSim.Client.Content.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Protocol/UltraSim.Game.Protocol.csproj",
            "Game/Replication.Remote/UltraSim.Game.Replication.Remote.csproj",
            "Game/UI/UltraSim.Game.UI.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Server/Hosting/UltraSim.Server.Hosting.csproj",
        ],
        ["Server/ConceptDemo.LocalHost/UltraSim.Server.ConceptDemo.LocalHost.csproj"] =
        [
            "Client/Local/UltraSim.Client.Local.csproj",
            "Game/Replication.Remote/UltraSim.Game.Replication.Remote.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Server/ConceptDemo/UltraSim.Server.ConceptDemo.csproj",
        ],
        ["Client/Core/UltraSim.Client.Core.csproj"] =
        [
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Presentation.Contracts/UltraSim.Game.Presentation.Contracts.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/Spatial/UltraSim.Spatial.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
        ],
        ["Client/Content/UltraSim.Client.Content.csproj"] =
        [
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
        ],
        ["Client/StandardUi/UltraSim.Client.StandardUi.csproj"] =
            ["Kernels/UI/UltraSim.UI.csproj", "Framework/Definitions/UltraSim.Framework.Definitions.csproj"],
        ["Client/BuiltInTestWorlds/UltraSim.Client.BuiltInTestWorlds.csproj"] =
            ["Client/Content/UltraSim.Client.Content.csproj"],
        ["Client/Network/UltraSim.Client.Network.csproj"] =
        [
            "Client/Content/UltraSim.Client.Content.csproj",
            "Client/Core/UltraSim.Client.Core.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Protocol/UltraSim.Game.Protocol.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
        ],
        ["Client/Local/UltraSim.Client.Local.csproj"] =
        [
            "Client/Core/UltraSim.Client.Core.csproj",
            "Game/Admission/UltraSim.Game.Admission.csproj",
            "Game/Chat/UltraSim.Game.Chat.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Inventory/UltraSim.Game.Inventory.csproj",
            "Game/Persistence/UltraSim.Game.Persistence.csproj",
            "Game/Replication/UltraSim.Game.Replication.csproj",
            "Game/UI/UltraSim.Game.UI.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/ECS/UltraSim.ECS.csproj",
            "Kernels/Spatial/UltraSim.Spatial.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
            "Server/Persistence/UltraSim.Server.Persistence.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Client/Godot/UltraSim.Client.Application.Godot.csproj"] =
        [
            "Client/BuiltInTestWorlds/UltraSim.Client.BuiltInTestWorlds.csproj",
            "Client/Content/UltraSim.Client.Content.csproj",
            "Client/ControlPlane/UltraSim.Client.ControlPlane.csproj",
            "Client/Core/UltraSim.Client.Core.csproj",
            "Framework/ControlPlane/UltraSim.Framework.ControlPlane.csproj",
            "Client/Local/UltraSim.Client.Local.csproj",
            "Client/Network/UltraSim.Client.Network.csproj",
            "Client/StandardUi/UltraSim.Client.StandardUi.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/UI/UltraSim.Game.UI.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/Spatial/UltraSim.Spatial.csproj",
            "Server/ConceptDemo.LocalHost/UltraSim.Server.ConceptDemo.LocalHost.csproj",
        ],
        ["Game/Accounting/UltraSim.Game.Accounting.csproj"] =
            ["Kernels/ECS/UltraSim.ECS.csproj"],
        ["Game/Contracts/UltraSim.Game.Contracts.csproj"] =
            ["Game/Accounting/UltraSim.Game.Accounting.csproj", "Kernels/ECS/UltraSim.ECS.csproj"],
        ["Game/Inventory/UltraSim.Game.Inventory.csproj"] =
        [
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Kernels/ECS/UltraSim.ECS.csproj",
        ],
        ["Game/Items.Contracts/UltraSim.Game.Items.Contracts.csproj"] =
            ["Framework/Definitions/UltraSim.Framework.Definitions.csproj"],
        ["Game/Combat.Contracts/UltraSim.Game.Combat.Contracts.csproj"] =
            ["Framework/Definitions/UltraSim.Framework.Definitions.csproj"],
        ["Game/Skills.Contracts/UltraSim.Game.Skills.Contracts.csproj"] =
            ["Framework/Definitions/UltraSim.Framework.Definitions.csproj"],
        ["Game/Presentation.Contracts/UltraSim.Game.Presentation.Contracts.csproj"] =
            ["Framework/Definitions/UltraSim.Framework.Definitions.csproj"],
        ["Game/Admission/UltraSim.Game.Admission.csproj"] =
        [
            "Game/Accounting/UltraSim.Game.Accounting.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Game/Chat/UltraSim.Game.Chat.csproj"] =
        [
            "Game/Admission/UltraSim.Game.Admission.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/ECS/UltraSim.ECS.csproj",
            "Kernels/Spatial/UltraSim.Spatial.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Game/UI/UltraSim.Game.UI.csproj"] =
        [
            "Game/Inventory/UltraSim.Game.Inventory.csproj",
            // P6 projects an admitted public Definition portrait without giving UI/Inventory art ownership.
            "Game/Presentation.Contracts/UltraSim.Game.Presentation.Contracts.csproj",
            "Kernels/ECS/UltraSim.ECS.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Game/World/UltraSim.Game.World.csproj"] =
            ["Kernels/ECS/UltraSim.ECS.csproj", "Kernels/Spatial/UltraSim.Spatial.csproj"],
        ["Game/Physics/UltraSim.Game.Physics.csproj"] =
            ["Game/World/UltraSim.Game.World.csproj"],
        ["Game/Composition/UltraSim.Game.Composition.csproj"] =
        [
            "Framework/Definitions/UltraSim.Framework.Definitions.csproj",
            "Game/Accounting/UltraSim.Game.Accounting.csproj",
            "Game/Items.Contracts/UltraSim.Game.Items.Contracts.csproj",
            "Game/Combat.Contracts/UltraSim.Game.Combat.Contracts.csproj",
            "Game/Skills.Contracts/UltraSim.Game.Skills.Contracts.csproj",
            "Game/Presentation.Contracts/UltraSim.Game.Presentation.Contracts.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Inventory/UltraSim.Game.Inventory.csproj",
            "Game/Physics/UltraSim.Game.Physics.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/ECS.Generators/UltraSim.ECS.Generators.csproj",
            "Kernels/ECS/UltraSim.ECS.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Game/Persistence/UltraSim.Game.Persistence.csproj"] =
        [
            "Game/Accounting/UltraSim.Game.Accounting.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Server/Persistence/UltraSim.Server.Persistence.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Game/Protocol/UltraSim.Game.Protocol.csproj"] =
        [
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Kernels/Network.Generators/UltraSim.Network.Generators.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
        ],
        ["Game/Replication/UltraSim.Game.Replication.csproj"] =
        [
            "Game/Admission/UltraSim.Game.Admission.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/ECS/UltraSim.ECS.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Game/Replication.Remote/UltraSim.Game.Replication.Remote.csproj"] =
        [
            "Game/Accounting/UltraSim.Game.Accounting.csproj",
            "Game/Admission/UltraSim.Game.Admission.csproj",
            "Game/Chat/UltraSim.Game.Chat.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Protocol/UltraSim.Game.Protocol.csproj",
            "Game/Replication/UltraSim.Game.Replication.csproj",
            "Game/UI/UltraSim.Game.UI.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Tools/NetworkBots/UltraSim.NetworkBots.csproj"] =
        [
            "Client/Content/UltraSim.Client.Content.csproj",
            "Client/Core/UltraSim.Client.Core.csproj",
            "Client/Network/UltraSim.Client.Network.csproj",
            "Client/StandardUi/UltraSim.Client.StandardUi.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Protocol/UltraSim.Game.Protocol.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
        ],
    };

    string[] roots = ["Kernels", "Server", "Client", "Game", "Tools", "Framework", "Bridges"];
    string[] actualProjects = roots
        .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.csproj", SearchOption.AllDirectories))
        .Select(path => Relative(root, path))
        .OrderBy(static path => path, StringComparer.Ordinal)
        .ToArray();
    string[] expectedProjects = expected.Keys.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
    if (!actualProjects.SequenceEqual(expectedProjects, StringComparer.Ordinal))
        throw new InvalidOperationException(
            "The active production project inventory changed. Update the explicit architecture contract only after review.\n" +
            $"Expected: {string.Join(", ", expectedProjects)}\nActual: {string.Join(", ", actualProjects)}");

    foreach ((string project, string[] allowed) in expected)
    {
        string projectPath = Path.Combine(root, project);
        XDocument document = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
        string projectDirectory = Path.GetDirectoryName(projectPath)!;
        string[] references = document.Descendants("ProjectReference")
            .Select(element => element.Attribute("Include")?.Value ??
                throw new InvalidOperationException($"ProjectReference in '{project}' has no Include."))
            .Select(include => Relative(root, Path.GetFullPath(Path.Combine(projectDirectory,
                include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)))))
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();
        string[] orderedAllowed = allowed.OrderBy(static path => path, StringComparer.Ordinal).ToArray();
        if (references.Any(static path =>
                path.StartsWith("_V2/", StringComparison.Ordinal) ||
                path.StartsWith("_Archive/_V2/", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Active project '{project}' references frozen V2 source.");
        if (!references.SequenceEqual(orderedAllowed, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Project-reference boundary changed for '{project}'.\n" +
                $"Expected: {string.Join(", ", orderedAllowed)}\nActual: {string.Join(", ", references)}");
        if (project.StartsWith("Framework/", StringComparison.Ordinal) &&
            (document.Descendants("FrameworkReference").Any() || document.Descendants("PackageReference").Any()))
            throw new InvalidOperationException("Framework foundation projects must remain BCL-only.");
        if (!project.StartsWith("Client/Godot/", StringComparison.Ordinal) &&
            (document.Root?.Attribute("Sdk")?.Value.Contains("Godot", StringComparison.OrdinalIgnoreCase) == true ||
             document.Descendants("PackageReference").Any(e =>
                 e.Attribute("Include")?.Value.Contains("Godot", StringComparison.OrdinalIgnoreCase) == true) ||
             document.Descendants("Reference").Any(e =>
                 e.Attribute("Include")?.Value.Contains("Godot", StringComparison.OrdinalIgnoreCase) == true)))
            throw new InvalidOperationException($"Headless production project '{project}' directly depends on Godot.");
    }
    string[] headlessGodotSources = new[] { "Game", "Framework", "Kernels", "Server", "Bridges",
            "Client/Core", "Client/Local", "Client/Network" }
        .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
        .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) &&
                       !path.Contains("/bin/", StringComparison.Ordinal))
        .Where(path => Regex.IsMatch(File.ReadAllText(path),
            @"^\s*(?:global\s+)?using\s+(?:static\s+)?Godot(?:\.[A-Za-z_][A-Za-z0-9_]*)*\s*;|\bglobal::Godot\.",
            RegexOptions.Multiline))
        .Select(path => Relative(root, path)).ToArray();
    if (headlessGodotSources.Length != 0)
        throw new InvalidOperationException("Headless/Game source directly uses Godot: " +
            string.Join(", ", headlessGodotSources));
    string[] legacyHttpSources = new[] { "Server", "Framework", "Bridges", "Client", "Game", "Kernels" }
        .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.cs", SearchOption.AllDirectories))
        .Where(path => !path.Contains("/obj/", StringComparison.Ordinal) && !path.Contains("/bin/", StringComparison.Ordinal))
        .Where(path => Regex.IsMatch(File.ReadAllText(path), @"\bHttpListener(?:Context|Response)?\b"))
        .Select(path => Relative(root, path)).ToArray();
    if (legacyHttpSources.Length != 0)
        throw new InvalidOperationException("Production managed listener remains: " + string.Join(", ", legacyHttpSources));
}

static void VerifyDocumentation(string root)
{
    string[] rootMarkdown = Directory.EnumerateFiles(root, "*.md", SearchOption.TopDirectoryOnly)
        .Select(path => Path.GetFileName(path))
        .OrderBy(static path => path, StringComparer.Ordinal)
        .ToArray();
    string[] allowedRootMarkdown = ["README.md"];
    if (!rootMarkdown.SequenceEqual(allowedRootMarkdown, StringComparer.Ordinal))
    {
        throw new InvalidOperationException(
            "Root Markdown is restricted to the landing page. Put current contracts under Docs/ or the owning " +
            "subsystem; put unfinished work under Docs/Plans; use git for completed reports.\n" +
            $"Expected: {string.Join(", ", allowedRootMarkdown)}\nActual: {string.Join(", ", rootMarkdown)}");
    }

    string[] activeDocuments =
    [
        "README.md",
        "Docs/README.md",
        "Docs/ARCHITECTURE.md",
        "Docs/RUNTIME.md",
        "Docs/PERSISTENCE.md",
        "Docs/UI.md",
        "Docs/GAME.md",
        "Docs/Plans/NETWORK.md",
        "Docs/Plans/CHAT.md",
        "Docs/Plans/CLIENT.md",
        "Docs/Plans/ASSET_STREAMING.md",
        "Docs/Plans/PHYSICS.md",
        "Docs/Plans/TERRAIN.md",
        "Kernels/ECS/README.md",
        "Kernels/ECS/EXECUTION.md",
        "Kernels/ECS/SCHEDULER.md",
        "Kernels/ECS/STRUCTURAL.md",
        "Kernels/ECS/CONTENT.md",
        "Kernels/ECS/LIFECYCLE.md",
        "Kernels/ECS/PERSISTENCE.md",
        "Kernels/Network/README.md",
        "Kernels/Spatial/README.md",
        "Client/Core/README.md",
        "Client/Content/README.md",
        "Client/StandardUi/README.md",
        "Client/BuiltInTestWorlds/README.md",
        "Client/Local/README.md",
        "Client/Godot/README.md",
        "Game/Admission/README.md",
        "Game/World/README.md",
        "Game/Chat/README.md",
        "Game/Physics/README.md",
        "Game/Replication/README.md",
        "_Benchmarks/README.md",
        "_Benchmarks/Baselines/README.md",
        "_Benchmarks/ECS_V2_V3_COMPARISON.md",
        "_Benchmarks/ECS_EXTREME_SPHERE_STRESS.md",
        "_Benchmarks/NETWORK_V2_V3_BOT_COMPARISON.md",
        "_Tools/LocalAI/README.md",
    ];
    foreach (string document in activeDocuments)
    {
        string fullPath = Path.Combine(root, document.Replace('/', Path.DirectorySeparatorChar));
        if (!File.Exists(fullPath))
            throw new InvalidOperationException($"Canonical documentation '{document}' is missing.");
        VerifyRelativeMarkdownLinks(root, document, fullPath);
    }

    var navigation = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["README.md"] = ["Docs/README.md"],
        ["Docs/README.md"] =
        [
            "Docs/ARCHITECTURE.md", "Docs/RUNTIME.md", "Docs/PERSISTENCE.md", "Docs/UI.md", "Docs/GAME.md",
            "Docs/Plans/CHAT.md", "Docs/Plans/NETWORK.md", "Docs/Plans/PHYSICS.md",
            "Docs/Plans/TERRAIN.md", "Kernels/ECS/README.md",
            "Kernels/Network/README.md", "Kernels/Spatial/README.md", "Client/Core/README.md",
            "Client/Content/README.md", "Client/BuiltInTestWorlds/README.md",
            "Client/Local/README.md", "Client/StandardUi/README.md", "Client/Godot/README.md", "Game/World/README.md",
            "Game/Admission/README.md", "Game/Chat/README.md", "Game/Physics/README.md",
            "Game/Replication/README.md",
            "_Benchmarks/README.md", "_Tools/LocalAI/README.md",
        ],
        ["Kernels/ECS/README.md"] =
        [
            "Kernels/ECS/EXECUTION.md", "Kernels/ECS/SCHEDULER.md", "Kernels/ECS/STRUCTURAL.md",
            "Kernels/ECS/CONTENT.md", "Kernels/ECS/LIFECYCLE.md", "Kernels/ECS/PERSISTENCE.md",
        ],
        ["_Benchmarks/README.md"] =
        [
            "_Benchmarks/Baselines/README.md", "_Benchmarks/ECS_V2_V3_COMPARISON.md",
            "_Benchmarks/ECS_EXTREME_SPHERE_STRESS.md", "_Benchmarks/NETWORK_V2_V3_BOT_COMPARISON.md",
        ],
    };
    foreach ((string parent, string[] children) in navigation)
    {
        string text = File.ReadAllText(Path.Combine(root, parent.Replace('/', Path.DirectorySeparatorChar)));
        foreach (string child in children)
        {
            string fileName = Path.GetFileName(child);
            if (!text.Contains(fileName, StringComparison.Ordinal))
                throw new InvalidOperationException($"Documentation map '{parent}' does not link '{child}'.");
        }
    }

    string[] staleSentinels =
    [
        "V3 lifecycle publication is not implemented yet",
        "nominal 16 ms / 62.5 Hz",
        "ECS_NEXT_DESIGN.md",
        "S0 Integration Design.md",
    ];
    foreach (string document in activeDocuments)
    {
        string text = File.ReadAllText(Path.Combine(root, document.Replace('/', Path.DirectorySeparatorChar)));
        foreach (string sentinel in staleSentinels)
        {
            if (text.Contains(sentinel, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Stale documentation claim '{sentinel}' appears in '{document}'.");
        }
    }

    string[] productionRoots = ["Kernels", "Server", "Client", "Game", "Framework", "Bridges"];
    string[] documentationErrors =
    [
        "CS1570", "CS1572", "CS1573", "CS1574", "CS1580", "CS1581", "CS1584", "CS1587",
        "CS1711", "CS1734", "CS1735",
    ];
    foreach (string projectPath in productionRoots
        .SelectMany(folder => Directory.EnumerateFiles(Path.Combine(root, folder), "*.csproj", SearchOption.AllDirectories)))
    {
        string relative = Relative(root, projectPath);
        XDocument project = XDocument.Load(projectPath, LoadOptions.SetLineInfo);
        bool generateDocumentation = project.Descendants("GenerateDocumentationFile")
            .Any(element => string.Equals(element.Value.Trim(), "true", StringComparison.OrdinalIgnoreCase));
        if (!generateDocumentation)
            throw new InvalidOperationException($"Production project '{relative}' must emit an XML documentation file.");
        bool suppressesMissingTrivia = project.Descendants("NoWarn")
            .Any(element => element.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Contains("CS1591", StringComparer.OrdinalIgnoreCase));
        if (!suppressesMissingTrivia)
        {
            throw new InvalidOperationException(
                $"Production project '{relative}' must state the current intentional CS1591 policy. " +
                "Semantic public contracts are audited manually; generated/trivial member boilerplate remains suppressed.");
        }
        string[] warningsAsErrors = project.Descendants("WarningsAsErrors")
            .SelectMany(element => element.Value.Split(';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .ToArray();
        string[] missingDocumentationErrors = documentationErrors
            .Where(code => !warningsAsErrors.Contains(code, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (missingDocumentationErrors.Length != 0)
        {
            throw new InvalidOperationException(
                $"Production project '{relative}' must fail malformed or inconsistent XML documentation. " +
                $"Missing WarningsAsErrors: {string.Join(", ", missingDocumentationErrors)}");
        }
    }
}

static void VerifyRelativeMarkdownLinks(string root, string relativeDocument, string fullPath)
{
    string text = File.ReadAllText(fullPath);
    var links = Regex.Matches(text, @"!?\[[^\]\r\n]*\]\((?<target>[^)\r\n]+)\)", RegexOptions.CultureInvariant);
    foreach (Match match in links)
    {
        string target = match.Groups["target"].Value.Trim();
        if (target.Length >= 2 && target[0] == '<' && target[^1] == '>')
            target = target[1..^1];
        if (target.Length == 0 || target[0] == '#' || target.StartsWith("/", StringComparison.Ordinal) ||
            Uri.TryCreate(target, UriKind.Absolute, out _))
        {
            continue;
        }
        int fragment = target.IndexOf('#');
        if (fragment >= 0) target = target[..fragment];
        int query = target.IndexOf('?');
        if (query >= 0) target = target[..query];
        target = Uri.UnescapeDataString(target.Trim());
        if (target.Length == 0)
            continue;
        string documentDirectory = Path.GetDirectoryName(fullPath)!;
        string resolved = Path.GetFullPath(Path.Combine(documentDirectory,
            target.Replace('/', Path.DirectorySeparatorChar)));
        string containedPrefix = Path.GetFullPath(root) + Path.DirectorySeparatorChar;
        if (!resolved.StartsWith(containedPrefix, StringComparison.Ordinal) &&
            !string.Equals(resolved, Path.GetFullPath(root), StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Repository-relative link escapes the repository in '{relativeDocument}': {target}");
        }
        if (!File.Exists(resolved) && !Directory.Exists(resolved))
            throw new InvalidOperationException($"Broken relative link in '{relativeDocument}': {target}");
    }
}

static string FindRepositoryRoot()
{
    foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        DirectoryInfo? directory = new(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "README.md")) &&
                File.Exists(Path.Combine(directory.FullName, "Docs", "README.md")) &&
                Directory.Exists(Path.Combine(directory.FullName, "Kernels")))
                return directory.FullName;
            directory = directory.Parent;
        }
    }
    throw new DirectoryNotFoundException("Unable to locate the UltraSim repository root.");
}

static string ResolveGodot(string? configured)
{
    if (!string.IsNullOrWhiteSpace(configured))
    {
        string full = Path.GetFullPath(configured);
        if (!File.Exists(full)) throw new FileNotFoundException("Configured Godot executable was not found.", full);
        return full;
    }
    string? environment = Environment.GetEnvironmentVariable("ULTRASIM_GODOT");
    if (!string.IsNullOrWhiteSpace(environment))
    {
        string full = Path.GetFullPath(environment);
        if (!File.Exists(full)) throw new FileNotFoundException("ULTRASIM_GODOT was not found.", full);
        return full;
    }
    const string known = "/mnt/data/Programs/Godot v4.7.1 Mono/Godot_v4.7.1-stable_mono_linux.x86_64";
    if (File.Exists(known)) return known;
    throw new FileNotFoundException(
        "Godot was not found. Set ULTRASIM_GODOT, pass --godot=<path>, or explicitly use --skip-godot.");
}

static void Run(
    string root,
    string executable,
    IEnumerable<string> arguments,
    IReadOnlyDictionary<string, string>? environment = null)
{
    // Execute the same tracked script through Bash when the checkout is mounted noexec.
    bool shellScript = executable.EndsWith(".sh", StringComparison.Ordinal);
    var start = new ProcessStartInfo(shellScript ? "bash" : executable)
    {
        WorkingDirectory = root,
        UseShellExecute = false,
    };
    if (environment is not null)
        foreach ((string key, string value) in environment)
            start.Environment[key] = value;
    if (shellScript) start.ArgumentList.Add(executable);
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    Console.WriteLine($"> {start.FileName} {string.Join(' ', start.ArgumentList.Select(Quote))}");
    using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Unable to start '{executable}'.");
    process.WaitForExit();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"'{executable}' exited with code {process.ExitCode}.");
}

static void RunExpectFailure(
    string root,
    string executable,
    IEnumerable<string> arguments,
    string gate)
{
    var start = new ProcessStartInfo(executable)
    {
        WorkingDirectory = root,
        UseShellExecute = false,
    };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    Console.WriteLine($"> {executable} {string.Join(' ', start.ArgumentList.Select(Quote))}");
    using Process process = Process.Start(start) ?? throw new InvalidOperationException($"Unable to start '{executable}'.");
    process.WaitForExit();
    if (process.ExitCode == 0)
        throw new InvalidOperationException($"'{executable}' unexpectedly accepted the invalid product composition.");
    Console.WriteLine($"PASS {gate} exitCode={process.ExitCode}");
}

static string Quote(string value) => value.Any(char.IsWhiteSpace) ? $"\"{value}\"" : value;
static string Relative(string root, string path) => Path.GetRelativePath(root, path).Replace('\\', '/');

internal sealed record VerificationOptions(
    bool ArchitectureOnly,
    bool F17Only,
    bool F112Only,
    bool F112MixedOnly,
    bool F112R1Only,
    bool Pf1Only,
    bool Pf2Only,
    bool Pf25Only,
    bool Pf4aOnly,
    bool Pf4bOnly,
    bool Pf4cOnly,
    bool Pf4dOnly,
    bool Pf4eOnly,
    bool Pf4eR1Only,
    bool Pf4eR2Only,
    bool Pf4fOnly,
    bool Pf5Only,
    bool Pf5R1Only,
    bool Pf5R2Only,
    bool Pf5R2R1Only,
    bool Pf5R2R2Only,
    bool Pf5R3Only,
    bool Pf5R4Only,
    bool Pf5R5Only,
    bool Pf5R6Only,
    bool Pf5R7Only,
    bool Pf5R8Only,
    bool Pf5R9Only,
    bool Pf5R12Only,
    bool Pf3Only,
    bool Pf3NativeOnly,
    bool Pf25LongNative,
    bool AllConfigurations,
    string Configuration,
    bool SkipGodot,
    bool NoRestore,
    string? GodotPath,
    string? Pf25EvidenceOutputDirectory,
    int Pf3NativeWarmupSeconds,
    int Pf3NativeMeasurementSeconds,
    string? Pf3NativeEvidenceOutputDirectory,
    string Pf3NativeAliceRenderMode,
    string Pf3NativeBobRenderMode)
{
    internal static VerificationOptions Parse(string[] arguments)
    {
        bool architectureOnly = false, f17Only = false, f112Only = false, f112MixedOnly = false, f112R1Only = false,
            pf1Only = false, pf2Only = false, pf25Only = false,
            pf3Only = false, pf3NativeOnly = false, pf4aOnly = false, pf4bOnly = false,
            pf4cOnly = false, pf4dOnly = false, pf4eOnly = false, pf4eR1Only = false,
            pf4eR2Only = false, pf4fOnly = false, pf5Only = false,
            pf5R1Only = false, pf5R2Only = false, pf5R2R1Only = false,
            pf5R2R2Only = false, pf5R3Only = false, pf5R4Only = false,
            pf5R5Only = false, pf5R6Only = false, pf5R7Only = false,
            pf5R8Only = false, pf5R9Only = false, pf5R12Only = false,
            pf25LongNative = false,
            allConfigurations = false;
        bool skipGodot = false, noRestore = false;
        string configuration = "Release";
        string? godot = null, pf25EvidenceOutputDirectory = null,
            pf3NativeEvidenceOutputDirectory = null;
        int pf3NativeWarmupSeconds = 5, pf3NativeMeasurementSeconds = 20;
        string pf3NativeAliceRenderMode = "headless", pf3NativeBobRenderMode = "headless";
        foreach (string argument in arguments)
        {
            if (argument == "--architecture-only") architectureOnly = true;
            else if (argument == "--f1-7-only") f17Only = true;
            else if (argument == "--f1-12-only") f112Only = true;
            else if (argument == "--f1-12-mixed-only") f112MixedOnly = true;
            else if (argument == "--f1-12-r1-only") f112R1Only = true;
            else if (argument == "--pf1-only") pf1Only = true;
            else if (argument == "--pf2-only") pf2Only = true;
            else if (argument == "--pf2.5-only") pf25Only = true;
            else if (argument == "--pf3-only") pf3Only = true;
            else if (argument == "--pf3-native-only") pf3NativeOnly = true;
            else if (argument == "--pf4a-only") pf4aOnly = true;
            else if (argument == "--pf4b-only") pf4bOnly = true;
            else if (argument == "--pf4c-only") pf4cOnly = true;
            else if (argument == "--pf4d-only") pf4dOnly = true;
            else if (argument == "--pf4e-only") pf4eOnly = true;
            else if (argument == "--pf4e-r1-only") pf4eR1Only = true;
            else if (argument == "--pf4e-r2-only") pf4eR2Only = true;
            else if (argument == "--pf4f-only") pf4fOnly = true;
            else if (argument == "--pf5-only") pf5Only = true;
            else if (argument == "--pf5-r1-only") pf5R1Only = true;
            else if (argument == "--pf5-r2-only") pf5R2Only = true;
            else if (argument == "--pf5-r2-r1-only") pf5R2R1Only = true;
            else if (argument == "--pf5-r2-r2-only") pf5R2R2Only = true;
            else if (argument == "--pf5-r3-only") pf5R3Only = true;
            else if (argument == "--pf5-r4-only") pf5R4Only = true;
            else if (argument == "--pf5-r5-only") pf5R5Only = true;
            else if (argument == "--pf5-r6-only") pf5R6Only = true;
            else if (argument == "--pf5-r7-only") pf5R7Only = true;
            else if (argument == "--pf5-r8-only") pf5R8Only = true;
            else if (argument == "--pf5-r9-only") pf5R9Only = true;
            else if (argument == "--pf5-r12-only") pf5R12Only = true;
            else if (argument == "--pf2.5-long-native") pf25LongNative = true;
            else if (argument == "--all-configurations") allConfigurations = true;
            else if (argument == "--skip-godot") skipGodot = true;
            else if (argument == "--no-restore") noRestore = true;
            else if (argument.StartsWith("--configuration=", StringComparison.Ordinal))
                configuration = argument["--configuration=".Length..];
            else if (argument.StartsWith("--godot=", StringComparison.Ordinal)) godot = argument["--godot=".Length..];
            else if (argument.StartsWith("--pf2.5-evidence-output=", StringComparison.Ordinal))
                pf25EvidenceOutputDirectory = argument["--pf2.5-evidence-output=".Length..];
            else if (argument.StartsWith("--pf3-native-warmup-seconds=", StringComparison.Ordinal))
                pf3NativeWarmupSeconds = int.Parse(
                    argument["--pf3-native-warmup-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4a-native-warmup-seconds=", StringComparison.Ordinal))
                pf3NativeWarmupSeconds = int.Parse(
                    argument["--pf4a-native-warmup-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4b-native-warmup-seconds=", StringComparison.Ordinal))
                pf3NativeWarmupSeconds = int.Parse(
                    argument["--pf4b-native-warmup-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4c-native-warmup-seconds=", StringComparison.Ordinal))
                pf3NativeWarmupSeconds = int.Parse(
                    argument["--pf4c-native-warmup-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4d-native-warmup-seconds=", StringComparison.Ordinal))
                pf3NativeWarmupSeconds = int.Parse(
                    argument["--pf4d-native-warmup-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4e-native-warmup-seconds=", StringComparison.Ordinal))
                pf3NativeWarmupSeconds = int.Parse(
                    argument["--pf4e-native-warmup-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4f-native-warmup-seconds=", StringComparison.Ordinal))
                pf3NativeWarmupSeconds = int.Parse(
                    argument["--pf4f-native-warmup-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf5-native-warmup-seconds=", StringComparison.Ordinal))
                pf3NativeWarmupSeconds = int.Parse(
                    argument["--pf5-native-warmup-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf3-native-measurement-seconds=", StringComparison.Ordinal))
                pf3NativeMeasurementSeconds = int.Parse(
                    argument["--pf3-native-measurement-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4a-native-measurement-seconds=", StringComparison.Ordinal))
                pf3NativeMeasurementSeconds = int.Parse(
                    argument["--pf4a-native-measurement-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4b-native-measurement-seconds=", StringComparison.Ordinal))
                pf3NativeMeasurementSeconds = int.Parse(
                    argument["--pf4b-native-measurement-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4c-native-measurement-seconds=", StringComparison.Ordinal))
                pf3NativeMeasurementSeconds = int.Parse(
                    argument["--pf4c-native-measurement-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4d-native-measurement-seconds=", StringComparison.Ordinal))
                pf3NativeMeasurementSeconds = int.Parse(
                    argument["--pf4d-native-measurement-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4e-native-measurement-seconds=", StringComparison.Ordinal))
                pf3NativeMeasurementSeconds = int.Parse(
                    argument["--pf4e-native-measurement-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf4f-native-measurement-seconds=", StringComparison.Ordinal))
                pf3NativeMeasurementSeconds = int.Parse(
                    argument["--pf4f-native-measurement-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf5-native-measurement-seconds=", StringComparison.Ordinal))
                pf3NativeMeasurementSeconds = int.Parse(
                    argument["--pf5-native-measurement-seconds=".Length..],
                    System.Globalization.CultureInfo.InvariantCulture);
            else if (argument.StartsWith("--pf3-native-evidence-output=", StringComparison.Ordinal))
                pf3NativeEvidenceOutputDirectory =
                    argument["--pf3-native-evidence-output=".Length..];
            else if (argument.StartsWith("--pf4a-native-evidence-output=", StringComparison.Ordinal))
                pf3NativeEvidenceOutputDirectory =
                    argument["--pf4a-native-evidence-output=".Length..];
            else if (argument.StartsWith("--pf4b-native-evidence-output=", StringComparison.Ordinal))
                pf3NativeEvidenceOutputDirectory =
                    argument["--pf4b-native-evidence-output=".Length..];
            else if (argument.StartsWith("--pf4c-native-evidence-output=", StringComparison.Ordinal))
                pf3NativeEvidenceOutputDirectory =
                    argument["--pf4c-native-evidence-output=".Length..];
            else if (argument.StartsWith("--pf4d-native-evidence-output=", StringComparison.Ordinal))
                pf3NativeEvidenceOutputDirectory =
                    argument["--pf4d-native-evidence-output=".Length..];
            else if (argument.StartsWith("--pf4e-native-evidence-output=", StringComparison.Ordinal))
                pf3NativeEvidenceOutputDirectory =
                    argument["--pf4e-native-evidence-output=".Length..];
            else if (argument.StartsWith("--pf4f-native-evidence-output=", StringComparison.Ordinal))
                pf3NativeEvidenceOutputDirectory =
                    argument["--pf4f-native-evidence-output=".Length..];
            else if (argument.StartsWith("--pf5-native-evidence-output=", StringComparison.Ordinal))
                pf3NativeEvidenceOutputDirectory =
                    argument["--pf5-native-evidence-output=".Length..];
            else if (argument.StartsWith("--pf3-native-a=", StringComparison.Ordinal))
                pf3NativeAliceRenderMode = argument["--pf3-native-a=".Length..];
            else if (argument.StartsWith("--pf3-native-b=", StringComparison.Ordinal))
                pf3NativeBobRenderMode = argument["--pf3-native-b=".Length..];
            else throw new ArgumentException($"Unknown verification option '{argument}'.");
        }
        if (configuration is not ("Debug" or "Release"))
            throw new ArgumentException("Verification configuration must be Debug or Release.");
        if ((f17Only || f112Only || f112MixedOnly || f112R1Only || pf1Only || pf2Only || pf25Only || pf3Only || pf3NativeOnly || pf4aOnly ||
             pf4bOnly || pf4cOnly || pf4dOnly || pf4eOnly || pf4eR1Only || pf4eR2Only ||
             pf4fOnly || pf5Only || pf5R1Only || pf5R2Only || pf5R2R1Only ||
             pf5R2R2Only || pf5R3Only || pf5R4Only || pf5R5Only || pf5R6Only ||
             pf5R7Only || pf5R8Only || pf5R9Only || pf5R12Only) &&
            architectureOnly)
            throw new ArgumentException("Focused PF verification cannot be architecture-only.");
        if ((f17Only ? 1 : 0) + (f112Only ? 1 : 0) + (f112MixedOnly ? 1 : 0) + (f112R1Only ? 1 : 0) + (pf1Only ? 1 : 0) + (pf2Only ? 1 : 0) + (pf25Only ? 1 : 0) +
            (pf3Only ? 1 : 0) + (pf3NativeOnly ? 1 : 0) + (pf4aOnly ? 1 : 0) +
            (pf4bOnly ? 1 : 0) + (pf4cOnly ? 1 : 0) + (pf4dOnly ? 1 : 0) +
            (pf4eOnly ? 1 : 0) + (pf4eR1Only ? 1 : 0) + (pf4eR2Only ? 1 : 0) +
            (pf4fOnly ? 1 : 0) + (pf5Only ? 1 : 0) + (pf5R1Only ? 1 : 0) +
            (pf5R2Only ? 1 : 0) + (pf5R2R1Only ? 1 : 0) +
            (pf5R2R2Only ? 1 : 0) + (pf5R3Only ? 1 : 0) +
            (pf5R4Only ? 1 : 0) + (pf5R5Only ? 1 : 0) + (pf5R6Only ? 1 : 0) +
            (pf5R7Only ? 1 : 0) + (pf5R8Only ? 1 : 0) +
            (pf5R9Only ? 1 : 0) + (pf5R12Only ? 1 : 0) > 1)
            throw new ArgumentException("Choose one focused PF verification mode.");
        if ((pf25LongNative || pf25EvidenceOutputDirectory is not null) && !pf25Only)
            throw new ArgumentException(
                "PF2.5 long-native/evidence options require --pf2.5-only.");
        if (pf3NativeWarmupSeconds < 0 || pf3NativeWarmupSeconds > 3_600 ||
            pf3NativeMeasurementSeconds < 1 || pf3NativeMeasurementSeconds > 86_400)
            throw new ArgumentException(
                "PF3 native warmup must be 0..3600 seconds and measurement must be 1..86400 seconds.");
        string[] nativeModes =
        [
            "headless", "visible-default", "visible-vsync-off-unlimited",
            "visible-vsync-off-30", "visible-vsync-off-60", "visible-vsync-off-120",
            "visible-vsync-off-240", "visible-vsync-on-60",
        ];
        if (!nativeModes.Contains(pf3NativeAliceRenderMode, StringComparer.Ordinal) ||
            !nativeModes.Contains(pf3NativeBobRenderMode, StringComparer.Ordinal))
            throw new ArgumentException("Unknown PF3 native render mode.");
        bool hasPf3NativeOption = pf3NativeWarmupSeconds != 5 ||
            pf3NativeMeasurementSeconds != 20 || pf3NativeEvidenceOutputDirectory is not null ||
            pf3NativeAliceRenderMode != "headless" || pf3NativeBobRenderMode != "headless";
        if (hasPf3NativeOption && !pf3Only && !pf3NativeOnly && !pf4aOnly && !pf4bOnly &&
            !pf4cOnly && !pf4dOnly && !pf4eOnly && !pf4fOnly && !pf5Only)
            throw new ArgumentException(
                "Native PF options require --pf3-only, --pf3-native-only, --pf4a-only, --pf4b-only, --pf4c-only, --pf4d-only, --pf4e-only, --pf4f-only, or --pf5-only.");
        if ((f17Only || f112Only || f112MixedOnly || f112R1Only) && skipGodot)
            throw new ArgumentException("Focused F1 verification requires the native Godot acceptance path.");
return new(architectureOnly, f17Only, f112Only, f112MixedOnly, f112R1Only, pf1Only, pf2Only, pf25Only, pf4aOnly, pf4bOnly, pf4cOnly, pf4dOnly, pf4eOnly, pf4eR1Only, pf4eR2Only, pf4fOnly, pf5Only, pf5R1Only, pf5R2Only, pf5R2R1Only, pf5R2R2Only, pf5R3Only, pf5R4Only, pf5R5Only, pf5R6Only, pf5R7Only, pf5R8Only,
            pf5R9Only, pf5R12Only, pf3Only, pf3NativeOnly,
            pf25LongNative,
            allConfigurations, configuration, skipGodot, noRestore, godot,
            pf25EvidenceOutputDirectory, pf3NativeWarmupSeconds,
            pf3NativeMeasurementSeconds, pf3NativeEvidenceOutputDirectory,
            pf3NativeAliceRenderMode, pf3NativeBobRenderMode);
    }
}

internal sealed record ClientCompositionGate(
    string Name,
    string[] RequiredAssemblies,
    string[] ForbiddenAssemblies);

internal sealed record NativeDeploymentReady(
    string Endpoint,
    string Realm,
    string Fingerprint,
    string CatalogFingerprint,
    string WorldPackFingerprint,
    long ArchiveBytes,
    long ActorPresentationBytes,
    int WorldChunks,
    string ClientProfile,
    ulong ClientProfileRevision,
    string ClientProfileHash,
    ulong ServerPerformanceProfileRevision,
    string ServerPerformanceProfileHash,
    int SimulationFrequencyHz,
    int MaximumNetworkServiceIntervalMicroseconds,
    int MaximumInputHz,
    int RecommendedInputHz,
    int InputBurstCapacity,
    int InputBurstWindowMilliseconds,
    int MaximumParticipants,
    ulong ServerGeneration,
    string StructuralManifestFingerprint);

internal sealed record ContentlessOnlineExport(
    string Scope,
    string Executable);

internal sealed record FinalClientProductExports(
    string SinglePlayerExecutable,
    string CompleteExecutable);

internal sealed record NetworkFlavorProbeEvidence(
    string AssemblyName,
    string Flavor,
    long AssemblyBytes,
    ushort ProtocolVersion,
    ulong ProtocolFingerprint,
    string CanonicalPacketHex,
    string[] AllTypes,
    string[] PublicApi);

internal sealed record Pf3Case(
    string Name,
    int SampleHz,
    int SubmitHz,
    int AuthorityHz,
    int MaximumInputHz,
    int RecommendedInputHz,
    string InputMode,
    bool ExpectRateLimit,
    int InputBurstCapacity = 10,
    int InputBurstWindowMilliseconds = 100);

/// <summary>
/// External userspace UDP path used only by repository verification. It remains transparent
/// through admission, then applies bounded deterministic loss and reorder to the actual exported
/// client without linking production code to Network.Testing.
/// </summary>
internal sealed class ExternalUdpImpairmentProxy : IDisposable
{
    internal const int DropPeriod = 53;
    internal const int ReorderPeriod = 37;
    internal const int DefaultMaximumHoldMilliseconds = 20;

    private readonly Socket _socket;
    private readonly IPEndPoint _server;
    private readonly Thread _thread;
    private readonly int _dropPeriod;
    private readonly int _reorderPeriod;
    private readonly int _maximumHoldMilliseconds;
    private readonly byte[] _receive = new byte[65_535];
    private IPEndPoint? _client;
    private HeldDatagram? _clientToServerHeld;
    private HeldDatagram? _serverToClientHeld;
    private long _clientToServerSequence;
    private long _serverToClientSequence;
    private long _droppedDatagrams;
    private long _reorderedDatagrams;
    private volatile bool _enabled;
    private volatile bool _disposed;

    internal ExternalUdpImpairmentProxy(
        IPEndPoint server,
        int dropPeriod = DropPeriod,
        int reorderPeriod = ReorderPeriod,
        int maximumHoldMilliseconds = DefaultMaximumHoldMilliseconds)
    {
        if (dropPeriod < 2 || reorderPeriod < 2 || maximumHoldMilliseconds is < 1 or > 1_000)
            throw new ArgumentOutOfRangeException(nameof(dropPeriod));
        _server = server;
        _dropPeriod = dropPeriod;
        _reorderPeriod = reorderPeriod;
        _maximumHoldMilliseconds = maximumHoldMilliseconds;
        _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
        {
            ReceiveTimeout = 10,
        };
        _socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        Endpoint = ((IPEndPoint)_socket.LocalEndPoint!).ToString();
        _thread = new Thread(Run)
        {
            IsBackground = true,
            Name = "UltraSim PF4D external UDP impairment",
        };
        _thread.Start();
    }

    internal string Endpoint { get; }
    internal int ConfiguredDropPeriod => _dropPeriod;
    internal int ConfiguredReorderPeriod => _reorderPeriod;
    internal int ConfiguredMaximumHoldMilliseconds => _maximumHoldMilliseconds;
    internal long DroppedDatagrams => Interlocked.Read(ref _droppedDatagrams);
    internal long ReorderedDatagrams => Interlocked.Read(ref _reorderedDatagrams);
    internal void EnableImpairment() => _enabled = true;

    private void Run()
    {
        while (!_disposed)
        {
            FlushExpired();
            EndPoint remote = new IPEndPoint(IPAddress.Any, 0);
            try
            {
                int length = _socket.ReceiveFrom(
                    _receive, 0, _receive.Length, SocketFlags.None, ref remote);
                if (length <= 0 || remote is not IPEndPoint source) continue;
                bool fromServer = source.Address.Equals(_server.Address) &&
                    source.Port == _server.Port;
                IPEndPoint? target;
                if (fromServer)
                {
                    target = _client;
                }
                else
                {
                    _client = new IPEndPoint(source.Address, source.Port);
                    target = _server;
                }
                if (target is null) continue;
                if (!_enabled)
                {
                    Send(_receive.AsSpan(0, length), target);
                    continue;
                }
                Process(_receive.AsSpan(0, length), target, fromServer);
            }
            catch (SocketException exception) when (
                exception.SocketErrorCode is SocketError.TimedOut or SocketError.WouldBlock)
            {
            }
            catch (SocketException exception) when (
                _disposed && exception.SocketErrorCode is
                    SocketError.Interrupted or SocketError.OperationAborted)
            {
                return;
            }
            catch (ObjectDisposedException) when (_disposed)
            {
                return;
            }
        }
    }

    private void Process(ReadOnlySpan<byte> payload, IPEndPoint target, bool fromServer)
    {
        long sequence = fromServer
            ? ++_serverToClientSequence
            : ++_clientToServerSequence;
        if (sequence % _dropPeriod == 0)
        {
            Interlocked.Increment(ref _droppedDatagrams);
            return;
        }

        ref HeldDatagram? held = ref (fromServer
            ? ref _serverToClientHeld
            : ref _clientToServerHeld);
        if (held is not null)
        {
            Send(payload, target);
            Send(held.Payload, held.Target);
            held = null;
            Interlocked.Increment(ref _reorderedDatagrams);
            return;
        }
        if (sequence % _reorderPeriod == 0)
        {
            held = new HeldDatagram(
                payload.ToArray(), new IPEndPoint(target.Address, target.Port),
                Environment.TickCount64);
            return;
        }
        Send(payload, target);
    }

    private void FlushExpired()
    {
        long now = Environment.TickCount64;
        Flush(ref _clientToServerHeld, now);
        Flush(ref _serverToClientHeld, now);
    }

    private void Flush(ref HeldDatagram? held, long now)
    {
        if (held is null || now - held.StartedMilliseconds < _maximumHoldMilliseconds) return;
        Send(held.Payload, held.Target);
        held = null;
    }

    private void Send(ReadOnlySpan<byte> payload, IPEndPoint target)
    {
        try { _socket.SendTo(payload, SocketFlags.None, target); }
        catch (SocketException exception) when (
            _disposed && exception.SocketErrorCode is
                SocketError.Interrupted or SocketError.OperationAborted) { }
        catch (ObjectDisposedException) when (_disposed) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _socket.Dispose();
        if (!_thread.Join(2_000))
            throw new InvalidOperationException("PF4D UDP impairment proxy did not retire.");
    }

    private sealed record HeldDatagram(
        byte[] Payload,
        IPEndPoint Target,
        long StartedMilliseconds);
}
