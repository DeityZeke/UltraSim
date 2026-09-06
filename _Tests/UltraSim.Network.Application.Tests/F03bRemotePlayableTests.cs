using System.Net;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Input;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.UI;

internal static class F03bRemotePlayableTests
{
    internal const int TestCount = 4;

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
        private bool _disposed;

        private RemotePlayableFixture(
            string trustDirectory,
            NetworkServerIdentity identity,
            HostedUdpAdmissionHost host,
            ClientDirectConnectSession direct,
            RemotePlayableSession session,
            ClientWorldSemanticCache cache)
        {
            _trustDirectory = trustDirectory;
            _identity = identity;
            _host = host;
            _direct = direct;
            _session = session;
            Cache = cache;
        }

        internal RemotePlayableClient Playable => _session.Playable;
        internal RemoteChunkWorldStateClient World => _session.World;
        internal ClientChatState ChatState => _session.ChatState;
        internal RemoteChatClient Chat => _session.Chat;
        internal UiClientRuntime UiClient => _session.UiClient;
        internal ClientWorldSemanticCache Cache { get; }
        internal RemotePlayableSessionSnapshot SessionSnapshot => _session.GetSnapshot();

        internal static RemotePlayableFixture Create()
        {
            string trustDirectory = Path.Combine(Path.GetTempPath(),
                $"ultrasim-f03b-{Environment.ProcessId}-{Guid.NewGuid():N}");
            Directory.CreateDirectory(trustDirectory);
            var identity = NetworkServerIdentity.CreateEphemeral();
            try
            {
                var realm = new UltraSim.Game.Application.ApplicationRealmId(0xF03B, 1);
                var host = new HostedUdpAdmissionHost(
                    new IPEndPoint(IPAddress.Loopback, 0),
                    identity,
                    Protocol,
                    Udp,
                    Shared,
                    Admission,
                    Owners,
                    new RemoteGuestPlayableApplicationFactory(realm),
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
                        ClientWorldSemanticCache cache = CreatePreinstalledWorldCache();
                        var session = new RemotePlayableSession(
                            peer,
                            connected.ApplicationSessionEpoch,
                            cache,
                            new NullRenderer(),
                            new ClientChatLimits(16, 256, 128),
                            new UiClientLimits(8, 8),
                            maximumPendingWorldProofs: 256);
                        return new RemotePlayableFixture(
                            trustDirectory, identity, host, direct, session, cache);
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
            True(Playable.TrySubmitGuest(new GameGuestAdmissionRequest(body, name)));
            DriveUntil(() => Playable.GetSnapshot().State == RemotePlayableClientState.Active);
        }

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
                $"playable={Playable.GetSnapshot()} world={World.GetSnapshot()} " +
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
        }

        private void Pump()
        {
            _direct.Pump();
            _host.Pump();
            _direct.Pump();
            _ = _session.ProcessReceived();
        }

        private static ClientWorldSemanticCache CreatePreinstalledWorldCache()
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
