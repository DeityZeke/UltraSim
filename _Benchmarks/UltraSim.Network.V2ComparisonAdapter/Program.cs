using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using UltraSim.Network.Comparison;

AdapterOptions options = AdapterOptions.Parse(args);
NetworkBotScenario scenario = options.CreateScenario();
string repository = FindRepositoryRoot();
string v2Root = Path.Combine(repository, "_Archive", "_V2");
string v2Project = Path.Combine(v2Root, "_Benchmarks",
    "UltraSim.Network.BotHarness", "UltraSim.Network.BotHarness.csproj");
string temporary = Path.Combine(Path.GetTempPath(), "ultrasim-network-v2-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(temporary);
try
{
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = v2Root,
    };
    start.Environment["DOTNET_TieredCompilation"] =
        Environment.GetEnvironmentVariable("DOTNET_TieredCompilation") ?? "0";
    start.Environment["DOTNET_ReadyToRun"] =
        Environment.GetEnvironmentVariable("DOTNET_ReadyToRun") ?? "0";
    start.ArgumentList.Add("run");
    start.ArgumentList.Add("--project");
    start.ArgumentList.Add(v2Project);
    start.ArgumentList.Add("-c");
    start.ArgumentList.Add("Release");
    if (options.NoBuild) start.ArgumentList.Add("--no-build");
    start.ArgumentList.Add("--");
    Add("--stages", scenario.Clients.ToString(CultureInfo.InvariantCulture));
    Add("--stage-mode", "independent");
    Add("--stage-duration", (scenario.StageTicks / (double)scenario.TickHz).ToString("R", CultureInfo.InvariantCulture));
    Add("--settle-seconds", (scenario.SettleTicks / (double)scenario.TickHz).ToString("R", CultureInfo.InvariantCulture));
    Add("--tick-hz", scenario.TickHz.ToString(CultureInfo.InvariantCulture));
    Add("--active-ratio", scenario.ActiveRatio.ToString("R", CultureInfo.InvariantCulture));
    Add("--input-hz", scenario.InputHz.ToString(CultureInfo.InvariantCulture));
    start.ArgumentList.Add("--phase-input-sends");
    start.ArgumentList.Add("--phase-ping-probes");
    if (!scenario.ObserverState)
        start.ArgumentList.Add("--disable-async-server-flush");
    Add("--ping-interval-s", "1");
    Add("--stop-on", "none");
    Add("--spawn-spacing-m", scenario.SpawnSpacingMeters.ToString("R", CultureInfo.InvariantCulture));
    Add("--spawn-grid-width", scenario.SpawnGridWidth.ToString(CultureInfo.InvariantCulture));
    Add("--report-dir", temporary);
    if (scenario.ObserverState) start.ArgumentList.Add("--observer-replication");
    if (scenario.OrderedEvents) start.ArgumentList.Add("--observer-events");
    if (scenario.ObserverState)
    {
        Add("--aoi-radii-m", "7.5,23.5,39.5");
        Add("--ring-cadence-ticks", "1,2,3");
        start.ArgumentList.Add("--enable-latest-state-recipient-phasing");
        Add("--latest-state-connection-phasing-cadence-ticks", "2");
        Add("--owner-deadband", "0.10");
        Add("--owner-max-silent-ticks", "8");
    }

    using Process process = Process.Start(start)
        ?? throw new InvalidOperationException("Could not start the V2 bot harness.");
    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
    Task<string> errorTask = process.StandardError.ReadToEndAsync();
    long peakWorkingSet = 0;
    long peakPrivateBytes = 0;
    while (!process.WaitForExit(10))
    {
        try
        {
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
            peakPrivateBytes = Math.Max(peakPrivateBytes, process.PrivateMemorySize64);
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            break;
        }
    }
    string output = outputTask.GetAwaiter().GetResult();
    string error = errorTask.GetAwaiter().GetResult();
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"V2 bot harness failed ({process.ExitCode}): {error}\n{output}");
    string reportPath = Directory.GetFiles(temporary, "network_bot_harness_*.json").Single();
    using JsonDocument document = JsonDocument.Parse(File.ReadAllText(reportPath));
    JsonElement stage = document.RootElement.GetProperty("stages")[0];
    long logicalInputs = CountInputs(scenario);
    long allocated = stage.GetProperty("alloc_bytes").GetInt64();
    long ownerSamples = stage.GetProperty("owner_correction_samples").GetInt64();
    long ownerMessagesQueued = stage.GetProperty("owner_correction_sent").GetInt64();
    long pingSamples = CountPingSamples(stage.GetProperty("per_client_ping"));
    long normalizedLogicalBytes = checked(
        logicalInputs * 10 + ownerMessagesQueued * 30 + pingSamples * 4);
    IReadOnlyList<NetworkCanonicalOwnerState> canonicalStates =
        ParseCanonicalStates(stage.GetProperty("canonical_owner_states"));
    var diagnostics = new Dictionary<string, double>(StringComparer.Ordinal);
    foreach (JsonProperty property in stage.EnumerateObject())
        if (property.Value.ValueKind == JsonValueKind.Number &&
            property.Value.TryGetDouble(out double numeric))
            diagnostics["v2." + property.Name] = numeric;
    diagnostics["pending_max_per_client"] = stage.GetProperty("pending_max_per_client").GetDouble();
    diagnostics["replication_logical_datagrams"] = stage.GetProperty("replication_datagrams").GetDouble();
    diagnostics["replication_wire_datagrams"] = stage.GetProperty("replication_wire_datagrams").GetDouble();
    diagnostics["owner_correction_position_error_max"] =
        stage.GetProperty("owner_correction_max_position_error").GetDouble();
    diagnostics["owner_state_messages_queued"] = ownerMessagesQueued;
    diagnostics["configured_socket_bytes_per_client_including_shared_server"] =
        ((long)(scenario.Clients + 1) * 8 * 1024 * 1024) / (double)scenario.Clients;
    diagnostics["normalized_logical_application_bytes"] = normalizedLogicalBytes;
    var result = new NetworkBotStageResult
    {
        Generation = "V2",
        Scenario = scenario,
        EvidenceProfile = new(
            scenario.ObserverState
                ? "network-bot-sparse-observer-movement-v1"
                : "network-bot-owner-movement-v1",
            scenario.ObserverState
                ? "Historical sparse workload: deterministic phased owner input, one observer latest-state family, three AOI rings, owner correction, and ping; chat and ordered Events disabled."
                : "Deterministic phased owner input, authoritative latest control, owner correction, and ping; arrival-driven position remains diagnostic.",
            "workload bounds fixed; implementation reservations reported separately",
            $"fixed {scenario.TickHz} Hz; {scenario.InputHz} Hz phased input; realtime",
            "one shared server UDP socket with one client socket/session per bot",
            "process-wide allocated bytes and GC collections; physical memory where exposed"),
        ReadyClients = stage.GetProperty("ready_clients").GetInt32(),
        TickMilliseconds = Summary(stage, "tick_avg_ms", "tick_p95_ms", "tick_p99_ms", "tick_max_ms"),
        ServerPumpMilliseconds = Summary(stage, "server_pump_avg_ms", "server_pump_p95_ms"),
        ClientPumpMilliseconds = Summary(stage, "client_pump_avg_ms", "client_pump_p95_ms"),
        PingMilliseconds = Summary(stage, "ping_avg_ms", "ping_p95_ms", "ping_p99_ms", "ping_max_ms"),
        InputAcknowledgementMilliseconds = default,
        InputAcknowledgementTicks = default,
        PerClientInputAckP95Milliseconds = default,
        PerClientPingP95Milliseconds = PerClientPingSummary(stage.GetProperty("per_client_ping")),
        HandshakeMilliseconds = default,
        MetricCoverage = new(
            OwnerInputFreshness: false,
            WorldStateFreshnessByTier: false,
            ApplicationEventLatency: false,
            PerClientFairness: true,
            TickDeadlineCompliance: false,
            BoundedOccupancy: false,
            WireBytes: false,
            SocketSyscalls: false,
            SecurityBytes: false,
            ProcessMemory: true,
            ManagedAllocation: true,
            ProcessCpu: false,
            Handshake: false,
            InterestAmplification: false,
            BaselineRecovery: false,
            DeepPhaseTiming: true),
        TickDeadlineMisses = 0,
        LongestDeadlineMissStreak = 0,
        LogicalInputs = logicalInputs,
        OwnerStatesApplied = ownerSamples,
        WorldStateRecordsApplied = stage.GetProperty("observer_frames_applied").GetInt64(),
        ApplicationEventsApplied = stage.GetProperty("observer_events_applied").GetInt64(),
        WireDatagramsSent = 0,
        WireBytesSent = 0,
        RetransmittedPackets = 0,
        RetransmittedWireBytes = 0,
        ReservedNetworkBytes = 0,
        ConfiguredSocketBufferBytes = checked((long)(scenario.Clients + 1) * 8 * 1024 * 1024),
        ManagedHeapBytes = stage.GetProperty("managed_heap_bytes").GetInt64(),
        WorkingSetBytes = Math.Max(
            stage.GetProperty("working_set_bytes").GetInt64(), peakWorkingSet),
        PrivateBytes = Math.Max(
            stage.GetProperty("private_bytes").GetInt64(), peakPrivateBytes),
        AllocatedBytes = allocated,
        Gen0Collections = stage.GetProperty("gc_gen0_collections").GetInt32(),
        Gen1Collections = stage.GetProperty("gc_gen1_collections").GetInt32(),
        Gen2Collections = stage.GetProperty("gc_gen2_collections").GetInt32(),
        ProcessCpuMilliseconds = 0,
        ProcessCpuCoreUtilization = 0,
        MaximumReliableQueued = 0,
        MaximumUnreliableQueued = 0,
        MaximumReceivedQueued = 0,
        MaximumInFlightPackets = 0,
        ReliableOccupancy = default,
        IndependentUnreliableOccupancy = default,
        ReceivedOccupancy = default,
        InFlightOccupancy = default,
        CapacityPressureTicks50 = 0,
        CapacityPressureTicks75 = 0,
        CapacityPressureTicks90 = 0,
        CapacityPressureTicks100 = 0,
        CapacityDrops = 0,
        HandshakeAttempts = stage.GetProperty("connect_attempts").GetInt64(),
        HandshakeSuccesses = stage.GetProperty("connect_success").GetInt64(),
        HandshakeAllocatedBytes = 0,
        HandshakeWireDatagrams = 0,
        HandshakeWireBytes = 0,
        SocketSendCalls = 0,
        SocketReceivePollCalls = 0,
        SocketReceiveCalls = 0,
        SocketWouldBlockEvents = 0,
        MaximumSentDatagramBytes = 0,
        CanonicalOwnerStateHash = NetworkBotCanonicalState.ComputeHash(canonicalStates),
        CanonicalOwnerStates = canonicalStates,
        CanonicalValidation = scenario.ObserverState
            ? "Exact client-applied owner identity, acknowledged input sequence, and velocity. V2 does not expose a canonical observer-state digest or observer age; observer apply counts remain diagnostic."
            : "Exact client-applied owner identity, acknowledged input sequence, and velocity. Position remains diagnostic because V2 integrates arrival-relative input.",
        Diagnostics = diagnostics,
    };
    NetworkBotComparisonIo.Write(options.Output, NetworkBotAdapterReport.Create("V2", result));
    Console.Write(output);
    Console.WriteLine($"V2 {scenario.Id}/{scenario.Clients}: tick p95 {result.TickMilliseconds.P95:F3} ms, " +
        $"ping p95 {result.PingMilliseconds.P95:F3} ms");
    Console.WriteLine(Path.GetFullPath(options.Output));

    void Add(string name, string value)
    {
        start.ArgumentList.Add(name);
        start.ArgumentList.Add(value);
    }
}
finally
{
    if (Directory.Exists(temporary)) Directory.Delete(temporary, recursive: true);
}

static NetworkMetricSummary Summary(
    JsonElement stage,
    string mean,
    string p95,
    string? p99 = null,
    string? max = null)
{
    double average = stage.GetProperty(mean).GetDouble();
    double percentile95 = stage.GetProperty(p95).GetDouble();
    return new(average, 0, percentile95,
        p99 is null ? 0 : stage.GetProperty(p99).GetDouble(),
        max is null ? percentile95 : stage.GetProperty(max).GetDouble());
}

static NetworkMetricSummary PerClientPingSummary(JsonElement rows)
{
    var values = new List<double>(rows.GetArrayLength());
    foreach (JsonElement row in rows.EnumerateArray())
        if (row.TryGetProperty("sample_count", out JsonElement count) && count.GetInt32() > 0)
            values.Add(row.GetProperty("ping_p95_ms").GetDouble());
    return NetworkMetricSummary.From(values);
}

static IReadOnlyList<NetworkCanonicalOwnerState> ParseCanonicalStates(JsonElement rows)
{
    var result = new NetworkCanonicalOwnerState[rows.GetArrayLength()];
    int index = 0;
    foreach (JsonElement row in rows.EnumerateArray())
    {
        result[index++] = new(
            row.GetProperty("client_index").GetInt32(),
            row.GetProperty("network_entity_id").GetUInt32(),
            row.GetProperty("last_processed_input_sequence").GetUInt16(),
            row.GetProperty("position_x_bits").GetUInt32(),
            row.GetProperty("position_y_bits").GetUInt32(),
            row.GetProperty("velocity_x_bits").GetUInt32(),
            row.GetProperty("velocity_y_bits").GetUInt32());
    }
    return result;
}

static long CountInputs(NetworkBotScenario scenario)
{
    int interval = scenario.TickHz / scenario.InputHz;
    int active = checked((int)Math.Round(
        scenario.Clients * scenario.ActiveRatio, MidpointRounding.AwayFromZero));
    long count = 0;
    for (int client = 0; client < active; client++)
        for (int tick = client % interval; tick < scenario.StageTicks; tick += interval) count++;
    return count;
}

static long CountPingSamples(JsonElement rows)
{
    long count = 0;
    foreach (JsonElement row in rows.EnumerateArray())
        count = checked(count + row.GetProperty("sample_count").GetInt64());
    return count;
}

static string FindRepositoryRoot()
{
    DirectoryInfo? current = new(Environment.CurrentDirectory);
    while (current is not null)
    {
        if (File.Exists(Path.Combine(current.FullName, "README.md")) &&
            Directory.Exists(Path.Combine(current.FullName, "_Archive", "_V2")) &&
            Directory.Exists(Path.Combine(current.FullName, "Kernels", "Network")))
            return current.FullName;
        current = current.Parent;
    }
    throw new DirectoryNotFoundException("Could not locate the UltraSim repository root.");
}

internal sealed record AdapterOptions(
    string Output,
    string Scenario,
    int Clients,
    int TickHz,
    int InputHz,
    int StageTicks,
    int SettleTicks,
    double SpawnSpacing,
    double ActiveRatio,
    int SpawnGridWidth,
    bool NoBuild)
{
    internal static AdapterOptions Parse(string[] args) => new(
        Get(args, "--output=") ?? "v2-network-bot-comparison.json",
        Get(args, "--scenario=") ?? "owner_movement",
        ParseInt(Get(args, "--clients="), 32),
        ParseInt(Get(args, "--tick-hz="), 60),
        ParseInt(Get(args, "--input-hz="), 20),
        ParseInt(Get(args, "--stage-ticks="), 120),
        ParseInt(Get(args, "--settle-ticks="), 30),
        ParseDouble(Get(args, "--spawn-spacing="), 12),
        ParseDouble(Get(args, "--active-ratio="), 1),
        ParseInt(Get(args, "--spawn-grid-width="), 32),
        args.Contains("--no-build-v2", StringComparer.OrdinalIgnoreCase));

    internal NetworkBotScenario CreateScenario() => new NetworkBotScenario(
        Scenario, Clients, TickHz, InputHz, StageTicks, SettleTicks,
        SpawnSpacing,
        ObserverState: StringComparer.Ordinal.Equals(Scenario, "sparse_observer_movement"),
        OrderedEvents: false,
        ActiveRatio,
        SpawnGridWidth).Validate();

    private static string? Get(string[] args, string prefix) =>
        args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
}
