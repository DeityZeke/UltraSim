namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptStartupResult(
    UltraSimScriptCompileResult CompileResult,
    UltraSimScriptLoadResult? LoadResult,
    DefaultUltraSimScriptContext Context)
{
    public bool Succeeded => CompileResult.Succeeded && (LoadResult is null || LoadResult.Succeeded);
    public int WarningCount => CompileResult.Diagnostics.Count(static diagnostic => diagnostic.Severity == UltraSimScriptDiagnosticSeverity.Warning);
    public int ErrorCount => CompileResult.Diagnostics.Count(static diagnostic => diagnostic.Severity == UltraSimScriptDiagnosticSeverity.Error);
    public int ModuleCount => LoadResult?.ModuleCount ?? 0;
    public int CommandCount => Context.RegisteredCommands.Count;
    public int PanelProviderCount => Context.RegisteredPanelProviders.Count;
}
