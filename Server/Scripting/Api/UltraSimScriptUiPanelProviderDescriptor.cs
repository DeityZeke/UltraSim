using UltraSim.UI;

namespace UltraSim.Server.Scripting;

public sealed record UltraSimScriptUiPanelProviderDescriptor(
    string Key,
    Func<UiBuiltPanel> Provider);
