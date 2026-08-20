namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptStartupOptions(
    UltraSimRuntimeRootLayout RuntimeRoot,
    string ScriptApiVersion = "1",
    bool TreatWarningsAsErrors = false);
