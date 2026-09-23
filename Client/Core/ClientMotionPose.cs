using UltraSim.Game.World;

namespace UltraSim.Client.Core;

/// <summary>Renderer-neutral global pose. This is presentation data, never Game authority.</summary>
public readonly record struct ClientMotionPose(
    long GlobalXMillimeters,
    int YMillimeters,
    long GlobalZMillimeters,
    WorldYaw Orientation)
{
    public static ClientMotionPose FromAuthority(in WorldPosition position, WorldYaw orientation) =>
        new(position.GlobalXMillimeters, position.YMillimeters,
            position.GlobalZMillimeters, orientation);

    public bool TryGetWorldPosition(out WorldPosition position) =>
        WorldPosition.TryFromGlobalMillimeters(
            GlobalXMillimeters, YMillimeters, GlobalZMillimeters, out position);

    public static ClientMotionPose Interpolate(
        in ClientMotionPose from, in ClientMotionPose to, long elapsed, long duration)
    {
        if (duration <= 0 || elapsed <= 0) return from;
        if (elapsed >= duration) return to;
        long x = InterpolateLong(from.GlobalXMillimeters, to.GlobalXMillimeters, elapsed, duration);
        long z = InterpolateLong(from.GlobalZMillimeters, to.GlobalZMillimeters, elapsed, duration);
        int y = checked((int)InterpolateLong(from.YMillimeters, to.YMillimeters, elapsed, duration));
        int yawDelta = ((to.Orientation.Units - from.Orientation.Units +
            WorldYaw.UnitsPerTurn + WorldYaw.UnitsPerTurn / 2) % WorldYaw.UnitsPerTurn) -
            WorldYaw.UnitsPerTurn / 2;
        int yaw = (int)((from.Orientation.Units +
            (long)yawDelta * elapsed / duration + WorldYaw.UnitsPerTurn) % WorldYaw.UnitsPerTurn);
        return new ClientMotionPose(x, y, z, new WorldYaw(checked((ushort)yaw)));
    }

    public static long HorizontalDistanceMillimeters(
        in ClientMotionPose left, in ClientMotionPose right)
    {
        double dx = (double)left.GlobalXMillimeters - right.GlobalXMillimeters;
        double dz = (double)left.GlobalZMillimeters - right.GlobalZMillimeters;
        return (long)Math.Min(long.MaxValue, Math.Sqrt(dx * dx + dz * dz));
    }

    public static long SpatialDistanceMillimeters(
        in ClientMotionPose left, in ClientMotionPose right)
    {
        double dx = (double)left.GlobalXMillimeters - right.GlobalXMillimeters;
        double dy = (double)left.YMillimeters - right.YMillimeters;
        double dz = (double)left.GlobalZMillimeters - right.GlobalZMillimeters;
        return (long)Math.Min(long.MaxValue, Math.Sqrt(dx * dx + dy * dy + dz * dz));
    }

    private static long InterpolateLong(long from, long to, long elapsed, long duration) =>
        checked(from + (long)(((Int128)to - from) * elapsed / duration));
}

public enum ClientMotionPoseProvenance : byte
{
    Authoritative = 1,
    PredictedControlled = 2,
    ReconciledControlled = 3,
    InterpolatedRemote = 4,
    HeldRemote = 5,
    DiscontinuitySnap = 6,
}

public readonly record struct ClientResolvedMotionPose(
    ClientMotionPose Pose,
    ClientMotionPoseProvenance Provenance);
