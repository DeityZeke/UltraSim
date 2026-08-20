namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptCompileDiagnostic(
    UltraSimScriptDiagnosticSeverity Severity,
    string Id,
    string Message,
    string? FilePath,
    int Line,
    int Column);
