namespace UltraSim.Server.Scripting;

public sealed record UltraSimRuntimeContentGroupOperationResult(
    UltraSimRuntimeContentGroupOperationStatus Status,
    UltraSimRuntimeContentGroupSnapshot? Snapshot,
    string Error = "")
{
    public bool Accepted => Status == UltraSimRuntimeContentGroupOperationStatus.Accepted;

    public static UltraSimRuntimeContentGroupOperationResult AcceptedResult(
        UltraSimRuntimeContentGroupSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return new UltraSimRuntimeContentGroupOperationResult(
            UltraSimRuntimeContentGroupOperationStatus.Accepted,
            snapshot);
    }

    public static UltraSimRuntimeContentGroupOperationResult Rejected(
        UltraSimRuntimeContentGroupOperationStatus status,
        string error,
        UltraSimRuntimeContentGroupSnapshot? snapshot = null)
    {
        return new UltraSimRuntimeContentGroupOperationResult(
            status,
            snapshot,
            error ?? string.Empty);
    }
}
