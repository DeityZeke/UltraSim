using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using UltraSim.Client;
using UltraSim.Discovery;
using UltraSim.Network;
using UltraSim.Server;

await LanDiscoveryLaunchSmoke_RunRealHeadlessServerAndClientQuery();
Console.WriteLine("UltraSim.LanDiscovery.LaunchSmoke passed");

static async Task LanDiscoveryLaunchSmoke_RunRealHeadlessServerAndClientQuery()
{
    string repoRoot = FindRepoRoot();
    string headlessProject = Path.Combine(repoRoot, "Server", "_Headless", "UltraSim.Server.Headless.csproj");
    string dotnet = ResolveDotnetExecutable();
    string runtimeRoot = CreateRuntimeRootPath();
    int gamePort = GetAvailableUdpPort();
    int lanPort = GetAvailableUdpPort();

    WriteServerSettingsConfig(
        runtimeRoot,
        new Dictionary<string, string>
        {
            ["server.name"] = "LAN Launch Smoke",
            ["server.network.port"] = gamePort.ToString(),
            ["server.network.bind_address"] = "127.0.0.1",
            ["server.network.max_clients"] = "8",
            ["server.discovery.enabled"] = "on",
            ["server.discovery.publish_mode"] = "disabled",
            ["server.discovery.advertised_host"] = "0.0.0.0",
            ["server.discovery.server_id"] = "lan-launch-smoke-shard",
            ["server.discovery.region"] = "local",
            ["server.discovery.language"] = "en-US",
            ["server.discovery.tags"] = "lan,smoke",
            ["server.discovery.lan_enabled"] = "on",
            ["server.discovery.lan_port"] = lanPort.ToString(),
            ["server.discovery.lan_bind_address"] = "127.0.0.1",
        });

    using var server = new LaunchedHeadlessServer(dotnet, headlessProject, runtimeRoot);
    server.Start();

    var provider = ClientLanDiscoveryFactory.CreateProvider(new ClientLanDiscoveryOptions(
        queryEndpoints: [new IPEndPoint(IPAddress.Loopback, lanPort)],
        timeout: TimeSpan.FromMilliseconds(200),
        listenEndPoint: new IPEndPoint(IPAddress.Loopback, 0)));

    IReadOnlyList<ServerDiscoveryRow> rows = await WaitForRowsAsync(
        provider,
        row => row.ServerId == "lan-launch-smoke-shard",
        server);
    ServerDiscoveryRow row = rows.Single(row => row.ServerId == "lan-launch-smoke-shard");
    Assert.Equal(ServerDiscoveryProviderKind.Lan, row.ProviderKind);
    Assert.Equal("LAN Launch Smoke", row.DisplayName);
    Assert.Equal("127.0.0.1", row.Host);
    Assert.Equal(gamePort, row.Port);
    Assert.Equal("local", row.Region);
    Assert.Equal("en-US", row.PrimaryLanguage);
    Assert.Equal(8, row.MaxUsers);
    Assert.Contains("lan", row.TagsOrEmpty);

    Assert.True(
        ClientDirectConnectRequest.TryCreate(row, out ClientDirectConnectRequest joinRequest, out string joinRequestError),
        joinRequestError);
    Assert.Equal(gamePort, joinRequest.Port);
    Assert.True(joinRequest.TryResolve(out IPEndPoint joinEndPoint, out string resolveError), resolveError);
    Assert.Equal(gamePort, joinEndPoint.Port);
    await VerifyDiscoveredEndpointHandshakeAsync(joinEndPoint);
}

static async Task VerifyDiscoveredEndpointHandshakeAsync(IPEndPoint discoveredEndPoint)
{
    PacketMetadataRegistry metadata = DefaultPacketMetadata.Create();
    using var clientTransport = new UdpSocketNetworkTransport(new UdpSocketNetworkTransportOptions(
        new IPEndPoint(IPAddress.Loopback, 0),
        metadata,
        discoveredEndPoint,
        new ConnectionId(1),
        AllowDynamicConnections: false));

    var handshakeHello = new HandshakeHelloPacket(
        protocolVersion: 3,
        schemaHash: 4,
        contentHash: 5,
        features: FeatureFlags.SharedPayloadFanout | FeatureFlags.Compression);
    var client = new ClientMovementSessionHost(
        clientTransport,
        new ConnectionId(1),
        handshakeHello,
        maxPollBatch: 8);

    clientTransport.Start();
    client.Start();
    SessionId sessionId = client.BeginHandshake(1000);
    Assert.True(sessionId.IsValid);

    Stopwatch stopwatch = Stopwatch.StartNew();
    int pump = 0;
    while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
    {
        client.Pump(1002 + pump * 2);
        if (client.IsReady)
            return;

        pump++;
        await Task.Delay(10);
    }

    throw new InvalidOperationException($"Timed out handshaking against discovered endpoint {discoveredEndPoint}.");
}

static async Task<IReadOnlyList<ServerDiscoveryRow>> WaitForRowsAsync(
    ServerBrowserProviderDefinition provider,
    Func<ServerDiscoveryRow, bool> predicate,
    LaunchedHeadlessServer server)
{
    Exception? lastException = null;
    Stopwatch stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < TimeSpan.FromSeconds(30))
    {
        server.ThrowIfExited();
        try
        {
            IReadOnlyList<ServerDiscoveryRow> rows = await provider.Query.QueryAsync();
            if (rows.Any(predicate))
                return rows;
        }
        catch (Exception ex)
        {
            lastException = ex;
        }

        await Task.Delay(100);
    }

    if (lastException is not null)
    {
        throw new InvalidOperationException(
            $"Timed out waiting for LAN discovery rows.{Environment.NewLine}{server.OutputText}",
            lastException);
    }

    throw new InvalidOperationException($"Timed out waiting for LAN discovery rows.{Environment.NewLine}{server.OutputText}");
}

static void WriteServerSettingsConfig(string runtimeRoot, IReadOnlyDictionary<string, string> values)
{
    var document = ServerSettingsConfigDocument.Empty();
    foreach (KeyValuePair<string, string> pair in values)
        document.SetValue(pair.Key, pair.Value);

    new ServerSettingsConfigStore().Save(ServerSettingsConfigPaths.GetPath(runtimeRoot), document);
}

static string CreateRuntimeRootPath()
{
    string path = Path.Combine(
        Path.GetTempPath(),
        "ultrasim-lan-launch-smoke",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(path);
    return path;
}

static string ResolveDotnetExecutable()
{
    string? configured = Environment.GetEnvironmentVariable("ULTRASIM_DOTNET");
    if (!string.IsNullOrWhiteSpace(configured))
        return configured;

    string userDotnet = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".dotnet",
        "dotnet");
    if (File.Exists(userDotnet))
        return userDotnet;

    return "dotnet";
}

static string FindRepoRoot()
{
    DirectoryInfo? directory = new(AppContext.BaseDirectory);
    while (directory is not null)
    {
        string headlessProject = Path.Combine(directory.FullName, "Server", "_Headless", "UltraSim.Server.Headless.csproj");
        if (File.Exists(headlessProject))
            return directory.FullName;

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find UltraSim repository root.");
}

static int GetAvailableUdpPort()
{
    using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
    socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
    return ((IPEndPoint)socket.LocalEndPoint!).Port;
}

internal sealed class LaunchedHeadlessServer : IDisposable
{
    private readonly string _dotnet;
    private readonly string _projectPath;
    private readonly string _runtimeRoot;
    private readonly List<string> _output = [];
    private Process? _process;

    public LaunchedHeadlessServer(string dotnet, string projectPath, string runtimeRoot)
    {
        _dotnet = dotnet;
        _projectPath = projectPath;
        _runtimeRoot = runtimeRoot;
    }

    public string OutputText
    {
        get
        {
            lock (_output)
                return string.Join(Environment.NewLine, _output);
        }
    }

    public void Start()
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _dotnet,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(_projectPath)!,
        };
        startInfo.ArgumentList.Add("run");
        startInfo.ArgumentList.Add("--project");
        startInfo.ArgumentList.Add(_projectPath);
        startInfo.ArgumentList.Add("-c");
        startInfo.ArgumentList.Add("Release");
        startInfo.ArgumentList.Add("--no-launch-profile");
        startInfo.ArgumentList.Add("--");
        startInfo.ArgumentList.Add($"--runtime-root={_runtimeRoot}");
        startInfo.ArgumentList.Add("--idle-delay-ms=1");

        string? dotnetRoot = Path.GetDirectoryName(_dotnet);
        if (!string.IsNullOrWhiteSpace(dotnetRoot) && Path.GetFileName(_dotnet) == "dotnet")
        {
            startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
            startInfo.Environment["PATH"] = $"{dotnetRoot}{Path.PathSeparator}{startInfo.Environment["PATH"]}";
        }

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start headless server process.");
        _process.OutputDataReceived += (_, args) => Capture(args.Data);
        _process.ErrorDataReceived += (_, args) => Capture(args.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public void ThrowIfExited()
    {
        if (_process?.HasExited == true)
        {
            throw new InvalidOperationException(
                $"Headless server exited early. Exit code: {_process.ExitCode}.{Environment.NewLine}{OutputText}");
        }
    }

    public void Dispose()
    {
        if (_process is null)
            return;

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }
        }
        finally
        {
            _process.Dispose();
        }
    }

    private void Capture(string? line)
    {
        if (string.IsNullOrWhiteSpace(line))
            return;

        lock (_output)
            _output.Add(line);
    }
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Assert.Equal failed. Expected: {expected}; Actual: {actual}");
    }

    public static void True(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Assert.True failed.");
    }

    public static void Contains<T>(T expected, IEnumerable<T> values)
    {
        if (!values.Contains(expected))
            throw new InvalidOperationException($"Assert.Contains failed. Expected value: {expected}");
    }
}
