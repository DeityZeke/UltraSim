namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptCompileResult(
    UltraSimScriptCompileResultKind Kind,
    string CacheKey,
    string? AssemblyPath,
    string? ManifestPath,
    int SourceCount,
    IReadOnlyList<UltraSimScriptCompileDiagnostic> Diagnostics)
{
    public bool Succeeded => Kind is UltraSimScriptCompileResultKind.Compiled or UltraSimScriptCompileResultKind.CacheHit or UltraSimScriptCompileResultKind.NoSources;
}
