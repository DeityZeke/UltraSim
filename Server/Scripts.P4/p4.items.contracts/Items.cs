using UltraSim.Framework.Definitions;

namespace P4.Items.Contracts;

public readonly record struct ItemFacet(string Name, int MaximumQuantity);
public static class ItemFacets
{
    public static readonly DefinitionFacetFamily<ItemFacet> Item =
        new(new DefinitionFacetId("p4.item"), new DefinitionPackageId("p4.items.contracts"));
}
