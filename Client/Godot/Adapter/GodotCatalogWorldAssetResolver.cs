using Godot;
using UltraSim.Client.Content;
using UltraSim.Game.Composition;
using UltraSim.Game.World;

namespace UltraSim.Client.Godot;

/// <summary>Shared Local/Remote native world palette. Owns only renderer-derived leases.</summary>
public readonly record struct GodotCatalogWorldAssetResolverSnapshot(int OwnedMeshes,
    int OwnedPrimitiveMaterials, int LeasedDerivedMaterials, ContentHash WallTextureHash,
    PresentationSourceKind WallTextureSource, int OptionalWorldMeshes = 0);

public sealed class GodotCatalogWorldAssetResolver : IGodotWorldAssetResolver, IDisposable
{
    private readonly GodotOwnedResources _owned = new();
    private readonly Dictionary<ushort, StandardMaterial3D> _terrain = new();
    private readonly Mesh _wood = null!;
    private readonly Mesh _stone = null!;
    private readonly Mesh _wallEight;
    private readonly Mesh _wallSix;
    private readonly Mesh _wallThree;
    private Mesh? _windowSix;
    private readonly Mesh _roof;
    private Mesh? _floor;
    private readonly Mesh _shelf;
    private readonly Mesh _book;
    private readonly Mesh _fallbackStructure;
    private readonly Mesh _character = null!;
    private readonly Mesh _door;
    private readonly Mesh _chest;
    private readonly Mesh _animal = null!;
    private readonly Mesh _telepad = null!;
    private readonly Mesh _farGate = null!;
    private readonly Mesh _informationalSign = null!;
    private readonly Mesh _sword = null!;
    private readonly Mesh _mountainWallEight = null!;
    private readonly Mesh _mountainWallTen = null!;
    private readonly Mesh _fallbackLabPedestal = null!;
    private readonly Mesh _fallbackLabModel = null!;
    private StandardMaterial3D? _roofMaterial;
    private readonly List<GodotDerivedMaterialLease> _materialLeases = [];
    private readonly Dictionary<LogicalContentId, Mesh> _worldMeshes = new();
    private GodotDerivedMaterialLease? _currentWall;
    private GodotDerivedMaterialLease? _currentWood;
    private bool _disposed;

    public GodotCatalogWorldAssetResolver(
        IWorldPresentationGenerationSource generation,
        GodotDerivedResourceStore derived)
    {
        try
        {
            _terrain[GameTerrainMaterialIds.Water] = Material(new Color(0.08f, 0.28f, 0.5f));
            _terrain[GameTerrainMaterialIds.Sand] = Material(new Color(0.62f, 0.55f, 0.34f));
            _terrain[GameTerrainMaterialIds.Grass] = Material(new Color(0.16f, 0.38f, 0.14f));
            _terrain[GameTerrainMaterialIds.Rock] = Material(new Color(0.32f, 0.34f, 0.31f));
            _terrain[GameTerrainMaterialIds.Snow] = Material(new Color(0.78f, 0.84f, 0.82f));
            _wood = GroundedBox(new Vector3(0.75f, 4.5f, 0.75f), new Color(0.24f, 0.14f, 0.07f));
            _stone = GroundedBox(Vector3.One, new Color(0.34f, 0.36f, 0.34f));
            _character = CharacterMesh();
            _animal = GroundedBox(new Vector3(1.1f, 1.15f, 0.55f), new Color(0.48f, 0.35f, 0.23f));
            _telepad = GroundedBox(new Vector3(1.8f, 0.18f, 1.8f), new Color(0.12f, 0.72f, 0.9f));
            _farGate = GroundedBox(new Vector3(1.5f, 2.8f, 0.35f), new Color(0.68f, 0.22f, 0.86f));
            _informationalSign = GroundedBox(new Vector3(1.25f, 1.65f, 0.16f), new Color(0.52f, 0.31f, 0.12f));
            _sword = GroundedBox(new Vector3(0.12f, 1.15f, 0.20f), new Color(0.72f, 0.75f, 0.78f));
            _mountainWallEight = GroundedBox(new Vector3(8f, 7.2f, 0.4f), new Color(0.29f, 0.30f, 0.27f));
            _mountainWallTen = GroundedBox(new Vector3(10f, 7.2f, 0.4f), new Color(0.29f, 0.30f, 0.27f));
            _fallbackLabPedestal = GroundedBox(new Vector3(2f, 0.4f, 2f), new Color(0.23f, 0.24f, 0.26f));
            _fallbackLabModel = _owned.Own(GodotMeshGeometry.CreateGroundedPointerWedge(
                0.5f, 0.42f, 0.72f, 1.6f, Material(new Color(0.18f, 0.78f, 0.36f))));
            LogicalContentId fallbackWood = OfficialCabinPresentation.Material("wood");
            LogicalContentId wallId = generation.GetSource(
                ClientWorldPresentationProfile.WallMaterial) == PresentationSourceKind.Primitive
                ? fallbackWood : ClientWorldPresentationProfile.WallMaterial;
            LogicalContentId woodId = generation.GetSource(
                ClientWorldPresentationProfile.WoodMaterial) == PresentationSourceKind.Primitive
                ? fallbackWood : ClientWorldPresentationProfile.WoodMaterial;
            GodotDerivedMaterialLease wallLease = AcquireMaterial(
                generation, derived, wallId,
                out ContentHash wallHash, out PresentationSourceKind wallSource);
            GodotDerivedMaterialLease woodLease = AcquireMaterial(
                generation, derived, woodId, out _, out _);
            StandardMaterial3D wall = wallLease.Material;
            StandardMaterial3D wood = woodLease.Material;
            _currentWall = wallLease;
            _currentWood = woodLease;
            WallTextureHash = wallHash;
            WallTextureSource = wallSource;
            WallDerivedKey = wallLease.Key;
            _wallEight = GroundedBox(new Vector3(8f, 2.5f, 0.3f), wall);
            _wallSix = GroundedBox(new Vector3(6f, 2.5f, 0.3f), wall);
            _wallThree = GroundedBox(new Vector3(3f, 2.5f, 0.3f), wall);
            _roofMaterial = RoofMaterial(wood);
            _roof = GroundedBox(new Vector3(8.3f, 0.25f, 6.3f), _roofMaterial);
            _shelf = GroundedBox(new Vector3(0.9f, 1.8f, 0.35f), wood);
            _book = GroundedBox(new Vector3(0.24f, 0.18f, 0.18f), wood);
            _fallbackStructure = GroundedBox(new Vector3(1f, 2f, 0.35f), wall);
            _door = GroundedBox(new Vector3(1.6f, 2.1f, 0.22f), wood);
            _chest = GroundedBox(new Vector3(1.2f, 0.8f, 0.8f), wood);
            PrepareOptionalWorldMeshes(generation, derived);
            if (generation.GetSource(ClientWorldPresentationProfile.BlueGlowMaterial) !=
                PresentationSourceKind.Primitive)
                AddedMaterial = AcquireMaterial(
                    generation, derived, ClientWorldPresentationProfile.BlueGlowMaterial,
                    out _, out _).Material;
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    public ContentHash WallTextureHash { get; private set; }
    public PresentationSourceKind WallTextureSource { get; private set; }
    public GodotDerivedResourceKey WallDerivedKey { get; private set; }
    public StandardMaterial3D? AddedMaterial { get; }
    public bool HasOptionalWorldMeshes => _worldMeshes.Count != 0;
    public int OptionalWorldMeshCount => _worldMeshes.Count;
    internal StandardMaterial3D? DiagnosticRoofMaterial => _roofMaterial;
    public GodotCatalogWorldAssetResolverSnapshot GetSnapshot() => _disposed ? default :
        new(21 + (_floor is null ? 0 : 1) + (_windowSix is null ? 0 : 1) + _worldMeshes.Count,
            18 + (HasOptionalWorldMeshes ? 6 : 0),
            _materialLeases.Count, WallTextureHash, WallTextureSource, _worldMeshes.Count);

    /// <summary>Commit changed materials onto retained meshes. Unchanged groups/instances are untouched.</summary>
    public void Refresh(IWorldPresentationGenerationSource generation, GodotDerivedResourceStore derived)
    {
        GodotDerivedMaterialLease? wall = null, wood = null;
        try
        {
            wall = AcquireMaterial(generation, derived, ClientWorldPresentationProfile.WallMaterial,
                out ContentHash hash, out PresentationSourceKind source);
            wood = AcquireMaterial(generation, derived, ClientWorldPresentationProfile.WoodMaterial, out _, out _);
            if (wall.Key != _currentWall!.Key)
            {
                foreach (Mesh mesh in new[] { _wallEight, _wallSix, _wallThree, _fallbackStructure })
                    ((ArrayMesh)mesh).SurfaceSetMaterial(0, wall.Material);
                if (_windowSix is ArrayMesh window) window.SurfaceSetMaterial(0, wall.Material);
                Release(_currentWall); _currentWall = wall; wall = null;
            }
            if (wood.Key != _currentWood!.Key)
            {
                StandardMaterial3D roof = RoofMaterial(wood.Material);
                ((ArrayMesh)_roof).SurfaceSetMaterial(0, roof);
                StandardMaterial3D? previousRoof = _roofMaterial;
                _roofMaterial = roof;
                if (previousRoof is not null) _owned.Release(previousRoof);
                foreach (Mesh mesh in new[] { _shelf, _book, _door, _chest })
                    ((ArrayMesh)mesh).SurfaceSetMaterial(0, wood.Material);
                if (_floor is ArrayMesh floor) floor.SurfaceSetMaterial(0, wood.Material);
                Release(_currentWood); _currentWood = wood; wood = null;
            }
            WallTextureHash = hash; WallTextureSource = source; WallDerivedKey = _currentWall.Key;
        }
        finally { if (wall is not null) Release(wall); if (wood is not null) Release(wood); }
    }

    private void Release(GodotDerivedMaterialLease lease)
    { lease.Dispose(); _materialLeases.Remove(lease); }

    public void Dispose()
    {
        if (_disposed) return;
        List<Exception>? failures = null;
        try { _owned.Dispose(); }
        catch (Exception error) { (failures ??= []).Add(error); }
        for (int index = _materialLeases.Count - 1; index >= 0; index--)
        {
            try
            {
                _materialLeases[index].Dispose();
                _materialLeases.RemoveAt(index);
            }
            catch (Exception error) { (failures ??= []).Add(error); }
        }
        if (failures is { Count: > 0 })
            throw failures.Count == 1 ? failures[0] :
                new AggregateException("World palette retirement failed.", failures);
        _disposed = true;
    }

    private GodotDerivedMaterialLease AcquireMaterial(
        IWorldPresentationGenerationSource generation,
        GodotDerivedResourceStore derived,
        LogicalContentId id,
        out ContentHash textureHash,
        out PresentationSourceKind textureSource)
    {
        textureHash = default;
        textureSource = PresentationSourceKind.Primitive;
        if (!generation.TryResolveMaterialPresentation(
                id, out ResolvedMaterialPresentation? logical, out var resolutionError))
            throw new InvalidDataException(
                $"Cabin material '{id}' is unavailable: {resolutionError}.");
        textureHash = logical!.AlbedoTexture?.File.Sha256 ?? default;
        textureSource = logical.AlbedoTexture?.Source ?? PresentationSourceKind.Primitive;
        if (!derived.TryAcquireMaterial(logical, out GodotDerivedMaterialLease? native,
                out string? error))
        {
            logical.Dispose();
            throw new InvalidDataException(
                $"Cabin material '{id}' could not be realized: {error}");
        }
        _materialLeases.Add(native!);
        return native!;
    }

    private GodotWorldMeshResolution ResolveFallbackLabMesh(ushort variant) => variant switch
    {
        GameWorldStaticVariants.FallbackLabCorrect =>
            GodotWorldMeshResolution.Available(_fallbackLabModel),
        GameWorldStaticVariants.FallbackLabModelMissing =>
            GodotWorldMeshResolution.MissingModel(),
        GameWorldStaticVariants.FallbackLabTextureMissing =>
            GodotWorldMeshResolution.MissingTexture(_fallbackLabModel),
        GameWorldStaticVariants.FallbackLabModelAndTextureMissing =>
            GodotWorldMeshResolution.MissingModelAndTexture(),
        _ => GodotWorldMeshResolution.Available(ResolveStaticMeshCore(
            new WorldStaticRenderKey(
                new WorldStaticDefinitionId(GameWorldStaticDefinitionIds.Rock),
                variant,
                WorldStaticFlags.Renderable))),
    };

    private Mesh ResolveStaticMeshCore(WorldStaticRenderKey key) => key.DefinitionId switch
    {
        var id when id.Value == GameWorldStaticDefinitionIds.Tree => WorldMesh("pine", _wood),
        var id when id.Value == GameWorldStaticDefinitionIds.Rock => key.Variant switch
        {
            GameWorldStaticVariants.MountainWallEightMeters => _mountainWallEight,
            GameWorldStaticVariants.MountainWallTenMeters => _mountainWallTen,
            GameWorldStaticVariants.FallbackLabPedestal => _fallbackLabPedestal,
            GameWorldStaticVariants.FallbackLabCorrect or
                GameWorldStaticVariants.FallbackLabModelMissing or
                GameWorldStaticVariants.FallbackLabTextureMissing or
                GameWorldStaticVariants.FallbackLabModelAndTextureMissing => _fallbackLabModel,
            _ => key.Variant % 2 == 0 ? WorldMesh("rock-a", _stone) :
                WorldMesh("rock-b", _stone),
        },
        var id when id.Value == GameWorldStaticDefinitionIds.CabinWall => key.Variant switch
        {
            GameWorldStaticVariants.CabinWallEightMeters => WorldMesh("wall-8m", _wallEight),
            GameWorldStaticVariants.CabinWallSixMeters => WorldMesh("wall-6m", _wallSix),
            GameWorldStaticVariants.CabinWallThreeMeters => WorldMesh("wall-3m", _wallThree),
            GameWorldStaticVariants.CabinWindowSixMeters =>
                _worldMeshes.TryGetValue(OfficialCabinPresentation.Mesh("wall-6m"), out Mesh? window)
                    ? window : WindowWallMesh(),
            _ => _fallbackStructure,
        },
        var id when id.Value == GameWorldStaticDefinitionIds.CabinRoof =>
            key.Variant == GameWorldStaticVariants.CabinFloorEightByTwelveMeters
                ? FloorMesh() : WorldMesh("roof", _roof),
        var id when id.Value == GameWorldStaticDefinitionIds.Shelf => WorldMesh("shelf", _shelf),
        var id when id.Value == GameWorldStaticDefinitionIds.Book => WorldMesh("book", _book),
        _ => _fallbackStructure,
    };

    GodotWorldMeshResolution IGodotWorldAssetResolver.ResolveStaticMesh(
        WorldStaticRenderKey key) =>
        key.DefinitionId.Value == GameWorldStaticDefinitionIds.Rock &&
        key.Variant is >= GameWorldStaticVariants.FallbackLabCorrect and
            <= GameWorldStaticVariants.FallbackLabModelAndTextureMissing
            ? ResolveFallbackLabMesh(key.Variant)
            : GodotWorldMeshResolution.Available(ResolveStaticMeshCore(key));

    public GodotWorldMeshResolution ResolvePublicMesh(uint prefabId) =>
        GodotWorldMeshResolution.Available(ResolvePublicMeshCore(prefabId));

    private Mesh ResolvePublicMeshCore(uint prefabId) => prefabId switch
    {
        GameWorldPublicPrefabIds.Character or GameWorldPublicPrefabIds.CharacterMale => _character,
        GameWorldPublicPrefabIds.CabinDoor => WorldMesh("door", _door),
        GameWorldPublicPrefabIds.WorldChest => WorldMesh("chest", _chest),
        GameWorldPublicPrefabIds.WoodlandAnimal => _animal,
        GameWorldPublicPrefabIds.SkyTelepad => _telepad,
        GameWorldPublicPrefabIds.FarGate => _farGate,
        GameWorldPublicPrefabIds.InformationalSign => _informationalSign,
        GameWorldPublicPrefabIds.IronSword => _sword,
        _ => _stone,
    };

    public Material? ResolveTerrainMaterial(ushort materialId) =>
        _terrain.TryGetValue(materialId, out StandardMaterial3D? material) ? material : null;

    private StandardMaterial3D Material(Color color) => _owned.Own(new StandardMaterial3D
    {
        AlbedoColor = color,
        Roughness = 0.95f,
    });

    private ArrayMesh GroundedBox(Vector3 size, Color color)
    {
        StandardMaterial3D material = Material(color);
        return GroundedBox(size, material);
    }

    private ArrayMesh GroundedBox(Vector3 size, StandardMaterial3D material) =>
        _owned.Own(GodotMeshGeometry.CreateGroundedBox(size, material));

    private StandardMaterial3D RoofMaterial(StandardMaterial3D wood)
    {
        var material = _owned.Own((StandardMaterial3D)wood.Duplicate());
        material.AlbedoColor = new Color(material.AlbedoColor, 0.42f);
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return material;
    }

    private ArrayMesh CharacterMesh()
    {
        StandardMaterial3D material = Material(new Color(0, 0, 0, 0));
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return _owned.Own(GodotMeshGeometry.CreateGroundedPointerWedge(
            0.42f, 0.42f, 0.68f, 1.8f, material));
    }

    private Mesh WorldMesh(string stem, Mesh fallback) =>
        _worldMeshes.TryGetValue(OfficialCabinPresentation.Mesh(stem), out Mesh? mesh)
            ? mesh : fallback;

    // The floor is a structural benchmark piece; its wood is resolved from the same P9
    // world.local material. Classic Cabin never creates this additional native resource.
    private Mesh FloorMesh() => _floor ??= GroundedBox(
        new Vector3(8f, 0.10f, 12f), _currentWood!.Material);

    private Mesh WindowWallMesh() => _windowSix ??= _owned.Own(
        GodotMeshGeometry.CreateGroundedWindowWall(_currentWall!.Material));

    private void PrepareOptionalWorldMeshes(
        IWorldPresentationGenerationSource generation, GodotDerivedResourceStore derived)
    {
        if (generation.GetSource(OfficialCabinPresentation.Material("wood")) ==
            PresentationSourceKind.Primitive)
            return;
        string[] families = ["wood", "pine", "stone", "roof", "glass"];
        var materials = new Material[families.Length];
        for (int index = 0; index < families.Length; index++)
        {
            GodotDerivedMaterialLease lease = AcquireMaterial(generation, derived,
                OfficialCabinPresentation.Material(families[index]), out _, out _);
            var display = _owned.Own((StandardMaterial3D)lease.Material.Duplicate());
            display.VertexColorUseAsAlbedo = true;
            if (families[index] == "glass")
            {
                display.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                display.AlbedoColor = new Color(0.65f, 0.86f, 0.90f, 0.70f);
            }
            materials[index] = display;
        }
        GodotDerivedMaterialLease groundLease = AcquireMaterial(generation, derived,
            OfficialCabinPresentation.Material("ground"), out _, out _);
        _terrain[GameTerrainMaterialIds.Grass] =
            _owned.Own((StandardMaterial3D)groundLease.Material.Duplicate());
        foreach (string stem in new[] { "pine", "rock-a", "rock-b", "wall-8m", "wall-6m",
                     "wall-3m", "roof", "door", "shelf", "book", "chest" })
        {
            LogicalContentId id = OfficialCabinPresentation.Mesh(stem);
            if (!generation.TryReadWorldMeshPresentation(id, out byte[]? bytes,
                    out _, out _) || bytes is null) continue;
            var mesh = _owned.Own(GodotMeshGeometry.CreateWorldPresentationMesh(bytes, materials));
            _worldMeshes.Add(id, mesh);
        }
    }

}
