namespace UltraSim.Client.Content;

/// <summary>
/// Optional, removable P9 world.local visual art. This is not the A0.1 Cabin oracle, a
/// WorldPack, or Game material truth. Exact hashes prevent a source-root path from silently
/// changing what the application authorizes.
/// </summary>
public static class OfficialCabinPresentation
{
    public const string ArchiveFileName = "ultrasim-official-cabin-p9-v1.uspkg";
    public const string AssetDirectory = "Cabin";

    private static readonly (string Path, ContentFileKind Kind, long Bytes, string Hash)[] Files =
    [
        ("meshes/book.usmesh", ContentFileKind.WorldMeshBinary, 6880, "b1656dec294d4c5a124450ce3c18c685f94e82b9e9fc8d3447b02576d3e5d047"),
        ("meshes/chest.usmesh", ContentFileKind.WorldMeshBinary, 17488, "2a4ec95011faa1556ba17f8001885227d6830d00719d7ae39eb3a9fceeb7c838"),
        ("meshes/door.usmesh", ContentFileKind.WorldMeshBinary, 42448, "e21211f2ae696613e20b7ed5e54079e9de96244effc5f4ca068e242789735b03"),
        ("meshes/pine.usmesh", ContentFileKind.WorldMeshBinary, 36208, "6944f61f6d5ffdba0bed15b394b91b6cefa3d1fd8ad035ec462d11b5a0bcac0a"),
        ("meshes/rock-a.usmesh", ContentFileKind.WorldMeshBinary, 12496, "c7065abf9ea1bf28dfc858de07472fb3b861debaa6f14eb1162c99412cd6b8a6"),
        ("meshes/rock-b.usmesh", ContentFileKind.WorldMeshBinary, 12496, "4b273d17100aced5b4ac9ca2c0d1e117310a4365664e4ace2ca86d2286b1e151"),
        ("meshes/roof.usmesh", ContentFileKind.WorldMeshBinary, 11872, "6c0e83b40e28f274a162bf7c941fbf297a491e9b43b78e90a202a38aec625ec8"),
        ("meshes/shelf.usmesh", ContentFileKind.WorldMeshBinary, 11248, "a2ecef212baa37d765944dfebc678beb0c1ccd89bdc725f1a47f62f92d372378"),
        ("meshes/wall-3m.usmesh", ContentFileKind.WorldMeshBinary, 44944, "0e6bf4fb34b6858cd944a218d1555147fce7a3758dfbfb5c8def81af18c3c44d"),
        ("meshes/wall-6m.usmesh", ContentFileKind.WorldMeshBinary, 73024, "a589335d283cd5bed33ba773a57d12af5602fc9295ce8def2874a3a4e642557e"),
        ("meshes/wall-8m.usmesh", ContentFileKind.WorldMeshBinary, 44944, "e42e44df2748dd0335401a9746dc278d4d70d8a0ce67baf679ec8cb2830aee90"),
        ("textures/painted-ground.png", ContentFileKind.PngTexture, 2385594, "fa0840ae2cbd10fc3369694c0b02eebef37d015c47ddd35ffcaf0380a6f78ff3"),
        ("textures/painted-pine.png", ContentFileKind.PngTexture, 2306359, "513a32f669cec7b162b104a817af5cf71070028cce948e3e0e394496084c1abe"),
        ("textures/painted-roof.png", ContentFileKind.PngTexture, 2123210, "7257fd7402f8110ebffb2f5de85ed3fa117ee3454540bcd1209a348a1c8df41d"),
        ("textures/painted-stone.png", ContentFileKind.PngTexture, 2762563, "aaee3b344d1369758531fd3514185effc31610dab31aa675071b708aef98999b"),
        ("textures/painted-wood.png", ContentFileKind.PngTexture, 2177044, "48c44be53e9f2037744b12b93887be7b8695e9aaeca60d31608becfa6bf633b0"),
    ];

    public static ContentPackageManifest Manifest { get; } = CreateManifest();
    public static ContentActivationPlan Plan { get; } = CreatePlan();
    public static ContentScopeActivationKey ActivationKey { get; } =
        new(Plan.ScopeActivation, Plan.PlanHash);

    public static LogicalContentId Mesh(string stem) => Id($"cabin-p9:mesh/{stem}");
    public static LogicalContentId Material(string family) => Id($"cabin-p9:material/{family}");

    public static bool TryOpenOrAcquire(
        ContentCache cache, string? sourceRoot, out ContentCatalogLease? catalog)
    {
        ArgumentNullException.ThrowIfNull(cache);
        if (cache.TryOpenCatalog(ContentBaseCatalog.Empty, ActivationKey,
                out catalog, out ContentCacheError openError)) return true;
        if (openError != ContentCacheError.ActivationNotFound ||
            string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
            return false;
        var source = new LocalContentPackageSource(sourceRoot);
        return ContentActivationAcquirer.TryAcquireAndPublish(
            cache, ContentBaseCatalog.Empty, Plan,
            [new ContentPackageAcquisition(Manifest.PackageId, Manifest.ManifestHash,
                [new ContentSourceDescriptor(ContentSourceKind.Local, ArchiveFileName)])],
            source, persistentlyPinned: true, out catalog, out _);
    }

    public static string AssetPath(ContentFilePath packagePath)
    {
        string path = packagePath.Value!;
        string relative = path.StartsWith("meshes/", StringComparison.Ordinal)
            ? path[7..] : path[9..];
        return path.StartsWith("meshes/", StringComparison.Ordinal)
            ? System.IO.Path.Combine(AssetDirectory, relative)
            : System.IO.Path.Combine(AssetDirectory, "Textures", relative);
    }

    private static ContentPackageManifest CreateManifest()
    {
        if (!ContentPackageId.TryCreate("official.world-local.cabin-p9", out var packageId) ||
            !ContentNamespaceId.TryCreate("cabin-p9", out var contentNamespace))
            throw new InvalidOperationException("The P9 package identity is invalid.");
        var descriptors = new ContentFileDescriptor[Files.Length];
        var definitions = new List<ContentDefinitionDescriptor>(Files.Length + 5);
        for (int index = 0; index < Files.Length; index++)
        {
            var file = Files[index];
            if (!ContentFilePath.TryCreate(file.Path, out ContentFilePath path) ||
                !ContentHash.TryParse(file.Hash, out ContentHash hash) ||
                !ContentFileDescriptor.TryCreate(path, file.Kind, file.Bytes, hash, default,
                    out descriptors[index]))
                throw new InvalidOperationException($"The P9 file '{file.Path}' is invalid.");
            string stem = System.IO.Path.GetFileNameWithoutExtension(file.Path);
            LogicalContentId id = file.Kind == ContentFileKind.WorldMeshBinary
                ? Mesh(stem) : Id($"cabin-p9:texture/{stem[8..]}");
            if (!ContentDefinitionDescriptor.TryCreateFileDefinition(id,
                    file.Kind == ContentFileKind.WorldMeshBinary
                        ? ContentDefinitionKind.WorldPresentation : ContentDefinitionKind.Texture,
                    ContentDefinitionOperation.Add, path, out var definition))
                throw new InvalidOperationException($"The P9 definition '{id}' is invalid.");
            definitions.Add(definition!);
        }
        foreach (string family in new[] { "ground", "pine", "roof", "stone", "wood", "glass" })
        {
            if (!ContentMaterialDefinition.TryCreate(ContentMaterialFamily.StandardLit,
                    family == "glass" ? default : Id($"cabin-p9:texture/{family}"),
                    family == "glass" ? 0.14f : 0.86f, 0, 0, out var material) ||
                !ContentDefinitionDescriptor.TryCreateMaterial(Material(family),
                    ContentDefinitionOperation.Add, material, out var definition))
                throw new InvalidOperationException($"The P9 material '{family}' is invalid.");
            definitions.Add(definition!);
        }
        if (!ContentPackageManifest.TryCreate(1, 1, packageId, contentNamespace,
                "UltraSim Cabin painted world presentation", "1.0.0",
                ContentTrustClass.ScopeAuthorizedPassive, descriptors, definitions,
                out ContentPackageManifest? manifest, out string? error))
            throw new InvalidOperationException($"The P9 manifest is invalid: {error}");
        return manifest!;
    }

    private static ContentActivationPlan CreatePlan()
    {
        if (!ContentActivationPlan.TryCreate(ContentScopeKey.Local,
                // An earlier P9 art candidate used this activation identity with different
                // immutable package hashes. Keep that persistent record untouched; the final
                // painted package has its own authorization lineage.
                new ContentActivationId(0x5039_2D43_4142_494EUL, 0x2D41_5254_0000_0002UL),
                ContentBaseCatalog.Empty.Fingerprint,
                [new ContentActivationPackage(Manifest.PackageId, Manifest.ManifestHash,
                    ContentRequirementPolicy.Required)],
                out ContentActivationPlan? plan, out string? error))
            throw new InvalidOperationException($"The P9 activation is invalid: {error}");
        return plan!;
    }

    private static LogicalContentId Id(string value) =>
        LogicalContentId.TryCreate(value, out var id) ? id :
            throw new InvalidOperationException($"Invalid P9 identity: {value}");
}
