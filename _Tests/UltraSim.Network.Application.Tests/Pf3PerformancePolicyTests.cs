using System.Diagnostics;
using UltraSim.Client.Network;
using UltraSim.Game.Application;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Network;

internal static class Pf3PerformancePolicyTests
{
    internal const int TestCount = 4;

    internal static void RunAll()
    {
        ProfileIdentityIsCanonicalAndBounded();
        AuthenticatedProfileRequiresExactFreshProof();
        RateLimiterEnforcesSustainedAndBurstBounds();
        ClassificationIncludesPerformanceMessages();
        Console.WriteLine($"PASS PF3 server performance profile/rate policy ({TestCount} tests)");
    }

    private static void ProfileIdentityIsCanonicalAndBounded()
    {
        RemoteServerPerformanceProfile profile = CreateProfile();
        RemoteServerPerformanceProfileMessage message =
            UltraSimGameProtocol.CreateServerPerformanceProfile(77, profile);
        Span<byte> bytes = stackalloc byte[RemoteServerPerformanceProfileMessageCodec.EncodedSize];
        True(RemoteServerPerformanceProfileMessageCodec.TryWriteExact(bytes, in message));
        True(RemoteServerPerformanceProfileMessageCodec.TryReadExact(bytes, out var decoded));
        True(UltraSimGameProtocol.TryReadServerPerformanceProfile(in decoded, out var reopened));
        Equal(profile.Hash, reopened!.Hash);
        Equal(profile.MaximumInputHz, reopened.MaximumInputHz);
        decoded.RecommendedInputHz++;
        True(!UltraSimGameProtocol.TryReadServerPerformanceProfile(in decoded, out _));
        decoded = message;
        decoded.MaximumNetworkServiceIntervalMicroseconds = uint.MaxValue;
        True(!UltraSimGameProtocol.TryReadServerPerformanceProfile(in decoded, out _));
        True(!RemoteServerPerformanceProfile.TryCreate(
            1, 60, 2_000, 30, 31, 8, 100, 60, 60, 30, 10, 100, out _));
        True(!RemoteServerPerformanceProfile.TryCreate(
            1, 60, 2_000, 60, 30, 5, 100, 60, 60, 30, 10, 100, out _));
    }

    private static void AuthenticatedProfileRequiresExactFreshProof()
    {
        True(ProtocolLoopback.TryCreate(
            NetworkProtocolLimits.Default,
            new DeterministicDatagramLimits(2_048, 1_200),
            default,
            out ProtocolLoopback? loop,
            out _,
            UltraSimGameProtocol.Manifest));
        using (loop)
        using (var server = new RemoteGuestPlayableApplication(
            loop!.Server,
            new ApplicationRealmId(1, 2),
            new UltraSim.Game.Admission.ParticipantId(1),
            77,
            serverPerformanceProfile: CreateProfile()))
        {
            var client = new RemoteServerPerformanceClient(loop.Client, 77);
            for (int step = 0; step < 10_000 &&
                    !server.GetSnapshot().ServerPerformanceProfileApplied; step++)
            {
                _ = server.TryProgress();
                loop.Pump();
                while (loop.Client.TryPeekReceived(
                           out NetworkDeliveredMessage inbound, out ReadOnlySpan<byte> payload))
                {
                    try
                    {
                        if (inbound.MessageId ==
                                RemoteServerPerformanceProfileMessageCodec.Descriptor.Id)
                            True(client.TryProcess(in inbound, payload));
                    }
                    finally { _ = loop.Client.TryConsumeReceived(); }
                }
                _ = client.TryProgress();
                loop.Pump();
                while (loop.Server.TryPeekReceived(
                           out NetworkDeliveredMessage proof, out ReadOnlySpan<byte> payload))
                {
                    try { _ = server.TryProcess(in proof, payload); }
                    finally { _ = loop.Server.TryConsumeReceived(); }
                }
            }
            RemoteGuestPlayableServerSnapshot snapshot = server.GetSnapshot();
            True(snapshot.ServerPerformanceProfileRequired);
            True(snapshot.ServerPerformanceProfileApplied);
            Equal((ulong)1, client.GetSnapshot().ProofsQueued);
            Equal((ulong)77, client.GetSnapshot().ApplicationSessionEpoch);
        }
    }

    private static void RateLimiterEnforcesSustainedAndBurstBounds()
    {
        RemoteServerPerformanceProfile profile = CreateProfile();
        var limiter = new ServerInputRateLimiter(profile);
        long now = Stopwatch.GetTimestamp();
        for (int index = 0; index < profile.InputBurstCapacity; index++)
            True(limiter.TryAdmitAt(now));
        True(!limiter.TryAdmitAt(now));
        long burstWindowTicks = checked((long)((Int128)
            profile.InputBurstWindowMilliseconds * Stopwatch.Frequency / 1_000));
        True(!limiter.TryAdmitAt(now + burstWindowTicks - 1));
        True(limiter.TryAdmitAt(now + burstWindowTicks));

        long oneSecond = Stopwatch.Frequency;
        limiter = new ServerInputRateLimiter(profile);
        now = Stopwatch.GetTimestamp();
        int admitted = 0;
        for (int index = 1; index <= 120; index++)
        {
            long sample = checked(now + (long)((Int128)index * oneSecond / 120));
            if (limiter.TryAdmitAt(sample)) admitted++;
        }
        True(admitted <= profile.MaximumInputHz + profile.InputBurstCapacity);
        True(admitted >= profile.MaximumInputHz - 2);

        var regular = new ServerInputRateLimiter(profile);
        now = Stopwatch.GetTimestamp();
        for (int index = 1; index <= 600; index++)
            True(regular.TryAdmitAt(checked(
                now + (long)((Int128)index * oneSecond / profile.MaximumInputHz))));
    }

    private static void ClassificationIncludesPerformanceMessages()
    {
        True(GameProtocolTrafficClassification.TryClassify(
            RemoteServerPerformanceProfileMessageCodec.Descriptor.Id, out var advertised));
        True(GameProtocolTrafficClassification.TryClassify(
            RemoteServerPerformanceProfileAppliedMessageCodec.Descriptor.Id, out var proof));
        Equal(GameProtocolTrafficFamily.ServerPerformanceProfile, advertised);
        Equal(advertised, proof);
        True(GameProtocolTrafficClassification.IsComplete(UltraSimGameProtocol.Manifest));
    }

    private static RemoteServerPerformanceProfile CreateProfile()
    {
        True(RemoteServerPerformanceProfile.TryCreate(
            1, 60, 2_000, 60, 30, 10, 100, 60, 60, 30, 10, 100,
            out RemoteServerPerformanceProfile? profile));
        return profile!;
    }

    private static void True(bool value)
    {
        if (!value) throw new InvalidOperationException("Expected true.");
    }

    private static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}
