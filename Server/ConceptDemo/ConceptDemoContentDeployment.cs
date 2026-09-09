using System.Net;
using UltraSim.Client.Content;
using UltraSim.Game.Application;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Server.Hosting;
using UltraSim.UI;

namespace UltraSim.Server.ConceptDemo;

public sealed class ConceptDemoContentDeployment : IDisposable
{
    private const string ArchiveRoute = "/content/concept-demo.uspkg";
    private readonly StaticContentHttpHost _host;

    private ConceptDemoContentDeployment(
        StaticContentHttpHost host,
        RemoteContentAuthorization authorization,
        ContentDeploymentSnapshot snapshot)
    {
        _host = host;
        Authorization = authorization;
        Snapshot = snapshot;
    }

    public RemoteContentAuthorization Authorization { get; }
    public ContentDeploymentSnapshot Snapshot { get; }

    public static ConceptDemoContentDeployment Create(
        ApplicationRealmId realm,
        IPEndPoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
        if (!WorldPackFingerprint.TryParse(
                GameWorldContentProfile.ConceptDemoPackFingerprint, out WorldPackFingerprint expected) ||
            pack.Fingerprint != expected)
            throw new InvalidOperationException(
                "The compiled Concept Demo WorldPack no longer matches its compatible-client profile.");

        var worldBytes = new byte[pack.Length];
        pack.CopyTo(worldBytes);
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
                [worldFile, uiFile, monitorUiFile], [worldDefinition!],
                out ContentPackageManifest? manifest, out manifestError))
            throw new InvalidOperationException(
                $"The Concept Demo content manifest is invalid: {manifestError}");

        ContentFilePayload[] payloads =
        [
            new ContentFilePayload(worldPath, worldBytes),
            new ContentFilePayload(uiPath, uiBytes),
            new ContentFilePayload(monitorUiPath, monitorUiBytes),
        ];
        string? planError = null;
        ContentCatalogResolutionError catalogError = ContentCatalogResolutionError.None;
        if (!ContentPackageValidator.TryValidatePayloads(
                manifest!, payloads, out VerifiedContentPackage? verified, out _) ||
            !ContentActivationPlan.TryCreate(
                realm,
                new ContentActivationId(0x4130_2E38_2D4E_4154UL, 1),
                ContentBaseCatalog.Empty.Fingerprint,
                [new ContentActivationPackage(
                    packageId, manifest!.ManifestHash, ContentRequirementPolicy.Required)],
                out ContentActivationPlan? plan, out planError) ||
            !ContentCatalogResolver.TryResolve(
                ContentBaseCatalog.Empty, plan!, [verified!],
                out ResolvedContentCatalog? catalog, out catalogError) ||
            !ContentPackageArchive.TryEncodeCanonical(manifest!, payloads, out byte[] archive))
            throw new InvalidOperationException(
                $"The Concept Demo content deployment is invalid: plan={planError}, catalog={catalogError}.");

        var host = new StaticContentHttpHost(
            endpoint,
            [new StaticContentHttpResource(ArchiveRoute, archive)]);
        try
        {
            var acquisition = new ContentPackageAcquisition(
                packageId,
                manifest!.ManifestHash,
                [new ContentSourceDescriptor(
                    ContentSourceKind.ServerHostedStatic,
                    host.GetUri(ArchiveRoute).ToString())]);
            RemoteContentAuthorization authorization = ProjectAuthorization(
                plan!, catalog!.Fingerprint, [acquisition]);
            return new ConceptDemoContentDeployment(
                host,
                authorization,
                new ContentDeploymentSnapshot(
                    host.GetUri(ArchiveRoute), archive.Length, worldBytes.Length,
                    checked(uiBytes.Length + monitorUiBytes.Length),
                    manifest!.ManifestHash, plan!.PlanHash, catalog!.Fingerprint,
                    pack.Fingerprint, pack.ContentFingerprint, pack.ChunkCount));
        }
        catch
        {
            host.Dispose();
            throw;
        }
    }

    public void Dispose() => _host.Dispose();

    private static RemoteContentAuthorization ProjectAuthorization(
        ContentActivationPlan plan,
        ContentHash expectedCatalog,
        IReadOnlyList<ContentPackageAcquisition> acquisitions)
    {
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

public readonly record struct ContentDeploymentSnapshot(
    Uri ArchiveUri,
    int ArchiveBytes,
    int WorldPackBytes,
    int UiBytes,
    ContentHash ManifestHash,
    ContentHash PlanHash,
    ContentHash CatalogFingerprint,
    WorldPackFingerprint WorldPackFingerprint,
    WorldContentFingerprint WorldContentFingerprint,
    int WorldChunks);
