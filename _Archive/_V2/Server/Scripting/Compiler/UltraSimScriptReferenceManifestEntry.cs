namespace UltraSim.Server.Scripting;

internal sealed record UltraSimScriptReferenceManifestEntry(
    string Identity,
    string Path,
    string? Sha256,
    long Length);
