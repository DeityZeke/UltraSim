namespace UltraSim.Server.Scripting;

public sealed class UltraSimReplaceableProviderRegistry<TProvider>
    where TProvider : class
{
    private readonly Dictionary<string, List<UltraSimReplaceableProviderRegistration<TProvider>>> _registrationsByKey = new(StringComparer.Ordinal);

    public int KeyCount => _registrationsByKey.Count;

    public UltraSimReplaceableProviderSetResult<TProvider> SetDefault(
        string key,
        string owner,
        TProvider provider,
        string origin = "core")
    {
        return SetProvider(key, owner, provider, origin, priority: 0);
    }

    public UltraSimReplaceableProviderSetResult<TProvider> Replace(
        string key,
        string owner,
        TProvider provider,
        string origin = "script",
        int priority = 100)
    {
        return SetProvider(key, owner, provider, origin, priority);
    }

    public UltraSimReplaceableProviderSetResult<TProvider> Disable(
        string key,
        string owner,
        string origin = "operator",
        int priority = 1000)
    {
        string normalizedKey = NormalizeKey(key, nameof(key));
        string normalizedOwner = NormalizeKey(owner, nameof(owner));
        string normalizedOrigin = NormalizeOrigin(origin);
        var registration = new UltraSimReplaceableProviderRegistration<TProvider>(
            normalizedKey,
            normalizedOwner,
            normalizedOrigin,
            priority,
            Provider: null,
            DisablesProvider: true);
        return SetRegistration(registration);
    }

    public bool RemoveOwner(string key, string owner)
    {
        string normalizedKey = NormalizeKey(key, nameof(key));
        string normalizedOwner = NormalizeKey(owner, nameof(owner));
        if (!_registrationsByKey.TryGetValue(normalizedKey, out List<UltraSimReplaceableProviderRegistration<TProvider>>? registrations))
        {
            return false;
        }

        int removed = registrations.RemoveAll(registration => string.Equals(registration.Owner, normalizedOwner, StringComparison.Ordinal));
        if (registrations.Count == 0)
        {
            _registrationsByKey.Remove(normalizedKey);
        }

        return removed > 0;
    }

    public bool TryGetActive(string key, out UltraSimReplaceableProviderRegistration<TProvider> registration)
    {
        registration = default!;
        if (!TryGetState(key, out UltraSimReplaceableProviderState<TProvider>? state) ||
            state is null ||
            state.ActiveRegistration is null ||
            state.ActiveRegistration.DisablesProvider)
        {
            return false;
        }

        registration = state.ActiveRegistration;
        return true;
    }

    public bool TryGetState(string key, out UltraSimReplaceableProviderState<TProvider>? state)
    {
        string normalizedKey = NormalizeKey(key, nameof(key));
        if (!_registrationsByKey.TryGetValue(normalizedKey, out List<UltraSimReplaceableProviderRegistration<TProvider>>? registrations) ||
            registrations.Count == 0)
        {
            state = null;
            return false;
        }

        UltraSimReplaceableProviderRegistration<TProvider> active = registrations
            .OrderByDescending(static registration => registration.Priority)
            .ThenBy(static registration => registration.Owner, StringComparer.Ordinal)
            .First();
        UltraSimReplaceableProviderRegistration<TProvider>[] suppressed = registrations
            .Where(registration => !ReferenceEquals(registration, active))
            .OrderByDescending(static registration => registration.Priority)
            .ThenBy(static registration => registration.Owner, StringComparer.Ordinal)
            .ToArray();
        state = new UltraSimReplaceableProviderState<TProvider>(normalizedKey, active, suppressed);
        return true;
    }

    private UltraSimReplaceableProviderSetResult<TProvider> SetProvider(
        string key,
        string owner,
        TProvider provider,
        string origin,
        int priority)
    {
        ArgumentNullException.ThrowIfNull(provider);
        string normalizedKey = NormalizeKey(key, nameof(key));
        string normalizedOwner = NormalizeKey(owner, nameof(owner));
        string normalizedOrigin = NormalizeOrigin(origin);
        var registration = new UltraSimReplaceableProviderRegistration<TProvider>(
            normalizedKey,
            normalizedOwner,
            normalizedOrigin,
            priority,
            provider,
            DisablesProvider: false);
        return SetRegistration(registration);
    }

    private UltraSimReplaceableProviderSetResult<TProvider> SetRegistration(
        UltraSimReplaceableProviderRegistration<TProvider> registration)
    {
        if (!_registrationsByKey.TryGetValue(registration.Key, out List<UltraSimReplaceableProviderRegistration<TProvider>>? registrations))
        {
            registrations = new List<UltraSimReplaceableProviderRegistration<TProvider>>();
            _registrationsByKey.Add(registration.Key, registrations);
        }

        UltraSimReplaceableProviderRegistration<TProvider>? replaced = null;
        for (int i = 0; i < registrations.Count; i++)
        {
            UltraSimReplaceableProviderRegistration<TProvider> existing = registrations[i];
            if (existing.Priority == registration.Priority &&
                !string.Equals(existing.Owner, registration.Owner, StringComparison.Ordinal))
            {
                return UltraSimReplaceableProviderSetResult<TProvider>.Conflict(
                    $"Replaceable provider '{registration.Key}' already has owner '{existing.Owner}' at priority {registration.Priority}; '{registration.Owner}' must use a different priority or replace the same owner.");
            }

            if (string.Equals(existing.Owner, registration.Owner, StringComparison.Ordinal))
            {
                replaced = existing;
                registrations[i] = registration;
                return UltraSimReplaceableProviderSetResult<TProvider>.AcceptedResult(ResolveActive(registrations), replaced);
            }
        }

        registrations.Add(registration);
        return UltraSimReplaceableProviderSetResult<TProvider>.AcceptedResult(ResolveActive(registrations), replaced);
    }

    private static UltraSimReplaceableProviderRegistration<TProvider>? ResolveActive(
        IReadOnlyList<UltraSimReplaceableProviderRegistration<TProvider>> registrations)
    {
        return registrations
            .OrderByDescending(static registration => registration.Priority)
            .ThenBy(static registration => registration.Owner, StringComparer.Ordinal)
            .FirstOrDefault();
    }

    private static string NormalizeKey(string value, string parameterName)
    {
        ScriptDefinitionValidation.ValidateStableKey(value, parameterName);
        return ScriptDefinitionValidation.NormalizeKey(value);
    }

    private static string NormalizeOrigin(string origin)
    {
        return string.IsNullOrWhiteSpace(origin) ? "unknown" : origin.Trim();
    }
}
