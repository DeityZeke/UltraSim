using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Composition;

/// <summary>
/// Versioned P9 visual laboratory. This is a separate authored WorldPack, not a mutation of
/// Classic Cabin or its oracle. Its pieces are physical world truth; optional world.local art
/// only changes how the same definitions are realized.
/// </summary>
public static class GameCabinVisualBenchmarkWorldPack
{
    public const string DefinitionId = "benchmark-world:cabin-visual/v1";
    public const int FloorYMillimeters = 1_000;
    public const int RoomCenterXMillimeters = 16_000;
    public const int MainRoomCenterZMillimeters = 12_000;
    public const int WindowRoomCenterZMillimeters = 18_000;

    public static WorldPack Create()
        => CreateInSpace(GameWorldState.DefaultSpace);

    /// <summary>Same authored topology in a distinct spatial space; Create() retains the frozen v1 artifact.</summary>
    public static WorldPack CreateInSpace(SpatialSpaceId space)
    {
        if (!space.IsValid)
            throw new ArgumentOutOfRangeException(nameof(space));
        var builder = new WorldPackBuilder(space,
            GameContent.WorldFingerprint,
            new WorldPackLimits(2 * 1024 * 1024, 25, 8,
                maximumFrozenStatics: 16, maximumMultiInstances: 1,
                maximumMultiFragments: 64));
        var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        Array.Fill(materials, GameTerrainMaterialIds.Grass);
        var heights = new int[TerrainChunkFormat.HeightVertexCount];
        Array.Fill(heights, FloorYMillimeters);
        var offsets = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        if (!TerrainHeightEncoding.TryEncodePlane(heights, offsets,
                out TerrainHeightPlaneBase heightBase, out _, out _))
            throw new InvalidOperationException("The Cabin benchmark floor height is invalid.");
        for (int z = -2; z <= 2; z++)
        for (int x = -2; x <= 2; x++)
            if (!builder.TryAddBaseTerrain(
                    new SpatialChunkKey(space, x, z),
                    materials, heightBase, offsets, out WorldPackBuildError error))
                throw new InvalidOperationException($"Cabin benchmark terrain failed: {error}.");

        const WorldStaticFlags wall = WorldStaticFlags.Renderable | WorldStaticFlags.Collidable;
        const WorldStaticFlags shell = WorldStaticFlags.Renderable;
        WorldMultiPieceSource[] pieces =
        [
            // Room A: its only exterior opening is the existing public door at (16,9).
            Piece(1, GameWorldStaticDefinitionIds.CabinWall, 13_500, 9_000, 1_500, 150, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall),
            Piece(2, GameWorldStaticDefinitionIds.CabinWall, 18_500, 9_000, 1_500, 150, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall),
            Piece(3, GameWorldStaticDefinitionIds.CabinWall, 12_000, 10_500, 150, 1_500, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall, WorldYaw.QuarterTurn),
            Piece(4, GameWorldStaticDefinitionIds.CabinWall, 12_000, 13_500, 150, 1_500, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall, WorldYaw.QuarterTurn),
            Piece(5, GameWorldStaticDefinitionIds.CabinWall, 20_000, 10_500, 150, 1_500, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall, WorldYaw.QuarterTurn),
            Piece(6, GameWorldStaticDefinitionIds.CabinWall, 20_000, 13_500, 150, 1_500, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall, WorldYaw.QuarterTurn),
            // A true two-metre open doorway in the dividing wall; no new gameplay door.
            Piece(7, GameWorldStaticDefinitionIds.CabinWall, 13_500, 15_000, 1_500, 150, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall),
            Piece(8, GameWorldStaticDefinitionIds.CabinWall, 18_500, 15_000, 1_500, 150, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall),
            // Room B: solid west/north and precisely one east-facing window aperture.
            Piece(9, GameWorldStaticDefinitionIds.CabinWall, 12_000, 16_500, 150, 1_500, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall, WorldYaw.QuarterTurn),
            Piece(10, GameWorldStaticDefinitionIds.CabinWall, 12_000, 19_500, 150, 1_500, 2_500,
                GameWorldStaticVariants.CabinWallThreeMeters, wall, WorldYaw.QuarterTurn),
            Piece(11, GameWorldStaticDefinitionIds.CabinWall, 16_000, 21_000, 4_000, 150, 2_500,
                GameWorldStaticVariants.CabinWallEightMeters, wall),
            // The painted six-metre mesh is only the visual skin. Four separate collidable
            // pieces leave a real 2.5 m x 0.8 m aperture, not a renderer-only fake window.
            Piece(12, GameWorldStaticDefinitionIds.CabinWall, 20_000, 18_000, 150, 3_000, 2_500,
                GameWorldStaticVariants.CabinWindowSixMeters, shell, WorldYaw.QuarterTurn),
            Piece(13, GameWorldStaticDefinitionIds.CabinWall, 20_000, 15_875, 150, 875, 2_500,
                GameWorldStaticVariants.CabinWindowJamb, WorldStaticFlags.Collidable),
            Piece(14, GameWorldStaticDefinitionIds.CabinWall, 20_000, 20_125, 150, 875, 2_500,
                GameWorldStaticVariants.CabinWindowJamb, WorldStaticFlags.Collidable),
            Piece(15, GameWorldStaticDefinitionIds.CabinWall, 20_000, 18_000, 150, 1_250, 1_100,
                GameWorldStaticVariants.CabinWindowSill, WorldStaticFlags.Collidable),
            Piece(16, GameWorldStaticDefinitionIds.CabinWall, 20_000, 18_000, 150, 1_250, 600,
                GameWorldStaticVariants.CabinWindowLintel, WorldStaticFlags.Collidable,
                WorldYaw.Zero, verticalOffset: 1_900),
            // One continuous authored support surface avoids a footprint gap exactly at the
            // interior doorway. Its 100 mm top is a legal short step from the clearing.
            Piece(17, GameWorldStaticDefinitionIds.CabinRoof, 16_000, 15_000, 4_000, 6_000, 100,
                GameWorldStaticVariants.CabinFloorEightByTwelveMeters, wall),
            Piece(18, GameWorldStaticDefinitionIds.CabinRoof, 16_000, 12_000, 4_150, 3_150, 250,
                GameWorldStaticVariants.CabinRoofEightBySixMeters, shell,
                WorldYaw.Zero, verticalOffset: 2_500),
            Piece(19, GameWorldStaticDefinitionIds.CabinRoof, 16_000, 18_000, 4_150, 3_150, 250,
                GameWorldStaticVariants.CabinRoofEightBySixMeters, shell,
                WorldYaw.Zero, verticalOffset: 2_500),
        ];
        if (!builder.TryAddStaticMulti(new MultiInstanceId(1), Position(16_000, 12_000),
                WorldYaw.Zero, pieces, out WorldPackBuildError multiError))
            throw new InvalidOperationException($"Cabin benchmark structure failed: {multiError}.");

        AddStatic(10_001, GameWorldStaticDefinitionIds.Shelf, 18_500, 18_000,
            175, 450, 1_800, wall);
        AddStatic(10_002, GameWorldStaticDefinitionIds.Book, 18_500, 18_000,
            120, 90, 180, shell, 1_800);
        AddStatic(10_003, GameWorldStaticDefinitionIds.Tree, 5_000, 8_000,
            650, 650, 4_500, wall);
        AddStatic(10_004, GameWorldStaticDefinitionIds.Tree, 27_000, 24_000,
            650, 650, 4_500, wall);
        AddStatic(10_005, GameWorldStaticDefinitionIds.Rock, 7_000, 22_000,
            500, 500, 900, wall);

        if (!builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError))
            throw new InvalidOperationException($"Cabin benchmark WorldPack failed: {buildError}.");
        return artifact!.Open();

        void AddStatic(ulong id, uint definition, long x, long z, int halfX, int halfZ,
            int height, WorldStaticFlags flags, int offset = 0)
        {
            WorldPosition position = Position(x, z, offset);
            var source = new WorldFrozenStaticSource(new WorldStaticId(id),
                new WorldStaticDefinitionId(definition), position, WorldYaw.Zero, 0,
                flags, Bounds(position, halfX, halfZ, height));
            if (!builder.TryAddFrozenStatic(source, out WorldPackBuildError error))
                throw new InvalidOperationException($"Cabin benchmark static failed: {error}.");
        }
    }

    private static WorldMultiPieceSource Piece(uint id, uint definition, long x, long z,
        int halfX, int halfZ, int height, ushort variant, WorldStaticFlags flags,
        WorldYaw yaw = default, int verticalOffset = 0)
    {
        WorldPosition position = Position(x, z, verticalOffset);
        return new WorldMultiPieceSource(new MultiPieceId(id),
            new WorldStaticDefinitionId(definition), position, yaw, variant, flags,
            Bounds(position, halfX, halfZ, height));
    }

    private static WorldPosition Position(long x, long z, int verticalOffset = 0) =>
        WorldPosition.TryFromGlobalMillimeters(x, FloorYMillimeters + verticalOffset, z,
            out WorldPosition position) ? position :
            throw new InvalidOperationException("Cabin benchmark position is outside the world profile.");

    private static WorldStaticBounds Bounds(in WorldPosition position,
        int halfX, int halfZ, int height) => new(
        position.GlobalXMillimeters - halfX,
        position.YMillimeters,
        position.GlobalZMillimeters - halfZ,
        position.GlobalXMillimeters + halfX + 1,
        position.YMillimeters + height,
        position.GlobalZMillimeters + halfZ + 1);
}
