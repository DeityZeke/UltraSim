using UltraSim.Framework.Definitions;

namespace UltraSim.Game.Skills.Contracts;

public readonly record struct SkillsFacet(string SkillFamily);
public static class SkillsDefinitionFacets
{
    public static readonly DefinitionPackageId Owner = new("game.skills.contracts");
    public static readonly DefinitionCapabilityId Runtime = new("game.skills.runtime");
    public static readonly DefinitionFacetFamily<SkillsFacet> Skills = new(new("skills.tool"), Owner, Runtime,
        (_, value) => IsSkillFamily(value.SkillFamily));
    private static bool IsSkillFamily(string? value)
    { try { _ = new DefinitionTagId(value!); return true; } catch (ArgumentException) { return false; } }
    public static void Register(DefinitionComposer composer) => composer.RegisterFacet(Skills);
    public static DefinitionBuilder SkillsMeaning(this DefinitionBuilder builder, DefinitionPackageId contributor, SkillsFacet value)
    {
        _ = new DefinitionTagId(value.SkillFamily);
        return builder.With(contributor, Skills, value);
    }
}
