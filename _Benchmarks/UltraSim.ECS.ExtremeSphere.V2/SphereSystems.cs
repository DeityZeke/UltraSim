using System.Diagnostics;
using UltraSim.ECS;

namespace UltraSim.ECS.ExtremeSphere.V2;

internal abstract class TimedSystem : SystemBase
{
    internal long LastElapsedTicks { get; private set; }
    internal long LastRows { get; private set; }

    protected static long BeginTiming() => Stopwatch.GetTimestamp();
    protected void EndTiming(long start, long rows)
    {
        LastRows = rows;
        LastElapsedTicks = Stopwatch.GetTimestamp() - start;
    }
}

internal sealed class PhaseSystem : TimedSystem
{
    private const int BlockSize = 4_096;
    private readonly World.PatchChunkPlanCache<SpherePulse> _plan = new();
    private readonly PatchChunkAction<SpherePulse> _patch;
    private float _deltaSeconds;

    internal PhaseSystem(ComponentId pulseId)
    {
        _patch = Patch;
        Descriptor = new SystemDescriptor(
            "extreme-sphere.phase", SystemStage.Movement, 1, 0,
            ComponentMask.FromIds([pulseId]), ComponentMask.FromIds([pulseId]));
    }

    public override SystemDescriptor Descriptor { get; }

    public override void Update(in SystemContext context)
    {
        long start = BeginTiming();
        _deltaSeconds = context.DeltaSeconds;
        int rows = context.PatchChunksParallel(_plan, BlockSize, _patch);
        EndTiming(start, rows);
    }

    private void Patch(ReadOnlySpan<Entity> entities, Span<SpherePulse> pulses)
    {
        float delta = _deltaSeconds;
        for (int row = 0; row < pulses.Length; row++)
        {
            SpherePulse pulse = pulses[row];
            pulse.Phase = SphereWorkload.AdvancePhase(pulse.Phase, pulse.Frequency, delta);
            pulses[row] = pulse;
        }
    }
}

internal sealed class VelocitySystem : TimedSystem
{
    private const int BlockSize = 4_096;
    private readonly World.PatchChunkPlanCache<SpherePosition, SpherePulse, SphereVelocity> _plan = new();
    private readonly PatchChunkAction<SpherePosition, SpherePulse, SphereVelocity> _patch;

    internal VelocitySystem(ComponentId positionId, ComponentId pulseId, ComponentId velocityId)
    {
        _patch = Patch;
        Descriptor = new SystemDescriptor(
            "extreme-sphere.velocity", SystemStage.SpatialIndex, 1, 0,
            ComponentMask.FromIds([positionId, pulseId, velocityId]),
            ComponentMask.FromIds([velocityId]),
            requires: [typeof(PhaseSystem)]);
    }

    public override SystemDescriptor Descriptor { get; }

    public override void Update(in SystemContext context)
    {
        long start = BeginTiming();
        int rows = context.PatchChunksParallel(_plan, BlockSize, _patch);
        EndTiming(start, rows);
    }

    private static void Patch(
        ReadOnlySpan<Entity> entities,
        ReadOnlySpan<SpherePosition> positions,
        ReadOnlySpan<SpherePulse> pulses,
        Span<SphereVelocity> velocities)
    {
        for (int row = 0; row < velocities.Length; row++)
        {
            SpherePosition position = positions[row];
            SpherePulse pulse = pulses[row];
            SphereWorkload.ComputeVelocity(
                position.X, position.Y, position.Z, pulse.Speed, pulse.Phase,
                out float x, out float y, out float z);
            velocities[row] = new SphereVelocity { X = x, Y = y, Z = z };
        }
    }
}

internal sealed class MovementSystem : TimedSystem
{
    private const int BlockSize = 4_096;
    private readonly World.PatchChunkPlanCache<SphereVelocity, SpherePosition> _plan = new();
    private readonly PatchChunkAction<SphereVelocity, SpherePosition> _patch;
    private float _deltaSeconds;

    internal MovementSystem(ComponentId positionId, ComponentId velocityId)
    {
        _patch = Patch;
        Descriptor = new SystemDescriptor(
            "extreme-sphere.movement", SystemStage.Perception, 1, 0,
            ComponentMask.FromIds([positionId, velocityId]),
            ComponentMask.FromIds([positionId]),
            requires: [typeof(VelocitySystem)]);
    }

    public override SystemDescriptor Descriptor { get; }

    public override void Update(in SystemContext context)
    {
        long start = BeginTiming();
        _deltaSeconds = context.DeltaSeconds;
        int rows = context.PatchChunksParallel(_plan, BlockSize, _patch);
        EndTiming(start, rows);
    }

    private void Patch(
        ReadOnlySpan<Entity> entities,
        ReadOnlySpan<SphereVelocity> velocities,
        Span<SpherePosition> positions)
    {
        float delta = _deltaSeconds;
        for (int row = 0; row < positions.Length; row++)
        {
            SpherePosition position = positions[row];
            SphereVelocity velocity = velocities[row];
            SphereWorkload.Integrate(
                ref position.X, ref position.Y, ref position.Z,
                velocity.X, velocity.Y, velocity.Z, delta);
            positions[row] = position;
        }
    }
}
