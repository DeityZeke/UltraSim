using System.Diagnostics;
using System.Xml.Linq;

string root = FindRepositoryRoot();
VerificationOptions options = VerificationOptions.Parse(args);

VerifyArchitecture(root);
Console.WriteLine("PASS active project dependency boundaries");

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
    "_Tests/UltraSim.Client.Tests/UltraSim.Client.Tests.csproj",
    "_Tests/UltraSim.Game.Persistence.Tests/UltraSim.Game.Persistence.Tests.csproj",
    "_Tests/UltraSim.Game.Admission.Tests/UltraSim.Game.Admission.Tests.csproj",
    "_Tests/UltraSim.Game.Inventory.Tests/UltraSim.Game.Inventory.Tests.csproj",
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
    }
}

Run(root, "git", ["diff", "--check"]);
Console.WriteLine("PASS repository verification");
return 0;

static void VerifyArchitecture(string root)
{
    var expected = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Kernels/ECS.Generators/UltraSim.ECS.Generators.csproj"] = [],
        ["Kernels/ECS/UltraSim.ECS.csproj"] =
            ["Kernels/ECS.Generators/UltraSim.ECS.Generators.csproj"],
        ["Kernels/UI/UltraSim.UI.csproj"] = [],
        ["Server/Persistence/UltraSim.Server.Persistence.csproj"] =
            ["Kernels/ECS/UltraSim.ECS.csproj"],
        ["Server/Runtime/UltraSim.Server.Runtime.csproj"] =
            ["Kernels/ECS/UltraSim.ECS.csproj"],
        ["Server/Hosting/UltraSim.Server.Hosting.csproj"] =
            ["Server/Runtime/UltraSim.Server.Runtime.csproj"],
        ["Client/Core/UltraSim.Client.Core.csproj"] =
            ["Kernels/UI/UltraSim.UI.csproj"],
        ["Client/_Godot/UltraSim.Client.Godot.csproj"] =
            ["Client/Core/UltraSim.Client.Core.csproj"],
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
        ["Game/UI/UltraSim.Game.UI.csproj"] =
        [
            "Game/Inventory/UltraSim.Game.Inventory.csproj",
            "Kernels/ECS/UltraSim.ECS.csproj",
            "Kernels/UI/UltraSim.UI.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Game/Composition/UltraSim.Game.Composition.csproj"] =
        [
            "Game/Accounting/UltraSim.Game.Accounting.csproj",
            "Game/Contracts/UltraSim.Game.Contracts.csproj",
            "Game/Inventory/UltraSim.Game.Inventory.csproj",
            "Kernels/ECS.Generators/UltraSim.ECS.Generators.csproj",
            "Kernels/ECS/UltraSim.ECS.csproj",
            "Server/Runtime/UltraSim.Server.Runtime.csproj",
        ],
        ["Game/Persistence/UltraSim.Game.Persistence.csproj"] =
        [
            "Game/Accounting/UltraSim.Game.Accounting.csproj",
            "Game/Composition/UltraSim.Game.Composition.csproj",
            "Server/Persistence/UltraSim.Server.Persistence.csproj",
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
        if (references.Any(static path => path.StartsWith("_V2/", StringComparison.Ordinal)))
            throw new InvalidOperationException($"Active project '{project}' references frozen V2 source.");
        if (!references.SequenceEqual(orderedAllowed, StringComparer.Ordinal))
            throw new InvalidOperationException(
                $"Project-reference boundary changed for '{project}'.\n" +
                $"Expected: {string.Join(", ", orderedAllowed)}\nActual: {string.Join(", ", references)}");
    }
}

static string FindRepositoryRoot()
{
    foreach (string start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
    {
        DirectoryInfo? directory = new(Path.GetFullPath(start));
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "S0 Integration Design.md")) &&
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
