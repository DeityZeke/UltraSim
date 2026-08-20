namespace UltraSim.Inventory;

public sealed record InventoryContainerViewSnapshot(
    string ViewId,
    string ContainerId,
    InventoryRequesterRef Viewer,
    InventoryAccessFlags GrantedAccess,
    string? VisibilityScopeId = null)
{
    public InventoryValidationResult Validate()
    {
        InventoryValidationResult idResult = InventoryValidation.ValidateStableId(ViewId, nameof(ViewId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        idResult = InventoryValidation.ValidateStableId(ContainerId, nameof(ContainerId));
        if (!idResult.IsValid)
        {
            return idResult;
        }

        InventoryValidationResult viewerResult = Viewer.Validate();
        if (!viewerResult.IsValid)
        {
            return viewerResult;
        }

        InventoryValidationResult accessResult = InventoryContainerDelta.ValidateAccessFlags(
            GrantedAccess,
            nameof(GrantedAccess),
            allowNone: false);
        if (!accessResult.IsValid)
        {
            return accessResult;
        }

        return string.IsNullOrWhiteSpace(VisibilityScopeId)
            ? InventoryValidationResult.Valid
            : InventoryValidation.ValidateStableId(VisibilityScopeId, nameof(VisibilityScopeId));
    }

    public bool CanReceive(InventoryContainerDelta delta)
    {
        if (!string.Equals(ContainerId, delta.ContainerId, StringComparison.Ordinal))
        {
            return false;
        }

        bool isAdmin = (GrantedAccess & InventoryAccessFlags.Admin) == InventoryAccessFlags.Admin;
        bool hasRequiredAccess = (GrantedAccess & delta.RequiredAccess) == delta.RequiredAccess;
        if (!isAdmin && !hasRequiredAccess)
        {
            return false;
        }

        return isAdmin ||
            string.IsNullOrWhiteSpace(delta.VisibilityScopeId) ||
            string.Equals(VisibilityScopeId, delta.VisibilityScopeId, StringComparison.Ordinal);
    }
}
