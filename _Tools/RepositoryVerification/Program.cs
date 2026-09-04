using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

string root = FindRepositoryRoot();
VerificationOptions options = VerificationOptions.Parse(args);

VerifyArchitecture(root);
Console.WriteLine("PASS active project dependency boundaries");
VerifyClientPresentationAssets(root);
Console.WriteLine("PASS built-in client presentation assets");
VerifyDocumentation(root);
Console.WriteLine("PASS documentation architecture and links");

if (options.ArchitectureOnly)
    return 0;

string[] configurations = options.AllConfigurations ? ["Debug", "Release"] : [options.Configuration];
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

    VerifyClientCompositionArtifacts(root, configuration, options.NoRestore);

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
        const string playableProject = "Client/Playable.Godot/UltraSim.Client.Playable.Godot.csproj";
        var playableBuild = new List<string> { "build", playableProject, "-c", configuration };
        if (options.NoRestore) playableBuild.Add("--no-restore");
        Run(root, "dotnet", playableBuild);
        foreach (string localGodotProject in new[] { "Playable", "SinglePlayer", "Complete" })
        {
            Run(root, godot,
            [
                "--headless",
                "--editor",
                "--path", $"Client/{localGodotProject}.Godot",
                "--quit",
            ]);
        }
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Playable.Godot",
            "--", "--smoke",
        ]);
        foreach (string composition in new[] { "SinglePlayer", "Online", "Complete" })
        {
            Run(root, godot,
            [
                "--headless",
                "--path", $"Client/{composition}.Godot",
                "--", "--composition-smoke",
            ]);
        }
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/SinglePlayer.Godot",
            "--", "--shell-ui-smoke",
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
                "--path", "Client/SinglePlayer.Godot",
                "--", "--f0-2-smoke", $"--cabin-content={variant}", $"--human={human}",
            ]);
        }
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/Complete.Godot",
            "--", "--f0-2-smoke", "--cabin-content=default", "--human=x",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/SinglePlayer.Godot",
            "--", "--d0-0-smoke",
        ]);
        Run(root, godot,
        [
            "--headless",
            "--path", "Client/SinglePlayer.Godot",
            "--", "--d0-1-smoke",
        ]);
    }
}

Run(root, "git", ["diff", "--check"]);
Console.WriteLine("PASS repository verification");
return 0;

static void VerifyClientCompositionArtifacts(string root, string configuration, bool noRestore)
{
    ClientCompositionGate[] gates =
    [
        new(
            "SinglePlayer",
            "Client/SinglePlayer.Godot/UltraSim.Client.SinglePlayer.Godot.csproj",
            ["UltraSim.Client.BuiltInTestWorlds", "UltraSim.Client.Content", "UltraSim.Client.Local",
                "UltraSim.Client.Local.Godot", "UltraSim.Game.Composition", "UltraSim.Game.Replication",
                "UltraSim.Server.Runtime"],
            ["UltraSim.Client.Network", "UltraSim.Network", "UltraSim.Game.Protocol",
                "UltraSim.Game.Replication.Remote", "UltraSim.Server.Hosting"]),
        new(
            "Online",
            "Client/Online.Godot/UltraSim.Client.Online.Godot.csproj",
            ["UltraSim.Client.Network", "UltraSim.Game.Protocol", "UltraSim.Network"],
            ["UltraSim.Client.BuiltInTestWorlds", "UltraSim.Client.Local", "UltraSim.Client.Local.Godot",
                "UltraSim.Game.Composition", "UltraSim.Game.Persistence",
                "UltraSim.Game.Replication",
                "UltraSim.Game.Replication.Remote", "UltraSim.Server.Hosting",
                "UltraSim.Server.Persistence", "UltraSim.Server.Runtime"]),
        new(
            "Complete",
            "Client/Complete.Godot/UltraSim.Client.Complete.Godot.csproj",
            ["UltraSim.Client.BuiltInTestWorlds", "UltraSim.Client.Local", "UltraSim.Client.Local.Godot",
                "UltraSim.Client.Network", "UltraSim.Game.Composition", "UltraSim.Game.Protocol",
                "UltraSim.Game.Replication", "UltraSim.Network", "UltraSim.Server.Runtime"],
            []),
    ];

    foreach (ClientCompositionGate gate in gates)
    {
        var command = new List<string> { "build", gate.Project, "-c", configuration };
        if (noRestore) command.Add("--no-restore");
        Run(root, "dotnet", command);

        string output = Path.Combine(root, "Client", $"{gate.Name}.Godot", ".godot", "mono", "temp", "bin",
            configuration);
        string dependenciesPath = Path.Combine(output, $"UltraSim.Client.{gate.Name}.Godot.deps.json");
        if (!File.Exists(dependenciesPath))
            throw new InvalidOperationException($"{gate.Name} did not produce its expected .deps.json: {dependenciesPath}");
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
        int copiedNativeAssets = Directory.EnumerateFiles(output, "*", SearchOption.TopDirectoryOnly)
            .Count(static path => Path.GetExtension(path) is ".so" or ".dylib");
        Console.WriteLine(
            $"PASS {gate.Name}_ResolvedOutput dependencies={dependencies.Count} " +
            $"ultrasimAssemblies={outputAssemblies.Count} managedBytes={managedBytes} " +
            $"declaredNativeAssets={declaredNativeAssets} copiedNativeAssets={copiedNativeAssets}");
    }

    Console.WriteLine("PASS SinglePlayer_NoClientNetwork");
    Console.WriteLine("PASS SinglePlayer_NoNetworkKernel");
    Console.WriteLine("PASS SinglePlayer_UsesLocalAuthorityBridge");
    Console.WriteLine("PASS Online_NoLocalServerRuntime");
    Console.WriteLine("PASS Online_NoLocalAuthorityComposition");
    Console.WriteLine("PASS Online_NoLocalAuthorityBridge");
}

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
    foreach (string composition in new[] { "SinglePlayer", "Complete", "Playable" })
    {
        string link = Path.Combine(root, "Client", $"{composition}.Godot", "Assets");
        if (!Directory.Exists(link) ||
            !string.Equals(new DirectoryInfo(link).LinkTarget, "../PresentationAssets", StringComparison.Ordinal))
            throw new InvalidOperationException(
                $"{composition} does not expose the shared build-time presentation assets.");
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
        ],
        ["Client/BuiltInTestWorlds/UltraSim.Client.BuiltInTestWorlds.csproj"] =
            ["Client/Content/UltraSim.Client.Content.csproj"],
        ["Client/Network/UltraSim.Client.Network.csproj"] =
        [
            "Client/Core/UltraSim.Client.Core.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Protocol/UltraSim.Game.Protocol.csproj",
            "Kernels/Network/UltraSim.Network.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
        ],
        ["Client/_Godot/UltraSim.Client.Godot.csproj"] =
        [
            "Client/Core/UltraSim.Client.Core.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/Spatial/UltraSim.Spatial.csproj",
        ],
        ["Client/Bootstrap.Godot/UltraSim.Client.Bootstrap.Godot.csproj"] =
        [
            "Client/Core/UltraSim.Client.Core.csproj",
            "Client/_Godot/UltraSim.Client.Godot.csproj",
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
        ["Client/Local.Godot/UltraSim.Client.Local.Godot.csproj"] =
        [
            "Client/Bootstrap.Godot/UltraSim.Client.Bootstrap.Godot.csproj",
            "Client/BuiltInTestWorlds/UltraSim.Client.BuiltInTestWorlds.csproj",
            "Client/Core/UltraSim.Client.Core.csproj",
            "Client/Local/UltraSim.Client.Local.csproj",
            "Client/_Godot/UltraSim.Client.Godot.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/World/UltraSim.Game.World.csproj",
            "Kernels/Spatial/UltraSim.Spatial.csproj",
        ],
        ["Client/SinglePlayer.Godot/UltraSim.Client.SinglePlayer.Godot.csproj"] =
        [
            "Client/Bootstrap.Godot/UltraSim.Client.Bootstrap.Godot.csproj",
            "Client/Local.Godot/UltraSim.Client.Local.Godot.csproj",
        ],
        ["Client/Online.Godot/UltraSim.Client.Online.Godot.csproj"] =
        [
            "Client/Bootstrap.Godot/UltraSim.Client.Bootstrap.Godot.csproj",
            "Client/Network/UltraSim.Client.Network.csproj",
        ],
        ["Client/Complete.Godot/UltraSim.Client.Complete.Godot.csproj"] =
        [
            "Client/Bootstrap.Godot/UltraSim.Client.Bootstrap.Godot.csproj",
            "Client/Local.Godot/UltraSim.Client.Local.Godot.csproj",
            "Client/Network/UltraSim.Client.Network.csproj",
        ],
        ["Client/Playable.Godot/UltraSim.Client.Playable.Godot.csproj"] =
            ["Client/Local.Godot/UltraSim.Client.Local.Godot.csproj"],
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
        "Client/BuiltInTestWorlds/README.md",
        "Client/Local/README.md",
        "Client/Bootstrap.Godot/README.md",
        "Client/_Godot/README.md",
        "Client/Playable.Godot/README.md",
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
            "Client/Local/README.md", "Client/_Godot/README.md", "Client/Playable.Godot/README.md", "Game/World/README.md",
            "Game/Chat/README.md", "Game/Physics/README.md", "Game/Replication/README.md",
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

static void Run(string root, string executable, IEnumerable<string> arguments)
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
    if (process.ExitCode != 0)
        throw new InvalidOperationException($"'{executable}' exited with code {process.ExitCode}.");
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
    string Project,
    string[] RequiredAssemblies,
    string[] ForbiddenAssemblies);
