using System.Net;
using UltraSim.Client.Core;
using UltraSim.Client.Network;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Network;

internal static class F03aDirectConnectTests
{
    internal const int TestCount = 8;

    private static readonly NetworkProtocolLimits Protocol = NetworkProtocolLimits.Default;
    private static readonly UdpProtocolLimits Udp = new(
        MaximumDatagramBytes: 1_472,
        SocketSendBufferBytes: 64 * 1024,
        SocketReceiveBufferBytes: 64 * 1024,
        MaximumHandshakePumps: 4_096,
        CookieBucketMilliseconds: 1_000,
        IdleTimeoutMilliseconds: 30_000,
        DrainTimeoutMilliseconds: 1_000,
        KeepAliveIntervalMilliseconds: 5_000);
    private static readonly SharedUdpProtocolLimits Shared = new(
        MaximumSessions: 4,
        MaximumQueuedDatagramsPerSession: 16,
        MaximumInboundDatagramsPerPump: 256,
        SocketIoMode: SharedUdpSocketIoMode.Scalar,
        AggregateSocketSendBufferBytes: 128 * 1024,
        AggregateSocketReceiveBufferBytes: 128 * 1024);
    private static readonly HostedUdpAdmissionLimits Admission = new(
        MaximumValidatedCandidates: 4,
        MaximumTrackedSourceAddresses: 16,
        MaximumChallengesPerAddressPerBucket: 64,
        MaximumValidationsPerAddressPerBucket: 64,
        ValidatedCandidateTimeoutMilliseconds: 5_000);
    private static readonly HostedUdpOwnerShardingLimits Owners = new(
        MaximumOwners: 1,
        MaximumConnections: 4,
        MaximumPacketSlots: 64,
        MaximumQueuedPacketsPerOwner: 32,
        MaximumDatagramBytes: 1_472);

    internal static void RunAll()
    {
        Run("DirectConnectFirstTrustPersistsOnlyAfterAuthenticatedRealm",
            DirectConnectFirstTrustPersistsOnlyAfterAuthenticatedRealm);
        Run("DirectConnectReconnectUsesPinAndFreshSessionEpoch",
            DirectConnectReconnectUsesPinAndFreshSessionEpoch);
        Run("DirectConnectMismatchBlocksWithoutReplacingPin",
            DirectConnectMismatchBlocksWithoutReplacingPin);
        Run("DirectConnectExplicitFingerprintSkipsTofuPrompt",
            DirectConnectExplicitFingerprintSkipsTofuPrompt);
        Run("DirectConnectTrustFileFailsClosedAndRealmScopesSettings",
            DirectConnectTrustFileFailsClosedAndRealmScopesSettings);
        Run("DirectConnectTrustPersistenceFailureRollsBackMemory",
            DirectConnectTrustPersistenceFailureRollsBackMemory);
        Run("DirectConnectEndpointUpdatePersistenceFailureRestoresPriorLocation",
            DirectConnectEndpointUpdatePersistenceFailureRestoresPriorLocation);
        Run("DirectConnectObservesAutomaticTransportClosureAndReconnects",
            DirectConnectObservesAutomaticTransportClosureAndReconnects);
    }

    private static void DirectConnectFirstTrustPersistsOnlyAfterAuthenticatedRealm()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var realm = new ApplicationRealmId(0xF03A, 1);
        using HostedUdpAdmissionHost host = CreateHost(identity, realm);
        using TemporaryTrust trust = TemporaryTrust.Create();
        using var client = new ClientDirectConnectSession(
            trust.Registry, trust.Store, Protocol, Udp);
        True(client.TryStart(host.LocalEndPoint.ToString()));
        PumpUntil(host, client, ClientDirectConnectState.AwaitingTrust);
        Equal(0, trust.Registry.Count);
        False(File.Exists(trust.Store.Path));
        ClientDirectConnectSnapshot candidate = client.GetSnapshot();
        True(candidate.CandidateFingerprint.IsValid);
        False(candidate.PreviouslyTrusted);
        True(client.TryAcceptCandidate());
        PumpUntil(host, client, ClientDirectConnectState.Established);
        ClientDirectConnectSnapshot established = client.GetSnapshot();
        Equal(realm, established.Realm);
        True(established.ApplicationSessionEpoch != 0);
        True(established.Connection.IsValid);
        Equal($"realm-{realm}", established.SettingsScope.ToString());
        Equal(1, trust.Registry.Count);
        True(File.Exists(trust.Store.Path));
        HostedUdpAdmissionSnapshot server = host.GetSnapshot();
        True(server.DeploymentDiscoveryRequests != 0);
        True(server.DeploymentAnnouncementsSent != 0);
        True(client.TryGetTrafficSnapshot(out HostedUdpConnectionTrafficSnapshot liveTraffic));
        client.BeginDisconnect();
        PumpUntil(host, client, ClientDirectConnectState.Closed);
        True(client.TryGetTrafficSnapshot(out HostedUdpConnectionTrafficSnapshot finalTraffic));
        Equal(liveTraffic.Connection, finalTraffic.Connection);
        True(finalTraffic.UdpPayload.PayloadBytesSent >= liveTraffic.UdpPayload.PayloadBytesSent);
        True(finalTraffic.UdpPayload.PayloadBytesReceived >= liveTraffic.UdpPayload.PayloadBytesReceived);
    }

    private static void DirectConnectReconnectUsesPinAndFreshSessionEpoch()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var realm = new ApplicationRealmId(0xF03A, 2);
        using HostedUdpAdmissionHost host = CreateHost(identity, realm);
        using TemporaryTrust trust = TemporaryTrust.Create();
        ulong firstEpoch;
        using (var first = new ClientDirectConnectSession(
                   trust.Registry, trust.Store, Protocol, Udp))
        {
            True(first.TryStart(host.LocalEndPoint.ToString()));
            PumpUntil(host, first, ClientDirectConnectState.AwaitingTrust);
            True(first.TryAcceptCandidate());
            PumpUntil(host, first, ClientDirectConnectState.Established);
            firstEpoch = first.GetSnapshot().ApplicationSessionEpoch;
            first.BeginDisconnect();
            PumpUntil(host, first, ClientDirectConnectState.Closed);
        }
        using var second = new ClientDirectConnectSession(
            trust.Registry, trust.Store, Protocol, Udp);
        True(second.TryStart(host.LocalEndPoint.ToString()));
        PumpUntil(host, second, ClientDirectConnectState.Established);
        ClientDirectConnectSnapshot snapshot = second.GetSnapshot();
        True(snapshot.PreviouslyTrusted);
        True(snapshot.ApplicationSessionEpoch != firstEpoch);
        second.BeginDisconnect();
        PumpUntil(host, second, ClientDirectConnectState.Closed);
    }

    private static void DirectConnectMismatchBlocksWithoutReplacingPin()
    {
        using TemporaryTrust trust = TemporaryTrust.Create();
        var realm = new ApplicationRealmId(0xF03A, 3);
        int port;
        ClientDeploymentKeyFingerprint firstFingerprint;
        using (NetworkServerIdentity firstIdentity = NetworkServerIdentity.CreateEphemeral())
        {
            using HostedUdpAdmissionHost firstHost = CreateHost(firstIdentity, realm);
            port = firstHost.LocalEndPoint.Port;
            using var first = new ClientDirectConnectSession(
                trust.Registry, trust.Store, Protocol, Udp);
            True(first.TryStart(firstHost.LocalEndPoint.ToString()));
            PumpUntil(firstHost, first, ClientDirectConnectState.AwaitingTrust);
            firstFingerprint = first.GetSnapshot().CandidateFingerprint;
            True(first.TryAcceptCandidate());
            PumpUntil(firstHost, first, ClientDirectConnectState.Established);
        }

        using NetworkServerIdentity replacementIdentity = NetworkServerIdentity.CreateEphemeral();
        using HostedUdpAdmissionHost replacementHost = CreateHost(
            replacementIdentity, realm, new IPEndPoint(IPAddress.Loopback, port));
        using var replacement = new ClientDirectConnectSession(
            trust.Registry, trust.Store, Protocol, Udp);
        True(replacement.TryStart(replacementHost.LocalEndPoint.ToString()));
        PumpUntil(replacementHost, replacement, ClientDirectConnectState.Failed);
        ClientDirectConnectSnapshot failed = replacement.GetSnapshot();
        Equal(ClientDirectConnectFailure.TrustedKeyMismatch, failed.Failure);
        True(failed.CandidateFingerprint != firstFingerprint);
        True(trust.Registry.TryFindByEndpoint(
            replacementHost.LocalEndPoint.ToString(), out ClientTrustedDeployment? retained));
        Equal(firstFingerprint, retained!.Fingerprint);
        True(replacement.TryForgetMismatchedTrust());
        Equal(0, trust.Registry.Count);
    }

    private static void DirectConnectExplicitFingerprintSkipsTofuPrompt()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var realm = new ApplicationRealmId(0xF03A, 4);
        using HostedUdpAdmissionHost host = CreateHost(identity, realm);
        using TemporaryTrust trust = TemporaryTrust.Create();
        ClientDeploymentKeyFingerprint fingerprint =
            ClientDeploymentKeyFingerprint.Compute(identity.PublicKey);
        using var client = new ClientDirectConnectSession(
            trust.Registry, trust.Store, Protocol, Udp);
        True(client.TryStart(host.LocalEndPoint.ToString(),
            GroupFingerprint(fingerprint.ToString())));
        PumpUntil(host, client, ClientDirectConnectState.Established);
        ClientDirectConnectSnapshot snapshot = client.GetSnapshot();
        True(snapshot.ExplicitFingerprintSupplied);
        Equal(fingerprint, snapshot.CandidateFingerprint);
        Equal(realm, snapshot.Realm);
    }

    private static void DirectConnectTrustFileFailsClosedAndRealmScopesSettings()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using TemporaryTrust trust = TemporaryTrust.Create();
        var realm = new ApplicationRealmId(0xF03A, 5);
        Equal(ClientServerTrustCommitStatus.Added,
            trust.Registry.TryCommitAuthenticated(
                realm, "127.0.0.1:7777", identity.PublicKey));
        trust.Store.Save(trust.Registry);
        ClientServerTrustLoadResult loaded = trust.Store.Load();
        Equal(ClientServerTrustLoadStatus.Loaded, loaded.Status);
        True(loaded.Registry.TryFindByRealm(realm, out ClientTrustedDeployment? record));
        Equal(ClientDeploymentKeyFingerprint.Compute(identity.PublicKey), record!.Fingerprint);
        string canonical = File.ReadAllText(trust.Store.Path);
        File.WriteAllText(trust.Store.Path,
            canonical.Replace(realm.ToString(), realm.ToString().ToUpperInvariant(),
                StringComparison.Ordinal));
        ClientServerTrustLoadResult nonCanonical = trust.Store.Load();
        Equal(ClientServerTrustLoadStatus.Invalid, nonCanonical.Status);
        Equal(0, nonCanonical.Registry.Count);
        File.WriteAllText(trust.Store.Path, "{\"version\":1,\"deployments\":[{\"realm\":\"0\"}]}");
        ClientServerTrustLoadResult invalid = trust.Store.Load();
        Equal(ClientServerTrustLoadStatus.Invalid, invalid.Status);
        Equal(0, invalid.Registry.Count);
    }

    private static void DirectConnectTrustPersistenceFailureRollsBackMemory()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var realm = new ApplicationRealmId(0xF03A, 6);
        using HostedUdpAdmissionHost host = CreateHost(identity, realm);
        string directory = Path.Combine(Path.GetTempPath(),
            $"ultrasim-f03a-fail-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var registry = new ClientServerTrustRegistry();
            var unwritableStore = new ClientServerTrustFileStore(directory);
            using var client = new ClientDirectConnectSession(
                registry, unwritableStore, Protocol, Udp);
            True(client.TryStart(host.LocalEndPoint.ToString()));
            PumpUntil(host, client, ClientDirectConnectState.AwaitingTrust);
            True(client.TryAcceptCandidate());
            PumpUntil(host, client, ClientDirectConnectState.Failed);
            Equal(ClientDirectConnectFailure.TrustPersistenceFailed,
                client.GetSnapshot().Failure);
            Equal(0, registry.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static void DirectConnectEndpointUpdatePersistenceFailureRestoresPriorLocation()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var realm = new ApplicationRealmId(0xF03A, 7);
        using TemporaryTrust trust = TemporaryTrust.Create();
        string priorEndpoint;
        using (HostedUdpAdmissionHost priorHost = CreateHost(identity, realm))
        using (var prior = new ClientDirectConnectSession(
                   trust.Registry, trust.Store, Protocol, Udp))
        {
            priorEndpoint = priorHost.LocalEndPoint.ToString();
            True(prior.TryStart(priorEndpoint));
            PumpUntil(priorHost, prior, ClientDirectConnectState.AwaitingTrust);
            True(prior.TryAcceptCandidate());
            PumpUntil(priorHost, prior, ClientDirectConnectState.Established);
        }

        string blockedPath = Path.Combine(Path.GetTempPath(),
            $"ultrasim-f03a-update-fail-{Environment.ProcessId}-{Guid.NewGuid():N}");
        Directory.CreateDirectory(blockedPath);
        try
        {
            using HostedUdpAdmissionHost movedHost = CreateHost(identity, realm);
            string movedEndpoint = movedHost.LocalEndPoint.ToString();
            var unwritableStore = new ClientServerTrustFileStore(blockedPath);
            using var moved = new ClientDirectConnectSession(
                trust.Registry, unwritableStore, Protocol, Udp);
            True(moved.TryStart(movedEndpoint));
            PumpUntil(movedHost, moved, ClientDirectConnectState.AwaitingTrust);
            True(moved.TryAcceptCandidate());
            PumpUntil(movedHost, moved, ClientDirectConnectState.Failed);
            Equal(ClientDirectConnectFailure.TrustPersistenceFailed,
                moved.GetSnapshot().Failure);
            True(trust.Registry.TryFindByEndpoint(
                priorEndpoint, out ClientTrustedDeployment? retained));
            Equal(realm, retained!.Realm);
            False(trust.Registry.TryFindByEndpoint(movedEndpoint, out _));
        }
        finally
        {
            Directory.Delete(blockedPath, recursive: true);
        }
    }

    private static void DirectConnectObservesAutomaticTransportClosureAndReconnects()
    {
        UdpProtocolLimits shortUdp = Udp with
        {
            IdleTimeoutMilliseconds = 120,
            DrainTimeoutMilliseconds = 20,
            KeepAliveIntervalMilliseconds = 10,
        };
        HostedUdpAdmissionLimits shortAdmission = Admission with
        {
            ValidatedCandidateTimeoutMilliseconds = 60,
        };
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var realm = new ApplicationRealmId(0xF03A, 8);
        using TemporaryTrust trust = TemporaryTrust.Create();
        using var client = new ClientDirectConnectSession(
            trust.Registry, trust.Store, Protocol, shortUdp);
        int port;
        using (HostedUdpAdmissionHost first = CreateHost(
                   identity, realm, udp: shortUdp, admission: shortAdmission))
        {
            port = first.LocalEndPoint.Port;
            True(client.TryStart(first.LocalEndPoint.ToString()));
            PumpUntil(first, client, ClientDirectConnectState.AwaitingTrust);
            True(client.TryAcceptCandidate());
            PumpUntil(first, client, ClientDirectConnectState.Established);
            True(client.Peer is not null);
        }

        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (client.State != ClientDirectConnectState.Closed &&
               DateTime.UtcNow < deadline)
        {
            client.Pump();
            Thread.Sleep(1);
        }
        Equal(ClientDirectConnectState.Closed, client.State);
        Equal(ClientDirectConnectFailure.TransportClosed, client.GetSnapshot().Failure);
        True(client.Peer is null);

        using HostedUdpAdmissionHost replacement = CreateHost(
            identity, realm, new IPEndPoint(IPAddress.Loopback, port),
            shortUdp, shortAdmission);
        True(client.TryStart(replacement.LocalEndPoint.ToString()));
        PumpUntil(replacement, client, ClientDirectConnectState.Established);
        True(client.Peer is not null);
    }

    private static HostedUdpAdmissionHost CreateHost(
        NetworkServerIdentity identity,
        ApplicationRealmId realm,
        IPEndPoint? endpoint = null,
        UdpProtocolLimits? udp = null,
        HostedUdpAdmissionLimits? admission = null) => new(
            endpoint ?? new IPEndPoint(IPAddress.Loopback, 0),
            identity,
            Protocol,
            udp ?? Udp,
            Shared,
            admission ?? Admission,
            Owners,
            new RemoteApplicationSessionIdentityFactory(realm),
            UltraSimGameProtocol.Manifest);

    private static void PumpUntil(
        HostedUdpAdmissionHost host,
        ClientDirectConnectSession client,
        ClientDirectConnectState expected)
    {
        for (int iteration = 0; iteration < 40_000; iteration++)
        {
            client.Pump();
            host.Pump();
            if (client.State == expected) return;
            if (client.State == ClientDirectConnectState.Failed &&
                expected != ClientDirectConnectState.Failed)
                throw new InvalidOperationException(
                    $"Direct Connect failed: {client.GetSnapshot().Failure} {client.GetSnapshot().Detail}");
            Thread.Yield();
        }
        throw new InvalidOperationException(
            $"Direct Connect did not reach {expected}; current={client.State}.");
    }

    private static string GroupFingerprint(string fingerprint) => string.Join(':',
        Enumerable.Range(0, fingerprint.Length / 2)
            .Select(index => fingerprint.Substring(index * 2, 2)));

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

    private sealed class TemporaryTrust : IDisposable
    {
        private TemporaryTrust(string directory)
        {
            Directory = directory;
            Registry = new ClientServerTrustRegistry();
            Store = new ClientServerTrustFileStore(Path.Combine(directory, "servers.json"));
        }

        private string Directory { get; }
        internal ClientServerTrustRegistry Registry { get; }
        internal ClientServerTrustFileStore Store { get; }

        internal static TemporaryTrust Create()
        {
            string directory = Path.Combine(Path.GetTempPath(),
                $"ultrasim-f03a-{Environment.ProcessId}-{Guid.NewGuid():N}");
            System.IO.Directory.CreateDirectory(directory);
            return new TemporaryTrust(directory);
        }

        public void Dispose()
        {
            if (System.IO.Directory.Exists(Directory))
                System.IO.Directory.Delete(Directory, recursive: true);
        }
    }
}
