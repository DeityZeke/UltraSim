namespace UltraSim.Server.Scripting;

public sealed record UltraSimReplaceableProviderRegistration<TProvider>(
    string Key,
    string Owner,
    string Origin,
    int Priority,
    TProvider? Provider,
    bool DisablesProvider)
    where TProvider : class;
