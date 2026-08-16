using System.Diagnostics;
using System.Globalization;
using System.Text;
using UltraSim.Network.Comparison;

RunnerOptions options = RunnerOptions.Parse(args);
string repository = FindRepositoryRoot();
Directory.CreateDirectory(options.OutputDirectory);
string v2Project = Path.Combine(repository, "_Benchmarks", "UltraSim.Network.V2ComparisonAdapter",
    "UltraSim.Network.V2ComparisonAdapter.csproj");
string v3Project = Path.Combine(repository, "_Benchmarks", "UltraSim.Network.V3ComparisonAdapter",
    "UltraSim.Network.V3ComparisonAdapter.csproj");
var runs = new List<NetworkBotJoinedReport>();

foreach (int clients in options.ClientStages)
{
    bool[] orders = options.BothOrders ? [false, true] : [options.V3First];
    foreach (bool v3First in orders)
    {
        string orderName = v3First ? "v3-first" : "v2-first";
        string runDirectory = Path.Combine(options.OutputDirectory,
            $"clients-{clients.ToString(CultureInfo.InvariantCulture)}", orderName);
        Directory.CreateDirectory(runDirectory);
        string v2Path = Path.Combine(runDirectory, "v2-network-bot.json");
        string v3Path = Path.Combine(runDirectory, "v3-network-bot.json");
        AdapterProcessObservation v2Observation = default;
        AdapterProcessObservation v3Observation = default;
        if (!options.UseExisting)
        {
            if (v3First)
            {
                v3Observation = RunAdapter(v3Project, v3Path, false, clients);
                v2Observation = RunAdapter(v2Project, v2Path, true, clients);
            }
            else
            {
                v2Observation = RunAdapter(v2Project, v2Path, true, clients);
                v3Observation = RunAdapter(v3Project, v3Path, false, clients);
            }
        }

        NetworkBotAdapterReport v2 = NetworkBotComparisonIo.Read<NetworkBotAdapterReport>(v2Path);
        NetworkBotAdapterReport v3 = NetworkBotComparisonIo.Read<NetworkBotAdapterReport>(v3Path);
        if (!options.UseExisting)
        {
            v2 = ApplyProcessObservation(v2, v2Observation);
            v3 = ApplyProcessObservation(v3, v3Observation);
            NetworkBotComparisonIo.Write(v2Path, v2);
            NetworkBotComparisonIo.Write(v3Path, v3);
        }
        ValidateCatalog(v2.Result.Scenario, v3.Result.Scenario);
        NetworkBotJoinedReport joined = Join(v2, v3, v3First);
        runs.Add(joined);
        NetworkBotComparisonIo.Write(Path.Combine(runDirectory, "network-v2-v3-bot-comparison.json"), joined);
        File.WriteAllText(Path.Combine(runDirectory, "network-v2-v3-bot-comparison.md"),
            BuildRunMarkdown(joined));
        Console.WriteLine($"Canonical owner state ({clients}, {orderName}): " +
            (joined.CanonicalStateMatches ? "MATCH" : "MISMATCH"));
    }
}

var matrix = new NetworkBotMatrixReport(
    NetworkBotAdapterReport.Protocol,
    DateTimeOffset.UtcNow,
    options.Scenario,
    options.ClientStages,
    options.BothOrders,
    runs,
    CalculateScaleDeltas(runs),
    runs.All(run => run.CanonicalStateMatches));
string json = Path.Combine(options.OutputDirectory, "network-v2-v3-bot-comparison-matrix.json");
string markdown = Path.Combine(options.OutputDirectory, "network-v2-v3-bot-comparison-matrix.md");
NetworkBotComparisonIo.Write(json, matrix);
File.WriteAllText(markdown, BuildMatrixMarkdown(matrix));
Console.WriteLine(Path.GetFullPath(markdown));
if (!matrix.AllCanonicalStatesMatch) Environment.ExitCode = 2;

NetworkBotJoinedReport Join(
    NetworkBotAdapterReport v2,
    NetworkBotAdapterReport v3,
    bool v3First)
{
    NetworkComparisonDecision decision = NetworkComparisonContract.Evaluate(
        v2.Result.EvidenceProfile, v3.Result.EvidenceProfile,
        NetworkComparisonClass.Provisional);
    bool canonical = StringComparer.Ordinal.Equals(
        v2.Result.CanonicalOwnerStateHash, v3.Result.CanonicalOwnerStateHash);
    bool ratios = decision.MayReportHeadlineRatio && canonical;
    return new(
        NetworkBotAdapterReport.Protocol,
        DateTimeOffset.UtcNow,
        v3First ? "V3-then-V2" : "V2-then-V3",
        decision,
        canonical,
        canonical
            ? "Exact normalized owner identity, acknowledged input sequence, and velocity matched."
            : $"Mismatch: V2={v2.Result.CanonicalOwnerStateHash}, V3={v3.Result.CanonicalOwnerStateHash}",
        v2,
        v3,
        ratios ? Ratio(v3.Result.TickMilliseconds.P95, v2.Result.TickMilliseconds.P95) : null,
        ratios ? Ratio(v3.Result.PingMilliseconds.P95, v2.Result.PingMilliseconds.P95) : null,
        ratios ? Ratio(v3.Result.WireBytesSent, v2.Result.WireBytesSent) : null,
        ratios ? Ratio(v3.Result.AllocatedBytes, v2.Result.AllocatedBytes) : null,
        ratios ? Ratio(v3.Result.ReservedNetworkBytes, v2.Result.ReservedNetworkBytes) : null);
}

AdapterProcessObservation RunAdapter(string project, string output, bool v2Adapter, int clients)
{
    var start = new ProcessStartInfo("dotnet")
    {
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        WorkingDirectory = repository,
    };
    start.Environment["DOTNET_TieredCompilation"] = "0";
    start.Environment["DOTNET_ReadyToRun"] = "0";
    start.ArgumentList.Add("run");
    start.ArgumentList.Add("--project");
    start.ArgumentList.Add(project);
    start.ArgumentList.Add("-c");
    start.ArgumentList.Add("Release");
    if (options.NoBuild) start.ArgumentList.Add("--no-build");
    start.ArgumentList.Add("--");
    Add($"--output={Path.GetFullPath(output)}");
    Add($"--scenario={options.Scenario}");
    Add($"--clients={clients}");
    Add($"--tick-hz={options.TickHz}");
    Add($"--input-hz={options.InputHz}");
    Add($"--stage-ticks={options.StageTicks}");
    Add($"--settle-ticks={options.SettleTicks}");
    Add($"--spawn-spacing={options.SpawnSpacing.ToString("R", CultureInfo.InvariantCulture)}");
    if (!v2Adapter) Add($"--topology={options.V3Topology}");
    if (v2Adapter && options.NoBuildV2) Add("--no-build-v2");

    Console.WriteLine($"Running {Path.GetFileNameWithoutExtension(project)} ({clients})...");
    using Process process = Process.Start(start)
        ?? throw new InvalidOperationException($"Could not start '{project}'.");
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
        catch (InvalidOperationException) when (process.HasExited) { break; }
    }
    string standardOutput = outputTask.GetAwaiter().GetResult();
    string standardError = errorTask.GetAwaiter().GetResult();
    Console.Write(standardOutput);
    if (!string.IsNullOrWhiteSpace(standardError)) Console.Error.Write(standardError);
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"Adapter failed ({process.ExitCode}): {project}");
    return new(peakWorkingSet, peakPrivateBytes);

    void Add(string value) => start.ArgumentList.Add(value);
}

static NetworkBotAdapterReport ApplyProcessObservation(
    NetworkBotAdapterReport report,
    AdapterProcessObservation observation) => report with
{
    Result = report.Result with
    {
        WorkingSetBytes = Math.Max(report.Result.WorkingSetBytes, observation.PeakWorkingSetBytes),
        PrivateBytes = Math.Max(report.Result.PrivateBytes, observation.PeakPrivateBytes),
    },
};

static void ValidateCatalog(NetworkBotScenario left, NetworkBotScenario right)
{
    left.Validate();
    right.Validate();
    if (left != right)
        throw new InvalidOperationException($"Adapter workload mismatch: V2={left}; V3={right}.");
}

static double? Ratio(double numerator, double denominator) =>
    numerator > 0 && denominator > 0 ? numerator / denominator : null;

static IReadOnlyList<NetworkBotScaleDelta> CalculateScaleDeltas(
    IReadOnlyList<NetworkBotJoinedReport> runs)
{
    var stages = runs
        .GroupBy(run => run.V2.Result.Scenario.Clients)
        .OrderBy(group => group.Key)
        .Select(group => new
        {
            Clients = group.Key,
            V2 = group.Average(run => run.V2.Result.TickMilliseconds.P95),
            V3 = group.Average(run => run.V3.Result.TickMilliseconds.P95),
        })
        .ToArray();
    var deltas = new NetworkBotScaleDelta[Math.Max(0, stages.Length - 1)];
    for (int index = 1; index < stages.Length; index++)
    {
        int added = stages[index].Clients - stages[index - 1].Clients;
        deltas[index - 1] = new(
            stages[index - 1].Clients,
            stages[index].Clients,
            (stages[index].V2 - stages[index - 1].V2) * 1000d / added,
            (stages[index].V3 - stages[index - 1].V3) * 1000d / added);
    }
    return deltas;
}

static string BuildRunMarkdown(NetworkBotJoinedReport report)
{
    NetworkBotStageResult v2 = report.V2.Result;
    NetworkBotStageResult v3 = report.V3.Result;
    var text = new StringBuilder();
    text.AppendLine("# Network V2/V3 bot comparison").AppendLine();
    text.AppendLine($"- Workload: `{v2.Scenario.Id}` / {v2.Scenario.Clients} bots");
    text.AppendLine($"- Capture order: {report.CaptureOrder}");
    text.AppendLine($"- Classification: **{report.Decision.Classification}**");
    text.AppendLine($"- Canonical owner state: **{(report.CanonicalStateMatches ? "MATCH" : "MISMATCH")}**");
    text.AppendLine($"- Reason: {report.Decision.Reason}").AppendLine();
    text.AppendLine($"The ratio is provisional because V2 and V3 use the compared transport topologies recorded in their evidence profiles (V3: {v3.EvidenceProfile.ExecutionTopology}). Position remains diagnostic because the generations integrate current input differently. Internal capacities and reservations are reported, not normalized away.").AppendLine();
    text.AppendLine("| Metric | V2 | V3 | V3/V2 |");
    text.AppendLine("| --- | ---: | ---: | ---: |");
    Row("ready clients", v2.ReadyClients, v3.ReadyClients, null);
    Row("tick p95 ms", v2.TickMilliseconds.P95, v3.TickMilliseconds.P95, report.V3ToV2TickP95Ratio);
    Row("ping p95 ms", v2.PingMilliseconds.P95, v3.PingMilliseconds.P95, report.V3ToV2PingP95Ratio);
    Row("input ACK p95 ms", v2.InputAcknowledgementMilliseconds.P95, v3.InputAcknowledgementMilliseconds.P95, null);
    Row("input ACK p95 ticks", v2.InputAcknowledgementTicks.P95, v3.InputAcknowledgementTicks.P95, null);
    Row("worst-client input ACK p95 ms", v2.PerClientInputAckP95Milliseconds.Max,
        v3.PerClientInputAckP95Milliseconds.Max, null);
    Row("deadline misses", v2.TickDeadlineMisses, v3.TickDeadlineMisses, null);
    Row("allocated bytes", v2.AllocatedBytes, v3.AllocatedBytes, report.V3ToV2AllocatedBytesRatio);
    Row("reserved network bytes", v2.ReservedNetworkBytes, v3.ReservedNetworkBytes, report.V3ToV2ReservedBytesRatio);
    Row("configured socket bytes", v2.ConfiguredSocketBufferBytes, v3.ConfiguredSocketBufferBytes, null);
    Row("wire datagrams", v2.WireDatagramsSent, v3.WireDatagramsSent, null);
    Row("wire bytes", v2.WireBytesSent, v3.WireBytesSent, report.V3ToV2WireBytesRatio);
    Row("retransmitted wire bytes", v2.RetransmittedWireBytes, v3.RetransmittedWireBytes, null);
    Row("socket send calls", v2.SocketSendCalls, v3.SocketSendCalls, null);
    Row("handshake p95 ms", v2.HandshakeMilliseconds.P95, v3.HandshakeMilliseconds.P95, null);
    Row("max reliable occupancy %", v2.ReliableOccupancy.MaximumRatio * 100,
        v3.ReliableOccupancy.MaximumRatio * 100, null);
    Row("max receive occupancy %", v2.ReceivedOccupancy.MaximumRatio * 100,
        v3.ReceivedOccupancy.MaximumRatio * 100, null);
    Row("process CPU core-equivalents", v2.ProcessCpuCoreUtilization,
        v3.ProcessCpuCoreUtilization, null);
    Row("normalized logical application bytes",
        Diagnostic(v2, "normalized_logical_application_bytes"),
        Diagnostic(v3, "normalized_logical_application_bytes"), null);
    Row("wire / normalized logical byte", 0,
        Diagnostic(v3, "wire_bytes_per_normalized_logical_byte"), null);
    text.AppendLine().AppendLine("Inspect each JSON `metric_coverage` object before interpreting zero: false means the adapter/workload did not measure that family. Frozen V2's complete numeric stage output is retained under `diagnostics.v2.*`.");
    return text.ToString();

    void Row(string name, double left, double right, double? ratio) => text.Append("| ")
        .Append(name).Append(" | ").Append(left.ToString("0.###", CultureInfo.InvariantCulture))
        .Append(" | ").Append(right.ToString("0.###", CultureInfo.InvariantCulture))
        .Append(" | ").Append(ratio?.ToString("0.###x", CultureInfo.InvariantCulture) ?? "—")
        .AppendLine(" |");

    static double Diagnostic(NetworkBotStageResult result, string key) =>
        result.Diagnostics.TryGetValue(key, out double value) ? value : 0;
}

static string BuildMatrixMarkdown(NetworkBotMatrixReport report)
{
    var text = new StringBuilder();
    text.AppendLine("# Network V2/V3 bot comparison matrix").AppendLine();
    text.AppendLine($"- Protocol: `{report.ProtocolVersion}`");
    text.AppendLine($"- Scenario: `{report.Scenario}`");
    text.AppendLine($"- Both process orders: {report.BothProcessOrders}");
    text.AppendLine($"- All canonical results: **{(report.AllCanonicalStatesMatch ? "MATCH" : "MISMATCH")}**").AppendLine();
    text.AppendLine("| Bots | Order | V2 tick p95 ms | V3 tick p95 ms | V3/V2 | V2 ping p95 ms | V3 ping p95 ms | Canonical |");
    text.AppendLine("| ---: | --- | ---: | ---: | ---: | ---: | ---: | --- |");
    foreach (NetworkBotJoinedReport run in report.Runs)
    {
        NetworkBotStageResult v2 = run.V2.Result;
        NetworkBotStageResult v3 = run.V3.Result;
        text.Append("| ").Append(v2.Scenario.Clients).Append(" | ").Append(run.CaptureOrder)
            .Append(" | ").Append(v2.TickMilliseconds.P95.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(" | ").Append(v3.TickMilliseconds.P95.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(" | ").Append(run.V3ToV2TickP95Ratio?.ToString("0.###x", CultureInfo.InvariantCulture) ?? "—")
            .Append(" | ").Append(v2.PingMilliseconds.P95.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(" | ").Append(v3.PingMilliseconds.P95.ToString("0.###", CultureInfo.InvariantCulture))
            .Append(" | ").Append(run.CanonicalStateMatches ? "MATCH" : "MISMATCH").AppendLine(" |");
    }
    if (report.MarginalTickCosts.Count != 0)
    {
        text.AppendLine().AppendLine("## Marginal p95 tick cost").AppendLine();
        text.AppendLine("| Population step | V2 us/added client | V3 us/added client |");
        text.AppendLine("| --- | ---: | ---: |");
        foreach (NetworkBotScaleDelta delta in report.MarginalTickCosts)
            text.Append("| ").Append(delta.FromClients).Append(" → ").Append(delta.ToClients)
                .Append(" | ").Append(delta.V2MicrosecondsPerAddedClient.ToString("0.###", CultureInfo.InvariantCulture))
                .Append(" | ").Append(delta.V3MicrosecondsPerAddedClient.ToString("0.###", CultureInfo.InvariantCulture))
                .AppendLine(" |");
    }
    string v3Topologies = string.Join("; ", report.Runs
        .Select(run => run.V3.Result.EvidenceProfile.ExecutionTopology)
        .Distinct(StringComparer.Ordinal));
    text.AppendLine().AppendLine($"All ratios are **provisional topology evidence**. V2 uses one shared server socket. V3 evidence: {v3Topologies}. The matrix is an N0.10 input, not a direct production-topology verdict.");
    return text.ToString();
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

internal sealed record RunnerOptions(
    string OutputDirectory,
    string Scenario,
    IReadOnlyList<int> ClientStages,
    int TickHz,
    int InputHz,
    int StageTicks,
    int SettleTicks,
    double SpawnSpacing,
    bool BothOrders,
    bool V3First,
    bool UseExisting,
    bool NoBuild,
    bool NoBuildV2,
    string V3Topology)
{
    internal static RunnerOptions Parse(string[] args)
    {
        IReadOnlyList<int> stages = ParseStages(Get(args, "--clients=") ?? "32,64,128,256");
        return new(
            Path.GetFullPath(Get(args, "--output-dir=") ??
                Path.Combine("_Benchmarks", "results", "network-v2-v3-bot")),
            Get(args, "--scenario=") ?? "owner_movement",
            stages,
            ParseInt(Get(args, "--tick-hz="), 60),
            ParseInt(Get(args, "--input-hz="), 20),
            ParseInt(Get(args, "--stage-ticks="), 120),
            ParseInt(Get(args, "--settle-ticks="), 30),
            ParseDouble(Get(args, "--spawn-spacing="), 12),
            !Has(args, "--single-order"),
            Has(args, "--v3-first"),
            Has(args, "--use-existing"),
            Has(args, "--no-build"),
            Has(args, "--no-build-v2"),
            ParseV3Topology(Get(args, "--v3-topology=")));
    }

    private static IReadOnlyList<int> ParseStages(string value)
    {
        int[] stages = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => int.Parse(part, NumberStyles.Integer, CultureInfo.InvariantCulture))
            .ToArray();
        if (stages.Length == 0 || stages.Any(stage => stage <= 0) || stages.Distinct().Count() != stages.Length)
            throw new ArgumentException("Client stages must be unique positive integers.", nameof(value));
        return stages;
    }

    private static bool Has(string[] args, string value) =>
        args.Contains(value, StringComparer.OrdinalIgnoreCase);
    private static string? Get(string[] args, string prefix) =>
        args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];
    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : fallback;
    private static double ParseDouble(string? value, double fallback) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
    private static string ParseV3Topology(string? value) => value?.ToLowerInvariant() switch
    {
        null or "connected-pairs" => "connected-pairs",
        "shared-listener" => "shared-listener",
        _ => throw new ArgumentException($"Unsupported V3 UDP topology '{value}'."),
    };
}

internal readonly record struct AdapterProcessObservation(
    long PeakWorkingSetBytes,
    long PeakPrivateBytes);
