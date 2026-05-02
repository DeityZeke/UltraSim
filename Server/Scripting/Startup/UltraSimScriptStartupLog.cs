using UltraSim.Server;

namespace UltraSim.Server.Scripting;

public static class UltraSimScriptStartupLog
{
    public static IReadOnlyList<ServerApplicationEvent> CreateEvents(UltraSimScriptStartupResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var events = new List<ServerApplicationEvent>(4);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        UltraSimScriptCompileResult compile = result.CompileResult;
        string cached = compile.Kind == UltraSimScriptCompileResultKind.CacheHit ? ", cached" : string.Empty;
        string sourceNoun = compile.SourceCount == 1 ? "source file" : "source files";

        if (!compile.Succeeded)
        {
            events.Add(new ServerApplicationEvent(
                ServerApplicationEventKind.Log,
                now,
                $"Scripts: Compiling C# scripts... - failed ({result.WarningCount} warnings, {result.ErrorCount} errors, {compile.SourceCount} {sourceNoun})"));
            foreach (UltraSimScriptCompileDiagnostic diagnostic in compile.Diagnostics)
            {
                if (diagnostic.Severity == UltraSimScriptDiagnosticSeverity.Error)
                    events.Add(new ServerApplicationEvent(ServerApplicationEventKind.Log, now, $"  {diagnostic.Id}: {diagnostic.Message}"));
            }

            return events;
        }

        events.Add(new ServerApplicationEvent(
            ServerApplicationEventKind.Log,
            now,
            $"Scripts: Compiling C# scripts... - done ({result.WarningCount} warnings, {result.ErrorCount} errors, {compile.SourceCount} {sourceNoun}{cached})"));

        UltraSimScriptLoadResult? load = result.LoadResult;
        if (load is null)
        {
            events.Add(new ServerApplicationEvent(ServerApplicationEventKind.Log, now, "Scripts: Configuring... - done (0 modules)"));
            events.Add(new ServerApplicationEvent(ServerApplicationEventKind.Log, now, "Scripts: Initializing... - done (0 modules, 0 commands, 0 panels, 0 admission steps)"));
            return events;
        }

        if (!load.Succeeded)
        {
            events.Add(new ServerApplicationEvent(ServerApplicationEventKind.Log, now, $"Scripts: Configuring... - failed ({load.Errors.Count} errors)"));
            foreach (string error in load.Errors)
                events.Add(new ServerApplicationEvent(ServerApplicationEventKind.Log, now, $"  {error}"));
            return events;
        }

        string moduleNoun = result.ModuleCount == 1 ? "module" : "modules";
        string commandNoun = result.CommandCount == 1 ? "command" : "commands";
        string panelNoun = result.PanelProviderCount == 1 ? "panel" : "panels";
        string stepNoun = result.AdmissionStepCount == 1 ? "admission step" : "admission steps";
        events.Add(new ServerApplicationEvent(ServerApplicationEventKind.Log, now, $"Scripts: Configuring... - done ({result.ModuleCount} {moduleNoun})"));
        events.Add(new ServerApplicationEvent(ServerApplicationEventKind.Log, now, $"Scripts: Initializing... - done ({result.ModuleCount} {moduleNoun}, {result.CommandCount} {commandNoun}, {result.PanelProviderCount} {panelNoun}, {result.AdmissionStepCount} {stepNoun})"));
        return events;
    }
}
