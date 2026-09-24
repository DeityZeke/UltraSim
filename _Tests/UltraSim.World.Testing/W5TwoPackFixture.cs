using UltraSim.Game.World;
using UltraSim.Game.Composition;
using UltraSim.Spatial;

namespace UltraSim.World.Testing;

/// <summary>Small independently compiled declared-shard proof fixture; never shipped as product content.</summary>
public static class W5TwoPackFixture
{
    public static readonly SpatialSpaceId Space = GameWorldState.DefaultSpace;
    public static readonly WorldContentFingerprint Content = GameContent.WorldFingerprint;
    public static readonly WorldPackCoord WestCoord = new(0, 0);
    public static readonly WorldPackCoord EastCoord = new(1, 0);

    public static (PreparedWorldComposition Composition, BoundWorldPack West, BoundWorldPack East) Create(
        bool brokenSeam = false, bool duplicateStaticId = false, bool duplicateMultiId = false)
    {
        BoundWorldPack west = Build(WestCoord, 125, 127, 0, 701, 1001);
        BoundWorldPack east = Build(EastCoord, 128, 130, brokenSeam ? 5 : 0,
            duplicateStaticId ? 701UL : 702UL, duplicateMultiId ? 1001UL : 1002UL);
        if (!WorldStructuralManifest.TryCreate(
                [new(WestCoord, west.Fingerprint), new(EastCoord, east.Fingerprint)],
                out WorldStructuralManifest? manifest, out WorldStructuralManifestError manifestError))
            throw new InvalidOperationException($"W5 manifest build failed: {manifestError}.");
        if (!PreparedWorldComposition.TryCreate(Space, new WorldCompositionRevision(1), manifest,
                [new PreparedWorldPack(WestCoord, new WorldPreparedView(west)),
                 new PreparedWorldPack(EastCoord, new WorldPreparedView(east))],
                out PreparedWorldComposition? composition, out PreparedWorldCompositionError error))
            throw new InvalidOperationException($"W5 composition build failed: {error}.");
        return (composition!, west, east);
    }

    private static BoundWorldPack Build(WorldPackCoord coord, int firstX, int lastX,
        int heightMillimeters, ulong staticId, ulong multiId)
    {
        var builder = new WorldPackBuilder(Space, Content, new WorldPackLimits(1024 * 1024, 16, 8), coord);
        var materials = new ushort[TerrainChunkFormat.MaterialCellCount];
        Array.Fill(materials, (ushort)1);
        var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
        if (!TerrainHeightPlaneBase.TryCreate(heightMillimeters, out TerrainHeightPlaneBase heightBase))
            throw new InvalidOperationException("W5 terrain base is invalid.");
        for (int x = firstX; x <= lastX; x++)
        for (int z = -2; z <= 2; z++)
            if (!builder.TryAddBaseTerrain(new SpatialChunkKey(Space, x, z),
                    materials, heightBase, heights, out WorldPackBuildError terrainError))
                throw new InvalidOperationException($"W5 terrain build failed: {terrainError}.");

        int structuralX = coord == WestCoord ? 127 : 128;
        var staticPosition = new WorldPosition(structuralX, 0, 2_000, 2_000, heightMillimeters);
        if (!builder.TryAddFrozenStatic(new WorldFrozenStaticSource(
                new WorldStaticId(staticId), new WorldStaticDefinitionId(1), staticPosition,
                WorldYaw.Zero, 0, WorldStaticFlags.Renderable | WorldStaticFlags.Collidable,
                Bounds(staticPosition, 200)), out WorldPackBuildError staticError))
            throw new InvalidOperationException($"W5 static build failed: {staticError}.");
        var multiPosition = new WorldPosition(structuralX, 0, 3_000, 2_000, heightMillimeters);
        var piece = new WorldMultiPieceSource(new MultiPieceId(1), new WorldStaticDefinitionId(2),
            multiPosition, WorldYaw.Zero, 0,
            WorldStaticFlags.Renderable | WorldStaticFlags.Collidable, Bounds(multiPosition, 200));
        if (!builder.TryAddStaticMulti(new MultiInstanceId(multiId), multiPosition,
                WorldYaw.Zero, [piece], out WorldPackBuildError multiError))
            throw new InvalidOperationException($"W5 Multi build failed: {multiError}.");
        if (!builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError))
            throw new InvalidOperationException($"W5 Pack build failed: {buildError}.");
        if (!artifact!.Open().TryBind(new WorldContentBinding(Content, [1, 2, 3], [1, 2]),
                out BoundWorldPack? bound, out WorldPackBindingError bindingError))
            throw new InvalidOperationException($"W5 Pack binding failed: {bindingError}.");
        return bound!;
    }

    private static WorldStaticBounds Bounds(in WorldPosition position, int halfExtent) => new(
        position.GlobalXMillimeters - halfExtent, position.YMillimeters - halfExtent,
        position.GlobalZMillimeters - halfExtent, position.GlobalXMillimeters + halfExtent,
        position.YMillimeters + halfExtent, position.GlobalZMillimeters + halfExtent);
}
