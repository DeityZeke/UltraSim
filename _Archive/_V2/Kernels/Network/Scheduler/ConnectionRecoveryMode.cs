namespace UltraSim.Network;

public enum ConnectionRecoveryMode : byte
{
    Scheduled = 0,
    Baseline = 1,
    Retransmit = 2,
}
