namespace UltraSim.Server.Scripting;

public interface IUltraSimScriptCommandRegistry
{
    bool Register(
        string name,
        Action<UltraSimScriptCommandContext> handler,
        string helpText = "",
        int minAccessLevel = 0,
        string prefix = "/",
        IEnumerable<string>? aliases = null,
        string? permissionKey = null);

    bool RegisterForContentGroup(
        string contentGroupKey,
        string name,
        Action<UltraSimScriptCommandContext> handler,
        string helpText = "",
        int minAccessLevel = 0,
        string prefix = "/",
        IEnumerable<string>? aliases = null,
        string? permissionKey = null);

    bool Unregister(string name, string prefix = "/");
}
