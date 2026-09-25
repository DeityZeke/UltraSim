using System.Net;
using UltraSim.Client.Content;
using UltraSim.Game.Application;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Server.Hosting;
using UltraSim.UI;

namespace UltraSim.Server.ConceptDemo;

public sealed class ConceptDemoContentDeployment : IDisposable
{
    private const string ArchiveRoute = "/content/concept-demo.uspkg";
    private const string WestPackArchiveRoute = "/content/concept-demo-pack-west.uspkg";
    private const string EastPackArchiveRoute = "/content/concept-demo-pack-east.uspkg";
    private const string ActorArchiveRoute = "/content/concept-demo-actors.uspkg";
    private const string ActorReplaceArchiveRoute = "/content/concept-demo-actor-replace.uspkg";
    private readonly StaticContentHttpHost _host;

    private ConceptDemoContentDeployment(
        StaticContentHttpHost host,
        RemoteContentAuthorization authorization,
        RemoteContentAuthorization presentationAuthorization,
        ConceptDemoStructuralContent structural,
        ContentDeploymentSnapshot snapshot)
    {
        _host = host;
        Authorization = authorization;
        PresentationAuthorization = presentationAuthorization;
        Structural = structural;
        Snapshot = snapshot;
    }

    public RemoteContentAuthorization Authorization { get; }
    public RemoteContentAuthorization PresentationAuthorization { get; }
    public ConceptDemoStructuralContent Structural { get; }
    public RemoteStructuralWorldAuthorization StructuralAuthorization =>
        Structural.CreateAuthorization();
    public ContentDeploymentSnapshot Snapshot { get; }

    /// <summary>
    /// Returns cold HTTP response-body accounting. These bytes exclude HTTP/TCP/IP
    /// framing and are deliberately separate from gameplay UDP accounting.
    /// </summary>
    public StaticContentHttpHostSnapshot GetHttpSnapshot() => _host.GetSnapshot();

    public static ConceptDemoContentDeployment Create(
        ApplicationRealmId realm,
        IPEndPoint endpoint,
        string? officialContentRoot = null)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
        ConceptDemoStructuralSource structuralSource = GamePlayableWorld.CreateConceptDemoStructuralSource();
        if (!WorldPackFingerprint.TryParse(
                GameWorldContentProfile.ConceptDemoPackFingerprint, out WorldPackFingerprint expected) ||
            pack.Fingerprint != expected)
            throw new InvalidOperationException(
                "The compiled Concept Demo WorldPack no longer matches its compatible-client profile.");

        var worldBytes = new byte[pack.Length];
        pack.CopyTo(worldBytes);
        var structuralBytes = new byte[structuralSource.Manifest.Length];
        structuralSource.Manifest.CopyTo(structuralBytes);
        if (!ContentFilePath.TryCreate(
                "world/concept-demo.usworldpack", out ContentFilePath worldPath) ||
            !ContentFileDescriptor.TryCreate(
                worldPath, ContentFileKind.WorldPack, worldBytes.Length,
                ContentHash.Compute(worldBytes), default, out ContentFileDescriptor worldFile) ||
            !LogicalContentId.TryCreate(
                GameWorldContentProfile.ConceptDemoDefinitionId, out LogicalContentId worldId) ||
            !ContentDefinitionDescriptor.TryCreateFileDefinition(
                worldId, ContentDefinitionKind.WorldPack, ContentDefinitionOperation.Add,
                worldPath, out ContentDefinitionDescriptor? worldDefinition))
            throw new InvalidOperationException("The Concept Demo World content descriptor is invalid.");
        if (!ContentFilePath.TryCreate(
                "world/concept-demo.usworldmanifest", out ContentFilePath structuralPath) ||
            !ContentFileDescriptor.TryCreate(
                structuralPath, ContentFileKind.WorldStructuralManifest, structuralBytes.Length,
                ContentHash.Compute(structuralBytes), default,
                out ContentFileDescriptor structuralFile) ||
            !LogicalContentId.TryCreate(
                ConceptDemoStructuralContent.ManifestDefinitionValue,
                out LogicalContentId structuralId) ||
            !ContentDefinitionDescriptor.TryCreateFileDefinition(
                structuralId, ContentDefinitionKind.WorldStructuralManifest,
                ContentDefinitionOperation.Add, structuralPath,
                out ContentDefinitionDescriptor? structuralDefinition))
            throw new InvalidOperationException("The Concept Demo structural manifest descriptor is invalid.");

        CompiledUiDefinition inventory = InventoryUi.Definition;
        if (!UiDefinitionArtifactCodec.TryEncodeCanonical(inventory, out byte[] uiBytes) ||
            !ContentUiDefinitionPath.TryCreate(
                UiDefinitionIdentity.From(inventory), out ContentFilePath uiPath) ||
            !ContentFileDescriptor.TryCreate(
                uiPath, ContentFileKind.UiDefinition, uiBytes.Length,
                ContentHash.Compute(uiBytes), default, out ContentFileDescriptor uiFile))
            throw new InvalidOperationException("The standard Inventory UI artifact is invalid.");

        CompiledUiDefinition monitor = ServerMonitorUi.Definition;
        if (!UiDefinitionArtifactCodec.TryEncodeCanonical(monitor, out byte[] monitorUiBytes) ||
            !ContentUiDefinitionPath.TryCreate(
                UiDefinitionIdentity.From(monitor), out ContentFilePath monitorUiPath) ||
            !ContentFileDescriptor.TryCreate(
                monitorUiPath, ContentFileKind.UiDefinition, monitorUiBytes.Length,
                ContentHash.Compute(monitorUiBytes), default, out ContentFileDescriptor monitorUiFile))
            throw new InvalidOperationException("The Server Monitor UI artifact is invalid.");

        string? manifestError = null;
        if (!ContentPackageId.TryCreate(
                "server.concept-demo.bootstrap", out ContentPackageId packageId) ||
            !ContentNamespaceId.TryCreate("server-world", out ContentNamespaceId contentNamespace) ||
            !ContentPackageManifest.TryCreate(
                1, 1, packageId, contentNamespace,
                "Concept Demo world and standard UI", "1.0.0",
                ContentTrustClass.ServerAuthorizedPassive,
                [worldFile, structuralFile, uiFile, monitorUiFile],
                [worldDefinition!, structuralDefinition!],
                out ContentPackageManifest? manifest, out manifestError))
            throw new InvalidOperationException(
                $"The Concept Demo content manifest is invalid: {manifestError}");

        ContentFilePayload[] payloads =
        [
            new ContentFilePayload(worldPath, worldBytes),
            new ContentFilePayload(structuralPath, structuralBytes),
            new ContentFilePayload(uiPath, uiBytes),
            new ContentFilePayload(monitorUiPath, monitorUiBytes),
        ];
        VerifiedContentPackage bootstrap = ValidatePackage(manifest!, payloads);
        ContentPackageFixture westPack = CreateStructuralPackPackage(
            "server.concept-demo.pack-west", "world/concept-demo-west.usworldpack",
            "server-world:world/pack-west", structuralSource.West);
        ContentPackageFixture eastPack = CreateStructuralPackPackage(
            "server.concept-demo.pack-east", "world/concept-demo-east.usworldpack",
            "server-world:world/pack-east", structuralSource.East);
        (byte[] actorX, byte[] actorY) =
            ConceptDemoActorContent.LoadOfficialActors(officialContentRoot);
        byte[] actorXReplacement = ConceptDemoActorContent.CreateXReplacement(actorX);
        ContentPackageFixture actors = CreateActorPackage(
            "server.concept-demo.actors", "actors",
            [
                new ActorPresentationPayload(
                    ConceptDemoActorPresentationProfile.HumanXDefinitionId,
                    ContentDefinitionOperation.Add,
                    "actors/human-x.glb", actorX),
                new ActorPresentationPayload(
                    ConceptDemoActorPresentationProfile.HumanYDefinitionId,
                    ContentDefinitionOperation.Add,
                    "actors/human-y.glb", actorY),
            ]);
        ContentPackageFixture actorReplacement = CreateActorPackage(
            "server.concept-demo.actor-replace-x", "actor-replace",
            [new ActorPresentationPayload(
                ConceptDemoActorPresentationProfile.HumanXDefinitionId,
                ContentDefinitionOperation.Replace,
                "actors/replacement-human-x.glb", actorXReplacement)]);
        string? planError = null;
        ContentCatalogResolutionError catalogError = ContentCatalogResolutionError.None;
        if (!ContentActivationPlan.TryCreate(
                realm,
                new ContentActivationId(0x4130_2E38_2D4E_4154UL, 1),
                ContentBaseCatalog.Empty.Fingerprint,
                [Required(manifest!)],
                out ContentActivationPlan? plan, out planError) ||
            !ContentCatalogResolver.TryResolve(
                ContentBaseCatalog.Empty, plan!,
                [bootstrap],
                out ResolvedContentCatalog? catalog, out catalogError) ||
            !ContentActivationPlan.TryCreate(
                realm,
                new ContentActivationId(0x4631_2E32_2D50_5245UL, 1),
                ContentBaseCatalog.Empty.Fingerprint,
                [Required(actors.Manifest), Required(actorReplacement.Manifest)],
                out ContentActivationPlan? presentationPlan, out planError) ||
            !ContentCatalogResolver.TryResolve(
                ContentBaseCatalog.Empty, presentationPlan!,
                [actors.Verified, actorReplacement.Verified],
                out ResolvedContentCatalog? presentationCatalog, out catalogError) ||
            !ContentPackageArchive.TryEncodeCanonical(manifest!, payloads, out byte[] archive) ||
            !ContentPackageArchive.TryEncodeCanonical(
                westPack.Manifest, westPack.Payloads, out byte[] westPackArchive) ||
            !ContentPackageArchive.TryEncodeCanonical(
                eastPack.Manifest, eastPack.Payloads, out byte[] eastPackArchive) ||
            !ContentPackageArchive.TryEncodeCanonical(
                actors.Manifest, actors.Payloads, out byte[] actorArchive) ||
            !ContentPackageArchive.TryEncodeCanonical(
                actorReplacement.Manifest, actorReplacement.Payloads, out byte[] actorReplaceArchive))
            throw new InvalidOperationException(
                $"The Concept Demo content deployment is invalid: plan={planError}, catalog={catalogError}.");

        ResolvedContentCatalog resolvedCatalog = presentationCatalog!;
        RequireActorCatalogOwner(
            resolvedCatalog, ConceptDemoActorPresentationProfile.HumanXDefinitionId,
            actorReplacement.Manifest.PackageId);
        RequireActorCatalogOwner(
            resolvedCatalog, ConceptDemoActorPresentationProfile.HumanYDefinitionId,
            actors.Manifest.PackageId);

        var host = new StaticContentHttpHost(
            endpoint,
            [
                new StaticContentHttpResource(ArchiveRoute, archive),
                new StaticContentHttpResource(WestPackArchiveRoute, westPackArchive),
                new StaticContentHttpResource(EastPackArchiveRoute, eastPackArchive),
                new StaticContentHttpResource(ActorArchiveRoute, actorArchive),
                new StaticContentHttpResource(ActorReplaceArchiveRoute, actorReplaceArchive),
            ]);
        try
        {
            ContentPackageAcquisition[] acquisitions =
            [
                Acquisition(manifest!, host.GetUri(ArchiveRoute)),
                Acquisition(actors.Manifest, host.GetUri(ActorArchiveRoute)),
                Acquisition(actorReplacement.Manifest, host.GetUri(ActorReplaceArchiveRoute)),
            ];
            RemoteContentAuthorization authorization = ProjectAuthorization(
                plan!, catalog!.Fingerprint, [acquisitions[0]]);
            RemoteContentAuthorization presentationAuthorization = ProjectAuthorization(
                presentationPlan!, resolvedCatalog.Fingerprint,
                [acquisitions[1], acquisitions[2]]);
            var structural = new ConceptDemoStructuralContent(
                structuralSource.Manifest,
                Acquisition(westPack.Manifest, host.GetUri(WestPackArchiveRoute)),
                Acquisition(eastPack.Manifest, host.GetUri(EastPackArchiveRoute)));
            return new ConceptDemoContentDeployment(
                host,
                authorization,
                presentationAuthorization,
                structural,
                new ContentDeploymentSnapshot(
                    host.GetUri(ArchiveRoute),
                    checked(archive.Length + actorArchive.Length + actorReplaceArchive.Length),
                    worldBytes.Length,
                    checked(uiBytes.Length + monitorUiBytes.Length),
                    checked(actorX.Length + actorY.Length + actorXReplacement.Length),
                    manifest!.ManifestHash, plan!.PlanHash, catalog!.Fingerprint,
                    pack.Fingerprint, pack.ContentFingerprint, pack.ChunkCount)
                {
                    PresentationPlanHash = presentationPlan!.PlanHash,
                    PresentationCatalogFingerprint = resolvedCatalog.Fingerprint,
                    StructuralManifestFingerprint = structuralSource.Manifest.Fingerprint,
                    StructuralManifestBytes = structuralBytes.Length,
                    WestPackRawBytes = structuralSource.West.Length,
                    EastPackRawBytes = structuralSource.East.Length,
                    WestPackArchiveBytes = westPackArchive.Length,
                    EastPackArchiveBytes = eastPackArchive.Length,
                });
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public void Dispose() => _host.Dispose();

    private static ContentPackageFixture CreateStructuralPackPackage(
        string packageValue, string pathValue, string definitionValue, WorldPack pack)
    {
        var bytes = new byte[pack.Length];
        pack.CopyTo(bytes);
        string? error = null;
        if (!ContentFilePath.TryCreate(pathValue, out ContentFilePath path) ||
            !ContentFileDescriptor.TryCreate(path, ContentFileKind.WorldPack,
                bytes.Length, ContentHash.Compute(bytes), default, out ContentFileDescriptor file) ||
            !LogicalContentId.TryCreate(definitionValue, out LogicalContentId logicalId) ||
            !ContentDefinitionDescriptor.TryCreateFileDefinition(
                logicalId, ContentDefinitionKind.WorldPack, ContentDefinitionOperation.Add,
                path, out ContentDefinitionDescriptor? definition) ||
            !ContentPackageId.TryCreate(packageValue, out ContentPackageId packageId) ||
            !ContentNamespaceId.TryCreate("server-world", out ContentNamespaceId contentNamespace) ||
            !ContentPackageManifest.TryCreate(
                1, 1, packageId, contentNamespace, packageValue, "1.0.0",
                ContentTrustClass.ServerAuthorizedPassive,
                [file], [definition!], out ContentPackageManifest? manifest, out error))
            throw new InvalidOperationException(
                $"The Concept Demo structural Pack package '{packageValue}' is invalid: {error}");
        ContentFilePayload[] payloads = [new(path, bytes)];
        return new ContentPackageFixture(manifest!, payloads,
            ValidatePackage(manifest!, payloads));
    }

    private static ContentPackageFixture CreateActorPackage(
        string packageValue,
        string namespaceValue,
        IReadOnlyList<ActorPresentationPayload> actors)
    {
        var files = new ContentFileDescriptor[actors.Count];
        var definitions = new ContentDefinitionDescriptor[actors.Count];
        var payloads = new ContentFilePayload[actors.Count];
        for (int index = 0; index < actors.Count; index++)
        {
            ActorPresentationPayload actor = actors[index];
            if (!ContentFilePath.TryCreate(actor.FilePath, out ContentFilePath path) ||
                !ContentFileDescriptor.TryCreate(
                    path, ContentFileKind.ActorGltfBinary, actor.Bytes.Length,
                    ContentHash.Compute(actor.Bytes), default, out files[index]) ||
                !LogicalContentId.TryCreate(actor.LogicalId, out LogicalContentId logicalId) ||
                !ContentDefinitionDescriptor.TryCreateFileDefinition(
                    logicalId, ContentDefinitionKind.ActorPresentation, actor.Operation,
                    path, out ContentDefinitionDescriptor? definition))
                throw new InvalidOperationException(
                    $"The Concept Demo actor descriptor '{actor.LogicalId}' is invalid.");
            definitions[index] = definition!;
            payloads[index] = new ContentFilePayload(path, actor.Bytes);
        }
        if (!ContentPackageId.TryCreate(packageValue, out ContentPackageId packageId) ||
            !ContentNamespaceId.TryCreate(namespaceValue, out ContentNamespaceId contentNamespace))
            throw new InvalidOperationException(
                $"The Concept Demo actor package identity '{packageValue}' is invalid.");
        if (!ContentPackageManifest.TryCreate(
                1, 1, packageId, contentNamespace,
                packageValue, "1.0.0", ContentTrustClass.ServerAuthorizedPassive,
                files, definitions, out ContentPackageManifest? manifest, out string? error))
            throw new InvalidOperationException(
                $"The Concept Demo actor manifest '{packageValue}' is invalid: {error}");
        return new ContentPackageFixture(
            manifest!, payloads, ValidatePackage(manifest!, payloads));
    }

    private static VerifiedContentPackage ValidatePackage(
        ContentPackageManifest manifest,
        IReadOnlyList<ContentFilePayload> payloads)
    {
        if (!ContentPackageValidator.TryValidatePayloads(
                manifest, payloads, out VerifiedContentPackage? verified, out string? error))
            throw new InvalidOperationException(
                $"The Concept Demo package '{manifest.PackageId}' is invalid: {error}");
        return verified!;
    }

    private static ContentActivationPackage Required(ContentPackageManifest manifest) =>
        new(manifest.PackageId, manifest.ManifestHash, ContentRequirementPolicy.Required);

    private static ContentPackageAcquisition Acquisition(
        ContentPackageManifest manifest,
        Uri uri) =>
        new(
            manifest.PackageId,
            manifest.ManifestHash,
            [new ContentSourceDescriptor(
                ContentSourceKind.ServerHostedStatic, uri.ToString())]);

    private static void RequireActorCatalogOwner(
        ResolvedContentCatalog catalog,
        string logicalValue,
        ContentPackageId expectedOwner)
    {
        if (!LogicalContentId.TryCreate(logicalValue, out LogicalContentId logicalId) ||
            !catalog.TryGet(logicalId, out ResolvedContentEntry? entry) ||
            entry!.Kind != ContentDefinitionKind.ActorPresentation ||
            entry.OwnerPackageId != expectedOwner)
            throw new InvalidOperationException(
                $"The resolved actor catalog owner for '{logicalValue}' is invalid.");
    }

    private static RemoteContentAuthorization ProjectAuthorization(
        ContentActivationPlan plan,
        ContentHash expectedCatalog,
        IReadOnlyList<ContentPackageAcquisition> acquisitions)
    {
        if (!plan.Scope.IsRealm)
            throw new ArgumentException("Remote authorization requires an authenticated Realm content scope.",
                nameof(plan));
        var packages = new RemoteContentPackageRequirement[plan.Packages.Count];
        for (int index = 0; index < packages.Length; index++)
        {
            ContentActivationPackage required = plan.Packages[index];
            ContentPackageAcquisition acquisition = acquisitions.Single(value =>
                value.Package == required.PackageId && value.ManifestHash == required.ManifestHash);
            packages[index] = new RemoteContentPackageRequirement(
                required.PackageId.Value!,
                ToRemote(required.ManifestHash),
                (RemoteContentRequirementPolicy)required.Requirement,
                acquisition.Sources.Select(static source => new RemoteContentSource(
                    (RemoteContentSourceKind)source.Kind, source.Locator)));
        }
        return new RemoteContentAuthorization(
            plan.Realm.High, plan.Realm.Low,
            plan.Activation.High, plan.Activation.Low,
            ToRemote(plan.RequiredBaseCatalogFingerprint),
            ToRemote(plan.PlanHash),
            ToRemote(expectedCatalog),
            packages);
    }

    private static RemoteContentHash ToRemote(ContentHash hash) =>
        new(hash.Part0, hash.Part1, hash.Part2, hash.Part3);
}

internal readonly record struct ActorPresentationPayload(
    string LogicalId,
    ContentDefinitionOperation Operation,
    string FilePath,
    byte[] Bytes);

internal sealed record ContentPackageFixture(
    ContentPackageManifest Manifest,
    ContentFilePayload[] Payloads,
    VerifiedContentPackage Verified);

public readonly record struct ContentDeploymentSnapshot(
    Uri ArchiveUri,
    int ArchiveBytes,
    int WorldPackBytes,
    int UiBytes,
    int ActorPresentationBytes,
    ContentHash ManifestHash,
    ContentHash PlanHash,
    ContentHash CatalogFingerprint,
    WorldPackFingerprint WorldPackFingerprint,
    WorldContentFingerprint WorldContentFingerprint,
    int WorldChunks)
{
    public ContentHash PresentationPlanHash { get; init; }
    public ContentHash PresentationCatalogFingerprint { get; init; }
    public WorldStructuralManifestFingerprint StructuralManifestFingerprint { get; init; }
    public int StructuralManifestBytes { get; init; }
    public int WestPackRawBytes { get; init; }
    public int EastPackRawBytes { get; init; }
    public int WestPackArchiveBytes { get; init; }
    public int EastPackArchiveBytes { get; init; }
}

/// <summary>
/// Independently acquirable authored Pack packages. Neither package is part of the required
/// application activation; an authenticated structural grant chooses one exact package later.
/// </summary>
public sealed class ConceptDemoStructuralContent
{
    public const string ManifestDefinitionValue = "server-world:world/structural-manifest";

    public ConceptDemoStructuralContent(
        WorldStructuralManifest manifest,
        ContentPackageAcquisition west,
        ContentPackageAcquisition east)
    {
        Manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
        West = west ?? throw new ArgumentNullException(nameof(west));
        East = east ?? throw new ArgumentNullException(nameof(east));
        if (manifest.EntryCount != 2 ||
            !manifest.TryGetPack(ConceptDemoStructuralSource.WestCoord, out _) ||
            !manifest.TryGetPack(ConceptDemoStructuralSource.EastCoord, out _))
            throw new ArgumentException("The Concept Demo structural package table is incomplete.",
                nameof(manifest));
    }

    public WorldStructuralManifest Manifest { get; }
    public ContentPackageAcquisition West { get; }
    public ContentPackageAcquisition East { get; }

    public bool TryGetPackage(WorldPackCoord coord, out ContentPackageAcquisition? package)
    {
        package = coord == ConceptDemoStructuralSource.WestCoord ? West :
            coord == ConceptDemoStructuralSource.EastCoord ? East : null;
        return package is not null;
    }

    public RemoteStructuralWorldAuthorization CreateAuthorization() => new(
        Manifest, ManifestDefinitionValue, GameWorldState.DefaultSpace,
        new WorldCompositionRevision(1), ConceptDemoStructuralSource.WestCoord);
}
