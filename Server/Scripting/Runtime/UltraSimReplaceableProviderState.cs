namespace UltraSim.Server.Scripting;

public sealed record UltraSimReplaceableProviderState<TProvider>(
    string Key,
    UltraSimReplaceableProviderRegistration<TProvider>? ActiveRegistration,
    IReadOnlyList<UltraSimReplaceableProviderRegistration<TProvider>> SuppressedRegistrations)
    where TProvider : class
{
    public bool IsDisabled => ActiveRegistration?.DisablesProvider == true;
}
