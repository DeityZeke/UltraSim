using System.Net;
using System.Net.Sockets;
using UltraSim.Client.BuiltInTestWorlds;
using UltraSim.Client.Content;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Server.Hosting;
using UltraSim.UI;

internal static class F03bRemotePlayableTests
{
    internal const int F03bTestCount = 4;
    internal const int A03TestCount = 6;
    internal const int A04TestCount = 5;
    internal const int A05TestCount = 1;
    internal const int A06TestCount = 2;
    internal const int A07TestCount = 2;
    internal const int TestCount = F03bTestCount + A03TestCount + A04TestCount + A05TestCount +
        A06TestCount + A07TestCount;

    internal static void RunAll()
    {
        Run("RemotePlayableProtocolIsCanonicalAndTrustMinimal",
            RemotePlayableProtocolIsCanonicalAndTrustMinimal);
        Run("RemoteGuestAdmissionRejectsInvalidIdentityBeforeControl",
            RemoteGuestAdmissionRejectsInvalidIdentityBeforeControl);
        Run("RemoteGuestControlsConceptDemoAndAppliesPublicChatAndUi",
            RemoteGuestControlsConceptDemoAndAppliesPublicChatAndUi);
        Run("RemotePlayableReconnectRejectsPriorApplicationLineage",
            RemotePlayableReconnectRejectsPriorApplicationLineage);
        Run("RequiredContentProtocolIsCanonicalAndBounded",
            RequiredContentProtocolIsCanonicalAndBounded);
        Run("ColdCacheContentProofGatesAdmissionAndMatchesCabinOracle",
            ColdCacheContentProofGatesAdmissionAndMatchesCabinOracle);
        Run("WarmCacheReconnectReusesBytesButProvesFreshSession",
            WarmCacheReconnectReusesBytesButProvesFreshSession);
        Run("FailedContentAcquisitionCannotUnlockGuestSemantics",
            FailedContentAcquisitionCannotUnlockGuestSemantics);
        Run("PriorSessionContentProofCannotUnlockReconnect",
            PriorSessionContentProofCannotUnlockReconnect);
        Run("MismatchedSourceCandidateCannotPublish",
            MismatchedSourceCandidateCannotPublish);
        Run("RemoteDefaultTransfersNothingAndPreservesGameSemantics",
            RemoteDefaultTransfersNothingAndPreservesGameSemantics);
        Run("RemoteReplaceOverServerHttpMatchesLocalOracle",
            RemoteReplaceOverServerHttpMatchesLocalOracle);
        Run("RemoteReplaceOverExternalHttpsMatchesServerAndLocal",
            RemoteReplaceOverExternalHttpsMatchesServerAndLocal);
        Run("RemoteAddOverExternalHttpsMatchesLocalOracle",
            RemoteAddOverExternalHttpsMatchesLocalOracle);
        Run("HttpSourcePolicyAndEnvelopeFailureStayFailClosed",
            HttpSourcePolicyAndEnvelopeFailureStayFailClosed);
        Run("RemoteGranularHttpAcquisitionUsesCanonicalManifestAndBlobs",
            RemoteGranularHttpAcquisitionUsesCanonicalManifestAndBlobs);
        Run("UiDefinitionArtifactMatchesAcrossLocalCacheHttpAndHttps",
            UiDefinitionArtifactMatchesAcrossLocalCacheHttpAndHttps);
        Run("ContentBackedRemoteInventoryUsesHotOpenAndGameActions",
            ContentBackedRemoteInventoryUsesHotOpenAndGameActions);
        Run("StreamedWorldPackPublishesBeforeGuestAndMatchesAuthority",
            StreamedWorldPackPublishesBeforeGuestAndMatchesAuthority);
        Run("StreamedWorldPackCapacityFailurePublishesNothing",
            StreamedWorldPackCapacityFailurePublishesNothing);
    }

    private static void RemotePlayableProtocolIsCanonicalAndTrustMinimal()
    {
        var requested = new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Remote Alice");
        var encoded = new byte[UltraSimGameProtocol.MaximumGuestAdmissionMessageBytes];
        True(UltraSimGameProtocol.TryWriteGuestAdmissionRequest(
            encoded, 11, 1, in requested, out int written));
        True(UltraSimGameProtocol.TryReadGuestAdmissionRequest(
            encoded.AsSpan(0, written), out RemoteGuestAdmissionRequestHeader header,
            out GameGuestAdmissionRequest decoded));
        Equal(11UL, header.ApplicationSessionEpoch);
        Equal(1UL, header.AttemptSequence);
        Equal(requested, decoded);
        encoded[written] = 0;
        False(UltraSimGameProtocol.TryReadGuestAdmissionRequest(
            encoded.AsSpan(0, written + 1), out _, out _));

        var command = new GamePlayerCommand(
            new GameControlSessionEpoch(22), 1,
            GameLocomotionUpdate.CreateDirect(1_000, -1_000),
            GamePlayerActions.Interact,
            GamePlayerActions.Interact);
        RemotePlayableCommandMessage remote = UltraSimGameProtocol.CreatePlayableCommand(
            11, in command, new RemotePublicEntityTarget(7, 3),
            RemotePlayableCommandFlags.FaceRequested, 100, 200);
        True(UltraSimGameProtocol.TryCreatePlayableCommand(
            in remote, out GamePlayerCommand decodedCommand,
            out RemotePublicEntityTarget decodedTarget));
        Equal(command, decodedCommand);
        Equal(new RemotePublicEntityTarget(7, 3), decodedTarget);

        RemotePlayableCommandMessage malformed = remote;
        malformed.TargetPublicEntityGeneration = 0;
        False(UltraSimGameProtocol.TryCreatePlayableCommand(in malformed, out _, out _));
        malformed = remote;
        malformed.Flags = (RemotePlayableCommandFlags)0x80;
        False(UltraSimGameProtocol.TryCreatePlayableCommand(in malformed, out _, out _));
        malformed = remote;
        malformed.Flags = RemotePlayableCommandFlags.None;
        False(UltraSimGameProtocol.TryCreatePlayableCommand(in malformed, out _, out _));

        string[] ingressFields = typeof(RemoteGuestAdmissionRequestHeader)
            .GetFields()
            .Select(static field => field.Name)
            .OrderBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        Equal("ApplicationSessionEpoch,AttemptSequence,Body", string.Join(',', ingressFields));
        Equal(NetworkLane.Event, RemoteGuestAdmissionRequestHeaderCodec.Descriptor.Lane);
        Equal(NetworkLane.Event, RemoteGuestAdmissionResultHeaderCodec.Descriptor.Lane);
        Equal(NetworkLane.Control, RemoteGuestAdmissionAppliedMessageCodec.Descriptor.Lane);
        Equal(NetworkLane.Event, RemotePlayableCommandMessageCodec.Descriptor.Lane);
        Equal(NetworkLane.OwnerState, RemotePlayableOwnerStateMessageCodec.Descriptor.Lane);
    }

    private static void RemoteGuestAdmissionRejectsInvalidIdentityBeforeControl()
    {
        using var fixture = RemotePlayableFixture.Create();
        True(fixture.Playable.TrySubmitGuest(new GameGuestAdmissionRequest(
            GameGuestBody.HumanFemale, "invalid\u0001name")));
        fixture.DriveUntil(() =>
            fixture.Playable.GetSnapshot().State == RemotePlayableClientState.Rejected);
        RemotePlayableClientSnapshot rejected = fixture.Playable.GetSnapshot();
        False(rejected.Participant.IsValid);
        False(rejected.ControlSession.IsValid);
        False(rejected.PublicActor.IsValid);
        True(rejected.Detail?.Contains("control", StringComparison.OrdinalIgnoreCase) == true);
    }

    private static void RemoteGuestControlsConceptDemoAndAppliesPublicChatAndUi()
    {
        using var fixture = RemotePlayableFixture.Create();
        fixture.Admit("Remote Alice", GameGuestBody.HumanFemale);
        fixture.DriveUntil(() => fixture.Playable.GetSnapshot().StateSequence != 0);
        True(fixture.SessionSnapshot.SemanticConsumersActive);
        Equal(0UL, fixture.SessionSnapshot.RejectedPreAdmissionMessages);

        RemotePlayableClientSnapshot admitted = fixture.Playable.GetSnapshot();
        Equal("Remote Alice", admitted.GuestIdentity.DisplayName);
        Equal(GameGuestBody.HumanFemale, admitted.GuestIdentity.Body);
        True(admitted.Participant.IsValid);
        True(admitted.ControlSession.IsValid);
        True(admitted.PublicActor.IsValid);

        ulong beforeInvalidClientIntent = fixture.Playable.Input.Snapshot().LastSequence;
        False(fixture.Playable.TrySubmitCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true),
            flags: (RemotePlayableCommandFlags)0x80));
        Equal(beforeInvalidClientIntent, fixture.Playable.Input.Snapshot().LastSequence);

        fixture.DriveUntil(() =>
            fixture.TryFindPublic(GameWorldPublicPrefabIds.Character, out _) &&
            fixture.TryFindPublic(GameWorldPublicPrefabIds.CabinDoor, out _) &&
            fixture.TryFindPublic(GameWorldPublicPrefabIds.WorldChest, out _));
        Equal(0UL, fixture.World.GetSnapshot().RejectedDeliveries);

        WorldPosition before = admitted.Position;
        True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
            GamePlayerActions.None,
            true)));
        ulong movementSequence = fixture.Playable.Input.Snapshot().LastSequence;
        fixture.DriveUntil(() =>
        {
            RemotePlayableClientSnapshot current = fixture.Playable.GetSnapshot();
            return current.LastProcessedCommandSequence >= movementSequence &&
                current.Position.GlobalXMillimeters > before.GlobalXMillimeters;
        });

        True(fixture.Playable.TrySubmitCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        ulong inventorySequence = fixture.Playable.Input.Snapshot().LastSequence;
        fixture.DriveUntil(() => fixture.UiClient.OpenViewCount == 1 &&
            fixture.Playable.GetSnapshot().LastProcessedCommandSequence >= inventorySequence);

        // Replaying the exact retained Event must not execute the command twice or open a second
        // owner-private view. Packet reliability is not the authority fence.
        var duplicateCommand = new GamePlayerCommand(
            admitted.ControlSession,
            inventorySequence,
            GameLocomotionUpdate.Stop,
            GamePlayerActions.None,
            GamePlayerActions.None);
        fixture.QueueRaw(UltraSimGameProtocol.CreatePlayableCommand(
            admitted.ApplicationSessionEpoch,
            in duplicateCommand,
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        fixture.DriveSteps(256);
        Equal(1, fixture.UiClient.OpenViewCount);

        True(fixture.Chat.TrySubmit("remote hello", ClientChatSpeechMode.Say));
        fixture.DriveUntil(() => fixture.ChatState.GetSnapshot().Applied == 1);
        True(fixture.Chat.TrySubmit("/where", ClientChatSpeechMode.Say));
        fixture.DriveUntil(() => fixture.ChatState.GetSnapshot().Applied == 2);
        var rows = new ClientChatOccurrence[2];
        Equal(2, fixture.ChatState.CopyRecent(rows));
        Equal(ClientChatOccurrenceKind.SpatialSpeech, rows[0].Kind);
        Equal("Remote Alice", rows[0].DisplayLabel);
        Equal("remote hello", rows[0].Text);
        Equal(ClientChatOccurrenceKind.PrivateSystem, rows[1].Kind);
        True(rows[1].Text.StartsWith("Where:", StringComparison.Ordinal));
        Equal("System", rows[1].DisplayLabel);

        // A stale application epoch is rejected before it can advance the authoritative command
        // fence. The next legitimate command may reuse that numeric sequence in this session.
        ulong nextSequence = fixture.Playable.Input.Snapshot().LastSequence + 1;
        var stale = new GamePlayerCommand(
            admitted.ControlSession,
            nextSequence,
            GameLocomotionUpdate.Unchanged,
            GamePlayerActions.None,
            GamePlayerActions.None);
        fixture.QueueRaw(UltraSimGameProtocol.CreatePlayableCommand(
            admitted.ApplicationSessionEpoch + 1, in stale));
        var future = stale with { Sequence = nextSequence + 100 };
        fixture.QueueRaw(UltraSimGameProtocol.CreatePlayableCommand(
            admitted.ApplicationSessionEpoch, in future));
        fixture.DriveSteps(128);
        True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Unchanged, GamePlayerActions.None, true)));
        Equal(nextSequence, fixture.Playable.Input.Snapshot().LastSequence);
        fixture.DriveUntil(() =>
            fixture.Playable.GetSnapshot().LastProcessedCommandSequence == nextSequence);
    }

    private static void RemotePlayableReconnectRejectsPriorApplicationLineage()
    {
        ulong oldApplication;
        GameControlSessionEpoch oldControl;
        using (var old = RemotePlayableFixture.Create())
        {
            old.Admit("First Guest", GameGuestBody.HumanMale);
            RemotePlayableClientSnapshot snapshot = old.Playable.GetSnapshot();
            oldApplication = snapshot.ApplicationSessionEpoch;
            oldControl = snapshot.ControlSession;
        }

        using var current = RemotePlayableFixture.Create();
        current.Admit("Second Guest", GameGuestBody.HumanFemale);
        RemotePlayableClientSnapshot fresh = current.Playable.GetSnapshot();
        True(fresh.ApplicationSessionEpoch != oldApplication);
        True(fresh.ControlSession != oldControl);

        var obsolete = new GamePlayerCommand(
            oldControl,
            1,
            GameLocomotionUpdate.Stop,
            GamePlayerActions.None,
            GamePlayerActions.None);
        current.QueueRaw(UltraSimGameProtocol.CreatePlayableCommand(
            oldApplication, in obsolete));
        current.DriveSteps(128);

        True(current.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop, GamePlayerActions.None, true)));
        current.DriveUntil(() =>
            current.Playable.GetSnapshot().LastProcessedCommandSequence == 1);
    }

    private static void RequiredContentProtocolIsCanonicalAndBounded()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        ContentPackageAcquisition acquisition = RemoteReplaceAcquisition(oracle);
        RemoteContentAuthorization authorization = RemoteContentAuthorizationProjection.Create(
            oracle.Replace.Plan, oracle.Replace.Catalog.Fingerprint, [acquisition]);
        var requirement = new RemoteContentRequirement(77, 1, authorization);
        var bytes = new byte[UltraSimGameProtocol.MaximumContentRequirementMessageBytes];
        True(UltraSimGameProtocol.TryWriteContentRequirement(
            bytes, in requirement, out int written));
        True(UltraSimGameProtocol.TryReadContentRequirement(
            bytes.AsSpan(0, written), out RemoteContentRequirement decoded));
        Equal(77UL, decoded.ApplicationSessionEpoch);
        Equal(1UL, decoded.RequirementSequence);
        Equal(CabinContentOracle.FixtureRealm.High, decoded.Authorization.RealmHigh);
        Equal(oracle.Replace.Plan.Activation.High, decoded.Authorization.ActivationHigh);
        Equal(oracle.Replace.Plan.Packages[0].PackageId.Value,
            decoded.Authorization.Packages[0].PackageId);
        Equal(RemoteContentSourceKind.ExternalHttps,
            decoded.Authorization.Packages[0].Sources[0].Kind);
        bytes[written] = 0;
        False(UltraSimGameProtocol.TryReadContentRequirement(
            bytes.AsSpan(0, written + 1), out _));

        False(UltraSimGameProtocol.IsValidContentApplied(default));
        RemoteContentHash plan = RemoteContentAuthorizationProjection.ToRemote(
            oracle.Replace.Plan.PlanHash);
        RemoteContentHash resolved = RemoteContentAuthorizationProjection.ToRemote(
            oracle.Replace.Catalog.Fingerprint);
        var proof = new RemoteContentSetAppliedMessage
        {
            ApplicationSessionEpoch = 77,
            RequirementSequence = 1,
            ApplicationRealmHigh = CabinContentOracle.FixtureRealm.High,
            ApplicationRealmLow = CabinContentOracle.FixtureRealm.Low,
            ActivationHigh = oracle.Replace.Plan.Activation.High,
            ActivationLow = oracle.Replace.Plan.Activation.Low,
            PlanHash0 = plan.Part0,
            PlanHash1 = plan.Part1,
            PlanHash2 = plan.Part2,
            PlanHash3 = plan.Part3,
            ResolvedHash0 = resolved.Part0,
            ResolvedHash1 = resolved.Part1,
            ResolvedHash2 = resolved.Part2,
            ResolvedHash3 = resolved.Part3,
            Status = RemoteContentApplyStatus.Applied,
        };
        True(UltraSimGameProtocol.IsValidContentApplied(in proof));
        Equal(NetworkLane.Event, RemoteContentRequirementHeaderCodec.Descriptor.Lane);
        Equal(NetworkLane.Control, RemoteContentSetAppliedMessageCodec.Descriptor.Lane);
        Equal((ushort)18, UltraSimGameProtocol.Manifest.Version);
    }

    private static void ColdCacheContentProofGatesAdmissionAndMatchesCabinOracle()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        var source = new CabinReplaceSource(oracle);
        using var fixture = RemotePlayableFixture.CreateWithContent(oracle, source);

        False(fixture.TrySubmitGuest("Too Early", GameGuestBody.HumanFemale));
        fixture.DriveUntil(() => fixture.Content!.GetSnapshot().State == RemoteContentClientState.Applied);
        RemoteContentClient content = fixture.Content!;
        RemoteContentClientSnapshot applied = content.GetSnapshot();
        Equal(oracle.Replace.Catalog.Fingerprint, applied.ResolvedCatalogFingerprint);
        Equal(1, applied.Acquisition.PackagesFetched);
        Equal(0, applied.Acquisition.PackagesReused);
        Equal(1, applied.Acquisition.FilesFetched);
        True(applied.Acquisition.BytesFetched > 0);
        Equal(1, source.Fetches);

        fixture.Admit("Remote Content Alice", GameGuestBody.HumanFemale);
        True(fixture.SessionSnapshot.RequiredContentApplied);
        True(fixture.SessionSnapshot.SemanticConsumersActive);
        Equal(oracle.Replace.Catalog.Fingerprint, content.Catalog.Fingerprint);
        Console.WriteLine(
            $"A0.3 COLD packages_fetched={applied.Acquisition.PackagesFetched} " +
            $"files_fetched={applied.Acquisition.FilesFetched} bytes={applied.Acquisition.BytesFetched} " +
            $"fingerprint={applied.ResolvedCatalogFingerprint}");
    }

    private static void WarmCacheReconnectReusesBytesButProvesFreshSession()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        string root = TemporaryDirectory("ultrasim-a03-warm");
        ulong firstEpoch;
        try
        {
            var coldSource = new CabinReplaceSource(oracle);
            using (var first = RemotePlayableFixture.CreateWithContent(
                       oracle, coldSource, root, preserveContentRoot: true))
            {
                first.DriveUntil(() => first.Content!.Applied);
                firstEpoch = first.SessionSnapshot.Content.ApplicationSessionEpoch;
                Equal(1, coldSource.Fetches);
                first.Admit("Cold Guest", GameGuestBody.HumanFemale);
            }

            var warmSource = new CabinReplaceSource(oracle);
            using (var second = RemotePlayableFixture.CreateWithContent(
                       oracle, warmSource, root, preserveContentRoot: true))
            {
                second.DriveUntil(() => second.Content!.Applied);
                RemoteContentClientSnapshot warm = second.Content!.GetSnapshot();
                True(warm.ApplicationSessionEpoch != firstEpoch);
                Equal(0, warmSource.Fetches);
                Equal(0, warm.Acquisition.PackagesFetched);
                Equal(1, warm.Acquisition.PackagesReused);
                True(warm.ProofsQueued >= 1);
                Equal(oracle.Replace.Catalog.Fingerprint, warm.ResolvedCatalogFingerprint);
                second.Admit("Warm Guest", GameGuestBody.HumanMale);
                Console.WriteLine(
                    $"A0.3 WARM packages_reused={warm.Acquisition.PackagesReused} " +
                    $"bytes=0 fresh_epoch={warm.ApplicationSessionEpoch}");
            }
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void FailedContentAcquisitionCannotUnlockGuestSemantics()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        var source = new CabinReplaceSource(oracle, fail: true);
        using var fixture = RemotePlayableFixture.CreateWithContent(oracle, source);
        fixture.DriveUntil(() => fixture.Content!.GetSnapshot().State == RemoteContentClientState.Rejected);
        Equal(ContentActivationAcquisitionStatus.SourceFailure,
            fixture.Content!.GetSnapshot().Acquisition.Status);
        False(fixture.TrySubmitGuest("Blocked Guest", GameGuestBody.HumanFemale));
        fixture.QueueRawGuest("Hostile Bypass", GameGuestBody.HumanFemale);
        fixture.DriveSteps(512);
        Equal(RemotePlayableClientState.AwaitingAdmission, fixture.Playable.GetSnapshot().State);
        False(fixture.SessionSnapshot.SemanticConsumersActive);
    }

    private static void PriorSessionContentProofCannotUnlockReconnect()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        RemoteContentSetAppliedMessage stale;
        using (var first = RemotePlayableFixture.CreateWithContent(
                   oracle, new CabinReplaceSource(oracle)))
        {
            first.DriveUntil(() => first.Content!.Applied);
            stale = CreateAppliedProof(oracle, first.Content!.GetSnapshot().ApplicationSessionEpoch);
        }

        using var current = RemotePlayableFixture.CreateWithContent(
            oracle, new CabinReplaceSource(oracle, fail: true));
        current.DriveUntil(() =>
            current.Content!.GetSnapshot().State == RemoteContentClientState.Rejected);
        True(current.Content!.GetSnapshot().ApplicationSessionEpoch != stale.ApplicationSessionEpoch);
        current.QueueRaw(in stale);
        current.QueueRawGuest("Stale Proof Guest", GameGuestBody.HumanFemale);
        current.DriveSteps(512);
        Equal(RemotePlayableClientState.AwaitingAdmission, current.Playable.GetSnapshot().State);
        False(current.SessionSnapshot.SemanticConsumersActive);
    }

    private static void MismatchedSourceCandidateCannotPublish()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        var source = new CabinReplaceSource(oracle, wrongPackage: true);
        using var fixture = RemotePlayableFixture.CreateWithContent(oracle, source);
        fixture.DriveUntil(() =>
            fixture.Content!.GetSnapshot().State == RemoteContentClientState.Rejected);
        Equal(ContentActivationAcquisitionStatus.CandidateMismatch,
            fixture.Content!.GetSnapshot().Acquisition.Status);
        ContentCacheSnapshot cache = fixture.ContentStore!.GetSnapshot();
        Equal(0, cache.BlobCount);
        Equal(0, cache.RealmManifestCount);
        Equal(0, cache.RealmActivationCount);
        False(fixture.SessionSnapshot.SemanticConsumersActive);
    }

    private static void RemoteDefaultTransfersNothingAndPreservesGameSemantics()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        var source = new RejectingSource();
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Default, [], source);
        fixture.DriveUntil(() => fixture.Content!.Applied && fixture.SessionSnapshot.RequiredContentApplied);
        RemoteContentClientSnapshot content = fixture.Content!.GetSnapshot();
        Equal(oracle.Default.Catalog.Fingerprint, content.ResolvedCatalogFingerprint);
        Equal(0, content.Acquisition.PackagesRequired);
        Equal(0, content.Acquisition.PackagesFetched);
        Equal(0, content.Acquisition.FilesFetched);
        Equal(0L, content.Acquisition.BytesFetched);
        Equal(0, source.Fetches);
        AssertCatalogEqual(oracle.Default.Catalog, fixture.Content.Catalog);
        AssertBasicRemoteGameUnaffected(fixture, "Default Guest");
        Console.WriteLine(
            $"EVIDENCE A0.4 scenario=Default source=none transfer=0 " +
            $"plan={oracle.Default.Plan.PlanHash} fingerprint={content.ResolvedCatalogFingerprint}");
    }

    private static void RemoteReplaceOverServerHttpMatchesLocalOracle()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        byte[] archive = Archive(oracle.ReplacePackage);
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/cabin-replace.uspkg", archive)]);
        var acquisition = Acquisition(
            oracle.ReplacePackage, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/cabin-replace.uspkg").ToString());
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Replace, [acquisition], source);
        fixture.DriveUntil(() => fixture.Content!.Applied && fixture.SessionSnapshot.RequiredContentApplied);
        AssertCatalogEqual(oracle.Replace.Catalog, fixture.Content!.Catalog);
        RemoteContentClientSnapshot content = fixture.Content!.GetSnapshot();
        Equal(ContentSourceKind.ServerHostedStatic, content.Acquisition.SelectedSource.Kind);
        Equal(1, content.Acquisition.PackagesFetched);
        Equal(1, content.Acquisition.FilesFetched);
        Equal(oracle.ReplacePackage.Payloads.Sum(static value => (long)value.Bytes.Length),
            content.Acquisition.BytesFetched);
        Equal((long)archive.Length, source.GetSnapshot().ArchiveBytesRead);
        Equal(1UL, host.GetSnapshot().Served);
        AssertBasicRemoteGameUnaffected(fixture, "HTTP Replace Guest");
        Console.WriteLine(
            $"EVIDENCE A0.4 scenario=Replace source=ServerHostedStatic " +
            $"package={oracle.ReplacePackage.Manifest.PackageId} manifest={oracle.ReplacePackage.Manifest.ManifestHash} " +
            $"file={oracle.ReplacePackage.Manifest.Files[0].Sha256} " +
            $"plan={oracle.Replace.Plan.PlanHash} " +
            $"payload_bytes={content.Acquisition.BytesFetched} archive_bytes={archive.Length} " +
            $"fingerprint={content.ResolvedCatalogFingerprint}");
    }

    private static void RemoteReplaceOverExternalHttpsMatchesServerAndLocal()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        byte[] archive = Archive(oracle.ReplacePackage);
        ContentHash localFingerprint;
        ContentPackageAcquisition localAcquisition = Acquisition(
            oracle.ReplacePackage, ContentSourceKind.Local, "packages/replace.uspkg");
        string localRoot = TemporaryDirectory("ultrasim-a04-local-source");
        Directory.CreateDirectory(Path.Combine(localRoot, "packages"));
        try
        {
            File.WriteAllBytes(Path.Combine(localRoot, "packages", "replace.uspkg"), archive);
            var localSource = new LocalContentPackageSource(localRoot);
            using var local = RemotePlayableFixture.CreateWithContent(
                oracle, oracle.Replace, [localAcquisition], localSource);
            local.DriveUntil(() => local.Content!.Applied && local.SessionSnapshot.RequiredContentApplied);
            AssertCatalogEqual(oracle.Replace.Catalog, local.Content!.Catalog);
            localFingerprint = local.Content.GetSnapshot().ResolvedCatalogFingerprint;
        }
        finally
        {
            if (Directory.Exists(localRoot)) Directory.Delete(localRoot, recursive: true);
        }

        using var host = new LoopbackHttpsContentHost("/content/cabin-replace.uspkg", archive);
        var externalAcquisition = Acquisition(
            oracle.ReplacePackage, ContentSourceKind.ExternalHttps, host.ResourceUri.ToString());
        using var externalSource = new HttpContentPackageSource(
            host.CreatePinnedCertificateValidation(), FixtureHttpLimits(archive.Length, allowInsecureHttp: false));
        using var external = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Replace, [externalAcquisition], externalSource);
        external.DriveUntil(() =>
            external.Content!.Applied && external.SessionSnapshot.RequiredContentApplied);
        AssertCatalogEqual(oracle.Replace.Catalog, external.Content!.Catalog);
        RemoteContentClientSnapshot content = external.Content.GetSnapshot();
        Equal(ContentSourceKind.ExternalHttps, content.Acquisition.SelectedSource.Kind);
        Equal(localFingerprint, content.ResolvedCatalogFingerprint);
        Equal(oracle.Replace.Catalog.Fingerprint, content.ResolvedCatalogFingerprint);
        Equal((long)archive.Length, externalSource.GetSnapshot().ArchiveBytesRead);
        AssertBasicRemoteGameUnaffected(external, "HTTPS Replace Guest");

        RemoteContentAuthorization localAuthorization = RemoteContentAuthorizationProjection.Create(
            oracle.Replace.Plan, oracle.Replace.Catalog.Fingerprint, [localAcquisition]);
        RemoteContentAuthorization externalAuthorization = RemoteContentAuthorizationProjection.Create(
            oracle.Replace.Plan, oracle.Replace.Catalog.Fingerprint, [externalAcquisition]);
        Equal(localAuthorization.PlanHash, externalAuthorization.PlanHash);
        Equal(localAuthorization.ExpectedResolvedCatalogFingerprint,
            externalAuthorization.ExpectedResolvedCatalogFingerprint);
        True(localAuthorization.Packages[0].Sources[0].Locator !=
            externalAuthorization.Packages[0].Sources[0].Locator);
        Console.WriteLine(
            $"EVIDENCE A0.4 scenario=Replace source=ExternalHttps archive_bytes={archive.Length} " +
            $"manifest={oracle.ReplacePackage.Manifest.ManifestHash} plan={oracle.Replace.Plan.PlanHash} " +
            $"fingerprint={content.ResolvedCatalogFingerprint} local_source_parity=true");
    }

    private static void RemoteAddOverExternalHttpsMatchesLocalOracle()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        byte[] archive = Archive(oracle.AddPackage);
        using var host = new LoopbackHttpsContentHost("/content/cabin-add.uspkg", archive);
        var acquisition = Acquisition(
            oracle.AddPackage, ContentSourceKind.ExternalHttps, host.ResourceUri.ToString());
        using var source = new HttpContentPackageSource(
            host.CreatePinnedCertificateValidation(), FixtureHttpLimits(archive.Length, allowInsecureHttp: false));
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Add, [acquisition], source);
        fixture.DriveUntil(() => fixture.Content!.Applied && fixture.SessionSnapshot.RequiredContentApplied);
        AssertCatalogEqual(oracle.Add.Catalog, fixture.Content!.Catalog);
        foreach (LogicalContentId id in new[]
                 {
                     CabinContentIds.BlueGlowMaterial,
                     CabinContentIds.BlueGlowTexture,
                     CabinContentIds.StreamedBadgeIcon,
                 })
        {
            True(fixture.Content.Catalog.TryGet(id, out ResolvedContentEntry? entry));
            Equal(oracle.AddPackage.Manifest.PackageId, entry!.OwnerPackageId);
        }
        AssertBasicRemoteGameUnaffected(fixture, "HTTPS Add Guest");
        RemoteContentClientSnapshot content = fixture.Content!.GetSnapshot();
        Console.WriteLine(
            $"EVIDENCE A0.4 scenario=Add source=ExternalHttps files={content.Acquisition.FilesFetched} " +
            $"package={oracle.AddPackage.Manifest.PackageId} manifest={oracle.AddPackage.Manifest.ManifestHash} " +
            $"file0={oracle.AddPackage.Manifest.Files[0].Sha256} " +
            $"file1={oracle.AddPackage.Manifest.Files[1].Sha256} " +
            $"plan={oracle.Add.Plan.PlanHash} " +
            $"payload_bytes={content.Acquisition.BytesFetched} archive_bytes={archive.Length} " +
            $"fingerprint={content.ResolvedCatalogFingerprint}");
    }

    private static void HttpSourcePolicyAndEnvelopeFailureStayFailClosed()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        byte[] archive = Archive(oracle.ReplacePackage);
        archive[^1] ^= 0x7F;
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/damaged.uspkg", archive)]);
        var acquisition = Acquisition(
            oracle.ReplacePackage, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/damaged.uspkg").ToString());

        using (var denied = new HttpContentPackageSource())
        {
            var deniedRequest = new ContentPackageFetchRequest(
                CabinContentOracle.FixtureRealm, oracle.ReplacePackage.Manifest.PackageId,
                oracle.ReplacePackage.Manifest.ManifestHash, acquisition.Sources[0]);
            False(denied.TryFetch(in deniedRequest, out _, out ContentSourceFetchError policyError));
            Equal(ContentSourceFetchError.InvalidRequest, policyError);
            Equal(0L, denied.GetSnapshot().ArchiveBytesRead);
        }

        using (var bounded = new HttpContentPackageSource(new ContentPackageSourceLimits(
                   maximumArchiveBytes: archive.Length - 1,
                   requestTimeoutMilliseconds: 10_000,
                   allowInsecureServerHostedHttp: true,
                   allowPrivateNetworkTargets: true)))
        {
            var boundedRequest = new ContentPackageFetchRequest(
                CabinContentOracle.FixtureRealm, oracle.ReplacePackage.Manifest.PackageId,
                oracle.ReplacePackage.Manifest.ManifestHash, acquisition.Sources[0]);
            False(bounded.TryFetch(in boundedRequest, out _, out ContentSourceFetchError capacityError));
            Equal(ContentSourceFetchError.CapacityExceeded, capacityError);
            Equal(0L, bounded.GetSnapshot().ArchiveBytesRead);
        }

        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Replace, [acquisition], source);
        fixture.DriveUntil(() =>
            fixture.Content!.GetSnapshot().State == RemoteContentClientState.Rejected);
        Equal(ContentActivationAcquisitionStatus.SourceFailure,
            fixture.Content!.GetSnapshot().Acquisition.Status);
        Equal(ContentSourceFetchError.SourceFailure,
            fixture.Content.GetSnapshot().Acquisition.SourceError);
        ContentCacheSnapshot cache = fixture.ContentStore!.GetSnapshot();
        Equal(0, cache.BlobCount);
        Equal(0, cache.RealmManifestCount);
        Equal(0, cache.RealmActivationCount);
        False(fixture.SessionSnapshot.SemanticConsumersActive);
    }

    private static void RemoteGranularHttpAcquisitionUsesCanonicalManifestAndBlobs()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        True(ContentPackageManifestEnvelope.TryEncodeCanonical(
            oracle.AddPackage.Manifest, out byte[] manifestBytes));
        var resources = new List<StaticContentHttpResource>
        {
            new("/content/add/manifest.usmanifest", manifestBytes),
        };
        foreach (ContentFilePayload payload in oracle.AddPackage.Payloads)
        {
            ContentFileDescriptor descriptor = oracle.AddPackage.Manifest.Files.Single(
                value => value.Path == payload.Path);
            resources.Add(new StaticContentHttpResource(
                $"/content/add/blobs/{descriptor.Sha256}.blob", payload.Bytes));
        }
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()), resources);
        string root = new Uri(host.BaseUri, "content/add/").ToString();
        var acquisition = Acquisition(
            oracle.AddPackage, ContentSourceKind.ServerHostedStatic, root);
        int maximumBytes = Math.Max(manifestBytes.Length,
            oracle.AddPackage.Payloads.Max(static value => value.Bytes.Length));
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(maximumBytes, allowInsecureHttp: true));
        using var fixture = RemotePlayableFixture.CreateWithContent(
            oracle, oracle.Add, [acquisition], source);
        fixture.DriveUntil(() => fixture.Content!.GetSnapshot().State == RemoteContentClientState.Applied);
        RemoteContentClientSnapshot content = fixture.Content!.GetSnapshot();
        Equal(oracle.Add.Catalog.Fingerprint, content.ResolvedCatalogFingerprint);
        Equal(oracle.AddPackage.Payloads.Count, content.Acquisition.FilesFetched);
        Equal(1, content.Acquisition.SourceAttempts);
        Equal(oracle.AddPackage.Payloads.Sum(static value => (long)value.Bytes.Length),
            content.Acquisition.BytesFetched);
        ContentPackageSourceSnapshot sourceSnapshot = source.GetSnapshot();
        Equal((long)manifestBytes.Length, sourceSnapshot.ManifestBytesRead);
        Equal(content.Acquisition.BytesFetched, sourceSnapshot.PayloadBytesRead);
        True(sourceSnapshot.PeakConcurrentRequests is >= 1 and <= 4);
        Console.WriteLine($"EVIDENCE A0.5 granular_http files={content.Acquisition.FilesFetched} " +
            $"payload_bytes={content.Acquisition.BytesFetched} manifest_bytes={sourceSnapshot.ManifestBytesRead} " +
            $"peak_parallel={sourceSnapshot.PeakConcurrentRequests} fingerprint={content.ResolvedCatalogFingerprint}");
    }

    private static void UiDefinitionArtifactMatchesAcrossLocalCacheHttpAndHttps()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        UiContentFixture fixture = CreateInventoryUiContent(oracle);
        byte[] archive = Archive(fixture.Manifest, fixture.Payloads);
        string localRoot = TemporaryDirectory("ultrasim-a06-local-source");
        string cacheRoot = TemporaryDirectory("ultrasim-a06-warm-cache");
        Directory.CreateDirectory(Path.Combine(localRoot, "packages"));
        try
        {
            File.WriteAllBytes(Path.Combine(localRoot, "packages", "inventory.uspkg"), archive);
            var localAcquisition = Acquisition(
                fixture.Manifest, ContentSourceKind.Local, "packages/inventory.uspkg");
            var localSource = new LocalContentPackageSource(localRoot);
            ResolveUiDefinition(
                oracle.BaseCatalog, fixture, localAcquisition, localSource, cacheRoot,
                out ContentHash localFingerprint, out ContentActivationAcquisitionResult cold,
                preserveCacheRoot: true);
            Equal(1, cold.FilesFetched);

            // Persistent bytes reopen, but catalog/application publication is performed again.
            using (ContentCache warmCache = OpenCache(cacheRoot))
            {
                var unavailable = new RejectingSource();
                True(ContentActivationAcquirer.TryAcquireAndPublish(
                    warmCache, oracle.BaseCatalog, fixture.Plan, [localAcquisition], unavailable,
                    false, out ContentCatalogLease? lease,
                    out ContentActivationAcquisitionResult warm));
                using (lease)
                {
                    Equal(0, warm.FilesFetched);
                    AssertInventoryDefinition(lease!, fixture.Definition);
                }
                Equal(0, unavailable.Fetches);
            }

            using var httpHost = new StaticContentHttpHost(
                new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
                [new StaticContentHttpResource("/content/inventory.uspkg", archive)]);
            var httpAcquisition = Acquisition(
                fixture.Manifest, ContentSourceKind.ServerHostedStatic,
                httpHost.GetUri("/content/inventory.uspkg").ToString());
            using var httpSource = new HttpContentPackageSource(
                FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
            ResolveUiDefinition(
                oracle.BaseCatalog, fixture, httpAcquisition, httpSource,
                TemporaryDirectory("ultrasim-a06-http-cache"),
                out ContentHash httpFingerprint, out _);

            using var httpsHost = new LoopbackHttpsContentHost(
                "/content/inventory.uspkg", archive);
            var httpsAcquisition = Acquisition(
                fixture.Manifest, ContentSourceKind.ExternalHttps,
                httpsHost.ResourceUri.ToString());
            using var httpsSource = new HttpContentPackageSource(
                httpsHost.CreatePinnedCertificateValidation(),
                FixtureHttpLimits(archive.Length, allowInsecureHttp: false));
            ResolveUiDefinition(
                oracle.BaseCatalog, fixture, httpsAcquisition, httpsSource,
                TemporaryDirectory("ultrasim-a06-https-cache"),
                out ContentHash httpsFingerprint, out _);

            Equal(fixture.Catalog.Fingerprint, localFingerprint);
            Equal(localFingerprint, httpFingerprint);
            Equal(localFingerprint, httpsFingerprint);
            Console.WriteLine(
                $"EVIDENCE A0.6 ui_definition bytes={fixture.ArtifactBytes.Length} " +
                $"definition={fixture.Definition.Key}/v{fixture.Definition.Version} " +
                $"hash={fixture.Definition.Hash} artifact_sha={ContentHash.Compute(fixture.ArtifactBytes)} " +
                $"manifest={fixture.Manifest.ManifestHash} fingerprint={localFingerprint} " +
                "origins=local,warm-cache,server-http,external-https");
        }
        finally
        {
            if (Directory.Exists(localRoot)) Directory.Delete(localRoot, recursive: true);
            if (Directory.Exists(cacheRoot)) Directory.Delete(cacheRoot, recursive: true);
        }
    }

    private static void ContentBackedRemoteInventoryUsesHotOpenAndGameActions()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        UiContentFixture content = CreateInventoryUiContent(oracle);
        byte[] archive = Archive(content.Manifest, content.Payloads);
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/inventory.uspkg", archive)]);
        ContentPackageAcquisition acquisition = Acquisition(
            content.Manifest, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/inventory.uspkg").ToString());
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        using var remote = RemotePlayableFixture.CreateWithContent(
            CabinContentOracle.FixtureRealm,
            oracle.BaseCatalog,
            content.Plan,
            content.Catalog.Fingerprint,
            [acquisition],
            source,
            [UiDefinitionIdentity.From(content.Definition)]);
        remote.DriveUntil(() =>
            remote.Content!.Applied && remote.SessionSnapshot.RequiredContentApplied);
        remote.Admit("UI Content Guest", GameGuestBody.HumanFemale);
        True(remote.Playable.TrySubmitCommand(
            new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        remote.DriveUntil(() => remote.UiClient.OpenViewCount == 1);
        RemoteUiClientSnapshot ui = remote.SessionSnapshot.Ui;
        Equal(1UL, ui.ContentDefinitionsResolved);
        Equal(1, remote.UiClient.CachedDefinitionCount);

        var view = new UiViewInstanceId(1);
        _ = remote.UiClient.GetView(view);
        ReadOnlySpan<UiListRow> rows = remote.UiClient.GetRows(view, InventoryUi.Items);
        True(rows.Length != 0);
        ulong before = remote.UiClient.GetView(view).LastSequence;
        remote.UiClient.SubmitAction(view, InventoryUi.Select, rows[0].Id);
        remote.DriveUntil(() => remote.UiClient.GetView(view).LastSequence > before);
        Equal("Item selected.", remote.UiClient.GetValue(view, InventoryUi.Status).Text);
        Console.WriteLine(
            $"EVIDENCE A0.6 remote_inventory hot_content_resolutions={ui.ContentDefinitionsResolved} " +
            $"cached_definitions={remote.UiClient.CachedDefinitionCount} " +
            $"action_model_sequence={remote.UiClient.GetView(view).LastSequence}");
    }

    private static void StreamedWorldPackPublishesBeforeGuestAndMatchesAuthority()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        WorldContentFixture world = CreateConceptWorldContent(oracle);
        byte[] archive = Archive(world.Manifest, world.Payloads);
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/concept-world.uspkg", archive)]);
        ContentPackageAcquisition acquisition = Acquisition(
            world.Manifest, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/concept-world.uspkg").ToString());
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        var requiredWorld = new RemoteRequiredWorldBinding(
            world.DefinitionId,
            world.Pack.Fingerprint,
            world.Binding,
            new ClientWorldSemanticCacheLimits(192, 256, 32, 64, 4_096));
        using var remote = RemotePlayableFixture.CreateWithWorldContent(
            CabinContentOracle.FixtureRealm,
            oracle.BaseCatalog,
            world.Plan,
            world.Catalog.Fingerprint,
            [acquisition],
            source,
            requiredWorld);

        Equal(RemoteStructuralWorldState.AwaitingAppliedCatalog,
            remote.SessionSnapshot.StructuralWorld.State);
        False(remote.TrySubmitGuest("Too Early", GameGuestBody.HumanFemale));
        remote.DriveUntil(() =>
            remote.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Ready);
        RemotePlayableSessionSnapshot ready = remote.SessionSnapshot;
        True(ready.RequiredContentApplied);
        True(ready.StructuralWorld.Resolution.IsResolved);
        Equal(ClientSemanticChunkError.None, ready.StructuralWorld.PublicationError);
        Equal(world.Pack.Fingerprint, ready.StructuralWorld.WorldPackFingerprint);
        Equal(world.Pack.ContentFingerprint, ready.StructuralWorld.WorldContentFingerprint);
        Equal(world.Pack.ChunkCount, ready.StructuralWorld.PublishedStructuralChunks);
        Equal(world.Pack.ChunkCount, remote.Cache.GetSnapshot().ResidentChunks);
        Equal(1, remote.Content!.GetSnapshot().Acquisition.FilesFetched);
        Equal((long)world.Bytes.Length, remote.Content.GetSnapshot().Acquisition.BytesFetched);

        ClientWorldSemanticCache local = RemotePlayableFixture.CreatePreinstalledWorldCache();
        ClientWorldSemanticCacheSnapshot streamedSnapshot = remote.Cache.GetSnapshot();
        ClientWorldSemanticCacheSnapshot localSnapshot = local.GetSnapshot();
        Equal(localSnapshot.BaseWorldFingerprint, streamedSnapshot.BaseWorldFingerprint);
        Equal(localSnapshot.WorldSemanticHash, streamedSnapshot.WorldSemanticHash);
        Equal(localSnapshot.ResidentChunks, streamedSnapshot.ResidentChunks);
        Equal(local.BaseWorld.FrozenStaticCount, remote.Cache.BaseWorld.FrozenStaticCount);
        Equal(local.BaseWorld.MultiInstanceCount, remote.Cache.BaseWorld.MultiInstanceCount);
        Equal(local.BaseWorld.MultiFragmentCount, remote.Cache.BaseWorld.MultiFragmentCount);
        Equal(local.BaseWorld.AdditionalTerrainSurfaceFragmentCount,
            remote.Cache.BaseWorld.AdditionalTerrainSurfaceFragmentCount);

        remote.Admit("Streamed World Guest", GameGuestBody.HumanFemale);
        remote.DriveUntil(() =>
            remote.TryFindPublic(GameWorldPublicPrefabIds.Character, out _) &&
            remote.World.GetSnapshot().AppliedSnapshots != 0);
        Console.WriteLine(
            $"EVIDENCE A0.7 worldpack_bytes={world.Bytes.Length} " +
            $"blob_sha={ContentHash.Compute(world.Bytes)} manifest={world.Manifest.ManifestHash} " +
            $"plan={world.Plan.PlanHash} catalog={world.Catalog.Fingerprint} " +
            $"pack={world.Pack.Fingerprint} " +
            $"content={world.Pack.ContentFingerprint} chunks={world.Pack.ChunkCount} " +
            $"statics={world.Pack.FrozenStaticCount} multis={world.Pack.MultiInstanceCount}/" +
            $"{world.Pack.MultiFragmentCount} surfaces={world.Pack.AdditionalTerrainSurfaceFragmentCount} " +
            $"semantic={streamedSnapshot.WorldSemanticHash} dynamic_snapshots=" +
            $"{remote.World.GetSnapshot().AppliedSnapshots}");
    }

    private static void StreamedWorldPackCapacityFailurePublishesNothing()
    {
        CabinContentOracle oracle = CabinContentOracle.Create();
        WorldContentFixture world = CreateConceptWorldContent(oracle);
        byte[] archive = Archive(world.Manifest, world.Payloads);
        using var host = new StaticContentHttpHost(
            new IPEndPoint(IPAddress.Loopback, ReserveLoopbackPort()),
            [new StaticContentHttpResource("/content/concept-world.uspkg", archive)]);
        ContentPackageAcquisition acquisition = Acquisition(
            world.Manifest, ContentSourceKind.ServerHostedStatic,
            host.GetUri("/content/concept-world.uspkg").ToString());
        using var source = new HttpContentPackageSource(
            FixtureHttpLimits(archive.Length, allowInsecureHttp: true));
        var requiredWorld = new RemoteRequiredWorldBinding(
            world.DefinitionId,
            world.Pack.Fingerprint,
            world.Binding,
            new ClientWorldSemanticCacheLimits(
                world.Pack.ChunkCount - 1, 256, 32, 64, 4_096));
        using var remote = RemotePlayableFixture.CreateWithWorldContent(
            CabinContentOracle.FixtureRealm,
            oracle.BaseCatalog,
            world.Plan,
            world.Catalog.Fingerprint,
            [acquisition],
            source,
            requiredWorld);
        remote.DriveUntil(() =>
            remote.SessionSnapshot.StructuralWorld.State == RemoteStructuralWorldState.Rejected);
        RemotePlayableSessionSnapshot rejected = remote.SessionSnapshot;
        True(rejected.RequiredContentApplied);
        True(rejected.StructuralWorld.Resolution.IsResolved);
        Equal(ClientSemanticChunkError.CapacityExceeded,
            rejected.StructuralWorld.PublicationError);
        Equal(0, rejected.StructuralWorld.PublishedStructuralChunks);
        Equal(world.Pack.Fingerprint, rejected.StructuralWorld.WorldPackFingerprint);
        False(remote.TrySubmitGuest("No Partial World", GameGuestBody.HumanFemale));
    }

    private static WorldContentFixture CreateConceptWorldContent(CabinContentOracle oracle)
    {
        WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
        var bytes = new byte[pack.Length];
        pack.CopyTo(bytes);
        True(ContentFilePath.TryCreate(
            "world/concept-demo.usworldpack", out ContentFilePath path));
        True(ContentFileDescriptor.TryCreate(
            path, ContentFileKind.WorldPack, bytes.Length, ContentHash.Compute(bytes),
            default, out ContentFileDescriptor file));
        True(LogicalContentId.TryCreate(
            "server-world:world/concept-demo", out LogicalContentId definitionId));
        True(ContentDefinitionDescriptor.TryCreateFileDefinition(
            definitionId, ContentDefinitionKind.WorldPack, ContentDefinitionOperation.Add,
            path, out ContentDefinitionDescriptor? definition));
        True(ContentPackageId.TryCreate(
            "server.world.concept-demo", out ContentPackageId packageId));
        True(ContentNamespaceId.TryCreate(
            "server-world", out ContentNamespaceId contentNamespace));
        True(ContentPackageManifest.TryCreate(
            1, 1, packageId, contentNamespace,
            "Concept Demo canonical world", "1.0.0",
            ContentTrustClass.ServerAuthorizedPassive,
            [file], [definition!], out ContentPackageManifest? manifest, out _));
        var payloads = new[] { new ContentFilePayload(path, bytes) };
        True(ContentPackageValidator.TryValidatePayloads(
            manifest!, payloads, out VerifiedContentPackage? verified, out _));
        var requirement = new ContentActivationPackage(
            packageId, manifest!.ManifestHash, ContentRequirementPolicy.Required);
        True(ContentActivationPlan.TryCreate(
            CabinContentOracle.FixtureRealm,
            new ContentActivationId(0x4130_2E37_2D57_4F52UL, 1),
            oracle.BaseCatalog.Fingerprint,
            [requirement], out ContentActivationPlan? plan, out _));
        True(ContentCatalogResolver.TryResolve(
            oracle.BaseCatalog, plan!, [verified!], out ResolvedContentCatalog? catalog,
            out ContentCatalogResolutionError resolutionError));
        Equal(ContentCatalogResolutionError.None, resolutionError);
        return new WorldContentFixture(
            definitionId, pack, bytes, CreateWorldBinding(), manifest, payloads, plan!, catalog!);
    }

    private static WorldContentBinding CreateWorldBinding() => new(
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

    private static UiContentFixture CreateInventoryUiContent(CabinContentOracle oracle)
    {
        CompiledUiDefinition definition = InventoryUi.Definition;
        True(UiDefinitionArtifactCodec.TryEncodeCanonical(definition, out byte[] artifact));
        True(ContentUiDefinitionPath.TryCreate(
            UiDefinitionIdentity.From(definition), out ContentFilePath path));
        True(ContentFileDescriptor.TryCreate(
            path, ContentFileKind.UiDefinition, artifact.Length, ContentHash.Compute(artifact),
            default, out ContentFileDescriptor file));
        True(ContentPackageId.TryCreate(
            "server.ui.standard", out ContentPackageId packageId));
        True(ContentNamespaceId.TryCreate(
            "server-ui", out ContentNamespaceId contentNamespace));
        True(ContentPackageManifest.TryCreate(
            1, 1, packageId, contentNamespace,
            "Standard remote UI definitions", "1.0.0",
            ContentTrustClass.ServerAuthorizedPassive,
            [file], [], out ContentPackageManifest? manifest, out _));
        var payloads = new[] { new ContentFilePayload(path, artifact) };
        True(ContentPackageValidator.TryValidatePayloads(
            manifest!, payloads, out VerifiedContentPackage? verified, out _));
        var requirement = new ContentActivationPackage(
            packageId, manifest!.ManifestHash, ContentRequirementPolicy.Required);
        True(ContentActivationPlan.TryCreate(
            CabinContentOracle.FixtureRealm,
            new ContentActivationId(0x4130_2E36_2D55_4900UL, 1),
            oracle.BaseCatalog.Fingerprint,
            [requirement], out ContentActivationPlan? plan, out _));
        True(ContentCatalogResolver.TryResolve(
            oracle.BaseCatalog, plan!, [verified!], out ResolvedContentCatalog? catalog,
            out ContentCatalogResolutionError resolutionError));
        Equal(ContentCatalogResolutionError.None, resolutionError);
        return new UiContentFixture(definition, artifact, manifest, payloads, plan!, catalog!);
    }

    private static void ResolveUiDefinition(
        ContentBaseCatalog baseCatalog,
        UiContentFixture fixture,
        ContentPackageAcquisition acquisition,
        IContentPackageSource source,
        string cacheRoot,
        out ContentHash fingerprint,
        out ContentActivationAcquisitionResult acquisitionResult,
        bool preserveCacheRoot = false)
    {
        try
        {
            using ContentCache cache = OpenCache(cacheRoot);
            True(ContentActivationAcquirer.TryAcquireAndPublish(
                cache, baseCatalog, fixture.Plan, [acquisition], source,
                false, out ContentCatalogLease? lease, out acquisitionResult));
            using (lease)
            {
                fingerprint = lease!.Catalog.Fingerprint;
                AssertInventoryDefinition(lease, fixture.Definition);
            }
        }
        finally
        {
            if (!preserveCacheRoot && Directory.Exists(cacheRoot))
                Directory.Delete(cacheRoot, recursive: true);
        }
    }

    private static void AssertInventoryDefinition(
        ContentCatalogLease lease,
        CompiledUiDefinition expected)
    {
        True(ContentUiDefinitionResolver.TryResolve(
            lease, UiDefinitionIdentity.From(expected), out CompiledUiDefinition? actual,
            out ContentUiDefinitionResolutionError error));
        Equal(ContentUiDefinitionResolutionError.None, error);
        Equal(UiDefinitionIdentity.From(expected), UiDefinitionIdentity.From(actual!));
        True(UiDefinitionArtifactCodec.TryEncodeCanonical(actual!, out byte[] actualBytes));
        True(UiDefinitionArtifactCodec.TryEncodeCanonical(expected, out byte[] expectedBytes));
        True(actualBytes.AsSpan().SequenceEqual(expectedBytes));
    }

    private static ContentCache OpenCache(string root)
    {
        True(ContentCache.TryOpen(root, out ContentCache? cache, out ContentCacheError error));
        Equal(ContentCacheError.None, error);
        return cache!;
    }

    private static byte[] Archive(CabinLocalContentPackage package)
    {
        True(ContentPackageArchive.TryEncodeCanonical(
            package.Manifest, package.Payloads, out byte[] archive));
        return archive;
    }

    private static byte[] Archive(
        ContentPackageManifest manifest,
        IEnumerable<ContentFilePayload> payloads)
    {
        True(ContentPackageArchive.TryEncodeCanonical(manifest, payloads, out byte[] archive));
        return archive;
    }

    private static ContentPackageAcquisition Acquisition(
        CabinLocalContentPackage package,
        ContentSourceKind kind,
        string locator) => new(
            package.Manifest.PackageId,
            package.Manifest.ManifestHash,
            [new ContentSourceDescriptor(kind, locator)]);

    private static ContentPackageAcquisition Acquisition(
        ContentPackageManifest package,
        ContentSourceKind kind,
        string locator) => new(
            package.PackageId,
            package.ManifestHash,
            [new ContentSourceDescriptor(kind, locator)]);

    private static ContentPackageSourceLimits FixtureHttpLimits(
        int archiveBytes,
        bool allowInsecureHttp) => new(
            maximumArchiveBytes: archiveBytes,
            requestTimeoutMilliseconds: 10_000,
            allowInsecureServerHostedHttp: allowInsecureHttp,
            allowPrivateNetworkTargets: true);

    private static void AssertCatalogEqual(
        ResolvedContentCatalog expected,
        ResolvedContentCatalog actual)
    {
        Equal(expected.Fingerprint, actual.Fingerprint);
        Equal(expected.PlanHash, actual.PlanHash);
        Equal(expected.RequiredBaseCatalogFingerprint, actual.RequiredBaseCatalogFingerprint);
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

    private static void AssertBasicRemoteGameUnaffected(
        RemotePlayableFixture fixture,
        string displayName)
    {
        fixture.Admit(displayName, GameGuestBody.HumanFemale);
        fixture.DriveUntil(() =>
            fixture.TryFindPublic(GameWorldPublicPrefabIds.Character, out _) &&
            fixture.TryFindPublic(GameWorldPublicPrefabIds.CabinDoor, out _) &&
            fixture.TryFindPublic(GameWorldPublicPrefabIds.WorldChest, out _));
        WorldPosition before = fixture.Playable.GetSnapshot().Position;
        True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.CreateDirect(GamePlayerCommand.MaximumAxisMagnitude, 0),
            GamePlayerActions.None, true)));
        ulong movement = fixture.Playable.Input.Snapshot().LastSequence;
        fixture.DriveUntil(() =>
            fixture.Playable.GetSnapshot().LastProcessedCommandSequence >= movement &&
            fixture.Playable.GetSnapshot().Position.GlobalXMillimeters > before.GlobalXMillimeters);
        True(fixture.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
            GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
            flags: RemotePlayableCommandFlags.OpenInventoryRequested));
        fixture.DriveUntil(() => fixture.UiClient.OpenViewCount == 1);
        True(fixture.Chat.TrySubmit("content parity", ClientChatSpeechMode.Say));
        fixture.DriveUntil(() => fixture.ChatState.GetSnapshot().Applied == 1);
    }

    private static int ReserveLoopbackPort()
    {
        using var reservation = new TcpListener(IPAddress.Loopback, 0);
        reservation.Start();
        int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
        reservation.Stop();
        return port;
    }

    private static RemoteContentSetAppliedMessage CreateAppliedProof(
        CabinContentOracle oracle,
        ulong applicationSessionEpoch)
    {
        RemoteContentHash plan = RemoteContentAuthorizationProjection.ToRemote(
            oracle.Replace.Plan.PlanHash);
        RemoteContentHash resolved = RemoteContentAuthorizationProjection.ToRemote(
            oracle.Replace.Catalog.Fingerprint);
        return new RemoteContentSetAppliedMessage
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            RequirementSequence = 1,
            ApplicationRealmHigh = CabinContentOracle.FixtureRealm.High,
            ApplicationRealmLow = CabinContentOracle.FixtureRealm.Low,
            ActivationHigh = oracle.Replace.Plan.Activation.High,
            ActivationLow = oracle.Replace.Plan.Activation.Low,
            PlanHash0 = plan.Part0,
            PlanHash1 = plan.Part1,
            PlanHash2 = plan.Part2,
            PlanHash3 = plan.Part3,
            ResolvedHash0 = resolved.Part0,
            ResolvedHash1 = resolved.Part1,
            ResolvedHash2 = resolved.Part2,
            ResolvedHash3 = resolved.Part3,
            Status = RemoteContentApplyStatus.Applied,
        };
    }

    private static ContentPackageAcquisition RemoteReplaceAcquisition(CabinContentOracle oracle) =>
        new(
            oracle.ReplacePackage.Manifest.PackageId,
            oracle.ReplacePackage.Manifest.ManifestHash,
            [new ContentSourceDescriptor(
                ContentSourceKind.ExternalHttps,
                "https://content.example/ultrasim/cabin/replace")]);

    private static string TemporaryDirectory(string prefix)
    {
        string path = Path.Combine(Path.GetTempPath(),
            $"{prefix}-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return path;
    }

    private static void Run(string name, Action body)
    {
        body();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void False(bool value)
    {
        if (value) throw new InvalidOperationException("Expected false.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private sealed record UiContentFixture(
        CompiledUiDefinition Definition,
        byte[] ArtifactBytes,
        ContentPackageManifest Manifest,
        ContentFilePayload[] Payloads,
        ContentActivationPlan Plan,
        ResolvedContentCatalog Catalog);

    private sealed record WorldContentFixture(
        LogicalContentId DefinitionId,
        WorldPack Pack,
        byte[] Bytes,
        WorldContentBinding Binding,
        ContentPackageManifest Manifest,
        ContentFilePayload[] Payloads,
        ContentActivationPlan Plan,
        ResolvedContentCatalog Catalog);

    private sealed class CabinReplaceSource : IContentPackageSource
    {
        private readonly CabinContentOracle _oracle;
        private readonly bool _fail;
        private readonly bool _wrongPackage;

        internal CabinReplaceSource(
            CabinContentOracle oracle,
            bool fail = false,
            bool wrongPackage = false)
        {
            _oracle = oracle;
            _fail = fail;
            _wrongPackage = wrongPackage;
        }

        internal int Fetches { get; private set; }

        public bool CanFetch(ContentSourceKind kind) => kind == ContentSourceKind.ExternalHttps;

        public bool TryFetch(
            in ContentPackageFetchRequest request,
            out FetchedContentPackage? package,
            out ContentSourceFetchError error)
        {
            Fetches++;
            package = null;
            if (_fail)
            {
                error = ContentSourceFetchError.Unavailable;
                return false;
            }
            if (!request.IsValid || request.PackageId != _oracle.ReplacePackage.Manifest.PackageId ||
                request.ManifestHash != _oracle.ReplacePackage.Manifest.ManifestHash ||
                request.Source.Kind != ContentSourceKind.ExternalHttps ||
                request.Source.Locator != "https://content.example/ultrasim/cabin/replace")
            {
                error = ContentSourceFetchError.InvalidRequest;
                return false;
            }
            package = _wrongPackage
                ? new FetchedContentPackage(
                    _oracle.AddPackage.Manifest, _oracle.AddPackage.Payloads)
                : new FetchedContentPackage(
                    _oracle.ReplacePackage.Manifest, _oracle.ReplacePackage.Payloads);
            error = ContentSourceFetchError.None;
            return true;
        }
    }

    private sealed class RejectingSource : IContentPackageSource
    {
        internal int Fetches { get; private set; }

        public bool CanFetch(ContentSourceKind kind) => false;

        public bool TryFetch(
            in ContentPackageFetchRequest request,
            out FetchedContentPackage? package,
            out ContentSourceFetchError error)
        {
            Fetches++;
            package = null;
            error = ContentSourceFetchError.UnsupportedSource;
            return false;
        }
    }

    private sealed class RemotePlayableFixture : IDisposable
    {
        private static readonly NetworkProtocolLimits Protocol = NetworkProtocolLimits.Default;
        private static readonly UdpProtocolLimits Udp = new(
            MaximumDatagramBytes: 1_472,
            SocketSendBufferBytes: 128 * 1024,
            SocketReceiveBufferBytes: 128 * 1024,
            MaximumHandshakePumps: 4_096,
            CookieBucketMilliseconds: 1_000,
            IdleTimeoutMilliseconds: 30_000,
            DrainTimeoutMilliseconds: 1_000,
            KeepAliveIntervalMilliseconds: 5_000);
        private static readonly SharedUdpProtocolLimits Shared = new(
            MaximumSessions: 1,
            MaximumQueuedDatagramsPerSession: 64,
            MaximumInboundDatagramsPerPump: 256,
            SocketIoMode: SharedUdpSocketIoMode.Scalar,
            AggregateSocketSendBufferBytes: 256 * 1024,
            AggregateSocketReceiveBufferBytes: 256 * 1024);
        private static readonly HostedUdpAdmissionLimits Admission = new(
            MaximumValidatedCandidates: 2,
            MaximumTrackedSourceAddresses: 8,
            MaximumChallengesPerAddressPerBucket: 64,
            MaximumValidationsPerAddressPerBucket: 64,
            ValidatedCandidateTimeoutMilliseconds: 5_000);
        private static readonly HostedUdpOwnerShardingLimits Owners = new(
            MaximumOwners: 1,
            MaximumConnections: 1,
            MaximumPacketSlots: 256,
            MaximumQueuedPacketsPerOwner: 128,
            MaximumDatagramBytes: 1_472);

        private readonly string _trustDirectory;
        private readonly NetworkServerIdentity _identity;
        private readonly HostedUdpAdmissionHost _host;
        private readonly ClientDirectConnectSession _direct;
        private readonly RemotePlayableSession _session;
        private readonly ContentCache? _contentCache;
        private readonly string? _contentRoot;
        private readonly bool _preserveContentRoot;
        private bool _disposed;

        private RemotePlayableFixture(
            string trustDirectory,
            NetworkServerIdentity identity,
            HostedUdpAdmissionHost host,
            ClientDirectConnectSession direct,
            RemotePlayableSession session,
            ContentCache? contentCache = null,
            string? contentRoot = null,
            bool preserveContentRoot = false)
        {
            _trustDirectory = trustDirectory;
            _identity = identity;
            _host = host;
            _direct = direct;
            _session = session;
            _contentCache = contentCache;
            _contentRoot = contentRoot;
            _preserveContentRoot = preserveContentRoot;
        }

        internal RemotePlayableClient Playable => _session.Playable;
        internal RemoteChunkWorldStateClient World => _session.World;
        internal ClientChatState ChatState => _session.ChatState;
        internal RemoteChatClient Chat => _session.Chat;
        internal UiClientRuntime UiClient => _session.UiClient;
        internal RemoteContentClient? Content => _session.Content;
        internal ContentCache? ContentStore => _contentCache;
        internal ClientWorldSemanticCache Cache => _session.WorldCache;
        internal RemotePlayableSessionSnapshot SessionSnapshot => _session.GetSnapshot();

        internal static RemotePlayableFixture Create()
            => CreateCore(new UltraSim.Game.Application.ApplicationRealmId(0xF03B, 1));

        internal static RemotePlayableFixture CreateWithContent(
            CabinContentOracle oracle,
            IContentPackageSource source,
            string? contentRoot = null,
            bool preserveContentRoot = false)
            => CreateWithContent(
                oracle, oracle.Replace, [RemoteReplaceAcquisition(oracle)], source,
                contentRoot, preserveContentRoot);

        internal static RemotePlayableFixture CreateWithContent(
            CabinContentOracle oracle,
            CabinResolvedContent expected,
            ContentPackageAcquisition[] acquisitions,
            IContentPackageSource source,
            string? contentRoot = null,
            bool preserveContentRoot = false)
        {
            contentRoot ??= TemporaryDirectory("ultrasim-a03-cache");
            if (!ContentCache.TryOpen(contentRoot, out ContentCache? contentCache, out ContentCacheError error))
                throw new InvalidOperationException($"The A0.3 cache could not open: {error}.");
            try
            {
                RemoteContentAuthorization authorization = RemoteContentAuthorizationProjection.Create(
                    expected.Plan, expected.Catalog.Fingerprint, acquisitions);
                var binding = new RemoteRequiredContentBinding(
                    CabinContentOracle.FixtureRealm, contentCache!, oracle.BaseCatalog, source);
                return CreateCore(
                    CabinContentOracle.FixtureRealm, authorization, binding,
                    contentCache, contentRoot, preserveContentRoot);
            }
            catch
            {
                contentCache!.Dispose();
                if (!preserveContentRoot && Directory.Exists(contentRoot))
                    Directory.Delete(contentRoot, recursive: true);
                throw;
            }
        }

        internal static RemotePlayableFixture CreateWithContent(
            UltraSim.Game.Application.ApplicationRealmId realm,
            ContentBaseCatalog baseCatalog,
            ContentActivationPlan plan,
            ContentHash expectedFingerprint,
            ContentPackageAcquisition[] acquisitions,
            IContentPackageSource source,
            IEnumerable<UiDefinitionIdentity> contentBackedUiDefinitions)
        {
            string contentRoot = TemporaryDirectory("ultrasim-a06-cache");
            if (!ContentCache.TryOpen(
                    contentRoot, out ContentCache? contentCache, out ContentCacheError error))
                throw new InvalidOperationException($"The A0.6 cache could not open: {error}.");
            try
            {
                RemoteContentAuthorization authorization = RemoteContentAuthorizationProjection.Create(
                    plan, expectedFingerprint, acquisitions);
                var binding = new RemoteRequiredContentBinding(
                    realm, contentCache!, baseCatalog, source);
                return CreateCore(
                    realm, authorization, binding, contentCache, contentRoot,
                    preserveContentRoot: false,
                    contentBackedUiDefinitions: contentBackedUiDefinitions);
            }
            catch
            {
                contentCache!.Dispose();
                if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
                throw;
            }
        }

        internal static RemotePlayableFixture CreateWithWorldContent(
            UltraSim.Game.Application.ApplicationRealmId realm,
            ContentBaseCatalog baseCatalog,
            ContentActivationPlan plan,
            ContentHash expectedFingerprint,
            ContentPackageAcquisition[] acquisitions,
            IContentPackageSource source,
            RemoteRequiredWorldBinding requiredWorld)
        {
            string contentRoot = TemporaryDirectory("ultrasim-a07-cache");
            if (!ContentCache.TryOpen(
                    contentRoot, out ContentCache? contentCache, out ContentCacheError error))
                throw new InvalidOperationException($"The A0.7 cache could not open: {error}.");
            try
            {
                RemoteContentAuthorization authorization = RemoteContentAuthorizationProjection.Create(
                    plan, expectedFingerprint, acquisitions);
                var contentBinding = new RemoteRequiredContentBinding(
                    realm, contentCache!, baseCatalog, source);
                return CreateCore(
                    realm, authorization, contentBinding, contentCache, contentRoot,
                    preserveContentRoot: false,
                    requiredWorld: requiredWorld);
            }
            catch
            {
                contentCache!.Dispose();
                if (Directory.Exists(contentRoot)) Directory.Delete(contentRoot, recursive: true);
                throw;
            }
        }

        private static RemotePlayableFixture CreateCore(
            UltraSim.Game.Application.ApplicationRealmId realm,
            RemoteContentAuthorization? contentAuthorization = null,
            RemoteRequiredContentBinding? requiredContent = null,
            ContentCache? contentCache = null,
            string? contentRoot = null,
            bool preserveContentRoot = false,
            IEnumerable<UiDefinitionIdentity>? contentBackedUiDefinitions = null,
            RemoteRequiredWorldBinding? requiredWorld = null)
        {
            string trustDirectory = Path.Combine(Path.GetTempPath(),
                $"ultrasim-f03b-{Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(trustDirectory);
            var identity = NetworkServerIdentity.CreateEphemeral();
            try
            {
                var host = new HostedUdpAdmissionHost(
                    new IPEndPoint(IPAddress.Loopback, 0),
                    identity,
                    Protocol,
                    Udp,
                    Shared,
                    Admission,
                    Owners,
                    new RemoteGuestPlayableApplicationFactory(
                        realm, contentAuthorization, contentBackedUiDefinitions),
                    UltraSimGameProtocol.Manifest);
                try
                {
                    var trust = new ClientServerTrustRegistry();
                    var store = new ClientServerTrustFileStore(
                        Path.Combine(trustDirectory, "servers.json"));
                    var direct = new ClientDirectConnectSession(trust, store, Protocol, Udp);
                    try
                    {
                        ClientDeploymentKeyFingerprint fingerprint =
                            ClientDeploymentKeyFingerprint.Compute(identity.PublicKey);
                        True(direct.TryStart(host.LocalEndPoint.ToString(), fingerprint.ToString()));
                        DriveDirectUntil(host, direct, ClientDirectConnectState.Established);
                        ClientDirectConnectSnapshot connected = direct.GetSnapshot();
                        NetworkProtocolPeer peer = direct.Peer ??
                            throw new InvalidOperationException("The established client peer was absent.");
                        RemotePlayableSession session;
                        if (requiredWorld is null)
                        {
                            ClientWorldSemanticCache cache = CreatePreinstalledWorldCache();
                            session = new RemotePlayableSession(
                                peer,
                                connected.ApplicationSessionEpoch,
                                cache,
                                new NullRenderer(),
                                new ClientChatLimits(16, 256, 128),
                                new UiClientLimits(8, 8),
                                maximumPendingWorldProofs: 256,
                                requiredContent: requiredContent);
                        }
                        else
                        {
                            session = new RemotePlayableSession(
                                peer,
                                connected.ApplicationSessionEpoch,
                                requiredContent ?? throw new InvalidOperationException(
                                    "The streamed World requires a content binding."),
                                requiredWorld,
                                new NullRenderer(),
                                new ClientChatLimits(16, 256, 128),
                                new UiClientLimits(8, 8),
                                maximumPendingWorldProofs: 256);
                        }
                        return new RemotePlayableFixture(
                            trustDirectory, identity, host, direct, session,
                            contentCache, contentRoot, preserveContentRoot);
                    }
                    catch
                    {
                        direct.Dispose();
                        throw;
                    }
                }
                catch
                {
                    host.Dispose();
                    throw;
                }
            }
            catch
            {
                identity.Dispose();
                if (Directory.Exists(trustDirectory))
                    Directory.Delete(trustDirectory, recursive: true);
                throw;
            }
        }

        internal void Admit(string name, GameGuestBody body)
        {
            True(TrySubmitGuest(name, body));
            DriveUntil(() => Playable.GetSnapshot().State == RemotePlayableClientState.Active);
        }

        internal bool TrySubmitGuest(string name, GameGuestBody body) =>
            _session.TrySubmitGuest(new GameGuestAdmissionRequest(body, name));

        internal void DriveUntil(Func<bool> condition, int maximumSteps = 100_000)
        {
            for (int step = 0; step < maximumSteps; step++)
            {
                Pump();
                if (condition()) return;
                if ((step & 15) == 0) Thread.Sleep(1);
                else Thread.Yield();
            }
            throw new InvalidOperationException(
                $"Remote playable did not converge. direct={_direct.GetSnapshot()} " +
                $"playable={Playable.GetSnapshot()} world={_session.GetSnapshot().World} " +
                $"chat={Chat.GetSnapshot()} session={_session.GetSnapshot()}");
        }

        internal void DriveSteps(int count)
        {
            for (int step = 0; step < count; step++)
            {
                Pump();
                if ((step & 15) == 0) Thread.Sleep(1);
            }
        }

        internal void QueueRaw(in RemotePlayableCommandMessage message)
        {
            Span<byte> payload = stackalloc byte[RemotePlayableCommandMessageCodec.EncodedSize];
            True(RemotePlayableCommandMessageCodec.TryWriteExact(payload, in message));
            Equal(NetworkQueueStatus.Accepted,
                _direct.Peer!.TryQueue(RemotePlayableCommandMessageCodec.Descriptor.Id, payload));
        }

        internal void QueueRaw(in RemoteContentSetAppliedMessage message)
        {
            Span<byte> payload = stackalloc byte[RemoteContentSetAppliedMessageCodec.EncodedSize];
            True(RemoteContentSetAppliedMessageCodec.TryWriteExact(payload, in message));
            Equal(NetworkQueueStatus.Accepted,
                _direct.Peer!.TryQueue(RemoteContentSetAppliedMessageCodec.Descriptor.Id, payload));
        }

        internal void QueueRawGuest(string name, GameGuestBody body)
        {
            var request = new GameGuestAdmissionRequest(body, name);
            Span<byte> payload = stackalloc byte[UltraSimGameProtocol.MaximumGuestAdmissionMessageBytes];
            True(UltraSimGameProtocol.TryWriteGuestAdmissionRequest(
                payload, _direct.GetSnapshot().ApplicationSessionEpoch, 1, in request, out int written));
            Equal(NetworkQueueStatus.Accepted,
                _direct.Peer!.TryQueue(
                    RemoteGuestAdmissionRequestHeaderCodec.Descriptor.Id, payload[..written]));
        }

        internal bool TryFindPublic(uint prefab, out WorldPublicRecord found)
        {
            Span<ClientSemanticChunkInfo> chunks = stackalloc ClientSemanticChunkInfo[192];
            if (!Cache.TryCopyResidentInfo(chunks, out int count, out _))
            {
                found = default;
                return false;
            }
            for (int chunk = 0; chunk < count; chunk++)
            {
                if (!Cache.TryGetResidentView(chunks[chunk].Handle, out ClientSemanticChunkView view))
                    continue;
                foreach (WorldPublicRecord record in view.PublicRecords)
                {
                    if (record.PrefabId != prefab) continue;
                    found = record;
                    return true;
                }
            }
            found = default;
            return false;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _session.Dispose();
            _contentCache?.Dispose();
            if (_direct.State == ClientDirectConnectState.Established)
            {
                _direct.BeginDisconnect();
                for (int step = 0; step < 20_000 &&
                     _direct.State != ClientDirectConnectState.Closed; step++)
                {
                    _direct.Pump();
                    _host.Pump();
                    Thread.Yield();
                }
            }
            _direct.Dispose();
            _host.Dispose();
            _identity.Dispose();
            if (Directory.Exists(_trustDirectory))
                Directory.Delete(_trustDirectory, recursive: true);
            if (!_preserveContentRoot && _contentRoot is not null && Directory.Exists(_contentRoot))
                Directory.Delete(_contentRoot, recursive: true);
        }

        private void Pump()
        {
            _direct.Pump();
            _host.Pump();
            _direct.Pump();
            _ = _session.ProcessReceived();
        }

        internal static ClientWorldSemanticCache CreatePreinstalledWorldCache()
        {
            WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
            WorldContentBinding binding = CreateWorldBinding();
            True(pack.TryBind(binding, out BoundWorldPack? bound, out _));
            var cache = new ClientWorldSemanticCache(
                bound!, new ClientWorldSemanticCacheLimits(192, 256, 32, 64, 4_096));
            for (int index = 0; index < pack.ChunkCount; index++)
            {
                True(pack.TryGetChunkKey(index, out var key));
                True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
                True(cache.TryStageFromBaseWorld(handle, [], default, out _));
                True(cache.TryPublish(handle, out _));
            }
            return cache;
        }

        private static void DriveDirectUntil(
            HostedUdpAdmissionHost host,
            ClientDirectConnectSession client,
            ClientDirectConnectState expected)
        {
            for (int iteration = 0; iteration < 40_000; iteration++)
            {
                client.Pump();
                host.Pump();
                if (client.State == expected) return;
                if (client.State == ClientDirectConnectState.Failed)
                    throw new InvalidOperationException(
                        $"Direct Connect failed: {client.GetSnapshot()}");
                Thread.Yield();
            }
            throw new InvalidOperationException($"Direct Connect did not reach {expected}.");
        }

        private sealed class NullRenderer : IUiRenderer
        {
            public IUiRenderHandle Create(
                UiViewInstanceId instance,
                CompiledUiDefinition definition,
                UiModelView model,
                Action<UiActionRequest> submitAction) => new Handle();

            public void Apply(
                IUiRenderHandle handle,
                CompiledUiDefinition definition,
                UiModelView previous,
                UiModelView current,
                UiModelDelta delta,
                Action<UiActionRequest> submitAction) { }

            public void Close(IUiRenderHandle handle) { }

            private sealed class Handle : IUiRenderHandle;
        }
    }
}
