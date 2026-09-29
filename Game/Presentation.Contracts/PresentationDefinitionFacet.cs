using UltraSim.Framework.Definitions;

namespace UltraSim.Game.Presentation.Contracts;

/// <summary>Only disclosed semantics; art bytes and private item statistics are absent.</summary>
public readonly record struct PublicFallbackBounds(int WidthMillimeters, int HeightMillimeters, int DepthMillimeters)
{
    public bool IsValid => WidthMillimeters is > 0 and <= 64_000 &&
        HeightMillimeters is > 0 and <= 64_000 && DepthMillimeters is > 0 and <= 64_000;
}
/// <summary>Bounded public projection; private Combat/Durability/Skills facts are absent.</summary>
public readonly record struct PublicDefinitionPresentationDescriptor(
    SemanticPresentationReference World, PublicFallbackBounds FallbackBounds, bool InteractionVisible)
{
    public bool IsValid => World.IsValid && World.Role == PresentationRoleId.World && FallbackBounds.IsValid;
}
public readonly record struct PresentationFacet(
    SemanticPresentationReference World, PublicFallbackBounds FallbackBounds)
{
    public bool IsValid => World.IsValid && World.Role == PresentationRoleId.World && FallbackBounds.IsValid;
    public PublicDefinitionPresentationDescriptor PublicDescriptor(bool interactionVisible) =>
        IsValid ? new(World, FallbackBounds, interactionVisible) :
        throw new InvalidOperationException("Invalid authored presentation cannot be disclosed.");
}
public static class PresentationDefinitionFacets
{
    public static readonly DefinitionPackageId Owner = new("game.presentation.contracts");
    public static readonly DefinitionFacetFamily<PresentationFacet> Presentation = new(new("presentation.world"), Owner,
        validate: (id, value) => value.IsValid && value.World.Definition == id);
    public static void Register(DefinitionComposer composer) => composer.RegisterFacet(Presentation);
    public static DefinitionBuilder WorldPresentation(this DefinitionBuilder builder, DefinitionPackageId contributor,
        PresentationFacet value)
    {
        if (!value.IsValid || value.World.Definition != builder.Id)
            throw new ArgumentException("World presentation must name the authored Definition and valid public fallback bounds.");
        return builder.With(contributor, Presentation, value);
    }
}
