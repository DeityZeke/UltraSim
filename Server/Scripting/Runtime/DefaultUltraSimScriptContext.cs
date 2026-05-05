using UltraSim.Server;
using UltraSim.UI;

namespace UltraSim.Server.Scripting;

public sealed class DefaultUltraSimScriptContext : IUltraSimScriptContext, IUltraSimScriptLogger, IUltraSimScriptCommandRegistry, IUltraSimScriptUiRegistry, IUltraSimScriptAdmissionRegistry, IUltraSimScriptDefinitionRegistry
{
    private readonly List<UltraSimScriptLogEntry> _logEntries = new();
    private readonly Dictionary<string, UltraSimScriptCommandDescriptor> _commands = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, UltraSimScriptUiPanelProviderDescriptor> _panelProviders = new(StringComparer.Ordinal);
    private readonly UltraSimReplaceableProviderRegistry<UltraSimScriptAdmissionFlowStepDescriptor> _admissionSteps = new();
    private readonly HashSet<string> _admissionStepEventKeys = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ArchetypeDescriptor> _archetypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, PrefabDescriptor> _prefabs = new(StringComparer.Ordinal);
    private readonly UltraSimRuntimeContentGroupRegistry _contentGroups = new();

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
    public IUltraSimScriptAdmissionRegistry Admission => this;
    public IUltraSimScriptDefinitionRegistry Definitions => this;
    public IUltraSimScriptContentGroupRegistry ContentGroups => _contentGroups;
    public IReadOnlyList<UltraSimScriptLogEntry> LogEntries => _logEntries;
    public IReadOnlyCollection<UltraSimScriptCommandDescriptor> RegisteredCommands => _commands.Values;
    public IReadOnlyCollection<UltraSimScriptUiPanelProviderDescriptor> RegisteredPanelProviders => _panelProviders.Values;
    public IReadOnlyCollection<UltraSimScriptAdmissionFlowStepDescriptor> RegisteredAdmissionSteps
    {
        get
        {
            var result = new List<UltraSimScriptAdmissionFlowStepDescriptor>(_admissionStepEventKeys.Count);
            foreach (string eventKey in _admissionStepEventKeys)
            {
                if (_admissionSteps.TryGetActive(eventKey, out UltraSimReplaceableProviderRegistration<UltraSimScriptAdmissionFlowStepDescriptor> registration) &&
                    registration.Provider is not null)
                {
                    result.Add(registration.Provider);
                }
            }

            return result;
        }
    }
    public IReadOnlyCollection<ArchetypeDescriptor> RegisteredArchetypes => _archetypes.Values;
    public IReadOnlyCollection<PrefabDescriptor> RegisteredPrefabs => _prefabs.Values;
    public UltraSimRuntimeContentGroupRegistry RuntimeContentGroups => _contentGroups;

    public void Info(string message) => Log(UltraSimScriptLogLevel.Info, message);

    public void Warning(string message) => Log(UltraSimScriptLogLevel.Warning, message);

    public void Error(string message) => Log(UltraSimScriptLogLevel.Error, message);

    public bool Register(
        string name,
        Action<UltraSimScriptCommandContext> handler,
        string helpText = "",
        int minAccessLevel = 0,
        string prefix = "/",
        IEnumerable<string>? aliases = null,
        string? permissionKey = null)
        => TryAddCommand(name, handler, helpText, minAccessLevel, prefix, aliases, permissionKey, out _);

    public bool RegisterForContentGroup(
        string contentGroupKey,
        string name,
        Action<UltraSimScriptCommandContext> handler,
        string helpText = "",
        int minAccessLevel = 0,
        string prefix = "/",
        IEnumerable<string>? aliases = null,
        string? permissionKey = null)
    {
        ScriptDefinitionValidation.ValidateStableKey(contentGroupKey, nameof(contentGroupKey));
        if (!TryAddCommand(name, handler, helpText, minAccessLevel, prefix, aliases, permissionKey, out string commandKey))
            return false;

        UltraSimRuntimeContentGroupOperationResult cleanup = _contentGroups.RegisterUnmountAction(
            contentGroupKey,
            () => _commands.Remove(commandKey));
        if (cleanup.Accepted)
            return true;

        _commands.Remove(commandKey);
        return false;
    }

    public bool Unregister(string name, string prefix = "/")
    {
        if (string.IsNullOrWhiteSpace(name))
            return false;

        string key = MakeCommandKey(NormalizeCommandPrefix(prefix), name.Trim());
        return _commands.Remove(key);
    }

    public bool RegisterPanelProvider(string key, Func<UiBuiltPanel> provider)
        => TryAddPanelProvider(key, provider, out _);

    public bool RegisterPanelProviderForContentGroup(
        string contentGroupKey,
        string key,
        Func<UiBuiltPanel> provider)
    {
        ScriptDefinitionValidation.ValidateStableKey(contentGroupKey, nameof(contentGroupKey));
        if (!TryAddPanelProvider(key, provider, out string panelKey))
            return false;

        UltraSimRuntimeContentGroupOperationResult cleanup = _contentGroups.RegisterUnmountAction(
            contentGroupKey,
            () => _panelProviders.Remove(panelKey));
        if (cleanup.Accepted)
            return true;

        _panelProviders.Remove(panelKey);
        return false;
    }

    public bool UnregisterPanelProvider(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return _panelProviders.Remove(key.Trim());
    }

    public bool TryGetPanelProvider(string key, out UltraSimScriptUiPanelProviderDescriptor provider)
    {
        provider = default!;
        if (string.IsNullOrWhiteSpace(key))
            return false;

        return _panelProviders.TryGetValue(key.Trim(), out provider!);
    }

    public bool RegisterStep(UltraSimScriptAdmissionFlowStepDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        UltraSimScriptAdmissionFlowStepDescriptor normalized = descriptor.NormalizeAndValidate();
        UltraSimReplaceableProviderSetResult<UltraSimScriptAdmissionFlowStepDescriptor> result =
            _admissionSteps.Replace(
                normalized.StartsOnEvent,
                normalized.OwnerKey,
                normalized,
                origin: "script",
                normalized.Priority);
        if (!result.Accepted)
        {
            throw new InvalidOperationException(result.Error);
        }

        _admissionStepEventKeys.Add(normalized.StartsOnEvent);
        return true;
    }

    private bool TryAddPanelProvider(string key, Func<UiBuiltPanel> provider, out string trimmed)
    {
        if (string.IsNullOrWhiteSpace(key))
            throw new ArgumentException("Panel provider key cannot be empty.", nameof(key));
        ArgumentNullException.ThrowIfNull(provider);

        trimmed = key.Trim();
        if (_panelProviders.ContainsKey(trimmed))
            return false;

        _panelProviders.Add(trimmed, new UltraSimScriptUiPanelProviderDescriptor(trimmed, provider));
        return true;
    }

    public bool TryGetAdmissionStep(string startsOnEvent, out UltraSimScriptAdmissionFlowStepDescriptor descriptor)
    {
        descriptor = default!;
        if (string.IsNullOrWhiteSpace(startsOnEvent))
            return false;

        string normalized = ScriptDefinitionValidation.NormalizeKey(startsOnEvent);
        if (!_admissionSteps.TryGetActive(normalized, out UltraSimReplaceableProviderRegistration<UltraSimScriptAdmissionFlowStepDescriptor> registration) ||
            registration.Provider is null)
        {
            return false;
        }

        descriptor = registration.Provider;
        return true;
    }

    public bool RegisterArchetype<TShape>()
        where TShape : ArchetypeShape, new()
    {
        return RegisterArchetype(new TShape().BuildDescriptor());
    }

    public bool RegisterArchetype(ArchetypeDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ScriptDefinitionValidation.ValidateStableKey(descriptor.Key, nameof(descriptor.Key));
        string key = ScriptDefinitionValidation.NormalizeKey(descriptor.Key);
        if (_archetypes.ContainsKey(key))
            return false;

        _archetypes.Add(key, descriptor with { Key = key });
        return true;
    }

    public bool RegisterPrefab<TPrefab>()
        where TPrefab : IUltraSimScriptPrefabDefinition, new()
    {
        return RegisterPrefab(new TPrefab().BuildDescriptor());
    }

    public bool RegisterPrefab(PrefabDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);
        ScriptDefinitionValidation.ValidateStableKey(descriptor.Key, nameof(descriptor.Key));
        ScriptDefinitionValidation.ValidateStableKey(descriptor.ArchetypeKey, nameof(descriptor.ArchetypeKey));
        if (string.IsNullOrWhiteSpace(descriptor.SourceName))
            throw new ArgumentException("Prefab source name cannot be empty.", nameof(descriptor));
        string key = ScriptDefinitionValidation.NormalizeKey(descriptor.Key);
        string archetypeKey = ScriptDefinitionValidation.NormalizeKey(descriptor.ArchetypeKey);
        if (!_archetypes.ContainsKey(archetypeKey))
        {
            throw new InvalidOperationException(
                $"Prefab '{key}' references unregistered archetype '{archetypeKey}'. Register the archetype before registering the prefab.");
        }

        if (_prefabs.TryGetValue(key, out PrefabDescriptor? existing))
        {
            throw new InvalidOperationException(
                $"Duplicate prefab key '{key}':{Environment.NewLine}- {existing.SourceName}{Environment.NewLine}- {descriptor.SourceName}{Environment.NewLine}Rename one prefab or override Key.");
        }

        _prefabs.Add(key, descriptor with { Key = key, SourceName = descriptor.SourceName.Trim(), ArchetypeKey = archetypeKey });
        return true;
    }

    public void RegisterCommandsInto(ServerCommandRegistry registry, string? commandPrefixOverride = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        string? normalizedOverride = string.IsNullOrWhiteSpace(commandPrefixOverride)
            ? null
            : commandPrefixOverride.Trim();

        foreach (UltraSimScriptCommandDescriptor descriptor in _commands.Values)
        {
            string prefix = normalizedOverride ?? descriptor.Prefix;
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
                prefix,
                descriptor.MinAccessLevel,
                descriptor.HelpText,
                descriptor.Aliases,
                descriptor.PermissionKey);
        }
    }

    private void Log(UltraSimScriptLogLevel level, string message)
    {
        _logEntries.Add(new UltraSimScriptLogEntry(level, message ?? string.Empty, DateTimeOffset.UtcNow));
    }

    private bool TryAddCommand(
        string name,
        Action<UltraSimScriptCommandContext> handler,
        string helpText,
        int minAccessLevel,
        string prefix,
        IEnumerable<string>? aliases,
        string? permissionKey,
        out string key)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Command name cannot be empty.", nameof(name));
        ArgumentNullException.ThrowIfNull(handler);
        if (minAccessLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(minAccessLevel));

        string trimmedPrefix = NormalizeCommandPrefix(prefix);
        string trimmedName = name.Trim();
        key = MakeCommandKey(trimmedPrefix, trimmedName);
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
            aliasArray,
            string.IsNullOrWhiteSpace(permissionKey)
                ? ServerCommandPermissionKeys.ForCommandName(trimmedName)
                : ServerAccessKey.NormalizePermissionKey(permissionKey));
        _commands.Add(key, descriptor);
        return true;
    }

    private static string NormalizeCommandPrefix(string prefix)
        => string.IsNullOrWhiteSpace(prefix) ? "/" : prefix.Trim();

    private static string MakeCommandKey(string prefix, string name) => $"{prefix}\u001F{name}";
}
