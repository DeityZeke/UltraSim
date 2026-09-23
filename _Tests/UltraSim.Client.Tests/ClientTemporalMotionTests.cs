using System.Diagnostics;
using UltraSim.Client.Core;
using UltraSim.Game.Input;
using UltraSim.Game.World;

internal static class ClientTemporalMotionTests
{
    internal static void Run()
    {
        ControlledLeadAndOwnerTruth();
        StopReverseDestinationAndBlocked();
        DelayedPreAcknowledgementOwnerStatesDoNotSuppressPrediction();
        DeadbandAndDiscontinuityAreDistinct();
        CapacityLineageAndFrameRate();
        RemoteInterpolationAndFences();
        HighCadenceSamplesRetainTheDelayedBracket();
        WarmedUpdatesDoNotAllocate();
        FullBoundedActorWindowHasFiniteCost();
    }

    private static void ControlledLeadAndOwnerTruth()
    {
        Assert.False(default(ClientControlledMotionPolicy).IsValid);
        Assert.True(ClientControlledMotionPolicy.ConceptDemo.IsValid);
        var motion = new ClientControlledMotionPresentation();
        WorldPosition origin = Position(0);
        WorldPublicEntityId actor = new(11, 1);
        motion.ObserveOwner(1, new GameControlSessionEpoch(2), 3, actor,
            1, 0, origin, new WorldYaw(0), false, 1_000_000);
        Assert.True(motion.ObserveAdmittedCommand(1,
            GameLocomotionUpdate.CreateDirect(short.MaxValue, 0), 1_001_000));
        ClientResolvedMotionPose predicted = motion.Sample(1_101_000);
        Assert.Equal(ClientMotionPoseProvenance.PredictedControlled, predicted.Provenance);
        Assert.True(predicted.Pose.GlobalXMillimeters is >= 590 and <= 610);
        Assert.Equal(0L, origin.GlobalXMillimeters);
        Assert.Equal(100_000L, motion.GetSnapshot().InputToPresentedMicroseconds);
        motion.ObserveOwner(1, new GameControlSessionEpoch(2), 3, actor,
            2, 1, Position(300), new WorldYaw(0), false, 1_151_000);
        ClientResolvedMotionPose after = motion.Sample(1_151_000);
        Assert.True(after.Pose.GlobalXMillimeters >= predicted.Pose.GlobalXMillimeters);
        Assert.True(after.Pose.GlobalXMillimeters <= predicted.Pose.GlobalXMillimeters + 310);
        Assert.Equal(1UL, motion.GetSnapshot().CommandsAcknowledged);
        ClientControlledMotionSnapshot beforeDuplicate = motion.GetSnapshot();
        motion.ObserveOwner(1, new GameControlSessionEpoch(2), 3, actor,
            2, 1, Position(900), new WorldYaw(0), false, 1_160_000);
        Assert.Equal(beforeDuplicate, motion.GetSnapshot());
    }

    private static void StopReverseDestinationAndBlocked()
    {
        var motion = NewControlled();
        Assert.True(motion.ObserveAdmittedCommand(1,
            GameLocomotionUpdate.CreateDirect(short.MaxValue, 0), 1_001_000));
        long forward = motion.Sample(1_081_000).Pose.GlobalXMillimeters;
        Assert.True(motion.ObserveAdmittedCommand(2,
            GameLocomotionUpdate.CreateDirect(-short.MaxValue, 0), 1_081_000));
        long reverse = motion.Sample(1_121_000).Pose.GlobalXMillimeters;
        Assert.True(reverse < forward);
        Assert.True(motion.ObserveAdmittedCommand(3,
            GameLocomotionUpdate.Stop, 1_121_000));
        Assert.Equal(reverse, motion.Sample(1_171_000).Pose.GlobalXMillimeters);
        motion.ObserveOwner(1, new GameControlSessionEpoch(2), 3,
            new WorldPublicEntityId(11, 1), 2, 3, Position(100), new WorldYaw(0),
            false, 1_172_000);
        Assert.Equal(100L, motion.Sample(1_400_000).Pose.GlobalXMillimeters);
        Assert.True(motion.ObserveAdmittedCommand(4,
            GameLocomotionUpdate.CreateDestination(3_000, 0), 1_401_000));
        Assert.True(motion.Sample(1_451_000).Pose.GlobalXMillimeters > 100);

        var blocked = NewControlled();
        Assert.True(blocked.ObserveAdmittedCommand(1,
            GameLocomotionUpdate.CreateDirect(short.MaxValue, 0), 1_001_000));
        ClientResolvedMotionPose clamped = blocked.Sample(1_501_000);
        Assert.True(clamped.Pose.GlobalXMillimeters <=
            ClientControlledMotionPolicy.ConceptDemo.MaximumLeadMillimeters);
        Assert.True(blocked.GetSnapshot().PredictionClamps > 0);
        blocked.ObserveOwner(1, new GameControlSessionEpoch(2), 3,
            new WorldPublicEntityId(11, 1), 2, 1, Position(0), new WorldYaw(0),
            true, 1_502_000);
        Assert.Equal(0L, blocked.Sample(1_900_000).Pose.GlobalXMillimeters);
        Assert.True(blocked.GetSnapshot().SmoothCorrections > 0);
        Assert.True(blocked.ObserveAdmittedCommand(2,
            GameLocomotionUpdate.CreateDirect(short.MaxValue, 0), 1_901_000));
        Assert.Equal(0L, blocked.Sample(2_001_000).Pose.GlobalXMillimeters);
    }

    private static void DelayedPreAcknowledgementOwnerStatesDoNotSuppressPrediction()
    {
        var motion = NewControlled();
        Assert.True(motion.ObserveAdmittedCommand(1,
            GameLocomotionUpdate.CreateDirect(short.MaxValue, 0), 1_001_000));
        WorldPublicEntityId actor = new(11, 1);
        for (ulong state = 2; state <= 5; state++)
        {
            long now = 1_001_000 + (long)state * 16_000;
            motion.ObserveOwner(1, new GameControlSessionEpoch(2), 3, actor,
                state, 0, Position(0), WorldYaw.Zero, false, now);
        }
        Assert.True(motion.Sample(1_102_000).Pose.GlobalXMillimeters > 300);
        motion.ObserveOwner(1, new GameControlSessionEpoch(2), 3, actor,
            6, 1, Position(0), WorldYaw.Zero, true, 1_118_000);
        Assert.Equal(0L, motion.Sample(1_400_000).Pose.GlobalXMillimeters);
    }

    private static void DeadbandAndDiscontinuityAreDistinct()
    {
        var motion = NewControlled();
        WorldPublicEntityId actor = new(11, 1);
        motion.ObserveOwner(1, new GameControlSessionEpoch(2), 3, actor,
            2, 0, Position(10), WorldYaw.Zero, false, 1_016_000);
        Assert.Equal(0UL, motion.GetSnapshot().SmoothCorrections);
        motion.ObserveOwner(1, new GameControlSessionEpoch(2), 3, actor,
            3, 0, Position(5_010), WorldYaw.Zero, false, 1_032_000);
        ClientResolvedMotionPose snap = motion.Sample(1_032_000);
        Assert.Equal(ClientMotionPoseProvenance.DiscontinuitySnap, snap.Provenance);
        Assert.Equal(5_010L, snap.Pose.GlobalXMillimeters);
        Assert.Equal(1UL, motion.GetSnapshot().DiscontinuityResets);
        Assert.Equal(0UL, motion.GetSnapshot().LineageResets);
    }

    private static void CapacityLineageAndFrameRate()
    {
        var oneStep = NewControlled();
        var manySteps = NewControlled();
        Assert.True(oneStep.ObserveAdmittedCommand(1,
            GameLocomotionUpdate.CreateDirect(short.MaxValue, 0), 1_001_000));
        Assert.True(manySteps.ObserveAdmittedCommand(1,
            GameLocomotionUpdate.CreateDirect(short.MaxValue, 0), 1_001_000));
        long once = oneStep.Sample(1_151_000).Pose.GlobalXMillimeters;
        for (long time = 1_011_000; time <= 1_151_000; time += 10_000)
            _ = manySteps.Sample(time);
        long stepped = manySteps.Sample(1_151_000).Pose.GlobalXMillimeters;
        Assert.True(Math.Abs(once - stepped) <= 1);

        var full = NewControlled();
        for (ulong sequence = 1; sequence <= ClientControlledMotionPresentation.HistoryCapacity;
            sequence++)
            Assert.True(full.ObserveAdmittedCommand(sequence,
                GameLocomotionUpdate.Unchanged, 1_000_000 + (long)sequence));
        Assert.False(full.ObserveAdmittedCommand(129,
            GameLocomotionUpdate.CreateDirect(short.MaxValue, 0), 1_001_000));
        Assert.Equal(ClientControlledMotionPresentation.HistoryCapacity,
            full.GetSnapshot().HistoryCount);
        full.ObserveOwner(1, new GameControlSessionEpoch(2), 3,
            new WorldPublicEntityId(11, 1), 2, 100, Position(0), new WorldYaw(0),
            false, 1_002_000);
        Assert.Equal(28, full.GetSnapshot().HistoryCount);
        full.ObserveOwner(1, new GameControlSessionEpoch(2), 4,
            new WorldPublicEntityId(11, 2), 1, 0, Position(20_000), new WorldYaw(0),
            false, 1_003_000);
        Assert.Equal(0, full.GetSnapshot().HistoryCount);
        Assert.Equal(20_000L, full.Sample(1_003_000).Pose.GlobalXMillimeters);
        Assert.True(full.GetSnapshot().LineageResets > 0);
    }

    private static void RemoteInterpolationAndFences()
    {
        var interpolation = new ClientRemoteActorInterpolation();
        WorldPublicEntityId actor = new(7, 1);
        var first = new ClientMotionPose(0, 0, 0, new WorldYaw(65_530));
        var second = new ClientMotionPose(1_000, 0, 0, new WorldYaw(6));
        Assert.True(interpolation.Observe(3, actor, 1, first, 1_000_000));
        Assert.True(interpolation.TrySample(actor, 1_050_000, out ClientResolvedMotionPose one));
        Assert.Equal(first, one.Pose);
        Assert.Equal(ClientMotionPoseProvenance.HeldRemote, one.Provenance);
        Assert.True(interpolation.Observe(3, actor, 2, second, 1_100_000));
        Assert.False(interpolation.Observe(3, actor, 2, second, 1_101_000));
        Assert.True(interpolation.TrySample(actor, 1_150_000, out ClientResolvedMotionPose middle));
        Assert.Equal(500L, middle.Pose.GlobalXMillimeters);
        Assert.True(middle.Pose.Orientation.Units is >= 65_530 or <= 6);
        Assert.True(interpolation.TrySample(actor, 1_500_000, out ClientResolvedMotionPose held));
        Assert.Equal(second, held.Pose);
        Assert.Equal(ClientMotionPoseProvenance.HeldRemote, held.Provenance);
        Assert.True(interpolation.GetSnapshot().MaximumNewestSampleAgeMicroseconds >= 400_000);
        Assert.True(interpolation.TrySample(actor, 1_610_001, out _));
        Assert.True(interpolation.Observe(3, actor, 3,
            new ClientMotionPose(10_000, 0, 0, new WorldYaw(0)), 1_510_000));
        Assert.Equal(1UL, interpolation.GetSnapshot().TeleportResets);
        Assert.True(interpolation.TrySample(actor, 1_510_000, out ClientResolvedMotionPose teleport));
        Assert.Equal(ClientMotionPoseProvenance.DiscontinuitySnap, teleport.Provenance);
        interpolation.Remove(actor);
        Assert.False(interpolation.TrySample(actor, 1_520_000, out _));
        Assert.True(interpolation.Observe(3, new WorldPublicEntityId(7, 2), 1,
            second, 1_530_000));
        interpolation.ResetGeneration(4);
        Assert.Equal(0, interpolation.GetSnapshot().ActiveBuffers);
        Assert.False(interpolation.TrySample(new WorldPublicEntityId(7, 2), 1_540_000, out _));
        Assert.True(interpolation.Observe(4, actor, 1, first, 1_550_000));
        Assert.True(interpolation.Observe(4, actor, 2, second, 2_100_000));
        Assert.True(interpolation.GetSnapshot().GapResets > 0);
        Assert.True(interpolation.TrySample(actor, 2_100_000, out ClientResolvedMotionPose gap));
        Assert.Equal(ClientMotionPoseProvenance.DiscontinuitySnap, gap.Provenance);
        Assert.True(interpolation.GetSnapshot().SampleHighWater <=
            ClientRemoteActorInterpolation.SamplesPerActor);
        Assert.True(interpolation.GetSnapshot().StaleHeldFrames > 0);
    }

    private static void HighCadenceSamplesRetainTheDelayedBracket()
    {
        var interpolation = new ClientRemoteActorInterpolation();
        WorldPublicEntityId actor = new(7, 1);
        for (int index = 0; index < 20; index++)
            Assert.True(interpolation.Observe(1, actor, checked((ulong)(index + 1)),
                new ClientMotionPose(index * 100, 0, 0, WorldYaw.Zero),
                1_000_000 + index * 16_667L));
        Assert.True(interpolation.TrySample(actor, 1_000_000 + 19 * 16_667L,
            out ClientResolvedMotionPose delayed));
        Assert.Equal(ClientMotionPoseProvenance.InterpolatedRemote, delayed.Provenance);
        Assert.True(delayed.Pose.GlobalXMillimeters is >= 1_300 and <= 1_400);
        Assert.Equal(ClientRemoteActorInterpolation.SamplesPerActor,
            interpolation.GetSnapshot().SampleHighWater);
    }

    private static void WarmedUpdatesDoNotAllocate()
    {
        var controlled = NewControlled();
        var remote = new ClientRemoteActorInterpolation();
        WorldPublicEntityId actor = new(7, 1);
        var pose = new ClientMotionPose(0, 0, 0, new WorldYaw(0));
        Assert.True(remote.Observe(3, actor, 1, pose, 1_000_000));
        _ = controlled.Sample(1_001_000);
        _ = remote.TrySample(actor, 1_001_000, out _);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int index = 0; index < 1000; index++)
        {
            _ = controlled.Sample(1_002_000 + index);
            _ = remote.TrySample(actor, 1_002_000 + index, out _);
        }
        Assert.Equal(0L, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void FullBoundedActorWindowHasFiniteCost()
    {
        var remote = new ClientRemoteActorInterpolation();
        var ids = new WorldPublicEntityId[ClientRemoteActorInterpolation.MaximumActors];
        for (int index = 0; index < ids.Length; index++)
        {
            ids[index] = new WorldPublicEntityId(checked((uint)(index + 1)), 1);
            Assert.True(remote.Observe(1, ids[index], 1,
                new ClientMotionPose(index * 100, 0, 0, WorldYaw.Zero), 1_000_000));
            Assert.True(remote.Observe(1, ids[index], 2,
                new ClientMotionPose(index * 100 + 100, 0, 0, WorldYaw.Zero), 1_100_000));
        }
        for (int index = 0; index < ids.Length; index++)
            Assert.True(remote.TrySample(ids[index], 1_150_000, out _));
        const int frames = 1_000;
        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        long started = Stopwatch.GetTimestamp();
        for (int frame = 0; frame < frames; frame++)
            for (int index = 0; index < ids.Length; index++)
                _ = remote.TrySample(ids[index], 1_150_000 + frame, out _);
        long ticks = Stopwatch.GetTimestamp() - started;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
        Assert.Equal(0L, allocated);
        ClientRemoteActorInterpolationSnapshot snapshot = remote.GetSnapshot();
        Assert.Equal(ids.Length, snapshot.ActiveBuffers);
        Console.WriteLine($"EVIDENCE F1.8A core actor_window={ids.Length} frames={frames} " +
            $"total_us={(double)ticks * 1_000_000 / Stopwatch.Frequency:F0} " +
            $"alloc_bytes={allocated} sample_reservation={snapshot.ReservedSampleBytes} " +
            $"index_reservation={snapshot.ReservedActorIndexBytes} " +
            $"history_reservation={new ClientControlledMotionPresentation().GetSnapshot().ReservedHistoryBytes}");
    }

    private static ClientControlledMotionPresentation NewControlled()
    {
        var motion = new ClientControlledMotionPresentation();
        motion.ObserveOwner(1, new GameControlSessionEpoch(2), 3,
            new WorldPublicEntityId(11, 1), 1, 0, Position(0), new WorldYaw(0),
            false, 1_000_000);
        return motion;
    }

    private static WorldPosition Position(long x)
    {
        Assert.True(WorldPosition.TryFromGlobalMillimeters(x, 0, 0,
            out WorldPosition position));
        return position;
    }
}
