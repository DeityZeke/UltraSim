using UltraSim.Framework.Definitions;

namespace UltraSim.Game.Items.Contracts;

/// <summary>Semantic item meaning independent of ECS instantiation.</summary>
public readonly record struct ItemFacet(string DisplayName, int MaximumQuantity);
public readonly record struct InventoryFacet(ushort GridWidth, ushort GridHeight);
public readonly record struct EquipmentFacet(bool MainHand);
public readonly record struct DurabilityFacet(int Maximum);

/// <summary>Public typed Contracts vocabulary. No Inventory Core dependency.</summary>
public static class ItemDefinitionFacets
{
    public static readonly DefinitionPackageId Owner = new("game.items.contracts");
    public static readonly DefinitionFacetFamily<ItemFacet> Item = new(new("items.item"), Owner,
        validate: (_, value) => !string.IsNullOrWhiteSpace(value.DisplayName) && value.DisplayName.Length <= 128 &&
            value.MaximumQuantity > 0);
    public static readonly DefinitionFacetFamily<InventoryFacet> Inventory = new(new("items.inventory"), Owner,
        validate: (_, value) => value.GridWidth is >= 1 and <= 64 && value.GridHeight is >= 1 and <= 64);
    public static readonly DefinitionFacetFamily<EquipmentFacet> Equipment = new(new("items.equipment"), Owner);
    public static readonly DefinitionFacetFamily<DurabilityFacet> Durability = new(new("items.durability"), Owner,
        validate: (_, value) => value.Maximum is >= 1 and <= ushort.MaxValue);
    public static void Register(DefinitionComposer composer)
    { composer.RegisterFacet(Item); composer.RegisterFacet(Inventory); composer.RegisterFacet(Equipment); composer.RegisterFacet(Durability); }
    public static DefinitionBuilder ItemMeaning(this DefinitionBuilder builder, DefinitionPackageId contributor, ItemFacet value)
    {
        if (string.IsNullOrWhiteSpace(value.DisplayName) || value.DisplayName.Length > 128 || value.MaximumQuantity <= 0)
            throw new ArgumentException("Item meaning needs a bounded name and positive maximum quantity.");
        return builder.With(contributor, Item, value);
    }
    public static DefinitionBuilder InventoryMeaning(this DefinitionBuilder builder, DefinitionPackageId contributor, InventoryFacet value)
    {
        if (value.GridWidth is < 1 or > 64 || value.GridHeight is < 1 or > 64)
            throw new ArgumentException("Inventory footprint must be bounded and nonempty.");
        return builder.With(contributor, Inventory, value);
    }
    public static DefinitionBuilder EquipmentMeaning(this DefinitionBuilder builder, DefinitionPackageId contributor, EquipmentFacet value) =>
        builder.With(contributor, Equipment, value);
    public static DefinitionBuilder DurabilityMeaning(this DefinitionBuilder builder, DefinitionPackageId contributor, DurabilityFacet value)
    {
        if (value.Maximum is < 1 or > ushort.MaxValue)
            throw new ArgumentException("Durability maximum must fit the current authored item realization.");
        return builder.With(contributor, Durability, value);
    }
}
