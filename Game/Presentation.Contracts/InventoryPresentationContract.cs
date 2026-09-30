using UltraSim.Framework.Definitions;

namespace UltraSim.Game.Presentation.Contracts;

/// <summary>
/// Game-owned lookup from an existing item prefab provenance to a disclosed authored
/// presentation. This is a projection capability, not Inventory storage or authorization.
/// </summary>
public interface IInventoryPresentationLookup
{
    bool TryResolveInventoryPresentation(string prefabKey,
        out PublicDefinitionPresentationDescriptor descriptor);
}

/// <summary>
/// Exact field positions in the existing Inventory UI list-row transport. The first four
/// human-readable fields remain compatible with the older utilitarian viewer; every later
/// field is independent typed data, never parsed from the placement summary.
/// </summary>
public static class InventoryPresentationRowFields
{
    public const int DisplayName = 0;
    public const int Quantity = 1;
    public const int Equipment = 2;
    public const int PlacementSummary = 3;
    public const int PlacementMode = 4;
    public const int GridX = 5;
    public const int GridY = 6;
    public const int GridWidth = 7;
    public const int GridHeight = 8;
    public const int EquipmentKind = 9;
    public const int ItemFieldCount = 10;
    public const int CellLabel = 0;
    public const int CellX = 1;
    public const int CellY = 2;
}
