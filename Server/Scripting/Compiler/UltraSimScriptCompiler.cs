using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using UltraSim.Server;
using UltraSim.UI;

namespace UltraSim.Server.Scripting;

public sealed class UltraSimScriptCompiler
{
    private static readonly Lazy<ReferenceInput[]> DefaultReferenceInputs = new(
        () => BuildDefaultReferences().ToArray(),
        isThreadSafe: true);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };

    public UltraSimScriptCompileResult Compile(UltraSimScriptCompilerOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (string.IsNullOrWhiteSpace(options.AssemblyName))
            throw new ArgumentException("Script assembly name cannot be empty.", nameof(options));

        Directory.CreateDirectory(options.RuntimeRoot.ScriptsPath);
        Directory.CreateDirectory(options.RuntimeRoot.ScriptCachePath);
        Directory.CreateDirectory(options.RuntimeRoot.ScriptAssemblyCachePath);
        Directory.CreateDirectory(options.RuntimeRoot.ScriptManifestCachePath);
        Directory.CreateDirectory(options.RuntimeRoot.ScriptDiagnosticsCachePath);

        string[] sourcePaths = Directory
            .EnumerateFiles(options.RuntimeRoot.ScriptsPath, "*.cs", SearchOption.AllDirectories)
            .OrderBy(static path => path, StringComparer.Ordinal)
            .ToArray();

        if (sourcePaths.Length == 0)
        {
            return new UltraSimScriptCompileResult(
                UltraSimScriptCompileResultKind.NoSources,
                CacheKey: string.Empty,
                AssemblyPath: null,
                ManifestPath: null,
                SourceCount: 0,
                Diagnostics: Array.Empty<UltraSimScriptCompileDiagnostic>());
        }

        SourceInput[] sources = sourcePaths
            .Select(path => new SourceInput(
                path,
                Path.GetRelativePath(options.RuntimeRoot.RootPath, path).Replace(Path.DirectorySeparatorChar, '/'),
                File.ReadAllText(path, Encoding.UTF8)))
            .ToArray();

        ReferenceInput[] references = BuildReferences(options.AdditionalReferencePaths).ToArray();
        string compilerVersion = typeof(CSharpCompilation).Assembly.GetName().Version?.ToString() ?? "unknown";
        string cacheKey = ComputeCacheKey(options, compilerVersion, sources, references);
        string assemblyPath = Path.Combine(options.RuntimeRoot.ScriptAssemblyCachePath, $"{options.AssemblyName}.{cacheKey}.dll");
        string currentManifestPath = Path.Combine(options.RuntimeRoot.ScriptManifestCachePath, "current.json");
        string keyedManifestPath = Path.Combine(options.RuntimeRoot.ScriptManifestCachePath, $"{cacheKey}.json");
        string diagnosticsPath = Path.Combine(options.RuntimeRoot.ScriptDiagnosticsCachePath, $"{cacheKey}.json");

        if (TryReadManifest(currentManifestPath, out UltraSimScriptCacheManifest? manifest) &&
            manifest is not null &&
            !string.IsNullOrWhiteSpace(manifest.AssemblyPath) &&
            string.Equals(manifest.CacheKey, cacheKey, StringComparison.Ordinal) &&
            string.Equals(Path.GetFullPath(manifest.AssemblyPath), Path.GetFullPath(assemblyPath), StringComparison.Ordinal) &&
            IsUsableAssembly(assemblyPath))
        {
            return new UltraSimScriptCompileResult(
                UltraSimScriptCompileResultKind.CacheHit,
                cacheKey,
                assemblyPath,
                currentManifestPath,
                sources.Length,
                Array.Empty<UltraSimScriptCompileDiagnostic>());
        }

        CSharpParseOptions parseOptions = CSharpParseOptions.Default
            .WithLanguageVersion(LanguageVersion.Latest)
            .WithDocumentationMode(DocumentationMode.None)
            .WithKind(SourceCodeKind.Regular);
        SyntaxTree[] syntaxTrees = sources
            .Select(source => CSharpSyntaxTree.ParseText(source.Text, parseOptions, source.FullPath, Encoding.UTF8))
            .ToArray();

        var compilationOptions = new CSharpCompilationOptions(
            OutputKind.DynamicallyLinkedLibrary,
            optimizationLevel: OptimizationLevel.Release,
            warningLevel: 4,
            concurrentBuild: true,
            deterministic: true,
            allowUnsafe: false,
            nullableContextOptions: NullableContextOptions.Enable,
            generalDiagnosticOption: options.TreatWarningsAsErrors ? ReportDiagnostic.Error : ReportDiagnostic.Default);

        CSharpCompilation compilation = CSharpCompilation.Create(
            options.AssemblyName,
            syntaxTrees,
            references.Select(static reference => MetadataReference.CreateFromFile(reference.Path)),
            compilationOptions);

        Directory.CreateDirectory(Path.GetDirectoryName(assemblyPath)!);
        using var output = File.Open(assemblyPath, FileMode.Create, FileAccess.Write, FileShare.None);
        Microsoft.CodeAnalysis.Emit.EmitResult emit = compilation.Emit(output);
        output.Flush();

        UltraSimScriptCompileDiagnostic[] diagnostics = emit.Diagnostics
            .Where(static diagnostic => diagnostic.Severity != DiagnosticSeverity.Hidden)
            .Select(ConvertDiagnostic)
            .ToArray();
        File.WriteAllText(diagnosticsPath, JsonSerializer.Serialize(diagnostics, JsonOptions), Encoding.UTF8);

        if (!emit.Success)
        {
            TryDelete(assemblyPath);
            return new UltraSimScriptCompileResult(
                UltraSimScriptCompileResultKind.Failed,
                cacheKey,
                AssemblyPath: null,
                ManifestPath: null,
                sources.Length,
                diagnostics);
        }

        var newManifest = new UltraSimScriptCacheManifest(
            cacheKey,
            options.AssemblyName,
            assemblyPath,
            options.ScriptApiVersion,
            compilerVersion,
            options.TreatWarningsAsErrors,
            DateTimeOffset.UtcNow,
            sources.Select(static source => new UltraSimScriptSourceManifestEntry(
                source.RelativePath,
                Sha256Hex(source.Text),
                Encoding.UTF8.GetByteCount(source.Text))).ToArray(),
            references.Select(static reference => new UltraSimScriptReferenceManifestEntry(
                reference.Identity,
                reference.Path,
                reference.Hash,
                reference.Length)).ToArray());

        string manifestJson = JsonSerializer.Serialize(newManifest, JsonOptions);
        File.WriteAllText(keyedManifestPath, manifestJson, Encoding.UTF8);
        File.WriteAllText(currentManifestPath, manifestJson, Encoding.UTF8);

        return new UltraSimScriptCompileResult(
            UltraSimScriptCompileResultKind.Compiled,
            cacheKey,
            assemblyPath,
            currentManifestPath,
            sources.Length,
            diagnostics);
    }

    private static IReadOnlyList<ReferenceInput> BuildReferences(IReadOnlyList<string>? additionalReferencePaths)
    {
        if (additionalReferencePaths is null || additionalReferencePaths.Count == 0)
            return DefaultReferenceInputs.Value;

        var references = new Dictionary<string, ReferenceInput>(StringComparer.Ordinal);
        foreach (ReferenceInput reference in DefaultReferenceInputs.Value)
            references[reference.Path] = reference;

        foreach (ReferenceInput reference in BuildAdditionalReferences(additionalReferencePaths))
            references[reference.Path] = reference;

        return references.Values.OrderBy(static reference => reference.Path, StringComparer.Ordinal).ToArray();
    }

    private static IEnumerable<ReferenceInput> BuildDefaultReferences()
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        string? trustedPlatformAssemblies = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        if (!string.IsNullOrWhiteSpace(trustedPlatformAssemblies))
        {
            foreach (string path in trustedPlatformAssemblies.Split(Path.PathSeparator))
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    paths.Add(Path.GetFullPath(path));
            }
        }

        AddAssembly(paths, typeof(object).Assembly);
        AddAssembly(paths, typeof(Enumerable).Assembly);
        AddAssembly(paths, typeof(IUltraSimScriptModule).Assembly);
        AddAssembly(paths, typeof(ServerCommandRegistry).Assembly);
        AddAssembly(paths, typeof(UltraSim.ECS.IComponent<>).Assembly);
        AddAssembly(paths, typeof(UiPanelBuilder).Assembly);

        foreach (string path in paths)
        {
            if (TryBuildReference(path, out ReferenceInput? reference))
                yield return reference!;
        }
    }

    private static IEnumerable<ReferenceInput> BuildAdditionalReferences(IReadOnlyList<string> additionalReferencePaths)
    {
        var paths = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string path in additionalReferencePaths)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                paths.Add(Path.GetFullPath(path));
        }

        foreach (string path in paths)
        {
            if (TryBuildReference(path, out ReferenceInput? reference))
                yield return reference!;
        }
    }

    private static bool TryBuildReference(string path, out ReferenceInput? reference)
    {
        reference = null;
        AssemblyName identity;
        try
        {
            identity = AssemblyName.GetAssemblyName(path);
        }
        catch
        {
            return false;
        }

        bool hashReference = IsUltraSimReference(identity);
        reference = new ReferenceInput(
            path,
            identity.FullName,
            hashReference ? Sha256FileHex(path) : null,
            new FileInfo(path).Length);
        return true;
    }

    private static bool TryReadManifest(string path, out UltraSimScriptCacheManifest? manifest)
    {
        manifest = null;
        if (!File.Exists(path))
            return false;

        try
        {
            manifest = JsonSerializer.Deserialize<UltraSimScriptCacheManifest>(File.ReadAllText(path, Encoding.UTF8), JsonOptions);
            return manifest is not null;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsUsableAssembly(string path)
    {
        if (!File.Exists(path))
            return false;

        try
        {
            _ = AssemblyName.GetAssemblyName(path);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void AddAssembly(SortedSet<string> paths, Assembly assembly)
    {
        if (!string.IsNullOrWhiteSpace(assembly.Location) && File.Exists(assembly.Location))
            paths.Add(Path.GetFullPath(assembly.Location));
    }

    private static bool IsUltraSimReference(AssemblyName identity) =>
        identity.Name?.StartsWith("UltraSim.", StringComparison.Ordinal) == true;

    private static string ComputeCacheKey(
        UltraSimScriptCompilerOptions options,
        string compilerVersion,
        IReadOnlyList<SourceInput> sources,
        IReadOnlyList<ReferenceInput> references)
    {
        var builder = new StringBuilder();
        builder.Append("api=").Append(options.ScriptApiVersion).Append('\n');
        builder.Append("compiler=").Append(compilerVersion).Append('\n');
        builder.Append("assembly=").Append(options.AssemblyName).Append('\n');
        builder.Append("warningsAsErrors=").Append(options.TreatWarningsAsErrors).Append('\n');
        foreach (SourceInput source in sources)
        {
            builder.Append("src=").Append(source.RelativePath).Append('|').Append(Sha256Hex(source.Text)).Append('\n');
        }

        foreach (ReferenceInput reference in references)
        {
            builder.Append("ref=").Append(reference.Identity).Append('|').Append(reference.Hash ?? reference.Length.ToString()).Append('\n');
        }

        return Sha256Hex(builder.ToString())[..24];
    }

    private static UltraSimScriptCompileDiagnostic ConvertDiagnostic(Diagnostic diagnostic)
    {
        FileLinePositionSpan span = diagnostic.Location.GetLineSpan();
        string? path = span.Path;
        int line = span.StartLinePosition.Line >= 0 ? span.StartLinePosition.Line + 1 : 0;
        int column = span.StartLinePosition.Character >= 0 ? span.StartLinePosition.Character + 1 : 0;
        return new UltraSimScriptCompileDiagnostic(
            ConvertSeverity(diagnostic.Severity),
            diagnostic.Id,
            diagnostic.GetMessage(),
            string.IsNullOrWhiteSpace(path) ? null : path,
            line,
            column);
    }

    private static UltraSimScriptDiagnosticSeverity ConvertSeverity(DiagnosticSeverity severity) =>
        severity switch
        {
            DiagnosticSeverity.Error => UltraSimScriptDiagnosticSeverity.Error,
            DiagnosticSeverity.Warning => UltraSimScriptDiagnosticSeverity.Warning,
            DiagnosticSeverity.Info => UltraSimScriptDiagnosticSeverity.Info,
            _ => UltraSimScriptDiagnosticSeverity.Hidden,
        };

    private static string Sha256Hex(string text)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string Sha256FileHex(string path)
    {
        using FileStream stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
        }
    }

    private sealed record SourceInput(string FullPath, string RelativePath, string Text);

    private sealed record ReferenceInput(string Path, string Identity, string? Hash, long Length);
}
