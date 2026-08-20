namespace UltraSim.Server.Scripting;

internal sealed record UltraSimScriptCacheManifest(
    string CacheKey,
    string AssemblyName,
    string AssemblyPath,
    string ScriptApiVersion,
    string CompilerVersion,
    bool TreatWarningsAsErrors,
    DateTimeOffset CreatedAtUtc,
    IReadOnlyList<UltraSimScriptSourceManifestEntry> Sources,
    IReadOnlyList<UltraSimScriptReferenceManifestEntry> References);
