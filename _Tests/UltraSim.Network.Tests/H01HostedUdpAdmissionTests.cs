using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using UltraSim.Network;

internal static class H01HostedUdpAdmissionTests
{
    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        1,
        [new NetworkMessageDescriptor(new NetworkMessageId(1),
            "ultrasim.test.hosted-control", 1, NetworkLane.Control, 32, 0xA011)]);
    private static readonly UdpProtocolLimits s_udp = new(
        MaximumDatagramBytes: 512,
        SocketSendBufferBytes: 32 * 1024,
        SocketReceiveBufferBytes: 32 * 1024,
        MaximumHandshakePumps: 1_024,
        CookieBucketMilliseconds: 1_000,
        IdleTimeoutMilliseconds: 10_000,
        DrainTimeoutMilliseconds: 1_000,
        KeepAliveIntervalMilliseconds: 1_000);
    private static readonly SharedUdpProtocolLimits s_shared = new(
        MaximumSessions: 4,
        MaximumQueuedDatagramsPerSession: 4,
        MaximumInboundDatagramsPerPump: 256,
        SocketIoMode: SharedUdpSocketIoMode.Scalar,
        AggregateSocketSendBufferBytes: 64 * 1024,
        AggregateSocketReceiveBufferBytes: 64 * 1024);

    internal static void RunAll()
    {
        Run("HostedUdpCookieValidationAllocatesNoConnectionState", HostedUdpCookieValidationAllocatesNoConnectionState);
        Run("HostedUdpAdmissionRateCapacityAndAmplificationAreBounded", HostedUdpAdmissionRateCapacityAndAmplificationAreBounded);
        Run("HostedUdpAdmissionAcceptsPreviousBucketAndExpiresCandidates", HostedUdpAdmissionAcceptsPreviousBucketAndExpiresCandidates);
        Run("HostedUdpNativeReceiveRoutesUnknownAdmission", HostedUdpNativeReceiveRoutesUnknownAdmission);
        Run("WarmedHostedUdpUnknownAdmissionAllocatesZero", WarmedHostedUdpUnknownAdmissionAllocatesZero);
        Run("HostedUdpDeploymentDiscoveryIsUntrustedAndStateless",
            HostedUdpDeploymentDiscoveryIsUntrustedAndStateless);
        Run("HostedUdpDefaultLimitsAreOperational", HostedUdpDefaultLimitsAreOperational);
    }

    private static void HostedUdpCookieValidationAllocatesNoConnectionState()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_udp, s_shared,
            new HostedUdpAdmissionLimits(2, 8, 8, 8, 2_000), s_manifest);
        using Socket client = CreateClient(host.LocalEndPoint);
        using ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] nonce = RandomNumberGenerator.GetBytes(ProtocolLoopbackHandshake.NonceBytes);

        byte[] cookie = RequestCookie(host, client, nonce, publicKey);
        HostedUdpAdmissionSnapshot challenged = host.GetSnapshot();
        Equal(0, challenged.ValidatedCandidates);
        Equal(1UL, challenged.CookieChallengesSent);
        Equal(0, challenged.Listener.RegisteredPaths);
        Equal(0UL, challenged.AmplificationDrops);
        True(challenged.CookieChallengeBytes <= challenged.UnknownBytes,
            "The stateless response amplified unauthenticated input bytes.");

        SendKeyExchange(client, nonce, publicKey, cookie);
        host.Pump();
        HostedUdpAdmissionSnapshot admitted = host.GetSnapshot();
        Equal(1, admitted.ValidatedCandidates);
        Equal(1UL, admitted.CookieValidations);
        Equal(0, admitted.Listener.RegisteredPaths,
            "Cookie validation allocated an established endpoint before H0.2 promotion.");

        SendKeyExchange(client, nonce, publicKey, cookie);
        host.Pump();
        HostedUdpAdmissionSnapshot refreshed = host.GetSnapshot();
        Equal(1, refreshed.ValidatedCandidates);
        Equal(1UL, refreshed.CandidateRefreshes);

        byte[] copied = new byte[identity.PublicKey.Length];
        True(host.TryCopyServerPublicKey(copied, out int written));
        Equal(copied.Length, written);
        True(copied.AsSpan().SequenceEqual(identity.PublicKey));
        False(host.TryCopyServerPublicKey(new byte[copied.Length - 1], out written));
        Equal(0, written);
    }

    private static void HostedUdpAdmissionRateCapacityAndAmplificationAreBounded()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_udp, s_shared,
            new HostedUdpAdmissionLimits(1, 8, 2, 8, 2_000), s_manifest);
        using Socket first = CreateClient(host.LocalEndPoint);
        using Socket second = CreateClient(host.LocalEndPoint);
        using ECDiffieHellman firstKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        using ECDiffieHellman secondKey = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] firstPublic = firstKey.ExportSubjectPublicKeyInfo();
        byte[] secondPublic = secondKey.ExportSubjectPublicKeyInfo();
        byte[] firstNonce = RandomNumberGenerator.GetBytes(ProtocolLoopbackHandshake.NonceBytes);
        byte[] secondNonce = RandomNumberGenerator.GetBytes(ProtocolLoopbackHandshake.NonceBytes);

        byte[] firstCookie = RequestCookie(host, first, firstNonce, firstPublic);
        byte[] secondCookie = RequestCookie(host, second, secondNonce, secondPublic);
        SendHello(first, firstNonce, firstPublic);
        host.Pump();
        Equal(1UL, host.GetSnapshot().RateLimitedDatagrams);

        SendKeyExchange(first, firstNonce, firstPublic, firstCookie);
        host.Pump();
        Equal(1, host.GetSnapshot().ValidatedCandidates);

        byte[] invalidCookie = secondCookie.ToArray();
        invalidCookie[0] ^= 0x80;
        SendKeyExchange(second, secondNonce, secondPublic, invalidCookie);
        host.Pump();
        HostedUdpAdmissionSnapshot invalid = host.GetSnapshot();
        Equal(1, invalid.ValidatedCandidates);
        True(invalid.InvalidDatagrams >= 1);

        SendKeyExchange(second, secondNonce, secondPublic, secondCookie);
        host.Pump();
        HostedUdpAdmissionSnapshot full = host.GetSnapshot();
        Equal(1, full.ValidatedCandidates);
        Equal(1UL, full.CandidateCapacityDrops);
        True(full.CookieChallengeBytes <= full.UnknownBytes);
        Equal(0, full.Listener.RegisteredPaths);

        Span<byte> malformed = stackalloc byte[8];
        malformed.Fill(0xA5);
        for (int index = 0; index < 32; index++)
            Equal(malformed.Length, first.Send(malformed));
        host.Pump();
        HostedUdpAdmissionSnapshot hostile = host.GetSnapshot();
        True(hostile.InvalidDatagrams >= 33);
        Equal(2UL, hostile.CookieChallengesSent,
            "Malformed datagrams caused an unauthenticated response.");
    }

    private static void HostedUdpAdmissionAcceptsPreviousBucketAndExpiresCandidates()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        var clock = new ManualClock(1_100);
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_udp, s_shared,
            new HostedUdpAdmissionLimits(2, 8, 8, 8, 500), clock, s_manifest);
        using Socket client = CreateClient(host.LocalEndPoint);
        using ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] nonce = RandomNumberGenerator.GetBytes(ProtocolLoopbackHandshake.NonceBytes);
        byte[] cookie = RequestCookie(host, client, nonce, publicKey);

        clock.Milliseconds = 2_100;
        SendKeyExchange(client, nonce, publicKey, cookie);
        host.Pump();
        Equal(1, host.GetSnapshot().ValidatedCandidates,
            "The immediately previous cookie bucket was rejected.");

        clock.Milliseconds = 2_601;
        host.Pump();
        HostedUdpAdmissionSnapshot expired = host.GetSnapshot();
        Equal(0, expired.ValidatedCandidates);
        Equal(1UL, expired.CandidateTimeouts);

        byte[] staleNonce = RandomNumberGenerator.GetBytes(ProtocolLoopbackHandshake.NonceBytes);
        clock.Milliseconds = 3_100;
        byte[] staleCookie = RequestCookie(host, client, staleNonce, publicKey);
        clock.Milliseconds = 5_100;
        SendKeyExchange(client, staleNonce, publicKey, staleCookie);
        host.Pump();
        HostedUdpAdmissionSnapshot stale = host.GetSnapshot();
        Equal(0, stale.ValidatedCandidates);
        True(stale.InvalidDatagrams >= 1,
            "A cookie older than the immediately previous bucket was admitted.");
    }

    private static void WarmedHostedUdpUnknownAdmissionAllocatesZero()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_udp,
            s_shared with { MaximumInboundDatagramsPerPump = 512 },
            new HostedUdpAdmissionLimits(2, 8, 128, 8, 2_000), s_manifest);
        using Socket client = CreateClient(host.LocalEndPoint);
        using ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] nonce = RandomNumberGenerator.GetBytes(ProtocolLoopbackHandshake.NonceBytes);
        Span<byte> hello = stackalloc byte[512];
        int helloLength = ProtocolLoopbackHandshake.WriteClientHello(
            hello, s_manifest, nonce, publicKey);

        for (int index = 0; index < 8; index++)
        {
            Equal(helloLength, client.Send(hello[..helloLength]));
            host.Pump();
            DrainClient(client);
        }
        for (int index = 0; index < 64; index++)
            Equal(helloLength, client.Send(hello[..helloLength]));

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        long before = GC.GetAllocatedBytesForCurrentThread();
        host.Pump();
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Equal(0L, allocated,
            "The warmed external-cookie admission receive/challenge path allocated managed memory.");
        HostedUdpAdmissionSnapshot snapshot = host.GetSnapshot();
        Equal(72UL, snapshot.ClientHellos);
        True(snapshot.CookieChallengesSent <= 72);
        True(snapshot.CookieChallengeBytes <= snapshot.UnknownBytes);
    }

    private static void HostedUdpNativeReceiveRoutesUnknownAdmission()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_udp,
            s_shared with { SocketIoMode = SharedUdpSocketIoMode.PlatformSendReceiveBatch },
            new HostedUdpAdmissionLimits(2, 8, 8, 8, 2_000), s_manifest);
        using Socket client = CreateClient(host.LocalEndPoint);
        using ECDiffieHellman key = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = key.ExportSubjectPublicKeyInfo();
        byte[] nonce = RandomNumberGenerator.GetBytes(ProtocolLoopbackHandshake.NonceBytes);
        byte[] cookie = RequestCookie(host, client, nonce, publicKey);
        SendKeyExchange(client, nonce, publicKey, cookie);
        host.Pump();
        HostedUdpAdmissionSnapshot snapshot = host.GetSnapshot();
        Equal(1, snapshot.ValidatedCandidates);
        Equal(1UL, snapshot.CookieChallengesSent);
        Equal(1UL, snapshot.CookieValidations);
        Equal(0, snapshot.Listener.RegisteredPaths);
    }

    private static void HostedUdpDeploymentDiscoveryIsUntrustedAndStateless()
    {
        using NetworkServerIdentity identity = NetworkServerIdentity.CreateEphemeral();
        using var host = new HostedUdpAdmissionHost(
            new IPEndPoint(IPAddress.Loopback, 0), identity, s_udp, s_shared,
            new HostedUdpAdmissionLimits(2, 8, 8, 8, 2_000), s_manifest);
        True(HostedUdpDeploymentDiscoveryClient.TryCreate(
            host.LocalEndPoint, s_udp, out HostedUdpDeploymentDiscoveryClient? discovered,
            s_manifest));
        using HostedUdpDeploymentDiscoveryClient client = discovered!;
        for (int iteration = 0;
             iteration < 10_000 && client.State == HostedUdpDeploymentDiscoveryState.Discovering;
             iteration++)
        {
            client.Pump();
            host.Pump();
            Thread.Yield();
        }
        True(client.State == HostedUdpDeploymentDiscoveryState.Discovered);
        byte[] candidate = new byte[client.CandidatePublicKeyBytes];
        True(client.TryCopyCandidatePublicKey(candidate, out int written));
        Equal(candidate.Length, written);
        True(candidate.AsSpan().SequenceEqual(identity.PublicKey));

        HostedUdpAdmissionSnapshot snapshot = host.GetSnapshot();
        Equal(1UL, snapshot.DeploymentDiscoveryRequests);
        Equal(1UL, snapshot.DeploymentAnnouncementsSent);
        Equal(0, snapshot.ValidatedCandidates,
            "Untrusted deployment discovery allocated validated-candidate state.");
        Equal(0, snapshot.EstablishedSessions,
            "Untrusted deployment discovery allocated application-session state.");
        Equal(0, snapshot.Listener.RegisteredPaths,
            "Untrusted deployment discovery established a listener path.");
        True(snapshot.UnknownBytes >= 256,
            "The fixed discovery request was not accounted as unknown-source input.");
    }

    private static void HostedUdpDefaultLimitsAreOperational()
    {
        NetworkProtocolLimits.Default.Validate(DeterministicDatagramLimits.Default);
        UdpProtocolLimits.Default.Validate();
        SharedUdpProtocolLimits.Default.Validate(UdpProtocolLimits.Default);
        HostedUdpAdmissionLimits.Default.Validate(UdpProtocolLimits.Default);
        HostedUdpOwnerShardingLimits.Default.Validate();
    }

    private static byte[] RequestCookie(
        HostedUdpAdmissionHost host,
        Socket client,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey)
    {
        SendHello(client, nonce, publicKey);
        Span<byte> response = stackalloc byte[512];
        int spins = 0;
        while (spins++ < 10_000)
        {
            host.Pump();
            if (!client.Poll(0, SelectMode.SelectRead)) continue;
            int length = client.Receive(response);
            True(ProtocolLoopbackHandshake.TryReadCookieChallenge(
                response[..length], nonce, out ReadOnlySpan<byte> cookie));
            return cookie.ToArray();
        }
        throw new InvalidOperationException("The hosted listener did not return a cookie challenge.");
    }

    private static void SendHello(
        Socket client,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey)
    {
        Span<byte> datagram = stackalloc byte[512];
        int length = ProtocolLoopbackHandshake.WriteClientHello(
            datagram, s_manifest, nonce, publicKey);
        Equal(length, client.Send(datagram[..length]));
    }

    private static void SendKeyExchange(
        Socket client,
        ReadOnlySpan<byte> nonce,
        ReadOnlySpan<byte> publicKey,
        ReadOnlySpan<byte> cookie)
    {
        Span<byte> datagram = stackalloc byte[512];
        int length = ProtocolLoopbackHandshake.WriteClientKeyExchange(
            datagram, s_manifest, nonce, publicKey, cookie);
        Equal(length, client.Send(datagram[..length]));
    }

    private static Socket CreateClient(IPEndPoint server)
    {
        var socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        socket.Connect(server);
        socket.Blocking = false;
        return socket;
    }

    private static void DrainClient(Socket client)
    {
        Span<byte> scratch = stackalloc byte[512];
        while (client.Poll(0, SelectMode.SelectRead)) _ = client.Receive(scratch);
    }

    private static void Run(string name, Action test)
    {
        test();
        Console.WriteLine($"PASS {name}");
    }

    private static void True(bool condition, string? message = null)
    {
        if (!condition) throw new InvalidOperationException(message ?? "Expected true.");
    }

    private static void False(bool condition, string? message = null) =>
        True(!condition, message ?? "Expected false.");

    private static void Equal<T>(T expected, T actual, string? message = null)
        where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
            throw new InvalidOperationException(message ?? $"Expected {expected}, got {actual}.");
    }

    private sealed class ManualClock : INetworkMonotonicClock
    {
        internal ManualClock(ulong milliseconds) => Milliseconds = milliseconds;
        public ulong Milliseconds { get; set; }
    }
}
