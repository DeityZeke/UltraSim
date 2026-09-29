using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;
using UltraSim.Framework.ControlPlane;
using UltraSim.Framework.Definitions;
using UltraSim.Framework.Persistence;
using UltraSim.Framework.RuntimeHost;

namespace UltraSim.Server.Authoring;

public sealed record CompiledScriptPackage(RuntimePackageManifest Manifest, string CompileKey,
    string AssemblyName, string AssemblyHash, bool CacheHit, byte[] AssemblyBytes);

public sealed class ServerScriptCompilationException : Exception
{
    public ServerScriptCompilationException(RuntimePackageId package, string path, int line, int column,
        string code, string message) : base($"{package}:{path}:{line}:{column} {code}: {message}")
    { Package = package; Path = path; Line = line; Column = column; Code = code; }
    public RuntimePackageId Package { get; }
    public string Path { get; }
    public int Line { get; }
    public int Column { get; }
    public string Code { get; }
}

/// <summary>Cold Roslyn adapter. RuntimeHost never references compiler assemblies.</summary>
public static class ServerScriptCompiler
{
    public const string CompilerProfile = "Roslyn-4.5-CSharp11-Release-Nullable-Checked-v1";
    public const int MaximumAssemblyBytes = 4 * 1024 * 1024;
    private static readonly CSharpParseOptions ParseOptions = new(LanguageVersion.CSharp11);
    private static readonly CSharpCompilationOptions CompilationOptions = new(
        OutputKind.DynamicallyLinkedLibrary, optimizationLevel: OptimizationLevel.Release,
        checkOverflow: true, allowUnsafe: false, deterministic: true,
        concurrentBuild: false, nullableContextOptions: NullableContextOptions.Enable);

    public static IReadOnlyList<CompiledScriptPackage> Compile(
        IReadOnlyList<ServerScriptPackage> sources, RuntimeCompositionPlan plan, string cacheRoot)
    {
        ArgumentNullException.ThrowIfNull(sources); ArgumentNullException.ThrowIfNull(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(cacheRoot);
        if (sources.Count > RuntimeCompositionPlanner.MaximumPackages)
            throw new InvalidOperationException("Compiler package capacity exceeded.");
        var byId = new Dictionary<RuntimePackageId, ServerScriptPackage>();
        foreach (ServerScriptPackage? source in sources)
        {
            if (source?.Manifest is null || !byId.TryAdd(source.Manifest.Id, source))
                throw new InvalidOperationException("Compiler package input is null or duplicated.");
        }
        if (byId.Count != plan.OrderedPackages.Count ||
            plan.OrderedPackages.Any(p => !byId.TryGetValue(p.Id, out var source) || !p.HasSameMeaning(source.Manifest)))
            throw new InvalidOperationException("Compiler inputs differ from the resolved package plan.");
        var references = ReferenceSet.Create();
        var compiled = new Dictionary<RuntimePackageId, CompiledScriptPackage>();
        var results = new List<CompiledScriptPackage>(sources.Count);
        string fullCacheRoot = Path.GetFullPath(cacheRoot);
        Directory.CreateDirectory(fullCacheRoot);
        for (DirectoryInfo? parent = new(fullCacheRoot); parent is not null; parent = parent.Parent)
            if ((File.GetAttributes(parent.FullName) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Reparse/symlink compile-cache path is not admitted.");
        foreach (RuntimePackageManifest manifest in plan.OrderedPackages)
        {
            ServerScriptPackage package = byId[manifest.Id];
            RuntimePackageId[] dependencies = CompileDependencies(manifest, plan);
            CompiledScriptPackage[] admitted = dependencies.Select(id => compiled[id]).ToArray();
            string key = CompileKey(package, admitted, references);
            string assemblyName = "UltraSim.Script." +
                new string(manifest.Id.Value.Select(c => char.IsAsciiLetterOrDigit(c) ? c : '_').ToArray()) +
                "_" + key[..24];
            byte[]? bytes = TryReadCache(fullCacheRoot, key, assemblyName);
            bool cacheHit = bytes is not null;
            if (bytes is null)
            {
                var syntax = package.Sources.Select(source => CSharpSyntaxTree.ParseText(
                    SourceText.From(new UTF8Encoding(false, true).GetString(source.Bytes), Encoding.UTF8),
                    ParseOptions, package.Manifest.Id.Value + "/" + source.RelativePath)).ToArray();
                var packageReferences = references.Metadata.ToList();
                packageReferences.AddRange(admitted.Select(p => MetadataReference.CreateFromImage(p.AssemblyBytes)));
                CSharpCompilation compilation = CSharpCompilation.Create(assemblyName, syntax,
                    packageReferences, CompilationOptions);
                using var output = new MemoryStream();
                var emitted = compilation.Emit(output);
                if (!emitted.Success)
                {
                    Diagnostic error = emitted.Diagnostics.First(d => d.Severity == DiagnosticSeverity.Error);
                    var span = error.Location.GetLineSpan();
                    throw new ServerScriptCompilationException(manifest.Id,
                        span.IsValid ? span.Path : "<compilation>",
                        span.IsValid ? span.StartLinePosition.Line + 1 : 0,
                        span.IsValid ? span.StartLinePosition.Character + 1 : 0,
                        error.Id, Bounded(error.GetMessage()));
                }
                if (output.Length is < 1 or > MaximumAssemblyBytes)
                    throw new InvalidDataException($"Compiled output byte bound exceeded for {manifest.Id}.");
                bytes = output.ToArray();
                PublishCache(fullCacheRoot, key, assemblyName, bytes);
            }
            var result = new CompiledScriptPackage(manifest, key, assemblyName,
                ServerScriptSources.Hash(bytes), cacheHit, bytes);
            compiled.Add(manifest.Id, result);
            results.Add(result);
        }
        return results;
    }

    private static RuntimePackageId[] CompileDependencies(RuntimePackageManifest manifest, RuntimeCompositionPlan plan)
    {
        var ids = new HashSet<RuntimePackageId>(manifest.Dependencies.Select(d => plan.ResolveId(d.Package)));
        foreach (RuntimePackageManifest provider in plan.OrderedPackages)
        {
            if (provider.Id == manifest.Id) continue;
            if (provider.Provides.Any(id => manifest.Requires.Contains(id) || manifest.Optional.Contains(id)) ||
                provider.Services.Any(id => manifest.RequiresServices.Contains(id) || manifest.OptionalServices.Contains(id)))
                ids.Add(provider.Id);
        }
        return ids.Order().ToArray();
    }

    private static string CompileKey(ServerScriptPackage package, IReadOnlyList<CompiledScriptPackage> dependencies,
        ReferenceSet references)
    {
        var text = new StringBuilder("UltraSim.ScriptCompile/v1\n");
        text.Append(CompilerProfile).Append('|').Append(typeof(CSharpCompilation).Assembly.GetName().Version)
            .Append('|').Append(RuntimeInformation.ProcessArchitecture).Append('|')
            .Append(RuntimeInformation.FrameworkDescription).Append('\n');
        text.Append(package.Manifest.Id).Append('|').Append(package.Manifest.Version).Append('|')
            .Append(package.ManifestHash).Append('\n');
        foreach (ServerScriptSource source in package.Sources.OrderBy(s => s.RelativePath, StringComparer.Ordinal))
            text.Append(source.RelativePath).Append('|').Append(source.Hash).Append('\n');
        foreach (CompiledScriptPackage dependency in dependencies.OrderBy(d => d.Manifest.Id))
            text.Append(dependency.Manifest.Id).Append('|').Append(dependency.CompileKey).Append('|')
                .Append(dependency.AssemblyHash).Append('\n');
        foreach (var identity in references.Identities)
            text.Append(identity).Append('\n');
        return ServerScriptSources.Hash(Encoding.UTF8.GetBytes(text.ToString()));
    }

    private sealed record CacheMetadata(string Key, string AssemblyName, string AssemblyHash);
    private static byte[]? TryReadCache(string root, string key, string assemblyName)
    {
        string entry = Path.Combine(root, key);
        if (!Directory.Exists(entry)) return null;
        if ((File.GetAttributes(entry) & FileAttributes.ReparsePoint) != 0) return null;
        try
        {
            string metadataPath = Path.Combine(entry, "metadata.json");
            string assemblyPath = Path.Combine(entry, "assembly.dll");
            if (Directory.GetFileSystemEntries(entry).Length != 2 ||
                !File.Exists(metadataPath) || !File.Exists(assemblyPath)) return null;
            if (new FileInfo(metadataPath).Length > 4096 ||
                (File.GetAttributes(metadataPath) & FileAttributes.ReparsePoint) != 0 ||
                (File.GetAttributes(assemblyPath) & FileAttributes.ReparsePoint) != 0)
                return null;
            CacheMetadata? metadata = JsonSerializer.Deserialize<CacheMetadata>(File.ReadAllBytes(metadataPath));
            if (metadata?.Key != key || metadata.AssemblyName != assemblyName) return null;
            var info = new FileInfo(assemblyPath);
            if (info.Length is < 1 or > MaximumAssemblyBytes) return null;
            byte[] bytes = File.ReadAllBytes(assemblyPath);
            if (ServerScriptSources.Hash(bytes) != metadata.AssemblyHash) return null;
            using var image = new MemoryStream(bytes, writable: false);
            using var reader = new PEReader(image);
            if (!reader.HasMetadata) return null;
            var metadataReader = reader.GetMetadataReader();
            if (!metadataReader.IsAssembly ||
                metadataReader.GetString(metadataReader.GetAssemblyDefinition().Name) != assemblyName) return null;
            return bytes;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or
            ArgumentException or BadImageFormatException)
        { return null; }
    }

    private static void PublishCache(string root, string key, string assemblyName, byte[] bytes)
    {
        string destination = Path.Combine(root, key);
        string staging = Path.Combine(root, ".staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            File.WriteAllBytes(Path.Combine(staging, "assembly.dll"), bytes);
            File.WriteAllBytes(Path.Combine(staging, "metadata.json"), JsonSerializer.SerializeToUtf8Bytes(
                new CacheMetadata(key, assemblyName, ServerScriptSources.Hash(bytes))));
            if (Directory.Exists(destination))
            {
                if (TryReadCache(root, key, assemblyName) is not null) return;
                if ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Reparse/symlink compile-cache entry is not admitted.");
                foreach (string path in Directory.GetFileSystemEntries(destination))
                {
                    if (Path.GetFileName(path) is not ("assembly.dll" or "metadata.json") ||
                        Directory.Exists(path) || (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Unexpected cache entry content; refusing destructive replacement.");
                    File.Delete(path);
                }
                Directory.Delete(destination);
            }
            Directory.Move(staging, destination);
        }
        finally { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
    }

    private static string Bounded(string message) => message.Length <= 512 ? message : message[..512];

    private sealed record ReferenceSet(IReadOnlyList<MetadataReference> Metadata, IReadOnlyList<string> Identities)
    {
        internal static ReferenceSet Create()
        {
            string? tpa = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
            if (tpa is null) throw new InvalidOperationException("Runtime BCL reference set unavailable.");
            var paths = tpa.Split(Path.PathSeparator).Where(p =>
            {
                string name = Path.GetFileNameWithoutExtension(p);
                return name == "netstandard" || name == "mscorlib" || name == "Microsoft.CSharp" ||
                    name == "System" || name.StartsWith("System.", StringComparison.Ordinal);
            }).ToList();
            paths.Add(typeof(RuntimePackageHost).Assembly.Location);
            paths.Add(typeof(SaveSetCoordinator).Assembly.Location);
            paths.Add(typeof(ControlPlaneRegistry).Assembly.Location);
            paths.Add(typeof(DefinitionComposer).Assembly.Location);
            var ordered = paths.Distinct(StringComparer.Ordinal).OrderBy(p => Path.GetFileName(p), StringComparer.Ordinal).ToArray();
            var metadata = new List<MetadataReference>(ordered.Length);
            var identities = new List<string>(ordered.Length);
            foreach (string path in ordered)
            {
                byte[] image = File.ReadAllBytes(path);
                identities.Add(Path.GetFileName(path) + "|" + AssemblyName.GetAssemblyName(path).Version + "|" +
                    ServerScriptSources.Hash(image));
                metadata.Add(MetadataReference.CreateFromImage(image));
            }
            return new(metadata, identities);
        }
    }
}
