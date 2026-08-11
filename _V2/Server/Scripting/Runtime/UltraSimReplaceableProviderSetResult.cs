namespace UltraSim.Server.Scripting;

public sealed record UltraSimReplaceableProviderSetResult<TProvider>(
    UltraSimReplaceableProviderSetStatus Status,
    UltraSimReplaceableProviderRegistration<TProvider>? ActiveRegistration,
    UltraSimReplaceableProviderRegistration<TProvider>? ReplacedRegistration,
    string Error)
    where TProvider : class
{
    public bool Accepted => Status == UltraSimReplaceableProviderSetStatus.Accepted;

    public static UltraSimReplaceableProviderSetResult<TProvider> AcceptedResult(
        UltraSimReplaceableProviderRegistration<TProvider>? active,
        UltraSimReplaceableProviderRegistration<TProvider>? replaced)
    {
        return new UltraSimReplaceableProviderSetResult<TProvider>(
            UltraSimReplaceableProviderSetStatus.Accepted,
            active,
            replaced,
            string.Empty);
    }

    public static UltraSimReplaceableProviderSetResult<TProvider> Conflict(string error)
    {
        return new UltraSimReplaceableProviderSetResult<TProvider>(
            UltraSimReplaceableProviderSetStatus.RejectedConflict,
            ActiveRegistration: null,
            ReplacedRegistration: null,
            error);
    }
}
