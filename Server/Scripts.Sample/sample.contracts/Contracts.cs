using UltraSim.Framework.Definitions;
using UltraSim.Framework.RuntimeHost;

namespace Sample.Contracts;

public interface IGreeting
{
    string Name { get; }
}

public static class SampleIdentity
{
    public const string Greeting = "sample.greeting";
}

public readonly record struct SwordCombatFacet(int MinimumDamage, int MaximumDamage);

public static class SampleDefinitionFacets
{
    public static readonly DefinitionFacetFamily<SwordCombatFacet> Combat = new(
        new("sample.combat"), new DefinitionPackageId("sample.contracts"),
        new DefinitionCapabilityId("sample.runtime"));
}
