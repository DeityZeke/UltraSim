using System.Diagnostics;
using UltraSim.ECS;

namespace UltraSim.ECS.ExtremeSphere.V3;

internal abstract class TimedRuntime<TJob> : ISystemRuntime<TJob>
    where TJob : struct
{
    private long _start;

    internal long LastElapsedTicks { get; private set; }
    internal long LastRows { get; private set; }

    public TJob Prepare(in SystemPrepareContext context)
    {
        _start = Stopwatch.GetTimestamp();
        return CreateJob((float)context.DeltaSeconds);
    }

    public void Complete(in SystemCompleteContext context, ReadOnlySpan<TJob> participantResults)
    {
        long rows = 0;
        for (int index = 0; index < participantResults.Length; index++)
            rows += GetRows(in participantResults[index]);
        LastRows = rows;
        LastElapsedTicks = Stopwatch.GetTimestamp() - _start;
    }

    protected abstract TJob CreateJob(float deltaSeconds);
    protected abstract long GetRows(in TJob job);
}

internal sealed class PhaseRuntime : TimedRuntime<PhaseJob>
{
    protected override PhaseJob CreateJob(float deltaSeconds) => new(deltaSeconds);
    protected override long GetRows(in PhaseJob job) => job.Rows;
}

internal struct PhaseJob : IScheduledWriteChunkJob<SpherePulse>
{
    private readonly float _deltaSeconds;
    internal long Rows;

    internal PhaseJob(float deltaSeconds) => _deltaSeconds = deltaSeconds;

    public void Execute(in SystemJobContext context, in WriteComponentChunk<SpherePulse> chunk)
    {
        Span<SpherePulse> pulses = chunk.WriteComponents;
        for (int row = 0; row < pulses.Length; row++)
        {
            SpherePulse pulse = pulses[row];
            pulse.Phase = SphereWorkload.AdvancePhase(pulse.Phase, pulse.Frequency, _deltaSeconds);
            pulses[row] = pulse;
        }
        Rows += pulses.Length;
    }
}

internal sealed class VelocityRuntime : TimedRuntime<VelocityJob>
{
    protected override VelocityJob CreateJob(float deltaSeconds) => default;
    protected override long GetRows(in VelocityJob job) => job.Rows;
}

internal struct VelocityJob : IScheduledReadReadWriteChunkJob<SpherePosition, SpherePulse, SphereVelocity>
{
    internal long Rows;

    public void Execute(
        in SystemJobContext context,
        in ReadReadWriteComponentChunk<SpherePosition, SpherePulse, SphereVelocity> chunk)
    {
        ReadOnlySpan<SpherePosition> positions = chunk.ReadComponents1;
        ReadOnlySpan<SpherePulse> pulses = chunk.ReadComponents2;
        Span<SphereVelocity> velocities = chunk.WriteComponents;
        for (int row = 0; row < velocities.Length; row++)
        {
            SpherePosition position = positions[row];
            SpherePulse pulse = pulses[row];
            SphereWorkload.ComputeVelocity(
                position.X, position.Y, position.Z, pulse.Speed, pulse.Phase,
                out float x, out float y, out float z);
            velocities[row] = new SphereVelocity { X = x, Y = y, Z = z };
        }
        Rows += velocities.Length;
    }
}

internal sealed class MovementRuntime : TimedRuntime<MovementJob>
{
    protected override MovementJob CreateJob(float deltaSeconds) => new(deltaSeconds);
    protected override long GetRows(in MovementJob job) => job.Rows;
}

internal struct MovementJob : IScheduledReadWriteChunkJob<SphereVelocity, SpherePosition>
{
    private readonly float _deltaSeconds;
    internal long Rows;

    internal MovementJob(float deltaSeconds) => _deltaSeconds = deltaSeconds;

    public void Execute(
        in SystemJobContext context,
        in ReadWriteComponentChunk<SphereVelocity, SpherePosition> chunk)
    {
        ReadOnlySpan<SphereVelocity> velocities = chunk.ReadComponents;
        Span<SpherePosition> positions = chunk.WriteComponents;
        for (int row = 0; row < positions.Length; row++)
        {
            SpherePosition position = positions[row];
            SphereVelocity velocity = velocities[row];
            SphereWorkload.Integrate(
                ref position.X, ref position.Y, ref position.Z,
                velocity.X, velocity.Y, velocity.Z, _deltaSeconds);
            positions[row] = position;
        }
        Rows += positions.Length;
    }
}

internal struct CanonicalJob : IReadReadReadChunkJob<SpherePosition, SphereVelocity, SpherePulse>
{
    internal CanonicalSphereHasher Hasher;
    internal int Rows;

    public void Execute(in ReadReadReadComponentChunk<SpherePosition, SphereVelocity, SpherePulse> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
        {
            SpherePosition position = chunk.ReadComponents1[row];
            SphereVelocity velocity = chunk.ReadComponents2[row];
            SpherePulse pulse = chunk.ReadComponents3[row];
            Hasher.Add(
                chunk.EntityIndices[row],
                position.X, position.Y, position.Z,
                velocity.X, velocity.Y, velocity.Z,
                pulse.Speed, pulse.Frequency, pulse.Phase);
        }
        Rows += chunk.Count;
    }
}
