using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using UltraSim.Network.Comparison;

AdapterOptions options = AdapterOptions.Parse(args);
NetworkBotScenario scenario = options.CreateScenario();
string repository = FindRepositoryRoot();
string v2Project = Path.Combine(repository, "_V2", "_Benchmarks",
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
        WorkingDirectory = Path.Combine(repository, "_V2"),
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
    Add("--active-ratio", "1");
    Add("--input-hz", scenario.InputHz.ToString(CultureInfo.InvariantCulture));
    start.ArgumentList.Add("--phase-input-sends");
    start.ArgumentList.Add("--phase-ping-probes");
    start.ArgumentList.Add("--disable-async-server-flush");
    Add("--ping-interval-s", "1");
    Add("--stop-on", "none");
    Add("--spawn-spacing-m", scenario.SpawnSpacingMeters.ToString("R", CultureInfo.InvariantCulture));
    Add("--spawn-grid-width", "32");
    Add("--report-dir", temporary);
    if (scenario.ObserverState) start.ArgumentList.Add("--observer-replication");
    if (scenario.OrderedEvents) start.ArgumentList.Add("--observer-events");

    using Process process = Process.Start(start)
        ?? throw new InvalidOperationException("Could not start the V2 bot harness.");
    Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
    Task<string> errorTask = process.StandardError.ReadToEndAsync();
    long peakWorkingSet = 0;
    while (!process.WaitForExit(10))
    {
        try
        {
            process.Refresh();
            peakWorkingSet = Math.Max(peakWorkingSet, process.WorkingSet64);
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
    var result = new NetworkBotStageResult
    {
        Generation = "V2",
        Scenario = scenario,
        EvidenceProfile = new(
            "network-bot-owner-movement-v1",
            "Deterministic phased owner input, authoritative latest control, owner correction, and ping; arrival-driven position remains diagnostic.",
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
        TickDeadlineMisses = 0,
        LongestDeadlineMissStreak = 0,
        LogicalInputs = logicalInputs,
        OwnerStatesApplied = ownerSamples,
        WorldStateRecordsApplied = stage.GetProperty("observer_frames_applied").GetInt64(),
        ApplicationEventsApplied = stage.GetProperty("observer_events_applied").GetInt64(),
        WireDatagramsSent = 0,
        WireBytesSent = 0,
        RetransmittedPackets = 0,
        ReservedNetworkBytes = 0,
        ConfiguredSocketBufferBytes = checked((long)(scenario.Clients + 1) * 8 * 1024 * 1024),
        ManagedHeapBytes = 0,
        WorkingSetBytes = peakWorkingSet,
        PrivateBytes = 0,
        AllocatedBytes = allocated,
        Gen0Collections = stage.GetProperty("gc_gen0_collections").GetInt32(),
        Gen1Collections = stage.GetProperty("gc_gen1_collections").GetInt32(),
        Gen2Collections = stage.GetProperty("gc_gen2_collections").GetInt32(),
        MaximumReliableQueued = 0,
        MaximumUnreliableQueued = 0,
        MaximumReceivedQueued = 0,
        MaximumInFlightPackets = 0,
        CapacityPressureTicks50 = 0,
        CapacityPressureTicks75 = 0,
        CapacityPressureTicks90 = 0,
        CapacityPressureTicks100 = 0,
        CapacityDrops = 0,
        CanonicalOwnerStateHash = NetworkBotCanonicalState.ComputeHash(canonicalStates),
        CanonicalOwnerStates = canonicalStates,
        CanonicalValidation = "Exact client-applied owner identity, acknowledged input sequence, and velocity. Position remains diagnostic because V2 integrates arrival-relative input.",
        Diagnostics = new Dictionary<string, double>
        {
            ["pending_max_per_client"] = stage.GetProperty("pending_max_per_client").GetDouble(),
            ["replication_logical_datagrams"] = stage.GetProperty("replication_datagrams").GetDouble(),
            ["replication_wire_datagrams"] = stage.GetProperty("replication_wire_datagrams").GetDouble(),
            ["owner_correction_position_error_max"] = stage.GetProperty("owner_correction_max_position_error").GetDouble(),
            ["owner_state_messages_queued"] = ownerMessagesQueued,
            ["configured_socket_bytes_per_client_including_shared_server"] =
                ((long)(scenario.Clients + 1) * 8 * 1024 * 1024) / (double)scenario.Clients,
            ["normalized_logical_application_bytes"] = normalizedLogicalBytes,
        },
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
    long count = 0;
    for (int client = 0; client < scenario.Clients; client++)
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
            Directory.Exists(Path.Combine(current.FullName, "_V2")) &&
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
        args.Contains("--no-build-v2", StringComparer.OrdinalIgnoreCase));

    internal NetworkBotScenario CreateScenario() => new NetworkBotScenario(
        Scenario, Clients, TickHz, InputHz, StageTicks, SettleTicks,
        SpawnSpacing,
        ObserverState: false,
        OrderedEvents: false).Validate();

    private static string? Get(string[] args, string prefix) =>
        args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
}
