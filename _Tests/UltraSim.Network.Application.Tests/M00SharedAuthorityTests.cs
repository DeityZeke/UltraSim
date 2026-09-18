using System.Net;
using UltraSim.Client.Core;
using UltraSim.Client.Local;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Admission;
using UltraSim.Game.Application;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.UI;

internal static class M00SharedAuthorityTests
{
    internal const int TestCount = 3;

    internal static void RunAll()
    {
        OneDedicatedAuthorityServesThreeIndependentRemoteSessions();
        Console.WriteLine("PASS M00OneDedicatedAuthorityServesThreeIndependentRemoteSessions");
        SharedPresentationPreparationIsSharedAndProofWakeDeliversNewerState();
        Console.WriteLine("PASS PF5R10SharedPresentationPreparationIsSharedAndProofWakeDeliversNewerState");
        ExistingLocalAuthorityAddsAndRetiresRemoteRecipients();
        Console.WriteLine("PASS M00ExistingLocalAuthorityAddsAndRetiresRemoteRecipients");
    }

    private static void SharedPresentationPreparationIsSharedAndProofWakeDeliversNewerState()
    {
        const int maximumParticipants = 2;
        var realm = new ApplicationRealmId(0x4D10, 1);
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using SharedPlayableAuthority authority = SharedPlayableAuthority.CreateConceptDemo(
            new SharedPlayableAuthorityLimits(maximumParticipants));
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            identity,
            NetworkProtocolLimits.Default,
            UdpProtocolLimits.Default,
            SharedUdpProtocolLimits.Default with { MaximumSessions = maximumParticipants },
            HostedUdpAdmissionLimits.Default with
            {
                MaximumChallengesPerAddressPerBucket = 64,
                MaximumValidationsPerAddressPerBucket = 64,
            },
            new SharedRemoteGuestPlayableApplicationFactory(authority, realm),
            UltraSimGameProtocol.Manifest);
        string root = Path.Combine(Path.GetTempPath(),
            $"ultrasim-pf5-r10-presentation-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var clients = new List<ClientNode>();
        try
        {
            ClientNode alice = ClientNode.Connect(root, "alice", host, identity);
            ClientNode bob = ClientNode.Connect(root, "bob", host, identity);
            clients.Add(alice);
            clients.Add(bob);
            True(alice.Session.TrySubmitGuest(new GameGuestAdmissionRequest(
                GameGuestBody.HumanFemale, "Alice")));
            True(bob.Session.TrySubmitGuest(new GameGuestAdmissionRequest(
                GameGuestBody.HumanMale, "Bob")));
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                client.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active &&
                client.Session.PublicPresentationState.GetSnapshot().CurrentRecords >= 2));

            SharedActorPresentationCensus sourceBefore = authority.GetActorPresentationCensus();
            SharedPublicItemPresentationCensus itemSourceBefore =
                authority.GetPublicItemPresentationCensus();
            Span<RemoteSharedPresentationCensus> remoteBefore =
                stackalloc RemoteSharedPresentationCensus[maximumParticipants];
            Equal(maximumParticipants, authority.CopyRemotePresentationCensus(remoteBefore));
            Span<RemotePublicItemPresentationCensus> remoteItemBefore =
                stackalloc RemotePublicItemPresentationCensus[maximumParticipants];
            Equal(maximumParticipants,
                authority.CopyRemotePublicItemPresentationCensus(remoteItemBefore));
            for (int step = 0; step < 256; step++)
            {
                for (int index = 0; index < clients.Count; index++) clients[index].Direct.Pump();
                host.Pump();
                _ = authority.TickDue();
                for (int index = 0; index < clients.Count; index++)
                {
                    clients[index].Direct.Pump();
                    _ = clients[index].Session.ProcessReceived();
                }
            }
            SharedActorPresentationCensus sourceStable = authority.GetActorPresentationCensus();
            SharedPublicItemPresentationCensus itemSourceStable =
                authority.GetPublicItemPresentationCensus();
            Span<RemoteSharedPresentationCensus> remoteStable =
                stackalloc RemoteSharedPresentationCensus[maximumParticipants];
            Equal(maximumParticipants, authority.CopyRemotePresentationCensus(remoteStable));
            Span<RemotePublicItemPresentationCensus> remoteItemStable =
                stackalloc RemotePublicItemPresentationCensus[maximumParticipants];
            Equal(maximumParticipants,
                authority.CopyRemotePublicItemPresentationCensus(remoteItemStable));
            ulong itemProofWakeupsBefore = 0;
            for (int index = 0; index < maximumParticipants; index++)
                itemProofWakeupsBefore += remoteItemStable[index].ProofWakeups;
            ulong progressDelta = 0;
            ulong reconciliationDelta = 0;
            ulong skipDelta = 0;
            ulong actorSearchDelta = 0;
            ulong actorRowsExaminedDelta = 0;
            ulong actorEmptySearchDelta = 0;
            for (int index = 0; index < maximumParticipants; index++)
            {
                progressDelta += remoteStable[index].ProgressPasses - remoteBefore[index].ProgressPasses;
                reconciliationDelta += remoteStable[index].ReconciliationPasses -
                    remoteBefore[index].ReconciliationPasses;
                skipDelta += remoteStable[index].UnchangedSourceSkips -
                    remoteBefore[index].UnchangedSourceSkips;
                actorSearchDelta += remoteStable[index].DeliverySearches -
                    remoteBefore[index].DeliverySearches;
                actorRowsExaminedDelta += remoteStable[index].DeliveryRowsExamined -
                    remoteBefore[index].DeliveryRowsExamined;
                actorEmptySearchDelta += remoteStable[index].EmptyDeliverySearches -
                    remoteBefore[index].EmptyDeliverySearches;
            }
            True(progressDelta > 0);
            True(skipDelta > 0);
            True(reconciliationDelta < progressDelta);
            True(actorSearchDelta > 0);
            Equal(actorSearchDelta, actorEmptySearchDelta);
            Equal(actorSearchDelta * maximumParticipants, actorRowsExaminedDelta);
            True(sourceStable.ViewReads - sourceBefore.ViewReads >= progressDelta);
            True(sourceStable.Preparations - sourceBefore.Preparations < progressDelta);

            ulong itemProgressDelta = 0;
            ulong itemReconciliationDelta = 0;
            ulong itemSkipDelta = 0;
            ulong itemSearchDelta = 0;
            ulong itemRowsExaminedDelta = 0;
            ulong itemEmptySearchDelta = 0;
            for (int index = 0; index < maximumParticipants; index++)
            {
                itemProgressDelta += remoteItemStable[index].ProgressPasses -
                    remoteItemBefore[index].ProgressPasses;
                itemReconciliationDelta += remoteItemStable[index].ReconciliationPasses -
                    remoteItemBefore[index].ReconciliationPasses;
                itemSkipDelta += remoteItemStable[index].UnchangedSourceAndTopologySkips -
                    remoteItemBefore[index].UnchangedSourceAndTopologySkips;
                itemSearchDelta += remoteItemStable[index].DeliverySearches -
                    remoteItemBefore[index].DeliverySearches;
                itemRowsExaminedDelta += remoteItemStable[index].DeliveryRowsExamined -
                    remoteItemBefore[index].DeliveryRowsExamined;
                itemEmptySearchDelta += remoteItemStable[index].EmptyDeliverySearches -
                    remoteItemBefore[index].EmptyDeliverySearches;
            }
            True(itemProgressDelta > 0);
            True(itemSkipDelta > 0);
            True(itemReconciliationDelta < itemProgressDelta);
            True(itemSearchDelta > 0);
            Equal(itemSearchDelta, itemEmptySearchDelta);
            Equal(
                itemSearchDelta * SharedPlayableAuthority.MaximumPublicItemPresentations,
                itemRowsExaminedDelta);
            True(itemSourceStable.ViewReads - itemSourceBefore.ViewReads >= itemProgressDelta);
            True(itemSourceStable.Preparations - itemSourceBefore.Preparations < itemProgressDelta);

            var participants = new SharedPlayableParticipant[maximumParticipants];
            Equal(maximumParticipants, authority.CopyParticipants(participants));
            SharedPlayableParticipant aliceParticipant = participants.Single(
                static participant => participant.GuestIdentity.DisplayName == "Alice");
            True(authority.RecordPresentationAction(
                in aliceParticipant, 1, GamePublicActorActionKind.Interact));
            for (int step = 0; step < 64; step++)
            {
                host.Pump();
                _ = authority.TickDue();
                Thread.Yield();
            }
            True(authority.RecordPresentationAction(
                in aliceParticipant, 2, GamePublicActorActionKind.Primary));
            for (int step = 0; step < 64; step++)
            {
                host.Pump();
                _ = authority.TickDue();
                Thread.Yield();
            }
            DriveUntil(host, authority, clients, () => clients.All(client =>
                client.Session.PublicPresentationState.TryGet(
                    aliceParticipant.PublicEntity,
                    out ClientPublicPresentationMetadata metadata) &&
                metadata.ActorAction == ClientPublicActorActionKind.Primary &&
                metadata.ActorActionSequence == 2));

            Span<RemoteSharedPresentationCensus> remoteAfter =
                stackalloc RemoteSharedPresentationCensus[maximumParticipants];
            Equal(maximumParticipants, authority.CopyRemotePresentationCensus(remoteAfter));
            ulong proofWakeups = 0;
            for (int index = 0; index < maximumParticipants; index++)
                proofWakeups += remoteAfter[index].ProofWakeups - remoteStable[index].ProofWakeups;
            True(proofWakeups >= (ulong)maximumParticipants);

            // A partial source preparation must not leave changed retained rows hidden behind the
            // source version the recipients have already proved. Exercise both a failed capture
            // result and the outer exception path without allowing the failed view into progress.
            for (int step = 0; step < 128; step++)
            {
                for (int index = 0; index < clients.Count; index++) clients[index].Direct.Pump();
                host.Pump();
                for (int index = 0; index < clients.Count; index++)
                {
                    clients[index].Direct.Pump();
                    _ = clients[index].Session.ProcessReceived();
                }
            }
            ulong recoveredVersion = authority.GetActorPresentationView().Version;
            True(authority.RecordPresentationAction(
                in aliceParticipant, 3, GamePublicActorActionKind.Interact));
            authority.SetActorPresentationPreparationFaultForTesting(
                participants.Single(static participant =>
                    participant.GuestIdentity.DisplayName == "Bob").Slot,
                SharedActorPresentationPreparationFault.ReturnFalse);
            Throws<InvalidOperationException>(() => authority.GetActorPresentationView());
            SharedActorPresentationView recoveredFalse = authority.GetActorPresentationView();
            True(recoveredFalse.Version > recoveredVersion);
            DriveUntil(host, authority, clients, () => clients.All(client =>
                client.Session.PublicPresentationState.TryGet(
                    aliceParticipant.PublicEntity,
                    out ClientPublicPresentationMetadata metadata) &&
                metadata.ActorAction == ClientPublicActorActionKind.Interact &&
                metadata.ActorActionSequence == 3));

            recoveredVersion = recoveredFalse.Version;
            True(authority.RecordPresentationAction(
                in aliceParticipant, 4, GamePublicActorActionKind.Primary));
            authority.SetActorPresentationPreparationFaultForTesting(
                participants.Single(static participant =>
                    participant.GuestIdentity.DisplayName == "Bob").Slot,
                SharedActorPresentationPreparationFault.Throw);
            Throws<InvalidOperationException>(() => authority.GetActorPresentationView());
            SharedActorPresentationView recoveredThrow = authority.GetActorPresentationView();
            True(recoveredThrow.Version > recoveredVersion);
            DriveUntil(host, authority, clients, () => clients.All(client =>
                client.Session.PublicPresentationState.TryGet(
                    aliceParticipant.PublicEntity,
                    out ClientPublicPresentationMetadata metadata) &&
                metadata.ActorAction == ClientPublicActorActionKind.Primary &&
                metadata.ActorActionSequence == 4));

            // Source truth may change again while a remove is awaiting exact applied proof. The
            // proof must wake item reconciliation even though source/AOI versions are unchanged by
            // the time the row becomes eligible again.
            SharedPublicItemPresentationView publicItems =
                authority.GetPublicItemPresentationView();
            Equal(1, publicItems.Count);
            WorldPublicEntityId swordId = publicItems[0].Id;
            True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (world, _, game) =>
                {
                    Entity actor = Resolve(world, aliceParticipant.Entities.PlayerId);
                    Entity backpack = Resolve(world, aliceParticipant.Entities.BackpackId);
                    True(game.WorldState.Publications.TryResolvePublicEntity(swordId, out Entity item));
                    Equal(GameWorldActionStatus.Accepted, game.Items.TryPickup(
                        world, game.WorldState, game.Inventory, actor, item, backpack));
                    return true;
                }, out bool pickedUp) && pickedUp);
            authority.ObservePublicItemPresentationMutation();
            for (int step = 0; step < 64; step++)
            {
                host.Pump();
                _ = authority.TickDue();
                Thread.Yield();
            }
            True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (world, _, game) =>
                {
                    Entity actor = Resolve(world, aliceParticipant.Entities.PlayerId);
                    True(game.WorldState.Publications.TryResolvePublicEntity(swordId, out Entity item));
                    Equal(GameWorldActionStatus.Accepted,
                        game.Items.TryDrop(world, game.WorldState, game.Inventory, actor, item));
                    return true;
                }, out bool dropped) && dropped);
            authority.ObservePublicItemPresentationMutation();
            for (int step = 0; step < 64; step++)
            {
                host.Pump();
                _ = authority.TickDue();
                Thread.Yield();
            }
            DriveUntil(host, authority, clients, () => clients.All(client =>
                client.Session.PublicPresentationState.TryGet(
                    swordId, out ClientPublicPresentationMetadata metadata) &&
                (metadata.Flags & ClientPublicPresentationFlags.ItemProperties) != 0));
            DriveUntil(host, authority, clients, () =>
                CopyPublicItemProofWakeups(authority, maximumParticipants) -
                    itemProofWakeupsBefore >= (ulong)maximumParticipants);

            // A fully proven removal retires the server row, but the client deliberately retains
            // the exact identity/revision floor. Reintroducing that same live item must allocate a
            // newer metadata revision rather than starting the recycled row at revision one.
            True(clients[0].Session.PublicPresentationState.TryGet(
                swordId, out ClientPublicPresentationMetadata beforeRetirement));
            ulong retainedRevisionFloor = beforeRetirement.Revision;
            True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (world, _, game) =>
                {
                    Entity actor = Resolve(world, aliceParticipant.Entities.PlayerId);
                    Entity backpack = Resolve(world, aliceParticipant.Entities.BackpackId);
                    True(game.WorldState.Publications.TryResolvePublicEntity(swordId, out Entity item));
                    Equal(GameWorldActionStatus.Accepted, game.Items.TryPickup(
                        world, game.WorldState, game.Inventory, actor, item, backpack));
                    return true;
                }, out pickedUp) && pickedUp);
            authority.ObservePublicItemPresentationMutation();
            DriveUntil(host, authority, clients, () => clients.All(client =>
                !client.Session.PublicPresentationState.TryGet(swordId, out _)));
            for (int step = 0; step < 64; step++)
            {
                host.Pump();
                _ = authority.TickDue();
                for (int index = 0; index < clients.Count; index++)
                {
                    clients[index].Direct.Pump();
                    _ = clients[index].Session.ProcessReceived();
                }
            }
            True(authority.Runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                (world, _, game) =>
                {
                    Entity actor = Resolve(world, aliceParticipant.Entities.PlayerId);
                    True(game.WorldState.Publications.TryResolvePublicEntity(swordId, out Entity item));
                    Equal(GameWorldActionStatus.Accepted,
                        game.Items.TryDrop(world, game.WorldState, game.Inventory, actor, item));
                    return true;
                }, out dropped) && dropped);
            authority.ObservePublicItemPresentationMutation();
            DriveUntil(host, authority, clients, () => clients.All(client =>
                client.Session.PublicPresentationState.TryGet(
                    swordId, out ClientPublicPresentationMetadata metadata) &&
                metadata.Revision > retainedRevisionFloor));
        }
        finally
        {
            for (int index = clients.Count - 1; index >= 0; index--) clients[index].Dispose();
            for (int step = 0; step < 20_000 && authority.GetSnapshot().ParticipantCount != 0;
                 step++)
            {
                host.Pump();
                _ = authority.TickDue();
                Thread.Yield();
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        Equal(0, authority.GetSnapshot().ParticipantCount);
    }

    private static void Throws<TException>(Action body) where TException : Exception
    {
        try { body(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private static Entity Resolve(EcsWorld world, PersistentEntityId id) =>
        world.TryResolvePersistentEntity(id, out Entity entity)
            ? entity
            : throw new InvalidOperationException($"Persistent entity {id} was unavailable.");

    private static ulong CopyPublicItemProofWakeups(
        SharedPlayableAuthority authority,
        int maximumParticipants)
    {
        Span<RemotePublicItemPresentationCensus> census =
            stackalloc RemotePublicItemPresentationCensus[maximumParticipants];
        Equal(maximumParticipants, authority.CopyRemotePublicItemPresentationCensus(census));
        ulong result = 0;
        for (int index = 0; index < maximumParticipants; index++)
            result += census[index].ProofWakeups;
        return result;
    }

    private static void ExistingLocalAuthorityAddsAndRetiresRemoteRecipients()
    {
        string root = Path.Combine(Path.GetTempPath(),
            $"ultrasim-m00-hosted-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var factory = new EmbeddedTestExtensionFactory();
        using LocalPlayableSession local = LocalPlayableSession.CreateConceptDemo(
            Path.Combine(root, "local"),
            new GameGuestAdmissionRequest(GameGuestBody.HumanFemale, "Host Alice"),
            serverExtensionFactory: factory);
        local.WaitUntilReady(TimeSpan.FromSeconds(10));
        ClientWorldSemanticCache localWorld = CreateWorldCache(local.BoundWorld);
        var localChat = new ClientChatState(local.ApplicationSessionEpoch);
        using var localUi = new UiClientRuntime(new NullRenderer(), new UiClientLimits(8, 8));
        using LocalQueuedUiClient localUiBridge = local.AttachUiClient(localUi);
        ClientNode? bob = null;
        ClientNode? carol = null;
        ClientNode? bobAgain = null;
        try
        {
            True(local.TryGetLatestView(out LocalPlayableAuthorityView before));
            ulong generation = before.GenerationId;
            True(before.ServerExtension.Available);
            True(!before.ServerExtension.HostingEnabled);

            var localInput = new ClientWorldInputSequencer(local.ControlSession);
            True(localInput.TryCreateSemanticCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDirect(12_000, 0),
                GamePlayerActions.None,
                true), out GamePlayerCommand localMove));
            True(local.TrySubmit(new LocalPlayableCommand(
                localMove, LocalPlayableSelection.None, false, default, false)));
            DriveLocalUntil(local, localWorld, localChat, () =>
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence == localMove.Sequence &&
                current.PlayerPosition != before.PlayerPosition);
            True(localInput.TryCreateSemanticCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop,
                GamePlayerActions.None,
                true), out GamePlayerCommand preHostStop));
            True(local.TrySubmit(new LocalPlayableCommand(
                preHostStop, LocalPlayableSelection.None, false, default, false)));
            DriveLocalUntil(local, localWorld, localChat, () =>
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence == preHostStop.Sequence);

            True(local.TryRequestHosting(1, enabled: true));
            DriveLocalUntil(local, localWorld, localChat, () =>
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastAdministrativeSequence == 1 &&
                current.LastAdministrativeSucceeded &&
                current.ServerExtension.HostingEnabled);
            True(local.TryGetLatestView(out LocalPlayableAuthorityView hosted));
            Equal(generation, hosted.GenerationId);
            string endpoint = hosted.ServerExtension.Endpoint ??
                throw new InvalidOperationException("The hosted endpoint was not published.");
            string fingerprint = hosted.ServerExtension.DeploymentFingerprint ??
                throw new InvalidOperationException("The hosted deployment fingerprint was not published.");

            bob = ClientNode.Connect(root, "hosted-bob", endpoint, fingerprint);
            True(bob.Session.TrySubmitGuest(new GameGuestAdmissionRequest(
                GameGuestBody.HumanMale, "Remote Bob")));
            DriveHostedUntil(local, localWorld, localChat, [bob], () =>
                bob.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active &&
                CountHumanActors(localWorld) == 2 &&
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.ServerExtension.RemoteParticipants == 1);
            Equal(generation, bob.Session.Playable.GetSnapshot().ServerGenerationId);
            var metadata = new ClientPublicPresentationMetadata[8];
            Equal(3, local.CopyPublicPresentationMetadata(metadata));
            True(metadata.Take(3).Any(static row => row.DisplayLabel == "Host Alice"));
            True(metadata.Take(3).Any(static row => row.DisplayLabel == "Remote Bob"));
            True(metadata.Take(3).Any(static row =>
                row.DisplayLabel == "Iron Sword" &&
                (row.Flags & ClientPublicPresentationFlags.ItemProperties) != 0));

            True(localInput.TryCreateSemanticCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop,
                GamePlayerActions.Interact,
                true), out GamePlayerCommand hostInteract));
            True(local.TrySubmit(new LocalPlayableCommand(
                hostInteract,
                LocalPlayableSelection.CabinDoor,
                false,
                default,
                false)));
            DriveHostedUntil(local, localWorld, localChat, [bob], () =>
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence == hostInteract.Sequence &&
                current.LastCommandResult.Interaction == GameWorldActionStatus.Accepted &&
                bob.Session.PublicPresentationState.TryGet(
                    hosted.PublicActor, out ClientPublicPresentationMetadata hostMetadata) &&
                hostMetadata.ActorAction == ClientPublicActorActionKind.Interact &&
                hostMetadata.ActorActionSequence == hostInteract.Sequence);

            carol = ClientNode.Connect(root, "hosted-carol", endpoint, fingerprint);
            True(carol.Session.TrySubmitGuest(new GameGuestAdmissionRequest(
                GameGuestBody.HumanFemale, "Remote Carol")));
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
                carol.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active &&
                CountHumanActors(localWorld) == 3 &&
                CountHumanActors(bob.Session.WorldCache) == 3 &&
                CountHumanActors(carol.Session.WorldCache) == 3 &&
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.ServerExtension.RemoteParticipants == 2);
            Equal(generation, carol.Session.Playable.GetSnapshot().ServerGenerationId);
            Equal(4, local.CopyPublicPresentationMetadata(metadata));
            True(metadata.Take(4).Any(static row => row.DisplayLabel == "Host Alice"));
            True(metadata.Take(4).Any(static row => row.DisplayLabel == "Remote Bob"));
            True(metadata.Take(4).Any(static row => row.DisplayLabel == "Remote Carol"));
            True(metadata.Take(4).Any(static row =>
                row.DisplayLabel == "Iron Sword" &&
                (row.Flags & ClientPublicPresentationFlags.ItemProperties) != 0));
            True(local.TryRequestOpenServerMonitor(2));
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
            {
                localUiBridge.Pump();
                return localUi.OpenViewCount == 1 &&
                    local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                    current.LastAdministrativeSequence == 2 &&
                    current.LastAdministrativeSucceeded;
            });
            var hostedMonitorView = new UiViewInstanceId(1);
            ReadOnlySpan<UiListRow> hostedMonitorRows =
                localUi.GetRows(hostedMonitorView, ServerMonitorUi.Participants);
            Equal(3, hostedMonitorRows.Length);
            Equal(1, hostedMonitorRows.ToArray().Count(static row =>
                row.Values[0].Text!.Contains("Host Alice", StringComparison.Ordinal) &&
                row.Values[0].Text!.Contains("Local", StringComparison.Ordinal)));
            Equal(2, hostedMonitorRows.ToArray().Count(static row =>
                row.Values[0].Text!.Contains("Remote", StringComparison.Ordinal)));

            RemotePlayableClientSnapshot bobBefore = bob.Session.Playable.GetSnapshot();
            RemotePlayableClientSnapshot carolBefore = carol.Session.Playable.GetSnapshot();
            True(localInput.TryCreateSemanticCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDirect(-12_000, 0),
                GamePlayerActions.None,
                true), out GamePlayerCommand hostedMove));
            True(local.TrySubmit(new LocalPlayableCommand(
                hostedMove, LocalPlayableSelection.None, false, default, false)));
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence == hostedMove.Sequence);
            Equal(bobBefore.Position, bob.Session.Playable.GetSnapshot().Position);
            Equal(carolBefore.Position, carol.Session.Playable.GetSnapshot().Position);
            True(localInput.TryCreateSemanticCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop,
                GamePlayerActions.None,
                true), out GamePlayerCommand hostedStop));
            True(local.TrySubmit(new LocalPlayableCommand(
                hostedStop, LocalPlayableSelection.None, false, default, false)));
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastCommandSequence == hostedStop.Sequence);
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
                CountHumanActors(bob.Session.WorldCache) == 3 &&
                CountHumanActors(carol.Session.WorldCache) == 3);

            True(local.TrySubmitChat(1, ClientChatSpeechMode.Say, "local packet-free hello"));
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
                ContainsChat(bob.Session.ChatState, "local packet-free hello") &&
                ContainsChat(carol.Session.ChatState, "local packet-free hello"));
            True(bob.Session.Chat.TrySubmit("remote network hello"));
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
                ContainsChat(localChat, "remote network hello") &&
                ContainsChat(carol.Session.ChatState, "remote network hello"));
            True(local.TrySubmitChat(2, ClientChatSpeechMode.Global, "host global hello"));
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
                ContainsChat(localChat, "host global hello", ClientChatOccurrenceKind.GlobalSpeech) &&
                ContainsChat(bob.Session.ChatState, "host global hello", ClientChatOccurrenceKind.GlobalSpeech) &&
                ContainsChat(carol.Session.ChatState, "host global hello", ClientChatOccurrenceKind.GlobalSpeech));
            True(factory.RequestSystem("hosted authority test broadcast"));
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
                ContainsChat(localChat, "hosted authority test broadcast",
                    ClientChatOccurrenceKind.SystemBroadcast) &&
                ContainsChat(bob.Session.ChatState, "hosted authority test broadcast",
                    ClientChatOccurrenceKind.SystemBroadcast) &&
                ContainsChat(carol.Session.ChatState, "hosted authority test broadcast",
                    ClientChatOccurrenceKind.SystemBroadcast));

            ulong localUiAppliedBeforeRemoteInventory =
                local.GetSnapshot().UiOperationsApplied;
            True(bob.Session.Playable.TrySubmitCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
                flags: RemotePlayableCommandFlags.OpenInventoryRequested));
            ulong bobInventory = bob.Session.Playable.Input.Snapshot().LastSequence;
            DriveHostedUntil(local, localWorld, localChat, [bob, carol], () =>
                bob.Session.Playable.GetSnapshot().LastProcessedCommandSequence == bobInventory &&
                bob.Session.UiClient.OpenViewCount == 1);
            Equal(0, carol.Session.UiClient.OpenViewCount);
            Equal(localUiAppliedBeforeRemoteInventory,
                local.GetSnapshot().UiOperationsApplied);

            bob.Dispose();
            bob = null;
            DriveHostedUntil(local, localWorld, localChat, [carol], () =>
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.ServerExtension.RemoteParticipants == 1 &&
                CountHumanActors(localWorld) == 2 &&
                CountHumanActors(carol.Session.WorldCache) == 2);
            True(local.TryGetLatestView(out LocalPlayableAuthorityView afterLeave));
            Equal(generation, afterLeave.GenerationId);
            True(carol.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active);
            True(local.TrySubmitChat(3, ClientChatSpeechMode.Global, "global after Bob left"));
            DriveHostedUntil(local, localWorld, localChat, [carol], () =>
                ContainsChat(localChat, "global after Bob left", ClientChatOccurrenceKind.GlobalSpeech) &&
                ContainsChat(carol.Session.ChatState, "global after Bob left",
                    ClientChatOccurrenceKind.GlobalSpeech));

            bobAgain = ClientNode.Connect(root, "hosted-bob-again", endpoint, fingerprint);
            True(bobAgain.Session.TrySubmitGuest(new GameGuestAdmissionRequest(
                GameGuestBody.HumanMale, "Remote Bob Again")));
            DriveHostedUntil(local, localWorld, localChat, [carol, bobAgain], () =>
                bobAgain.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active &&
                CountHumanActors(localWorld) == 3 &&
                CountHumanActors(carol.Session.WorldCache) == 3 &&
                CountHumanActors(bobAgain.Session.WorldCache) == 3 &&
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.ServerExtension.RemoteParticipants == 2);
            Equal(generation, bobAgain.Session.Playable.GetSnapshot().ServerGenerationId);
            True(!ContainsChat(bobAgain.Session.ChatState, "hosted authority test broadcast"));
            True(!ContainsChat(bobAgain.Session.ChatState, "global after Bob left"));

            True(local.TryRequestHosting(3, enabled: false));
            DriveLocalUntil(local, localWorld, localChat, () =>
                local.TryGetLatestView(out LocalPlayableAuthorityView current) &&
                current.LastAdministrativeSequence == 3 &&
                current.LastAdministrativeSucceeded &&
                !current.ServerExtension.HostingEnabled &&
                current.ServerExtension.RemoteParticipants == 0);
            True(local.TryGetLatestView(out LocalPlayableAuthorityView stopped));
            Equal(generation, stopped.GenerationId);
            Console.WriteLine(
                $"EVIDENCE M0.0 embedded_hosted generation={generation} " +
                "host_path=packet-free remote_path=udp participants=3 " +
                "chat=three_recipient metadata=4 owner_ui=isolated reconnect=pass " +
                "stop_host_world_survives=pass");
        }
        finally
        {
            bobAgain?.Dispose();
            carol?.Dispose();
            bob?.Dispose();
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void OneDedicatedAuthorityServesThreeIndependentRemoteSessions()
    {
        const int maximumParticipants = 3;
        var realm = new ApplicationRealmId(0x4D00, 1);
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using SharedPlayableAuthority authority = SharedPlayableAuthority.CreateConceptDemo(
            new SharedPlayableAuthorityLimits(maximumParticipants));
        var shared = SharedUdpProtocolLimits.Default with
        {
            MaximumSessions = maximumParticipants,
        };
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0),
            identity,
            NetworkProtocolLimits.Default,
            UdpProtocolLimits.Default,
            shared,
            HostedUdpAdmissionLimits.Default with
            {
                MaximumChallengesPerAddressPerBucket = 64,
                MaximumValidationsPerAddressPerBucket = 64,
            },
            new SharedRemoteGuestPlayableApplicationFactory(authority, realm),
            UltraSimGameProtocol.Manifest);
        string root = Path.Combine(Path.GetTempPath(),
            $"ultrasim-m00-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var clients = new List<ClientNode>();
        try
        {
            ClientNode alice = ClientNode.Connect(root, "alice", host, identity);
            clients.Add(alice);
            True(alice.Session.TrySubmitGuest(new GameGuestAdmissionRequest(
                GameGuestBody.HumanFemale, "Alice")));
            DriveUntil(host, authority, clients, () =>
                alice.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active &&
                CountHumanActors(alice.Session.WorldCache) == 1);

            ClientNode bob = ClientNode.Connect(root, "bob", host, identity);
            clients.Add(bob);
            True(bob.Session.TrySubmitGuest(new GameGuestAdmissionRequest(
                GameGuestBody.HumanMale, "Bob")));
            DriveUntil(host, authority, clients, () =>
                bob.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active &&
                clients.All(static client => CountHumanActors(client.Session.WorldCache) == 2));

            ClientNode carol = ClientNode.Connect(root, "carol", host, identity);
            clients.Add(carol);
            True(carol.Session.TrySubmitGuest(new GameGuestAdmissionRequest(
                GameGuestBody.HumanFemale, "Carol")));
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                client.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active));
            ulong generation = authority.GetSnapshot().GenerationId;
            Equal(3, authority.GetSnapshot().ParticipantCount);
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                client.Session.PublicPresentationState.GetSnapshot().CurrentRecords >= 3));
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                CountHumanActors(client.Session.WorldCache) == 3));
            for (int index = 0; index < clients.Count; index++)
            {
                Equal(generation,
                    clients[index].Session.Playable.GetSnapshot().ServerGenerationId);
                Equal(3, CountHumanActors(clients[index].Session.WorldCache));
            }

            DriveUntil(host, authority, clients, () => clients.All(static client =>
                TryFindPublic(
                    client.Session.WorldCache,
                    GameWorldPublicPrefabIds.IronSword,
                    out _)));
            True(TryFindPublic(
                alice.Session.WorldCache,
                GameWorldPublicPrefabIds.IronSword,
                out WorldPublicRecord sharedSword));
            True(alice.Session.Playable.TrySubmitCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true),
                sharedSword.Id));
            ulong pickupSequence = alice.Session.Playable.Input.Snapshot().LastSequence;
            DriveUntil(host, authority, clients, () =>
                alice.Session.Playable.GetSnapshot().LastProcessedCommandSequence == pickupSequence &&
                alice.Session.Playable.GetSnapshot().Interaction == RemotePlayableActionStatus.Accepted &&
                clients.All(client => !ContainsPublic(client.Session.WorldCache, sharedSword.Id)));

            True(alice.Session.Playable.TrySubmitCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
                flags: RemotePlayableCommandFlags.OpenInventoryRequested));
            ulong inventorySequence = alice.Session.Playable.Input.Snapshot().LastSequence;
            DriveUntil(host, authority, clients, () =>
                alice.Session.Playable.GetSnapshot().LastProcessedCommandSequence == inventorySequence &&
                alice.Session.UiClient.OpenViewCount == 1);
            var aliceInventory = new UiViewInstanceId(1);
            UiRowId swordRow = FindRow(
                alice.Session.UiClient.GetRows(aliceInventory, InventoryUi.Items), "Iron Sword");
            ulong beforeSelection = alice.Session.UiClient.GetView(aliceInventory).LastSequence;
            alice.Session.UiClient.SubmitAction(aliceInventory, InventoryUi.Select, swordRow);
            DriveUntil(host, authority, clients, () =>
                alice.Session.UiClient.GetView(aliceInventory).LastSequence > beforeSelection &&
                alice.Session.UiClient.GetActionState(aliceInventory, InventoryUi.Equip).Enabled);
            string aliceSwordDetails =
                alice.Session.UiClient.GetValue(aliceInventory, InventoryUi.Details).Text ?? string.Empty;
            True(aliceSwordDetails.Contains("Damage ", StringComparison.Ordinal));
            True(aliceSwordDetails.Contains("Quality ", StringComparison.Ordinal));
            True(aliceSwordDetails.Contains("Durability ", StringComparison.Ordinal));
            alice.Session.UiClient.SubmitAction(aliceInventory, InventoryUi.Equip);
            DriveUntil(host, authority, clients, () =>
            {
                UiListRow row = alice.Session.UiClient.GetRows(aliceInventory, InventoryUi.Items)
                    .ToArray().Single(candidate => candidate.Id == swordRow);
                RemotePlayableClientSnapshot owner = alice.Session.Playable.GetSnapshot();
                return string.Equals(row.Values[2].Text, "Main Hand", StringComparison.Ordinal) &&
                    TryFindPublicNear(
                        alice.Session.WorldCache,
                        GameWorldPublicPrefabIds.IronSword,
                        owner.Position.GlobalXMillimeters,
                        owner.Position.GlobalZMillimeters,
                        250,
                        out _);
            });
            RemotePlayableClientSnapshot equippedOwner = alice.Session.Playable.GetSnapshot();
            DriveUntil(host, authority, clients, () => clients.All(client =>
                client.Session.PublicPresentationState.TryGet(
                    equippedOwner.PublicActor, out ClientPublicPresentationMetadata metadata) &&
                (metadata.Flags & ClientPublicPresentationFlags.ActorState) != 0 &&
                metadata.VerticalPhase is >= ClientPublicActorVerticalPhase.Grounded and
                    <= ClientPublicActorVerticalPhase.Falling &&
                metadata.MainHandPrefabId == GameWorldPublicPrefabIds.IronSword));
            True(TryFindPublicNear(
                alice.Session.WorldCache,
                GameWorldPublicPrefabIds.IronSword,
                equippedOwner.Position.GlobalXMillimeters,
                equippedOwner.Position.GlobalZMillimeters,
                250,
                out WorldPublicRecord equippedSword));
            True(equippedSword.Id != sharedSword.Id);
            True(alice.Session.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.Primary, true)));
            ulong primarySequence = alice.Session.Playable.Input.Snapshot().LastSequence;
            DriveUntil(host, authority, clients, () =>
                alice.Session.Playable.GetSnapshot().LastProcessedCommandSequence == primarySequence &&
                alice.Session.Playable.GetSnapshot().Primary == RemotePlayableActionStatus.Accepted);
            DriveUntil(host, authority, clients, () => clients.All(client =>
                client.Session.PublicPresentationState.TryGet(
                    equippedOwner.PublicActor, out ClientPublicPresentationMetadata metadata) &&
                metadata.ActorAction == ClientPublicActorActionKind.Primary &&
                metadata.ActorActionSequence == primarySequence));
            ulong beforeDropSelection = alice.Session.UiClient.GetView(aliceInventory).LastSequence;
            alice.Session.UiClient.SubmitAction(aliceInventory, InventoryUi.Select, swordRow);
            DriveUntil(host, authority, clients, () =>
                alice.Session.UiClient.GetView(aliceInventory).LastSequence > beforeDropSelection &&
                alice.Session.UiClient.GetActionState(aliceInventory, InventoryUi.Drop).Enabled);
            alice.Session.UiClient.SubmitAction(aliceInventory, InventoryUi.Drop);
            DriveUntil(host, authority, clients, () => clients.All(client =>
                ContainsPublicWithFlags(
                    client.Session.WorldCache,
                    equippedSword.Id,
                    WorldPublicStateFlags.Visible | WorldPublicStateFlags.GroundItem)));
            DriveUntil(host, authority, clients, () => clients.All(client =>
                client.Session.PublicPresentationState.TryGet(
                    equippedOwner.PublicActor, out ClientPublicPresentationMetadata metadata) &&
                metadata.MainHandPrefabId == 0));

            True(bob.Session.Playable.TrySubmitCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop, GamePlayerActions.Interact, true),
                equippedSword.Id));
            ulong bobPickupSequence = bob.Session.Playable.Input.Snapshot().LastSequence;
            DriveUntil(host, authority, clients, () =>
                bob.Session.Playable.GetSnapshot().LastProcessedCommandSequence == bobPickupSequence &&
                bob.Session.Playable.GetSnapshot().Interaction == RemotePlayableActionStatus.Accepted &&
                clients.All(client => !ContainsPublic(client.Session.WorldCache, equippedSword.Id)));
            True(bob.Session.Playable.TrySubmitCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
                flags: RemotePlayableCommandFlags.OpenInventoryRequested));
            ulong bobInventorySequence = bob.Session.Playable.Input.Snapshot().LastSequence;
            DriveUntil(host, authority, clients, () =>
                bob.Session.Playable.GetSnapshot().LastProcessedCommandSequence == bobInventorySequence &&
                bob.Session.UiClient.OpenViewCount == 1);
            var bobItemView = new UiViewInstanceId(1);
            UiRowId bobSwordRow =
                FindRow(bob.Session.UiClient.GetRows(bobItemView, InventoryUi.Items), "Iron Sword");
            ulong bobBeforeSelection = bob.Session.UiClient.GetView(bobItemView).LastSequence;
            bob.Session.UiClient.SubmitAction(bobItemView, InventoryUi.Select, bobSwordRow);
            DriveUntil(host, authority, clients, () =>
                bob.Session.UiClient.GetView(bobItemView).LastSequence > bobBeforeSelection);
            Equal(
                aliceSwordDetails,
                bob.Session.UiClient.GetValue(bobItemView, InventoryUi.Details).Text);

            RemotePlayableClientSnapshot aliceBefore = alice.Session.Playable.GetSnapshot();
            RemotePlayableClientSnapshot bobBefore = bob.Session.Playable.GetSnapshot();
            True(alice.Session.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.CreateDirect(12_000, 0),
                GamePlayerActions.None,
                true)));
            DriveUntil(host, authority, clients, () =>
                alice.Session.Playable.GetSnapshot().LastProcessedCommandSequence ==
                    alice.Session.Playable.Input.Snapshot().LastSequence &&
                alice.Session.Playable.GetSnapshot().Position.GlobalXMillimeters !=
                    aliceBefore.Position.GlobalXMillimeters);
            Equal(bobBefore.Position.GlobalXMillimeters,
                bob.Session.Playable.GetSnapshot().Position.GlobalXMillimeters);
            Equal(bobBefore.Position.GlobalZMillimeters,
                bob.Session.Playable.GetSnapshot().Position.GlobalZMillimeters);

            True(alice.Session.Chat.TrySubmit("shared authority hello"));
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                client.Session.ChatState.GetSnapshot().RetainedOccurrences >= 1));
            for (int index = 0; index < clients.Count; index++)
                True(ContainsChat(clients[index].Session.ChatState, "shared authority hello"));
            True(alice.Session.Chat.TrySubmit(
                "dedicated global hello", ClientChatSpeechMode.Global));
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                ContainsChat(client.Session.ChatState, "dedicated global hello",
                    ClientChatOccurrenceKind.GlobalSpeech)));
            True(authority.PublishSystem("dedicated authority test broadcast").Accepted);
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                ContainsChat(client.Session.ChatState, "dedicated authority test broadcast",
                    ClientChatOccurrenceKind.SystemBroadcast)));

            True(!authority.PublishTestMarker(
                GameTestMarkerPhase.Complete, "Far Island journey").Accepted);
            GameTestMarkerResult markerStart = authority.PublishTestMarker(
                GameTestMarkerPhase.Start, "Far Island journey");
            True(markerStart.Accepted);
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                ContainsChat(client.Session.ChatState,
                    "TEST START #1: Far Island journey",
                    ClientChatOccurrenceKind.SystemBroadcast)));
            GameTestMarkerResult markerComplete = authority.PublishTestMarker(
                GameTestMarkerPhase.Complete, "Far Island journey");
            True(markerComplete.Accepted);
            Equal(markerStart.Sequence, markerComplete.Sequence);
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                ContainsChat(client.Session.ChatState,
                    "TEST COMPLETE #1: Far Island journey",
                    ClientChatOccurrenceKind.SystemBroadcast)));

            ServerMonitorSnapshot monitorSnapshot = authority.CaptureServerMonitor();
            Equal(generation, monitorSnapshot.GenerationId);
            Equal(3, monitorSnapshot.Participants.Length);
            Equal(maximumParticipants, monitorSnapshot.MaximumParticipants);
            True(monitorSnapshot.EntityCount >= 3);
            Equal("#1 COMPLETE — Far Island journey", monitorSnapshot.TestMarker!);
            True(monitorSnapshot.Participants.ToArray().All(static row =>
                row.Connection == ServerMonitorConnectionKind.Remote &&
                row.RoundTripMilliseconds is >= 0 and <= 60_000));
            True(monitorSnapshot.Participants.ToArray().Select(static row => row.DisplayName)
                .OrderBy(static name => name, StringComparer.Ordinal)
                .SequenceEqual(new[] { "Alice", "Bob", "Carol" }));

            True(carol.Session.Playable.TrySubmitCommand(
                new ClientWorldCommandIntent(
                    GameLocomotionUpdate.Stop, GamePlayerActions.None, true),
                flags: RemotePlayableCommandFlags.OpenServerMonitorRequested));
            ulong monitorCommand = carol.Session.Playable.Input.Snapshot().LastSequence;
            DriveUntil(host, authority, clients, () =>
                carol.Session.Playable.GetSnapshot().LastProcessedCommandSequence == monitorCommand &&
                carol.Session.UiClient.OpenViewCount == 1);
            var monitorView = new UiViewInstanceId(1);
            ReadOnlySpan<UiListRow> monitorRows =
                carol.Session.UiClient.GetRows(monitorView, ServerMonitorUi.Participants);
            Equal(3, monitorRows.Length);
            True(monitorRows.ToArray().All(static row =>
                row.Values[0].Text!.Contains("Remote", StringComparison.Ordinal) &&
                row.Values[0].Text!.Contains("Chunk (", StringComparison.Ordinal) &&
                !row.Values[0].Text!.Contains("127.0.0.1", StringComparison.Ordinal)));
            True((carol.Session.UiClient.GetValue(
                    monitorView, ServerMonitorUi.Marker).Text ?? string.Empty)
                .Contains("#1 COMPLETE — Far Island journey", StringComparison.Ordinal));
            Equal(1, alice.Session.UiClient.OpenViewCount);
            Equal(1, bob.Session.UiClient.OpenViewCount);

            bob.Dispose();
            clients.RemoveAt(1);
            DriveUntil(host, authority, clients,
                () => authority.GetSnapshot().ParticipantCount == 2);
            ServerMonitorSnapshot afterBobLeft = authority.CaptureServerMonitor();
            Equal(2, afterBobLeft.Participants.Length);
            True(afterBobLeft.Participants.ToArray().All(static row => row.DisplayName != "Bob"));
            Equal(generation, authority.GetSnapshot().GenerationId);
            True(alice.Session.Playable.TrySubmitCommand(new ClientWorldCommandIntent(
                GameLocomotionUpdate.Stop, GamePlayerActions.None, true)));
            ulong stopSequence = alice.Session.Playable.Input.Snapshot().LastSequence;
            DriveUntil(host, authority, clients, () =>
                alice.Session.Playable.GetSnapshot().LastProcessedCommandSequence == stopSequence);
            True(carol.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active);
            True(alice.Session.Chat.TrySubmit(
                "dedicated global after Bob left", ClientChatSpeechMode.Global));
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                ContainsChat(client.Session.ChatState, "dedicated global after Bob left",
                    ClientChatOccurrenceKind.GlobalSpeech)));

            ClientNode bobAgain = ClientNode.Connect(root, "bob-again", host, identity);
            clients.Add(bobAgain);
            True(bobAgain.Session.TrySubmitGuest(new GameGuestAdmissionRequest(
                GameGuestBody.HumanMale, "Bob Again")));
            DriveUntil(host, authority, clients, () =>
                bobAgain.Session.Playable.GetSnapshot().State == RemotePlayableClientState.Active &&
                authority.GetSnapshot().ParticipantCount == 3);
            DriveUntil(host, authority, clients, () => clients.All(static client =>
                client.Session.PublicPresentationState.GetSnapshot().CurrentRecords >= 3));
            Equal(generation, authority.GetSnapshot().GenerationId);
            ServerMonitorSnapshot afterBobReturned = authority.CaptureServerMonitor();
            Equal(3, afterBobReturned.Participants.Length);
            True(afterBobReturned.Participants.ToArray().Any(static row =>
                row.DisplayName == "Bob Again"));
            True(afterBobReturned.Participants.ToArray().All(static row => row.DisplayName != "Bob"));
            True(!ContainsChat(
                bobAgain.Session.ChatState, "dedicated authority test broadcast"));
            True(!ContainsChat(
                bobAgain.Session.ChatState, "dedicated global after Bob left"));

            SharedPlayableAuthoritySnapshot evidence = authority.GetSnapshot();
            Console.WriteLine(
                $"EVIDENCE M0.0 dedicated_shared generation={evidence.GenerationId} " +
                $"participants={evidence.ParticipantCount}/{evidence.MaximumParticipants} " +
                $"joins={evidence.Joins} leaves={evidence.Leaves} ticks={evidence.Ticks} " +
                "public_actors=3 chat_recipients=3 independent_commands=pass reconnect=pass " +
                "item_world_inventory_equip_primary_drop_other_owner=pass " +
                "monitor_rows=3 sanitized=pass marker_start_complete=pass");
        }
        finally
        {
            for (int index = clients.Count - 1; index >= 0; index--) clients[index].Dispose();
            for (int step = 0; step < 20_000 && authority.GetSnapshot().ParticipantCount != 0;
                 step++)
            {
                host.Pump();
                _ = authority.TickDue();
                Thread.Yield();
            }
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
        Equal(0, authority.GetSnapshot().ParticipantCount);
    }

    private static void DriveUntil(
        HostedUdpAdmissionHost host,
        SharedPlayableAuthority authority,
        IReadOnlyList<ClientNode> clients,
        Func<bool> condition,
        int maximumSteps = 12_000)
    {
        for (int step = 0; step < maximumSteps; step++)
        {
            for (int index = 0; index < clients.Count; index++) clients[index].Direct.Pump();
            host.Pump();
            _ = authority.TickDue();
            for (int index = 0; index < clients.Count; index++)
            {
                clients[index].Direct.Pump();
                _ = clients[index].Session.ProcessReceived();
            }
            if (condition()) return;
            if ((step & 15) == 0) Thread.Sleep(1);
            else Thread.Yield();
        }
        throw new InvalidOperationException(
            $"The shared remote authority did not converge: {authority.GetSnapshot()}; " +
            $"server={DescribeServer(authority)}; transport={DescribeTransport(host, clients)}; " +
            string.Join(" | ", clients.Select(static client =>
                $"playable={client.Session.Playable.GetSnapshot()} " +
                $"session={client.Session.GetSnapshot()} " +
                $"peer={client.Direct.Peer?.GetSnapshot()} " +
                $"actors={CountHumanActors(client.Session.WorldCache)} " +
                $"public={DescribePublic(client.Session.WorldCache)}")));
    }

    private static void DriveLocalUntil(
        LocalPlayableSession local,
        ClientWorldSemanticCache localWorld,
        ClientChatState localChat,
        Func<bool> condition,
        int maximumSteps = 60_000) =>
        DriveHostedUntil(local, localWorld, localChat, [], condition, maximumSteps);

    private static void DriveHostedUntil(
        LocalPlayableSession local,
        ClientWorldSemanticCache localWorld,
        ClientChatState localChat,
        IReadOnlyList<ClientNode> clients,
        Func<bool> condition,
        int maximumSteps = 60_000)
    {
        for (int step = 0; step < maximumSteps; step++)
        {
            for (int index = 0; index < clients.Count; index++) clients[index].Direct.Pump();
            _ = local.PumpWorld(localWorld);
            _ = local.PumpChat(localChat);
            for (int index = 0; index < clients.Count; index++)
            {
                clients[index].Direct.Pump();
                _ = clients[index].Session.ProcessReceived();
            }
            if (condition()) return;
            if ((step & 15) == 0) Thread.Sleep(1);
            else Thread.Yield();
        }
        throw new InvalidOperationException(
            $"The embedded hosted authority did not converge: local={local.GetSnapshot()} " +
            $"localActors={CountHumanActors(localWorld)} " +
            $"localPublic={DescribePublic(localWorld)} " +
            $"localView={(local.TryGetLatestView(out LocalPlayableAuthorityView latest) ? latest : default)}; " +
            string.Join(" | ", clients.Select(static client =>
                $"playable={client.Session.Playable.GetSnapshot()} " +
                $"session={client.Session.GetSnapshot()} peer={client.Direct.Peer?.GetSnapshot()} " +
                $"actors={CountHumanActors(client.Session.WorldCache)} " +
                $"public={DescribePublic(client.Session.WorldCache)}")));
    }

    private static string DescribeTransport(
        HostedUdpAdmissionHost host,
        IReadOnlyList<ClientNode> clients)
    {
        return string.Join(" | ", clients.Select(client =>
        {
            ConnectionId connection = client.Direct.GetSnapshot().Connection;
            return host.TryGetSession(connection, out HostedUdpProtocolSession? session)
                ? session!.Server.GetSnapshot().ToString()
                : "missing";
        }));
    }

    private static string DescribeServer(SharedPlayableAuthority authority)
    {
        var snapshots = new RemoteGuestPlayableServerSnapshot[8];
        int count = authority.CopyRemoteSessionSnapshots(snapshots);
        return count < 0 ? "overflow" : string.Join(" | ", snapshots.Take(count).Select(
            static snapshot => $"participant={snapshot.Participant} " +
                $"guest={snapshot.GuestAdmitted}/{snapshot.AdmissionApplied} " +
                $"world={snapshot.World}"));
    }

    private static int CountHumanActors(ClientWorldSemanticCache cache)
    {
        Span<ClientSemanticChunkInfo> chunks = stackalloc ClientSemanticChunkInfo[192];
        True(cache.TryCopyResidentInfo(chunks, out int count, out _));
        int actors = 0;
        for (int chunk = 0; chunk < count; chunk++)
        {
            if (!cache.TryGetResidentView(chunks[chunk].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (WorldPublicRecord record in view.PublicRecords)
                if (record.PrefabId is GameWorldPublicPrefabIds.Character or
                    GameWorldPublicPrefabIds.CharacterMale)
                    actors++;
        }
        return actors;
    }

    private static bool TryFindPublic(
        ClientWorldSemanticCache cache,
        uint prefabId,
        out WorldPublicRecord result)
    {
        Span<ClientSemanticChunkInfo> chunks = stackalloc ClientSemanticChunkInfo[192];
        True(cache.TryCopyResidentInfo(chunks, out int count, out _));
        for (int chunk = 0; chunk < count; chunk++)
        {
            if (!cache.TryGetResidentView(chunks[chunk].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (WorldPublicRecord record in view.PublicRecords)
            {
                if (record.PrefabId != prefabId) continue;
                result = record;
                return true;
            }
        }
        result = default;
        return false;
    }

    private static bool ContainsPublic(
        ClientWorldSemanticCache cache,
        WorldPublicEntityId id)
    {
        Span<ClientSemanticChunkInfo> chunks = stackalloc ClientSemanticChunkInfo[192];
        True(cache.TryCopyResidentInfo(chunks, out int count, out _));
        for (int chunk = 0; chunk < count; chunk++)
        {
            if (!cache.TryGetResidentView(chunks[chunk].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (WorldPublicRecord record in view.PublicRecords)
                if (record.Id == id) return true;
        }
        return false;
    }

    private static bool ContainsPublicWithFlags(
        ClientWorldSemanticCache cache,
        WorldPublicEntityId id,
        WorldPublicStateFlags requiredFlags)
    {
        Span<ClientSemanticChunkInfo> chunks = stackalloc ClientSemanticChunkInfo[192];
        True(cache.TryCopyResidentInfo(chunks, out int count, out _));
        for (int chunk = 0; chunk < count; chunk++)
        {
            if (!cache.TryGetResidentView(chunks[chunk].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (WorldPublicRecord record in view.PublicRecords)
                if (record.Id == id && (record.StateFlags & requiredFlags) == requiredFlags)
                    return true;
        }
        return false;
    }

    private static bool TryFindPublicNear(
        ClientWorldSemanticCache cache,
        uint prefabId,
        long x,
        long z,
        long maximumDistanceMillimeters,
        out WorldPublicRecord result)
    {
        Span<ClientSemanticChunkInfo> chunks = stackalloc ClientSemanticChunkInfo[192];
        True(cache.TryCopyResidentInfo(chunks, out int count, out _));
        Int128 maximumSquared = (Int128)maximumDistanceMillimeters * maximumDistanceMillimeters;
        for (int chunk = 0; chunk < count; chunk++)
        {
            if (!cache.TryGetResidentView(chunks[chunk].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (WorldPublicRecord record in view.PublicRecords)
            {
                if (record.PrefabId != prefabId) continue;
                Int128 dx = (Int128)record.Position.GlobalXMillimeters - x;
                Int128 dz = (Int128)record.Position.GlobalZMillimeters - z;
                if (dx * dx + dz * dz > maximumSquared) continue;
                result = record;
                return true;
            }
        }
        result = default;
        return false;
    }

    private static UiRowId FindRow(ReadOnlySpan<UiListRow> rows, string name)
    {
        foreach (UiListRow row in rows)
            if (row.Values.Length != 0 &&
                string.Equals(row.Values[0].Text, name, StringComparison.Ordinal))
                return row.Id;
        throw new InvalidOperationException($"The UI row '{name}' was unavailable.");
    }

    private static string DescribePublic(ClientWorldSemanticCache cache)
    {
        Span<ClientSemanticChunkInfo> chunks = stackalloc ClientSemanticChunkInfo[192];
        if (!cache.TryCopyResidentInfo(chunks, out int count, out _)) return "invalid";
        var prefabs = new List<uint>();
        for (int chunk = 0; chunk < count; chunk++)
        {
            if (!cache.TryGetResidentView(chunks[chunk].Handle, out ClientSemanticChunkView view))
                continue;
            foreach (WorldPublicRecord record in view.PublicRecords) prefabs.Add(record.PrefabId);
        }
        return $"{prefabs.Count}:[{string.Join(',', prefabs.Take(24))}]";
    }

    private static bool ContainsChat(
        ClientChatState state,
        string text,
        ClientChatOccurrenceKind? kind = null)
    {
        var occurrences = new ClientChatOccurrence[16];
        int count = state.CopyRecent(occurrences);
        for (int index = 0; index < count; index++)
            if (string.Equals(occurrences[index].Text, text, StringComparison.Ordinal) &&
                (!kind.HasValue || occurrences[index].Kind == kind.Value))
                return true;
        return false;
    }

    private static ClientWorldSemanticCache CreateWorldCache()
    {
        WorldPack pack = GamePlayableWorld.CreateConceptDemoWorldPack();
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
        True(pack.TryBind(binding, out BoundWorldPack? bound, out _));
        return CreateWorldCache(bound!);
    }

    private static ClientWorldSemanticCache CreateWorldCache(BoundWorldPack bound)
    {
        WorldPack pack = bound.Pack;
        var cache = new ClientWorldSemanticCache(
            bound, new ClientWorldSemanticCacheLimits(192, 256, 32, 64, 4_096));
        for (int index = 0; index < pack.ChunkCount; index++)
        {
            True(pack.TryGetChunkKey(index, out var key));
            True(cache.TryRequest(key, out ClientSemanticChunkHandle handle, out _));
            True(cache.TryStageFromBaseWorld(handle, [], default, out _));
            True(cache.TryPublish(handle, out _));
        }
        return cache;
    }

    private static void True(bool condition)
    {
        if (!condition) throw new InvalidOperationException("Expected true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected {expected}, got {actual}.");
    }

    private sealed class ClientNode : IDisposable
    {
        private readonly string _trustRoot;
        private readonly HostedUdpAdmissionHost? _host;
        private bool _disposed;

        private ClientNode(
            string trustRoot,
            HostedUdpAdmissionHost? host,
            ClientDirectConnectSession direct,
            RemotePlayableSession session)
        {
            _trustRoot = trustRoot;
            _host = host;
            Direct = direct;
            Session = session;
        }

        internal ClientDirectConnectSession Direct { get; }
        internal RemotePlayableSession Session { get; }

        internal static ClientNode Connect(
            string root,
            string name,
            HostedUdpAdmissionHost host,
            NetworkServerIdentity identity)
        {
            string trustRoot = Path.Combine(root, name);
            Directory.CreateDirectory(trustRoot);
            var direct = new ClientDirectConnectSession(
                new ClientServerTrustRegistry(),
                new ClientServerTrustFileStore(Path.Combine(trustRoot, "servers.json")),
                NetworkProtocolLimits.Default,
                UdpProtocolLimits.Default);
            try
            {
                ClientDeploymentKeyFingerprint fingerprint =
                    ClientDeploymentKeyFingerprint.Compute(identity.PublicKey);
                True(direct.TryStart(host.LocalEndPoint.ToString(), fingerprint.ToString()));
                for (int step = 0; step < 10_000; step++)
                {
                    direct.Pump();
                    host.Pump();
                    if (direct.State == ClientDirectConnectState.Established) break;
                    if (direct.State == ClientDirectConnectState.Failed)
                        throw new InvalidOperationException(
                            $"Shared Direct Connect failed: {direct.GetSnapshot()}.");
                    Thread.Sleep(1);
                }
                Equal(ClientDirectConnectState.Established, direct.State);
                ClientDirectConnectSnapshot connected = direct.GetSnapshot();
                var session = new RemotePlayableSession(
                    direct.Peer ?? throw new InvalidOperationException("Client peer missing."),
                    connected.ApplicationSessionEpoch,
                    CreateWorldCache(),
                    new NullRenderer(),
                    new ClientChatLimits(16, 256, 128),
                    new UiClientLimits(8, 8),
                    maximumPendingWorldProofs: 256);
                return new ClientNode(trustRoot, host, direct, session);
            }
            catch
            {
                direct.Dispose();
                throw;
            }
        }

        internal static ClientNode Connect(
            string root,
            string name,
            string endpoint,
            string fingerprint)
        {
            string trustRoot = Path.Combine(root, name);
            Directory.CreateDirectory(trustRoot);
            var direct = new ClientDirectConnectSession(
                new ClientServerTrustRegistry(),
                new ClientServerTrustFileStore(Path.Combine(trustRoot, "servers.json")),
                NetworkProtocolLimits.Default,
                UdpProtocolLimits.Default);
            try
            {
                True(direct.TryStart(endpoint, fingerprint));
                for (int step = 0; step < 10_000; step++)
                {
                    direct.Pump();
                    if (direct.State == ClientDirectConnectState.Established) break;
                    if (direct.State == ClientDirectConnectState.Failed)
                        throw new InvalidOperationException(
                            $"Embedded-host Direct Connect failed: {direct.GetSnapshot()}.");
                    Thread.Sleep(1);
                }
                Equal(ClientDirectConnectState.Established, direct.State);
                ClientDirectConnectSnapshot connected = direct.GetSnapshot();
                var session = new RemotePlayableSession(
                    direct.Peer ?? throw new InvalidOperationException("Client peer missing."),
                    connected.ApplicationSessionEpoch,
                    CreateWorldCache(),
                    new NullRenderer(),
                    new ClientChatLimits(16, 256, 128),
                    new UiClientLimits(8, 8),
                    maximumPendingWorldProofs: 256);
                return new ClientNode(trustRoot, null, direct, session);
            }
            catch
            {
                direct.Dispose();
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _ = Session.TryRequestDetach();
            Session.Dispose();
            if (Direct.State == ClientDirectConnectState.Established)
            {
                Direct.BeginDisconnect();
                for (int step = 0; step < 20_000 &&
                     Direct.State != ClientDirectConnectState.Closed; step++)
                {
                    Direct.Pump();
                    _host?.Pump();
                    Thread.Yield();
                }
            }
            Direct.Dispose();
            if (Directory.Exists(_trustRoot)) Directory.Delete(_trustRoot, recursive: true);
        }
    }

    private sealed class EmbeddedTestExtensionFactory : ILocalPlayableServerExtensionFactory
    {
        private EmbeddedTestExtension? _extension;

        public ILocalPlayableServerExtension Create(LocalPlayableServerContext context) =>
            _extension = new EmbeddedTestExtension(context);

        internal bool RequestSystem(string text) =>
            (_extension ?? throw new InvalidOperationException(
                "The embedded test extension is not initialized.")).RequestSystem(text);

    }

    private sealed class EmbeddedTestExtension : ILocalPlayableServerExtension
    {
        private const int MaximumParticipants = 3;
        private readonly SharedPlayableAuthority _authority;
        private readonly RemoteChatDeliveryService _chatDelivery;
        private readonly RemoteChatLocalRegistration _localChat;
        private readonly GamePublicActorPresentation[] _presentations =
            new GamePublicActorPresentation[MaximumParticipants];
        private readonly SharedPlayableParticipant _localParticipant;
        private readonly System.Collections.Concurrent.ConcurrentQueue<string> _systemRequests = new();
        private readonly NetworkServerIdentity _identity = NetworkServerIdentity.CreateEphemeral();
        private readonly ApplicationRealmId _realm = new(0x4D00, 2);
        private HostedUdpAdmissionHost? _host;
        private bool _disposed;

        internal EmbeddedTestExtension(LocalPlayableServerContext context)
        {
            var delivery = new RemoteChatDeliveryService(
                context.Runtime.Game,
                context.Chat,
                new RemoteChatServerLimits(
                    MaximumParticipants,
                    MaximumParticipants * 64,
                    64,
                    8,
                    2_000,
                    1_000));
            try
            {
                var local = new SharedPlayableParticipant(
                    context.LocalParticipant,
                    context.LocalGuestIdentity,
                    context.LocalControlSession,
                    context.LocalEntities,
                    context.LocalPublicEntity,
                    0);
                _authority = SharedPlayableAuthority.AttachExisting(
                    context.WorldPack,
                    context.Composition,
                    context.Runtime,
                    context.Admission,
                    context.Chat,
                    delivery,
                    context.Scene,
                    local,
                    context.TickTimings,
                    context.GenerationStartedTimestamp,
                    new SharedPlayableAuthorityLimits(MaximumParticipants));
                _localParticipant = local;
                _chatDelivery = delivery;
                _localChat = delivery.RegisterLocal(
                    context.LocalParticipant,
                    new PacketFreeChatRecipient(context.LocalChat));
            }
            catch
            {
                delivery.Dispose();
                _identity.Dispose();
                throw;
            }
        }

        public bool OwnsSharedChatHandoff => true;

        public bool TryCaptureServerMonitor(out ServerMonitorSnapshot? snapshot)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            snapshot = _authority.CaptureServerMonitor();
            return true;
        }

        internal bool RequestSystem(string text)
        {
            if (_disposed || string.IsNullOrWhiteSpace(text)) return false;
            _systemRequests.Enqueue(text);
            return true;
        }

        public void Pump()
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            while (_systemRequests.TryDequeue(out string? text))
            {
                if (!_authority.PublishSystem(text).Accepted)
                    throw new InvalidOperationException(
                        "The embedded authority rejected its bounded test system broadcast.");
            }
            _host?.Pump();
            _ = _chatDelivery.TryHandoffPendingOccurrences();
        }

        public bool TrySetHostingEnabled(bool enabled, out string? detail)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (enabled == (_host is not null))
            {
                detail = enabled ? "Already hosting." : "Already stopped.";
                return true;
            }
            if (!enabled)
            {
                _host!.Dispose();
                _host = null;
                detail = "Stopped without replacing the borrowed Game world.";
                return true;
            }
            _host = new HostedUdpAdmissionHost(
                new IPEndPoint(IPAddress.Loopback, 0),
                _identity,
                NetworkProtocolLimits.Default,
                UdpProtocolLimits.Default,
                SharedUdpProtocolLimits.Default with
                {
                    MaximumSessions = MaximumParticipants - 1,
                },
                HostedUdpAdmissionLimits.Default with
                {
                    MaximumChallengesPerAddressPerBucket = 64,
                    MaximumValidationsPerAddressPerBucket = 64,
                },
                new SharedRemoteGuestPlayableApplicationFactory(
                    _authority, _realm, firstParticipantId: 1),
                UltraSimGameProtocol.Manifest);
            detail = "Hosting the borrowed local authority.";
            return true;
        }

        public LocalPlayableServerExtensionSnapshot GetSnapshot()
        {
            SharedPlayableAuthoritySnapshot authority = _authority.GetSnapshot();
            Span<RemoteGuestPlayableServerSnapshot> remote = stackalloc RemoteGuestPlayableServerSnapshot[2];
            int remoteCount = _authority.CopyRemoteSessionSnapshots(remote);
            string detail = _host is null
                ? "Test host stopped."
                : remoteCount > 0
                    ? $"Test host active; remoteWorld={remote[0].World}."
                    : "Test host active; no remote application.";
            return new LocalPlayableServerExtensionSnapshot(
                true,
                _host is not null,
                _host?.LocalEndPoint.ToString(),
                _realm.ToString(),
                ClientDeploymentKeyFingerprint.Compute(_identity.PublicKey).ToString(),
                Math.Max(0, authority.ParticipantCount - 1),
                authority.GenerationId,
                detail);
        }

        public int CopyPublicPresentationMetadata(
            Span<ClientPublicPresentationMetadata> destination)
        {
            int count = _authority.CopyActorPresentations(_presentations);
            if (count < 0 || destination.Length < count) return -1;
            ulong generation = _authority.GetSnapshot().GenerationId;
            for (int index = 0; index < count; index++)
            {
                GamePublicActorPresentation presentation = _presentations[index];
                SharedPlayableParticipant participant = presentation.Participant;
                destination[index] = new ClientPublicPresentationMetadata(
                    generation,
                    participant.PublicEntity,
                    presentation.Revision,
                    ClientPublicPresentationFlags.NameplateEligible |
                        ClientPublicPresentationFlags.ActorState,
                    participant.GuestIdentity.DisplayName!,
                    MainHandPrefabId: presentation.MainHandPrefabId,
                    VerticalPhase:
                        (ClientPublicActorVerticalPhase)presentation.VerticalPhase,
                    ActorAction: (ClientPublicActorActionKind)presentation.Action,
                    ActorActionSequence: presentation.ActionSequence);
            }
            return count;
        }

        public void ObserveLocalPresentationAction(
            ulong commandSequence,
            LocalPublicActorActionKind action)
        {
            GamePublicActorActionKind shared = action switch
            {
                LocalPublicActorActionKind.Interact => GamePublicActorActionKind.Interact,
                LocalPublicActorActionKind.Primary => GamePublicActorActionKind.Primary,
                _ => throw new ArgumentOutOfRangeException(nameof(action)),
            };
            True(_authority.RecordPresentationAction(
                in _localParticipant, commandSequence, shared));
        }

        public void Dispose()
        {
            if (_disposed) return;
            _host?.Dispose();
            _host = null;
            _localChat.Dispose();
            _authority.Dispose();
            _identity.Dispose();
            _disposed = true;
        }

        private sealed class PacketFreeChatRecipient : IRemoteChatLocalRecipient
        {
            private readonly ILocalPlayableChatSink _sink;
            internal PacketFreeChatRecipient(ILocalPlayableChatSink sink) => _sink = sink;
            public bool CanPublish => _sink.CanPublish;
            public bool TryPublish(in UltraSim.Game.Chat.GameSpeechFact fact) =>
                _sink.TryPublish(in fact);
        }
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
