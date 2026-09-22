using UltraSim.Client.Core;
using UltraSim.Game.Composition;
using UltraSim.Game.World;

internal static class ClientPrimitivePresentationTests
{
    internal static void Run()
    {
        Check(!default(ClientPrimitivePresentation).IsValid,
            "The default descriptor must not authorize a primitive.");
        foreach (ClientActorVisualKind kind in Enum.GetValues<ClientActorVisualKind>())
        {
            ClientPrimitivePresentation actor = ClientPrimitivePresentationCatalog.ForActor(kind);
            Check(actor.IsValid && actor.Kind == ClientPrimitiveKind.Actor &&
                actor.Shape is ClientPrimitiveShape.Capsule or ClientPrimitiveShape.Box &&
                actor.InteractionVisible,
                "An actor lost its bounded, interactable fallback.");
        }
        foreach (uint prefab in new[]
        {
            GameWorldPublicPrefabIds.CabinDoor, GameWorldPublicPrefabIds.WorldChest,
            GameWorldPublicPrefabIds.SkyTelepad, GameWorldPublicPrefabIds.FarGate,
            GameWorldPublicPrefabIds.InformationalSign, GameWorldPublicPrefabIds.IronSword,
        })
        {
            ClientPrimitivePresentation first = ClientPrimitivePresentationCatalog.ForPublic(prefab);
            Check(first.IsValid && first.InteractionVisible &&
                first == ClientPrimitivePresentationCatalog.ForPublic(prefab),
                "A known public object lost deterministic bounds or visibility.");
        }
        ClientPrimitivePresentation sword = ClientPrimitivePresentationCatalog.ForPublic(
            GameWorldPublicPrefabIds.IronSword);
        Check(sword.Kind == ClientPrimitiveKind.GroundItem &&
            sword.HeightMillimeters == 1_200,
            "The sword needs an asset-independent ground-item pick/presentation volume.");
        Check(ClientPrimitivePresentationCatalog.ForPublic(uint.MaxValue).Kind ==
            ClientPrimitiveKind.InteractiveMarker,
            "An unfamiliar disclosed public prefab should have a bounded marker.");

        Check(WorldPosition.TryFromGlobalMillimeters(1_000, 1_000, 2_000,
            out WorldPosition position), "The structural fixture position was invalid.");
        var bounds = new WorldStaticBounds(500, 800, 1_700, 2_500, 3_800, 2_300);
        Check(ClientPrimitivePresentation.TryFromStructuralBounds(
                bounds, position, false, out ClientPrimitivePresentation frozen) &&
            frozen.IsValid && frozen.Kind == ClientPrimitiveKind.FrozenStatic &&
            frozen.WidthMillimeters == 2_000 && frozen.HeightMillimeters == 3_000 &&
            frozen.DepthMillimeters == 600,
            "Frozen-static fallback did not preserve its disclosed structural volume.");
        Check(ClientPrimitivePresentation.TryFromStructuralBounds(
                bounds, position, true, out ClientPrimitivePresentation piece) &&
            piece.Kind == ClientPrimitiveKind.MultiFragment &&
            (piece with { Kind = ClientPrimitiveKind.FrozenStatic }) == frozen,
            "A Multi fragment did not retain its independently declared volume.");
        Check(!ClientPrimitivePresentation.TryFromStructuralBounds(
                new WorldStaticBounds(0, 0, 0, 100_000, 100, 100),
                position, false, out _),
            "An oversized fallback must fail bounded descriptor admission.");
        Check(!new ClientPrimitivePresentation(ClientPrimitiveKind.Actor,
                ClientPrimitiveShape.Box, 0, 0, 0, int.MaxValue, 1, 1, true).IsValid,
            "Overflow-sized descriptors must be rejected.");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
