using UltraSim.Client.Core;
using UltraSim.Game.World;

internal static class ClientActorRepresentationTests
{
    internal static void Run()
    {
        Assert.False(default(ClientActorRepresentationPolicy).IsValid);
        var policy = new ClientActorRepresentationPolicy(true, 2, 20_000, 28_000);
        var selector = new ClientActorRepresentationSelector(4, policy);
        var viewer = new ClientMotionPose(0, 0, 0, WorldYaw.Zero);
        var candidates = new[] { Candidate(4, 27_000), Candidate(2, 27_000), Candidate(3, 27_000) };
        var tiers = new ClientActorRepresentationTier[4];
        selector.Select(candidates, viewer, tiers);
        Assert.Equal(ClientActorRepresentationTier.BatchedPrimitive, tiers[0]);
        Assert.Equal(ClientActorRepresentationTier.Detailed, tiers[1]);
        Assert.Equal(ClientActorRepresentationTier.Detailed, tiers[2]);
        Array.Reverse(candidates);
        selector.Select(candidates, viewer, tiers);
        Assert.Equal(ClientActorRepresentationTier.BatchedPrimitive, tiers[2]);
        candidates[0] = Candidate(1, 28_001);
        selector.Select(candidates.AsSpan(0, 1), viewer, tiers);
        Assert.Equal(ClientActorRepresentationTier.BatchedPrimitive, tiers[0]);
        candidates[0] = Candidate(1, 24_000) with { Current = ClientActorRepresentationTier.BatchedPrimitive };
        selector.Select(candidates.AsSpan(0, 1), viewer, tiers);
        Assert.Equal(ClientActorRepresentationTier.BatchedPrimitive, tiers[0]);
        candidates[0] = candidates[0] with { Pose = new(19_000, 0, 0, WorldYaw.Zero) };
        selector.Select(candidates.AsSpan(0, 1), viewer, tiers);
        Assert.Equal(ClientActorRepresentationTier.Detailed, tiers[0]);
        for (int i = 0; i < 3; i++) candidates[i] = Candidate((uint)i + 1, 90_000) with { MandatoryDetailed = true };
        selector.Select(candidates, viewer, tiers);
        for (int i = 0; i < 3; i++) Assert.Equal(ClientActorRepresentationTier.Detailed, tiers[i]);
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++) selector.Select(candidates, viewer, tiers);
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - allocated);
        Assert.Throws<ArgumentOutOfRangeException>(() => new ClientActorRepresentationSelector(257, policy));
        Assert.Throws<ArgumentException>(() => selector.Select(candidates, viewer, tiers.AsSpan(0, 1)));
        TemporalBracketsAreActualSamples();
    }

    private static ClientActorRepresentationCandidate Candidate(uint index, long x) =>
        new(new(index, 1), new(x, 0, 0, WorldYaw.Zero), ClientActorRepresentationTier.Detailed, false);

    private static void TemporalBracketsAreActualSamples()
    {
        var interpolation = new ClientRemoteActorInterpolation();
        WorldPublicEntityId id = new(8, 1);
        var from = new ClientMotionPose(1000, 0, 0, WorldYaw.Zero);
        var to = new ClientMotionPose(2000, 0, 0, WorldYaw.QuarterTurn);
        Assert.True(interpolation.Observe(1, id, 10, from, 1_000_000));
        Assert.True(interpolation.Observe(1, id, 11, to, 1_100_000));
        Assert.True(interpolation.TrySampleTemporal(id, 1_150_000, out var sample));
        Assert.Equal(from, sample.Previous); Assert.Equal(to, sample.Current);
        Assert.Equal(10UL, sample.PreviousPublicVersion); Assert.Equal(11UL, sample.CurrentPublicVersion);
        Assert.Equal(1_050_000L, sample.TargetReceiptMicroseconds);
        Assert.Equal(1_000_000L, sample.PreviousReceiptMicroseconds);
        Assert.Equal(1_100_000L, sample.CurrentReceiptMicroseconds);
        Assert.Equal(1500L, sample.Resolved.Pose.GlobalXMillimeters);
        Assert.Equal(1UL, interpolation.GetSnapshot().InterpolatedFrames);
        Assert.True(interpolation.TrySample(id, 1_150_000, out var legacy));
        Assert.Equal(sample.Resolved, legacy);
        var relocated = to with { YMillimeters = 10_000 };
        Assert.True(interpolation.Observe(1, id, 12, relocated, 1_200_000));
        Assert.True(interpolation.TrySampleTemporal(id, 1_201_000, out sample));
        Assert.Equal(relocated, sample.Previous); Assert.Equal(relocated, sample.Current);
        Assert.Equal(ClientMotionPoseProvenance.DiscontinuitySnap, sample.Resolved.Provenance);
        interpolation.ResetGeneration(2);
        Assert.False(interpolation.TrySampleTemporal(id, 1_300_000, out _));
        Assert.True(interpolation.Observe(2, new(8, 2), 1, from, 1_400_000));
        Assert.False(interpolation.TrySampleTemporal(id, 1_400_001, out _));
    }
}
