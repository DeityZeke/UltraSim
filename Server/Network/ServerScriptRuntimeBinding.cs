using UltraSim.Network;
using UltraSim.Server.Scripting;
using UltraSim.UI;

namespace UltraSim.Server.Network;

public sealed class ServerScriptRuntimeBinding
{
    private readonly DefaultUltraSimScriptContext _scriptContext;
    private readonly ServerUiService _ui;
    private long _nextScriptPanelSequence;

    public ServerScriptRuntimeBinding(
        DefaultUltraSimScriptContext scriptContext,
        ServerCommandRegistry commands,
        ServerUiService ui)
    {
        _scriptContext = scriptContext ?? throw new ArgumentNullException(nameof(scriptContext));
        ArgumentNullException.ThrowIfNull(commands);
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));

        RegisterPanelOpenCommand(commands);
        _scriptContext.RegisterCommandsInto(commands);
    }

    public DefaultUltraSimScriptContext ScriptContext => _scriptContext;

    private void RegisterPanelOpenCommand(ServerCommandRegistry commands)
    {
        commands.Register(
            "panel",
            (in ServerCommandRequest request, ServerTextCommandInvocation invocation) =>
            {
                if (invocation.Arguments.Count == 0)
                {
                    request.Reply?.Invoke("Usage: /panel <script-panel-key>");
                    return;
                }

                string panelKey = invocation.Arguments[0];
                if (!_scriptContext.TryGetPanelProvider(panelKey, out UltraSimScriptUiPanelProviderDescriptor provider))
                {
                    request.Reply?.Invoke($"Unknown script panel '{panelKey}'.");
                    return;
                }

                var connectionId = new ConnectionId(request.ConnectionId);
                if (!connectionId.IsValid)
                {
                    request.Reply?.Invoke("Script panels can only be opened from a live client session.");
                    return;
                }

                UiBuiltPanel panel;
                try
                {
                    panel = provider.Provider();
                }
                catch (Exception ex)
                {
                    request.Reply?.Invoke($"Script panel '{panelKey}' failed to build: {ex.Message}");
                    return;
                }

                long sequence = Interlocked.Increment(ref _nextScriptPanelSequence);
                string instanceId = $"script.panel/{SanitizeStableSegment(provider.Key)}/{request.ConnectionId}/{sequence}";
                if (!_ui.TryOpenPanel(connectionId, panel, instanceId))
                {
                    request.Reply?.Invoke($"Script panel '{panelKey}' could not be opened.");
                    return;
                }

                request.Reply?.Invoke($"Opened script panel '{panelKey}'.");
            },
            prefix: "/",
            helpText: "Open a script-registered UI panel.");
    }

    private static string SanitizeStableSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "panel";

        Span<char> buffer = stackalloc char[Math.Min(value.Length, 64)];
        int length = 0;
        for (int i = 0; i < value.Length && length < buffer.Length; i++)
        {
            char c = value[i];
            bool valid =
                c is >= 'a' and <= 'z' ||
                c is >= 'A' and <= 'Z' ||
                c is >= '0' and <= '9' ||
                c is '.' or '_' or '-' or ':' or '/';
            buffer[length++] = valid ? c : '_';
        }

        string sanitized = new(buffer[..length]);
        sanitized = sanitized.Trim('/');
        return sanitized.Length == 0 ? "panel" : sanitized;
    }
}
