namespace UltraSim.Game.Inventory;

/// <summary>Server-owned selection policy for content-authored container rules.</summary>
public enum InventoryRuleScope : byte
{
    PerContainerProfile = 0,
    Uniform = 1,
}

/// <summary>Stable identity of one compiled inventory rule profile.</summary>
public readonly record struct InventoryRuleProfileId(ushort Value)
{
    public bool IsValid => Value != 0;
}

/// <summary>
/// Viewer-independent placement truth. Unplaced containers can be projected as
/// lists without retaining a list index in authoritative item state.
/// </summary>
public enum InventoryPlacementMode : byte
{
    Unplaced = 0,
    Grid = 1,
    Slot = 2,
    Freeform = 3,
}

/// <summary>Compact content-defined slot identity for slot-backed placement.</summary>
public readonly record struct InventoryPlacementSlotId(ushort Value)
{
    public bool IsValid => Value != 0;
}

/// <summary>Optional placement facts interpreted only by the matching container profile.</summary>
public readonly record struct InventoryPlacement
{
    private InventoryPlacement(
        InventoryPlacementMode mode,
        int x,
        int y,
        int z,
        ushort width,
        ushort height,
        InventoryPlacementSlotId slot,
        bool rotated)
    {
        Mode = mode;
        X = x;
        Y = y;
        Z = z;
        Width = width;
        Height = height;
        Slot = slot;
        Rotated = rotated;
    }

    public InventoryPlacementMode Mode { get; }
    public int X { get; }
    public int Y { get; }
    public int Z { get; }
    public ushort Width { get; }
    public ushort Height { get; }
    public InventoryPlacementSlotId Slot { get; }
    public bool Rotated { get; }

    public static InventoryPlacement Unplaced => default;

    public static InventoryPlacement Grid(
        int x,
        int y,
        ushort width,
        ushort height,
        bool rotated = false) =>
        new(InventoryPlacementMode.Grid, x, y, 0, width, height, default, rotated);

    public static InventoryPlacement SlotPlacement(InventoryPlacementSlotId slot) =>
        new(InventoryPlacementMode.Slot, 0, 0, 0, 0, 0, slot, false);

    public static InventoryPlacement Freeform(int x, int y, int z = 0) =>
        new(InventoryPlacementMode.Freeform, x, y, z, 0, 0, default, false);

    public bool IsValid => Mode switch
    {
        InventoryPlacementMode.Unplaced =>
            X == 0 && Y == 0 && Z == 0 && Width == 0 && Height == 0 &&
            !Slot.IsValid && !Rotated,
        InventoryPlacementMode.Grid =>
            X >= 0 && Y >= 0 && Z == 0 && Width > 0 && Height > 0 &&
            !Slot.IsValid,
        InventoryPlacementMode.Slot =>
            X == 0 && Y == 0 && Z == 0 && Width == 0 && Height == 0 &&
            Slot.IsValid && !Rotated,
        InventoryPlacementMode.Freeform =>
            Width == 0 && Height == 0 && !Slot.IsValid && !Rotated,
        _ => false,
    };

    public bool IsCompatibleWith(InventoryPlacementMode mode) => IsValid && Mode == mode;
}

/// <summary>
/// Independent capacity dimensions for one container profile. A zero weight
/// limit means weight is not constrained. A zero nesting depth forbids nesting.
/// Grid dimensions are present only for a Grid placement profile.
/// </summary>
public readonly record struct InventoryCapacityDimensions(
    int MaximumItemCount,
    ushort MaximumGridWidth = 0,
    ushort MaximumGridHeight = 0,
    long MaximumWeightGrams = 0,
    byte MaximumNestingDepth = 0)
{
    public const ushort MaximumGridEdge = 64;
    public const int MaximumGridCells = 4_096;

    public bool IsValidFor(InventoryPlacementMode mode)
    {
        if (MaximumItemCount <= 0 || MaximumWeightGrams < 0)
            return false;
        bool hasGrid = MaximumGridWidth != 0 || MaximumGridHeight != 0;
        return mode == InventoryPlacementMode.Grid
            ? MaximumGridWidth != 0 && MaximumGridHeight != 0 &&
                MaximumGridWidth <= MaximumGridEdge && MaximumGridHeight <= MaximumGridEdge &&
                checked((int)MaximumGridWidth * MaximumGridHeight) <= MaximumGridCells
            : !hasGrid;
    }
}

/// <summary>
/// Persistent effective capacity of one concrete container instance. Grid
/// dimensions are zero for non-Grid placement modes and remain independent
/// from the item-count limit.
/// </summary>
public readonly record struct InventoryInstanceCapacity(
    int ItemCapacity,
    ushort GridWidth = 0,
    ushort GridHeight = 0);

/// <summary>One immutable, compiled container-policy profile.</summary>
public readonly record struct InventoryRuleProfile(
    InventoryRuleProfileId Id,
    InventoryPlacementMode PlacementMode,
    InventoryCapacityDimensions Capacity,
    ushort InitialGridWidth = 0,
    ushort InitialGridHeight = 0)
{
    public bool IsValid =>
        Id.IsValid &&
        PlacementMode is >= InventoryPlacementMode.Unplaced and <= InventoryPlacementMode.Freeform &&
        Capacity.IsValidFor(PlacementMode) &&
        (PlacementMode == InventoryPlacementMode.Grid
            ? InitialGridWidth != 0 && InitialGridHeight != 0 &&
                InitialGridWidth <= Capacity.MaximumGridWidth &&
                InitialGridHeight <= Capacity.MaximumGridHeight
            : InitialGridWidth == 0 && InitialGridHeight == 0);

    /// <summary>
    /// I0.10 operational floor. Grid placement is authoritative; weight,
    /// nesting, Slot, and Freeform remain declared but fail closed.
    /// </summary>
    public bool IsOperational =>
        PlacementMode is InventoryPlacementMode.Unplaced or InventoryPlacementMode.Grid &&
        Capacity.MaximumWeightGrams == 0 &&
        Capacity.MaximumNestingDepth == 0;
}

/// <summary>Server selection of content profiles or one forced uniform profile.</summary>
public readonly record struct InventoryRuleSelection(
    InventoryRuleScope Scope,
    InventoryRuleProfileId UniformProfile)
{
    public static InventoryRuleSelection PerContainer =>
        new(InventoryRuleScope.PerContainerProfile, default);

    public static InventoryRuleSelection Uniform(InventoryRuleProfileId profile) =>
        new(InventoryRuleScope.Uniform, profile);

    public bool IsValid => Scope switch
    {
        InventoryRuleScope.PerContainerProfile => !UniformProfile.IsValid,
        InventoryRuleScope.Uniform => UniformProfile.IsValid,
        _ => false,
    };
}

/// <summary>Resolved effective rules for one concrete container instance.</summary>
public readonly record struct InventoryEffectiveRules(
    InventoryRuleProfileId Profile,
    InventoryPlacementMode PlacementMode,
    int ItemCapacity,
    ushort GridWidth,
    ushort GridHeight,
    long MaximumWeightGrams,
    byte MaximumNestingDepth)
{
    public bool IsOperational =>
        Profile.IsValid && ItemCapacity > 0 &&
        PlacementMode is InventoryPlacementMode.Unplaced or InventoryPlacementMode.Grid &&
        (PlacementMode == InventoryPlacementMode.Grid
            ? GridWidth != 0 && GridHeight != 0
            : GridWidth == 0 && GridHeight == 0) &&
        MaximumWeightGrams == 0 && MaximumNestingDepth == 0;
}

/// <summary>
/// Immutable bounded catalog. It resolves server scope, content profile, and
/// persistent instance capacity without turning the profiles into a
/// universal mutation interface.
/// </summary>
public sealed class InventoryRuleCatalog
{
    private readonly InventoryRuleProfile[] _profiles;

    public InventoryRuleCatalog(
        InventoryRuleSelection selection,
        IReadOnlyList<InventoryRuleProfile> profiles)
    {
        if (!selection.IsValid)
            throw new ArgumentException("The inventory rule selection is invalid.", nameof(selection));
        ArgumentNullException.ThrowIfNull(profiles);
        if (profiles.Count == 0 || profiles.Count > ushort.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(profiles));

        _profiles = new InventoryRuleProfile[profiles.Count];
        for (int index = 0; index < profiles.Count; index++)
        {
            InventoryRuleProfile profile = profiles[index];
            if (!profile.IsValid)
                throw new ArgumentException("An inventory rule profile is invalid.", nameof(profiles));
            for (int prior = 0; prior < index; prior++)
            {
                if (_profiles[prior].Id == profile.Id)
                    throw new ArgumentException("Inventory rule profile identities must be unique.", nameof(profiles));
            }
            _profiles[index] = profile;
        }
        Selection = selection;
        if (selection.Scope == InventoryRuleScope.Uniform &&
            !TryGetProfile(selection.UniformProfile, out _))
        {
            throw new ArgumentException("The uniform inventory rule profile is not in the catalog.", nameof(selection));
        }
    }

    public InventoryRuleSelection Selection { get; }
    public int Count => _profiles.Length;

    public bool TryGetProfile(InventoryRuleProfileId id, out InventoryRuleProfile profile)
    {
        for (int index = 0; index < _profiles.Length; index++)
        {
            if (_profiles[index].Id == id)
            {
                profile = _profiles[index];
                return true;
            }
        }
        profile = default;
        return false;
    }

    public bool TryResolve(
        InventoryRuleProfileId contentProfile,
        int instanceItemCapacity,
        out InventoryEffectiveRules rules)
    {
        if (!TryGetSelectedProfile(contentProfile, out InventoryRuleProfile profile))
        {
            rules = default;
            return false;
        }
        return TryResolveProfile(
            profile,
            new InventoryInstanceCapacity(
                instanceItemCapacity,
                profile.InitialGridWidth,
                profile.InitialGridHeight),
            out rules);
    }

    public bool TryResolve(
        InventoryRuleProfileId contentProfile,
        in InventoryInstanceCapacity instanceCapacity,
        out InventoryEffectiveRules rules)
    {
        if (!TryGetSelectedProfile(contentProfile, out InventoryRuleProfile profile))
        {
            rules = default;
            return false;
        }
        return TryResolveProfile(profile, instanceCapacity, out rules);
    }

    private bool TryGetSelectedProfile(
        InventoryRuleProfileId contentProfile,
        out InventoryRuleProfile profile)
    {
        InventoryRuleProfileId selected = Selection.Scope == InventoryRuleScope.Uniform
            ? Selection.UniformProfile
            : contentProfile;
        return TryGetProfile(selected, out profile);
    }

    private static bool TryResolveProfile(
        in InventoryRuleProfile profile,
        in InventoryInstanceCapacity instanceCapacity,
        out InventoryEffectiveRules rules)
    {
        if (instanceCapacity.ItemCapacity <= 0 ||
            instanceCapacity.ItemCapacity > profile.Capacity.MaximumItemCount ||
            (profile.PlacementMode == InventoryPlacementMode.Grid
                ? instanceCapacity.GridWidth == 0 || instanceCapacity.GridHeight == 0 ||
                    instanceCapacity.GridWidth > profile.Capacity.MaximumGridWidth ||
                    instanceCapacity.GridHeight > profile.Capacity.MaximumGridHeight ||
                    checked((int)instanceCapacity.GridWidth * instanceCapacity.GridHeight) >
                        InventoryCapacityDimensions.MaximumGridCells
                : instanceCapacity.GridWidth != 0 || instanceCapacity.GridHeight != 0))
        {
            rules = default;
            return false;
        }
        rules = new InventoryEffectiveRules(
            profile.Id,
            profile.PlacementMode,
            instanceCapacity.ItemCapacity,
            instanceCapacity.GridWidth,
            instanceCapacity.GridHeight,
            profile.Capacity.MaximumWeightGrams,
            profile.Capacity.MaximumNestingDepth);
        return true;
    }
}
