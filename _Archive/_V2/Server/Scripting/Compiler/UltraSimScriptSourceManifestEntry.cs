namespace UltraSim.Server.Scripting;

internal sealed record UltraSimScriptSourceManifestEntry(
    string RelativePath,
    string Sha256,
    long Length);
