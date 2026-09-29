using UltraSim.Game.Composition;
using UltraSim.Game.World;
using UltraSim.Game.Presentation.Contracts;

namespace UltraSim.Client.Core;

/// <summary>Finite renderer-independent shapes earned by the current fallback consumers.</summary>
public enum ClientPrimitiveShape : byte
{
    Box = 1,
    Capsule = 2,
    Marker = 3,
}

/// <summary>Presentation category, not an authoritative Game object classification.</summary>
public enum ClientPrimitiveKind : byte
{
    Actor = 1,
    GroundItem = 2,
    PublicObject = 3,
    FrozenStatic = 4,
    MultiFragment = 5,
    InteractiveMarker = 6,
}

/// <summary>
/// Bounded, asset-independent local-space fallback bounds. Millimetres are relative to the
/// semantic object's position; a renderer may realize these without opening optional art.
/// This descriptor neither grants disclosure nor changes authoritative interaction bounds.
/// </summary>
public readonly record struct ClientPrimitivePresentation(
    ClientPrimitiveKind Kind,
    ClientPrimitiveShape Shape,
    int MinimumXMillimeters,
    int MinimumYMillimeters,
    int MinimumZMillimeters,
    int MaximumXMillimeters,
    int MaximumYMillimeters,
    int MaximumZMillimeters,
    bool InteractionVisible)
{
    public const int MaximumExtentMillimeters = 64_000;

    public bool IsValid =>
        Kind is >= ClientPrimitiveKind.Actor and <= ClientPrimitiveKind.InteractiveMarker &&
        Shape is >= ClientPrimitiveShape.Box and <= ClientPrimitiveShape.Marker &&
        MinimumXMillimeters < MaximumXMillimeters &&
        MinimumYMillimeters < MaximumYMillimeters &&
        MinimumZMillimeters < MaximumZMillimeters &&
        (long)MaximumXMillimeters - MinimumXMillimeters <= MaximumExtentMillimeters &&
        (long)MaximumYMillimeters - MinimumYMillimeters <= MaximumExtentMillimeters &&
        (long)MaximumZMillimeters - MinimumZMillimeters <= MaximumExtentMillimeters &&
        MinimumXMillimeters is >= -MaximumExtentMillimeters and <= MaximumExtentMillimeters &&
        MinimumYMillimeters is >= -MaximumExtentMillimeters and <= MaximumExtentMillimeters &&
        MinimumZMillimeters is >= -MaximumExtentMillimeters and <= MaximumExtentMillimeters &&
        MaximumXMillimeters is >= -MaximumExtentMillimeters and <= MaximumExtentMillimeters &&
        MaximumYMillimeters is >= -MaximumExtentMillimeters and <= MaximumExtentMillimeters &&
        MaximumZMillimeters is >= -MaximumExtentMillimeters and <= MaximumExtentMillimeters;

    public int WidthMillimeters => checked(MaximumXMillimeters - MinimumXMillimeters);
    public int HeightMillimeters => checked(MaximumYMillimeters - MinimumYMillimeters);
    public int DepthMillimeters => checked(MaximumZMillimeters - MinimumZMillimeters);

    public static ClientPrimitivePresentation Grounded(
        ClientPrimitiveKind kind, ClientPrimitiveShape shape,
        int widthMillimeters, int heightMillimeters, int depthMillimeters,
        bool interactionVisible = false)
    {
        if (widthMillimeters <= 0 || heightMillimeters <= 0 || depthMillimeters <= 0 ||
            widthMillimeters > MaximumExtentMillimeters ||
            heightMillimeters > MaximumExtentMillimeters ||
            depthMillimeters > MaximumExtentMillimeters)
            throw new ArgumentOutOfRangeException(nameof(widthMillimeters));
        var result = new ClientPrimitivePresentation(kind, shape,
            -widthMillimeters / 2, 0, -depthMillimeters / 2,
            widthMillimeters - widthMillimeters / 2, heightMillimeters,
            depthMillimeters - depthMillimeters / 2, interactionVisible);
        if (!result.IsValid) throw new ArgumentOutOfRangeException(nameof(kind));
        return result;
    }

    /// <summary>Uses the already-disclosed exact structural AABB, not optional static art.</summary>
    public static bool TryFromStructuralBounds(
        in WorldStaticBounds bounds, in WorldPosition position, bool multiFragment,
        out ClientPrimitivePresentation presentation)
    {
        presentation = default;
        if (!bounds.IsValid) return false;
        long minX = bounds.MinimumXMillimeters - position.GlobalXMillimeters;
        long minY = bounds.MinimumYMillimeters - position.YMillimeters;
        long minZ = bounds.MinimumZMillimeters - position.GlobalZMillimeters;
        long maxX = bounds.MaximumXExclusiveMillimeters - position.GlobalXMillimeters;
        long maxY = bounds.MaximumYExclusiveMillimeters - position.YMillimeters;
        long maxZ = bounds.MaximumZExclusiveMillimeters - position.GlobalZMillimeters;
        if (minX < -MaximumExtentMillimeters || minY < -MaximumExtentMillimeters ||
            minZ < -MaximumExtentMillimeters || maxX > MaximumExtentMillimeters ||
            maxY > MaximumExtentMillimeters || maxZ > MaximumExtentMillimeters)
            return false;
        var candidate = new ClientPrimitivePresentation(
            multiFragment ? ClientPrimitiveKind.MultiFragment : ClientPrimitiveKind.FrozenStatic,
            ClientPrimitiveShape.Box,
            (int)minX, (int)minY, (int)minZ,
            (int)maxX, (int)maxY, (int)maxZ,
            false);
        if (!candidate.IsValid) return false;
        presentation = candidate;
        return true;
    }

    /// <summary>Uses only admitted public Definition presentation facts, never optional art or private facets.</summary>
    public static bool TryFromDefinitionDescriptor(
        in PublicDefinitionPresentationDescriptor descriptor, ClientPrimitiveKind kind,
        out ClientPrimitivePresentation presentation)
    {
        presentation = default;
        if (!descriptor.IsValid || kind is not (ClientPrimitiveKind.GroundItem or ClientPrimitiveKind.PublicObject))
            return false;
        PublicFallbackBounds bounds = descriptor.FallbackBounds;
        presentation = Grounded(kind, ClientPrimitiveShape.Box,
            bounds.WidthMillimeters, bounds.HeightMillimeters, bounds.DepthMillimeters,
            descriptor.InteractionVisible);
        return true;
    }
}

/// <summary>Small stock fallback vocabulary for already-disclosed Concept Demo public facts.</summary>
public static class ClientPrimitivePresentationCatalog
{
    public static ClientPrimitivePresentation ForActor(ClientActorVisualKind kind) => kind switch
    {
        ClientActorVisualKind.HumanX or ClientActorVisualKind.HumanY =>
            ClientPrimitivePresentation.Grounded(ClientPrimitiveKind.Actor,
                ClientPrimitiveShape.Capsule, 600, 1_800, 600, true),
        ClientActorVisualKind.WoodlandAnimal =>
            ClientPrimitivePresentation.Grounded(ClientPrimitiveKind.Actor,
                ClientPrimitiveShape.Box, 1_100, 1_150, 550, true),
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    public static ClientPrimitivePresentation ForPublic(uint prefabId) => prefabId switch
    {
        GameWorldPublicPrefabIds.CabinDoor => Public(1_800, 2_100, 700),
        GameWorldPublicPrefabIds.WorldChest => Public(1_400, 800, 1_000),
        GameWorldPublicPrefabIds.WoodlandAnimal => ForActor(ClientActorVisualKind.WoodlandAnimal),
        GameWorldPublicPrefabIds.SkyTelepad => Public(2_000, 250, 2_000),
        GameWorldPublicPrefabIds.FarGate => Public(1_700, 2_800, 800),
        GameWorldPublicPrefabIds.InformationalSign => Public(1_400, 1_650, 500),
        GameWorldPublicPrefabIds.IronSword =>
            ClientPrimitivePresentation.Grounded(ClientPrimitiveKind.GroundItem,
                ClientPrimitiveShape.Box, 500, 1_200, 500, true),
        GameWorldPublicPrefabIds.Character or GameWorldPublicPrefabIds.CharacterMale =>
            ForActor(ClientActorVisualKind.HumanX),
        _ => ClientPrimitivePresentation.Grounded(ClientPrimitiveKind.InteractiveMarker,
            ClientPrimitiveShape.Marker, 400, 900, 400),
    };

    private static ClientPrimitivePresentation Public(int width, int height, int depth) =>
        ClientPrimitivePresentation.Grounded(ClientPrimitiveKind.PublicObject,
            ClientPrimitiveShape.Box, width, height, depth, true);
}
