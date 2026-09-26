using System.Buffers.Binary;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using Godot;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Application;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.World;
using UltraSim.Server.Runtime;
using UltraSim.Spatial;

public partial class T010PlayableWorldAcceptance : Node3D
{
    public override void _Ready()
    {
        try
        {
            var memoryFields = GodotDiagnosticProcessMemory.ParseFields(
                ["VmRSS:\t450 kB", "RssFile: 30 kB", "Bogus: 999 kB", "Swap: bad kB"],
                ["VmRSS", "RssFile", "Swap", "Absent"]);
            Expect(memoryFields.Count == 4 && memoryFields["VmRSS"] == 450 * 1024L &&
                memoryFields["RssFile"] == 30 * 1024L && memoryFields["Swap"] is null &&
                memoryFields["Absent"] is null, "Diagnostic Linux memory units/availability parsing changed.");
            RunAcceptance();
            RunJoinedInputAuthorityAcceptance();
            RunRemotePublicActorAcceptance();
            RunTemporalMotionNativeAcceptance();
            RunControlledActorOwnerPoseAcceptance();
            RunBakedBodyAnimationAcceptance();
            RunContentBackedBodyRealizationAcceptance();
            RunDerivedTextureMaterialAcceptance();
            RunTallPublicObjectPickingAcceptance();
            RunSystemChatBannerAcceptance();
            GD.Print("T0.10 GODOT PLAYABLE WORLD ACCEPTANCE PASS");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"T0.10 GODOT PLAYABLE WORLD ACCEPTANCE FAIL: {exception}");
            GetTree().Quit(1);
        }
    }

    private void RunDerivedTextureMaterialAcceptance()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        CabinLocalPresentationContent local =
            oracle.CreateLocalPresentation(CabinContentVariant.Default);
        CabinLocalContentPackage userPack = oracle.CreateUserWallTexturePack();
        CabinLocalContentPackage ineffectivePack = oracle.CreateIneffectiveUserTexturePack();
        var userIdentity = new UserResourcePackIdentity(
            userPack.Manifest.PackageId, userPack.Manifest.ManifestHash);
        var ineffectiveIdentity = new UserResourcePackIdentity(
            ineffectivePack.Manifest.PackageId, ineffectivePack.Manifest.ManifestHash);
        string root = Path.Combine(Path.GetTempPath(),
            $"ultrasim-godot-derived-f16-{System.Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            Expect(ContentCache.TryOpen(root, out ContentCache? cache, out ContentCacheError openError),
                $"The F1.6 content cache could not open: {openError}.");
            using (cache)
            {
                foreach (CabinLocalContentPackage package in local.Packages)
                    Expect(cache!.TryInstallPackage(
                            ContentScopeKey.Local, package.Manifest, package.Payloads,
                            out _, out ContentCacheError installError),
                        $"The F1.6 Local presentation package could not install: {installError}.");
                foreach (CabinLocalContentPackage pack in new[] { userPack, ineffectivePack })
                    Expect(cache!.TryInstallUserResourcePack(
                            pack.Manifest, pack.Payloads,
                            out _, out ContentCacheError installError),
                        $"The F1.6 user texture pack could not install: {installError}.");
                Expect(cache!.TryPublishCatalog(
                        ContentBaseCatalog.Empty, local.Plan, true,
                        out ContentCatalogLease? initialCatalog,
                        out ContentCacheError publishError),
                    $"The F1.6 Local presentation catalog could not publish: {publishError}.");

                var standalonePolicy = GodotDerivedResourcePolicy.ProductionStandalone("test-linux-x64");
                (long legacyTicks, long legacyAllocated) = MeasureFormerCabinMaterialPath(oracle);
                var coldGeneration = PresentationGeneration.CreateWithResources(
                    null, null, null, null, initialCatalog);
                PresentationGenerationId firstGeneration = coldGeneration.Generation;
                ContentHash stableFingerprint = coldGeneration.StableFingerprint;
                var coldStore = new GodotDerivedResourceStore(root, standalonePolicy);
                long coldAllocatedStart = GC.GetAllocatedBytesForCurrentThread();
                GodotDerivedMaterialLease coldWall = AcquireMaterial(
                    coldGeneration, coldStore, CabinContentIds.WallMaterial,
                    out ResolvedMaterialPresentation coldWallLogical);
                GodotDerivedMaterialLease coldWood = AcquireMaterial(
                    coldGeneration, coldStore, CabinContentIds.WoodMaterial,
                    out ResolvedMaterialPresentation coldWoodLogical);
                long coldAllocated = GC.GetAllocatedBytesForCurrentThread() - coldAllocatedStart;
                GodotDerivedResourceKey lowerWallKey = coldWall.Key;
                string lowerWallPath = coldStore.GetTextureArtifactPath(
                    coldStore.GetTextureKey(coldWallLogical.AlbedoTexture!));
                float lowerRoughness = coldWallLogical.Definition.Roughness;
                ContentHash lowerTextureHash = coldWallLogical.AlbedoTexture!.File.Sha256;
                coldGeneration.Dispose();
                Expect(coldWall.Material.AlbedoTexture is not null &&
                    !coldWall.Material.AlbedoTexture.GetImage().IsEmpty(),
                    "A live native material did not survive retirement of its presentation owner.");
                bool liveRetirementRejected = false;
                try { coldStore.Dispose(); }
                catch (InvalidOperationException) { liveRetirementRejected = true; }
                Expect(liveRetirementRejected,
                    "The derived store retired underneath live native presentation leases.");
                coldWood.Dispose();
                coldWall.Dispose();
                GodotDerivedResourceStoreSnapshot cold = coldStore.GetSnapshot();
                Expect(cold is
                    { TextureBuilds: 2, DiskTextureHits: 0, MaterialBuilds: 2,
                      LiveTextures: 0, LiveMaterials: 0 } &&
                    cold.DerivedBytesWritten > 0,
                    "The cold derived texture/material build did not create the exact bounded set.");
                coldStore.Dispose();

                PresentationGeneration warmGeneration = OpenCabinGeneration(cache, local, null);
                var warmStore = new GodotDerivedResourceStore(root, standalonePolicy);
                long warmAllocatedStart = GC.GetAllocatedBytesForCurrentThread();
                GodotDerivedMaterialLease warmWall = AcquireMaterial(
                    warmGeneration, warmStore, CabinContentIds.WallMaterial, out _);
                long warmAllocated = GC.GetAllocatedBytesForCurrentThread() - warmAllocatedStart;
                Expect(warmGeneration.Generation != firstGeneration &&
                    warmGeneration.StableFingerprint == stableFingerprint &&
                    warmWall.Key == lowerWallKey,
                    "Fresh process-local generation identity changed stable derived identity.");
                warmWall.Dispose();
                GodotDerivedResourceStoreSnapshot warm = warmStore.GetSnapshot();
                Expect(warm is { DiskTextureHits: 1, TextureBuilds: 0, LiveTextures: 0,
                        LiveMaterials: 0 },
                    "The warm derived reopen decoded source or rebuilt instead of using disk output.");
                warmGeneration.Dispose();
                warmStore.Dispose();

                System.IO.File.WriteAllBytes(lowerWallPath, [1, 2, 3]);
                PresentationGeneration repairedGeneration = OpenCabinGeneration(cache, local, null);
                var repairedStore = new GodotDerivedResourceStore(root, standalonePolicy);
                GodotDerivedMaterialLease repairedWall = AcquireMaterial(
                    repairedGeneration, repairedStore, CabinContentIds.WallMaterial,
                    out ResolvedMaterialPresentation repairedLogical);
                Expect(ContentHash.Compute(repairedLogical.AlbedoTexture!.Bytes.Span) == lowerTextureHash,
                    "Derived corruption affected the verified source presentation bytes.");
                repairedWall.Dispose();
                GodotDerivedResourceStoreSnapshot repaired = repairedStore.GetSnapshot();
                Expect(repaired is { CorruptArtifacts: 1, TextureBuilds: 1 },
                    "A corrupt derived artifact was not rejected and rebuilt from source.");
                repairedGeneration.Dispose();
                repairedStore.Dispose();

                GodotDerivedResourceStoreSnapshot wrongVersion = DamageAndRebuildDerived(
                    cache, local, root, standalonePolicy, lowerWallPath,
                    static bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(8, 4), 99));
                GodotDerivedResourceStoreSnapshot wrongKey = DamageAndRebuildDerived(
                    cache, local, root, standalonePolicy, lowerWallPath,
                    static bytes => bytes[12] ^= 0x80);
                Expect(wrongVersion is { CorruptArtifacts: 1, TextureBuilds: 1 } &&
                    wrongKey is { CorruptArtifacts: 1, TextureBuilds: 1 },
                    "Wrong-version/key derived artifacts did not fail closed and rebuild.");

                System.IO.File.Delete(lowerWallPath);
                PresentationGeneration deletedGeneration = OpenCabinGeneration(cache, local, null);
                var deletedStore = new GodotDerivedResourceStore(root, standalonePolicy);
                GodotDerivedMaterialLease deletedWall = AcquireMaterial(
                    deletedGeneration, deletedStore, CabinContentIds.WallMaterial, out _);
                deletedWall.Dispose();
                GodotDerivedResourceStoreSnapshot deleted = deletedStore.GetSnapshot();
                Expect(deleted is { TextureMisses: 1, TextureBuilds: 1 },
                    "Deleting disposable derived output did not cause a safe source rebuild.");
                deletedGeneration.Dispose();
                deletedStore.Dispose();

                var changedPolicy = standalonePolicy with { PolicyVersion = 2 };
                PresentationGeneration policyGeneration = OpenCabinGeneration(cache, local, null);
                var policyStore = new GodotDerivedResourceStore(root, changedPolicy);
                GodotDerivedMaterialLease policyWall = AcquireMaterial(
                    policyGeneration, policyStore, CabinContentIds.WallMaterial, out _);
                Expect(policyWall.Key != lowerWallKey,
                    "A derived policy change did not invalidate the persistent native key.");
                policyWall.Dispose();
                policyGeneration.Dispose();
                policyStore.Dispose();

                Expect(cache.TrySetEnabledUserResourcePacks(
                        [userIdentity], out ContentCacheError enableError),
                    $"The F1.6 wall pack could not be enabled: {enableError}.");
                PresentationGeneration userGeneration = OpenCabinGeneration(
                    cache, local, OpenUserStack(cache));
                var userStore = new GodotDerivedResourceStore(root, standalonePolicy);
                GodotDerivedMaterialLease userWall = AcquireMaterial(
                    userGeneration, userStore, CabinContentIds.WallMaterial,
                    out ResolvedMaterialPresentation userLogical);
                Expect(userLogical.AlbedoTexture!.Source == PresentationSourceKind.User &&
                    userLogical.LogicalId == CabinContentIds.WallMaterial &&
                    userLogical.Definition.Roughness == lowerRoughness &&
                    userLogical.AlbedoTexture.File.Sha256 != lowerTextureHash &&
                    userWall.Key != lowerWallKey &&
                    userGeneration.StableFingerprint != stableFingerprint,
                    "The user wall leaf did not change only effective texture/native identity.");
                GodotDerivedResourceKey userWallKey = userWall.Key;
                userWall.Dispose();
                userGeneration.Dispose();
                userStore.Dispose();

                Expect(cache.TrySetEnabledUserResourcePacks(
                        [ineffectiveIdentity], out ContentCacheError noOpError),
                    $"The ineffective F1.6 pack could not be selected: {noOpError}.");
                PresentationGeneration noOpGeneration = OpenCabinGeneration(
                    cache, local, OpenUserStack(cache));
                var noOpStore = new GodotDerivedResourceStore(root, standalonePolicy);
                GodotDerivedMaterialLease noOpWall = AcquireMaterial(
                    noOpGeneration, noOpStore, CabinContentIds.WallMaterial, out _);
                Expect(noOpGeneration.StableFingerprint == stableFingerprint &&
                    noOpWall.Key == lowerWallKey,
                    "An ineffective user pack churned stable or derived presentation identity.");
                noOpWall.Dispose();
                GodotDerivedResourceStoreSnapshot noOp = noOpStore.GetSnapshot();
                Expect(noOp is { DiskTextureHits: 1, TextureBuilds: 0 },
                    "An ineffective user pack rebuilt unchanged derived content.");
                noOpGeneration.Dispose();
                noOpStore.Dispose();

                Expect(cache.TrySetEnabledUserResourcePacks([], out ContentCacheError disableError),
                    $"The F1.6 wall pack could not be disabled: {disableError}.");
                PresentationGeneration restoredGeneration = OpenCabinGeneration(cache, local, null);
                var restoredStore = new GodotDerivedResourceStore(root, standalonePolicy);
                GodotDerivedMaterialLease restoredWall = AcquireMaterial(
                    restoredGeneration, restoredStore, CabinContentIds.WallMaterial, out _);
                Expect(restoredGeneration.StableFingerprint == stableFingerprint &&
                    restoredWall.Key == lowerWallKey,
                    "Disabling the wall pack did not restore the exact lower presentation.");
                restoredWall.Dispose();
                GodotDerivedResourceStoreSnapshot restored = restoredStore.GetSnapshot();
                restoredGeneration.Dispose();
                restoredStore.Dispose();

                PresentationGeneration atlasGeneration = OpenCabinGeneration(cache, local, null);
                var atlasPolicy = new GodotDerivedResourcePolicy(
                    1, "test-linux-x64", "rgba8-srgb-no-mip-v1",
                    GodotDerivedAtlasFamily.WorldOpaque);
                var atlasStore = new GodotDerivedResourceStore(root, atlasPolicy);
                GodotDerivedTextureLease atlasWall = AcquireTexture(
                    atlasGeneration, atlasStore, CabinContentIds.WallTexture);
                GodotDerivedTextureLease atlasWood = AcquireTexture(
                    atlasGeneration, atlasStore, CabinContentIds.WoodTexture);
                byte[] sourceWallPixels = atlasWall.Texture.GetImage().GetData();
                byte[] sourceWoodPixels = atlasWood.Texture.GetImage().GetData();
                Expect(GodotWorldOpaqueAtlasCandidate.TryBuildOwned(
                        [atlasWall, atlasWood], atlasPolicy,
                        out GodotWorldOpaqueAtlasCandidate? atlas, out string? atlasError),
                    $"The measured WorldOpaque atlas candidate failed: {atlasError}.");
                GodotWorldOpaqueAtlasSnapshot atlasResult;
                using (atlas)
                {
                    atlasResult = atlas!.Snapshot;
                    Expect(atlasResult is { SourceTextures: 2, Pages: 1 } &&
                        atlasResult.Occupancy == 1.0 && atlas.Regions.Count == 2,
                        "The WorldOpaque candidate did not pack the exact two-texture family.");
                    byte[] pagePixels = atlas.Page.GetImage().GetData();
                    Expect(AtlasRegionEquals(
                            pagePixels, atlasResult.Width, atlas.Regions[0], sourceWallPixels) &&
                        AtlasRegionEquals(
                            pagePixels, atlasResult.Width, atlas.Regions[1], sourceWoodPixels),
                        "Atlas regions were not pixel-identical to their standalone sources.");
                }
                GodotDerivedResourceStoreSnapshot atlasStoreSnapshot = atlasStore.GetSnapshot();
                Expect(atlasStoreSnapshot is { LiveTextures: 0, TextureBuilds: 2 },
                    "Atlas candidate source leases did not retire exactly.");
                atlasGeneration.Dispose();
                atlasStore.Dispose();

                VerifyDerivedRootRejectsReparsePoint(root, standalonePolicy);

                GD.Print(
                    $"EVIDENCE F1.6 stable={stableFingerprint} lower_key={lowerWallKey} " +
                    $"user_key={userWallKey} cold_build_ms={cold.TextureBuildMilliseconds:F6} " +
                    $"warm_load_ms={warm.TextureLoadMilliseconds:F6} cold_bytes={cold.DerivedBytesWritten} " +
                    $"former_build_ms={legacyTicks * 1_000d / Stopwatch.Frequency:F6} " +
                    $"former_alloc={legacyAllocated} cold_alloc={coldAllocated} warm_alloc={warmAllocated} " +
                    $"standalone_textures={cold.TextureBuilds} standalone_materials={cold.MaterialBuilds} " +
                    $"atlas_sources=2 atlas_pages=1 atlas_page_bytes={atlasResult.AtlasPixelBytes} " +
                    $"atlas_build_ms={atlasResult.BuildMilliseconds:F6} " +
                    $"atlas_source_artifact_bytes={atlasStoreSnapshot.DerivedBytesWritten} " +
                    $"restored_disk_hits={restored.DiskTextureHits} corruption_rebuild=pass " +
                    "version_key_rebuild=pass safe_paths=pass policy_invalidation=pass " +
                    "ineffective_no_churn=pass atlas=measured-rejected " +
                    "reason=no-observed-draw-or-material-reduction");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static PresentationGeneration OpenCabinGeneration(
        ContentCache cache,
        CabinLocalPresentationContent local,
        UserResourcePackStackLease? user)
    {
        Expect(cache.TryOpenCatalog(
                ContentBaseCatalog.Empty, local.ActivationKey,
                out ContentCatalogLease? catalog, out ContentCacheError error),
            $"The F1.6 Local resource catalog could not reopen: {error}.");
        return PresentationGeneration.CreateWithResources(
            user, null, null, null, catalog);
    }

    private static UserResourcePackStackLease OpenUserStack(ContentCache cache)
    {
        Expect(cache.TryOpenEnabledUserResourcePacks(
                out UserResourcePackStackLease? stack, out ContentCacheError error),
            $"The F1.6 user stack could not open: {error}.");
        return stack!;
    }

    private static GodotDerivedMaterialLease AcquireMaterial(
        PresentationGeneration generation,
        GodotDerivedResourceStore store,
        LogicalContentId id,
        out ResolvedMaterialPresentation logical)
    {
        Expect(generation.TryResolveMaterialPresentation(
                id, out ResolvedMaterialPresentation? resolved,
                out PresentationResourceResolutionError resolutionError),
            $"The logical material {id} could not resolve: {resolutionError}.");
        logical = resolved!;
        Expect(store.TryAcquireMaterial(
                logical, out GodotDerivedMaterialLease? lease, out string? error),
            $"The derived material {id} could not realize: {error}.");
        return lease!;
    }

    private static GodotDerivedTextureLease AcquireTexture(
        PresentationGeneration generation,
        GodotDerivedResourceStore store,
        LogicalContentId id)
    {
        Expect(generation.TryResolveTexturePresentation(
                id, out ResolvedTexturePresentation? resolved,
                out PresentationResourceResolutionError resolutionError),
            $"The logical texture {id} could not resolve: {resolutionError}.");
        Expect(store.TryAcquireTexture(
                resolved!, out GodotDerivedTextureLease? lease, out string? error),
            $"The derived texture {id} could not realize: {error}.");
        return lease!;
    }

    private static bool AtlasRegionEquals(
        ReadOnlySpan<byte> page,
        int pageWidth,
        in GodotWorldOpaqueAtlasRegion region,
        ReadOnlySpan<byte> source)
    {
        int sourceRowBytes = checked(region.Pixels.Size.X * 4);
        if (source.Length != checked(sourceRowBytes * region.Pixels.Size.Y)) return false;
        for (int row = 0; row < region.Pixels.Size.Y; row++)
        {
            ReadOnlySpan<byte> actual = page.Slice(
                checked(((region.Pixels.Position.Y + row) * pageWidth +
                    region.Pixels.Position.X) * 4), sourceRowBytes);
            if (!actual.SequenceEqual(source.Slice(row * sourceRowBytes, sourceRowBytes)))
                return false;
        }
        return true;
    }

    private static (long Ticks, long Allocated) MeasureFormerCabinMaterialPath(
        CabinContentOracle oracle)
    {
        long allocatedStart = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        var materials = new List<StandardMaterial3D>(2);
        var textures = new List<Texture2D>(2);
        var images = new List<Image>(2);
        try
        {
            foreach (LogicalContentId id in new[]
                     { CabinContentIds.WallMaterial, CabinContentIds.WoodMaterial })
            {
                Expect(oracle.Default.Catalog.TryGet(id, out ResolvedContentEntry? entry) &&
                    entry!.Kind == ContentDefinitionKind.Material &&
                    entry.ReferencedFiles.Count == 1,
                    $"The former Cabin material input {id} was unavailable.");
                Expect(oracle.TryGetPayload(
                        entry!.ReferencedFiles[0], out ReadOnlyMemory<byte> bytes),
                    $"The former Cabin material payload {id} was unavailable.");
                var image = new Image();
                Expect(image.LoadPngFromBuffer(bytes.ToArray()) == Error.Ok,
                    $"The former Cabin material input {id} could not decode.");
                ImageTexture texture = ImageTexture.CreateFromImage(image);
                ContentMaterialDefinition definition = entry!.Definition.Material;
                var material = new StandardMaterial3D
                {
                    AlbedoTexture = texture,
                    Roughness = definition.Roughness,
                    Metallic = definition.Metallic,
                };
                images.Add(image);
                textures.Add(texture);
                materials.Add(material);
            }
            long ticks = Stopwatch.GetTimestamp() - started;
            long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedStart;
            return (ticks, allocated);
        }
        finally
        {
            foreach (StandardMaterial3D material in materials) material.Dispose();
            foreach (Texture2D texture in textures) texture.Dispose();
            foreach (Image image in images) image.Dispose();
        }
    }

    private static GodotDerivedResourceStoreSnapshot DamageAndRebuildDerived(
        ContentCache cache,
        CabinLocalPresentationContent local,
        string root,
        GodotDerivedResourcePolicy policy,
        string path,
        Action<byte[]> damage)
    {
        byte[] bytes = System.IO.File.ReadAllBytes(path);
        damage(bytes);
        System.IO.File.WriteAllBytes(path, bytes);
        PresentationGeneration generation = OpenCabinGeneration(cache, local, null);
        var store = new GodotDerivedResourceStore(root, policy);
        GodotDerivedMaterialLease wall = AcquireMaterial(
            generation, store, CabinContentIds.WallMaterial, out _);
        wall.Dispose();
        GodotDerivedResourceStoreSnapshot snapshot = store.GetSnapshot();
        generation.Dispose();
        store.Dispose();
        return snapshot;
    }

    private static void VerifyDerivedRootRejectsReparsePoint(
        string root,
        GodotDerivedResourcePolicy policy)
    {
        string target = Path.Combine(root, "derived-link-target");
        string link = Path.Combine(root, "derived-link");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(link, target);
        bool rejected = false;
        try { _ = new GodotDerivedResourceStore(link, policy); }
        catch (InvalidOperationException) { rejected = true; }
        Expect(rejected, "A reparse-point derived root was accepted.");
        Directory.Delete(link);
    }

    private void RunBakedBodyAnimationAcceptance()
    {
        using OfficialVisualFixture official = CreateOfficialVisualFixture();
        ClientActorDefinitionCatalog catalog = ClientActorDefinitionCatalog.ConceptDemo;
        bool hasX = catalog.TryGet(
            GameWorldPublicPrefabIds.Character, out ClientActorDefinition xDefinition);
        bool hasY = catalog.TryGet(
            GameWorldPublicPrefabIds.CharacterMale, out ClientActorDefinition yDefinition);
        Expect(hasX && hasY,
            "The baked-body acceptance could not resolve X/Y actor definitions.");
        bool hasXPosition = WorldPosition.TryFromGlobalMillimeters(
            1_000, 1_000, 1_000, out WorldPosition xPosition);
        bool hasYPosition = WorldPosition.TryFromGlobalMillimeters(
            2_000, 1_000, 1_000, out WorldPosition yPosition);
        Expect(hasXPosition && hasYPosition,
            "The baked-body acceptance positions were invalid.");
        WorldPublicEntityId xId = new(801, 1);
        WorldPublicEntityId yId = new(802, 1);
        ClientActorPresentationChange[] additions =
        [
            new(ClientActorPresentationChangeKind.Added, xId,
                new ClientActorPresentationRecord(
                    xId, GameWorldPublicPrefabIds.Character, xPosition, WorldYaw.Zero,
                    xDefinition, "X", true, GameWorldPublicPrefabIds.IronSword,
                    ClientPublicActorVerticalPhase.Grounded)),
            new(ClientActorPresentationChangeKind.Added, yId,
                new ClientActorPresentationRecord(
                    yId, GameWorldPublicPrefabIds.CharacterMale, yPosition, WorldYaw.Zero,
                    yDefinition, "Y", true)),
        ];

        using var presenter = new GodotActorPresenter(this, catalog, 2, official.Visuals);
        presenter.Apply(additions, xPosition, 1_000);
        Expect(presenter.GetSnapshot() is
            { ActiveActors: 2, PrimitiveFallbackActors: 0, AnimatedActors: 2,
              EquippedMainHands: 1 },
            "The baked X/Y bodies did not realize as two animated non-primitive actors.");
        Node3D? xRoot = null;
        Node3D? xSocket = null;
        Skeleton3D? xSkeleton = null;
        AnimationPlayer? xAnimations = null;
        int xHandBone = -1;
        foreach (WorldPublicEntityId id in new[] { xId, yId })
        {
            Expect(presenter.TryGetActorRoot(id, out Node3D? root) && root is not null,
                "A baked actor root was not realized.");
            AnimationPlayer? player = FindDescendant<AnimationPlayer>(root!);
            Expect(player is not null && HasAnimation(player, "Idle") &&
                HasAnimation(player, "Walk") && HasAnimation(player, "Jump"),
                "A baked body did not expose the finite Idle/Walk/Jump animation set.");
            Expect(player!.RootMotionTrack.GetNameCount() != 0 &&
                RootMotionTrackHasWalkTranslation(player),
                "A baked body did not cancel imported skeleton-root translation.");
            Skeleton3D? skeleton = FindDescendant<Skeleton3D>(root!);
            Expect(skeleton is not null && skeleton.GetBoneCount() == 65,
                "A baked body did not retain its exact 65-bone destination skeleton.");
            var socket = FindNamedDescendant(root!, "MainHandSocket") as Node3D;
            Expect(socket is not null && socket.GetParent() == root,
                "A baked body did not expose an actor-relative semantic MainHand socket.");
            if (id == xId)
            {
                xRoot = root;
                xSocket = socket;
                xSkeleton = skeleton;
                xAnimations = player;
                for (int bone = 0; bone < skeleton!.GetBoneCount(); bone++)
                    if (skeleton.GetBoneName(bone).ToString().EndsWith(
                            "RightHand", StringComparison.OrdinalIgnoreCase))
                    {
                        xHandBone = bone;
                        break;
                    }
            }
            Expect(presenter.TryGetActorDiagnostics(
                    id, out GodotActorPresentationDiagnostics diagnostics) &&
                diagnostics.HasAnimationPlayer,
                "The presenter did not bind the baked body's AnimationPlayer.");
        }

        Expect(WorldPosition.TryFromGlobalMillimeters(
                1_300, 1_000, 1_000, out WorldPosition moved),
            "The baked-body walk pose was invalid.");
        ClientActorPresentationRecord movedX = additions[0].Current with { Position = moved };
        presenter.Apply(
            [new ClientActorPresentationChange(
                ClientActorPresentationChangeKind.Updated, xId, movedX)],
            moved,
            1_040);
        Expect(presenter.TryGetActorDiagnostics(
                xId, out GodotActorPresentationDiagnostics walking) &&
            walking.MotionPhase == GodotActorMotionPhase.Walk,
            "The baked X body did not consume authoritative position change as Walk presentation.");
        Expect(xRoot is not null && xSocket is not null && xSkeleton is not null &&
            xAnimations is not null && xHandBone >= 0,
            "The baked-body hand stabilization fixture was incomplete.");
        Transform3D idleHand = xRoot!.GlobalTransform.AffineInverse() *
            xSkeleton!.GlobalTransform * xSkeleton.GetBoneGlobalPose(xHandBone);
        Basis idleHandBasis = idleHand.Basis.Orthonormalized();
        Expect(xSocket!.Position.IsEqualApprox(idleHand.Origin) &&
            xSocket.Basis.IsEqualApprox(idleHandBasis) &&
            xSocket.Basis.Scale.IsEqualApprox(Vector3.One),
            "The equipped-item socket did not inherit the scale-free idle hand transform.");
        xAnimations!.Advance(0.2);
        presenter.UpdateOriginAndLifetimes(moved, 1_240);
        Transform3D walkingHand = xRoot.GlobalTransform.AffineInverse() *
            xSkeleton.GlobalTransform * xSkeleton.GetBoneGlobalPose(xHandBone);
        Basis walkingHandBasis = walkingHand.Basis.Orthonormalized();
        Expect(xSocket.Position.IsEqualApprox(walkingHand.Origin) &&
            xSocket.Basis.IsEqualApprox(walkingHandBasis) &&
            !xSocket.Basis.IsEqualApprox(idleHandBasis) &&
            xSocket.Basis.Scale.IsEqualApprox(Vector3.One),
            "Walk animation did not carry the equipped item with the complete hand transform.");
        var walkingGrip = FindNamedDescendant(xRoot, "MainHandPresentation") as Node3D;
        Vector3 expectedGripAxis = new Vector3(-0.20f, 0.34f, 0.92f).Normalized();
        Expect(walkingGrip is not null && walkingGrip.GetParent() == xSocket &&
            walkingGrip.Basis.Y.Normalized().IsEqualApprox(expectedGripAxis) &&
            walkingGrip.GlobalBasis.Y.Normalized().IsEqualApprox(
                (xRoot.GlobalBasis * walkingHandBasis * expectedGripAxis).Normalized()),
            "Animated sword assurance must compare hand-local grip, not assume a world-down blade.");
        GD.Print("EVIDENCE D0.7a baked_bodies=2 bones_each=65 clips=Idle,Walk,Jump " +
            "animated=2 primitive=0 main_hand_socket=true scale_free_hand_basis=true " +
            "walk_transition=true");
    }

    private void RunContentBackedBodyRealizationAcceptance()
    {
        byte[] archive = File.ReadAllBytes(OfficialArchivePath());
        Expect(OfficialLocalPresentation.TryDecodeArchive(
                archive, out FetchedContentPackage? official),
            "The canonical Official actor package could not be decoded.");
        byte[] xBytes = official!.Payloads.Single(payload => string.Equals(
            payload.Path.Value, OfficialLocalPresentation.HumanXPath,
            StringComparison.Ordinal)).Bytes.ToArray();
        byte[] yBytes = official.Payloads.Single(payload => string.Equals(
            payload.Path.Value, OfficialLocalPresentation.HumanYPath,
            StringComparison.Ordinal)).Bytes.ToArray();
        ActorContentFixture fixture = CreateActorContentFixture(xBytes, yBytes);
        var realm = new ApplicationRealmId(0xD07B, 1);
        string cacheRoot = Path.Combine(Path.GetTempPath(),
            $"ultrasim-godot-actor-{System.Environment.ProcessId}-{Guid.NewGuid():N}");
        try
        {
            Expect(ContentCache.TryOpen(
                    cacheRoot, out ContentCache? cache, out ContentCacheError openError),
                $"The actor-content native cache could not open: {openError}.");
            using (cache)
            {
                Expect(cache!.TryInstallPackage(
                        realm, fixture.Manifest, fixture.Payloads, out _,
                        out ContentCacheError installError),
                    $"The actor-content native package could not install: {installError}.");
                Expect(ContentActivationPlan.TryCreate(
                        realm, new ContentActivationId(0xD07B, 2),
                        ContentBaseCatalog.Empty.Fingerprint,
                        [new ContentActivationPackage(
                            fixture.Manifest.PackageId, fixture.Manifest.ManifestHash,
                            ContentRequirementPolicy.Required)],
                        out ContentActivationPlan? plan, out string? planError),
                    $"The actor-content native plan was invalid: {planError}.");
                Expect(cache.TryPublishCatalog(
                        ContentBaseCatalog.Empty, plan!, false,
                        out ContentCatalogLease? catalogLease,
                        out ContentCacheError publishError),
                    $"The actor-content native catalog could not publish: {publishError}.");
                using (catalogLease)
                {
                    using var source = new SwitchableActorContentSource(catalogLease!);
                    using var visuals = new GodotContentActorVisualResolver(source);
                    ClientActorDefinitionCatalog definitions = ClientActorDefinitionCatalog.ConceptDemo;
                    Expect(definitions.TryGet(
                            GameWorldPublicPrefabIds.Character,
                            out ClientActorDefinition xDefinition),
                        "The content-backed actor definition was unavailable.");
                    Expect(WorldPosition.TryFromGlobalMillimeters(
                            4_000, 1_000, 5_000, out WorldPosition position),
                        "The content-backed actor position was invalid.");
                    var actorId = new WorldPublicEntityId(901, 4);
                    var actor = new ClientActorPresentationRecord(
                        actorId, GameWorldPublicPrefabIds.Character, position, WorldYaw.Zero,
                        xDefinition, "Content Alice", true,
                        GameWorldPublicPrefabIds.IronSword,
                        ClientPublicActorVerticalPhase.Grounded);
                    using var presenter = new GodotActorPresenter(this, definitions, 2, visuals);

                    presenter.Apply(
                        [new ClientActorPresentationChange(
                            ClientActorPresentationChangeKind.Added, actorId, actor)],
                        position, 1_000);
                    Expect(presenter.GetSnapshot() is
                        { ActiveActors: 1, PrimitiveFallbackActors: 1, AnimatedActors: 0,
                          EquippedMainHands: 1 },
                        "Unavailable actor content did not degrade to the primitive substrate.");
                    Expect(presenter.TryGetActorRoot(actorId, out Node3D? originalRoot) &&
                        originalRoot is not null,
                        "The primitive actor root was not created.");
                    ulong rootLifetime = originalRoot!.GetInstanceId();
                    var speech = new ClientChatOccurrence(
                        71, 1, 1, 1, 100,
                        ClientChatOccurrenceKind.SpatialSpeech, ClientChatSpeechMode.Say,
                        actorId, "Content Alice", "Still the same actor.");
                    Expect(presenter.TryPresentOverhead(in speech, 71, 10_000) &&
                        presenter.TryPresentPrimaryAction(actorId, 1, 1_200),
                        "The primitive actor's retained presentation state was not established.");
                    presenter.UpdateOriginAndLifetimes(position, 1_200);

                    source.PublishRealm();
                    int replacements = presenter.RefreshResolvedVisuals();
                    GodotContentActorVisualResolverSnapshot resourceSnapshot = visuals.GetSnapshot();
                    Expect(replacements == 1,
                        "The newly available exact actor content did not replace the primitive visual: " +
                        resourceSnapshot.LastFailure);
                    Expect(presenter.TryGetActorRoot(actorId, out Node3D? upgradedRoot) &&
                        upgradedRoot is not null && upgradedRoot.GetInstanceId() == rootLifetime,
                        "Native actor realization replaced the authoritative actor presentation lifetime.");
                    Node3D retainedRoot = upgradedRoot!;
                    Expect(presenter.GetSnapshot() is
                        { ActiveActors: 1, PrimitiveFallbackActors: 0, AnimatedActors: 1,
                          EquippedMainHands: 1, VisibleOverheadLabels: 1, VisualReplacements: 1 },
                        "Native actor realization did not retain actor-local presentation state.");
                    Expect(presenter.TryGetActorDiagnostics(
                            actorId, out GodotActorPresentationDiagnostics actorDiagnostics) &&
                        actorDiagnostics.HasAnimationPlayer &&
                        !actorDiagnostics.UsesPrimitiveFallback &&
                        actorDiagnostics.PrimaryCueActive,
                        "The upgraded actor did not retain animation/action presentation state.");
                    Expect(FindNamedDescendant(retainedRoot, "Nameplate") is not null &&
                        FindNamedDescendant(retainedRoot, "OverheadSpeech") is not null &&
                        FindNamedDescendant(retainedRoot, "MainHandSocket") is not null,
                        "Visual replacement discarded an actor-owned anchor or label.");
                    Expect(resourceSnapshot is
                        { RealizedResources: 1, RealizationAttempts: 2,
                          RealizationFailures: 1, InstancesCreated: 1 } &&
                        resourceSnapshot.RetainedArtifactBytes == xBytes.Length,
                        "The lazy actor native-resource cache did not retain exactly one demanded asset.");
                    Expect(visuals.TryGetDiagnostics(
                            ClientActorVisualKind.HumanX,
                            out GodotContentActorResourceDiagnostics resource) &&
                        resource.OwnerPackage == fixture.Manifest.PackageId &&
                        resource.CatalogGeneration == catalogLease!.Catalog.Generation &&
                        resource.PresentationGeneration == source.CurrentPresentationGeneration &&
                        resource.Source == PresentationSourceKind.Realm &&
                        resource.Artifact.Bones == 65 &&
                        resource.Artifact.SemanticClips.HasFlag(
                            ActorPresentationSemanticClips.RequiredV1),
                        "The realized native actor did not retain its applied-catalog identity/profile.");

                    Expect(definitions.TryGet(
                            GameWorldPublicPrefabIds.CharacterMale,
                            out ClientActorDefinition yDefinition),
                        "The content-backed Y actor definition was unavailable.");
                    Node3D? yVisual = visuals.Instantiate(in yDefinition);
                    Expect(yVisual is not null &&
                        FindDescendant<Skeleton3D>(yVisual) is { } ySkeleton &&
                        ySkeleton.GetBoneCount() == 65 &&
                        FindDescendant<AnimationPlayer>(yVisual) is { } yAnimations &&
                        HasAnimation(yAnimations, "Idle") && HasAnimation(yAnimations, "Walk") &&
                        HasAnimation(yAnimations, "Jump"),
                        "The exact Y actor artifact did not realize through the same native cache.");
                    yVisual!.Free();
                    GodotContentActorVisualResolverSnapshot completeResources = visuals.GetSnapshot();
                    Expect(completeResources is
                        { RealizedResources: 2, RealizationAttempts: 3,
                          RealizationFailures: 1, InstancesCreated: 2,
                          LastFailure: GodotActorNativeRealizationError.None } &&
                        completeResources.RetainedArtifactBytes == xBytes.Length + yBytes.Length,
                        "The content-backed X/Y native resource set was not exact and bounded.");

                    // A policy-selected cheap body holds no hidden detailed instance across a
                    // REAL User/Realm presentation-generation replacement.
                    using var tieredPresenter = new GodotActorPresenter(this, definitions, 2, visuals);
                    WorldPosition.TryFromGlobalMillimeters(130_000, 1_000, 5_000, out var farPosition);
                    var tierId = new WorldPublicEntityId(902, 4);
                    var tierActor = actor with { Id = tierId, Position = farPosition, MainHandPrefabId = 0 };
                    tieredPresenter.Apply([new(ClientActorPresentationChangeKind.Added, tierId, tierActor)], position, 2_000);
                    Expect(tieredPresenter.GetSnapshot() is { DetailedActors: 0, BatchedPrimitiveActors: 1 } &&
                        !tieredPresenter.TryGetActorRoot(tierId, out _), "Cheap content actor retained a hidden root.");
                    byte[] userX = TintActorArtifact(xBytes);
                    ActorContentFixture userPack = CreateUserActorOverrideFixture(userX);
                    Expect(cache.TryInstallUserResourcePack(
                            userPack.Manifest, userPack.Payloads, out InstalledUserResourcePack installed,
                            out ContentCacheError userInstall),
                        $"The user actor override could not install: {userInstall}.");
                    var userIdentity = new UserResourcePackIdentity(
                        userPack.Manifest.PackageId, userPack.Manifest.ManifestHash);
                    Expect(cache.TrySetEnabledUserResourcePacks([userIdentity], out var enableError),
                        $"The user actor override could not be enabled: {enableError}.");
                    Expect(cache.TryOpenEnabledUserResourcePacks(
                            out UserResourcePackStackLease? userStack, out var userOpen),
                        $"The enabled user actor override could not open: {userOpen}.");
                    var realmKey = new ContentScopeActivationKey(
                        plan!.ScopeActivation, plan.PlanHash);
                    Expect(cache.TryOpenCatalog(
                            ContentBaseCatalog.Empty, realmKey,
                            out ContentCatalogLease? userRealm, out var userRealmOpen),
                        $"The Realm layer could not reopen for the user composition: {userRealmOpen}.");
                    source.Publish(PresentationGeneration.Create(userStack, userRealm, null));
                    int userReplacements = presenter.RefreshResolvedVisuals();
                    visuals.ReleaseRetiredResources();
                    Expect(userReplacements == 1 &&
                        presenter.TryGetActorRoot(actorId, out Node3D? userRoot) &&
                        userRoot is not null && userRoot.GetInstanceId() == rootLifetime,
                        "The user override did not hot-replace the visual under the retained actor root.");
                    Expect(visuals.TryGetDiagnostics(
                            ClientActorVisualKind.HumanX,
                            out GodotContentActorResourceDiagnostics userResource) &&
                        userResource.Source == PresentationSourceKind.User &&
                        userResource.OwnerPackage == installed.Identity.PackageId &&
                        userResource.FileHash != resource.FileHash,
                        "The native user override did not retain exact User provenance.");
                    Expect(presenter.GetSnapshot() is
                        { ActiveActors: 1, AnimatedActors: 1, EquippedMainHands: 1,
                          VisibleOverheadLabels: 1, VisualReplacements: 2 },
                        "The User hot replacement discarded actor-local presentation state.");

                    Expect(tieredPresenter.RefreshResolvedVisuals() == 0,
                        "A cheap actor needlessly instantiated the User visual offscreen.");
                    tieredPresenter.SetPresentedPose(tierId, ClientMotionPose.FromAuthority(position, WorldYaw.Zero));
                    tieredPresenter.UpdateOriginAndLifetimes(position, 3_000);
                    Expect(tieredPresenter.TryGetActorRoot(tierId, out Node3D? tierRoot) &&
                        tierRoot is not null && tieredPresenter.GetSnapshot().AnimatedActors == 1 &&
                        visuals.TryGetDiagnostics(ClientActorVisualKind.HumanX, out var promotedUser) &&
                        promotedUser.FileHash == userResource.FileHash && promotedUser.Source == PresentationSourceKind.User,
                        "Promotion resurrected pre-User actor content.");
                    tieredPresenter.SetPresentedPose(tierId, ClientMotionPose.FromAuthority(farPosition, WorldYaw.Zero));
                    tieredPresenter.UpdateOriginAndLifetimes(position, 4_000);

                    Expect(cache.TrySetEnabledUserResourcePacks([], out var disableError),
                        $"The user actor override could not be disabled: {disableError}.");
                    Expect(cache.TryOpenEnabledUserResourcePacks(
                            out UserResourcePackStackLease? emptyStack, out var emptyOpen),
                        $"The empty user selection could not open: {emptyOpen}.");
                    Expect(cache.TryOpenCatalog(
                            ContentBaseCatalog.Empty, realmKey,
                            out ContentCatalogLease? restoredRealm, out var restoreOpen),
                        $"The Realm layer could not reopen after disable: {restoreOpen}.");
                    source.Publish(PresentationGeneration.Create(emptyStack, restoredRealm, null));
                    int restoreReplacements = presenter.RefreshResolvedVisuals();
                    visuals.ReleaseRetiredResources();
                    Expect(restoreReplacements == 1 &&
                        visuals.TryGetDiagnostics(
                            ClientActorVisualKind.HumanX,
                            out GodotContentActorResourceDiagnostics restoredResource) &&
                        restoredResource.Source == PresentationSourceKind.Realm &&
                        restoredResource.FileHash == resource.FileHash,
                        "Disabling the user override did not restore the exact Realm presentation.");

                    tieredPresenter.SetPresentedPose(tierId, ClientMotionPose.FromAuthority(position, WorldYaw.Zero));
                    tieredPresenter.UpdateOriginAndLifetimes(position, 5_000);
                    Expect(tieredPresenter.GetSnapshot().AnimatedActors == 1 &&
                        visuals.TryGetDiagnostics(ClientActorVisualKind.HumanX, out var promotedRealm) &&
                        promotedRealm.FileHash == resource.FileHash && promotedRealm.Source == PresentationSourceKind.Realm,
                        "Post-disable promotion resurrected the User generation.");
                    GD.Print("F1.10 REAL CONTENT TRANSITION PASS cheap=user-replaced promotion=current-user disable=current-realm");

                    GD.Print(
                        $"EVIDENCE D0.7b actor_root_preserved=true primitive_to_content=true " +
                        $"lazy_first={resourceSnapshot.RealizedResources} " +
                        $"xy_resources={completeResources.RealizedResources} " +
                        $"retained_bytes={completeResources.RetainedArtifactBytes} " +
                        "bones_each=65 clips=Idle,Walk,Jump anchors_retained=true " +
                        "user_hot_replace=true user_disable_restore=true");
                }
            }
        }
        finally
        {
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }
    }

    private static ActorContentFixture CreateActorContentFixture(byte[] xBytes, byte[] yBytes)
    {
        string[] logicalIds =
        [
            ConceptDemoActorPresentationProfile.HumanXDefinitionId,
            ConceptDemoActorPresentationProfile.HumanYDefinitionId,
        ];
        string[] pathValues = ["actors/human-x.glb", "actors/human-y.glb"];
        byte[][] bytes = [xBytes, yBytes];
        var files = new ContentFileDescriptor[2];
        var definitions = new ContentDefinitionDescriptor[2];
        var payloads = new ContentFilePayload[2];
        for (int index = 0; index < 2; index++)
        {
            Expect(ContentFilePath.TryCreate(pathValues[index], out ContentFilePath path),
                $"The native actor fixture path {index} was invalid.");
            Expect(ContentFileDescriptor.TryCreate(
                    path, ContentFileKind.ActorGltfBinary, bytes[index].Length,
                    ContentHash.Compute(bytes[index]), default, out files[index]),
                $"The native actor fixture file {index} was invalid.");
            Expect(LogicalContentId.TryCreate(
                    logicalIds[index], out LogicalContentId logicalId),
                $"The native actor fixture logical ID {index} was invalid.");
            Expect(ContentDefinitionDescriptor.TryCreateFileDefinition(
                    logicalId, ContentDefinitionKind.ActorPresentation,
                    ContentDefinitionOperation.Add, path,
                    out ContentDefinitionDescriptor? definition),
                $"The native actor fixture descriptor {index} was invalid.");
            definitions[index] = definition!;
            payloads[index] = new ContentFilePayload(path, bytes[index]);
        }
        Expect(ContentPackageId.TryCreate(
                "test.native-actors", out ContentPackageId packageId),
            "The native actor fixture identity was invalid.");
        Expect(ContentNamespaceId.TryCreate(
                "actors", out ContentNamespaceId contentNamespace),
            "The native actor fixture namespace was invalid.");
        Expect(ContentPackageManifest.TryCreate(
                1, 1, packageId, contentNamespace,
                "Native actor realization fixture", "1.0.0",
                ContentTrustClass.ServerAuthorizedPassive, files, definitions,
                out ContentPackageManifest? manifest, out string? manifestError),
            $"The native actor fixture manifest was invalid: {manifestError}.");
        Expect(ContentPackageValidator.TryValidatePayloads(
                manifest!, payloads, out _, out string? payloadError),
            $"The native actor fixture payload was invalid: {payloadError}.");
        return new ActorContentFixture(manifest!, payloads);
    }

    private static ActorContentFixture CreateUserActorOverrideFixture(byte[] bytes)
    {
        Expect(ContentPackageId.TryCreate(
                "test.user-actor-red", out ContentPackageId packageId),
            "The native user actor package ID was invalid.");
        Expect(ContentNamespaceId.TryCreate("userpack", out ContentNamespaceId contentNamespace),
            "The native user actor namespace was invalid.");
        Expect(ContentFilePath.TryCreate("actors/user-red-x.glb", out ContentFilePath path),
            "The native user actor path was invalid.");
        Expect(ContentFileDescriptor.TryCreate(
                path, ContentFileKind.ActorGltfBinary, bytes.Length, ContentHash.Compute(bytes),
                default, out ContentFileDescriptor file),
            "The native user actor file was invalid.");
        Expect(LogicalContentId.TryCreate(
                ConceptDemoActorPresentationProfile.HumanXDefinitionId,
                out LogicalContentId logicalId),
            "The native user actor target was invalid.");
        Expect(ContentDefinitionDescriptor.TryCreateFileDefinition(
                logicalId, ContentDefinitionKind.ActorPresentation,
                ContentDefinitionOperation.Replace, path,
                out ContentDefinitionDescriptor? definition),
            "The native user actor definition was invalid.");
        Expect(ContentPackageManifest.TryCreateUserResourcePack(
                1, 1, packageId, contentNamespace, "Red user X", "1",
                [file], [definition!], out ContentPackageManifest? manifest, out _),
            "The native user actor fixture was invalid.");
        return new ActorContentFixture(
            manifest!, [new ContentFilePayload(path, bytes)]);
    }

    private static byte[] TintActorArtifact(byte[] source)
    {
        const uint jsonChunk = 0x4E4F_534A;
        const uint binaryChunk = 0x004E_4942;
        int jsonLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(12)));
        int jsonStart = 20;
        int binaryHeader = checked(jsonStart + jsonLength);
        int binaryLength = checked((int)BinaryPrimitives.ReadUInt32LittleEndian(
            source.AsSpan(binaryHeader)));
        JsonObject root = JsonNode.Parse(source.AsSpan(jsonStart, jsonLength))!.AsObject();
        foreach (JsonNode? materialNode in root["materials"]!.AsArray())
        {
            JsonObject material = materialNode!.AsObject();
            JsonObject pbr = material["pbrMetallicRoughness"]?.AsObject() ?? new JsonObject();
            pbr["baseColorFactor"] = new JsonArray(0.95, 0.04, 0.04, 1.0);
            material["pbrMetallicRoughness"] = pbr;
        }
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
        BinaryPrimitives.WriteUInt32LittleEndian(
            result.AsSpan(outputBinaryHeader + 4), binaryChunk);
        source.AsSpan(binaryHeader + 8, binaryLength).CopyTo(
            result.AsSpan(outputBinaryHeader + 8));
        return result;
    }

    private static bool HasAnimation(AnimationPlayer player, string expected)
    {
        foreach (StringName animation in player.GetAnimationList())
            if (animation.ToString().Contains(expected, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static bool RootMotionTrackHasWalkTranslation(AnimationPlayer player)
    {
        foreach (StringName animationName in player.GetAnimationList())
        {
            if (!animationName.ToString().Contains("Walk", StringComparison.OrdinalIgnoreCase))
                continue;
            Animation? animation = player.GetAnimation(animationName);
            if (animation is null) continue;
            for (int track = 0; track < animation.GetTrackCount(); track++)
            {
                if (animation.TrackGetType(track) != Animation.TrackType.Position3D ||
                    animation.TrackGetPath(track) != player.RootMotionTrack ||
                    animation.TrackGetKeyCount(track) < 2)
                    continue;
                Vector3 firstValue = animation.TrackGetKeyValue(track, 0).AsVector3();
                for (int key = 1; key < animation.TrackGetKeyCount(track); key++)
                {
                    if (!animation.TrackGetKeyValue(track, key).AsVector3().IsEqualApprox(firstValue))
                        return true;
                }
            }
        }
        return false;
    }

    private static Node? FindNamedDescendant(Node root, string name)
    {
        if (string.Equals(root.Name, name, StringComparison.Ordinal)) return root;
        foreach (Node child in root.GetChildren())
        {
            Node? found = FindNamedDescendant(child, name);
            if (found is not null) return found;
        }
        return null;
    }

    private static T? FindDescendant<T>(Node root) where T : Node
    {
        if (root is T match) return match;
        foreach (Node child in root.GetChildren())
        {
            T? found = FindDescendant<T>(child);
            if (found is not null) return found;
        }
        return null;
    }

    private static Label3D? FindLabelByText(Node root, string text)
    {
        if (root is Label3D label && string.Equals(label.Text, text, StringComparison.Ordinal))
            return label;
        foreach (Node child in root.GetChildren())
        {
            Label3D? found = FindLabelByText(child, text);
            if (found is not null) return found;
        }
        return null;
    }

    private void RunSystemChatBannerAcceptance()
    {
        var host = new Control { Name = "SystemBannerAcceptanceHost" };
        AddChild(host);
        using var banner = new GodotSystemChatBanner(host);
        var system = new ClientChatOccurrence(
            7,
            1,
            1,
            9,
            20,
            ClientChatOccurrenceKind.SystemBroadcast,
            ClientChatSpeechMode.None,
            default,
            "System",
            "Authoritative test marker reached.");
        Expect(banner.TryPresent(in system, 100),
            "An authoritative system occurrence did not enter the transient banner.");
        Expect(banner.GetSnapshot() is
            { Visible: true, Text: "Authoritative test marker reached.", Presented: 1 },
            "The native system banner did not expose its current bounded presentation state.");
        banner.Update(100 + GodotSystemChatBanner.DisplayDurationMilliseconds - 1);
        Expect(banner.GetSnapshot().Visible,
            "The native system banner retired before its presentation-local deadline.");
        banner.Update(100 + GodotSystemChatBanner.DisplayDurationMilliseconds);
        Expect(!banner.GetSnapshot().Visible,
            "The native system banner did not retire at its presentation-local deadline.");
        var global = system with
        {
            DeliveryId = 2,
            RecipientSequence = 2,
            Kind = ClientChatOccurrenceKind.GlobalSpeech,
            SpeechMode = ClientChatSpeechMode.Global,
            DisplayLabel = "Alice",
        };
        Expect(!banner.TryPresent(in global, 10_000),
            "Ordinary Global speech incorrectly entered the system-only banner.");
        var laterGlobal = global with { DeliveryId = 3, RecipientSequence = 3 };
        Span<ClientChatOccurrence> batch = [system, laterGlobal];
        banner.Observe(batch, 20_000);
        Expect(banner.GetSnapshot() is
            { Visible: true, Text: "Authoritative test marker reached.", Presented: 2 },
            "A later player occurrence hid a system occurrence delivered in the same Chat batch.");
        var oldGenerationSystem = system with
        {
            ServerGenerationId = 6,
            DeliveryId = 4,
            RecipientSequence = 4,
            Text = "Retained old-generation marker.",
        };
        var newGenerationSystem = system with
        {
            ServerGenerationId = 8,
            DeliveryId = 5,
            RecipientSequence = 5,
            Text = "Current-generation marker.",
        };
        Span<ClientChatOccurrence> retainedAcrossGenerations =
            [oldGenerationSystem, newGenerationSystem];
        banner.Observe(retainedAcrossGenerations, 30_000);
        Expect(banner.GetSnapshot() is
            { Visible: true, Text: "Current-generation marker.", Presented: 4 },
            "The banner did not converge on the newest retained system occurrence across generations.");
        banner.Observe(retainedAcrossGenerations, 30_001);
        Expect(banner.GetSnapshot().Presented == 4,
            "Rescanning retained occurrences replayed old/new generation system banners.");
        host.QueueFree();
    }

    private static void RunTallPublicObjectPickingAcceptance()
    {
        Vector3 origin = new(0, 1.9f, 5f);
        Vector3 direction = Vector3.Forward;
        Expect(GodotPublicPointerPicker.TryIntersect(
                GameWorldPublicPrefabIds.CabinDoor,
                origin,
                direction,
                Vector3.Zero,
                out float doorDistance) && doorDistance > 0,
            "The upper portion of a Cabin door was not pointer-pickable.");
        Expect(GodotPublicPointerPicker.TryIntersect(
                GameWorldPublicPrefabIds.InformationalSign,
                new Vector3(0, 1.45f, 5f),
                direction,
                Vector3.Zero,
                out float signDistance) && signDistance > 0,
            "The upper portion of an informational sign was not pointer-pickable.");
        Expect(GodotPublicPointerPicker.TryIntersect(
                GameWorldPublicPrefabIds.IronSword,
                new Vector3(0, 0.8f, 5f),
                direction,
                Vector3.Zero,
                out float swordDistance) && swordDistance > 0,
            "The primitive Iron Sword was not pointer-pickable.");
        Expect(GodotPublicPointerPicker.IsPointerTarget(
                GameWorldPublicPrefabIds.IronSword,
                WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem),
            "A grounded public Iron Sword was not pointer-eligible.");
        Expect(!GodotPublicPointerPicker.IsPointerTarget(
                GameWorldPublicPrefabIds.IronSword,
                WorldPublicStateFlags.Visible),
            "Equipped Iron Sword presentation remained independently pointer-eligible.");
        Expect(!GodotPublicPointerPicker.TryIntersect(
                GameWorldPublicPrefabIds.CabinDoor,
                new Vector3(2f, 1.9f, 5f),
                direction,
                Vector3.Zero,
                out _),
            "The Cabin door pick volume extended far outside its visual width.");
    }

    private void RunControlledActorOwnerPoseAcceptance()
    {
        WorldPublicEntityId controlledId = new(701, 3);
        WorldPublicEntityId otherId = new(702, 4);
        ClientActorDefinitionCatalog catalog = ClientActorDefinitionCatalog.ConceptDemo;
        if (!catalog.TryGet(
                GameWorldPublicPrefabIds.Character, out ClientActorDefinition controlledDefinition) ||
            !catalog.TryGet(
                GameWorldPublicPrefabIds.CharacterMale, out ClientActorDefinition otherDefinition))
            throw new InvalidOperationException(
                "The controlled-pose fixture could not resolve its actor definitions.");
        if (!WorldPosition.TryFromGlobalMillimeters(
                1_000, 1_000, 1_000, out WorldPosition staleControlled) ||
            !WorldPosition.TryFromGlobalMillimeters(
                2_000, 1_300, 2_000, out WorldPosition ownerPose) ||
            !WorldPosition.TryFromGlobalMillimeters(
                4_000, 1_000, 5_000, out WorldPosition otherPosition))
            throw new InvalidOperationException(
                "The controlled-pose fixture positions were invalid.");

        ClientActorPresentationChange[] additions =
        [
            new(
                ClientActorPresentationChangeKind.Added,
                controlledId,
                new ClientActorPresentationRecord(
                    controlledId,
                    GameWorldPublicPrefabIds.Character,
                    staleControlled,
                    WorldYaw.Zero,
                    controlledDefinition,
                    "Controlled",
                    true,
                    MainHandPrefabId: GameWorldPublicPrefabIds.IronSword,
                    VerticalPhase: ClientPublicActorVerticalPhase.Grounded,
                    Action: ClientPublicActorActionKind.Primary,
                    ActionSequence: 5)),
            new(
                ClientActorPresentationChangeKind.Added,
                otherId,
                new ClientActorPresentationRecord(
                    otherId,
                    GameWorldPublicPrefabIds.CharacterMale,
                    otherPosition,
                    WorldYaw.QuarterTurn,
                    otherDefinition,
                    "Other",
                    true)),
        ];

        using var presenter = new GodotActorPresenter(this, catalog, 2);
        presenter.SetControlledActorPose(
            controlledId, ownerPose, WorldYaw.HalfTurn, nowMilliseconds: 1);
        presenter.Apply(additions, ownerPose, nowMilliseconds: 1);
        if (!presenter.TryGetActorRoot(controlledId, out Node3D? controlled) || controlled is null)
            throw new InvalidOperationException("The controlled actor root was not realized.");
        Expect(controlled.Position.IsEqualApprox(Vector3.Zero) &&
            Mathf.IsEqualApprox(controlled.Rotation.Y, MathF.PI),
            "A stale public record displaced or rotated the controlled actor away from its fresh owner pose.");
        Expect(presenter.GetSnapshot() is
            { EquippedMainHands: 1, VisiblePrimaryCues: 0, PrimaryCuesPresented: 0 },
            "An equipped MainHand was not attached, or an old retained action replayed on first presentation.");
        Expect(presenter.TryGetActorDiagnostics(
                controlledId, out GodotActorPresentationDiagnostics initialPresentation) &&
            initialPresentation.MainHandVisible && !initialPresentation.PrimaryCueActive,
            "The controlled actor did not expose its semantic MainHand socket presentation.");
        var mainHandSocket = FindNamedDescendant(controlled, "MainHandSocket") as Node3D;
        var mainHandPresentation = FindNamedDescendant(
            controlled, "MainHandPresentation") as Node3D;
        var mainHandActionPivot = FindNamedDescendant(
            controlled, "MainHandActionPivot") as Node3D;
        Expect(mainHandSocket is not null && mainHandSocket.GetParent() == controlled &&
            mainHandPresentation is not null && mainHandActionPivot is not null,
            "The controlled actor did not separate its stable hand mount from action animation.");
        Vector3 restingBladeInActor =
            (controlled.GlobalBasis.Inverse() * mainHandPresentation!.GlobalBasis).Y.Normalized();
        Expect(restingBladeInActor.Y < -0.6f &&
            new Vector2(restingBladeInActor.X, restingBladeInActor.Z).Length() > 0.35f,
            "The equipped sword did not retain its diagonal hand-owned resting grip.");
        Vector3 restingMainHandRotation = mainHandPresentation.Rotation;
        if (!presenter.TryGetActorRoot(otherId, out Node3D? other) || other is null)
            throw new InvalidOperationException("The other actor root was not realized.");
        Expect(other.Position.IsEqualApprox(new Vector3(2f, -0.3f, 3f)) &&
            Mathf.IsEqualApprox(other.Rotation.Y, MathF.PI * 0.5f),
            "The owner-pose override contaminated ordinary public actor presentation.");

        Expect(WorldPosition.TryFromGlobalMillimeters(
                2_200, 1_500, 2_300, out WorldPosition nextOwnerPose),
            "The next controlled owner pose was invalid.");
        presenter.SetControlledActorPose(
            controlledId, nextOwnerPose, WorldYaw.ThreeQuarterTurn, nowMilliseconds: 2);
        ClientActorPresentationRecord risingRecord = additions[0].Current with
        {
            VerticalPhase = ClientPublicActorVerticalPhase.Rising,
        };
        presenter.Apply(
            [new ClientActorPresentationChange(
                ClientActorPresentationChangeKind.Updated, controlledId, risingRecord)],
            nextOwnerPose,
            nowMilliseconds: 2);
        presenter.UpdateOriginAndLifetimes(nextOwnerPose, nowMilliseconds: 2);
        Expect(controlled.Position.IsEqualApprox(Vector3.Zero) &&
            Mathf.IsEqualApprox(controlled.Rotation.Y, MathF.PI * 1.5f),
            "Owner movement did not keep the controlled actor pinned to the player-relative origin.");
        Expect(other.Position.IsEqualApprox(new Vector3(1.8f, -0.5f, 2.7f)),
            "An ordinary public actor stopped following the player-relative render origin.");
        Expect(presenter.TryGetActorDiagnostics(
                controlledId, out GodotActorPresentationDiagnostics rising) &&
            rising.MotionPhase == GodotActorMotionPhase.Rising,
            "An authoritative upward pose transition did not select Rising presentation.");

        Expect(WorldPosition.TryFromGlobalMillimeters(
                2_200, 1_300, 2_300, out WorldPosition fallingPose),
            "The falling presentation pose was invalid.");
        presenter.SetControlledActorPose(controlledId, fallingPose, WorldYaw.ThreeQuarterTurn, 40);
        ClientActorPresentationRecord fallingRecord = risingRecord with
        {
            VerticalPhase = ClientPublicActorVerticalPhase.Falling,
        };
        presenter.Apply(
            [new ClientActorPresentationChange(
                ClientActorPresentationChangeKind.Updated, controlledId, fallingRecord)],
            fallingPose,
            nowMilliseconds: 40);
        presenter.UpdateOriginAndLifetimes(fallingPose, 40);
        Expect(presenter.TryGetActorDiagnostics(
                controlledId, out GodotActorPresentationDiagnostics falling) &&
            falling.MotionPhase == GodotActorMotionPhase.Falling &&
            presenter.GetSnapshot().LandingCuesPresented == 0,
            "Falling was not selected, or the rising-to-falling apex produced a false landing.");
        ClientActorPresentationRecord groundedRecord = fallingRecord with
        {
            VerticalPhase = ClientPublicActorVerticalPhase.Grounded,
        };
        presenter.Apply(
            [new ClientActorPresentationChange(
                ClientActorPresentationChangeKind.Updated, controlledId, groundedRecord)],
            fallingPose,
            nowMilliseconds: 40 + GodotActorPresenter.MotionHoldMilliseconds);
        Expect(presenter.TryGetActorDiagnostics(
                controlledId, out GodotActorPresentationDiagnostics landed) &&
            landed.MotionPhase == GodotActorMotionPhase.Idle &&
            presenter.GetSnapshot().LandingCuesPresented == 1,
            "Falling did not converge once to Idle through the bounded landing presentation cue.");

        presenter.UpdateOriginAndLifetimes(
            nextOwnerPose,
            nowMilliseconds: 1 + GodotActorPresenter.ControlledNameplateGraceMilliseconds);
        Expect(presenter.GetSnapshot().VisibleNameplates == 0,
            "The controlled nameplate remained permanently visible after its join grace period.");
        presenter.SetRevealHeld(true);
        Expect(presenter.GetSnapshot().VisibleNameplates == 2,
            "Ctrl reveal did not restore all already-known eligible nameplates.");
        presenter.SetRevealHeld(false);
        Expect(presenter.GetSnapshot().VisibleNameplates == 0,
            "Releasing Ctrl reveal restored an expired controlled nameplate.");
        const ulong swingStarted = 3_000;
        Expect(presenter.TryPresentPrimaryAction(controlledId, 7, swingStarted),
            "An accepted Primary action did not start its minimum native swing cue.");
        Expect(!presenter.TryPresentPrimaryAction(controlledId, 7, swingStarted),
            "A duplicate Primary sequence replayed its native swing cue.");
        Expect(presenter.GetSnapshot() is
            { VisiblePrimaryCues: 1, PrimaryCuesPresented: 1 },
            "The active Primary cue was not observable in bounded native state.");
        Expect(mainHandPresentation.Rotation.IsEqualApprox(restingMainHandRotation) &&
            !mainHandActionPivot!.Rotation.IsEqualApprox(Vector3.Zero),
            "A Primary cue overwrote the equipped weapon's resting mount.");
        presenter.UpdateOriginAndLifetimes(
            nextOwnerPose,
            swingStarted + GodotActorPresenter.PrimaryCueDurationMilliseconds);
        Expect(presenter.GetSnapshot() is
            { VisiblePrimaryCues: 0, PrimaryCuesPresented: 1 },
            "The transient Primary cue did not retire at its fixed presentation lifetime.");
        Expect(mainHandPresentation.Rotation.IsEqualApprox(restingMainHandRotation) &&
            mainHandActionPivot!.Rotation.IsEqualApprox(Vector3.Zero),
            "Primary-cue retirement did not preserve the resting mount and reset only its action pivot.");

        ClientActorPresentationRecord interacted = additions[0].Current with
        {
            Position = fallingPose,
            Action = ClientPublicActorActionKind.Interact,
            ActionSequence = 8,
        };
        presenter.Apply(
            [new ClientActorPresentationChange(
                ClientActorPresentationChangeKind.Updated, controlledId, interacted)],
            fallingPose,
            swingStarted + GodotActorPresenter.PrimaryCueDurationMilliseconds + 1);
        Expect(presenter.TryGetActorDiagnostics(
                controlledId, out GodotActorPresentationDiagnostics interaction) &&
            interaction.InteractCueActive && presenter.GetSnapshot().InteractCuesPresented == 1,
            "An accepted public Interact transition did not produce its bounded native cue.");

        WorldPublicRecord equippedSword = new(
            new WorldPublicEntityId(999, 1),
            1,
            GameWorldPublicPrefabIds.IronSword,
            fallingPose,
            WorldYaw.Zero,
            WorldPublicStateFlags.Visible,
            new ChunkPublicationRevision(1));
        Expect(!presenter.ShouldBatch(in equippedSword),
            "The equipped sword remained independently batched despite its actor attachment.");
        WorldPublicRecord groundSword = equippedSword with
        {
            StateFlags = WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem,
        };
        Expect(presenter.ShouldBatch(in groundSword),
            "A dropped GroundItem sword failed to return to ordinary world presentation.");
    }

    private void RunJoinedInputAuthorityAcceptance()
    {
        GodotWorldInputMap map = GodotWorldInputMap.Default;
        Expect(InputMap.HasAction(map.MoveLeft) && InputMap.HasAction(map.MoveRight) &&
            InputMap.HasAction(map.MoveForward) && InputMap.HasAction(map.MoveBackward) &&
            InputMap.HasAction(map.Interact) && InputMap.HasAction(map.Primary),
            "The native project does not declare the playable InputMap contract.");

        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        BoundWorldPack bound = BindPlayablePack(pack);
        var composition = new UltraSimGameComposition(
            new UltraSim.Game.Inventory.InventoryLimits(32, 128, 32),
            baseWorldPack: pack);
        var options = new ServerRuntimeOptions(
            UltraSimGameComposition.CreateWorldOptions(
                128,
                new PersistenceIdentityOptions(new PersistentWorldId(0x80_3010))),
            WorldClockOptions.Default);
        using var runtime = new ServerRuntime(composition, options);
        runtime.Initialize();
        runtime.Start();
        using var admission = new AdmissionService(runtime.Game, composition);
        AdmissionResult entered = admission.JoinAccountless(new ParticipantId(3010));
        Expect(entered.IsEntered, "The joined native actor could not enter authority.");
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayableWorldEntities>(
            (world, _, game) => GamePlayableWorld.Populate(world, game, entered.Entity),
            out GamePlayableWorldEntities entities),
            "The joined native world could not populate.");
        runtime.Tick();

        var cache = new ClientWorldSemanticCache(
            bound,
            new ClientWorldSemanticCacheLimits(14, 16, 16, 16, 64));
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (_, _, game) =>
            {
                StageAuthoritativeWindow(game.WorldState.Publications, cache,
                    GamePlayableWorld.PlayerStart.Chunk);
                return true;
            }, out bool staged) && staged,
            "The joined native cache did not consume authoritative publication snapshots.");

        using var renderer = new GodotWorldRenderer(
            this,
            cache,
            GodotWorldRendererLimits.ForStructuralWindow(1, 5, 16));
        var window = new GodotSlidingRenderWindow(cache, renderer, 1);
        var changes = new ClientPresentationChange[18];
        Expect(window.TryTrackPlayer(
            GamePlayableWorld.PlayerStart,
            changes,
            out _, out _, out _, out _),
            "The joined native presentation window could not initialize.");
        _ = Drain(renderer);

        var sequencer = new ClientWorldInputSequencer();
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GameWorldActionStatus>(
            (world, _, game) => game.WorldActions.BeginControlSession(
                world, entities.Player, sequencer.ControlSession),
            out GameWorldActionStatus controlBound) && controlBound == GameWorldActionStatus.Accepted,
            "The joined native control session could not bind to the actor.");
        try
        {
            Input.ActionPress(map.MoveRight, 1f);
            Input.ActionPress(map.Interact, 1f);
            ClientWorldInputSample sample = GodotWorldInput.Capture(map, windowHasFocus: true);
            Expect(sequencer.TryCreateCommand(sample, out GamePlayerCommand command),
                "Godot input did not form the shared player command.");
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, GamePlayerCommandResult>(
                (world, _, game) =>
                {
                    game.WorldActions.ApplyPlayerCommand(
                        world,
                        game.WorldState,
                        entities.Player,
                        command,
                        entities.CabinDoor,
                        out GamePlayerCommandResult result);
                    return result;
                }, out GamePlayerCommandResult authority) && authority.WasAdmitted &&
                authority.Locomotion == GameWorldActionStatus.Accepted &&
                authority.Interaction == GameWorldActionStatus.Accepted,
                "Godot input did not reach composed movement/interaction authority.");
        }
        finally
        {
            Input.ActionRelease(map.MoveRight);
            Input.ActionRelease(map.Interact);
        }
        runtime.Tick();

        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, WorldPosition>(
            (world, _, game) =>
            {
                Expect(game.WorldActions.TryReadCabinDoor(world, entities.CabinDoor, out bool open) && open,
                    "The joined native door authority did not open.");
                Expect(game.WorldState.TryReadPosition(world, entities.Player, out WorldPosition position),
                    "The joined native player position was missing.");
                RefreshAuthoritativeChunk(game.WorldState.Publications, cache,
                    GamePlayableWorld.PlayerStart.ToChunkKey(GameWorldState.DefaultSpace));
                RefreshAuthoritativeChunk(game.WorldState.Publications, cache,
                    GamePlayableWorld.DoorPosition.ToChunkKey(GameWorldState.DefaultSpace));
                return position;
            }, out WorldPosition moved),
            "The joined native authority/publication refresh failed.");
        Expect(moved.GlobalXMillimeters ==
            GamePlayableWorld.PlayerStart.GlobalXMillimeters + 100,
            "The joined native 60 Hz movement did not integrate to 100 mm.");

        SpatialChunkKey playerKey = GamePlayableWorld.PlayerStart.ToChunkKey(GameWorldState.DefaultSpace);
        SpatialChunkKey doorKey = GamePlayableWorld.DoorPosition.ToChunkKey(GameWorldState.DefaultSpace);
        Expect(cache.TryGetResidentHandle(playerKey, out ClientSemanticChunkHandle playerHandle) &&
            cache.TryGetResidentHandle(doorKey, out ClientSemanticChunkHandle doorHandle) &&
            renderer.TryQueuePublicRefresh(playerHandle, out _) &&
            renderer.TryQueuePublicRefresh(doorHandle, out _),
            "The joined native public presentation refresh did not queue.");
        renderer.ProcessFrame(new GodotWorldFrameBudget(2, 0));
        Expect(window.TryTrackPlayer(moved, changes, out _, out _, out _, out _),
            "The joined native player position did not reach presentation.");
        GodotWorldRendererSnapshot snapshot = renderer.GetSnapshot();
        Expect(snapshot.PublicRefreshes == 2 && snapshot.ActiveRenderChunks == 9,
            "The joined native renderer did not realize authoritative public updates.");

        GD.Print(
            $"T0.10 JOINED INPUT METRICS commands={sequencer.Snapshot().CommandsProduced} " +
            $"player_x_mm={moved.GlobalXMillimeters} public_refreshes={snapshot.PublicRefreshes} " +
            $"active_chunks={snapshot.ActiveRenderChunks}");
    }

    private static BoundWorldPack BindPlayablePack(WorldPack pack)
    {
        var binding = new WorldContentBinding(
            GameContent.WorldFingerprint,
            [
                GameTerrainMaterialIds.Water,
                GameTerrainMaterialIds.Sand,
                GameTerrainMaterialIds.Grass,
                GameTerrainMaterialIds.Rock,
                GameTerrainMaterialIds.Snow,
            ],
            [
                GameWorldStaticDefinitionIds.Tree,
                GameWorldStaticDefinitionIds.Rock,
                GameWorldStaticDefinitionIds.CabinWall,
                GameWorldStaticDefinitionIds.CabinRoof,
                GameWorldStaticDefinitionIds.Shelf,
                GameWorldStaticDefinitionIds.Book,
            ]);
        Expect(pack.TryBind(binding, out BoundWorldPack? bound, out _),
            "The authored playable pack did not bind to game content.");
        return bound!;
    }

    private static void StageAuthoritativeWindow(
        ChunkPublicationStore publications,
        ClientWorldSemanticCache cache,
        SpatialChunkCoord center)
    {
        Span<WorldPublicRecord> rows = stackalloc WorldPublicRecord[16];
        for (int z = center.Z - 1; z <= center.Z + 1; z++)
        {
            for (int x = center.X - 1; x <= center.X + 1; x++)
            {
                SpatialChunkKey key = Key(x, z);
                Expect(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _),
                    $"The authoritative native chunk {key} could not be requested.");
                ChunkPublicationReadStatus status = publications.TryCopySnapshot(
                    key, rows, out ChunkPublicationBatch batch);
                bool staged = status == ChunkPublicationReadStatus.Snapshot
                    ? cache.TryStageFromBaseWorld(handle, rows[..batch.Count], batch.Cursor, out _)
                    : status == ChunkPublicationReadStatus.ChunkAbsent &&
                        cache.TryStageFromBaseWorld(handle, [], default, out _);
                Expect(staged && cache.TryPublish(handle, out _),
                    $"The authoritative native chunk {key} could not publish locally.");
            }
        }
    }

    private static void RefreshAuthoritativeChunk(
        ChunkPublicationStore publications,
        ClientWorldSemanticCache cache,
        SpatialChunkKey key)
    {
        Expect(cache.TryGetResidentHandle(key, out ClientSemanticChunkHandle handle),
            $"The authoritative native refresh key {key} was not resident.");
        Span<WorldPublicRecord> rows = stackalloc WorldPublicRecord[16];
        ChunkPublicationReadStatus status = publications.TryCopySnapshot(
            key, rows, out ChunkPublicationBatch batch);
        Expect(status == ChunkPublicationReadStatus.Snapshot &&
            cache.TryApplySnapshot(handle, batch, rows[..batch.Count], out _),
            $"The authoritative native refresh for {key} failed.");
    }

    private void RunAcceptance()
    {
        ValidateGodotGeometryConventions();
        WorldPack pack = GamePlayableWorld.CreateWorldPack();
        var binding = new WorldContentBinding(
            GameContent.WorldFingerprint,
            [
                GameTerrainMaterialIds.Water,
                GameTerrainMaterialIds.Sand,
                GameTerrainMaterialIds.Grass,
                GameTerrainMaterialIds.Rock,
                GameTerrainMaterialIds.Snow,
            ],
            [
                GameWorldStaticDefinitionIds.Tree,
                GameWorldStaticDefinitionIds.Rock,
                GameWorldStaticDefinitionIds.CabinWall,
                GameWorldStaticDefinitionIds.CabinRoof,
                GameWorldStaticDefinitionIds.Shelf,
                GameWorldStaticDefinitionIds.Book,
            ]);
        Expect(pack.TryBind(binding, out BoundWorldPack? bound, out _),
            "The authored playable pack did not bind to game content.");
        var cache = new ClientWorldSemanticCache(
            bound!,
            new ClientWorldSemanticCacheLimits(14, 16, 16, 16, 64));
        PublicationEpoch epoch = PublicationEpoch.Create();
        WorldPosition player = GamePlayableWorld.PlayerStart;
        StageWindow(cache, epoch, player.Chunk);

        double resourceBaseline = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        double bufferBaseline = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        using var renderer = new GodotWorldRenderer(
            this,
            cache,
            GodotWorldRendererLimits.ForStructuralWindow(1, 5, 16));
        var window = new GodotSlidingRenderWindow(cache, renderer, 1);
        var changes = new ClientPresentationChange[18];
        Expect(window.TryTrackPlayer(
            player, changes, out ClientPresentationWindowTransition initial,
            out _, out ClientPresentationWindowError initialError,
            out GodotWorldRenderQueueError initialRendererError),
            $"Initial native window failed: {initialError}/{initialRendererError}.");
        Expect(initial.Entered == 9 && initial.ActiveChunks == 9,
            "The native playable window was not a complete 3x3.");
        int frames = Drain(renderer);
        GodotWorldRendererSnapshot initialRender = renderer.GetSnapshot();
        Expect(initialRender.ActiveRenderChunks == 9 && initialRender.CompletedBuilds == 9,
            "The authored terrain/static/Multi/public window did not render.");
        Expect(initialRender.ActiveTerrainSurfaceMeshes >= 9 &&
            initialRender.ActiveStaticBatchGroups > 0 &&
            initialRender.ActivePublicBatchGroups > 0 &&
            initialRender.ActiveFallbackGroups > 0,
            "The bounded renderer realization census omitted an active presentation family.");
        Label3D? groundItemName = FindLabelByText(renderer.Root, "Iron Sword");
        Expect(groundItemName is not null && !groundItemName.IsVisibleInTree(),
            "The ground sword did not retain a hidden generic world-name label.");
        renderer.SetPublicNamesRevealHeld(true);
        Expect(groundItemName!.IsVisibleInTree() && groundItemName.Text == "Iron Sword",
            "Ctrl reveal did not expose only the ground sword's generic name.");
        renderer.SetPublicNamesRevealHeld(false);
        Expect(!groundItemName.IsVisibleInTree(),
            "Releasing Ctrl did not hide the generic ground-item name.");
        ValidateGodotTerrainFrontFaces(renderer.Root);
        Expect(initialRender.ActivePublicNodes == CountPublicLayerNodes(renderer.Root),
            "Public-layer node diagnostic drifted from native ownership.");
        Expect(initialRender.ActiveNodes < 9 * TerrainChunkFormat.MaterialCellCount,
            "The playable renderer created cell-shaped scene-tree identity.");
        var swordPrimitive = FindNamedDescendant(renderer.Root,
            $"Public_{GameWorldPublicPrefabIds.IronSword}") as MultiMeshInstance3D;
        ClientPrimitivePresentation swordBounds = ClientPrimitivePresentationCatalog.ForPublic(
            GameWorldPublicPrefabIds.IronSword);
        Expect(swordPrimitive?.Multimesh?.Mesh is not null &&
            swordPrimitive.CustomAabb.Size.IsEqualApprox(new Vector3(
                swordBounds.WidthMillimeters, swordBounds.HeightMillimeters,
                swordBounds.DepthMillimeters) * 0.001f),
            "The missing sword model and pointer descriptor no longer share one visible bound.");

        for (int z = -1; z <= 1; z++) StageIfMissing(cache, epoch, Key(2, z));
        WorldPosition crossed = GamePlayableWorld.ChestPosition;
        Expect(window.TryTrackPlayer(
            crossed, changes, out ClientPresentationWindowTransition crossing,
            out int required, out ClientPresentationWindowError crossingError,
            out GodotWorldRenderQueueError crossingRendererError),
            $"Native chunk crossing failed: {crossingError}/{crossingRendererError}.");
        Expect(crossing.Entered == 3 && crossing.Retained == 6 && crossing.Exited == 3,
            "The native playable crossing was not 3/6/3.");
        frames += Drain(renderer);
        for (int index = 0; index < required; index++)
        {
            if (changes[index].Kind != ClientPresentationChangeKind.Exit) continue;
            Expect(cache.BeginRetire(changes[index].Handle) &&
                cache.CompleteRetire(changes[index].Handle),
                "Exited native semantics could not retire.");
        }

        SpatialChunkKey doorKey = GamePlayableWorld.DoorPosition.ToChunkKey(GameWorldState.DefaultSpace);
        Expect(cache.TryGetResidentHandle(doorKey, out ClientSemanticChunkHandle doorHandle),
            "The rendered cabin-door chunk was not resident.");
        WorldPublicRecord openDoor = Public(
            2, 2, GameWorldPublicPrefabIds.CabinDoor,
            GamePlayableWorld.DoorPosition, WorldYaw.QuarterTurn,
            revision: 2);
        var delta = new WorldPublicDeltaRecord(
            WorldPublicOperation.Present,
            WorldPublicFieldMask.Transform,
            openDoor.Id,
            openDoor.EntityPublicVersion,
            openDoor);
        var batch = new ChunkPublicationBatch(
            ChunkPublicationReadStatus.Delta,
            doorKey,
            new ChunkPublicationCursor(epoch, doorKey, new ChunkPublicationRevision(2)),
            new ChunkPublicationRevision(1),
            1,
            1);
        Expect(cache.TryApplyDelta(doorHandle, batch, [delta], out _),
            "The native door semantic update did not apply.");
        GodotWorldRendererSnapshot beforeRefresh = renderer.GetSnapshot();
        Expect(renderer.TryQueuePublicRefresh(doorHandle, out _),
            "The native door public refresh did not queue.");
        renderer.ProcessFrame(new GodotWorldFrameBudget(1, 0));
        GodotWorldRendererSnapshot afterRefresh = renderer.GetSnapshot();
        Expect(afterRefresh.ActivePublicNodes == CountPublicLayerNodes(renderer.Root),
            "Public refresh lost exact public-layer node accounting.");
        Expect(afterRefresh.PublicRefreshes == beforeRefresh.PublicRefreshes + 1 &&
            afterRefresh.CompletedBuilds == beforeRefresh.CompletedBuilds,
            "Opening the door rebuilt immutable cabin presentation.");

        Expect(window.TryTrackPlayer(
            crossed, changes, out ClientPresentationWindowTransition sameCenter,
            out _, out _, out _),
            "Same-center native movement failed.");
        Expect(sameCenter.Entered == 0 && sameCenter.Exited == 0,
            "Same-center movement changed native membership.");

        double peakResources = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        double peakBuffers = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        renderer.Dispose();
        Expect(renderer.GetSnapshot().ActivePublicNodes == 0,
            "Retired renderer retained public-layer node ownership.");
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        double resourcesAfter = Performance.GetMonitor(Performance.Monitor.ObjectResourceCount);
        double buffersAfter = Performance.GetMonitor(Performance.Monitor.RenderBufferMemUsed);
        Expect(resourcesAfter <= peakResources && buffersAfter <= peakBuffers,
            "Native resource pressure continued growing after playable renderer disposal.");

        GD.Print(
            $"T0.10 METRICS pack_bytes={pack.Length} identity_index_bytes={bound!.StructuralIdentityIndexReservedBytes} " +
            $"semantic_reserved_bytes={cache.GetSnapshot().ReservedBytes} active_chunks={afterRefresh.ActiveRenderChunks} " +
            $"active_nodes={afterRefresh.ActiveNodes} active_resources={afterRefresh.ActiveResources} " +
            $"terrain_surfaces={afterRefresh.ActiveTerrainSurfaceMeshes} " +
            $"static_batches={afterRefresh.ActiveStaticBatchGroups} " +
            $"public_batches={afterRefresh.ActivePublicBatchGroups} " +
            $"fallback_groups={afterRefresh.ActiveFallbackGroups} " +
            $"builds={afterRefresh.CompletedBuilds} public_refreshes={afterRefresh.PublicRefreshes} " +
            $"crossing_frames={frames} frame_max_us={afterRefresh.MaximumFrameMicroseconds:F3} " +
            $"mesh_max_us={afterRefresh.MaximumMeshBuildMicroseconds:F3} " +
            $"resource_baseline={resourceBaseline:F0} resource_peak={peakResources:F0} " +
            $"resource_after={resourcesAfter:F0} buffer_baseline={bufferBaseline:F0} " +
            $"buffer_peak={peakBuffers:F0} buffer_after={buffersAfter:F0}");
    }

    private static void StageWindow(
        ClientWorldSemanticCache cache,
        PublicationEpoch epoch,
        SpatialChunkCoord center)
    {
        for (int z = center.Z - 1; z <= center.Z + 1; z++)
        {
            for (int x = center.X - 1; x <= center.X + 1; x++)
                StageIfMissing(cache, epoch, Key(x, z));
        }
    }

    private static void StageIfMissing(
        ClientWorldSemanticCache cache,
        PublicationEpoch epoch,
        SpatialChunkKey key)
    {
        if (cache.TryGetResidentHandle(key, out _)) return;
        Expect(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _),
            $"Native semantic request failed for {key}.");
        WorldPublicRecord[] rows = PublicRows(key);
        ChunkPublicationCursor cursor = rows.Length == 0
            ? default
            : new ChunkPublicationCursor(epoch, key, new ChunkPublicationRevision(1));
        Expect(cache.TryStageFromBaseWorld(handle, rows, cursor, out _) &&
            cache.TryPublish(handle, out _),
            $"Native semantic stage/publish failed for {key}.");
    }

    private static WorldPublicRecord[] PublicRows(SpatialChunkKey key)
    {
        if (key.Coord == new SpatialChunkCoord(0, 0))
        {
            return
            [
                Public(1, 1, GameWorldPublicPrefabIds.Character,
                    GamePlayableWorld.PlayerStart, WorldYaw.Zero, 1),
                Public(4, 1, GameWorldPublicPrefabIds.WoodlandAnimal,
                    GamePlayableWorld.AnimalPosition, WorldYaw.Zero, 1),
                Public(9, 1, GameWorldPublicPrefabIds.IronSword,
                    GamePlayableWorld.IronSwordSpawnerPosition, WorldYaw.Zero, 1,
                    WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem),
            ];
        }
        if (key.Coord == new SpatialChunkCoord(1, 0))
        {
            return
            [
                Public(2, 1, GameWorldPublicPrefabIds.CabinDoor,
                    GamePlayableWorld.DoorPosition, WorldYaw.Zero, 1),
                Public(3, 1, GameWorldPublicPrefabIds.WorldChest,
                    GamePlayableWorld.ChestPosition, WorldYaw.Zero, 1),
            ];
        }
        return [];
    }

    private static WorldPublicRecord Public(
        uint index,
        ulong version,
        uint prefab,
        WorldPosition position,
        WorldYaw orientation,
        ulong revision,
        WorldPublicStateFlags flags = WorldPublicStateFlags.Visible) => new(
            new WorldPublicEntityId(index, 1),
            version,
            prefab,
            position,
            orientation,
            flags,
            new ChunkPublicationRevision(revision));

    private static void ValidateGodotTerrainFrontFaces(Node root)
    {
        MeshInstance3D? terrain = root.FindChild("BaseTerrain", recursive: true, owned: false)
            as MeshInstance3D;
        ArrayMesh? mesh = terrain?.Mesh as ArrayMesh;
        Expect(mesh is not null && mesh.GetSurfaceCount() != 0,
            "The native terrain mesh could not be inspected.");
        if (mesh is null) return;
        for (int surface = 0; surface < mesh.GetSurfaceCount(); surface++)
        {
            using var data = new MeshDataTool();
            Expect(data.CreateFromSurface(mesh, surface) == Error.Ok && data.GetFaceCount() != 0,
                "A native terrain surface had no inspectable triangles.");
            for (int face = 0; face < data.GetFaceCount(); face++)
            {
                Vector3 first = data.GetVertex(data.GetFaceVertex(face, 0));
                Vector3 second = data.GetVertex(data.GetFaceVertex(face, 1));
                Vector3 third = data.GetVertex(data.GetFaceVertex(face, 2));
                Expect((second - first).Cross(third - first).Y < 0,
                    "A Godot terrain triangle was not clockwise when viewed from above.");
            }
        }
    }

    private static void ValidateGodotGeometryConventions()
    {
        GodotMeshGeometry.ConvertCanonicalTriangle(0, 1, 2,
            out int first, out int second, out int third);
        Expect(first == 0 && second == 2 && third == 1,
            "The Godot adapter did not own canonical triangle conversion.");

        var material = new StandardMaterial3D();
        ArrayMesh mesh = GodotMeshGeometry.CreateGroundedBox(new Vector3(2f, 3f, 4f), material);
        using var data = new MeshDataTool();
        Expect(data.CreateFromSurface(mesh, 0) == Error.Ok && data.GetFaceCount() == 12,
            "The Godot procedural box did not expose twelve triangles.");
        for (int face = 0; face < data.GetFaceCount(); face++)
        {
            int firstVertex = data.GetFaceVertex(face, 0);
            int secondVertex = data.GetFaceVertex(face, 1);
            int thirdVertex = data.GetFaceVertex(face, 2);
            Vector3 a = data.GetVertex(firstVertex);
            Vector3 b = data.GetVertex(secondVertex);
            Vector3 c = data.GetVertex(thirdVertex);
            Vector3 triangleCenter = (a + b + c) / 3f;
            Vector3 outward = ExpectedBoxNormal(triangleCenter);
            Vector3 storedNormal = (data.GetVertexNormal(firstVertex) +
                data.GetVertexNormal(secondVertex) +
                data.GetVertexNormal(thirdVertex)).Normalized();
            float normalDot = storedNormal.Dot(outward);
            float windingDot = (b - a).Cross(c - a).Normalized().Dot(outward);
            Expect(normalDot > 0.999f,
                $"Godot procedural box face {face} normal was not outward: " +
                $"dot={normalDot}, center={triangleCenter}, normal={storedNormal}.");
            Expect(windingDot < -0.999f,
                $"Godot procedural box face {face} was not native clockwise winding: " +
                $"dot={windingDot}.");
        }

        static Vector3 ExpectedBoxNormal(Vector3 faceCenter)
        {
            if (Mathf.IsEqualApprox(faceCenter.Y, 0f)) return Vector3.Down;
            if (Mathf.IsEqualApprox(faceCenter.Y, 3f)) return Vector3.Up;
            if (Mathf.IsEqualApprox(faceCenter.X, -1f)) return Vector3.Left;
            if (Mathf.IsEqualApprox(faceCenter.X, 1f)) return Vector3.Right;
            if (Mathf.IsEqualApprox(faceCenter.Z, -2f)) return Vector3.Forward;
            if (Mathf.IsEqualApprox(faceCenter.Z, 2f)) return Vector3.Back;
            throw new InvalidOperationException($"A procedural box face was not planar: {faceCenter}.");
        }

        ArrayMesh wedge = GodotMeshGeometry.CreateGroundedPointerWedge(
            0.42f, 0.42f, 0.68f, 1.8f, material);
        using var wedgeData = new MeshDataTool();
        Expect(wedgeData.CreateFromSurface(wedge, 0) == Error.Ok &&
            wedgeData.GetFaceCount() == 8,
            "The Godot character wedge did not expose eight triangles.");
        Vector3 inside = new(0f, 0.9f, -0.05f);
        for (int face = 0; face < wedgeData.GetFaceCount(); face++)
        {
            int firstVertex = wedgeData.GetFaceVertex(face, 0);
            int secondVertex = wedgeData.GetFaceVertex(face, 1);
            int thirdVertex = wedgeData.GetFaceVertex(face, 2);
            Vector3 a = wedgeData.GetVertex(firstVertex);
            Vector3 b = wedgeData.GetVertex(secondVertex);
            Vector3 c = wedgeData.GetVertex(thirdVertex);
            Vector3 normal = (wedgeData.GetVertexNormal(firstVertex) +
                wedgeData.GetVertexNormal(secondVertex) +
                wedgeData.GetVertexNormal(thirdVertex)).Normalized();
            Vector3 rawCross = (b - a).Cross(c - a).Normalized();
            Expect(normal.Dot((a + b + c) / 3f - inside) > 0f,
                $"Godot character wedge face {face} normal pointed into the prism.");
            Expect(rawCross.Dot(normal) < -0.999f,
                $"Godot character wedge face {face} did not preserve native winding plus outward normal.");
        }
    }

    private static OfficialVisualFixture CreateOfficialVisualFixture()
    {
        string cacheRoot = Path.Combine(Path.GetTempPath(),
            $"ultrasim-official-native-{System.Environment.ProcessId}-{Guid.NewGuid():N}");
        if (!ContentCache.TryOpen(
                cacheRoot, out ContentCache? cache, out ContentCacheError openError))
            throw new InvalidOperationException(
                $"The Official native cache could not open: {openError}.");
        try
        {
            string sourceRoot = Path.GetDirectoryName(OfficialArchivePath())!;
            if (!OfficialLocalPresentation.TryOpenOrAcquire(
                    cache!, sourceRoot, out ContentCatalogLease? local,
                    out OfficialLocalPresentationResult result))
                throw new InvalidOperationException(
                    $"The Official native package could not apply: {result}.");
            var generation = PresentationGeneration.Create(null, local);
            return new OfficialVisualFixture(
                cacheRoot, cache!, generation,
                new GodotContentActorVisualResolver(generation));
        }
        catch
        {
            cache?.Dispose();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
            throw;
        }
    }

    private static string OfficialArchivePath() => Path.Combine(
        Path.GetFullPath(Path.Combine(ProjectSettings.GlobalizePath("res://"), "..", "..")),
        "Client", "OfficialContent", OfficialLocalPresentation.ArchiveFileName);

    private sealed class OfficialVisualFixture(
        string cacheRoot,
        ContentCache cache,
        PresentationGeneration generation,
        GodotContentActorVisualResolver visuals) : IDisposable
    {
        internal GodotContentActorVisualResolver Visuals { get; } = visuals;

        public void Dispose()
        {
            Visuals.Dispose();
            generation.Dispose();
            cache.Dispose();
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }
    }

    private sealed class SwitchableActorContentSource : IActorPresentationGenerationSource, IDisposable
    {
        private readonly ContentCatalogLease _catalog;
        private PresentationGeneration _generation = PresentationGeneration.Create(null, null);

        internal SwitchableActorContentSource(ContentCatalogLease catalog) =>
            _catalog = catalog;

        public PresentationGenerationId CurrentPresentationGeneration =>
            _generation.CurrentPresentationGeneration;

        internal void PublishRealm()
        {
            PresentationGeneration previous = _generation;
            _generation = PresentationGeneration.Create(_catalog, null);
            previous.Dispose();
        }

        internal void Publish(PresentationGeneration generation)
        {
            ArgumentNullException.ThrowIfNull(generation);
            PresentationGeneration previous = _generation;
            _generation = generation;
            previous.Dispose();
        }

        public bool TryResolveActorPresentation(
            LogicalContentId definitionId,
            ActorPresentationResolutionRequirements requirements,
            out ActorPresentationContentLease? lease,
            out ContentActorPresentationResolutionError error,
            ActorPresentationArtifactLimits limits = default)
        {
            return _generation.TryResolveActorPresentation(
                definitionId, requirements, out lease, out error, limits);
        }

        public void Dispose() => _generation.Dispose();
    }

    private sealed record ActorContentFixture(
        ContentPackageManifest Manifest,
        ContentFilePayload[] Payloads);

    // Cold acceptance observation only; production snapshots use maintained owner-local counts.
    private static int CountPublicLayerNodes(Node root)
    {
        int total = 0;
        foreach (Node child in root.GetChildren())
            total += child.Name == "Public" ? CountSubtree(child) : CountPublicLayerNodes(child);
        return total;

        static int CountSubtree(Node node)
        {
            int count = 1;
            foreach (Node child in node.GetChildren()) count += CountSubtree(child);
            return count;
        }
    }

    private static int Drain(GodotWorldRenderer renderer)
    {
        int frames = 0;
        while (renderer.GetSnapshot().PendingBuilds != 0 ||
            renderer.GetSnapshot().PendingRetirements != 0)
        {
            renderer.ProcessFrame(new GodotWorldFrameBudget(2, 2));
            if (++frames > 64) throw new InvalidOperationException("Native playable work did not converge.");
        }
        return frames;
    }

    private static SpatialChunkKey Key(int x, int z) =>
        new(GameWorldState.DefaultSpace, x, z);

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
