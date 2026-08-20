namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptCompilerOptions(
    UltraSimRuntimeRootLayout RuntimeRoot,
    string ScriptApiVersion = "1",
    bool TreatWarningsAsErrors = false,
    string AssemblyName = "UltraSim.Server.Scripts",
    IReadOnlyList<string>? AdditionalReferencePaths = null);
