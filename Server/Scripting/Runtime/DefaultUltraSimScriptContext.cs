using UltraSim.Server;
using UltraSim.UI;

namespace UltraSim.Server.Scripting;

public sealed class DefaultUltraSimScriptContext : IUltraSimScriptContext, IUltraSimScriptLogger, IUltraSimScriptCommandRegistry, IUltraSimScriptUiRegistry
{
    private readonly List<UltraSimScriptLogEntry> _logEntries = new();
    private readonly Dictionary<string, UltraSimScriptCommandDescriptor> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UltraSimScriptUiPanelProviderDescriptor> _panelProviders = new(StringComparer.Ordinal);

    public DefaultUltraSimScriptContext(UltraSimRuntimeRootLayout runtimeRoot, string scriptApiVersion = "1")
    {
        RuntimeRoot = runtimeRoot ?? throw new ArgumentNullException(nameof(runtimeRoot));
        ScriptApiVersion = string.IsNullOrWhiteSpace(scriptApiVersion) ? "1" : scriptApiVersion.Trim();
    }

    public string ScriptApiVersion { get; }
    public UltraSimRuntimeRootLayout RuntimeRoot { get; }
    public IUltraSimScriptLogger Logger => this;
    public IUltraSimScriptCommandRegistry Commands => this;
    public IUltraSimScriptUiRegistry UiPanels => this;
    public IReadOnlyList<UltraSimScriptLogEntry> LogEntries => _logEntries;
    public IReadOnlyCollection<UltraSimScriptCommandDescriptor> RegisteredCommands => _commands.Values;
    public IReadOnlyCollection<UltraSimScriptUiPanelProviderDescriptor> RegisteredPanelProviders => _panelProviders.Values;

    public void Info(string message) => Log(UltraSimScriptLogLevel.Info, message);

    public void Warning(string message) => Log(UltraSimScriptLogLevel.Warning, message);

    public void Error(string message) => Log(UltraSimScriptLogLevel.Error, message);

    public bool Register(
        string name,
        Action<UltraSimScriptCommandContext> handler,
        string helpText = "",
        int minAccessLevel = 0,
        string prefix = "/",
        IEnumerable<string>? aliases = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name cannot be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(handler);
        if (minAccessLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(minAccessLevel));

        string trimmedPrefix = string.IsNullOrWhiteSpace(prefix) ? "/" : prefix.Trim();
        string trimmedName = name.Trim();
        string key = MakeCommandKey(trimmedPrefix, trimmedName);
        if (_commands.ContainsKey(key))
            return false;

        string[] aliasArray = aliases?.Where(static alias => !string.IsNullOrWhiteSpace(alias)).Select(static alias => alias.Trim()).ToArray()
            ?? Array.Empty<string>();

        var descriptor = new UltraSimScriptCommandDescriptor(
            trimmedName,
            trimmedPrefix,
            minAccessLevel,
            helpText ?? string.Empty,
            handler,
            aliasArray);
        _commands.Add(key, descriptor);
        return true;
    }

    public bool RegisterPanelProvider(string key, Func<UiBuiltPanel> provider)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Panel provider key cannot be empty.", nameof(key));
        ArgumentNullException.ThrowIfNull(provider);

        string trimmed = key.Trim();
        if (_panelProviders.ContainsKey(trimmed))
            return false;

        _panelProviders.Add(trimmed, new UltraSimScriptUiPanelProviderDescriptor(trimmed, provider));
        return true;
    }

    public void RegisterCommandsInto(ServerCommandRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(registry);

        foreach (UltraSimScriptCommandDescriptor descriptor in _commands.Values)
        {
            registry.Register(
                descriptor.Name,
                (in ServerCommandRequest request, ServerTextCommandInvocation invocation) =>
                {
                    var context = new UltraSimScriptCommandContext(
                        request.RawText,
                        invocation.RawArguments,
                        invocation.Arguments,
                        request.AccessLevel,
                        request.SessionId,
                        request.ConnectionId,
                        request.SenderName,
                        request.Reply);
                    descriptor.Handler(context);
                },
                descriptor.Prefix,
                descriptor.MinAccessLevel,
                descriptor.HelpText,
                descriptor.Aliases);
        }
    }

    private void Log(UltraSimScriptLogLevel level, string message)
    {
        _logEntries.Add(new UltraSimScriptLogEntry(level, message ?? string.Empty, DateTimeOffset.UtcNow));
    }

    private static string MakeCommandKey(string prefix, string name) => $"{prefix}\u001F{name}";
}
