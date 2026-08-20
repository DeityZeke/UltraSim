namespace UltraSim.Inventory;

public sealed record InventoryPlacementDescriptor(
    InventoryPlacementMode Mode,
    int Index = -1,
    int X = 0,
    int Y = 0,
    int Width = 0,
    int Height = 0,
    bool Rotated = false,
    int ZLayer = 0,
    string? SlotId = null)
{
    public static InventoryPlacementDescriptor None { get; } = new(InventoryPlacementMode.None);

    public static InventoryPlacementDescriptor List(int index)
    {
        return new InventoryPlacementDescriptor(InventoryPlacementMode.List, Index: index);
    }

    public static InventoryPlacementDescriptor Grid(int x, int y, int width, int height, bool rotated = false)
    {
        return new InventoryPlacementDescriptor(InventoryPlacementMode.Grid, X: x, Y: y, Width: width, Height: height, Rotated: rotated);
    }

    public static InventoryPlacementDescriptor Freeform(int x, int y, int zLayer = 0)
    {
        return new InventoryPlacementDescriptor(InventoryPlacementMode.Freeform, X: x, Y: y, ZLayer: zLayer);
    }

    public static InventoryPlacementDescriptor Slot(string slotId)
    {
        return new InventoryPlacementDescriptor(InventoryPlacementMode.Slot, SlotId: slotId);
    }

    public InventoryValidationResult Validate()
    {
        if (!InventoryValidation.IsDefined(Mode))
        {
            return InventoryValidationResult.Invalid("Inventory placement mode is invalid.");
        }

        return Mode switch
        {
            InventoryPlacementMode.None => ValidateNone(),
            InventoryPlacementMode.List => Index >= 0
                ? InventoryValidationResult.Valid
                : InventoryValidationResult.Invalid("List placement requires a non-negative index."),
            InventoryPlacementMode.Grid => ValidateGrid(),
            InventoryPlacementMode.Freeform => InventoryValidationResult.Valid,
            InventoryPlacementMode.Slot => InventoryValidation.ValidateStableId(SlotId, nameof(SlotId), 96),
            _ => InventoryValidationResult.Invalid("Inventory placement mode is invalid."),
        };
    }

    public InventoryValidationResult ValidateForContainerMode(InventoryPlacementMode containerMode)
    {
        if (!InventoryValidation.IsDefined(containerMode))
        {
            return InventoryValidationResult.Invalid("Container placement mode is invalid.");
        }

        InventoryValidationResult placementResult = Validate();
        if (!placementResult.IsValid)
        {
            return placementResult;
        }

        return Mode == containerMode
            ? InventoryValidationResult.Valid
            : InventoryValidationResult.Invalid($"Placement mode {Mode} is not compatible with container mode {containerMode}.");
    }

    private InventoryValidationResult ValidateNone()
    {
        if (Index != -1 ||
            X != 0 ||
            Y != 0 ||
            Width != 0 ||
            Height != 0 ||
            Rotated ||
            ZLayer != 0 ||
            !string.IsNullOrWhiteSpace(SlotId))
        {
            return InventoryValidationResult.Invalid("None placement cannot carry placement coordinates or slot id.");
        }

        return InventoryValidationResult.Valid;
    }

    private InventoryValidationResult ValidateGrid()
    {
        if (X < 0 || Y < 0)
        {
            return InventoryValidationResult.Invalid("Grid placement x/y must be non-negative.");
        }

        if (Width <= 0 || Height <= 0)
        {
            return InventoryValidationResult.Invalid("Grid placement width/height must be positive.");
        }

        return InventoryValidationResult.Valid;
    }
}
