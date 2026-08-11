using UltraSim.ECS;
using UltraSim.Network;
using UltraSim.Server.Admission;
using UltraSim.Server.Scripting;
using UltraSim.UI;

namespace UltraSim.Server.Network;

public delegate bool ServerScriptPrefabSpawner(string prefabKey, out Entity entity);

public sealed class ServerScriptRuntimeBinding
{
    private readonly DefaultUltraSimScriptContext _scriptContext;
    private readonly ServerUiService _ui;
    private readonly ServerScriptPrefabSpawner? _spawnPrefab;
    private readonly ServerAdmissionFlowRuntime _admission;
    private readonly string _commandPrefix;
    private long _nextScriptPanelSequence;

    public ServerScriptRuntimeBinding(
        DefaultUltraSimScriptContext scriptContext,
        ServerCommandRegistry commands,
        ServerUiService ui,
        ServerScriptPrefabSpawner? spawnPrefab = null,
        IAdmissionCredentialValidator? credentialValidator = null,
        IAdmissionRuntimePolicy? admissionRuntimePolicy = null,
        string commandPrefix = "/")
    {
        _scriptContext = scriptContext ?? throw new ArgumentNullException(nameof(scriptContext));
        ArgumentNullException.ThrowIfNull(commands);
        _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        _spawnPrefab = spawnPrefab;
        if (string.IsNullOrWhiteSpace(commandPrefix))
            throw new ArgumentException("Command prefix cannot be empty.", nameof(commandPrefix));
        _commandPrefix = commandPrefix.Trim();
        _admission = new ServerAdmissionFlowRuntime(
            _scriptContext,
            new ServerUiAdmissionPanelGateway(_ui),
            credentialValidator,
            admissionRuntimePolicy);
        _ui.ActionReceived += _admission.ProcessUiAction;

        RegisterPanelOpenCommand(commands);
        if (_spawnPrefab is not null)
            RegisterSpawnCommand(commands);
        _scriptContext.RegisterCommandsInto(commands, _commandPrefix);
    }

    public DefaultUltraSimScriptContext ScriptContext => _scriptContext;
    public ServerAdmissionFlowRuntime Admission => _admission;

    private void RegisterPanelOpenCommand(ServerCommandRegistry commands)
    {
        commands.Register(
            "panel",
            (in ServerCommandRequest request, ServerTextCommandInvocation invocation) =>
            {
                if (invocation.Arguments.Count == 0)
                {
                    request.Reply?.Invoke($"Usage: {_commandPrefix}panel <script-panel-key>");
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
            prefix: _commandPrefix,
            helpText: "Open a script-registered UI panel.");
    }

    private void RegisterSpawnCommand(ServerCommandRegistry commands)
    {
        commands.Register(
            "spawn",
            (in ServerCommandRequest request, ServerTextCommandInvocation invocation) =>
            {
                if (invocation.Arguments.Count == 0)
                {
                    request.Reply?.Invoke($"Usage: {_commandPrefix}spawn <prefab-key>");
                    return;
                }

                string prefabKey = invocation.Arguments[0].Trim();
                if (prefabKey.Length == 0)
                {
                    request.Reply?.Invoke($"Usage: {_commandPrefix}spawn <prefab-key>");
                    return;
                }

                try
                {
                    if (_spawnPrefab is null || !_spawnPrefab(prefabKey, out Entity entity))
                    {
                        request.Reply?.Invoke($"Unknown or unspawnable prefab '{prefabKey}'.");
                        return;
                    }

                    request.Reply?.Invoke($"Spawned prefab '{prefabKey}' as {entity}.");
                }
                catch (Exception ex)
                {
                    request.Reply?.Invoke($"Spawn failed for prefab '{prefabKey}': {ex.Message}");
                }
            },
            prefix: _commandPrefix,
            helpText: "Spawn a registered script prefab.");
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

    private sealed class ServerUiAdmissionPanelGateway : IAdmissionPanelGateway
    {
        private readonly ServerUiService _ui;

        public ServerUiAdmissionPanelGateway(ServerUiService ui)
        {
            _ui = ui ?? throw new ArgumentNullException(nameof(ui));
        }

        public bool TryOpenPanel(ConnectionId connectionId, UiBuiltPanel panel, string panelInstanceId)
        {
            return _ui.TryOpenPanel(connectionId, panel, panelInstanceId);
        }

        public bool TryUpdatePanel(ConnectionId connectionId, UiModelUpdateDocument update)
        {
            return _ui.TryUpdatePanel(connectionId, update);
        }
    }
}
