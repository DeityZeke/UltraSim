using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Composition;

/// <summary>Cold immutable authorship for the two declared Concept Demo Pack windows.</summary>
public sealed class ConceptDemoStructuralSource
{
    public static readonly WorldPackCoord WestCoord = new(0, 0);
    public static readonly WorldPackCoord EastCoord = new(1, 0);
    /// <summary>Frozen pre-W5R combined artifact identity, never canonical World authority.</summary>
    public static WorldPackFingerprint LegacyCombinedFingerprint
    {
        get
        {
            if (!WorldPackFingerprint.TryParse(
                    "323141A827CD25ECC5BC144CE860FECF717F472E1C0ED57626BCC89EC29CF795",
                    out WorldPackFingerprint fingerprint))
                throw new InvalidOperationException("The frozen legacy identity is malformed.");
            return fingerprint;
        }
    }

    internal ConceptDemoStructuralSource(WorldPack west, WorldPack east)
    {
        West = west;
        East = east;
        if (!West.TryValidateSpatialWindow(WestCoord, out _, out _) ||
            !East.TryValidateSpatialWindow(EastCoord, out _, out _))
            throw new InvalidOperationException("Concept Demo authorship escaped its declared Pack window.");
        if (!WorldStructuralManifest.TryCreate(
                [new(WestCoord, west.Fingerprint), new(EastCoord, east.Fingerprint)],
                out WorldStructuralManifest? manifest, out WorldStructuralManifestError error))
            throw new InvalidOperationException($"Concept Demo structural manifest failed: {error}.");
        Manifest = manifest!;
    }

    public WorldPack West { get; }
    public WorldPack East { get; }
    public WorldStructuralManifest Manifest { get; }

    public PreparedWorldComposition Prepare(WorldContentBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        if (!West.TryBind(binding, out BoundWorldPack? west, out WorldPackBindingError westError))
            throw new InvalidOperationException($"Concept Demo west Pack did not bind: {westError}.");
        if (!East.TryBind(binding, out BoundWorldPack? east, out WorldPackBindingError eastError))
            throw new InvalidOperationException($"Concept Demo east Pack did not bind: {eastError}.");
        if (!PreparedWorldComposition.TryCreate(GameWorldState.DefaultSpace,
                new WorldCompositionRevision(1), Manifest,
                [new PreparedWorldPack(WestCoord, new WorldPreparedView(west!)),
                 new PreparedWorldPack(EastCoord, new WorldPreparedView(east!))],
                out PreparedWorldComposition? composition, out PreparedWorldCompositionError error) ||
            !composition!.TryValidateComplete(out error))
            throw new InvalidOperationException($"Concept Demo composition failed: {error}.");
        return composition!;
    }

    public static WorldContentBinding CreateBinding() => new(
        GameContent.WorldFingerprint,
        [GameTerrainMaterialIds.Water, GameTerrainMaterialIds.Sand,
         GameTerrainMaterialIds.Grass, GameTerrainMaterialIds.Rock, GameTerrainMaterialIds.Snow],
        [GameWorldStaticDefinitionIds.Tree, GameWorldStaticDefinitionIds.Rock,
         GameWorldStaticDefinitionIds.CabinWall, GameWorldStaticDefinitionIds.CabinRoof,
         GameWorldStaticDefinitionIds.Shelf, GameWorldStaticDefinitionIds.Book]);
}
