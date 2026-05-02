namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptContext
{
    string ScriptApiVersion { get; }
    UltraSimRuntimeRootLayout RuntimeRoot { get; }
    IUltraSimScriptLogger Logger { get; }
    IUltraSimScriptCommandRegistry Commands { get; }
    IUltraSimScriptUiRegistry UiPanels { get; }
    IUltraSimScriptAdmissionRegistry Admission { get; }
    IUltraSimScriptDefinitionRegistry Definitions { get; }
}
