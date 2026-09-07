using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Game.Admission;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Network;

internal static class F03cClientProfileTests
{
    internal const int TestCount = 3;

    internal static void RunAll()
    {
        Run("ClientProfileProtocolIsCanonicalBoundedAndCodeFree",
            ClientProfileProtocolIsCanonicalBoundedAndCodeFree);
        Run("AuthenticatedProfileDecisionGatesApplicationAndPreservesReservedControls",
            AuthenticatedProfileDecisionGatesApplicationAndPreservesReservedControls);
        Run("ExactRealmProfileReconnectSkipsPromptButChangedProfilePrompts",
            ExactRealmProfileReconnectSkipsPromptButChangedProfilePrompts);
    }

    private static void ClientProfileProtocolIsCanonicalBoundedAndCodeFree()
    {
        RemoteClientProfileAdvertisement profile = Profile(7, orbit: true);
        var bytes = new byte[UltraSimGameProtocol.MaximumClientProfileMessageBytes + 1];
        True(UltraSimGameProtocol.TryWriteClientProfile(bytes, 91, profile, out int written));
        True(UltraSimGameProtocol.TryReadClientProfile(
            bytes.AsSpan(0, written), out ulong epoch,
            out RemoteClientProfileAdvertisement? decoded));
        Equal(91UL, epoch);
        Equal(profile.Revision, decoded!.Revision);
        Equal(profile.Hash, decoded.Hash);
        Equal(RemoteClientControlGrammar.OrbitCommand, decoded.ControlGrammar);
        Equal(RemoteClientCameraProfile.Orbit, decoded.CameraProfile);
        Equal(2, decoded.Bindings.Count);
        Equal(RemoteClientSemanticAction.Primary, decoded.Bindings[0].Action);
        bytes[written] = 0;
        False(UltraSimGameProtocol.TryReadClientProfile(
            bytes.AsSpan(0, written + 1), out _, out _));
        Equal(NetworkLane.Control, RemoteClientProfileHeaderCodec.Descriptor.Lane);
        Equal(NetworkLane.Control, RemoteClientProfileAppliedMessageCodec.Descriptor.Lane);
        Equal((ushort)20, UltraSimGameProtocol.Manifest.Version);

        string fields = string.Join(',', typeof(RemoteClientProfileHeader).GetFields()
            .Select(static value => value.Name).OrderBy(static value => value, StringComparer.Ordinal));
        False(fields.Contains("Code", StringComparison.Ordinal));
        False(fields.Contains("Assembly", StringComparison.Ordinal));
        False(fields.Contains("Script", StringComparison.Ordinal));
        var duplicate = new[]
        {
            Binding(RemoteClientSemanticAction.Interact, "E"),
            Binding(RemoteClientSemanticAction.Interact, "F"),
        };
        False(RemoteClientProfileAdvertisement.TryCreate(
            1, RemoteClientControlGrammar.ThirdPersonDirect,
            RemoteClientCameraProfile.ThirdPersonFollow, 8_500, 700,
            duplicate, out _));

        True(RemoteClientProfileAdvertisement.TryCreate(
            2, RemoteClientControlGrammar.ThirdPersonDirect,
            RemoteClientCameraProfile.ThirdPersonFollow, 8_500, 700,
            [
                Binding(RemoteClientSemanticAction.MoveLeft, "A"),
                Binding(RemoteClientSemanticAction.MoveRight, "D"),
            ],
            out RemoteClientProfileAdvertisement? ordered));
        True(UltraSimGameProtocol.TryWriteClientProfile(bytes, 92, ordered!, out written));
        int firstBinding = RemoteClientProfileHeaderCodec.EncodedSize;
        const int oneCharacterBindingBytes = 5;
        Span<byte> first = stackalloc byte[oneCharacterBindingBytes];
        bytes.AsSpan(firstBinding, oneCharacterBindingBytes).CopyTo(first);
        bytes.AsSpan(firstBinding + oneCharacterBindingBytes, oneCharacterBindingBytes)
            .CopyTo(bytes.AsSpan(firstBinding, oneCharacterBindingBytes));
        first.CopyTo(bytes.AsSpan(firstBinding + oneCharacterBindingBytes, oneCharacterBindingBytes));
        False(UltraSimGameProtocol.TryReadClientProfile(
            bytes.AsSpan(0, written), out _, out _));
    }

    private static void AuthenticatedProfileDecisionGatesApplicationAndPreservesReservedControls()
    {
        string root = TemporaryRoot();
        try
        {
            var realm = new ApplicationRealmId(0xF03C, 1);
            RemoteClientProfileAdvertisement profile = Profile(1, orbit: false);
            var store = new ClientProfileDecisionFileStore(Path.Combine(root, "profiles.json"));
            using ProfileFixture fixture = ProfileFixture.Create(
                realm, profile, store, ClientProfileDecisionDocument.Empty);
            fixture.DriveUntil(RemoteClientProfileState.AwaitingDecision);
            False(fixture.Server.GetSnapshot().ClientProfileApplied);

            // A stale proof cannot unlock the server's profile gate.
            RemoteClientProfileAppliedMessage stale = UltraSimGameProtocol.CreateClientProfileApplied(
                fixture.Epoch + 1, profile, RemoteClientProfileApplyStatus.Applied);
            True(fixture.Loop.Client.TryQueue(
                RemoteClientProfileAppliedMessageCodec.Descriptor.Id,
                Encode(stale)) == NetworkQueueStatus.Accepted);
            fixture.DriveSteps(64);
            False(fixture.Server.GetSnapshot().ClientProfileApplied);
            True(fixture.Server.GetSnapshot().RejectedClientProfileProofs != 0);

            True(fixture.Client.TryChoose(ClientProfileApplicationMode.UseServerDefaults));
            fixture.DriveUntilServerApplied();
            True(File.Exists(store.Path));
            RemoteClientProfileSnapshot applied = fixture.Client.GetSnapshot();
            Equal(ClientProfileApplicationMode.UseServerDefaults, applied.Mode);
            False(applied.RememberedDecision);

            ClientSettingsScopeKey.TryCreate($"realm-{realm}", out ClientSettingsScopeKey scope);
            ClientPhysicalInput.TryCreate(
                ClientPhysicalInputKind.Keyboard, "R", ClientInputModifiers.None,
                out ClientPhysicalInput scopedInteract);
            var settings = new ClientSettingsDocument().WithScope(scope, new ClientSettingsLayer(
                cameraProfile: ClientCameraProfile.IndependentMoveLook,
                bindings: [new ClientPhysicalBinding(
                    ClientSemanticAction.Interact, scopedInteract)]));
            ResolvedClientSettings resolved = ClientSettingsResolver.Resolve(
                settings, scope, fixture.Client.Recommendation, applied.Mode);
            Equal(ClientControlGrammar.ThirdPersonDirect, resolved.ControlGrammar);
            Equal(ClientCameraProfile.IndependentMoveLook, resolved.CameraProfile);
            True(resolved.TryGetBinding(ClientSemanticAction.Interact, out ClientPhysicalInput interact));
            Equal("R", interact.Code);
            True(resolved.TryGetBinding(ClientSemanticAction.OpenSettings, out ClientPhysicalInput reserved));
            Equal("F10", reserved.Code);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static void ExactRealmProfileReconnectSkipsPromptButChangedProfilePrompts()
    {
        string root = TemporaryRoot();
        try
        {
            var realm = new ApplicationRealmId(0xF03C, 2);
            var store = new ClientProfileDecisionFileStore(Path.Combine(root, "profiles.json"));
            RemoteClientProfileAdvertisement firstProfile = Profile(1, orbit: true);
            using (ProfileFixture first = ProfileFixture.Create(
                       realm, firstProfile, store, ClientProfileDecisionDocument.Empty))
            {
                first.DriveUntil(RemoteClientProfileState.AwaitingDecision);
                True(first.Client.TryChoose(ClientProfileApplicationMode.KeepMyControls));
                first.DriveUntilServerApplied();
            }

            ClientProfileDecisionDocument decisions = store.Load().Document;
            using (ProfileFixture same = ProfileFixture.Create(realm, firstProfile, store, decisions))
            {
                same.DriveUntil(RemoteClientProfileState.Applied);
                RemoteClientProfileSnapshot applied = same.Client.GetSnapshot();
                True(applied.RememberedDecision);
                Equal(ClientProfileApplicationMode.KeepMyControls, applied.Mode);
                same.DriveUntilServerApplied();
            }

            using (ProfileFixture changedRevision = ProfileFixture.Create(
                       realm, Profile(2, orbit: true), store, decisions))
                changedRevision.DriveUntil(RemoteClientProfileState.AwaitingDecision);
            using (ProfileFixture changedHash = ProfileFixture.Create(
                       realm, Profile(1, orbit: false), store, decisions))
                changedHash.DriveUntil(RemoteClientProfileState.AwaitingDecision);
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        }
    }

    private static RemoteClientProfileAdvertisement Profile(ulong revision, bool orbit)
    {
        RemoteClientProfileBinding[] bindings =
        [
            Binding(RemoteClientSemanticAction.Primary, "MIDDLE", RemoteClientPhysicalInputKind.MouseButton),
            Binding(RemoteClientSemanticAction.Interact, orbit ? "E" : "F"),
        ];
        True(RemoteClientProfileAdvertisement.TryCreate(
            revision,
            orbit ? RemoteClientControlGrammar.OrbitCommand : RemoteClientControlGrammar.ThirdPersonDirect,
            orbit ? RemoteClientCameraProfile.Orbit : RemoteClientCameraProfile.ThirdPersonFollow,
            orbit ? 14_000u : 8_500u,
            orbit ? (ushort)700 : (ushort)760,
            bindings,
            out RemoteClientProfileAdvertisement? profile));
        return profile!;
    }

    private static RemoteClientProfileBinding Binding(
        RemoteClientSemanticAction action,
        string code,
        RemoteClientPhysicalInputKind kind = RemoteClientPhysicalInputKind.Keyboard) =>
        new(action, kind, RemoteClientInputModifiers.None, code);

    private static byte[] Encode(in RemoteClientProfileAppliedMessage value)
    {
        byte[] bytes = new byte[RemoteClientProfileAppliedMessageCodec.EncodedSize];
        True(RemoteClientProfileAppliedMessageCodec.TryWriteExact(bytes, in value));
        return bytes;
    }

    private static string TemporaryRoot()
    {
        string root = Path.Combine(Path.GetTempPath(),
            $"ultrasim-f03c-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        return root;
    }

    private static void Run(string name, Action body)
    {
        body();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool value) { if (!value) throw new InvalidOperationException("Expected true."); }
    private static void False(bool value) { if (value) throw new InvalidOperationException("Expected false."); }
    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    private sealed class ProfileFixture : IDisposable
    {
        private ProfileFixture(
            ProtocolLoopback loop,
            RemoteGuestPlayableApplication server,
            RemoteClientProfileClient client,
            ulong epoch)
        {
            Loop = loop;
            Server = server;
            Client = client;
            Epoch = epoch;
        }

        internal ProtocolLoopback Loop { get; }
        internal RemoteGuestPlayableApplication Server { get; }
        internal RemoteClientProfileClient Client { get; }
        internal ulong Epoch { get; }

        internal static ProfileFixture Create(
            ApplicationRealmId realm,
            RemoteClientProfileAdvertisement profile,
            ClientProfileDecisionFileStore store,
            ClientProfileDecisionDocument decisions)
        {
            True(ProtocolLoopback.TryCreate(
                NetworkProtocolLimits.Default,
                new DeterministicDatagramLimits(2_048, 1_200),
                default,
                out ProtocolLoopback? loop,
                out _,
                UltraSimGameProtocol.Manifest));
            ulong epoch = unchecked(profile.Revision * 10_000 + realm.Low + 1);
            if (epoch == 0) epoch = 1;
            var server = new RemoteGuestPlayableApplication(
                loop!.Server, realm, new ParticipantId(1), epoch,
                clientProfile: profile);
            for (int step = 0; step < 4_096; step++)
            {
                _ = server.TryProgress();
                loop.Pump();
                if (!loop.Client.TryPeekReceived(
                        out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
                    continue;
                True(message.MessageId == RemoteApplicationSessionIdentityMessageCodec.Descriptor.Id);
                True(RemoteApplicationSessionIdentityMessageCodec.TryReadExact(
                    payload, out RemoteApplicationSessionIdentityMessage identity));
                Equal(epoch, identity.ApplicationSessionEpoch);
                _ = loop.Client.TryConsumeReceived();
                return new ProfileFixture(loop, server,
                    new RemoteClientProfileClient(loop.Client, epoch, realm, decisions, store), epoch);
            }
            server.Dispose();
            loop.Dispose();
            throw new InvalidOperationException("The profile fixture identity did not arrive.");
        }

        internal void DriveUntil(RemoteClientProfileState expected)
        {
            for (int step = 0; step < 20_000; step++)
            {
                DriveOne();
                if (Client.GetSnapshot().State == expected) return;
                if (Client.GetSnapshot().State == RemoteClientProfileState.Faulted)
                    throw new InvalidOperationException(Client.GetSnapshot().Detail);
            }
            throw new InvalidOperationException($"The profile client did not reach {expected}.");
        }

        internal void DriveUntilServerApplied()
        {
            for (int step = 0; step < 20_000; step++)
            {
                DriveOne();
                if (Server.GetSnapshot().ClientProfileApplied) return;
            }
            throw new InvalidOperationException("The server did not accept the exact profile proof.");
        }

        internal void DriveSteps(int count)
        {
            for (int step = 0; step < count; step++) DriveOne();
        }

        private void DriveOne()
        {
            _ = Server.TryProgress();
            _ = Client.ProcessReceived();
            while (Loop.Server.TryPeekReceived(
                       out NetworkDeliveredMessage message, out ReadOnlySpan<byte> payload))
            {
                try { _ = Server.TryProcess(in message, payload); }
                finally { _ = Loop.Server.TryConsumeReceived(); }
            }
            _ = Client.TryProgress();
            Loop.Pump();
        }

        public void Dispose()
        {
            Server.Dispose();
            Loop.Dispose();
        }
    }
}
