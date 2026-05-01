using UltraSim.UI;

namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptUiRegistry
{
    bool RegisterPanelProvider(string key, Func<UiBuiltPanel> provider);
}
