using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using UltraSim.Client;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;
using UltraSim.Server;
using UltraSim.Server.Discovery;

await RegistryLaunchSmoke_RunRealRegistryPublisherAndClientQuery();
Console.WriteLine("UltraSim.Registry.LaunchSmoke passed");

static async Task RegistryLaunchSmoke_RunRealRegistryPublisherAndClientQuery()
{
    string repoRoot = FindRepoRoot();
    string registryProject = Path.Combine(repoRoot, "Services", "Registry", "UltraSim.Registry.csproj");
    int port = GetAvailableTcpPort();
    string registryUrl = $"http://127.0.0.1:{port}";
    string dotnet = ResolveDotnetExecutable();

    using var registry = new LaunchedRegistry(dotnet, registryProject, registryUrl);
    registry.Start();
    await registry.WaitUntilHealthyAsync(TimeSpan.FromSeconds(45));

    using var httpClient = new HttpClient
    {
        BaseAddress = new Uri(registryUrl),
        Timeout = TimeSpan.FromSeconds(5),
    };

    ServerDiscoveryPublicationPlan plan = ServerDiscoveryPublicationFactory.CreateOfficialRegistryPlan(
        new ServerDiscoveryPublicationSettings(
            Enabled: true,
            RegistryUrl: registryUrl,
            PublishMode: "unverified",
            ServerId: "launch-smoke-shard",
            DisplayName: "Launch Smoke Shard",
            AdvertisedHost: "127.0.0.1",
            Port: 42042,
            MaxUsers: 64,
            Description: "Registry launched-service smoke",
            Region: "local",
            PrimaryLanguage: "en-US",
            Tags: "smoke, launched",
            HeartbeatIntervalSeconds: 1,
            UnpublishOnStop: true,
            CompatibilityId: "launch-smoke"),
        httpClient);

    DateTimeOffset now = DateTimeOffset.UtcNow;
    var coordinator = new ServerDiscoveryPublisherCoordinator(
        plan.Publisher,
        plan.AdvertisedSettings,
        () => new ServerStatusSnapshot(
            ProfileName: "launch-smoke",
            State: ServerApplicationState.Running,
            IsInitialized: true,
            IsRunning: true,
            IsAcceptingConnections: true,
            TickCount: 256,
            ActiveSessions: 2,
            PeakSessions: 3,
            StartedAtUtc: now.AddMinutes(-2),
            Uptime: TimeSpan.FromMinutes(2)),
        plan.PublisherOptions);

    await coordinator.PublishStartupAsync();

    ServerBrowserProviderDefinition provider = ClientOfficialRegistryDiscoveryFactory.CreateProvider(
        new ClientOfficialRegistryDiscoveryOptions(
            RegistryUrl: registryUrl,
            Limit: 25,
            SortBy: "name",
            Descending: false),
        httpClient);

    IReadOnlyList<ServerDiscoveryRow> rows = await WaitForRowsAsync(provider, row => row.ServerId == "launch-smoke-shard");
    ServerDiscoveryRow row = rows.Single(row => row.ServerId == "launch-smoke-shard");
    Assert.Equal("Launch Smoke Shard", row.DisplayName);
    Assert.Equal(ServerDiscoveryProviderKind.Official, row.ProviderKind);
    Assert.Equal(ServerListingHealthState.Online, row.HealthState);
    Assert.Equal("127.0.0.1", row.Host);
    Assert.Equal(42042, row.Port);
    Assert.Equal(2, row.CurrentUsers);
    Assert.Equal(64, row.MaxUsers);
    Assert.Contains("smoke", row.TagsOrEmpty);

    await coordinator.PublishShutdownAsync();

    IReadOnlyList<ServerDiscoveryRow> afterShutdown = await WaitForRowsAsync(
        provider,
        row => row.ServerId == "launch-smoke-shard",
        expectedToExist: false);
    Assert.False(afterShutdown.Any(row => row.ServerId == "launch-smoke-shard"));
}

static async Task<IReadOnlyList<ServerDiscoveryRow>> WaitForRowsAsync(
    ServerBrowserProviderDefinition provider,
    Func<ServerDiscoveryRow, bool> predicate,
    bool expectedToExist = true)
{
    Exception? lastException = null;
    Stopwatch stopwatch = Stopwatch.StartNew();
    while (stopwatch.Elapsed < TimeSpan.FromSeconds(10))
    {
        try
        {
            IReadOnlyList<ServerDiscoveryRow> rows = await provider.Query.QueryAsync();
            bool exists = rows.Any(predicate);
            if (exists == expectedToExist)
                return rows;
        }
        catch (Exception ex)
        {
            lastException = ex;
        }

        await Task.Delay(100);
    }

    if (lastException is not null)
        throw new InvalidOperationException("Timed out waiting for registry rows.", lastException);

    throw new InvalidOperationException("Timed out waiting for registry rows.");
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
        string registryProject = Path.Combine(directory.FullName, "Services", "Registry", "UltraSim.Registry.csproj");
        if (File.Exists(registryProject))
            return directory.FullName;

        directory = directory.Parent;
    }

    throw new DirectoryNotFoundException("Could not find UltraSim repository root.");
}

static int GetAvailableTcpPort()
{
    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

internal sealed class LaunchedRegistry : IDisposable
{
    private readonly string _dotnet;
    private readonly string _projectPath;
    private readonly string _registryUrl;
    private readonly List<string> _output = [];
    private Process? _process;

    public LaunchedRegistry(string dotnet, string projectPath, string registryUrl)
    {
        _dotnet = dotnet;
        _projectPath = projectPath;
        _registryUrl = registryUrl;
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
        startInfo.ArgumentList.Add("--urls");
        startInfo.ArgumentList.Add(_registryUrl);
        startInfo.ArgumentList.Add("--OfficialRegistry:EnableStatePersistence=false");
        startInfo.ArgumentList.Add("--OfficialRegistry:EnablePolling=false");
        startInfo.ArgumentList.Add("--OfficialRegistry:PublicQueryCacheSeconds=0");
        startInfo.ArgumentList.Add("--OfficialRegistry:UnverifiedRegisterCooldownSeconds=0");
        startInfo.Environment["ASPNETCORE_ENVIRONMENT"] = "Production";

        string? dotnetRoot = Path.GetDirectoryName(_dotnet);
        if (!string.IsNullOrWhiteSpace(dotnetRoot) && Path.GetFileName(_dotnet) == "dotnet")
        {
            startInfo.Environment["DOTNET_ROOT"] = dotnetRoot;
            startInfo.Environment["PATH"] = $"{dotnetRoot}{Path.PathSeparator}{startInfo.Environment["PATH"]}";
        }

        _process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start registry process.");
        _process.OutputDataReceived += (_, args) => Capture(args.Data);
        _process.ErrorDataReceived += (_, args) => Capture(args.Data);
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
    }

    public async Task WaitUntilHealthyAsync(TimeSpan timeout)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
        Stopwatch stopwatch = Stopwatch.StartNew();
        Exception? lastException = null;

        while (stopwatch.Elapsed < timeout)
        {
            if (_process?.HasExited == true)
            {
                throw new InvalidOperationException(
                    $"Registry exited before becoming healthy. Exit code: {_process.ExitCode}.{Environment.NewLine}{OutputText}");
            }

            try
            {
                using HttpResponseMessage response = await client.GetAsync($"{_registryUrl}/healthz");
                if (response.IsSuccessStatusCode)
                    return;
            }
            catch (Exception ex)
            {
                lastException = ex;
            }

            await Task.Delay(250);
        }

        throw new InvalidOperationException(
            $"Timed out waiting for registry health.{Environment.NewLine}{OutputText}",
            lastException);
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

    private string OutputText
    {
        get
        {
            lock (_output)
                return string.Join(Environment.NewLine, _output);
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

    public static void False(bool condition)
    {
        if (condition)
            throw new InvalidOperationException("Assert.False failed.");
    }

    public static void Contains<T>(T expected, IEnumerable<T> values)
    {
        if (!values.Contains(expected))
            throw new InvalidOperationException($"Assert.Contains failed. Missing: {expected}");
    }
}
