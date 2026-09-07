using System.Buffers.Binary;
using System.Net;
using UltraSim.Client.Content;
using UltraSim.Game.Application;
using UltraSim.Game.World;
using UltraSim.Spatial;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("IdentityGrammarAndDefaultsFailClosed", IdentityGrammarAndDefaultsFailClosed),
    ("ManifestIdentityIsCanonicalAndBounded", ManifestIdentityIsCanonicalAndBounded),
    ("BoundedAdmissionsStopBeforeOversizedMaterialization", BoundedAdmissionsStopBeforeOversizedMaterialization),
    ("PassivePayloadsRequireExactBytesAndAllowedEnvelopes", PassivePayloadsRequireExactBytesAndAllowedEnvelopes),
    ("CanonicalPackageArchiveRoundTripsAndRejectsDamage", CanonicalPackageArchiveRoundTripsAndRejectsDamage),
    ("LocalPackageSourceIsRootedBoundedAndExact", LocalPackageSourceIsRootedBoundedAndExact),
    ("ActivationIdentityPreservesAuthorityOrderNotByteSource", ActivationIdentityPreservesAuthorityOrderNotByteSource),
    ("DefaultReplaceAddResolveDeterministically", DefaultReplaceAddResolveDeterministically),
    ("ResolutionFailuresAreTransactionalAndDiagnosable", ResolutionFailuresAreTransactionalAndDiagnosable),
    ("OptionalPackageFallbackAndApplicationProofAreExact", OptionalPackageFallbackAndApplicationProofAreExact),
    ("PersistentCacheInstallsPublishesAndReopensWithoutSource", PersistentCacheInstallsPublishesAndReopensWithoutSource),
    ("GlobalBlobDedupDoesNotGrantCrossRealmAuthorization", GlobalBlobDedupDoesNotGrantCrossRealmAuthorization),
    ("FailedInstallAndInterruptedStagingNeverPublish", FailedInstallAndInterruptedStagingNeverPublish),
    ("CorruptBlobCannotReopenOrPublish", CorruptBlobCannotReopenOrPublish),
    ("CorruptCanonicalMetadataFailsClosedAtOpen", CorruptCanonicalMetadataFailsClosedAtOpen),
    ("PinnedReachabilityControlsQuotaEviction", PinnedReachabilityControlsQuotaEviction),
    ("OptionalBytesDoNotRetargetPersistedCatalogImplicitly", OptionalBytesDoNotRetargetPersistedCatalogImplicitly),
    ("CacheRejectsReparsePointStorage", CacheRejectsReparsePointStorage),
    ("ManifestEnvelopeIsCanonicalAndExact", ManifestEnvelopeIsCanonicalAndExact),
    ("MirrorsAndHashDeltaConvergeWithBoundedParallelism", MirrorsAndHashDeltaConvergeWithBoundedParallelism),
    ("CorruptAndInterruptedAcquisitionRepairTransactionally", CorruptAndInterruptedAcquisitionRepairTransactionally),
    ("OptionalAcquisitionFallsBackThenHotUpgradesExplicitly", OptionalAcquisitionFallsBackThenHotUpgradesExplicitly),
    ("ResolvedPrivateDnsTargetIsRejectedAtConnectTime", ResolvedPrivateDnsTargetIsRejectedAtConnectTime),
    ("UiDefinitionArtifactResolvesOnlyThroughAppliedRealmCatalog", UiDefinitionArtifactResolvesOnlyThroughAppliedRealmCatalog),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} Client Content tests passed.");

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
            new ApplicationRealmId(9, 9), fixture.Manifest.PackageId,
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
    byte[] bytes = new byte[16];
    "RIFF"u8.CopyTo(bytes);
    BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 8);
    "WEBP"u8.CopyTo(bytes.AsSpan(8));
    bytes[12] = marker;
    return bytes;
}

static byte[] WorldPackBytes()
{
    var builder = new WorldPackBuilder(new SpatialSpaceId(1), new WorldContentFingerprint(2, 3),
        new WorldPackLimits(2 * 1024 * 1024, 4, 4));
    var materials = Enumerable.Repeat((ushort)1, TerrainChunkFormat.MaterialCellCount).ToArray();
    True(TerrainHeightPlaneBase.TryCreate(0, out TerrainHeightPlaneBase heightBase));
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
