using UltraSim.UI;

namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptUiRegistry
{
    bool RegisterPanelProvider(string key, Func<UiBuiltPanel> provider);

    bool RegisterPanelProviderForContentGroup(
        string contentGroupKey,
        string key,
        Func<UiBuiltPanel> provider);

    bool UnregisterPanelProvider(string key);
}
