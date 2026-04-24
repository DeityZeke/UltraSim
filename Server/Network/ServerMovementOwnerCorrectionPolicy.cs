namespace UltraSim.Server.Network;

public readonly record struct ServerMovementOwnerCorrectionPolicy
{
    public static ServerMovementOwnerCorrectionPolicy Default => new(
        positionDeadband: 0f,
        maxSilentTicks: 1);

    public ServerMovementOwnerCorrectionPolicy(float positionDeadband, uint maxSilentTicks, uint phaseCadenceTicks = 1)
    {
        if (float.IsNaN(positionDeadband) || float.IsInfinity(positionDeadband) || positionDeadband < 0f)
            throw new ArgumentOutOfRangeException(nameof(positionDeadband));
        if (maxSilentTicks == 0)
            throw new ArgumentOutOfRangeException(nameof(maxSilentTicks));
        if (phaseCadenceTicks == 0)
            throw new ArgumentOutOfRangeException(nameof(phaseCadenceTicks));

        PositionDeadband = positionDeadband;
        MaxSilentTicks = maxSilentTicks;
        PhaseCadenceTicks = phaseCadenceTicks;
    }

    public float PositionDeadband { get; init; }
    public uint MaxSilentTicks { get; init; }
    public uint PhaseCadenceTicks { get; init; }
}
