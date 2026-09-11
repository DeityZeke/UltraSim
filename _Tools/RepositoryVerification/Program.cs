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

if (options.Pf1Only)
{
    if (options.SkipGodot)
        throw new ArgumentException("--pf1-only requires the actual exported Online product.");
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

string[] configurations = options.AllConfigurations ? ["Debug", "Release"] : [options.Configuration];
bool contentlessOnlineVerified = false;
bool finalClientExportsVerified = false;
bool embeddedCompleteHostedVerified = false;
bool mixedSessionLifecycleVerified = false;
bool pf1MixedSwarmVerified = false;
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
    VerifyNetworkCapabilityArtifacts(root, configuration);
    foreach (string project in testProjects)
    {
        var command = new List<string> { "run", "--project", project, "-c", configuration };
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
                FinalClientProductExports finalProducts = VerifyFinalClientProductExports(
                    root, godot, product.Scope);
                finalClientExportsVerified = true;
                VerifyContentlessOnlineNative(
                    root, configuration, product.Executable,
                    ["--headless", "--"]);
                VerifyEmbeddedCompleteHostedNative(
                    root, product.Executable, finalProducts.CompleteExecutable);
                embeddedCompleteHostedVerified = true;
                VerifyPf1MixedExportedSwarm(
                    root, configuration, product.Executable);
                pf1MixedSwarmVerified = true;
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
        !pf1MixedSwarmVerified)
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
                "UltraSim.Client.StandardUi",
                "UltraSim.Game.Composition", "UltraSim.Game.Replication",
                "UltraSim.Server.Runtime"],
            ["UltraSim.Client.Network", "UltraSim.Network", "UltraSim.Game.Protocol",
                "UltraSim.Game.Replication.Remote", "UltraSim.Server.Hosting",
                "UltraSim.Server.ConceptDemo", "UltraSim.Server.ConceptDemo.LocalHost"]),
        new(
            "Online",
            ["UltraSim.Client.Application.Godot", "UltraSim.Client.Content", "UltraSim.Client.Network",
                "UltraSim.Client.StandardUi", "UltraSim.Game.Protocol", "UltraSim.Network"],
            ["UltraSim.Client.BuiltInTestWorlds", "UltraSim.Client.Local",
                "UltraSim.Game.Composition", "UltraSim.Game.Inventory", "UltraSim.Game.Persistence",
                "UltraSim.Game.Replication",
                "UltraSim.Game.Replication.Remote", "UltraSim.Game.UI", "UltraSim.Server.Hosting",
                "UltraSim.Server.Persistence", "UltraSim.Server.Runtime",
                "UltraSim.Server.ConceptDemo", "UltraSim.Server.ConceptDemo.LocalHost"]),
        new(
            "Complete",
            ["UltraSim.Client.Application.Godot", "UltraSim.Client.BuiltInTestWorlds",
                "UltraSim.Client.Local", "UltraSim.Client.StandardUi",
                "UltraSim.Client.Network", "UltraSim.Game.Composition", "UltraSim.Game.Protocol",
                "UltraSim.Game.Replication", "UltraSim.Network", "UltraSim.Server.Runtime",
                "UltraSim.Server.ConceptDemo", "UltraSim.Server.ConceptDemo.LocalHost"],
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
        Run(root, "dotnet",
        [
            "publish",
            "Client/Godot/UltraSim.Client.Application.Godot.csproj",
            "-c", configuration,
            $"-p:UltraSimEdition={gate.Name}",
            $"-p:UltraSimNetworkFlavor={networkFlavor}",
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
        "UltraSim.Client.Core",
        "UltraSim.Client.Local",
        "UltraSim.Client.StandardUi",
        "UltraSim.ECS",
        "UltraSim.Game.Accounting",
        "UltraSim.Game.Admission",
        "UltraSim.Game.Chat",
        "UltraSim.Game.Composition",
        "UltraSim.Game.Contracts",
        "UltraSim.Game.Inventory",
        "UltraSim.Game.Persistence",
        "UltraSim.Game.Physics",
        "UltraSim.Game.Replication",
        "UltraSim.Game.UI",
        "UltraSim.Game.World",
        "UltraSim.Server.Persistence",
        "UltraSim.Server.Runtime",
        "UltraSim.Spatial",
        "UltraSim.UI",
    ];
    RequireExactExportAssemblies(
        singlePlayerDirectory, singlePlayerAssemblies, "Single Player");
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

    ExportClient(root, godot, "Complete", completeExecutable);
    string[] completeAssemblies =
    [
        "UltraSim.Client.Application.Godot",
        "UltraSim.Client.BuiltInTestWorlds",
        "UltraSim.Client.Content",
        "UltraSim.Client.Core",
        "UltraSim.Client.Local",
        "UltraSim.Client.Network",
        "UltraSim.Client.StandardUi",
        "UltraSim.ECS",
        "UltraSim.Game.Accounting",
        "UltraSim.Game.Admission",
        "UltraSim.Game.Chat",
        "UltraSim.Game.Composition",
        "UltraSim.Game.Contracts",
        "UltraSim.Game.Inventory",
        "UltraSim.Game.Persistence",
        "UltraSim.Game.Physics",
        "UltraSim.Game.Protocol",
        "UltraSim.Game.Replication",
        "UltraSim.Game.Replication.Remote",
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

    Console.WriteLine(
        $"PASS F0.8_PhysicalClientExports " +
        $"singlePlayerAssemblies={singlePlayerAssemblies.Length} singlePlayerNetwork=absent " +
        $"onlineAssemblies=13 onlineNetwork=ClientOnly onlineAuthority=absent " +
        $"completeAssemblies={completeAssemblies.Length} completeNetwork=Full " +
        "singlePlayerNative=pass completeLocalNative=pass");
    return new FinalClientProductExports(singlePlayerExecutable, completeExecutable);
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
            RequireNativeA08Result(cold, "cold", coldReady.WorldPackFingerprint,
                expectedWarm: false, expectedRememberedProfile: false,
                expectedProfile: "grammar=ThirdPersonDirect camera=ThirdPersonFollow " +
                    "distance=8.5 fov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT");
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
                expectedProfile: "grammar=ThirdPersonDirect camera=ThirdPersonFollow " +
                    "distance=8.5 fov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT");
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
            RequireNativeA08Result(changed, "warm", changedReady.WorldPackFingerprint,
                expectedWarm: true, expectedRememberedProfile: false,
                expectedProfile: "grammar=OrbitCommand camera=Orbit " +
                    "distance=14.0 fov=70 select=MouseButton:LEFT primary=MouseButton:MIDDLE");
        }
        finally { StopOwnedProcess(changedServer); }
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
            bobOutput, "cold", worldPackFingerprint: string.Empty,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: "grammar=ThirdPersonDirect camera=ThirdPersonFollow " +
                "distance=8.5 fov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT",
            requireWorldFingerprint: false);
        RequireNativeA08Result(
            carolOutput, "cold", worldPackFingerprint: string.Empty,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: "grammar=ThirdPersonDirect camera=ThirdPersonFollow " +
                "distance=8.5 fov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT",
            requireWorldFingerprint: false);
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

static void VerifyPf1MixedExportedSwarm(
    string root,
    string configuration,
    string onlineExecutable)
{
    const int botCount = 8;
    const int nativeClientCount = 2;
    const int participantCapacity = botCount + nativeClientCount;
    const int expectedPresentedActors = participantCapacity + 1; // Woodland animal.
    const ulong seed = 104_729;
    const string scenario = "mixed-chaos";
    const string expectedProfile =
        "grammar=ThirdPersonDirect camera=ThirdPersonFollow " +
        "distance=8.5 fov=76 select=MouseButton:MIDDLE primary=MouseButton:LEFT";

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
                "PF1 Native Alice", "x"), 210_000));
        bobTask = Task.Run(() => RunCaptured(
            root, onlineExecutable,
            NativeSharedClientArguments(
                scope, "bob", ready.Endpoint, ready.Fingerprint,
                bobReady, nativeRelease, expectedPresentedActors,
                "PF1 Native Bob", "y"), 210_000));

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
        string aliceOutput = aliceTask.GetAwaiter().GetResult();
        string bobOutput = bobTask.GetAwaiter().GetResult();
        RequireNativeA08Result(
            aliceOutput, "cold", ready.WorldPackFingerprint,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: expectedProfile);
        RequireNativeA08Result(
            bobOutput, "cold", ready.WorldPackFingerprint,
            expectedWarm: false, expectedRememberedProfile: false,
            expectedProfile: expectedProfile);
        if (!aliceOutput.Contains($"generation={ready.ServerGeneration}", StringComparison.Ordinal) ||
            !bobOutput.Contains($"generation={ready.ServerGeneration}", StringComparison.Ordinal) ||
            !aliceOutput.Contains("guest=PF1 Native Alice", StringComparison.Ordinal) ||
            !aliceOutput.Contains("body=HumanFemale", StringComparison.Ordinal) ||
            !bobOutput.Contains("guest=PF1 Native Bob", StringComparison.Ordinal) ||
            !bobOutput.Contains("body=HumanMale", StringComparison.Ordinal) ||
            !aliceOutput.Contains($"actors={expectedPresentedActors}", StringComparison.Ordinal) ||
            !bobOutput.Contains($"actors={expectedPresentedActors}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                "The exported Online clients did not preserve exact PF1 generation/body/actor presentation.");

        File.WriteAllText(botRelease, "release" + Environment.NewLine);
        string botOutput = botTask.GetAwaiter().GetResult();
        if (!botOutput.Contains("PF1 BOT HARNESS PASS", StringComparison.Ordinal) ||
            !File.Exists(botSummary))
            throw new InvalidOperationException(
                $"The PF1 bot harness omitted its accepted summary:\n{Tail(botOutput, 8_000)}");
        using JsonDocument botDocument = JsonDocument.Parse(File.ReadAllText(botSummary));
        JsonElement bots = botDocument.RootElement;
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
            bots.GetProperty("commandsAttempted").GetInt32() == 0 ||
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
        foreach (string marker in new[] { botStartRelease, nativeRelease, botRelease, serverStop })
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
        "UltraSim.Client.StandardUi", "UltraSim.ECS", "UltraSim.Game.Accounting",
        "UltraSim.Game.Contracts", "UltraSim.Game.Protocol", "UltraSim.Game.World",
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
        "UltraSim.Game.Replication", "UltraSim.Game.Replication.Remote", "UltraSim.Game.UI",
        "UltraSim.Network.Testing", "UltraSim.Server.Hosting", "UltraSim.Server.Persistence",
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
    string godot)
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

        string output = RunCaptured(root, godot,
        [
            "--headless",
            "--path", "Client/Godot",
            "--",
            "--f0-7-smoke",
            "--expect-edition=Complete",
            $"--f0-7-a-endpoint={readyA.Endpoint}",
            $"--f0-7-a-fingerprint={readyA.Fingerprint}",
            $"--f0-7-b-endpoint={readyB.Endpoint}",
            $"--f0-7-b-fingerprint={readyB.Fingerprint}",
            $"--a0-8-cache={Path.Combine(scope, "content-cache")}",
            $"--a0-8-trust={Path.Combine(scope, "client", "trusted-servers.json")}",
            $"--f0-3c-profile-decisions={Path.Combine(scope, "client", "server-profiles.json")}",
        ], 240_000);
        if (!output.Contains("F0.7 MIXED SESSION LIFECYCLE PASS", StringComparison.Ordinal) ||
            !output.Contains($"realms={readyA.Realm}/{readyB.Realm}/{readyA.Realm}",
                StringComparison.Ordinal) ||
            !output.Contains("profile_reuse=False/False/True", StringComparison.Ordinal) ||
            !output.Contains("local_surfaces=1/1/1", StringComparison.Ordinal) ||
            !output.Contains("remote_surfaces=1/1/1", StringComparison.Ordinal) ||
            !output.Contains("boundaries=clean", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The F0.7 native lifecycle evidence was incomplete:\n{Tail(output, 8_000)}");
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

static string[] NativeSharedClientArguments(
    string scope,
    string name,
    string endpoint,
    string fingerprint,
    string ready,
    string release,
    int expectedActors = 4,
    string? guestName = null,
    string? guestBody = null)
{
    var arguments = new List<string>
    {
        "--headless", "--", "--a0-8-smoke",
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
    };
    if (guestName is not null) arguments.Add($"--a0-8-guest-name={guestName}");
    if (guestBody is not null) arguments.Add($"--a0-8-guest-body={guestBody}");
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
    string? summaryFile = null)
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
        $"--maximum-participants={maximumParticipants}",
        $"--same-address-admission-burst={admissionBurstPerAddress}",
    }) start.ArgumentList.Add(argument);
    if (stopFile is not null) start.ArgumentList.Add($"--stop-file={stopFile}");
    if (summaryFile is not null) start.ArgumentList.Add($"--summary-file={summaryFile}");
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
            value.GetProperty("MaximumParticipants").GetInt32(),
            value.GetProperty("ServerGeneration").GetUInt64());
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
    string expectedProfile,
    bool requireWorldFingerprint = true)
{
    string pass = $"A0.8 CONTENTLESS ONLINE PASS mode={mode}";
    if (!output.Contains(pass, StringComparison.Ordinal) ||
        !output.Contains("F0.3c CLIENT PROFILE APPLIED", StringComparison.Ordinal) ||
        !output.Contains(expectedRememberedProfile ? "remembered=True" : "remembered=False", StringComparison.Ordinal) ||
        !output.Contains(expectedProfile, StringComparison.Ordinal) ||
        requireWorldFingerprint &&
            !output.Contains($"world={worldPackFingerprint}", StringComparison.Ordinal) ||
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
    foreach ((string body, string asset) in new[]
        {
            ("x_bot", "x_bot_animated.glb"),
            ("y_bot", "y_bot_animated.glb"),
        })
    {
        string scene = File.ReadAllText(Path.Combine(
            root, "Client", "PresentationAssets", "Avatars", body, $"{body}_visual.tscn"));
        if (!scene.Contains($"res://Assets/Avatars/{body}/{asset}", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"The {body} runtime scene does not consume its consolidated animated body.");
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
        (reference.Attribute("Include")?.Value ?? "").Contains("Game/Composition", StringComparison.Ordinal)).ToArray();
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
    if (localReferences.Length != 3 || localReferences.Any(reference =>
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
        ["Kernels/Network.Testing/UltraSim.Network.Testing.csproj"] =
            ["Kernels/Network/UltraSim.Network.csproj"],
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
            "Game/Protocol/UltraSim.Game.Protocol.csproj",
            "Game/Replication.Remote/UltraSim.Game.Replication.Remote.csproj",
            "Game/UI/UltraSim.Game.UI.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Server/ConceptDemo/UltraSim.Server.ConceptDemo.csproj",
        ],
        ["Server/ConceptDemo/UltraSim.Server.ConceptDemo.csproj"] =
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
            "Client/Content/UltraSim.Client.Content.csproj",
            "Client/Core/UltraSim.Client.Core.csproj",
            "Client/Local/UltraSim.Client.Local.csproj",
            "Client/Network/UltraSim.Client.Network.csproj",
            "Client/StandardUi/UltraSim.Client.StandardUi.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
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

    string[] roots = ["Kernels", "Server", "Client", "Game", "Tools"];
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
    bool Pf1Only,
    bool AllConfigurations,
    string Configuration,
    bool SkipGodot,
    bool NoRestore,
    string? GodotPath)
{
    internal static VerificationOptions Parse(string[] arguments)
    {
        bool architectureOnly = false, pf1Only = false, allConfigurations = false;
        bool skipGodot = false, noRestore = false;
        string configuration = "Release";
        string? godot = null;
        foreach (string argument in arguments)
        {
            if (argument == "--architecture-only") architectureOnly = true;
            else if (argument == "--pf1-only") pf1Only = true;
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
        return new(architectureOnly, pf1Only, allConfigurations, configuration, skipGodot, noRestore, godot);
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
    int MaximumParticipants,
    ulong ServerGeneration);

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
