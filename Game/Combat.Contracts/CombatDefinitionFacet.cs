using UltraSim.Framework.Definitions;

namespace UltraSim.Game.Combat.Contracts;

public readonly record struct CombatFacet(int MinimumDamage, int MaximumDamage);
public static class CombatDefinitionFacets
{
    public static readonly DefinitionPackageId Owner = new("game.combat.contracts");
    public static readonly DefinitionCapabilityId Runtime = new("game.combat.runtime");
    public static readonly DefinitionFacetFamily<CombatFacet> Combat = new(new("combat.attack"), Owner, Runtime,
        (_, value) => value.MinimumDamage is >= 0 and <= ushort.MaxValue &&
            value.MaximumDamage >= value.MinimumDamage && value.MaximumDamage <= ushort.MaxValue);
    public static void Register(DefinitionComposer composer) => composer.RegisterFacet(Combat);
    public static DefinitionBuilder CombatMeaning(this DefinitionBuilder builder, DefinitionPackageId contributor, CombatFacet value)
    {
        if (value.MinimumDamage is < 0 or > ushort.MaxValue || value.MaximumDamage < value.MinimumDamage ||
            value.MaximumDamage > ushort.MaxValue)
            throw new ArgumentException("Combat damage range is invalid for the current authored realization.");
        return builder.With(contributor, Combat, value);
    }
}
