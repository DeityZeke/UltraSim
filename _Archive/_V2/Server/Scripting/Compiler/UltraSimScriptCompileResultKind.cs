namespace UltraSim.Server.Scripting;

public enum UltraSimScriptCompileResultKind
{
    Compiled = 0,
    CacheHit = 1,
    NoSources = 2,
    Failed = 3,
}
