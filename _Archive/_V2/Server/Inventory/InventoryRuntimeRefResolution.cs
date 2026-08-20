namespace UltraSim.Server.Inventory;

public readonly record struct InventoryRuntimeRefResolution(
    InventoryRuntimeRefResolutionKind Kind,
    InventoryResolvedRuntimeRef Resolved,
    string? Error)
{
    public bool IsResolved => Kind == InventoryRuntimeRefResolutionKind.Resolved;

    public static InventoryRuntimeRefResolution Success(InventoryResolvedRuntimeRef resolved)
    {
        return new InventoryRuntimeRefResolution(InventoryRuntimeRefResolutionKind.Resolved, resolved, Error: null);
    }

    public static InventoryRuntimeRefResolution Failure(InventoryRuntimeRefResolutionKind kind, string error)
    {
        if (kind == InventoryRuntimeRefResolutionKind.Resolved)
        {
            throw new ArgumentException("Resolved is not a failure kind.", nameof(kind));
        }

        return new InventoryRuntimeRefResolution(kind, default, error);
    }
}
