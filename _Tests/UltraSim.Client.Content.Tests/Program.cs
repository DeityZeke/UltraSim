using System.Buffers.Binary;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using UltraSim.Client.Content;
using UltraSim.Game.Application;
using UltraSim.Game.World;
using UltraSim.Spatial;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("IdentityGrammarAndDefaultsFailClosed", IdentityGrammarAndDefaultsFailClosed),
    ("TypedContentScopeIdentityCannotConfuseLocalAndRealm", TypedContentScopeIdentityCannotConfuseLocalAndRealm),
    ("EmptyBaseCatalogIsCanonicalPrimitiveOnlyBaseline", EmptyBaseCatalogIsCanonicalPrimitiveOnlyBaseline),
    ("ManifestIdentityIsCanonicalAndBounded", ManifestIdentityIsCanonicalAndBounded),
    ("BoundedAdmissionsStopBeforeOversizedMaterialization", BoundedAdmissionsStopBeforeOversizedMaterialization),
    ("PassivePayloadsRequireExactBytesAndAllowedEnvelopes", PassivePayloadsRequireExactBytesAndAllowedEnvelopes),
    ("CanonicalPackageArchiveRoundTripsAndRejectsDamage", CanonicalPackageArchiveRoundTripsAndRejectsDamage),
    ("LocalPackageSourceIsRootedBoundedAndExact", LocalPackageSourceIsRootedBoundedAndExact),
    ("OfficialLocalPresentationIsExactPersistentAndScopeIsolated", OfficialLocalPresentationIsExactPersistentAndScopeIsolated),
    ("OfficialCabinWorldMeshesArePassiveRemovableAndScopeIsolated", OfficialCabinWorldMeshesArePassiveRemovableAndScopeIsolated),
    ("FinalCabinArtCoexistsWithOlderWarmActivation", FinalCabinArtCoexistsWithOlderWarmActivation),
    ("ActivationIdentityPreservesAuthorityOrderNotByteSource", ActivationIdentityPreservesAuthorityOrderNotByteSource),
    ("DefaultReplaceAddResolveDeterministically", DefaultReplaceAddResolveDeterministically),
    ("ResolutionFailuresAreTransactionalAndDiagnosable", ResolutionFailuresAreTransactionalAndDiagnosable),
    ("OptionalPackageFallbackAndApplicationProofAreExact", OptionalPackageFallbackAndApplicationProofAreExact),
    ("PersistentCacheInstallsPublishesAndReopensWithoutSource", PersistentCacheInstallsPublishesAndReopensWithoutSource),
    ("GlobalBlobDedupDoesNotGrantCrossRealmAuthorization", GlobalBlobDedupDoesNotGrantCrossRealmAuthorization),
    ("LocalAndRealmsShareCasButNotAuthorization", LocalAndRealmsShareCasButNotAuthorization),
    ("ScopeCatalogsWarmReopenAndRetireWithoutDeletingSharedBytes", ScopeCatalogsWarmReopenAndRetireWithoutDeletingSharedBytes),
    ("FailedInstallAndInterruptedStagingNeverPublish", FailedInstallAndInterruptedStagingNeverPublish),
    ("CorruptBlobCannotReopenOrPublish", CorruptBlobCannotReopenOrPublish),
    ("CorruptCanonicalMetadataFailsClosedAtOpen", CorruptCanonicalMetadataFailsClosedAtOpen),
    ("PinnedReachabilityControlsQuotaEviction", PinnedReachabilityControlsQuotaEviction),
    ("OptionalBytesDoNotRetargetPersistedCatalogImplicitly", OptionalBytesDoNotRetargetPersistedCatalogImplicitly),
    ("CacheRejectsReparsePointStorage", CacheRejectsReparsePointStorage),
    ("ManifestEnvelopeIsCanonicalAndExact", ManifestEnvelopeIsCanonicalAndExact),
    ("MirrorsAndHashDeltaConvergeWithBoundedParallelism", MirrorsAndHashDeltaConvergeWithBoundedParallelism),
    ("ExactPackageAcquisitionReusesCasWithoutCatalogActivation", ExactPackageAcquisitionReusesCasWithoutCatalogActivation),
    ("CorruptAndInterruptedAcquisitionRepairTransactionally", CorruptAndInterruptedAcquisitionRepairTransactionally),
    ("OptionalAcquisitionFallsBackThenHotUpgradesExplicitly", OptionalAcquisitionFallsBackThenHotUpgradesExplicitly),
    ("ResolvedPrivateDnsTargetIsRejectedAtConnectTime", ResolvedPrivateDnsTargetIsRejectedAtConnectTime),
    ("UiDefinitionArtifactResolvesOnlyThroughAppliedRealmCatalog", UiDefinitionArtifactResolvesOnlyThroughAppliedRealmCatalog),
    ("WorldPackResolvesThroughAppliedCatalogAndWorldBinding", WorldPackResolvesThroughAppliedCatalogAndWorldBinding),
    ("StructuralManifestRequiresCatalogAuthorizationAndWorldFingerprint", StructuralManifestRequiresCatalogAuthorizationAndWorldFingerprint),
    ("ActorPresentationGlbIsBoundedPassiveAndCatalogResolved", ActorPresentationGlbIsBoundedPassiveAndCatalogResolved),
    ("PresentationGenerationSelectsRealmLocalPrimitiveAndRetainsOldLeases", PresentationGenerationSelectsRealmLocalPrimitiveAndRetainsOldLeases),
    ("UserResourcePackPolicyIsStrictAndSeparate", UserResourcePackPolicyIsStrictAndSeparate),
    ("UserResourcePackStorePersistsOrderSharesCasAndFailsClosed", UserResourcePackStorePersistsOrderSharesCasAndFailsClosed),
    ("UserPresentationOverlayIsTransactionalOrderedAndLeaseSafe", UserPresentationOverlayIsTransactionalOrderedAndLeaseSafe),
    ("TextureMaterialPresentationUsesEffectiveUserLeafAndStableIdentity", TextureMaterialPresentationUsesEffectiveUserLeafAndStableIdentity),
    ("WorkerPreparedPresentationGenerationBindsExplicitPublicationOwner", WorkerPreparedPresentationGenerationBindsExplicitPublicationOwner),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} Client Content tests passed.");

static void WorkerPreparedPresentationGenerationBindsExplicitPublicationOwner()
{
    int owner = Environment.CurrentManagedThreadId;
    PresentationGeneration generation = Task.Run(() =>
        PresentationGeneration.PrepareWithResourcesForOwner(
            null, null, null, null, null, owner)).GetAwaiter().GetResult();
    True(generation.Generation.IsValid);
    bool workerRejected = Task.Run(() =>
    {
        try { generation.Dispose(); return false; }
        catch (InvalidOperationException) { return true; }
    }).GetAwaiter().GetResult();
    True(workerRejected, "Worker may prepare but not publish or retire the generation.");
    generation.Dispose();
}

static void TypedContentScopeIdentityCannotConfuseLocalAndRealm()
{
    False(default(ContentScopeKey).IsValid);
    Equal(ContentScopeKind.Invalid, default(ContentScopeKey).Kind);
    ContentScopeKey local = ContentScopeKey.Local;
    True(local.IsValid);
    True(local.IsLocal);
    False(local.IsRealm);
    Equal("world.local", local.ToString());
    False(local.TryGetRealm(out _));

    ApplicationRealmId realmA = new(1, 2);
    ApplicationRealmId realmB = new(1, 3);
    ContentScopeKey first = ContentScopeKey.ForRealm(realmA);
    ContentScopeKey second = ContentScopeKey.ForRealm(realmB);
    True(first.IsRealm);
    True(first.TryGetRealm(out ApplicationRealmId projected));
    Equal(realmA, projected);
    NotEqual(local, first);
    NotEqual(first, second);
    bool rejected = false;
    try { _ = ContentScopeKey.ForRealm(default); }
    catch (ArgumentException) { rejected = true; }
    True(rejected, "A default or textual pseudo-realm must not manufacture Local.");
}

static void EmptyBaseCatalogIsCanonicalPrimitiveOnlyBaseline()
{
    ContentBaseCatalog empty = ContentBaseCatalog.Empty;
    Equal(0, empty.Entries.Count);
    Equal(ContentBaseCatalog.Empty.Fingerprint, empty.Fingerprint);
    True(empty.Fingerprint.IsValid);
    False(ContentBaseCatalog.TryCreate([], out _, out ContentCatalogResolutionError error));
    Equal(ContentCatalogResolutionError.InvalidBaseCatalog, error);
}

static void CanonicalPackageArchiveRoundTripsAndRejectsDamage()
{
    PackageFixture fixture = CreateReplacePackage();
    True(ContentPackageArchive.TryEncodeCanonical(
        fixture.Manifest, fixture.Payloads.Reverse(), out byte[] first));
    True(ContentPackageArchive.TryEncodeCanonical(
        fixture.Manifest, fixture.Payloads, out byte[] second));
    True(first.AsSpan().SequenceEqual(second));
    True(ContentPackageArchive.TryDecodeCanonical(
        first, fixture.Manifest.PackageId, fixture.Manifest.ManifestHash,
        out FetchedContentPackage? decoded));
    Equal(fixture.Manifest.ManifestHash, decoded!.Manifest.ManifestHash);
    Equal(fixture.Payloads.Length, decoded.Payloads.Count);
    for (int index = 0; index < fixture.Payloads.Length; index++)
    {
        Equal(fixture.Payloads[index].Path, decoded.Payloads[index].Path);
        True(fixture.Payloads[index].Bytes.Span.SequenceEqual(decoded.Payloads[index].Bytes.Span));
    }

    byte[] damaged = first.ToArray();
    damaged[^1] ^= 0x5A;
    False(ContentPackageArchive.TryDecodeCanonical(
        damaged, fixture.Manifest.PackageId, fixture.Manifest.ManifestHash, out _));
    False(ContentPackageArchive.TryDecodeCanonical(
        first.AsMemory(0, first.Length - 1), fixture.Manifest.PackageId,
        fixture.Manifest.ManifestHash, out _));
    False(ContentPackageArchive.TryDecodeCanonical(
        first, Package("wrong.package"), fixture.Manifest.ManifestHash, out _));
    False(ContentPackageArchive.TryDecodeCanonical(
        first, fixture.Manifest.PackageId, ContentHash.Compute("wrong"u8), out _));
}

static void LocalPackageSourceIsRootedBoundedAndExact()
{
    PackageFixture fixture = CreateReplacePackage();
    True(ContentPackageArchive.TryEncodeCanonical(
        fixture.Manifest, fixture.Payloads, out byte[] archive));
    string root = Path.Combine(Path.GetTempPath(),
        $"ultrasim-local-source-{Environment.ProcessId}-{Guid.NewGuid():N}");
    Directory.CreateDirectory(Path.Combine(root, "packages"));
    try
    {
        System.IO.File.WriteAllBytes(Path.Combine(root, "packages", "replace.uspkg"), archive);
        var descriptor = new ContentSourceDescriptor(
            ContentSourceKind.Local, "packages/replace.uspkg");
        var request = new ContentPackageFetchRequest(
            ContentScopeKey.Local, fixture.Manifest.PackageId,
            fixture.Manifest.ManifestHash, descriptor);
        var source = new LocalContentPackageSource(root);
        True(source.TryFetch(in request, out FetchedContentPackage? fetched,
            out ContentSourceFetchError error), error.ToString());
        Equal(fixture.Manifest.ManifestHash, fetched!.Manifest.ManifestHash);
        Equal(1UL, source.GetSnapshot().Completed);
        Equal((long)archive.Length, source.GetSnapshot().ArchiveBytesRead);

        var tiny = new LocalContentPackageSource(root,
            new ContentPackageSourceLimits(
                archive.Length - 1, 1_000,
                allowInsecureServerHostedHttp: false,
                allowPrivateNetworkTargets: false));
        False(tiny.TryFetch(in request, out _, out ContentSourceFetchError capacity));
        Equal(ContentSourceFetchError.CapacityExceeded, capacity);

        string outside = Path.Combine(Path.GetTempPath(),
            $"ultrasim-local-source-outside-{Environment.ProcessId}-{Guid.NewGuid():N}.uspkg");
        System.IO.File.WriteAllBytes(outside, archive);
        string link = Path.Combine(root, "packages", "linked.uspkg");
        try
        {
            System.IO.File.CreateSymbolicLink(link, outside);
            var linkedRequest = request with
            {
                Source = new ContentSourceDescriptor(ContentSourceKind.Local, "packages/linked.uspkg"),
            };
            False(source.TryFetch(in linkedRequest, out _, out ContentSourceFetchError unsafeError));
            Equal(ContentSourceFetchError.Unavailable, unsafeError);
        }
        finally
        {
            if (System.IO.File.Exists(link)) System.IO.File.Delete(link);
            if (System.IO.File.Exists(outside)) System.IO.File.Delete(outside);
        }

        True(new ContentSourceDescriptor(
            ContentSourceKind.ServerHostedStatic, "http://127.0.0.1:8080/content.uspkg").IsValid);
        False(new ContentSourceDescriptor(
            ContentSourceKind.ExternalHttps, "http://cdn.example/content.uspkg").IsValid);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void OfficialLocalPresentationIsExactPersistentAndScopeIsolated()
{
    string repositoryRoot = FindRepositoryRoot();
    string sourceRoot = Path.Combine(repositoryRoot, "Client", "OfficialContent");
    string archivePath = Path.Combine(sourceRoot, OfficialLocalPresentation.ArchiveFileName);
    byte[] archive = System.IO.File.ReadAllBytes(archivePath);
    True(OfficialLocalPresentation.TryDecodeArchive(
        archive, out FetchedContentPackage? official));
    Equal(OfficialLocalPresentation.Manifest.ManifestHash, official!.Manifest.ManifestHash);
    Equal(2, official.Payloads.Count);
    var realm = new ApplicationRealmId(0xF14, 0xA);
    string cacheRoot = NewCacheRoot();
    string readOnlySourceRoot = cacheRoot + "-official-source";
    Directory.CreateDirectory(readOnlySourceRoot);
    string readOnlyArchive = Path.Combine(
        readOnlySourceRoot, OfficialLocalPresentation.ArchiveFileName);
    System.IO.File.Copy(archivePath, readOnlyArchive);
    if (!OperatingSystem.IsWindows())
    {
        System.IO.File.SetUnixFileMode(readOnlyArchive,
            UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        System.IO.File.SetUnixFileMode(readOnlySourceRoot,
            UnixFileMode.UserRead | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
    try
    {
        True(ContentCache.TryOpen(cacheRoot, out ContentCache? cache,
            out ContentCacheError openError), openError.ToString());
        using (cache)
        {
            True(OfficialLocalPresentation.TryOpenOrAcquire(
                cache!, readOnlySourceRoot, out ContentCatalogLease? local,
                out OfficialLocalPresentationResult installed), installed.ToString());
            using (local)
            {
                Equal(OfficialLocalPresentationStatus.Installed, installed.Status);
                True(local!.Catalog.Scope.IsLocal);
                Equal(2, local.Catalog.Entries.Count);
                False(cache!.TryOpenPackage(
                    realm, official.Manifest.PackageId, official.Manifest.ManifestHash,
                    out _, out ContentCacheError isolated));
                Equal(ContentCacheError.MissingManifest, isolated);

                ulong reusedBefore = cache.GetSnapshot().ReusedBlobs;
                True(cache.TryInstallPackage(
                    realm, official.Manifest, official.Payloads,
                    out _, out ContentCacheError realmInstall), realmInstall.ToString());
                ContentCacheSnapshot shared = cache.GetSnapshot();
                Equal(2, shared.BlobCount);
                Equal(reusedBefore + 2, shared.ReusedBlobs);
                Equal(1, shared.LocalManifestCount);
                Equal(1, shared.RealmManifestCount);
            }
        }

        True(ContentCache.TryOpen(cacheRoot, out ContentCache? reopened,
            out ContentCacheError reopenError), reopenError.ToString());
        using (reopened)
        {
            True(OfficialLocalPresentation.TryOpenOrAcquire(
                reopened!, officialSourceRoot: null, out ContentCatalogLease? warm,
                out OfficialLocalPresentationResult warmResult), warmResult.ToString());
            using (warm)
            {
                Equal(OfficialLocalPresentationStatus.Reopened, warmResult.Status);
                Equal(2, warm!.Catalog.Entries.Count);
            }
        }

        string untrustedRoot = Path.Combine(cacheRoot, "untrusted-source");
        Directory.CreateDirectory(untrustedRoot);
        byte[] changed = archive.ToArray();
        changed[^1] ^= 0x20;
        System.IO.File.WriteAllBytes(
            Path.Combine(untrustedRoot, OfficialLocalPresentation.ArchiveFileName), changed);
        string cleanCacheRoot = cacheRoot + "-clean";
        try
        {
            True(ContentCache.TryOpen(cleanCacheRoot, out ContentCache? clean,
                out ContentCacheError cleanError), cleanError.ToString());
            using (clean)
            {
                False(OfficialLocalPresentation.TryOpenOrAcquire(
                    clean!, untrustedRoot, out _, out OfficialLocalPresentationResult rejected));
                Equal(OfficialLocalPresentationStatus.AcquisitionFailure, rejected.Status);
                Equal(0, clean!.GetSnapshot().LocalManifestCount);
            }
        }
        finally
        {
            if (Directory.Exists(cleanCacheRoot)) Directory.Delete(cleanCacheRoot, recursive: true);
        }
    }
    finally
    {
        if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        if (Directory.Exists(readOnlySourceRoot))
        {
            if (!OperatingSystem.IsWindows())
                System.IO.File.SetUnixFileMode(readOnlySourceRoot,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(readOnlySourceRoot, recursive: true);
        }
    }
}

static void OfficialCabinWorldMeshesArePassiveRemovableAndScopeIsolated()
{
    string sourceRoot = Path.Combine(FindRepositoryRoot(), "Client", "OfficialContent");
    byte[] archive = System.IO.File.ReadAllBytes(Path.Combine(
        sourceRoot, OfficialCabinPresentation.ArchiveFileName));
    True(ContentPackageArchive.TryDecodeCanonical(archive,
        OfficialCabinPresentation.Manifest.PackageId,
        OfficialCabinPresentation.Manifest.ManifestHash,
        out FetchedContentPackage? package));
    Equal(16, package!.Payloads.Count);
    string root = NewCacheRoot();
    try
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var open), open.ToString());
        using (cache)
        {
            using PresentationGeneration primitive =
                PresentationGeneration.CreateWithResources(null, null, null, null, null);
            LogicalContentId pineId = OfficialCabinPresentation.Mesh("pine");
            Equal(PresentationSourceKind.Primitive, primitive.GetSource(pineId));
            False(primitive.TryReadWorldMeshPresentation(pineId, out _, out _, out _));
            True(OfficialCabinPresentation.TryOpenOrAcquire(cache!, sourceRoot,
                out ContentCatalogLease? local));
            using (local)
            {
                Equal(16 + 6, local!.Catalog.Entries.Count);
                using PresentationGeneration installed = PresentationGeneration.CreateWithResources(
                    null, null, null, null,
                    cache!.TryOpenCatalog(ContentBaseCatalog.Empty,
                        OfficialCabinPresentation.ActivationKey,
                        out ContentCatalogLease? selected, out _) ? selected : null);
                Equal(PresentationSourceKind.Local, installed.GetSource(pineId));
                True(installed.TryReadWorldMeshPresentation(pineId,
                    out byte[]? bytes, out ContentHash hash, out var source));
                Equal(PresentationSourceKind.Local, source);
                True(WorldMeshArtifact.TryInspect(bytes!, out WorldMeshArtifactInfo mesh));
                True(mesh.Triangles >= 200);
                Equal(ContentHash.Compute(bytes), hash);
                byte[] truncated = bytes![..^1];
                False(WorldMeshArtifact.TryInspect(truncated, out _));
                byte[] invalidSlot = bytes.ToArray();
                BinaryPrimitives.WriteInt32LittleEndian(
                    invalidSlot.AsSpan(WorldMeshArtifact.HeaderBytes + 48),
                    WorldMeshArtifact.MaximumMaterialSlots);
                False(WorldMeshArtifact.TryInspect(invalidSlot, out _));
                var realm = new ApplicationRealmId(0xC4B1A, 1);
                False(cache.TryOpenPackage(realm,
                    package.Manifest.PackageId, package.Manifest.ManifestHash, out _, out _));
                ulong reused = cache.GetSnapshot().ReusedBlobs;
                True(cache.TryInstallPackage(realm, package.Manifest, package.Payloads,
                    out _, out var install), install.ToString());
                Equal(reused + (ulong)package.Payloads.Count, cache.GetSnapshot().ReusedBlobs);

                ContentScopeKey realmScope = ContentScopeKey.ForRealm(realm);
                True(ContentActivationPlan.TryCreate(realmScope, new(0xC4B1A, 2),
                    ContentBaseCatalog.Empty.Fingerprint,
                    [new ContentActivationPackage(package.Manifest.PackageId,
                        package.Manifest.ManifestHash, ContentRequirementPolicy.Required)],
                    out ContentActivationPlan? realmPlan, out var planError), planError);
                True(cache.TryPublishCatalog(ContentBaseCatalog.Empty, realmPlan!, true,
                    out ContentCatalogLease? realmCatalog, out var publish), publish.ToString());
                using (realmCatalog)
                using (PresentationGeneration realmWins = PresentationGeneration.CreateWithResources(
                    null, null, null, realmCatalog,
                    cache.TryOpenCatalog(ContentBaseCatalog.Empty,
                        OfficialCabinPresentation.ActivationKey,
                        out ContentCatalogLease? lower, out _) ? lower : null))
                {
                    Equal(PresentationSourceKind.Realm, realmWins.GetSource(pineId));
                    Equal(PresentationSourceKind.Realm,
                        realmWins.GetSource(OfficialCabinPresentation.Material("wood")));
                    var user = UserTexturePackage("userpack.p9-wood",
                        "cabin-p9:texture/wood", "textures/wood.png", 0x47);
                    True(cache.TryInstallUserResourcePack(user.Manifest, user.Payloads,
                        out _, out var userInstall), userInstall.ToString());
                    True(cache.TrySetEnabledUserResourcePacks([UserIdentity(user)], out var selectedUser),
                        selectedUser.ToString());
                    True(cache.TryOpenEnabledUserResourcePacks(out UserResourcePackStackLease? stack,
                        out var stackError), stackError.ToString());
                    using (stack)
                    using (PresentationGeneration userWins = PresentationGeneration.CreateWithResources(
                        stack, null, null,
                        cache.TryOpenCatalog(ContentBaseCatalog.Empty,
                            new ContentScopeActivationKey(realmPlan!.ScopeActivation, realmPlan.PlanHash),
                            out ContentCatalogLease? upper, out _) ? upper : null,
                        cache.TryOpenCatalog(ContentBaseCatalog.Empty,
                            OfficialCabinPresentation.ActivationKey,
                            out ContentCatalogLease? baseLease, out _) ? baseLease : null))
                    {
                        Equal(PresentationSourceKind.User,
                            userWins.GetSource(Logical("cabin-p9:texture/wood")));
                        Equal(PresentationSourceKind.Realm, userWins.GetSource(pineId));
                    }
                    True(cache.TrySetEnabledUserResourcePacks([], out var disabled),
                        disabled.ToString());
                }
            }
        }
        True(ContentCache.TryOpen(root, out ContentCache? reopened,
            out var reopen), reopen.ToString());
        using (reopened)
        {
            True(OfficialCabinPresentation.TryOpenOrAcquire(reopened!, null, out var warm));
            using (warm) Equal(16 + 6, warm!.Catalog.Entries.Count);
        }
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void FinalCabinArtCoexistsWithOlderWarmActivation()
{
    string sourceRoot = Path.Combine(FindRepositoryRoot(), "Client", "OfficialContent");
    string root = NewCacheRoot();
    try
    {
        PackageFixture older = CreateSingleTexturePackage(
            "official.world-local.cabin-p9", "cabin-p9", "cabin-p9:texture/old",
            ContentDefinitionOperation.Add, "textures/old.png", 0x39);
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var open), open.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(ContentScopeKey.Local, older.Manifest,
                older.Payloads, out _, out var install), install.ToString());
            True(ContentActivationPlan.TryCreate(ContentScopeKey.Local,
                new ContentActivationId(0x5039_2D43_4142_494EUL, 0x2D41_5254_0000_0001UL),
                ContentBaseCatalog.Empty.Fingerprint, [Required(older.Manifest)],
                out ContentActivationPlan? oldPlan, out var planError), planError);
            True(cache.TryPublishCatalog(ContentBaseCatalog.Empty, oldPlan!, true,
                out ContentCatalogLease? oldLease, out var published), published.ToString());
            oldLease!.Dispose();

            True(OfficialCabinPresentation.TryOpenOrAcquire(cache, sourceRoot,
                out ContentCatalogLease? current));
            using (current)
            {
                Equal(16 + 6, current!.Catalog.Entries.Count);
                True(current.Catalog.TryGet(OfficialCabinPresentation.Mesh("roof"), out _));
            }
            var oldKey = new ContentScopeActivationKey(
                oldPlan!.ScopeActivation, oldPlan.PlanHash);
            True(cache.TryOpenCatalog(ContentBaseCatalog.Empty, oldKey,
                out ContentCatalogLease? oldAgain, out var oldOpen), oldOpen.ToString());
            using (oldAgain) Equal(1, oldAgain!.Catalog.Entries.Count);
        }
        True(ContentCache.TryOpen(root, out ContentCache? reopened, out var warmOpen),
            warmOpen.ToString());
        using (reopened)
        {
            True(OfficialCabinPresentation.TryOpenOrAcquire(reopened!, null,
                out ContentCatalogLease? warm));
            using (warm) Equal(16 + 6, warm!.Catalog.Entries.Count);
        }
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void IdentityGrammarAndDefaultsFailClosed()
{
    False(default(ContentHash).IsValid);
    False(default(ContentNamespaceId).IsValid);
    False(default(ContentPackageId).IsValid);
    False(default(LogicalContentId).IsValid);
    False(default(ContentFilePath).IsValid);
    False(default(ApplicationRealmId).IsValid);
    False(default(ContentActivationId).IsValid);
    False(default(ContentSetAppliedProof).IsValid);

    True(ContentNamespaceId.TryCreate("cabin", out _));
    False(ContentNamespaceId.TryCreate("Cabin", out _));
    False(ContentNamespaceId.TryCreate("cabin.mod", out _));
    True(ContentPackageId.TryCreate("cabin.default", out _));
    False(ContentPackageId.TryCreate("cabin..default", out _));
    True(LogicalContentId.TryCreate("cabin:texture/wall", out LogicalContentId logical));
    Equal("cabin", logical.Namespace.Value!);
    False(LogicalContentId.TryCreate("cabin:Texture/wall", out _));
    False(LogicalContentId.TryCreate("cabin:../wall", out _));
    True(ContentFilePath.TryCreate("textures/cabin-wall.png", out _));
    False(ContentFilePath.TryCreate("../cabin-wall.png", out _));
    False(ContentFilePath.TryCreate("textures\\wall.png", out _));

    ContentHash hash = ContentHash.Compute("canonical"u8);
    True(ContentHash.TryParse(hash.ToString(), out ContentHash parsed));
    Equal(hash, parsed);
    False(ContentHash.TryParse(hash.ToString().ToUpperInvariant(), out _));
}

static void ManifestIdentityIsCanonicalAndBounded()
{
    PackageFixture fixture = CreateBasePackage();
    True(ContentPackageManifest.TryCreate(
        ContentLimits.CurrentPipelineFormat,
        ContentLimits.CurrentPipelineFormat,
        fixture.Manifest.PackageId,
        fixture.Manifest.Namespace,
        fixture.Manifest.Name,
        fixture.Manifest.Version,
        fixture.Manifest.TrustClass,
        fixture.Manifest.Files.Reverse(),
        fixture.Manifest.Definitions.Reverse(),
        out ContentPackageManifest? reordered,
        out _));
    Equal(fixture.Manifest.ManifestHash, reordered!.ManifestHash);

    ContentNamespaceId otherNamespace = Namespace("other");
    ContentDefinitionDescriptor invalidAdd = TextureDefinition(
        "cabin:texture/foreign", ContentDefinitionOperation.Add, "textures/default.png");
    False(ContentPackageManifest.TryCreate(1, 1, Package("other.invalid"), otherNamespace,
        "Invalid", "1", ContentTrustClass.ServerAuthorizedPassive, fixture.Manifest.Files,
        [invalidAdd], out _, out _));
    False(ContentPackageManifest.TryCreate(2, 2, Package("future.pipeline"), otherNamespace,
        "Future", "1", ContentTrustClass.ServerAuthorizedPassive, [], [], out _, out _));
    False(ContentPackageManifest.TryCreate(1, 1, Package("executable.package"), otherNamespace,
        "Executable", "1", ContentTrustClass.ExecutableExtension, [], [], out _, out _));
    var tight = new ContentLimits(1, 2, 8, 2, 2 * 1024 * 1024, 2 * 1024 * 1024);
    False(ContentPackageManifest.TryCreate(1, 1, fixture.Manifest.PackageId, fixture.Manifest.Namespace,
        fixture.Manifest.Name, fixture.Manifest.Version, fixture.Manifest.TrustClass,
        fixture.Manifest.Files, fixture.Manifest.Definitions, out _, out _, tight));

    byte[] script = "extends Node"u8.ToArray();
    True(ContentFilePath.TryCreate("scripts/attack.gd", out ContentFilePath scriptPath));
    False(ContentFileDescriptor.TryCreate(scriptPath, ContentFileKind.PngTexture, script.Length,
        ContentHash.Compute(script), default, out _));
}

static void BoundedAdmissionsStopBeforeOversizedMaterialization()
{
    PackageFixture fixture = CreateBasePackage();
    ContentBaseCatalog baseCatalog = BaseCatalog(fixture);
    ContentActivationPackage requirement = Required(CreateReplacePackage().Manifest);
    int packageVisits = 0;
    IEnumerable<ContentActivationPackage> TooManyPackages()
    {
        while (true)
        {
            packageVisits++;
            yield return requirement;
        }
    }
    var tight = new ContentLimits(1, 4, 8, 2, 2 * 1024 * 1024, 2 * 1024 * 1024);
    False(ContentActivationPlan.TryCreate(new ApplicationRealmId(1, 1), new ContentActivationId(1, 1),
        baseCatalog.Fingerprint, TooManyPackages(), out _, out _, tight));
    Equal(2, packageVisits);

    int payloadVisits = 0;
    IEnumerable<ContentFilePayload> TooManyPayloads()
    {
        while (true)
        {
            payloadVisits++;
            yield return fixture.Payloads[0];
        }
    }
    False(ContentPackageValidator.TryValidatePayloads(fixture.Manifest, TooManyPayloads(), out _, out _));
    Equal(fixture.Manifest.Files.Count + 1, payloadVisits);
}

static void PassivePayloadsRequireExactBytesAndAllowedEnvelopes()
{
    PackageFixture fixture = CreateBasePackage();
    True(ContentPackageValidator.TryValidatePayloads(fixture.Manifest, fixture.Payloads,
        out VerifiedContentPackage? verified, out _));
    True(verified is not null);

    ContentFilePayload[] corrupted = fixture.Payloads.ToArray();
    byte[] corruptPng = corrupted[0].Bytes.ToArray();
    corruptPng[^1] ^= 0x1;
    corrupted[0] = new ContentFilePayload(corrupted[0].Path, corruptPng);
    False(ContentPackageValidator.TryValidatePayloads(fixture.Manifest, corrupted, out _, out _));

    byte[] disguised = new byte[24];
    ContentFilePath path = FilePath("textures/disguised.png");
    ContentFileDescriptor descriptor = File(path, ContentFileKind.PngTexture, disguised);
    ContentDefinitionDescriptor definition = TextureDefinition(
        "bad:texture/disguised", ContentDefinitionOperation.Add, path.Value!);
    ContentPackageManifest manifest = Manifest("bad.passive", "bad", ContentTrustClass.ServerAuthorizedPassive,
        [descriptor], [definition]);
    False(ContentPackageValidator.TryValidatePayloads(manifest,
        [new ContentFilePayload(path, disguised)], out _, out _));

    ContentFilePayload worldPayload = fixture.Payloads.Single(value =>
        value.Path.Value!.EndsWith(".usworldpack", StringComparison.Ordinal));
    byte[] damagedWorld = worldPayload.Bytes.ToArray();
    damagedWorld[^1] ^= 0x20;
    ContentFileDescriptor damagedDescriptor = File(worldPayload.Path, ContentFileKind.WorldPack, damagedWorld);
    ContentDefinitionDescriptor damagedDefinition = FileDefinition(
        "bad:world/damaged", ContentDefinitionKind.WorldPack, ContentDefinitionOperation.Add,
        worldPayload.Path.Value!);
    ContentPackageManifest damagedManifest = Manifest("bad.world", "bad",
        ContentTrustClass.ServerAuthorizedPassive, [damagedDescriptor], [damagedDefinition]);
    False(ContentPackageValidator.TryValidatePayloads(damagedManifest,
        [new ContentFilePayload(worldPayload.Path, damagedWorld)], out _, out _));
}

static void ActorPresentationGlbIsBoundedPassiveAndCatalogResolved()
{
    byte[] x = OfficialActor(OfficialLocalPresentation.HumanXPath);
    byte[] y = OfficialActor(OfficialLocalPresentation.HumanYPath);
    True(ActorPresentationArtifact.TryInspect(
        x, out ActorPresentationArtifactInfo xInfo, out ActorPresentationArtifactError xError),
        xError.ToString());
    True(ActorPresentationArtifact.TryInspect(
        y, out ActorPresentationArtifactInfo yInfo, out ActorPresentationArtifactError yError),
        yError.ToString());
    Equal(65, xInfo.Bones);
    Equal(65, yInfo.Bones);
    Equal(xInfo.OrderedSkeletonFingerprint, yInfo.OrderedSkeletonFingerprint);
    Equal(ActorPresentationSemanticClips.RequiredV1,
        xInfo.SemanticClips & ActorPresentationSemanticClips.RequiredV1);
    True(xInfo.MainHandBone.EndsWith("RightHand", StringComparison.Ordinal));
    True(xInfo.AnimationTracks > 0 && xInfo.AnimationKeys > 0);
    True(xInfo.MeshVertices > 0 && xInfo.MeshIndices > xInfo.MeshVertices);

    var tooSmall = ActorPresentationArtifactLimits.Default with
    {
        MaximumArtifactBytes = x.Length - 1,
    };
    False(ActorPresentationArtifact.TryInspect(x, out _, out ActorPresentationArtifactError bounded, tooSmall));
    Equal(ActorPresentationArtifactError.LimitExceeded, bounded);
    var tooManyVertices = ActorPresentationArtifactLimits.Default with
    {
        MaximumMeshVertices = xInfo.MeshVertices - 1,
    };
    False(ActorPresentationArtifact.TryInspect(
        x, out _, out ActorPresentationArtifactError vertexBound, tooManyVertices));
    Equal(ActorPresentationArtifactError.InvalidReference, vertexBound);
    var tooManyIndices = ActorPresentationArtifactLimits.Default with
    {
        MaximumMeshIndices = xInfo.MeshIndices - 1,
    };
    False(ActorPresentationArtifact.TryInspect(
        x, out _, out ActorPresentationArtifactError indexBound, tooManyIndices));
    Equal(ActorPresentationArtifactError.InvalidReference, indexBound);
    byte[] damaged = x.ToArray();
    BinaryPrimitives.WriteUInt32LittleEndian(damaged.AsSpan(8), checked((uint)(damaged.Length - 4)));
    False(ActorPresentationArtifact.TryInspect(
        damaged, out _, out ActorPresentationArtifactError container));
    Equal(ActorPresentationArtifactError.InvalidContainer, container);
    byte[] external = RewriteActorJson(x, static root =>
        root["buffers"]!.AsArray()[0]!.AsObject()["uri"] = "https://invalid.example/body.bin");
    False(ActorPresentationArtifact.TryInspect(
        external, out _, out ActorPresentationArtifactError externalReference));
    Equal(ActorPresentationArtifactError.UnsupportedFeature, externalReference);
    byte[] cyclic = RewriteActorJson(x, static root =>
        root["nodes"]!.AsArray()[0]!.AsObject()["children"] = new JsonArray(0));
    False(ActorPresentationArtifact.TryInspect(
        cyclic, out _, out ActorPresentationArtifactError cyclicGraph));
    Equal(ActorPresentationArtifactError.InvalidReference, cyclicGraph);
    byte[] unknownAnimation = RewriteActorJson(x, static root =>
    {
        JsonArray animations = root["animations"]!.AsArray();
        JsonObject attack = animations[0]!.DeepClone().AsObject();
        attack["name"] = "Attack";
        animations.Add(attack);
    });
    False(ActorPresentationArtifact.TryInspect(
        unknownAnimation, out _, out ActorPresentationArtifactError unknownClip));
    Equal(ActorPresentationArtifactError.UnsupportedFeature, unknownClip);
    byte[] duplicateSemanticAnimation = RewriteActorJson(x, static root =>
    {
        JsonArray animations = root["animations"]!.AsArray();
        JsonObject duplicateIdle = animations.Single(animation =>
            animation!["name"]!.GetValue<string>() == "Idle")!.DeepClone().AsObject();
        duplicateIdle["name"] = "alternate/Idle-loop";
        animations.Add(duplicateIdle);
    });
    False(ActorPresentationArtifact.TryInspect(
        duplicateSemanticAnimation, out _,
        out ActorPresentationArtifactError duplicateSemanticClip));
    Equal(ActorPresentationArtifactError.UnsupportedFeature, duplicateSemanticClip);

    PackageFixture builtInX = ActorPackage(
        "ultrasim.actor.default-x", "actors", ContentTrustClass.BuiltIn,
        "actors:human/x", ContentDefinitionOperation.Add,
        "actors/human-x.glb", x);
    PackageFixture serverY = ActorPackage(
        "server.actor.add-y", "actors-server", ContentTrustClass.ServerAuthorizedPassive,
        "actors-server:human/y", ContentDefinitionOperation.Add,
        "actors/human-y.glb", y);
    PackageFixture replaceX = ActorPackage(
        "server.actor.replace-x", "actors-server", ContentTrustClass.ServerAuthorizedPassive,
        "actors:human/x", ContentDefinitionOperation.Replace,
        "actors/replacement-x.glb", x);
    ContentBaseCatalog baseCatalog = BaseCatalog(builtInX);
    ContentActivationPlan plan = Plan(
        new ApplicationRealmId(0xD07B, 1), new ContentActivationId(0xD07B, 2),
        baseCatalog.Fingerprint,
        [Required(serverY.Manifest), Required(replaceX.Manifest)]);
    True(ContentCatalogResolver.TryResolve(
        baseCatalog, plan, [Verified(replaceX), Verified(serverY)],
        out ResolvedContentCatalog? catalog, out ContentCatalogResolutionError resolution),
        resolution.ToString());
    True(catalog!.TryGet(Logical("actors:human/x"), out ResolvedContentEntry? resolvedX));
    True(catalog.TryGet(Logical("actors-server:human/y"), out ResolvedContentEntry? resolvedY));
    Equal(replaceX.Manifest.PackageId, resolvedX!.OwnerPackageId);
    Equal(serverY.Manifest.PackageId, resolvedY!.OwnerPackageId);
    Equal(ContentDefinitionKind.ActorPresentation, resolvedX.Kind);

    string cacheRoot = Path.Combine(Path.GetTempPath(),
        $"ultrasim-actor-content-{Environment.ProcessId}-{Guid.NewGuid():N}");
    try
    {
        True(ContentCache.TryOpen(cacheRoot, out ContentCache? cache, out ContentCacheError openError),
            openError.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(
                plan.Realm, serverY.Manifest, serverY.Payloads, out _, out ContentCacheError yInstall),
                yInstall.ToString());
            True(cache.TryInstallPackage(
                plan.Realm, replaceX.Manifest, replaceX.Payloads, out _, out ContentCacheError xInstall),
                xInstall.ToString());
            True(cache.TryPublishCatalog(
                baseCatalog, plan, false, out ContentCatalogLease? liveCatalog,
                out ContentCacheError publishError), publishError.ToString());
            using (liveCatalog)
            {
                var requirements = new ActorPresentationResolutionRequirements(
                    65, xInfo.OrderedSkeletonFingerprint,
                    ActorPresentationSemanticClips.RequiredV1);
                True(ContentActorPresentationResolver.TryResolve(
                    liveCatalog!, Logical("actors:human/x"), requirements,
                    out ActorPresentationContentLease? xLease,
                    out ContentActorPresentationResolutionError xResolution), xResolution.ToString());
                True(ContentActorPresentationResolver.TryResolve(
                    liveCatalog!, Logical("actors-server:human/y"), requirements,
                    out ActorPresentationContentLease? yLease,
                    out ContentActorPresentationResolutionError yResolution), yResolution.ToString());
                using (xLease)
                using (yLease)
                {
                    Equal(replaceX.Manifest.PackageId, xLease!.OwnerPackage);
                    Equal(serverY.Manifest.PackageId, yLease!.OwnerPackage);
                    Equal(xInfo.OrderedSkeletonFingerprint,
                        xLease.Info.OrderedSkeletonFingerprint);
                    Equal(y.Length, yLease.Bytes.Length);
                    True(xLease.CatalogGeneration == yLease.CatalogGeneration);
                }

                var wrong = requirements with
                {
                    OrderedSkeletonFingerprint = ContentHash.Compute("wrong-skeleton"u8),
                };
                False(ContentActorPresentationResolver.TryResolve(
                    liveCatalog!, Logical("actors:human/x"), wrong, out _,
                    out ContentActorPresentationResolutionError mismatch));
                Equal(ContentActorPresentationResolutionError.SkeletonProfileMismatch, mismatch);
            }
        }
    }
    finally
    {
        if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
    }
}

static void PresentationGenerationSelectsRealmLocalPrimitiveAndRetainsOldLeases()
{
    byte[] x = OfficialActor(OfficialLocalPresentation.HumanXPath);
    byte[] y = OfficialActor(OfficialLocalPresentation.HumanYPath);
    PackageFixture localX = ActorPackage(
        "local.actor.x", "actors", ContentTrustClass.ServerAuthorizedPassive,
        ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        ContentDefinitionOperation.Add, "actors/local-x.glb", x);
    PackageFixture localY = ActorPackage(
        "local.actor.y", "actors", ContentTrustClass.ServerAuthorizedPassive,
        ConceptDemoActorPresentationProfile.HumanYDefinitionId,
        ContentDefinitionOperation.Add, "actors/local-y.glb", y);
    PackageFixture realmX = ActorPackage(
        "realm.actor.x", "actors", ContentTrustClass.ServerAuthorizedPassive,
        ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        ContentDefinitionOperation.Add, "actors/realm-x.glb", x);
    PackageFixture incompatibleRealmX = CreateSingleTexturePackage(
        "realm.fake-actor-x", "actors", ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        ContentDefinitionOperation.Add, "textures/fake-x.png", 0x7A);
    ApplicationRealmId realm = new(0xF12, 1);
    ContentActivationPlan localPlan = ScopePlan(
        ContentScopeKey.Local, new ContentActivationId(0xF12, 2),
        ContentBaseCatalog.Empty.Fingerprint,
        [Required(localX.Manifest), Required(localY.Manifest)]);
    ContentActivationPlan realmPlan = ScopePlan(
        ContentScopeKey.ForRealm(realm), new ContentActivationId(0xF12, 3),
        ContentBaseCatalog.Empty.Fingerprint, [Required(realmX.Manifest)]);
    ContentActivationPlan incompatiblePlan = ScopePlan(
        ContentScopeKey.ForRealm(realm), new ContentActivationId(0xF12, 5),
        ContentBaseCatalog.Empty.Fingerprint, [Required(incompatibleRealmX.Manifest)]);
    LogicalContentId xId = Logical(ConceptDemoActorPresentationProfile.HumanXDefinitionId);
    LogicalContentId yId = Logical(ConceptDemoActorPresentationProfile.HumanYDefinitionId);
    ContentHash stableLocalFingerprint = default;
    PresentationGenerationId originalLocalGeneration = default;

    WithCacheRoot(root =>
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            foreach (PackageFixture package in new[] { localX, localY })
                True(cache!.TryInstallPackage(ContentScopeKey.Local,
                    package.Manifest, package.Payloads, out _, out var error), error.ToString());
            True(cache!.TryInstallPackage(ContentScopeKey.ForRealm(realm),
                realmX.Manifest, realmX.Payloads, out _, out var realmError), realmError.ToString());
            True(cache.TryInstallPackage(ContentScopeKey.ForRealm(realm),
                incompatibleRealmX.Manifest, incompatibleRealmX.Payloads,
                out _, out var incompatibleInstall), incompatibleInstall.ToString());
            True(cache.TryPublishCatalog(ContentBaseCatalog.Empty, localPlan, false,
                out ContentCatalogLease? localCatalog, out var localError), localError.ToString());
            True(cache.TryPublishCatalog(ContentBaseCatalog.Empty, realmPlan, false,
                out ContentCatalogLease? realmCatalog, out var publishError), publishError.ToString());

            using PresentationGeneration primitive = PresentationGeneration.Create(null, null);
            Equal(PresentationSourceKind.Primitive, primitive.GetSource(xId));
            Equal(2, primitive.Snapshot.PrimitiveSelections);
            using PresentationGeneration local = PresentationGeneration.Create(null, localCatalog);
            stableLocalFingerprint = local.StableFingerprint;
            originalLocalGeneration = local.Generation;
            Equal(PresentationSourceKind.Local, local.GetSource(xId));
            Equal(PresentationSourceKind.Local, local.GetSource(yId));
            True(cache.TryOpenCatalog(ContentBaseCatalog.Empty,
                new ContentScopeActivationKey(localPlan.ScopeActivation, localPlan.PlanHash),
                out ContentCatalogLease? sameLocal, out var sameLocalOpen), sameLocalOpen.ToString());
            using PresentationGeneration localAgain = PresentationGeneration.Create(null, sameLocal);
            Equal(local.StableFingerprint, localAgain.StableFingerprint);
            NotEqual(local.Generation, localAgain.Generation);
            using PresentationGeneration mixed = PresentationGeneration.Create(realmCatalog, null);
            Equal(PresentationSourceKind.Realm, mixed.GetSource(xId));
            Equal(PresentationSourceKind.Primitive, mixed.GetSource(yId));

            True(cache.TryPublishCatalog(ContentBaseCatalog.Empty, incompatiblePlan, false,
                out ContentCatalogLease? incompatibleCatalog,
                out var incompatiblePublish), incompatiblePublish.ToString());
            True(cache.TryOpenCatalog(ContentBaseCatalog.Empty,
                new ContentScopeActivationKey(localPlan.ScopeActivation, localPlan.PlanHash),
                out ContentCatalogLease? fallbackLocal, out var fallbackOpen), fallbackOpen.ToString());
            using PresentationGeneration incompatible = PresentationGeneration.Create(
                incompatibleCatalog, fallbackLocal);
            Equal(PresentationSourceKind.Local, incompatible.GetSource(xId));

            var localKey = new ContentScopeActivationKey(localPlan.ScopeActivation, localPlan.PlanHash);
            True(cache.TryOpenCatalog(ContentBaseCatalog.Empty, localKey,
                out ContentCatalogLease? secondLocal, out var reopenError), reopenError.ToString());
            PresentationGeneration layered = PresentationGeneration.Create(
                OpenRealmAgain(cache, realmPlan), secondLocal);
            Equal(PresentationSourceKind.Realm, layered.GetSource(xId));
            Equal(PresentationSourceKind.Local, layered.GetSource(yId));
            Equal(local.StableFingerprint, layered.StableFingerprint);
            NotEqual(local.Generation, layered.Generation);
            True(layered.TryResolveActorPresentation(
                xId, ConceptDemoActorPresentationProfile.Requirements,
                out ActorPresentationContentLease? retained, out var resolution),
                resolution.ToString());
            Equal(PresentationSourceKind.Realm, retained!.PresentationSource);
            Equal(layered.Generation, retained.PresentationGeneration);
            True(retained.Bytes.Span.SequenceEqual(x));
            False(layered.TryResolveActorPresentation(
                xId, ConceptDemoActorPresentationProfile.Requirements,
                out ActorPresentationContentLease? tooLarge, out var limitError,
                ActorPresentationArtifactLimits.Default with { MaximumArtifactBytes = x.Length - 1 }));
            True(tooLarge is null);
            Equal(ContentActorPresentationResolutionError.InvalidArtifact, limitError);
            layered.Dispose();
            True(cache.GetSnapshot().LiveBlobPins > 0);
            retained.Dispose();
            local.Dispose();
            localAgain.Dispose();
            mixed.Dispose();
            incompatible.Dispose();
            primitive.Dispose();
            Equal(0, cache.GetSnapshot().LiveBlobPins);
            Equal(0, cache.GetSnapshot().LiveCatalogPins);
        }
        True(ContentCache.TryOpen(root, out ContentCache? reopened,
            out var reopenCacheError), reopenCacheError.ToString());
        using (reopened)
        {
            True(reopened!.TryOpenCatalog(ContentBaseCatalog.Empty,
                new ContentScopeActivationKey(localPlan.ScopeActivation, localPlan.PlanHash),
                out ContentCatalogLease? persistedLocal,
                out var persistedOpenError), persistedOpenError.ToString());
            using PresentationGeneration persisted = PresentationGeneration.Create(
                null, persistedLocal);
            Equal(stableLocalFingerprint, persisted.StableFingerprint);
            NotEqual(originalLocalGeneration, persisted.Generation);
        }
    });
}

static ContentCatalogLease OpenRealmAgain(ContentCache cache, ContentActivationPlan plan)
{
    var key = new ContentScopeActivationKey(plan.ScopeActivation, plan.PlanHash);
    True(cache.TryOpenCatalog(ContentBaseCatalog.Empty, key,
        out ContentCatalogLease? catalog, out var error), error.ToString());
    return catalog!;
}

static void UserResourcePackPolicyIsStrictAndSeparate()
{
    byte[] actor = OfficialActor(OfficialLocalPresentation.HumanXPath);
    ContentFilePath actorPath = FilePath("actors/user-x.glb");
    ContentFileDescriptor actorFile = File(actorPath, ContentFileKind.ActorGltfBinary, actor);
    ContentDefinitionDescriptor replace = FileDefinition(
        ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        ContentDefinitionKind.ActorPresentation, ContentDefinitionOperation.Replace,
        actorPath.Value!);
    ContentPackageId package = Package("user.actor.strict");
    ContentNamespaceId contentNamespace = Namespace("userpack");

    False(ContentPackageManifest.TryCreate(
        1, 1, package, contentNamespace, "User", "1",
        ContentTrustClass.UserResourcePack, [actorFile], [replace], out _, out _));
    True(ContentPackageManifest.TryCreateUserResourcePack(
        1, 1, package, contentNamespace, "User", "1", [actorFile], [replace],
        out ContentPackageManifest? accepted, out string? acceptedError), acceptedError);
    Equal(ContentTrustClass.UserResourcePack, accepted!.TrustClass);

    ContentDefinitionDescriptor add = FileDefinition(
        "userpack:new-actor", ContentDefinitionKind.ActorPresentation,
        ContentDefinitionOperation.Add, actorPath.Value!);
    False(ContentPackageManifest.TryCreateUserResourcePack(
        1, 1, package, contentNamespace, "Bad Add", "1", [actorFile], [add],
        out _, out _));

    byte[] texture = Png(0x55);
    ContentFilePath texturePath = FilePath("textures/user.png");
    ContentFileDescriptor textureFile = File(texturePath, ContentFileKind.PngTexture, texture);
    ContentDefinitionDescriptor textureReplace = TextureDefinition(
        "cabin:wall", ContentDefinitionOperation.Replace, texturePath.Value!);
    True(ContentPackageManifest.TryCreateUserResourcePack(
        1, 1, package, contentNamespace, "Texture", "1",
        [textureFile], [textureReplace], out ContentPackageManifest? texturePack,
        out string? textureError), textureError);
    True(ContentPackageValidator.TryValidatePayloads(
        texturePack!, [new ContentFilePayload(texturePath, texture)], out _, out _));

    True(ContentMaterialDefinition.TryCreate(
        ContentMaterialFamily.StandardLit, Logical("cabin:texture/wall"),
        0.5f, 0f, 0f, out ContentMaterialDefinition material));
    True(ContentDefinitionDescriptor.TryCreateMaterial(
        Logical("cabin:material/wall"), ContentDefinitionOperation.Replace,
        material, out ContentDefinitionDescriptor? materialReplace));
    False(ContentPackageManifest.TryCreateUserResourcePack(
        1, 1, package, contentNamespace, "Bad material", "1",
        [textureFile], [materialReplace!], out _, out _));

    byte[] world = WorldPackBytes();
    ContentFilePath worldPath = FilePath("worlds/forbidden.usworldpack");
    ContentFileDescriptor worldFile = File(worldPath, ContentFileKind.WorldPack, world);
    ContentDefinitionDescriptor worldReplace = FileDefinition(
        "cabin:world", ContentDefinitionKind.WorldPack,
        ContentDefinitionOperation.Replace, worldPath.Value!);
    False(ContentPackageManifest.TryCreateUserResourcePack(
        1, 1, package, contentNamespace, "Bad world", "1",
        [worldFile], [worldReplace], out _, out _));

    ContentFilePath uiPath = FilePath("ui/forbidden.usui");
    ContentFileDescriptor.TryCreate(
        uiPath, ContentFileKind.UiDefinition, 1, ContentHash.Compute([1]), default,
        out ContentFileDescriptor uiFile);
    ContentDefinitionDescriptor uiDefinition = FileDefinition(
        "cabin:ui", ContentDefinitionKind.Icon,
        ContentDefinitionOperation.Replace, uiPath.Value!);
    False(ContentPackageManifest.TryCreateUserResourcePack(
        1, 1, package, contentNamespace, "Bad UI", "1",
        [uiFile], [uiDefinition], out _, out _));

    ContentFileDescriptor.TryCreate(
        actorPath, ContentFileKind.ActorGltfBinary,
        ActorPresentationArtifactLimits.Default.MaximumArtifactBytes + 1L,
        ContentHash.Compute("oversized"u8), default, out ContentFileDescriptor oversized);
    False(ContentPackageManifest.TryCreateUserResourcePack(
        1, 1, package, contentNamespace, "Oversized", "1",
        [oversized], [replace], out _, out _));

    byte[] external = RewriteActorJson(actor, static root =>
        root["buffers"]![0]!["uri"] = "https://example.invalid/evil.bin");
    PackageFixture externalPack = UserActorPackage(
        "user.actor.external", ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        "actors/external.glb", external);
    False(ContentPackageValidator.TryValidatePayloads(
        externalPack.Manifest, externalPack.Payloads, out _, out _));
}

static void UserResourcePackStorePersistsOrderSharesCasAndFailsClosed()
{
    byte[] x = OfficialActor(OfficialLocalPresentation.HumanXPath);
    byte[] tinted = TintActor(x, 0.92, 0.08, 0.18);
    PackageFixture localX = ActorPackage(
        "local.shared-x", "actors", ContentTrustClass.ScopeAuthorizedPassive,
        ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        ContentDefinitionOperation.Add, "actors/shared-x.glb", x);
    PackageFixture packA = UserActorPackage(
        "user.pack.a", ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        "actors/a.glb", x);
    PackageFixture packB = UserActorPackage(
        "user.pack.b", ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        "actors/b.glb", tinted);
    UserResourcePackIdentity a = UserIdentity(packA);
    UserResourcePackIdentity b = UserIdentity(packB);
    ApplicationRealmId realm = new(0xF15, 0xCA5);
    string root = NewCacheRoot();
    string sourceRoot = Path.Combine(Path.GetTempPath(),
        "ultrasim-user-pack-source", Guid.NewGuid().ToString("N"));
    try
    {
        Directory.CreateDirectory(sourceRoot);
        True(ContentPackageArchive.TryEncodeCanonical(
            packA.Manifest, packA.Payloads, out byte[] archive));
        System.IO.File.WriteAllBytes(Path.Combine(sourceRoot, "pack-a.uspkg"), archive);
        var source = new LocalContentPackageSource(sourceRoot);
        var fetch = new UserResourcePackFetchRequest(a, FilePath("pack-a.uspkg"));
        True(source.TryFetchUserResourcePack(
            in fetch, out FetchedContentPackage? fetched, out var fetchError),
            fetchError.ToString());
        Equal(a.ManifestHash, fetched!.Manifest.ManifestHash);
        False(ContentPackageArchive.TryDecodeCanonical(
            archive, a.PackageId, a.ManifestHash, out _));

        True(ContentCache.TryOpen(root, out ContentCache? cache, out var open), open.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(ContentScopeKey.Local, localX.Manifest,
                localX.Payloads, out _, out var localInstall), localInstall.ToString());
            Equal(1, cache.GetSnapshot().BlobCount);
            True(cache.TryInstallUserResourcePack(
                packA.Manifest, packA.Payloads, out InstalledUserResourcePack installedA,
                out var installA), installA.ToString());
            True(cache.TryInstallUserResourcePack(
                packB.Manifest, packB.Payloads, out InstalledUserResourcePack installedB,
                out var installB), installB.ToString());
            True(cache.TryInstallPackage(ContentScopeKey.ForRealm(realm), localX.Manifest,
                localX.Payloads, out var realmInstall, out var realmInstallError),
                realmInstallError.ToString());
            Equal(a, installedA.Identity);
            Equal(b, installedB.Identity);
            True(cache.GetSnapshot().ReusedBlobs >= 2);
            Equal(2, cache.GetSnapshot().BlobCount);
            Equal(2, cache.GetSnapshot().InstalledResourcePackCount);
            False(cache.TryOpenPackage(ContentScopeKey.Local, packA.Manifest.PackageId,
                packA.Manifest.ManifestHash, out _, out _));
            False(cache.TryOpenPackage(ContentScopeKey.ForRealm(realm),
                packA.Manifest.PackageId, packA.Manifest.ManifestHash, out _, out _));
            True(cache.TrySetEnabledUserResourcePacks([a, b], out var selection), selection.ToString());
            True(cache.TryOpenEnabledUserResourcePacks(
                out UserResourcePackStackLease? stack, out var stackError), stackError.ToString());
            using (stack)
            {
                Equal(2, stack!.Identities.Count);
                Equal(a, stack.Identities[0]);
                Equal(b, stack.Identities[1]);
            }
            Equal(0, cache.GetSnapshot().LiveResourcePackLeases);
        }

        True(ContentCache.TryOpen(root, out ContentCache? reopened, out var reopen), reopen.ToString());
        using (reopened)
        {
            ContentCacheSnapshot snapshot = reopened!.GetSnapshot();
            Equal(2, snapshot.InstalledResourcePackCount);
            Equal(2, snapshot.EnabledResourcePackCount);
            True(snapshot.ResourcePackSelectionValid);
            True(reopened.TryOpenEnabledUserResourcePacks(
                out UserResourcePackStackLease? warm, out var warmError), warmError.ToString());
            using (warm) Equal(b, warm!.Identities[1]);
        }

        System.IO.File.WriteAllBytes(
            Path.Combine(root, "resource-packs", "selection", "enabled.selection"),
            "corrupt selection"u8.ToArray());
        True(ContentCache.TryOpen(root, out ContentCache? corrupt, out var corruptOpen),
            corruptOpen.ToString());
        using (corrupt)
        {
            ContentCacheSnapshot failedClosed = corrupt!.GetSnapshot();
            False(failedClosed.ResourcePackSelectionValid);
            Equal(0, failedClosed.EnabledResourcePackCount);
            False(corrupt.TryOpenEnabledUserResourcePacks(out _, out var corruptSelection));
            Equal(ContentCacheError.CorruptResourcePackSelection, corruptSelection);
            True(corrupt.TrySetEnabledUserResourcePacks([], out var repair), repair.ToString());
            True(corrupt.GetSnapshot().ResourcePackSelectionValid);
            True(corrupt.TrySetEnabledUserResourcePacks([a], out var missingSelection),
                missingSelection.ToString());
        }

        System.IO.File.Delete(Path.Combine(
            root, "resource-packs", "manifests", a.ManifestHash + ".manifest"));
        True(ContentCache.TryOpen(root, out ContentCache? missing, out var missingOpen),
            missingOpen.ToString());
        using (missing)
        {
            True(missing!.GetSnapshot().ResourcePackSelectionValid);
            False(missing.TryOpenEnabledUserResourcePacks(out _, out var missingPack));
            Equal(ContentCacheError.MissingResourcePack, missingPack);
            True(missing.TrySetEnabledUserResourcePacks([b], out var selectBlobFailure),
                selectBlobFailure.ToString());
        }
        ContentHash missingBlobHash = packB.Manifest.Files[0].Sha256;
        string missingBlobText = missingBlobHash.ToString();
        System.IO.File.Delete(Path.Combine(
            root, "blobs", "sha256", missingBlobText[..2], missingBlobText + ".blob"));
        True(ContentCache.TryOpen(root, out ContentCache? corruptBlob, out var corruptBlobOpen),
            corruptBlobOpen.ToString());
        using (corruptBlob)
        {
            False(corruptBlob!.TryOpenEnabledUserResourcePacks(out _, out var missingBlob));
            Equal(ContentCacheError.CorruptBlob, missingBlob);
            True(corruptBlob.TrySetEnabledUserResourcePacks([], out var disableMissing),
                disableMissing.ToString());
        }
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        if (Directory.Exists(sourceRoot)) Directory.Delete(sourceRoot, recursive: true);
    }
}

static void TextureMaterialPresentationUsesEffectiveUserLeafAndStableIdentity()
{
    const string textureIdValue = "cabin:texture/wall";
    const string materialIdValue = "cabin:material/wall";
    LogicalContentId textureId = Logical(textureIdValue);
    LogicalContentId materialId = Logical(materialIdValue);
    PackageFixture lower = CreateTextureMaterialPackage(
        "cabin.presentation", "cabin", textureIdValue, materialIdValue, 0x21);
    PackageFixture user = UserTexturePackage(
        "user.cabin.wall", textureIdValue, "textures/user-wall.png", 0x91);
    PackageFixture ineffective = UserTexturePackage(
        "user.other.texture", "other:texture/wall", "textures/other.png", 0x44);
    UserResourcePackIdentity userIdentity = UserIdentity(user);
    UserResourcePackIdentity ineffectiveIdentity = UserIdentity(ineffective);
    ContentActivationPlan plan = ScopePlan(
        ContentScopeKey.Local,
        new ContentActivationId(0xF16, 1),
        ContentBaseCatalog.Empty.Fingerprint,
        [Required(lower.Manifest)]);
    ContentScopeActivationKey key = new(plan.ScopeActivation, plan.PlanHash);

    WithCacheRoot(root =>
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var open), open.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(
                ContentScopeKey.Local, lower.Manifest, lower.Payloads,
                out _, out var install), install.ToString());
            foreach (PackageFixture pack in new[] { user, ineffective })
                True(cache.TryInstallUserResourcePack(
                    pack.Manifest, pack.Payloads, out _, out var userInstall),
                    userInstall.ToString());

            PresentationGeneration lowerGeneration;
            True(cache.TryPublishCatalog(
                ContentBaseCatalog.Empty, plan, false,
                out ContentCatalogLease? lowerCatalog, out var publish), publish.ToString());
            lowerGeneration = PresentationGeneration.CreateWithResources(
                null, null, null, null, lowerCatalog);
            Equal(PresentationSourceKind.Local, lowerGeneration.GetSource(textureId));
            Equal(PresentationSourceKind.Local, lowerGeneration.GetSource(materialId));
            Equal(1, lowerGeneration.Snapshot.TextureSelections);
            Equal(1, lowerGeneration.Snapshot.MaterialSelections);
            True(lowerGeneration.TryResolveMaterialPresentation(
                materialId, out ResolvedMaterialPresentation? lowerMaterial,
                out var lowerError), lowerError.ToString());
            Equal(PresentationSourceKind.Local, lowerMaterial!.AlbedoTexture!.Source);
            ContentHash lowerTextureHash = lowerMaterial.AlbedoTexture.File.Sha256;
            ContentHash lowerFingerprint = lowerGeneration.StableFingerprint;
            float lowerRoughness = lowerMaterial.Definition.Roughness;
            lowerMaterial.Dispose();

            True(cache.TrySetEnabledUserResourcePacks(
                [userIdentity], out var select), select.ToString());
            True(cache.TryOpenEnabledUserResourcePacks(
                out UserResourcePackStackLease? userStack, out var openUser), openUser.ToString());
            True(cache.TryOpenCatalog(
                ContentBaseCatalog.Empty, key,
                out ContentCatalogLease? userCatalog, out var openCatalog), openCatalog.ToString());
            using PresentationGeneration overridden = PresentationGeneration.CreateWithResources(
                userStack, null, null, null, userCatalog);
            True(overridden.TryResolveMaterialPresentation(
                materialId, out ResolvedMaterialPresentation? userMaterial,
                out var userError), userError.ToString());
            Equal(materialId, userMaterial!.LogicalId);
            Equal(materialId, lowerMaterial?.LogicalId ?? materialId);
            Equal(lowerRoughness, userMaterial.Definition.Roughness);
            Equal(PresentationSourceKind.Local, userMaterial.Source);
            Equal(PresentationSourceKind.User, userMaterial.AlbedoTexture!.Source);
            Equal(0, overridden.Snapshot.UserSelections);
            Equal(1, overridden.Snapshot.UserResourceSelections);
            NotEqual(lowerTextureHash, userMaterial.AlbedoTexture.File.Sha256);
            NotEqual(lowerFingerprint, overridden.StableFingerprint);
            True(overridden.TryGetUserResourcePack(textureId, out UserResourcePackIdentity winner));
            Equal(userIdentity, winner);
            userMaterial.Dispose();

            True(cache.TrySetEnabledUserResourcePacks(
                [ineffectiveIdentity], out var noOpSelect), noOpSelect.ToString());
            True(cache.TryOpenEnabledUserResourcePacks(
                out UserResourcePackStackLease? noOpStack, out var noOpOpen), noOpOpen.ToString());
            True(cache.TryOpenCatalog(
                ContentBaseCatalog.Empty, key,
                out ContentCatalogLease? noOpCatalog, out var noOpCatalogOpen),
                noOpCatalogOpen.ToString());
            using PresentationGeneration noOp = PresentationGeneration.CreateWithResources(
                noOpStack, null, null, null, noOpCatalog);
            Equal(lowerFingerprint, noOp.StableFingerprint);
            Equal(0, noOp.Snapshot.UserSelections);

            True(cache.TrySetEnabledUserResourcePacks([], out var disable), disable.ToString());
            True(cache.TryOpenCatalog(
                ContentBaseCatalog.Empty, key,
                out ContentCatalogLease? restoredCatalog, out var restoredOpen),
                restoredOpen.ToString());
            using PresentationGeneration restored = PresentationGeneration.CreateWithResources(
                null, null, null, null, restoredCatalog);
            Equal(lowerFingerprint, restored.StableFingerprint);
            NotEqual(lowerGeneration.Generation, restored.Generation);
            lowerGeneration.Dispose();

            Console.WriteLine(
                $"EVIDENCE F1.6 logical_texture={textureId} logical_material={materialId} " +
                $"lower={lowerFingerprint} user={overridden.StableFingerprint} " +
                "precedence=user>local material_scalars=stable ineffective=no-churn disable=exact");
        }
    });
}

static void UserPresentationOverlayIsTransactionalOrderedAndLeaseSafe()
{
    byte[] x = OfficialActor(OfficialLocalPresentation.HumanXPath);
    byte[] y = OfficialActor(OfficialLocalPresentation.HumanYPath);
    byte[] red = TintActor(x, 0.95, 0.04, 0.04);
    byte[] blue = TintActor(x, 0.04, 0.10, 0.95);
    byte[] incompatible = RewriteActorJson(x, static root =>
    {
        JsonArray joints = root["skins"]![0]!["joints"]!.AsArray();
        int node = joints[0]!.GetValue<int>();
        root["nodes"]![node]!["name"] = "user-incompatible-root";
    });
    PackageFixture realmX = ActorPackage(
        "realm.x", "actors", ContentTrustClass.ScopeAuthorizedPassive,
        ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        ContentDefinitionOperation.Add, "actors/realm-x.glb", x);
    PackageFixture realmY = ActorPackage(
        "realm.y", "actors", ContentTrustClass.ScopeAuthorizedPassive,
        ConceptDemoActorPresentationProfile.HumanYDefinitionId,
        ContentDefinitionOperation.Add, "actors/realm-y.glb", y);
    PackageFixture packA = UserActorPackage(
        "user.overlay", ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        "actors/red.glb", red, "1");
    PackageFixture packB = UserActorPackage(
        "user.overlay.second", ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        "actors/blue.glb", blue, "1");
    PackageFixture update = UserActorPackage(
        "user.overlay", ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        "actors/blue-update.glb", blue, "2");
    PackageFixture bad = UserActorPackage(
        "user.bad-skeleton", ConceptDemoActorPresentationProfile.HumanXDefinitionId,
        "actors/bad.glb", incompatible);
    PackageFixture unused = UserActorPackage(
        "user.other-world", "otherworld:human/x", "actors/unused.glb", red);
    UserResourcePackIdentity a = UserIdentity(packA);
    UserResourcePackIdentity b = UserIdentity(packB);
    UserResourcePackIdentity v2 = UserIdentity(update);
    UserResourcePackIdentity invalid = UserIdentity(bad);
    UserResourcePackIdentity noOp = UserIdentity(unused);
    ApplicationRealmId realm = new(0xF15, 1);
    ContentActivationPlan realmPlan = ScopePlan(
        ContentScopeKey.ForRealm(realm), new ContentActivationId(0xF15, 2),
        ContentBaseCatalog.Empty.Fingerprint,
        [Required(realmX.Manifest), Required(realmY.Manifest)]);
    ContentActivationPlan localPlan = ScopePlan(
        ContentScopeKey.Local, new ContentActivationId(0xF15, 3),
        ContentBaseCatalog.Empty.Fingerprint,
        [Required(realmX.Manifest), Required(realmY.Manifest)]);
    LogicalContentId xId = Logical(ConceptDemoActorPresentationProfile.HumanXDefinitionId);
    LogicalContentId yId = Logical(ConceptDemoActorPresentationProfile.HumanYDefinitionId);

    WithCacheRoot(root =>
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var open), open.ToString());
        using (cache)
        {
            foreach (PackageFixture package in new[] { realmX, realmY })
            {
                True(cache!.TryInstallPackage(ContentScopeKey.ForRealm(realm), package.Manifest,
                    package.Payloads, out _, out var install), install.ToString());
                True(cache.TryInstallPackage(ContentScopeKey.Local, package.Manifest,
                    package.Payloads, out _, out var localInstall), localInstall.ToString());
            }
            foreach (PackageFixture package in new[] { packA, packB, update, bad, unused })
                True(cache!.TryInstallUserResourcePack(
                    package.Manifest, package.Payloads, out _, out var install), install.ToString());

            True(cache!.TryPublishCatalog(ContentBaseCatalog.Empty, realmPlan, false,
                out ContentCatalogLease? realmCatalog, out var publish), publish.ToString());
            var semanticActivation = new AuthorizedSemanticActivation(
                realmPlan.Scope, realmPlan.Activation, realmPlan.PlanHash,
                realmCatalog!.Catalog.Fingerprint, 0xF150UL);
            True(semanticActivation.IsValid);
            using PresentationGeneration lower = PresentationGeneration.Create(realmCatalog, null);
            Equal(PresentationSourceKind.Realm, lower.GetSource(xId));
            Equal(PresentationSourceKind.Realm, lower.GetSource(yId));
            True(cache.TryPublishCatalog(ContentBaseCatalog.Empty, localPlan, false,
                out ContentCatalogLease? localCatalog, out var localPublish),
                localPublish.ToString());

            True(cache.TrySetEnabledUserResourcePacks([a, b], out var select), select.ToString());
            True(cache.TryOpenEnabledUserResourcePacks(out UserResourcePackStackLease? stack,
                out var stackError), stackError.ToString());
            PresentationGeneration current = PresentationGeneration.Create(
                stack, OpenRealmAgain(cache, realmPlan), null);
            Equal(PresentationSourceKind.User, current.GetSource(xId));
            Equal(PresentationSourceKind.Realm, current.GetSource(yId));
            Equal(1, current.Snapshot.UserSelections);
            Equal(2, current.Snapshot.EnabledUserPacks);
            True(current.TryGetUserResourcePack(xId, out UserResourcePackIdentity winner));
            Equal(b, winner);
            True(current.TryResolveActorPresentation(
                xId, ConceptDemoActorPresentationProfile.Requirements,
                out ActorPresentationContentLease? retained, out var resolved), resolved.ToString());
            Equal(packB.Manifest.ManifestHash, retained!.OwnerManifest);

            True(cache.TryOpenEnabledUserResourcePacks(out UserResourcePackStackLease? localStack,
                out var localStackError), localStackError.ToString());
            using (PresentationGeneration userOverLocal = PresentationGeneration.Create(
                localStack, null, localCatalog))
            {
                Equal(PresentationSourceKind.User, userOverLocal.GetSource(xId));
                Equal(PresentationSourceKind.Local, userOverLocal.GetSource(yId));
            }

            True(cache.TrySetEnabledUserResourcePacks([b, a], out var reorder), reorder.ToString());
            True(cache.TryOpenEnabledUserResourcePacks(out UserResourcePackStackLease? reordered,
                out var reorderOpen), reorderOpen.ToString());
            PresentationGeneration reorderedGeneration = PresentationGeneration.Create(
                reordered, OpenRealmAgain(cache, realmPlan), null);
            True(reorderedGeneration.TryGetUserResourcePack(xId, out winner));
            Equal(a, winner);
            NotEqual(current.StableFingerprint, reorderedGeneration.StableFingerprint);

            True(cache.TrySetEnabledUserResourcePacks([a, noOp], out var noOpSelect),
                noOpSelect.ToString());
            True(cache.TryOpenEnabledUserResourcePacks(out UserResourcePackStackLease? noOpStack,
                out var noOpOpen), noOpOpen.ToString());
            PresentationGeneration noOpGeneration = PresentationGeneration.Create(
                noOpStack, OpenRealmAgain(cache, realmPlan), null);
            Equal(reorderedGeneration.StableFingerprint, noOpGeneration.StableFingerprint);

            True(cache.TrySetEnabledUserResourcePacks([a], out var userOnlySelection),
                userOnlySelection.ToString());
            True(cache.TryOpenEnabledUserResourcePacks(out UserResourcePackStackLease? userOnlyStack,
                out var userOnlyOpen), userOnlyOpen.ToString());
            using (PresentationGeneration userOnly = PresentationGeneration.Create(
                userOnlyStack, null, null))
            {
                Equal(PresentationSourceKind.User, userOnly.GetSource(xId));
                Equal(PresentationSourceKind.Primitive, userOnly.GetSource(yId));
            }

            True(cache.TrySetEnabledUserResourcePacks([invalid], out var badSelection),
                badSelection.ToString());
            True(cache.TryOpenEnabledUserResourcePacks(out UserResourcePackStackLease? badStack,
                out var badOpen), badOpen.ToString());
            bool rejected = false;
            try
            {
                _ = PresentationGeneration.Create(badStack, OpenRealmAgain(cache, realmPlan), null);
            }
            catch (InvalidDataException) { rejected = true; }
            True(rejected, "An incompatible selected user artifact must reject the candidate.");
            Equal(packB.Manifest.ManifestHash, retained.OwnerManifest);

            True(cache.TrySetEnabledUserResourcePacks([v2], out var updateSelection),
                updateSelection.ToString());
            True(cache.TryOpenEnabledUserResourcePacks(out UserResourcePackStackLease? updateStack,
                out var updateOpen), updateOpen.ToString());
            using PresentationGeneration updated = PresentationGeneration.Create(
                updateStack, OpenRealmAgain(cache, realmPlan), null);
            Equal(PresentationSourceKind.User, updated.GetSource(xId));
            True(updated.TryGetUserResourcePack(xId, out winner));
            Equal(v2, winner);
            False(cache.TryRetireUserResourcePack(a, out var liveBusy));
            Equal(ContentCacheError.CacheBusy, liveBusy);
            reorderedGeneration.Dispose();
            noOpGeneration.Dispose();
            current.Dispose();
            False(cache.TryRetireUserResourcePack(b, out var nativeBusy));
            Equal(ContentCacheError.CacheBusy, nativeBusy);
            retained.Dispose();
            True(cache.TryRetireUserResourcePack(b, out var retired), retired.ToString());
            True(cache.TryRetireUserResourcePack(a, out var retiredA), retiredA.ToString());
            Equal(semanticActivation, new AuthorizedSemanticActivation(
                realmPlan.Scope, realmPlan.Activation, realmPlan.PlanHash,
                semanticActivation.ResolvedFingerprint,
                semanticActivation.ApplicationSessionEpoch));
            Console.WriteLine(
                $"EVIDENCE F1.5 pack_a={a.PackageId}@{a.ManifestHash} " +
                $"pack_b={b.PackageId}@{b.ManifestHash} " +
                $"pack_v2={v2.PackageId}@{v2.ManifestHash} " +
                $"semantic_plan={semanticActivation.PlanHash} " +
                $"semantic_catalog={semanticActivation.ResolvedFingerprint} " +
                "precedence=user>realm>local>primitive local_absent=user+primitive " +
                "reorder=pass invalid_atomic=pass immutable_update=pass live_retire=pass");
        }
    });
}

static void ActivationIdentityPreservesAuthorityOrderNotByteSource()
{
    ContentBaseCatalog baseCatalog = BaseCatalog();
    PackageFixture replace = CreateReplacePackage();
    PackageFixture add = CreateAddPackage();
    ApplicationRealmId realm = new(10, 20);
    ContentActivationId activation = new(30, 40);
    ContentActivationPackage replaceRequirement = Required(replace.Manifest);
    ContentActivationPackage addRequirement = Required(add.Manifest);

    ContentActivationPlan plan = Plan(realm, activation, baseCatalog.Fingerprint,
        [replaceRequirement, addRequirement]);
    ContentActivationPlan identical = Plan(realm, activation, baseCatalog.Fingerprint,
        [replaceRequirement, addRequirement]);
    ContentActivationPlan reversed = Plan(realm, activation, baseCatalog.Fingerprint,
        [addRequirement, replaceRequirement]);
    Equal(plan.PlanHash, identical.PlanHash);
    NotEqual(plan.PlanHash, reversed.PlanHash);
    False(ContentActivationPlan.TryCreate(realm, activation, baseCatalog.Fingerprint,
        [replaceRequirement, replaceRequirement], out _, out _));

    var local = new ContentPackageAcquisition(replace.Manifest.PackageId, replace.Manifest.ManifestHash,
        [new ContentSourceDescriptor(ContentSourceKind.Local, "packages/replace.uspkg")]);
    var mirrors = new ContentPackageAcquisition(replace.Manifest.PackageId, replace.Manifest.ManifestHash,
        [new ContentSourceDescriptor(ContentSourceKind.ServerHostedStatic, "https://game.example/content/replace"),
         new ContentSourceDescriptor(ContentSourceKind.ExternalHttps, "https://cdn.example/replace")]);
    Equal(local.Package, mirrors.Package);
    Equal(local.ManifestHash, mirrors.ManifestHash);
    Equal(plan.PlanHash, identical.PlanHash);
    False(new ContentSourceDescriptor(ContentSourceKind.ExternalHttps, "http://cdn.example/replace").IsValid);

    ContentBlobKey blobA = new(replace.Manifest.Files[0].Sha256);
    ContentBlobKey blobB = new(replace.Manifest.Files[0].Sha256);
    Equal(blobA, blobB);
    ContentRealmActivationKey realmA = new(plan.RealmActivation, plan.PlanHash);
    ContentRealmActivationKey realmB = new(
        new ContentRealmActivationId(new ApplicationRealmId(10, 21), activation), plan.PlanHash);
    True(realmA.IsValid);
    True(realmB.IsValid);
    NotEqual(realmA, realmB);
    Equal(plan.RealmActivation, reversed.RealmActivation);
    NotEqual(plan.PlanHash, reversed.PlanHash);
}

static void DefaultReplaceAddResolveDeterministically()
{
    PackageFixture basePackage = CreateBasePackage();
    ContentBaseCatalog baseCatalog = BaseCatalog(basePackage);
    PackageFixture replace = CreateReplacePackage();
    PackageFixture add = CreateAddPackage();
    ContentActivationPlan plan = Plan(new ApplicationRealmId(1, 2), new ContentActivationId(3, 4),
        baseCatalog.Fingerprint, [Required(replace.Manifest), Required(add.Manifest)]);

    bool firstResolved = ContentCatalogResolver.TryResolve(baseCatalog, plan, [Verified(add), Verified(replace)],
        out ResolvedContentCatalog? first, out ContentCatalogResolutionError firstError);
    True(firstResolved, firstError.ToString());
    Equal(ContentCatalogResolutionError.None, firstError);
    bool secondResolved = ContentCatalogResolver.TryResolve(baseCatalog, plan, [Verified(replace), Verified(add)],
        out ResolvedContentCatalog? second, out ContentCatalogResolutionError secondError);
    True(secondResolved, secondError.ToString());
    Equal(first!.Fingerprint, second!.Fingerprint);
    NotEqual(first.Generation, second.Generation);

    True(first.TryGet(Logical("cabin:texture/wall"), out ResolvedContentEntry? wall));
    Equal(replace.Manifest.PackageId, wall!.OwnerPackageId);
    Equal(replace.Manifest.Files[0].Sha256, wall.ReferencedFiles.Single());
    True(first.TryGet(Logical("cabin:material/wall"), out ResolvedContentEntry? material));
    Equal(replace.Manifest.Files[0].Sha256, material!.ReferencedFiles.Single());
    True(first.TryGet(Logical("server:texture/banner"), out ResolvedContentEntry? banner));
    Equal(add.Manifest.PackageId, banner!.OwnerPackageId);
    Equal(basePackage.Manifest.Definitions.Count + add.Manifest.Definitions.Count, first.Entries.Count);
}

static void ResolutionFailuresAreTransactionalAndDiagnosable()
{
    ContentBaseCatalog baseCatalog = BaseCatalog();
    PackageFixture replace = CreateReplacePackage();
    ContentActivationPlan required = Plan(new ApplicationRealmId(4, 5), new ContentActivationId(6, 7),
        baseCatalog.Fingerprint, [Required(replace.Manifest)]);
    False(ContentCatalogResolver.TryResolve(baseCatalog, required, [], out ResolvedContentCatalog? missing,
        out ContentCatalogResolutionError missingError));
    Null(missing);
    Equal(ContentCatalogResolutionError.MissingRequiredPackage, missingError);

    ContentActivationPlan wrongBase = Plan(new ApplicationRealmId(4, 5), new ContentActivationId(6, 8),
        ContentHash.Compute("wrong-base"u8), []);
    False(ContentCatalogResolver.TryResolve(baseCatalog, wrongBase, [], out _,
        out ContentCatalogResolutionError baseError));
    Equal(ContentCatalogResolutionError.BaseCatalogMismatch, baseError);

    PackageFixture addCollision = CreateSingleTexturePackage("collision.package", "cabin",
        "cabin:texture/wall", ContentDefinitionOperation.Add, "textures/collision.png", 0x77);
    ContentActivationPlan collisionPlan = Plan(new ApplicationRealmId(4, 5), new ContentActivationId(6, 9),
        baseCatalog.Fingerprint, [Required(addCollision.Manifest)]);
    False(ContentCatalogResolver.TryResolve(baseCatalog, collisionPlan, [Verified(addCollision)], out _,
        out ContentCatalogResolutionError collisionError));
    Equal(ContentCatalogResolutionError.AddConflictsWithExistingDefinition, collisionError);

    PackageFixture missingTarget = CreateSingleTexturePackage("replace.missing", "replace",
        "cabin:texture/not-present", ContentDefinitionOperation.Replace, "textures/missing.png", 0x66);
    ContentActivationPlan missingTargetPlan = Plan(new ApplicationRealmId(4, 5), new ContentActivationId(6, 10),
        baseCatalog.Fingerprint, [Required(missingTarget.Manifest)]);
    False(ContentCatalogResolver.TryResolve(baseCatalog, missingTargetPlan, [Verified(missingTarget)], out _,
        out ContentCatalogResolutionError targetError));
    Equal(ContentCatalogResolutionError.ReplaceTargetsMissingDefinition, targetError);
}

static void OptionalPackageFallbackAndApplicationProofAreExact()
{
    ContentBaseCatalog baseCatalog = BaseCatalog();
    PackageFixture add = CreateAddPackage();
    ContentActivationPlan optional = Plan(new ApplicationRealmId(90, 91), new ContentActivationId(92, 93),
        baseCatalog.Fingerprint,
        [new ContentActivationPackage(add.Manifest.PackageId, add.Manifest.ManifestHash,
            ContentRequirementPolicy.Optional)]);
    True(ContentCatalogResolver.TryResolve(baseCatalog, optional, [], out ResolvedContentCatalog? catalog,
        out ContentCatalogResolutionError error));
    Equal(ContentCatalogResolutionError.None, error);
    Equal(baseCatalog.Entries.Count, catalog!.Entries.Count);
    False(catalog.TryGet(Logical("server:texture/banner"), out _));

    var proof = new ContentSetAppliedProof(100, optional.Realm, optional.Activation,
        optional.PlanHash, catalog.Fingerprint);
    True(proof.IsValid);
    False((proof with { ApplicationSessionEpoch = 0 }).IsValid);
    False((proof with { PlanHash = default }).IsValid);
    Equal(ContentApplicationState.None, default(ContentApplicationState));
}

static void PersistentCacheInstallsPublishesAndReopensWithoutSource()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(100, 200);
        PackageFixture replace = CreateReplacePackage();
        PackageFixture add = CreateAddPackage();
        ContentBaseCatalog baseCatalog = BaseCatalog();
        ContentActivationPlan plan = Plan(realm, new ContentActivationId(300, 400),
            baseCatalog.Fingerprint, [Required(replace.Manifest), Required(add.Manifest)]);
        ContentHash fingerprint;
        ContentCatalogGenerationId firstGeneration;

        True(ContentCache.TryOpen(root, out ContentCache? first, out ContentCacheError openError),
            openError.ToString());
        using (first)
        {
            False(ContentCache.TryOpen(root, out _, out ContentCacheError busy));
            Equal(ContentCacheError.CacheBusy, busy);
            True(first!.TryInstallPackage(realm, replace.Manifest, replace.Payloads, out _, out var replaceError),
                replaceError.ToString());
            True(first.TryInstallPackage(realm, add.Manifest, add.Payloads, out _, out var addError),
                addError.ToString());
            True(first.TryPublishCatalog(baseCatalog, plan, persistentlyPinned: false,
                out ContentCatalogLease? lease, out var publishError), publishError.ToString());
            using (lease)
            {
                fingerprint = lease!.Catalog.Fingerprint;
                firstGeneration = lease.Catalog.Generation;
            }
            ContentActivationPlan conflicting = Plan(realm, plan.Activation, baseCatalog.Fingerprint,
                [Required(add.Manifest), Required(replace.Manifest)]);
            False(first.TryPublishCatalog(baseCatalog, conflicting, false, out _,
                out ContentCacheError conflict));
            Equal(ContentCacheError.ActivationConflict, conflict);
            ContentCacheSnapshot snapshot = first.GetSnapshot();
            Equal(2, snapshot.BlobCount);
            Equal(2, snapshot.RealmManifestCount);
            Equal(1, snapshot.RealmActivationCount);
            long manifestBytes = Directory.GetFiles(Path.Combine(root, "realms"), "*.manifest",
                SearchOption.AllDirectories).Sum(static path => new FileInfo(path).Length);
            long activationBytes = Directory.GetFiles(Path.Combine(root, "realms"), "*.activation",
                SearchOption.AllDirectories).Sum(static path => new FileInfo(path).Length);
            Console.WriteLine($"A0.2 CACHE EVIDENCE blobs={snapshot.BlobCount}/{snapshot.BlobBytes}B " +
                $"manifests={snapshot.RealmManifestCount}/{manifestBytes}B " +
                $"activations={snapshot.RealmActivationCount}/{activationBytes}B");
        }

        True(ContentCache.TryOpen(root, out ContentCache? reopened, out openError), openError.ToString());
        using (reopened)
        {
            var key = new ContentRealmActivationKey(plan.RealmActivation, plan.PlanHash);
            True(reopened!.TryOpenCatalog(baseCatalog, key, out ContentCatalogLease? lease,
                out var reopenError), reopenError.ToString());
            using (lease)
            {
                Equal(fingerprint, lease!.Catalog.Fingerprint);
                NotEqual(firstGeneration, lease.Catalog.Generation);
            }
            True(reopened.TryOpenBlob(replace.Manifest.Files[0], out ContentBlobLease? blob, out var blobError),
                blobError.ToString());
            using (blob)
                Equal((long)replace.Payloads[0].Bytes.Length, blob!.Stream.Length);
        }
    });
}

static void GlobalBlobDedupDoesNotGrantCrossRealmAuthorization()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId firstRealm = new(1, 1000);
        ApplicationRealmId secondRealm = new(2, 1000);
        ApplicationRealmId unauthorizedRealm = new(3, 1000);
        PackageFixture package = CreateReplacePackage();
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(firstRealm, package.Manifest, package.Payloads, out _, out var firstError),
                firstError.ToString());
            True(cache.TryInstallPackage(secondRealm, package.Manifest, package.Payloads, out _, out var secondError),
                secondError.ToString());
            ContentCacheSnapshot snapshot = cache.GetSnapshot();
            Equal(1, snapshot.BlobCount);
            Equal(2, snapshot.RealmManifestCount);
            True(snapshot.ReusedBlobs >= 1);
            False(cache.TryOpenPackage(unauthorizedRealm, package.Manifest.PackageId,
                package.Manifest.ManifestHash, out _, out ContentCacheError unauthorized));
            Equal(ContentCacheError.MissingManifest, unauthorized);
        }
        string[] blobs = Directory.GetFiles(Path.Combine(root, "blobs", "sha256"), "*.blob",
            SearchOption.AllDirectories);
        Equal(1, blobs.Length);
        Equal(package.Manifest.Files[0].Sha256 + ".blob", Path.GetFileName(blobs[0]));
        False(blobs[0].Contains(package.Manifest.Files[0].Path.Value!, StringComparison.Ordinal));
    });
}

static void LocalAndRealmsShareCasButNotAuthorization()
{
    WithCacheRoot(root =>
    {
        ContentScopeKey local = ContentScopeKey.Local;
        ContentScopeKey realmA = ContentScopeKey.ForRealm(new ApplicationRealmId(11, 101));
        ContentScopeKey realmB = ContentScopeKey.ForRealm(new ApplicationRealmId(12, 101));
        PackageFixture package = CreateReplacePackage();
        PackageFixture realmOnly = CreateAddPackage();
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(local, package.Manifest, package.Payloads,
                out InstalledContentPackage localInstall, out var localError), localError.ToString());
            False(cache.TryOpenPackage(realmA, package.Manifest.PackageId, package.Manifest.ManifestHash,
                out _, out ContentCacheError localDoesNotAuthorizeRealm));
            Equal(ContentCacheError.MissingManifest, localDoesNotAuthorizeRealm);
            True(cache.TryInstallPackage(realmA, package.Manifest, package.Payloads,
                out InstalledContentPackage realmAInstall, out var realmAError), realmAError.ToString());
            False(cache.TryOpenPackage(realmB, package.Manifest.PackageId, package.Manifest.ManifestHash,
                out _, out ContentCacheError realmADoesNotAuthorizeRealmB));
            Equal(ContentCacheError.MissingManifest, realmADoesNotAuthorizeRealmB);
            True(cache.TryInstallPackage(realmB, package.Manifest, package.Payloads,
                out InstalledContentPackage realmBInstall, out var realmBError), realmBError.ToString());
            True(cache.TryInstallPackage(realmA, realmOnly.Manifest, realmOnly.Payloads,
                out _, out var realmOnlyError), realmOnlyError.ToString());
            False(cache.TryOpenPackage(local, realmOnly.Manifest.PackageId, realmOnly.Manifest.ManifestHash,
                out _, out ContentCacheError realmDoesNotAuthorizeLocal));
            Equal(ContentCacheError.MissingManifest, realmDoesNotAuthorizeLocal);
            Equal(local, localInstall.Scope);
            Equal(realmA, realmAInstall.Scope);
            Equal(realmB, realmBInstall.Scope);

            ContentCacheSnapshot snapshot = cache.GetSnapshot();
            Equal(2, snapshot.BlobCount);
            Equal(3, snapshot.ScopeCount);
            Equal(1, snapshot.LocalManifestCount);
            Equal(3, snapshot.RealmManifestCount);
            True(snapshot.ReusedBlobs >= 2);

            ContentScopeKey unauthorized = ContentScopeKey.ForRealm(new ApplicationRealmId(13, 101));
            False(cache.TryOpenPackage(unauthorized, package.Manifest.PackageId,
                package.Manifest.ManifestHash, out _, out ContentCacheError missing));
            Equal(ContentCacheError.MissingManifest, missing);
        }

        string[] blobs = Directory.GetFiles(Path.Combine(root, "blobs", "sha256"), "*.blob",
            SearchOption.AllDirectories);
        Equal(2, blobs.Length);
        Equal(1, blobs.Count(path => Path.GetFileName(path) == package.Manifest.Files[0].Sha256 + ".blob"));
        Equal(1, Directory.GetFiles(Path.Combine(root, "scopes", "local", "manifests"),
            "*.manifest").Length);
        Equal(3, Directory.GetFiles(Path.Combine(root, "realms"), "*.manifest",
            SearchOption.AllDirectories).Length);
    });
}

static void ScopeCatalogsWarmReopenAndRetireWithoutDeletingSharedBytes()
{
    WithCacheRoot(root =>
    {
        ContentScopeKey local = ContentScopeKey.Local;
        ContentScopeKey realm = ContentScopeKey.ForRealm(new ApplicationRealmId(21, 201));
        ContentBaseCatalog baseCatalog = BaseCatalog();
        PackageFixture package = CreateReplacePackage();
        ContentActivationId activation = new(22, 202);
        ContentActivationPlan localPlan = ScopePlan(
            local, activation, baseCatalog.Fingerprint, [Required(package.Manifest)]);
        ContentActivationPlan realmPlan = ScopePlan(
            realm, activation, baseCatalog.Fingerprint, [Required(package.Manifest)]);
        ContentHash localFingerprint;
        ContentHash realmFingerprint;

        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(local, package.Manifest, package.Payloads, out _, out var localError),
                localError.ToString());
            True(cache.TryInstallPackage(realm, package.Manifest, package.Payloads, out _, out var realmError),
                realmError.ToString());
            True(cache.TryPublishCatalog(baseCatalog, localPlan, false,
                out ContentCatalogLease? localLease, out var localPublish), localPublish.ToString());
            True(cache.TryPublishCatalog(baseCatalog, realmPlan, false,
                out ContentCatalogLease? realmLease, out var realmPublish), realmPublish.ToString());
            using (localLease)
            using (realmLease)
            {
                Equal(local, localLease!.Catalog.Scope);
                Equal(realm, realmLease!.Catalog.Scope);
                localFingerprint = localLease.Catalog.Fingerprint;
                realmFingerprint = realmLease.Catalog.Fingerprint;
                NotEqual(localFingerprint, realmFingerprint);
                CatalogEntriesEqual(localLease.Catalog, realmLease.Catalog);
            }
        }

        True(ContentCache.TryOpen(root, out ContentCache? reopened, out openError), openError.ToString());
        using (reopened)
        {
            True(reopened!.TryOpenCatalog(baseCatalog, localPlan.ScopeActivation,
                out ContentCatalogLease? identityLocal, out _));
            using (identityLocal) Equal(localFingerprint, identityLocal!.Catalog.Fingerprint);
            True(reopened.TryOpenCatalog(baseCatalog, realmPlan.ScopeActivation,
                out ContentCatalogLease? identityRealm, out _));
            using (identityRealm) Equal(realmFingerprint, identityRealm!.Catalog.Fingerprint);
            False(reopened.TryOpenCatalog(baseCatalog, default(ContentScopeActivationId), out _, out _));
            var localKey = new ContentScopeActivationKey(localPlan.ScopeActivation, localPlan.PlanHash);
            var realmKey = new ContentScopeActivationKey(realmPlan.ScopeActivation, realmPlan.PlanHash);
            True(reopened!.TryOpenCatalog(baseCatalog, localKey,
                out ContentCatalogLease? localLease, out var localOpen), localOpen.ToString());
            True(reopened.TryOpenCatalog(baseCatalog, realmKey,
                out ContentCatalogLease? realmLease, out var realmOpen), realmOpen.ToString());
            Equal(localFingerprint, localLease!.Catalog.Fingerprint);
            Equal(realmFingerprint, realmLease!.Catalog.Fingerprint);

            False(reopened.TryRetireScope(local, out _, out ContentCacheError busy));
            Equal(ContentCacheError.CacheBusy, busy);
            localLease.Dispose();
            True(reopened.TryRetireScope(local, out ContentScopeRetirementResult retired,
                out ContentCacheError retireError), retireError.ToString());
            Equal(1, retired.ManifestsRemoved);
            Equal(1, retired.ActivationsRemoved);
            False(reopened.TryOpenPackage(local, package.Manifest.PackageId, package.Manifest.ManifestHash,
                out _, out ContentCacheError localMissing));
            Equal(ContentCacheError.MissingManifest, localMissing);
            True(reopened.TryOpenPackage(realm, package.Manifest.PackageId, package.Manifest.ManifestHash,
                out _, out ContentCacheError realmStillAuthorized), realmStillAuthorized.ToString());
            True(reopened.TryOpenBlob(package.Manifest.Files[0], out ContentBlobLease? blob,
                out ContentCacheError blobError), blobError.ToString());
            blob!.Dispose();
            realmLease.Dispose();
            ContentCacheSnapshot snapshot = reopened.GetSnapshot();
            Equal(1, snapshot.BlobCount);
            Equal(0, snapshot.LocalManifestCount);
            Equal(1, snapshot.RealmManifestCount);
            Equal(0, snapshot.LiveBlobPins);
            Equal(0, snapshot.LiveCatalogPins);
        }
    });
}

static void FailedInstallAndInterruptedStagingNeverPublish()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(4, 1000);
        PackageFixture package = CreateReplacePackage();
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            PackageFixture builtIn = CreateBasePackage();
            False(cache!.TryInstallPackage(realm, builtIn.Manifest, builtIn.Payloads, out _,
                out ContentCacheError trustError));
            Equal(ContentCacheError.UnsupportedTrustClass, trustError);
            ContentFilePayload[] corrupt = package.Payloads.ToArray();
            byte[] bytes = corrupt[0].Bytes.ToArray();
            bytes[^1] ^= 0xff;
            corrupt[0] = new ContentFilePayload(corrupt[0].Path, bytes);
            False(cache.TryInstallPackage(realm, package.Manifest, corrupt, out _, out var installError));
            Equal(ContentCacheError.PayloadValidationFailed, installError);
            Equal(0, cache.GetSnapshot().BlobCount);
            Equal(0, cache!.GetSnapshot().RealmManifestCount);
        }

        string staging = Path.Combine(root, "staging");
        string abandoned = Path.Combine(staging, "abandoned.tmp");
        System.IO.File.WriteAllBytes(abandoned, "partial"u8.ToArray());
        True(ContentCache.TryOpen(root, out ContentCache? reopened, out openError), openError.ToString());
        using (reopened)
        {
            False(System.IO.File.Exists(abandoned));
            False(reopened!.TryOpenPackage(realm, package.Manifest.PackageId,
                package.Manifest.ManifestHash, out _, out ContentCacheError missing));
            Equal(ContentCacheError.MissingManifest, missing);
        }
    });
}

static void CorruptBlobCannotReopenOrPublish()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(5, 1000);
        PackageFixture package = CreateReplacePackage();
        ContentBaseCatalog baseCatalog = BaseCatalog();
        ContentActivationPlan plan = Plan(realm, new ContentActivationId(5, 2),
            baseCatalog.Fingerprint, [Required(package.Manifest)]);
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(realm, package.Manifest, package.Payloads, out _, out var installError),
                installError.ToString());
            ContentHash hash = package.Manifest.Files[0].Sha256;
            string blobPath = CacheBlobPath(root, hash);
            byte[] corrupt = System.IO.File.ReadAllBytes(blobPath);
            corrupt[^1] ^= 0x1;
            System.IO.File.WriteAllBytes(blobPath, corrupt);

            False(cache.TryOpenPackage(realm, package.Manifest.PackageId, package.Manifest.ManifestHash,
                out _, out ContentCacheError packageError));
            Equal(ContentCacheError.CorruptBlob, packageError);
            False(cache.TryPublishCatalog(baseCatalog, plan, false, out _, out ContentCacheError catalogError));
            Equal(ContentCacheError.CorruptBlob, catalogError);
            True(cache.GetSnapshot().CorruptionsDetected >= 2);
        }
    });
}

static void CorruptCanonicalMetadataFailsClosedAtOpen()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(5, 1001);
        PackageFixture package = CreateReplacePackage();
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
            True(cache!.TryInstallPackage(realm, package.Manifest, package.Payloads, out _,
                out var installError), installError.ToString());

        string manifestPath = Directory.GetFiles(Path.Combine(root, "realms"), "*.manifest",
            SearchOption.AllDirectories).Single();
        byte[] metadata = System.IO.File.ReadAllBytes(manifestPath);
        metadata[^1] ^= 0x1;
        System.IO.File.WriteAllBytes(manifestPath, metadata);
        False(ContentCache.TryOpen(root, out _, out ContentCacheError corrupt));
        Equal(ContentCacheError.CorruptManifest, corrupt);
    });
}

static void PinnedReachabilityControlsQuotaEviction()
{
    WithCacheRoot(root =>
    {
        var limits = new ContentCacheLimits(50, 25, 8, 8, 8, 1024 * 1024);
        ApplicationRealmId realm = new(6, 1000);
        ContentBaseCatalog baseCatalog = BaseCatalog();
        PackageFixture pinned = CreateSingleTexturePackage("quota.pinned", "quota",
            "cabin:texture/wall", ContentDefinitionOperation.Replace, "textures/pinned.png", 0x41);
        PackageFixture old = CreateSingleTexturePackage("quota.old", "quota",
            "cabin:texture/wall", ContentDefinitionOperation.Replace, "textures/old.png", 0x42);
        PackageFixture recent = CreateSingleTexturePackage("quota.recent", "quota",
            "cabin:texture/wall", ContentDefinitionOperation.Replace, "textures/recent.png", 0x43);
        ContentActivationPlan pinnedPlan = Plan(realm, new ContentActivationId(6, 1),
            baseCatalog.Fingerprint, [Required(pinned.Manifest)]);

        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError, limits),
            openError.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(realm, pinned.Manifest, pinned.Payloads, out _, out var pinInstall),
                pinInstall.ToString());
            True(cache.TryPublishCatalog(baseCatalog, pinnedPlan, persistentlyPinned: true,
                out ContentCatalogLease? pinnedLease, out var publishError), publishError.ToString());
            pinnedLease!.Dispose();
            True(cache.TryInstallPackage(realm, old.Manifest, old.Payloads, out _, out var oldError),
                oldError.ToString());
            True(cache.TryInstallPackage(realm, recent.Manifest, recent.Payloads, out _, out var recentError),
                recentError.ToString());

            True(cache.TryOpenPackage(realm, pinned.Manifest.PackageId, pinned.Manifest.ManifestHash,
                out _, out var pinnedOpen), pinnedOpen.ToString());
            False(cache.TryOpenPackage(realm, old.Manifest.PackageId, old.Manifest.ManifestHash,
                out _, out ContentCacheError oldMissing));
            Equal(ContentCacheError.MissingManifest, oldMissing);
            True(cache.TryOpenPackage(realm, recent.Manifest.PackageId, recent.Manifest.ManifestHash,
                out _, out var recentOpen), recentOpen.ToString());

            True(cache.TryOpenBlob(recent.Manifest.Files[0], out ContentBlobLease? live, out var blobError),
                blobError.ToString());
            False(cache.TryEnforceQuota(out _, out ContentCacheError pinnedError));
            Equal(ContentCacheError.QuotaExceeded, pinnedError);
            live!.Dispose();
            True(cache.TryEnforceQuota(out ContentCacheEvictionResult eviction, out var evictionError),
                evictionError.ToString());
            Equal(1, eviction.BlobsRemoved);
            Equal(25L, eviction.RemainingBlobBytes);
            True(cache.TryOpenPackage(realm, pinned.Manifest.PackageId, pinned.Manifest.ManifestHash,
                out _, out pinnedOpen), pinnedOpen.ToString());
            False(cache.TryOpenPackage(realm, recent.Manifest.PackageId, recent.Manifest.ManifestHash,
                out _, out ContentCacheError recentMissing));
            Equal(ContentCacheError.MissingManifest, recentMissing);
        }
    });
}

static void OptionalBytesDoNotRetargetPersistedCatalogImplicitly()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(7, 1000);
        ContentBaseCatalog baseCatalog = BaseCatalog();
        PackageFixture optionalPackage = CreateAddPackage();
        var optional = new ContentActivationPackage(optionalPackage.Manifest.PackageId,
            optionalPackage.Manifest.ManifestHash, ContentRequirementPolicy.Optional);
        ContentActivationPlan plan = Plan(realm, new ContentActivationId(7, 1),
            baseCatalog.Fingerprint, [optional]);
        var key = new ContentRealmActivationKey(plan.RealmActivation, plan.PlanHash);

        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            True(cache!.TryPublishCatalog(baseCatalog, plan, false, out ContentCatalogLease? initial,
                out var initialError), initialError.ToString());
            ContentHash fallbackFingerprint;
            using (initial) fallbackFingerprint = initial!.Catalog.Fingerprint;

            True(cache.TryInstallPackage(realm, optionalPackage.Manifest, optionalPackage.Payloads,
                out _, out var installError), installError.ToString());
            True(cache.TryOpenCatalog(baseCatalog, key, out ContentCatalogLease? unchanged,
                out var unchangedError), unchangedError.ToString());
            using (unchanged)
            {
                Equal(fallbackFingerprint, unchanged!.Catalog.Fingerprint);
                False(unchanged.Catalog.TryGet(Logical("server:texture/banner"), out _));
            }

            True(cache.TryPublishCatalog(baseCatalog, plan, false, out ContentCatalogLease? refreshed,
                out var refreshError), refreshError.ToString());
            using (refreshed)
            {
                NotEqual(fallbackFingerprint, refreshed!.Catalog.Fingerprint);
                True(refreshed.Catalog.TryGet(Logical("server:texture/banner"), out _));
            }
        }
    });
}

static void CacheRejectsReparsePointStorage()
{
    string root = NewCacheRoot();
    string outside = NewCacheRoot();
    try
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        cache!.Dispose();
        string link = Path.Combine(root, "blobs", "sha256", "ff");
        Directory.CreateSymbolicLink(link, outside);
        False(ContentCache.TryOpen(root, out _, out ContentCacheError unsafeError));
        Equal(ContentCacheError.UnsafePath, unsafeError);
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        if (Directory.Exists(outside)) Directory.Delete(outside, recursive: true);
    }
}

static void ManifestEnvelopeIsCanonicalAndExact()
{
    PackageFixture fixture = CreateDeltaPackage(0x72);
    True(ContentPackageManifestEnvelope.TryEncodeCanonical(fixture.Manifest, out byte[] first));
    True(ContentPackageManifestEnvelope.TryEncodeCanonical(fixture.Manifest, out byte[] second));
    True(first.AsSpan().SequenceEqual(second));
    True(ContentPackageManifestEnvelope.TryDecodeCanonical(
        first, fixture.Manifest.PackageId, fixture.Manifest.ManifestHash,
        out ContentPackageManifest? decoded));
    Equal(fixture.Manifest.ManifestHash, decoded!.ManifestHash);
    byte[] damaged = first.ToArray();
    damaged[^1] ^= 0x40;
    False(ContentPackageManifestEnvelope.TryDecodeCanonical(
        damaged, fixture.Manifest.PackageId, fixture.Manifest.ManifestHash, out _));
}

static void MirrorsAndHashDeltaConvergeWithBoundedParallelism()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(20, 1000);
        ContentBaseCatalog baseCatalog = BaseCatalog();
        PackageFixture first = CreateDeltaPackage(0x72);
        PackageFixture second = CreateDeltaPackage(0x73);
        using var source = new GranularFixtureSource([first, second],
            new Dictionary<string, GranularSourceBehavior>
            {
                ["https://unavailable.example/package/"] = GranularSourceBehavior.Unavailable,
                ["https://corrupt.example/package/"] = GranularSourceBehavior.Corrupt,
                ["https://good.example/package/"] = GranularSourceBehavior.Valid,
            });
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            ContentActivationPlan firstPlan = Plan(realm, new ContentActivationId(20, 1),
                baseCatalog.Fingerprint, [Required(first.Manifest)]);
            ContentPackageAcquisition firstAcquisition = Acquisition(first.Manifest,
                "https://unavailable.example/package/", "https://good.example/package/");
            True(ContentActivationAcquirer.TryAcquireAndPublish(
                cache!, baseCatalog, firstPlan, [firstAcquisition], source, false,
                out ContentCatalogLease? firstLease, out ContentActivationAcquisitionResult cold));
            using (firstLease)
            {
                Equal(2, cold.SourceAttempts);
                Equal(1, cold.SourcesRejected);
                Equal(2, cold.FilesFetched);
                Equal(0, cold.FilesReused);
                True(cold.PeakConcurrentFileFetches is >= 1 and <= 2);
            }

            ContentActivationPlan secondPlan = Plan(realm, new ContentActivationId(20, 2),
                baseCatalog.Fingerprint, [Required(second.Manifest)]);
            ContentPackageAcquisition secondAcquisition = Acquisition(second.Manifest,
                "https://corrupt.example/package/", "https://good.example/package/");
            True(ContentActivationAcquirer.TryAcquireAndPublish(
                cache!, baseCatalog, secondPlan, [secondAcquisition], source, false,
                out ContentCatalogLease? secondLease, out ContentActivationAcquisitionResult delta));
            using (secondLease)
            {
                Equal(1, delta.FilesFetched);
                Equal(1, delta.FilesReused);
                Equal(2, delta.SourceAttempts);
                Equal(1, delta.SourcesRejected);
                Equal((long)second.Payloads.Single(value =>
                    value.Path.Value == "textures/changed.png").Bytes.Length, delta.BytesFetched);
            }
            Console.WriteLine($"A0.5 DELTA EVIDENCE cold_files={cold.FilesFetched} " +
                $"delta_files={delta.FilesFetched} delta_reused={delta.FilesReused} " +
                $"delta_bytes={delta.BytesFetched} mirrors={cold.SourceAttempts} " +
                $"peak_parallel={cold.PeakConcurrentFileFetches}");
        }
    });

    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(20, 2000);
        ContentBaseCatalog baseCatalog = BaseCatalog();
        PackageFixture package = CreateDeltaPackage(0x76);
        using var source = new GranularFixtureSource([package],
            new Dictionary<string, GranularSourceBehavior>
            {
                ["https://first.example/package/"] = GranularSourceBehavior.Unavailable,
                ["https://second.example/package/"] = GranularSourceBehavior.Unavailable,
                ["https://third.example/package/"] = GranularSourceBehavior.Valid,
            });
        ContentActivationPlan plan = Plan(realm, new ContentActivationId(20, 3),
            baseCatalog.Fingerprint, [Required(package.Manifest)]);
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            False(ContentActivationAcquirer.TryAcquireAndPublish(cache!, baseCatalog, plan,
                [Acquisition(package.Manifest,
                    "https://first.example/package/",
                    "https://second.example/package/",
                    "https://third.example/package/")],
                source, false, out _, out ContentActivationAcquisitionResult bounded,
                new ContentActivationAcquirerLimits(2, 1)));
            Equal(2, bounded.SourceAttempts);
            Equal(2, bounded.SourcesRejected);
            Equal(0, cache!.GetSnapshot().RealmManifestCount);
        }
    });
}

static void ExactPackageAcquisitionReusesCasWithoutCatalogActivation()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(0x771, 1);
        PackageFixture package = CreateDeltaPackage(0x79);
        ContentPackageAcquisition acquisition = Acquisition(package.Manifest,
            "https://good.example/package/");
        using var source = new GranularFixtureSource([package],
            new Dictionary<string, GranularSourceBehavior>
            {
                ["https://good.example/package/"] = GranularSourceBehavior.Valid,
            });
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError),
            openError.ToString());
        using (cache)
        {
            ContentScopeKey scope = ContentScopeKey.ForRealm(realm);
            True(ContentActivationAcquirer.TryAcquireExactPackage(cache!, scope,
                acquisition, source, out VerifiedContentPackage? first,
                out ContentActivationAcquisitionResult cold));
            Equal(package.Manifest.ManifestHash, first!.ManifestHash);
            Equal(1, cold.PackagesFetched);
            True(cold.BytesFetched > 0);
            Equal(0, cache!.GetSnapshot().RealmActivationCount);

            True(ContentActivationAcquirer.TryAcquireExactPackage(cache, scope,
                acquisition, source, out VerifiedContentPackage? second,
                out ContentActivationAcquisitionResult warm));
            Equal(package.Manifest.ManifestHash, second!.ManifestHash);
            Equal(1, warm.PackagesReused);
            Equal(0L, warm.BytesFetched);
            Equal(0, warm.SourceAttempts);
            Equal(0, cache.GetSnapshot().RealmActivationCount);
            False(cache.TryOpenPackage(ContentScopeKey.ForRealm(new ApplicationRealmId(0x771, 2)),
                package.Manifest.PackageId, package.Manifest.ManifestHash, out _, out _));
        }
    });
}

static void CorruptAndInterruptedAcquisitionRepairTransactionally()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(21, 1000);
        ContentBaseCatalog baseCatalog = BaseCatalog();
        PackageFixture package = CreateDeltaPackage(0x74);
        ContentActivationPlan initialPlan = Plan(realm, new ContentActivationId(21, 1),
            baseCatalog.Fingerprint, [Required(package.Manifest)]);
        using var valid = new GranularFixtureSource([package],
            new Dictionary<string, GranularSourceBehavior>
            {
                ["https://good.example/package/"] = GranularSourceBehavior.Valid,
            });
        ContentPackageAcquisition acquisition = Acquisition(package.Manifest,
            "https://good.example/package/");
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            True(ContentActivationAcquirer.TryAcquireAndPublish(cache!, baseCatalog, initialPlan,
                [acquisition], valid, false, out ContentCatalogLease? initial, out _));
            ContentFileDescriptor damaged = package.Manifest.Files[0];
            byte[] bytes = System.IO.File.ReadAllBytes(CacheBlobPath(root, damaged.Sha256));
            bytes[^1] ^= 0x20;
            System.IO.File.WriteAllBytes(CacheBlobPath(root, damaged.Sha256), bytes);

            ContentActivationPlan repairPlan = Plan(realm, new ContentActivationId(21, 2),
                baseCatalog.Fingerprint, [Required(package.Manifest)]);
            False(ContentActivationAcquirer.TryAcquireAndPublish(cache!, baseCatalog, repairPlan,
                [acquisition], valid, false, out _, out ContentActivationAcquisitionResult busyRepair));
            Equal(ContentActivationAcquisitionStatus.InstallFailure, busyRepair.Status);
            Equal(ContentCacheError.CacheBusy, busyRepair.CacheError);
            initial!.Dispose();
            True(ContentActivationAcquirer.TryAcquireAndPublish(cache!, baseCatalog, repairPlan,
                [acquisition], valid, false, out ContentCatalogLease? repaired,
                out ContentActivationAcquisitionResult repair));
            repaired!.Dispose();
            Equal(1, repair.FilesFetched);
            Equal(1UL, cache!.GetSnapshot().BlobsRepaired);
            True(cache.TryOpenPackage(realm, package.Manifest.PackageId,
                package.Manifest.ManifestHash, out _, out var packageError), packageError.ToString());

            string manifestPath = Directory.GetFiles(Path.Combine(root, "realms"), "*.manifest",
                SearchOption.AllDirectories).Single();
            byte[] manifestBytes = System.IO.File.ReadAllBytes(manifestPath);
            manifestBytes[^1] ^= 0x10;
            System.IO.File.WriteAllBytes(manifestPath, manifestBytes);
            ContentActivationPlan manifestRepairPlan = Plan(realm, new ContentActivationId(21, 3),
                baseCatalog.Fingerprint, [Required(package.Manifest)]);
            True(ContentActivationAcquirer.TryAcquireAndPublish(cache, baseCatalog, manifestRepairPlan,
                [acquisition], valid, false, out ContentCatalogLease? manifestRepaired,
                out ContentActivationAcquisitionResult manifestRepair));
            manifestRepaired!.Dispose();
            Equal(0, manifestRepair.FilesFetched);
            Equal(2, manifestRepair.FilesReused);
            Equal(1UL, cache.GetSnapshot().ManifestsRepaired);
        }
    });

    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(21, 2000);
        ContentBaseCatalog baseCatalog = BaseCatalog();
        PackageFixture package = CreateDeltaPackage(0x75);
        ContentActivationPlan plan = Plan(realm, new ContentActivationId(21, 3),
            baseCatalog.Fingerprint, [Required(package.Manifest)]);
        using var interrupted = new GranularFixtureSource([package],
            new Dictionary<string, GranularSourceBehavior>
            {
                ["https://interrupted.example/package/"] = GranularSourceBehavior.Interrupted,
            });
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            False(ContentActivationAcquirer.TryAcquireAndPublish(cache!, baseCatalog, plan,
                [Acquisition(package.Manifest, "https://interrupted.example/package/")],
                interrupted, false, out _, out ContentActivationAcquisitionResult failed));
            Equal(ContentActivationAcquisitionStatus.SourceFailure, failed.Status);
            Equal(0, cache!.GetSnapshot().BlobCount);
            Equal(0, cache.GetSnapshot().RealmManifestCount);
            Equal(0, cache.GetSnapshot().RealmActivationCount);
        }
    });
}

static void OptionalAcquisitionFallsBackThenHotUpgradesExplicitly()
{
    WithCacheRoot(root =>
    {
        ApplicationRealmId realm = new(22, 1000);
        ContentBaseCatalog baseCatalog = BaseCatalog();
        PackageFixture package = CreateAddPackage();
        var optional = new ContentActivationPackage(package.Manifest.PackageId,
            package.Manifest.ManifestHash, ContentRequirementPolicy.Optional);
        ContentActivationPlan plan = Plan(realm, new ContentActivationId(22, 1),
            baseCatalog.Fingerprint, [optional]);
        string locator = "https://optional.example/package/";
        ContentPackageAcquisition acquisition = Acquisition(package.Manifest, locator);
        True(ContentCache.TryOpen(root, out ContentCache? cache, out var openError), openError.ToString());
        using (cache)
        {
            ContentCatalogLease? fallback = null;
            using (var unavailable = new GranularFixtureSource([package],
                       new Dictionary<string, GranularSourceBehavior>
                       {
                           [locator] = GranularSourceBehavior.Unavailable,
                       }))
            {
                True(ContentActivationAcquirer.TryAcquireAndPublish(cache!, baseCatalog, plan,
                    [acquisition], unavailable, false, out fallback,
                    out ContentActivationAcquisitionResult deferred));
                Equal(1, deferred.OptionalPackagesDeferred);
                False(fallback!.Catalog.TryGet(Logical("server:texture/banner"), out _));
            }
            using (var available = new GranularFixtureSource([package],
                       new Dictionary<string, GranularSourceBehavior>
                       {
                           [locator] = GranularSourceBehavior.Valid,
                       }))
            {
                True(ContentActivationAcquirer.TryAcquireAndPublish(cache!, baseCatalog, plan,
                    [acquisition], available, false, out ContentCatalogLease? upgraded,
                    out ContentActivationAcquisitionResult applied));
                using (upgraded)
                {
                    Equal(0, applied.OptionalPackagesDeferred);
                    True(upgraded!.Catalog.TryGet(Logical("server:texture/banner"), out _));
                    False(fallback!.Catalog.TryGet(Logical("server:texture/banner"), out _));
                }
            }
            fallback!.Dispose();
        }
    });
}

static void ResolvedPrivateDnsTargetIsRejectedAtConnectTime()
{
    PackageFixture package = CreateReplacePackage();
    var resolver = new FixedContentHostResolver(IPAddress.Loopback);
    using var source = new HttpContentPackageSource(
        resolver, certificateValidation: null,
        new ContentPackageSourceLimits(1024 * 1024, 2_000,
            allowInsecureServerHostedHttp: false, allowPrivateNetworkTargets: false));
    var request = new ContentPackageManifestFetchRequest(
        new ApplicationRealmId(23, 1000), package.Manifest.PackageId,
        package.Manifest.ManifestHash,
        new ContentSourceDescriptor(ContentSourceKind.ExternalHttps,
            "https://public-looking.example/package/"));
    False(source.TryFetchManifest(in request, out _, out ContentSourceFetchError error));
    Equal(ContentSourceFetchError.Unavailable, error);
    Equal(1, resolver.Resolutions);
    Equal(0L, source.GetSnapshot().ManifestBytesRead);
}

static void UiDefinitionArtifactResolvesOnlyThroughAppliedRealmCatalog()
{
    CompiledUiDefinition expected = CreateUiDefinition();
    PackageFixture package = CreateUiPackage(expected);
    ContentFilePath noncanonicalPath = FilePath("ui/not-the-definition-hash.usui");
    byte[] canonicalBytes = package.Payloads[0].Bytes.ToArray();
    ContentPackageManifest noncanonical = Manifest(
        "server.ui.bad-address", "server-ui", ContentTrustClass.ServerAuthorizedPassive,
        [File(noncanonicalPath, ContentFileKind.UiDefinition, canonicalBytes)], []);
    False(ContentPackageValidator.TryValidatePayloads(
        noncanonical, [new ContentFilePayload(noncanonicalPath, canonicalBytes)], out _, out _));
    ContentBaseCatalog baseCatalog = BaseCatalog();
    var realm = new ApplicationRealmId(24, 1000);
    ContentActivationPlan plan = Plan(
        realm,
        new ContentActivationId(24, 1),
        baseCatalog.Fingerprint,
        [Required(package.Manifest)]);
    string root = Path.Combine(Path.GetTempPath(),
        $"ultrasim-ui-content-{Environment.ProcessId}-{Guid.NewGuid():N}");
    try
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out ContentCacheError openError),
            openError.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(
                realm, package.Manifest, package.Payloads, out _, out ContentCacheError installError),
                installError.ToString());
            False(cache.TryOpenPackage(
                new ApplicationRealmId(24, 2), package.Manifest.PackageId,
                package.Manifest.ManifestHash, out _, out _));
            True(cache.TryPublishCatalog(
                baseCatalog, plan, false, out ContentCatalogLease? lease,
                out ContentCacheError publishError), publishError.ToString());
            using (lease)
            {
                Equal(baseCatalog.Entries.Count, lease!.Catalog.Entries.Count);
                True(ContentUiDefinitionResolver.TryResolve(
                    lease, UiDefinitionIdentity.From(expected), out CompiledUiDefinition? actual,
                    out ContentUiDefinitionResolutionError resolveError), resolveError.ToString());
                Equal(UiDefinitionIdentity.From(expected), UiDefinitionIdentity.From(actual!));
                False(ContentUiDefinitionResolver.TryResolve(
                    lease,
                    new UiDefinitionIdentity(expected.Key, checked((ushort)(expected.Version + 1)), expected.Hash),
                    out _, out ContentUiDefinitionResolutionError missing));
                Equal(ContentUiDefinitionResolutionError.InvalidArtifact, missing);
            }
            False(ContentUiDefinitionResolver.TryResolve(
                lease!, UiDefinitionIdentity.From(expected), out _,
                out ContentUiDefinitionResolutionError disposed));
            Equal(ContentUiDefinitionResolutionError.CatalogDisposed, disposed);
        }

        ContentFilePayload[] damaged = package.Payloads
            .Select(static payload => new ContentFilePayload(payload.Path, payload.Bytes.ToArray()))
            .ToArray();
        byte[] damagedBytes = damaged[0].Bytes.ToArray();
        damagedBytes[^1] ^= 0x40;
        damaged[0] = new ContentFilePayload(damaged[0].Path, damagedBytes);
        False(ContentPackageValidator.TryValidatePayloads(
            package.Manifest, damaged, out _, out _));
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static void WorldPackResolvesThroughAppliedCatalogAndWorldBinding()
{
    byte[] bytes = WorldPackBytes();
    var worldLimits = new WorldPackLimits(2 * 1024 * 1024, 4, 4);
    var contentFingerprint = new WorldContentFingerprint(2, 3);
    True(WorldPack.TryOpen(bytes, worldLimits, contentFingerprint,
        out WorldPack? expected, out WorldPackOpenError expectedError), expectedError.ToString());
    ContentFilePath path = FilePath("world/cabin.usworldpack");
    ContentPackageManifest manifest = Manifest(
        "server.world.cabin", "server-world", ContentTrustClass.ServerAuthorizedPassive,
        [File(path, ContentFileKind.WorldPack, bytes)],
        [FileDefinition("cabin:world/default", ContentDefinitionKind.WorldPack,
            ContentDefinitionOperation.Replace, path.Value!)]);
    var package = new PackageFixture(
        manifest, [new ContentFilePayload(path, bytes)]);
    ContentBaseCatalog baseCatalog = BaseCatalog();
    var realm = new ApplicationRealmId(25, 1000);
    ContentActivationPlan plan = Plan(
        realm, new ContentActivationId(25, 1), baseCatalog.Fingerprint,
        [Required(manifest)]);
    var binding = new WorldContentBinding(contentFingerprint, [1]);
    string root = NewCacheRoot();
    try
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out ContentCacheError openError),
            openError.ToString());
        using (cache)
        {
            True(cache!.TryInstallPackage(
                realm, manifest, package.Payloads, out _, out ContentCacheError installError),
                installError.ToString());
            True(cache.TryPublishCatalog(
                baseCatalog, plan, false, out ContentCatalogLease? lease,
                out ContentCacheError publishError), publishError.ToString());
            using (lease)
            {
                True(ContentWorldPackResolver.TryResolve(
                    lease!, Logical("cabin:world/default"), expected!.Fingerprint, binding,
                    out BoundWorldPack? actual, out ContentWorldPackResolutionResult result,
                    worldLimits), result.ToString());
                True(result.IsResolved);
                Equal(expected.Fingerprint, actual!.Fingerprint);
                Equal(expected.ContentFingerprint, actual.Content.Fingerprint);
                Equal(expected.ChunkCount, actual.ChunkCount);
                Equal(expected.FrozenStaticCount, actual.FrozenStaticCount);
                Equal(expected.MultiInstanceCount, actual.MultiInstanceCount);

                byte[] otherBytes = WorldPackBytes(baseHeightMillimeters: 5_000);
                True(WorldPack.TryOpen(otherBytes, worldLimits, contentFingerprint,
                    out WorldPack? other, out _));
                False(ContentWorldPackResolver.TryResolve(
                    lease!, Logical("cabin:world/default"), other!.Fingerprint, binding,
                    out _, out ContentWorldPackResolutionResult mismatch, worldLimits));
                Equal(ContentWorldPackResolutionStatus.PackFingerprintMismatch, mismatch.Status);

                var wrongBinding = new WorldContentBinding(
                    new WorldContentFingerprint(8, 9), [1]);
                False(ContentWorldPackResolver.TryResolve(
                    lease!, Logical("cabin:world/default"), expected.Fingerprint, wrongBinding,
                    out _, out ContentWorldPackResolutionResult wrongContent, worldLimits));
                Equal(ContentWorldPackResolutionStatus.InvalidArtifact, wrongContent.Status);
                Equal(WorldPackOpenError.ContentMismatch, wrongContent.OpenError);
            }
            False(ContentWorldPackResolver.TryResolve(
                lease!, Logical("cabin:world/default"), expected!.Fingerprint, binding,
                out _, out ContentWorldPackResolutionResult disposed, worldLimits));
            Equal(ContentWorldPackResolutionStatus.CatalogDisposed, disposed.Status);
        }
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }

    ContentActivationPlan defaultPlan = Plan(
        realm, new ContentActivationId(25, 2), baseCatalog.Fingerprint, []);
    string defaultRoot = NewCacheRoot();
    try
    {
        True(ContentCache.TryOpen(
            defaultRoot, out ContentCache? cache, out ContentCacheError openError),
            openError.ToString());
        using (cache)
        {
            True(cache!.TryPublishCatalog(
                baseCatalog, defaultPlan, false, out ContentCatalogLease? lease,
                out ContentCacheError publishError), publishError.ToString());
            using (lease)
            {
                False(ContentWorldPackResolver.TryResolve(
                    lease!, Logical("cabin:world/default"), expected!.Fingerprint, binding,
                    out _, out ContentWorldPackResolutionResult unselected, worldLimits));
                Equal(ContentWorldPackResolutionStatus.DefinitionNotSelected, unselected.Status);
            }
        }
    }
    finally
    {
        if (Directory.Exists(defaultRoot)) Directory.Delete(defaultRoot, recursive: true);
    }
}

static void StructuralManifestRequiresCatalogAuthorizationAndWorldFingerprint()
{
    byte[] packBytes = WorldPackBytes();
    True(WorldPack.TryOpen(packBytes, WorldPackLimits.Default,
        new WorldContentFingerprint(2, 3), out WorldPack? pack, out _));
    True(WorldStructuralManifest.TryCreate(
        [new WorldStructuralManifestEntry(new WorldPackCoord(0, 0), pack!.Fingerprint)],
        out WorldStructuralManifest? structural, out _));
    byte[] bytes = new byte[structural!.Length];
    structural.CopyTo(bytes);
    ContentFilePath path = FilePath("world/structure.usworldmanifest");
    ContentPackageManifest package = Manifest(
        "server.world.structure", "server-world", ContentTrustClass.ServerAuthorizedPassive,
        [File(path, ContentFileKind.WorldStructuralManifest, bytes)],
        [FileDefinition("server-world:world/structure", ContentDefinitionKind.WorldStructuralManifest,
            ContentDefinitionOperation.Add, path.Value!)]);
    var realm = new ApplicationRealmId(81, 1);
    ContentActivationPlan plan = Plan(realm, new ContentActivationId(81, 1),
        ContentBaseCatalog.Empty.Fingerprint, [Required(package)]);
    string root = NewCacheRoot();
    try
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out _));
        using (cache)
        {
            True(cache!.TryInstallPackage(realm, package,
                [new ContentFilePayload(path, bytes)], out _, out _));
            False(cache.TryOpenPackage(new ApplicationRealmId(81, 2),
                package.PackageId, package.ManifestHash, out _, out _));
            True(cache.TryPublishCatalog(ContentBaseCatalog.Empty, plan, false,
                out ContentCatalogLease? lease, out _));
            using (lease)
            {
                LogicalContentId id = Logical("server-world:world/structure");
                True(ContentWorldStructuralManifestResolver.TryResolve(
                    lease!, id, structural.Fingerprint,
                    out WorldStructuralManifest? opened,
                    out ContentWorldStructuralManifestResolutionStatus status));
                Equal(ContentWorldStructuralManifestResolutionStatus.None, status);
                Equal(structural.Fingerprint, opened!.Fingerprint);
                Span<byte> differentBytes = stackalloc byte[WorldStructuralManifestFormat.FingerprintBytes];
                differentBytes.Fill(0xA5);
                True(WorldStructuralManifestFingerprint.TryRead(differentBytes,
                    out WorldStructuralManifestFingerprint wrong));
                False(ContentWorldStructuralManifestResolver.TryResolve(
                    lease!, id, wrong, out _, out status));
                Equal(ContentWorldStructuralManifestResolutionStatus.FingerprintMismatch, status);
            }
        }
    }
    finally
    {
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
    }
}

static ContentBaseCatalog BaseCatalog(PackageFixture? fixture = null)
{
    PackageFixture selected = fixture ?? CreateBasePackage();
    True(ContentBaseCatalog.TryCreate([Verified(selected)], out ContentBaseCatalog? catalog,
        out ContentCatalogResolutionError error));
    Equal(ContentCatalogResolutionError.None, error);
    return catalog!;
}

static ContentActivationPlan Plan(
    ApplicationRealmId realm,
    ContentActivationId activation,
    ContentHash baseFingerprint,
    IEnumerable<ContentActivationPackage> packages)
{
    True(ContentActivationPlan.TryCreate(realm, activation, baseFingerprint, packages,
        out ContentActivationPlan? plan, out _));
    return plan!;
}

static ContentActivationPlan ScopePlan(
    ContentScopeKey scope,
    ContentActivationId activation,
    ContentHash baseFingerprint,
    IEnumerable<ContentActivationPackage> packages)
{
    True(ContentActivationPlan.TryCreate(scope, activation, baseFingerprint, packages,
        out ContentActivationPlan? plan, out _));
    return plan!;
}

static void CatalogEntriesEqual(ResolvedContentCatalog expected, ResolvedContentCatalog actual)
{
    Equal(expected.Entries.Count, actual.Entries.Count);
    for (int index = 0; index < expected.Entries.Count; index++)
    {
        ResolvedContentEntry left = expected.Entries[index];
        ResolvedContentEntry right = actual.Entries[index];
        Equal(left.Id, right.Id);
        Equal(left.Kind, right.Kind);
        Equal(left.OwnerPackageId, right.OwnerPackageId);
        Equal(left.OwnerManifestHash, right.OwnerManifestHash);
        Equal(left.Definition.DefinitionHash, right.Definition.DefinitionHash);
        True(left.ReferencedFiles.SequenceEqual(right.ReferencedFiles));
    }
}

static VerifiedContentPackage Verified(PackageFixture fixture)
{
    True(ContentPackageValidator.TryValidatePayloads(fixture.Manifest, fixture.Payloads,
        out VerifiedContentPackage? package, out string? error), error);
    return package!;
}

static ContentActivationPackage Required(ContentPackageManifest manifest) =>
    new(manifest.PackageId, manifest.ManifestHash, ContentRequirementPolicy.Required);

static PackageFixture CreateBasePackage()
{
    byte[] png = Png(0x11);
    byte[] webp = Webp(0x12);
    byte[] world = WorldPackBytes();
    ContentFilePath texturePath = FilePath("textures/default.png");
    ContentFilePath iconPath = FilePath("icons/default.webp");
    ContentFilePath worldPath = FilePath("world/cabin.usworldpack");
    ContentFileDescriptor textureFile = File(texturePath, ContentFileKind.PngTexture, png);
    ContentFileDescriptor iconFile = File(iconPath, ContentFileKind.WebpTexture, webp);
    ContentFileDescriptor worldFile = File(worldPath, ContentFileKind.WorldPack, world);
    ContentDefinitionDescriptor texture = TextureDefinition(
        "cabin:texture/wall", ContentDefinitionOperation.Add, texturePath.Value!);
    True(ContentMaterialDefinition.TryCreate(ContentMaterialFamily.StandardLit,
        Logical("cabin:texture/wall"), -0f, 0f, 0f, out ContentMaterialDefinition materialValue));
    True(ContentDefinitionDescriptor.TryCreateMaterial(Logical("cabin:material/wall"),
        ContentDefinitionOperation.Add, materialValue, out ContentDefinitionDescriptor? material));
    ContentDefinitionDescriptor worldDefinition = FileDefinition(
        "cabin:world/default", ContentDefinitionKind.WorldPack, ContentDefinitionOperation.Add,
        worldPath.Value!);
    ContentDefinitionDescriptor iconDefinition = FileDefinition(
        "cabin:icon/default", ContentDefinitionKind.Icon, ContentDefinitionOperation.Add,
        iconPath.Value!);
    ContentPackageManifest manifest = Manifest("cabin.default", "cabin", ContentTrustClass.BuiltIn,
        [worldFile, textureFile, iconFile], [worldDefinition, iconDefinition, material!, texture]);
    return new PackageFixture(manifest,
        [new ContentFilePayload(texturePath, png), new ContentFilePayload(iconPath, webp),
         new ContentFilePayload(worldPath, world)]);
}

static PackageFixture CreateReplacePackage() => CreateSingleTexturePackage(
    "server.replace", "server", "cabin:texture/wall", ContentDefinitionOperation.Replace,
    "textures/replacement.png", 0x22);

static PackageFixture CreateAddPackage()
{
    byte[] png = Png(0x33);
    ContentFilePath path = FilePath("textures/banner.png");
    ContentFileDescriptor file = File(path, ContentFileKind.PngTexture, png);
    ContentDefinitionDescriptor texture = TextureDefinition(
        "server:texture/banner", ContentDefinitionOperation.Add, path.Value!);
    True(ContentMaterialDefinition.TryCreate(ContentMaterialFamily.StandardLit,
        Logical("server:texture/banner"), 0.5f, 0.1f, 0f, out ContentMaterialDefinition materialValue));
    True(ContentDefinitionDescriptor.TryCreateMaterial(Logical("server:material/banner"),
        ContentDefinitionOperation.Add, materialValue, out ContentDefinitionDescriptor? material));
    ContentPackageManifest manifest = Manifest("server.add", "server",
        ContentTrustClass.ServerAuthorizedPassive, [file], [texture, material!]);
    return new PackageFixture(manifest, [new ContentFilePayload(path, png)]);
}

static CompiledUiDefinition CreateUiDefinition()
{
    var builder = new UiDefinitionBuilder("content.inventory-standard");
    builder.DeclareSlot("title", UiSlotValueKind.Text);
    builder.DeclareAction("close");
    builder.AddCanvas(canvas => canvas.AddVertical(
        "root",
        vertical =>
        {
            vertical.AddText("title", "title");
            vertical.AddButton("close", "close", "title");
        },
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 320, 200))));
    return UiDefinitionCompiler.Compile(builder);
}

static PackageFixture CreateUiPackage(CompiledUiDefinition definition)
{
    True(UiDefinitionArtifactCodec.TryEncodeCanonical(definition, out byte[] bytes));
    True(ContentUiDefinitionPath.TryCreate(
        UiDefinitionIdentity.From(definition), out ContentFilePath path));
    ContentPackageManifest manifest = Manifest(
        "server.ui.standard",
        "server-ui",
        ContentTrustClass.ServerAuthorizedPassive,
        [File(path, ContentFileKind.UiDefinition, bytes)],
        []);
    return new PackageFixture(manifest, [new ContentFilePayload(path, bytes)]);
}

static PackageFixture CreateDeltaPackage(byte changedMarker)
{
    byte[] commonBytes = Png(0x71);
    byte[] changedBytes = Png(changedMarker);
    ContentFilePath commonPath = FilePath("textures/common.png");
    ContentFilePath changedPath = FilePath("textures/changed.png");
    ContentDefinitionDescriptor changed = TextureDefinition(
        "cabin:texture/wall", ContentDefinitionOperation.Replace, changedPath.Value!);
    ContentDefinitionDescriptor common = TextureDefinition(
        "delta:texture/common", ContentDefinitionOperation.Add, commonPath.Value!);
    ContentPackageManifest manifest = Manifest(
        "delta.presentation", "delta", ContentTrustClass.ServerAuthorizedPassive,
        [File(commonPath, ContentFileKind.PngTexture, commonBytes),
         File(changedPath, ContentFileKind.PngTexture, changedBytes)],
        [common, changed]);
    return new PackageFixture(manifest,
        [new ContentFilePayload(commonPath, commonBytes),
         new ContentFilePayload(changedPath, changedBytes)]);
}

static ContentPackageAcquisition Acquisition(
    ContentPackageManifest manifest,
    params string[] locators) => new(
        manifest.PackageId,
        manifest.ManifestHash,
        locators.Select(static value => new ContentSourceDescriptor(
            ContentSourceKind.ExternalHttps, value)));

static PackageFixture CreateSingleTexturePackage(
    string packageId,
    string contentNamespace,
    string logicalId,
    ContentDefinitionOperation operation,
    string filePath,
    byte marker)
{
    byte[] bytes = Png(marker);
    ContentFilePath path = FilePath(filePath);
    ContentFileDescriptor file = File(path, ContentFileKind.PngTexture, bytes);
    ContentDefinitionDescriptor definition = TextureDefinition(logicalId, operation, filePath);
    ContentPackageManifest manifest = Manifest(packageId, contentNamespace,
        ContentTrustClass.ServerAuthorizedPassive, [file], [definition]);
    return new PackageFixture(manifest, [new ContentFilePayload(path, bytes)]);
}

static PackageFixture CreateTextureMaterialPackage(
    string packageId,
    string contentNamespace,
    string textureId,
    string materialId,
    byte marker)
{
    byte[] bytes = Png(marker);
    ContentFilePath path = FilePath("textures/wall.png");
    ContentFileDescriptor file = File(path, ContentFileKind.PngTexture, bytes);
    ContentDefinitionDescriptor texture = TextureDefinition(
        textureId, ContentDefinitionOperation.Add, path.Value!);
    True(ContentMaterialDefinition.TryCreate(
        ContentMaterialFamily.StandardLit, Logical(textureId),
        0.82f, 0.07f, 0.03f, out ContentMaterialDefinition material));
    True(ContentDefinitionDescriptor.TryCreateMaterial(
        Logical(materialId), ContentDefinitionOperation.Add,
        material, out ContentDefinitionDescriptor? materialDefinition));
    return new PackageFixture(
        Manifest(packageId, contentNamespace, ContentTrustClass.ScopeAuthorizedPassive,
            [file], [texture, materialDefinition!]),
        [new ContentFilePayload(path, bytes)]);
}

static PackageFixture UserTexturePackage(
    string packageId,
    string logicalId,
    string filePath,
    byte marker)
{
    byte[] bytes = Png(marker);
    ContentFilePath path = FilePath(filePath);
    ContentFileDescriptor file = File(path, ContentFileKind.PngTexture, bytes);
    ContentDefinitionDescriptor texture = TextureDefinition(
        logicalId, ContentDefinitionOperation.Replace, filePath);
    True(ContentPackageManifest.TryCreateUserResourcePack(
        1, 1, Package(packageId), Namespace("userpack"), packageId, "1.0.0",
        [file], [texture], out ContentPackageManifest? manifest, out string? error), error);
    return new PackageFixture(manifest!, [new ContentFilePayload(path, bytes)]);
}

static ContentPackageManifest Manifest(
    string package,
    string contentNamespace,
    ContentTrustClass trust,
    IEnumerable<ContentFileDescriptor> files,
    IEnumerable<ContentDefinitionDescriptor> definitions)
{
    True(ContentPackageManifest.TryCreate(1, 1, Package(package), Namespace(contentNamespace),
        package, "1.0.0", trust, files, definitions, out ContentPackageManifest? manifest, out string? error), error);
    return manifest!;
}

static PackageFixture ActorPackage(
    string package,
    string contentNamespace,
    ContentTrustClass trust,
    string logicalId,
    ContentDefinitionOperation operation,
    string filePath,
    byte[] bytes)
{
    ContentFilePath path = FilePath(filePath);
    ContentFileDescriptor file = File(path, ContentFileKind.ActorGltfBinary, bytes);
    ContentDefinitionDescriptor definition = FileDefinition(
        logicalId, ContentDefinitionKind.ActorPresentation, operation, filePath);
    return new PackageFixture(
        Manifest(package, contentNamespace, trust, [file], [definition]),
        [new ContentFilePayload(path, bytes)]);
}

static PackageFixture UserActorPackage(
    string package,
    string logicalId,
    string filePath,
    byte[] bytes,
    string version = "1")
{
    ContentFilePath path = FilePath(filePath);
    ContentFileDescriptor file = File(path, ContentFileKind.ActorGltfBinary, bytes);
    ContentDefinitionDescriptor definition = FileDefinition(
        logicalId, ContentDefinitionKind.ActorPresentation,
        ContentDefinitionOperation.Replace, filePath);
    True(ContentPackageManifest.TryCreateUserResourcePack(
        1, 1, Package(package), Namespace("userpack"), package, version,
        [file], [definition], out ContentPackageManifest? manifest, out string? error), error);
    return new PackageFixture(manifest!, [new ContentFilePayload(path, bytes)]);
}

static UserResourcePackIdentity UserIdentity(PackageFixture fixture) => new(
    fixture.Manifest.PackageId, fixture.Manifest.ManifestHash);

static byte[] TintActor(byte[] source, double red, double green, double blue) =>
    RewriteActorJson(source, root =>
    {
        JsonArray materials = root["materials"]!.AsArray();
        foreach (JsonNode? materialNode in materials)
        {
            JsonObject material = materialNode!.AsObject();
            JsonObject pbr = material["pbrMetallicRoughness"]?.AsObject() ?? new JsonObject();
            pbr["baseColorFactor"] = new JsonArray(red, green, blue, 1.0);
            material["pbrMetallicRoughness"] = pbr;
        }
    });

static byte[] OfficialActor(string path)
{
    string root = FindRepositoryRoot();
    byte[] archive = System.IO.File.ReadAllBytes(Path.Combine(
        root, "Client", "OfficialContent", OfficialLocalPresentation.ArchiveFileName));
    True(OfficialLocalPresentation.TryDecodeArchive(archive, out FetchedContentPackage? package));
    return package!.Payloads.Single(payload =>
        string.Equals(payload.Path.Value, path, StringComparison.Ordinal)).Bytes.ToArray();
}

static string FindRepositoryRoot()
{
    string? current = Directory.GetCurrentDirectory();
    while (current is not null)
    {
        if (System.IO.File.Exists(Path.Combine(current, "verify.sh"))) return current;
        current = Directory.GetParent(current)?.FullName;
    }
    throw new InvalidOperationException("The UltraSim repository root was not found.");
}

static byte[] RewriteActorJson(byte[] source, Action<JsonObject> mutate)
{
    const uint jsonChunk = 0x4E4F_534A;
    const uint binaryChunk = 0x004E_4942;
    int jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(12)));
    int jsonStart = 20;
    int binaryHeader = checked(jsonStart + jsonLength);
    int binaryLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
        source.AsSpan(binaryHeader)));
    Equal(binaryChunk, BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(binaryHeader + 4)));
    Equal(source.Length, checked(binaryHeader + 8 + binaryLength));
    JsonObject root = JsonNode.Parse(source.AsSpan(jsonStart, jsonLength))!.AsObject();
    mutate(root);
    byte[] json = JsonSerializer.SerializeToUtf8Bytes(root);
    int paddedJsonLength = checked((json.Length + 3) & ~3);
    var result = new byte[checked(12 + 8 + paddedJsonLength + 8 + binaryLength)];
    BinaryPrimitives.WriteUInt32LittleEndian(result, 0x4654_6C67);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), 2);
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(8), checked((uint)result.Length));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(12), checked((uint)paddedJsonLength));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(16), jsonChunk);
    json.CopyTo(result.AsSpan(20));
    result.AsSpan(20 + json.Length, paddedJsonLength - json.Length).Fill(0x20);
    int outputBinaryHeader = 20 + paddedJsonLength;
    BinaryPrimitives.WriteUInt32LittleEndian(
        result.AsSpan(outputBinaryHeader), checked((uint)binaryLength));
    BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(outputBinaryHeader + 4), binaryChunk);
    source.AsSpan(binaryHeader + 8, binaryLength).CopyTo(result.AsSpan(outputBinaryHeader + 8));
    return result;
}

static ContentFileDescriptor File(ContentFilePath path, ContentFileKind kind, byte[] bytes)
{
    True(ContentFileDescriptor.TryCreate(path, kind, bytes.Length, ContentHash.Compute(bytes),
        default, out ContentFileDescriptor descriptor));
    return descriptor;
}

static ContentDefinitionDescriptor TextureDefinition(
    string id,
    ContentDefinitionOperation operation,
    string path) => FileDefinition(id, ContentDefinitionKind.Texture, operation, path);

static ContentDefinitionDescriptor FileDefinition(
    string id,
    ContentDefinitionKind kind,
    ContentDefinitionOperation operation,
    string path)
{
    True(ContentDefinitionDescriptor.TryCreateFileDefinition(Logical(id), kind, operation,
        FilePath(path), out ContentDefinitionDescriptor? definition));
    return definition!;
}

static ContentPackageId Package(string value)
{
    True(ContentPackageId.TryCreate(value, out ContentPackageId id));
    return id;
}

static ContentNamespaceId Namespace(string value)
{
    True(ContentNamespaceId.TryCreate(value, out ContentNamespaceId id));
    return id;
}

static LogicalContentId Logical(string value)
{
    True(LogicalContentId.TryCreate(value, out LogicalContentId id));
    return id;
}

static ContentFilePath FilePath(string value)
{
    True(ContentFilePath.TryCreate(value, out ContentFilePath path));
    return path;
}

static byte[] Png(byte marker)
{
    byte[] bytes = new byte[25];
    new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }.CopyTo(bytes, 0);
    "IHDR"u8.CopyTo(bytes.AsSpan(12));
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), 1);
    BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), 1);
    bytes[24] = marker;
    return bytes;
}

static byte[] Webp(byte marker)
{
    byte[] bytes = new byte[30];
    "RIFF"u8.CopyTo(bytes);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), checked((uint)bytes.Length - 8));
    "WEBP"u8.CopyTo(bytes.AsSpan(8));
    "VP8X"u8.CopyTo(bytes.AsSpan(12));
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(16), 10);
    bytes[21] = marker;
    // VP8X stores width/height minus one as little-endian 24-bit integers. Zero is 1x1.
    return bytes;
}

static byte[] WorldPackBytes(int baseHeightMillimeters = 0)
{
    var builder = new WorldPackBuilder(new SpatialSpaceId(1), new WorldContentFingerprint(2, 3),
        new WorldPackLimits(2 * 1024 * 1024, 4, 4));
    var materials = Enumerable.Repeat((ushort)1, TerrainChunkFormat.MaterialCellCount).ToArray();
    True(TerrainHeightPlaneBase.TryCreate(
        baseHeightMillimeters, out TerrainHeightPlaneBase heightBase));
    var heights = new TerrainHeightOffset[TerrainChunkFormat.HeightVertexCount];
    True(builder.TryAddBaseTerrain(new SpatialChunkKey(new SpatialSpaceId(1), 0, 0), materials,
        heightBase, heights, out WorldPackBuildError addError));
    Equal(WorldPackBuildError.None, addError);
    True(builder.TryBuild(out WorldPackArtifact? artifact, out WorldPackBuildError buildError));
    Equal(WorldPackBuildError.None, buildError);
    byte[] bytes = new byte[artifact!.Length];
    artifact.CopyTo(bytes);
    return bytes;
}

static void WithCacheRoot(Action<string> body)
{
    string root = NewCacheRoot();
    try { body(root); }
    finally { if (Directory.Exists(root)) Directory.Delete(root, recursive: true); }
}

static string NewCacheRoot() =>
    Path.Combine(Path.GetTempPath(), "ultrasim-content-cache-tests", Guid.NewGuid().ToString("N"));

static string CacheBlobPath(string root, ContentHash hash)
{
    string value = hash.ToString();
    return Path.Combine(root, "blobs", "sha256", value[..2], value + ".blob");
}

static void True(bool value, string? message = null)
{
    if (!value) throw new InvalidOperationException(message ?? "Expected true.");
}

static void False(bool value)
{
    if (value) throw new InvalidOperationException("Expected false.");
}

static void Null(object? value)
{
    if (value is not null) throw new InvalidOperationException("Expected null.");
}

static void Equal<T>(T expected, T actual) where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
        throw new InvalidOperationException($"Expected '{expected}', actual '{actual}'.");
}

static void NotEqual<T>(T left, T right) where T : notnull
{
    if (EqualityComparer<T>.Default.Equals(left, right))
        throw new InvalidOperationException($"Expected values to differ, both were '{left}'.");
}

internal sealed record PackageFixture(
    ContentPackageManifest Manifest,
    ContentFilePayload[] Payloads);

internal enum GranularSourceBehavior : byte
{
    Valid = 1,
    Unavailable = 2,
    Corrupt = 3,
    Interrupted = 4,
}

internal sealed class GranularFixtureSource : IContentPackageFileSource, IDisposable
{
    private readonly Dictionary<ContentHash, PackageFixture> _packages;
    private readonly IReadOnlyDictionary<string, GranularSourceBehavior> _behaviors;
    private int _active;
    private int _peak;

    internal GranularFixtureSource(
        IEnumerable<PackageFixture> packages,
        IReadOnlyDictionary<string, GranularSourceBehavior> behaviors)
    {
        _packages = packages.ToDictionary(static value => value.Manifest.ManifestHash);
        _behaviors = behaviors;
    }

    internal int PeakConcurrency => Volatile.Read(ref _peak);

    public bool CanFetch(ContentSourceKind kind) => false;

    public bool CanFetchFiles(in ContentSourceDescriptor source) =>
        source.Kind == ContentSourceKind.ExternalHttps && _behaviors.ContainsKey(source.Locator);

    public bool TryFetch(
        in ContentPackageFetchRequest request,
        out FetchedContentPackage? package,
        out ContentSourceFetchError error)
    {
        package = null;
        error = ContentSourceFetchError.UnsupportedSource;
        return false;
    }

    public bool TryFetchManifest(
        in ContentPackageManifestFetchRequest request,
        out ContentPackageManifest? manifest,
        out ContentSourceFetchError error)
    {
        manifest = null;
        if (!_behaviors.TryGetValue(request.Source.Locator, out GranularSourceBehavior behavior) ||
            behavior == GranularSourceBehavior.Unavailable ||
            !_packages.TryGetValue(request.ManifestHash, out PackageFixture? package))
        {
            error = ContentSourceFetchError.Unavailable;
            return false;
        }
        manifest = package.Manifest;
        error = ContentSourceFetchError.None;
        return true;
    }

    public bool TryFetchFile(
        in ContentFileFetchRequest request,
        out ContentFilePayload payload,
        out ContentSourceFetchError error)
    {
        payload = default;
        if (!_behaviors.TryGetValue(request.Source.Locator, out GranularSourceBehavior behavior) ||
            behavior == GranularSourceBehavior.Unavailable ||
            !_packages.TryGetValue(request.ManifestHash, out PackageFixture? package))
        {
            error = ContentSourceFetchError.Unavailable;
            return false;
        }
        int active = Interlocked.Increment(ref _active);
        UpdatePeak(active);
        try
        {
            Thread.Sleep(20);
            if (behavior == GranularSourceBehavior.Interrupted &&
                request.File.Path.Value!.Contains("changed", StringComparison.Ordinal))
            {
                error = ContentSourceFetchError.Unavailable;
                return false;
            }
            ContentFilePath requestedPath = request.File.Path;
            ContentFilePayload expected = package.Payloads.Single(value => value.Path == requestedPath);
            if (behavior == GranularSourceBehavior.Corrupt)
            {
                byte[] damaged = expected.Bytes.ToArray();
                damaged[^1] ^= 0x40;
                payload = new ContentFilePayload(expected.Path, damaged);
            }
            else
            {
                payload = expected;
            }
            error = ContentSourceFetchError.None;
            return true;
        }
        finally
        {
            Interlocked.Decrement(ref _active);
        }
    }

    public void Dispose()
    {
    }

    private void UpdatePeak(int value)
    {
        while (true)
        {
            int current = Volatile.Read(ref _peak);
            if (current >= value || Interlocked.CompareExchange(ref _peak, value, current) == current) return;
        }
    }
}

internal sealed class FixedContentHostResolver : IContentHostResolver
{
    private readonly IPAddress[] _addresses;

    internal FixedContentHostResolver(params IPAddress[] addresses) => _addresses = addresses;

    internal int Resolutions { get; private set; }

    public ValueTask<IReadOnlyList<IPAddress>> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        Resolutions++;
        return ValueTask.FromResult<IReadOnlyList<IPAddress>>(_addresses);
    }
}
