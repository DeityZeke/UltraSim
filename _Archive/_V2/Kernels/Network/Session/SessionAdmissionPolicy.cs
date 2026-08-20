namespace UltraSim.Network;

public readonly record struct SessionAdmissionPolicy(
    DuplicateSessionPolicy DuplicateSessionPolicy,
    int MaxActiveSessions,
    long AuthTimeoutTicks,
    long KeepAliveTimeoutTicks)
{
    public static SessionAdmissionPolicy Default => new(
        DuplicateSessionPolicy.RejectNew,
        MaxActiveSessions: 0,
        AuthTimeoutTicks: 0,
        KeepAliveTimeoutTicks: 0);
}
