namespace UltraSim.Game.Composition;

/// <summary>Compact structural terrain identities admitted by the first game world content profile.</summary>
public static class GameTerrainMaterialIds
{
    public const ushort Water = 1;
    public const ushort Sand = 2;
    public const ushort Grass = 3;
    public const ushort Rock = 4;
    public const ushort Snow = 5;
}

/// <summary>Compact immutable-world definition identities admitted by the first game world profile.</summary>
public static class GameWorldStaticDefinitionIds
{
    public const uint Tree = 1;
    public const uint Rock = 2;
    public const uint CabinWall = 3;
    public const uint CabinRoof = 4;
    public const uint Shelf = 5;
    public const uint Book = 6;
}

/// <summary>
/// Definition-local presentation variants used by the concrete authored woods/cabin fixture.
/// These are content semantics rather than renderer slot or engine identities.
/// </summary>
public static class GameWorldStaticVariants
{
    public const ushort CabinWallEightMeters = 1;
    public const ushort CabinWallSixMeters = 2;
    public const ushort CabinWallThreeMeters = 3;
    public const ushort CabinRoofEightBySixMeters = 1;
    public const ushort MountainWallEightMeters = 100;
    public const ushort MountainWallTenMeters = 101;
    public const ushort FallbackLabPedestal = 200;
    public const ushort FallbackLabCorrect = 201;
    public const ushort FallbackLabModelMissing = 202;
    public const ushort FallbackLabTextureMissing = 203;
    public const ushort FallbackLabModelAndTextureMissing = 204;
}

/// <summary>Compact public-projection prefab identities admitted by the concrete game.</summary>
public static class GameWorldPublicPrefabIds
{
    public const uint Character = 1;
    public const uint CabinDoor = 2;
    public const uint WorldChest = 3;
    public const uint WoodlandAnimal = 4;
    public const uint SkyTelepad = 5;
    public const uint FarGate = 6;
    public const uint InformationalSign = 7;
    public const uint CharacterMale = 8;
    public const uint IronSword = 9;
}

/// <summary>
/// Stable Game vocabulary required by a compatible thin client to bind the canonical Concept Demo
/// WorldPack. It carries no world bytes and no authority implementation.
/// </summary>
public static class GameWorldContentProfile
{
    private static readonly ushort[] TerrainMaterials =
    [
        GameTerrainMaterialIds.Water,
        GameTerrainMaterialIds.Sand,
        GameTerrainMaterialIds.Grass,
        GameTerrainMaterialIds.Rock,
        GameTerrainMaterialIds.Snow,
    ];
    private static readonly uint[] StaticDefinitions =
    [
        GameWorldStaticDefinitionIds.Tree,
        GameWorldStaticDefinitionIds.Rock,
        GameWorldStaticDefinitionIds.CabinWall,
        GameWorldStaticDefinitionIds.CabinRoof,
        GameWorldStaticDefinitionIds.Shelf,
        GameWorldStaticDefinitionIds.Book,
    ];

    public const ulong ContentFingerprintLow = 0x3B8D_14CB_E652_EE21UL;
    public const ulong ContentFingerprintHigh = 0xD241_8739_B74F_10A5UL;
    public const string ConceptDemoDefinitionId = "server-world:world/concept-demo";
    public const string ConceptDemoPackFingerprint =
        "323141A827CD25ECC5BC144CE860FECF717F472E1C0ED57626BCC89EC29CF795";

    public static ReadOnlySpan<ushort> TerrainMaterialIds => TerrainMaterials;
    public static ReadOnlySpan<uint> StaticDefinitionIds => StaticDefinitions;
}
