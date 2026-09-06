using System.Buffers.Binary;
using UltraSim.Client.Content;
using UltraSim.Game.Application;
using UltraSim.Game.World;
using UltraSim.Spatial;

var tests = new (string Name, Action Body)[]
{
    ("IdentityGrammarAndDefaultsFailClosed", IdentityGrammarAndDefaultsFailClosed),
    ("ManifestIdentityIsCanonicalAndBounded", ManifestIdentityIsCanonicalAndBounded),
    ("BoundedAdmissionsStopBeforeOversizedMaterialization", BoundedAdmissionsStopBeforeOversizedMaterialization),
    ("PassivePayloadsRequireExactBytesAndAllowedEnvelopes", PassivePayloadsRequireExactBytesAndAllowedEnvelopes),
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
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}
Console.WriteLine($"All {tests.Length} Client Content tests passed.");

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
            Equal(0, cache.GetSnapshot().RealmManifestCount);
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
