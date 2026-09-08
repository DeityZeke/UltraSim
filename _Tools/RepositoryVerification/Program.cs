using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

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

string[] configurations = options.AllConfigurations ? ["Debug", "Release"] : [options.Configuration];
bool contentlessOnlineVerified = false;
string[] testProjects =
[
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
    "_Tests/UltraSim.S0.Integration.Tests/UltraSim.S0.Integration.Tests.csproj",
];

foreach (string configuration in configurations)
{
    Console.WriteLine($"VERIFY configuration={configuration}");
    foreach (string project in testProjects)
    {
        var command = new List<string> { "run", "--project", project, "-c", configuration };
        if (options.NoRestore) command.Add("--no-restore");
        Run(root, "dotnet", command);
    }

    Run(root, "dotnet",
    [
        "run", "--project", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration, "--", "--ephemeral-smoke",
    ]);

    VerifyClientCompositionArtifacts(root, configuration);

    if (!options.SkipGodot)
    {
        const string godotProject = "_Tests/UltraSim.S0.Godot.Tests/UltraSim.S0.Godot.Tests.csproj";
        var build = new List<string> { "build", godotProject, "-c", configuration };
        if (options.NoRestore) build.Add("--no-restore");
        Run(root, "dotnet", build);
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
            "--", "--d0-5-smoke", "--expect-edition=SinglePlayer",
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
                VerifyContentlessOnlineNative(
                    root, configuration, product.Executable,
                    ["--headless", "--"]);
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
    }
}

Run(root, "git", ["diff", "--check"]);
Console.WriteLine("PASS repository verification");
return 0;

static void VerifyClientCompositionArtifacts(string root, string configuration)
{
    ClientCompositionGate[] gates =
    [
        new(
            "SinglePlayer",
            ["UltraSim.Client.Application.Godot", "UltraSim.Client.BuiltInTestWorlds",
                "UltraSim.Client.Content", "UltraSim.Client.Local",
                "UltraSim.Client.StandardUi",
                "UltraSim.Game.Composition", "UltraSim.Game.Replication",
                "UltraSim.Server.Runtime"],
            ["UltraSim.Client.Network", "UltraSim.Network", "UltraSim.Game.Protocol",
                "UltraSim.Game.Replication.Remote", "UltraSim.Server.Hosting"]),
        new(
            "Online",
            ["UltraSim.Client.Application.Godot", "UltraSim.Client.Content", "UltraSim.Client.Network",
                "UltraSim.Client.StandardUi", "UltraSim.Game.Protocol", "UltraSim.Network"],
            ["UltraSim.Client.BuiltInTestWorlds", "UltraSim.Client.Local",
                "UltraSim.Game.Composition", "UltraSim.Game.Inventory", "UltraSim.Game.Persistence",
                "UltraSim.Game.Replication",
                "UltraSim.Game.Replication.Remote", "UltraSim.Game.UI", "UltraSim.Server.Hosting",
                "UltraSim.Server.Persistence", "UltraSim.Server.Runtime"]),
        new(
            "Complete",
            ["UltraSim.Client.Application.Godot", "UltraSim.Client.BuiltInTestWorlds",
                "UltraSim.Client.Local", "UltraSim.Client.StandardUi",
                "UltraSim.Client.Network", "UltraSim.Game.Composition", "UltraSim.Game.Protocol",
                "UltraSim.Game.Replication", "UltraSim.Network", "UltraSim.Server.Runtime"],
            []),
    ];

    foreach (ClientCompositionGate gate in gates)
    {
        string output = Path.Combine(
            Path.GetTempPath(), "ultrasim-repository-verification", Environment.ProcessId.ToString(),
            configuration, gate.Name);
        if (Directory.Exists(output)) Directory.Delete(output, recursive: true);
        Directory.CreateDirectory(output);
        Run(root, "dotnet",
        [
            "publish",
            "Client/Godot/UltraSim.Client.Application.Godot.csproj",
            "-c", configuration,
            $"-p:UltraSimEdition={gate.Name}",
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
    Run(root, "dotnet",
    [
        "build",
        "Client/Godot/UltraSim.Client.Application.Godot.csproj",
        "-c", configuration,
        $"-p:UltraSimEdition={edition}",
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
        "UltraSim.Client.Core",
        "UltraSim.Client.Network",
        "UltraSim.Client.StandardUi",
        "UltraSim.ECS",
        "UltraSim.Game.Accounting",
        "UltraSim.Game.Contracts",
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
            RequireNativeA08Result(cold, "cold", coldReady.WorldPackFingerprint,
                expectedWarm: false, expectedRememberedProfile: false,
                expectedProfile: "grammar=ThirdPersonDirect camera=ThirdPersonFollow");
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
                "--a0-8-allow-private-content",
                "--a0-8-expect=warm",
                "--expect-edition=Online",
            ]), 45_000);
            RequireNativeA08Result(warm, "warm", warmReady.WorldPackFingerprint,
                expectedWarm: true, expectedRememberedProfile: true,
                expectedProfile: "grammar=ThirdPersonDirect camera=ThirdPersonFollow");
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
            RequireNativeA08Result(changed, "warm", changedReady.WorldPackFingerprint,
                expectedWarm: true, expectedRememberedProfile: false,
                expectedProfile: "grammar=OrbitCommand camera=Orbit");
        }
        finally { StopOwnedProcess(changedServer); }
    }

    Console.WriteLine(
        $"PASS A0.8_ContentlessOnlineNative coldWarm=true profileChange=true realm={coldReady.Realm} " +
        $"world={coldReady.WorldPackFingerprint} chunks={coldReady.WorldChunks} " +
        $"archiveBytes={coldReady.ArchiveBytes} profileRevision={coldReady.ClientProfileRevision} " +
        $"thirdPersonHash={coldReady.ClientProfileHash} orbitHash={changedReady.ClientProfileHash}");
    Directory.Delete(scope, recursive: true);
}

static Process StartNativeDeployment(
    string root,
    string configuration,
    string identityDirectory,
    string readyFile,
    int applicationPort,
    int contentPort,
    out NativeDeploymentReady ready,
    string clientProfile = "third-person")
{
    var start = new ProcessStartInfo("dotnet")
    {
        WorkingDirectory = root,
        UseShellExecute = false,
    };
    foreach (string argument in new[]
    {
        "run", "--project", "Server/DirectConnect/UltraSim.Server.DirectConnect.csproj",
        "-c", configuration, "--no-build", "--",
        $"--bind=127.0.0.1:{applicationPort}",
        $"--content-bind=127.0.0.1:{contentPort}",
        $"--identity-dir={identityDirectory}",
        $"--ready-file={readyFile}",
        $"--client-profile={clientProfile}",
    }) start.ArgumentList.Add(argument);
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
            value.GetProperty("WorldChunks").GetInt32(),
            RequiredString(value, "ClientProfile"),
            value.GetProperty("ClientProfileRevision").GetUInt64(),
            RequiredString(value, "ClientProfileHash"));
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
    string worldPackFingerprint,
    bool expectedWarm,
    bool expectedRememberedProfile,
    string expectedProfile)
{
    string pass = $"A0.8 CONTENTLESS ONLINE PASS mode={mode}";
    if (!output.Contains(pass, StringComparison.Ordinal) ||
        !output.Contains("F0.3c CLIENT PROFILE APPLIED", StringComparison.Ordinal) ||
        !output.Contains(expectedRememberedProfile ? "remembered=True" : "remembered=False", StringComparison.Ordinal) ||
        !output.Contains(expectedProfile, StringComparison.Ordinal) ||
        !output.Contains($"world={worldPackFingerprint}", StringComparison.Ordinal) ||
        !output.Contains("chunks=9", StringComparison.Ordinal) ||
        !output.Contains("proofs=1", StringComparison.Ordinal) ||
        !output.Contains("ui=1", StringComparison.Ordinal) ||
        !output.Contains("chat=1", StringComparison.Ordinal) ||
        expectedWarm && !output.Contains("filesFetched=0 bytesFetched=0", StringComparison.Ordinal) ||
        !expectedWarm && output.Contains("bytesFetched=0", StringComparison.Ordinal))
        throw new InvalidOperationException(
            $"The A0.8 {mode} native product output was incomplete:\n{Tail(output, 8_000)}");
}

static string RunCaptured(
    string root,
    string executable,
    IEnumerable<string> arguments,
    int timeoutMilliseconds)
{
    var start = new ProcessStartInfo(executable)
    {
        WorkingDirectory = root,
        UseShellExecute = false,
        RedirectStandardOutput = true,
        RedirectStandardError = true,
    };
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    Console.WriteLine($"> {executable} {string.Join(' ', start.ArgumentList.Select(Quote))}");
    using Process process = Process.Start(start) ??
        throw new InvalidOperationException($"Unable to start '{executable}'.");
    Task<string> standardOutput = process.StandardOutput.ReadToEndAsync();
    Task<string> standardError = process.StandardError.ReadToEndAsync();
    if (!process.WaitForExit(timeoutMilliseconds))
    {
        process.Kill(entireProcessTree: true);
        process.WaitForExit();
        throw new TimeoutException($"'{executable}' exceeded {timeoutMilliseconds} ms.");
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
        ["Client/PresentationAssets/Avatars/x_bot/X Bot.fbx"] =
            "706ffe9e298271fa69a3a8e28dc6a391479f58c4945a63608d33e2fb87e09aa6",
        ["Client/PresentationAssets/Avatars/y_bot/Y Bot.fbx"] =
            "ed035787491803490456840a1f2479e8c0a7ae65cfa38b5447fb2f1a9f8e2fd5",
    };
    foreach ((string relative, string hash) in expected)
    {
        string path = Path.Combine(root, relative);
        if (!File.Exists(path) ||
            !string.Equals(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant(),
                hash, StringComparison.Ordinal))
            throw new InvalidOperationException($"The frozen human calibration asset is missing or changed: {relative}.");
    }
    string link = Path.Combine(root, "Client", "Godot", "Assets");
    if (!Directory.Exists(link) ||
        !string.Equals(new DirectoryInfo(link).LinkTarget, "../PresentationAssets", StringComparison.Ordinal))
        throw new InvalidOperationException(
            "The maintained Godot client does not expose the shared build-time presentation assets.");
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
        (reference.Attribute("Include")?.Value ?? "").Contains("Game/Contracts", StringComparison.Ordinal)).ToArray();
    XElement online = references.Single(reference =>
        reference.Attribute("Include")?.Value.EndsWith(
            "Network/UltraSim.Client.Network.csproj", StringComparison.Ordinal) == true);
    string onlineCondition = online.Attribute("Condition")?.Value ?? "";
    if (localReferences.Length != 4 || localReferences.Any(reference =>
        !(reference.Attribute("Condition")?.Value ?? "").Contains("SinglePlayer", StringComparison.Ordinal) ||
        !(reference.Attribute("Condition")?.Value ?? "").Contains("Complete", StringComparison.Ordinal) ||
        (reference.Attribute("Condition")?.Value ?? "").Contains("Online", StringComparison.Ordinal)))
        throw new InvalidOperationException("The local client references are not edition-fenced.");
    if (!onlineCondition.Contains("Online", StringComparison.Ordinal) ||
        !onlineCondition.Contains("Complete", StringComparison.Ordinal) ||
        onlineCondition.Contains("SinglePlayer", StringComparison.Ordinal))
        throw new InvalidOperationException("The remote client reference is not edition-fenced.");

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
    Match onlinePreset = Regex.Match(
        presets,
        "name=\"UltraSim Online Linux\"(?<body>[\\s\\S]*?)(?=\\[preset\\.1\\.options\\])",
        RegexOptions.CultureInvariant);
    if (!onlinePreset.Success ||
        !onlinePreset.Groups["body"].Value.Contains("exclude_filter=\"Assets/**\"", StringComparison.Ordinal))
        throw new InvalidOperationException("The Online export does not exclude local presentation assets.");

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
        ["Kernels/ECS.Generators/UltraSim.ECS.Generators.csproj"] = [],
        ["Kernels/ECS/UltraSim.ECS.csproj"] =
            ["Kernels/ECS.Generators/UltraSim.ECS.Generators.csproj"],
        ["Kernels/Network.Generators/UltraSim.Network.Generators.csproj"] = [],
        ["Kernels/Network/UltraSim.Network.csproj"] =
            ["Kernels/Network.Generators/UltraSim.Network.Generators.csproj"],
        ["Kernels/Spatial/UltraSim.Spatial.csproj"] = [],
        ["Kernels/UI/UltraSim.UI.csproj"] = [],
        ["Server/Persistence/UltraSim.Server.Persistence.csproj"] =
            ["Kernels/ECS/UltraSim.ECS.csproj"],
        ["Server/Runtime/UltraSim.Server.Runtime.csproj"] =
            ["Kernels/ECS/UltraSim.ECS.csproj"],
        ["Server/Hosting/UltraSim.Server.Hosting.csproj"] =
            ["Server/Runtime/UltraSim.Server.Runtime.csproj"],
        ["Server/DirectConnect/UltraSim.Server.DirectConnect.csproj"] =
        [
            "Client/Content/UltraSim.Client.Content.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Protocol/UltraSim.Game.Protocol.csproj",
            "Game/Replication.Remote/UltraSim.Game.Replication.Remote.csproj",
            "Game/UI/UltraSim.Game.UI.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Server/Hosting/UltraSim.Server.Hosting.csproj",
        ],
        ["Client/Core/UltraSim.Client.Core.csproj"] =
        [
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
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
            ["Kernels/UI/UltraSim.UI.csproj"],
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
            "Client/Core/UltraSim.Client.Core.csproj",
            "Client/Local/UltraSim.Client.Local.csproj",
            "Client/Network/UltraSim.Client.Network.csproj",
            "Client/StandardUi/UltraSim.Client.StandardUi.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/Spatial/UltraSim.Spatial.csproj",
        ],
        ["Game/Accounting/UltraSim.Game.Accounting.csproj"] =
            ["Kernels/ECS/UltraSim.ECS.csproj"],
        ["Game/Contracts/UltraSim.Game.Contracts.csproj"] =
            ["Game/Accounting/UltraSim.Game.Accounting.csproj", "Kernels/ECS/UltraSim.ECS.csproj"],
        ["Game/Inventory/UltraSim.Game.Inventory.csproj"] =
            ["Kernels/ECS/UltraSim.ECS.csproj"],
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
            "Game/Accounting/UltraSim.Game.Accounting.csproj",
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
    };

    string[] roots = ["Kernels", "Server", "Client", "Game"];
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
    }
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

    string[] productionRoots = ["Kernels", "Server", "Client", "Game"];
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
    var start = new ProcessStartInfo(executable)
    {
        WorkingDirectory = root,
        UseShellExecute = false,
    };
    if (environment is not null)
        foreach ((string key, string value) in environment)
            start.Environment[key] = value;
    foreach (string argument in arguments) start.ArgumentList.Add(argument);
    Console.WriteLine($"> {executable} {string.Join(' ', start.ArgumentList.Select(Quote))}");
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
    bool AllConfigurations,
    string Configuration,
    bool SkipGodot,
    bool NoRestore,
    string? GodotPath)
{
    internal static VerificationOptions Parse(string[] arguments)
    {
        bool architectureOnly = false, allConfigurations = false, skipGodot = false, noRestore = false;
        string configuration = "Release";
        string? godot = null;
        foreach (string argument in arguments)
        {
            if (argument == "--architecture-only") architectureOnly = true;
            else if (argument == "--all-configurations") allConfigurations = true;
            else if (argument == "--skip-godot") skipGodot = true;
            else if (argument == "--no-restore") noRestore = true;
            else if (argument.StartsWith("--configuration=", StringComparison.Ordinal))
                configuration = argument["--configuration=".Length..];
            else if (argument.StartsWith("--godot=", StringComparison.Ordinal)) godot = argument["--godot=".Length..];
            else throw new ArgumentException($"Unknown verification option '{argument}'.");
        }
        if (configuration is not ("Debug" or "Release"))
            throw new ArgumentException("Verification configuration must be Debug or Release.");
        return new(architectureOnly, allConfigurations, configuration, skipGodot, noRestore, godot);
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
    int WorldChunks,
    string ClientProfile,
    ulong ClientProfileRevision,
    string ClientProfileHash);

internal sealed record ContentlessOnlineExport(
    string Scope,
    string Executable);
