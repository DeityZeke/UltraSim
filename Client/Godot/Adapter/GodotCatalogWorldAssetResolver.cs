using Godot;
using UltraSim.Client.Content;
using UltraSim.Game.Composition;
using UltraSim.Game.World;

namespace UltraSim.Client.Godot;

/// <summary>Shared Local/Remote native world palette. Owns only renderer-derived leases.</summary>
public readonly record struct GodotCatalogWorldAssetResolverSnapshot(int OwnedMeshes,
    int OwnedPrimitiveMaterials, int LeasedDerivedMaterials, ContentHash WallTextureHash,
    PresentationSourceKind WallTextureSource);

public sealed class GodotCatalogWorldAssetResolver : IGodotWorldAssetResolver, IDisposable
{
    private readonly Dictionary<ushort, StandardMaterial3D> _terrain = new()
    {
        [GameTerrainMaterialIds.Water] = Material(new Color(0.08f, 0.28f, 0.5f)),
        [GameTerrainMaterialIds.Sand] = Material(new Color(0.62f, 0.55f, 0.34f)),
        [GameTerrainMaterialIds.Grass] = Material(new Color(0.16f, 0.38f, 0.14f)),
        [GameTerrainMaterialIds.Rock] = Material(new Color(0.32f, 0.34f, 0.31f)),
        [GameTerrainMaterialIds.Snow] = Material(new Color(0.78f, 0.84f, 0.82f)),
    };
    private readonly Mesh _wood = GroundedBox(
        new Vector3(0.75f, 4.5f, 0.75f), new Color(0.24f, 0.14f, 0.07f));
    private readonly Mesh _stone = GroundedBox(
        Vector3.One, new Color(0.34f, 0.36f, 0.34f));
    private readonly Mesh _wallEight;
    private readonly Mesh _wallSix;
    private readonly Mesh _wallThree;
    private readonly Mesh _roof;
    private readonly Mesh _shelf;
    private readonly Mesh _book;
    private readonly Mesh _fallbackStructure;
    private readonly Mesh _character = CharacterMesh();
    private readonly Mesh _door;
    private readonly Mesh _chest;
    private readonly Mesh _animal = GroundedBox(
        new Vector3(1.1f, 1.15f, 0.55f), new Color(0.48f, 0.35f, 0.23f));
    private readonly Mesh _telepad = GroundedBox(
        new Vector3(1.8f, 0.18f, 1.8f), new Color(0.12f, 0.72f, 0.9f));
    private readonly Mesh _farGate = GroundedBox(
        new Vector3(1.5f, 2.8f, 0.35f), new Color(0.68f, 0.22f, 0.86f));
    private readonly Mesh _informationalSign = GroundedBox(
        new Vector3(1.25f, 1.65f, 0.16f), new Color(0.52f, 0.31f, 0.12f));
    private readonly Mesh _sword = GroundedBox(
        new Vector3(0.12f, 1.15f, 0.20f), new Color(0.72f, 0.75f, 0.78f));
    private readonly Mesh _mountainWallEight = GroundedBox(
        new Vector3(8f, 7.2f, 0.4f), new Color(0.29f, 0.30f, 0.27f));
    private readonly Mesh _mountainWallTen = GroundedBox(
        new Vector3(10f, 7.2f, 0.4f), new Color(0.29f, 0.30f, 0.27f));
    private readonly Mesh _fallbackLabPedestal = GroundedBox(
        new Vector3(2f, 0.4f, 2f), new Color(0.23f, 0.24f, 0.26f));
    private readonly Mesh _fallbackLabModel = GodotMeshGeometry.CreateGroundedPointerWedge(
        0.5f,
        0.42f,
        0.72f,
        1.6f,
        Material(new Color(0.18f, 0.78f, 0.36f)));
    private readonly List<GodotDerivedMaterialLease> _materialLeases = [];
    private GodotDerivedMaterialLease? _currentWall;
    private GodotDerivedMaterialLease? _currentWood;
    private bool _disposed;

    public GodotCatalogWorldAssetResolver(
        IWorldPresentationGenerationSource generation,
        GodotDerivedResourceStore derived)
    {
        try
        {
            GodotDerivedMaterialLease wallLease = AcquireMaterial(
                generation, derived, ClientWorldPresentationProfile.WallMaterial,
                out ContentHash wallHash, out PresentationSourceKind wallSource);
            GodotDerivedMaterialLease woodLease = AcquireMaterial(
                generation, derived, ClientWorldPresentationProfile.WoodMaterial, out _, out _);
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
            _roof = GroundedBox(new Vector3(8.3f, 0.25f, 6.3f), wood, transparent: true);
            _shelf = GroundedBox(new Vector3(0.9f, 1.8f, 0.35f), wood);
            _book = GroundedBox(new Vector3(0.24f, 0.18f, 0.18f), wood);
            _fallbackStructure = GroundedBox(new Vector3(1f, 2f, 0.35f), wall);
            _door = GroundedBox(new Vector3(1.6f, 2.1f, 0.22f), wood);
            _chest = GroundedBox(new Vector3(1.2f, 0.8f, 0.8f), wood);
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
    public GodotCatalogWorldAssetResolverSnapshot GetSnapshot() => _disposed ? default :
        new(21, 18, _materialLeases.Count, WallTextureHash, WallTextureSource);

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
                Release(_currentWall); _currentWall = wall; wall = null;
            }
            if (wood.Key != _currentWood!.Key)
            {
                var roof = (StandardMaterial3D)wood.Material.Duplicate();
                roof.AlbedoColor = new Color(roof.AlbedoColor, .42f);
                roof.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
                Material previousRoof = ((ArrayMesh)_roof).SurfaceGetMaterial(0);
                ((ArrayMesh)_roof).SurfaceSetMaterial(0, roof);
                previousRoof.Dispose();
                foreach (Mesh mesh in new[] { _shelf, _book, _door, _chest })
                    ((ArrayMesh)mesh).SurfaceSetMaterial(0, wood.Material);
                Release(_currentWood); _currentWood = wood; wood = null;
            }
            WallTextureHash = hash; WallTextureSource = source; WallDerivedKey = _currentWall.Key;
        }
        finally { if (wall is not null) Release(wall); if (wood is not null) Release(wood); }
    }

    private void Release(GodotDerivedMaterialLease lease)
    { _materialLeases.Remove(lease); lease.Dispose(); }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        var retiredMaterials = new HashSet<Rid>();
        foreach (Mesh? mesh in new Mesh?[]
        {
            _wood, _stone, _wallEight, _wallSix, _wallThree, _roof, _shelf, _book,
            _fallbackStructure, _character, _door, _chest, _animal, _telepad, _farGate,
            _informationalSign, _sword, _mountainWallEight, _mountainWallTen,
            _fallbackLabPedestal, _fallbackLabModel,
        })
        {
            if (mesh is null) continue;
            for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
            {
                Material? material = mesh.SurfaceGetMaterial(surface);
                if (material is null || !retiredMaterials.Add(material.GetRid()) ||
                    _materialLeases.Any(lease => lease.Material.GetRid() == material.GetRid())) continue;
                material.Dispose();
            }
            mesh.Dispose();
        }
        foreach (StandardMaterial3D material in _terrain.Values) material.Dispose();
        for (int index = _materialLeases.Count - 1; index >= 0; index--)
            _materialLeases[index].Dispose();
        _materialLeases.Clear();
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
        var id when id.Value == GameWorldStaticDefinitionIds.Tree => _wood,
        var id when id.Value == GameWorldStaticDefinitionIds.Rock => key.Variant switch
        {
            GameWorldStaticVariants.MountainWallEightMeters => _mountainWallEight,
            GameWorldStaticVariants.MountainWallTenMeters => _mountainWallTen,
            GameWorldStaticVariants.FallbackLabPedestal => _fallbackLabPedestal,
            GameWorldStaticVariants.FallbackLabCorrect or
                GameWorldStaticVariants.FallbackLabModelMissing or
                GameWorldStaticVariants.FallbackLabTextureMissing or
                GameWorldStaticVariants.FallbackLabModelAndTextureMissing => _fallbackLabModel,
            _ => _stone,
        },
        var id when id.Value == GameWorldStaticDefinitionIds.CabinWall => key.Variant switch
        {
            GameWorldStaticVariants.CabinWallEightMeters => _wallEight,
            GameWorldStaticVariants.CabinWallSixMeters => _wallSix,
            GameWorldStaticVariants.CabinWallThreeMeters => _wallThree,
            _ => _fallbackStructure,
        },
        var id when id.Value == GameWorldStaticDefinitionIds.CabinRoof => _roof,
        var id when id.Value == GameWorldStaticDefinitionIds.Shelf => _shelf,
        var id when id.Value == GameWorldStaticDefinitionIds.Book => _book,
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
        GameWorldPublicPrefabIds.CabinDoor => _door,
        GameWorldPublicPrefabIds.WorldChest => _chest,
        GameWorldPublicPrefabIds.WoodlandAnimal => _animal,
        GameWorldPublicPrefabIds.SkyTelepad => _telepad,
        GameWorldPublicPrefabIds.FarGate => _farGate,
        GameWorldPublicPrefabIds.InformationalSign => _informationalSign,
        GameWorldPublicPrefabIds.IronSword => _sword,
        _ => _stone,
    };

    public Material? ResolveTerrainMaterial(ushort materialId) =>
        _terrain.TryGetValue(materialId, out StandardMaterial3D? material) ? material : null;

    private static StandardMaterial3D Material(Color color) => new()
    {
        AlbedoColor = color,
        Roughness = 0.95f,
    };

    private static ArrayMesh GroundedBox(Vector3 size, Color color, bool transparent = false)
    {
        StandardMaterial3D material = Material(color);
        if (transparent)
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return GodotMeshGeometry.CreateGroundedBox(size, material);
    }

    private static ArrayMesh GroundedBox(
        Vector3 size,
        StandardMaterial3D material,
        bool transparent = false)
    {
        if (transparent)
        {
            material = (StandardMaterial3D)material.Duplicate();
            material.AlbedoColor = new Color(material.AlbedoColor, 0.42f);
            material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        }
        return GodotMeshGeometry.CreateGroundedBox(size, material);
    }

    private static ArrayMesh CharacterMesh()
    {
        StandardMaterial3D material = Material(new Color(0, 0, 0, 0));
        material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
        return GodotMeshGeometry.CreateGroundedPointerWedge(
            0.42f, 0.42f, 0.68f, 1.8f, material);
    }

}
