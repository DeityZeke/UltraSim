using UltraSim.Framework.Definitions;

namespace P4.Combat.Contracts;

public readonly record struct CombatFacet(int Minimum, int Maximum);
public static class CombatFacets
{
    public static readonly DefinitionCapabilityId Runtime = new("p4.combat.runtime");
    public static readonly DefinitionFacetFamily<CombatFacet> Combat =
        new(new DefinitionFacetId("p4.combat"), new DefinitionPackageId("p4.combat.contracts"), Runtime);
}
