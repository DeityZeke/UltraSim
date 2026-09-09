namespace UltraSim.Network;

/// <summary>
/// Role-neutral lifetime decision shared by scalar and sharded Hosted server
/// compositions. Execution ownership decides how a transition is applied; the
/// policy deciding when it occurs remains identical.
/// </summary>
internal static class HostedUdpSessionLifetimePolicy
{
    internal static HostedUdpSessionLifetimeDecision Evaluate(
        UdpProtocolSessionState state,
        bool applicationRetirementRequested,
        ulong now,
        ulong lastActivity,
        ulong drainDeadline,
        int reliableQueued,
        UdpProtocolLimits limits)
    {
        if (applicationRetirementRequested)
            return HostedUdpSessionLifetimeDecision.Retire;
        if (state == UdpProtocolSessionState.Established &&
            now - lastActivity >= (ulong)limits.IdleTimeoutMilliseconds)
            return HostedUdpSessionLifetimeDecision.BeginDrain;
        if (state == UdpProtocolSessionState.Draining &&
            (reliableQueued == 0 || now >= drainDeadline))
            return HostedUdpSessionLifetimeDecision.Retire;
        return HostedUdpSessionLifetimeDecision.Continue;
    }

    internal static ulong GetDrainDeadline(ulong now, UdpProtocolLimits limits) =>
        checked(now + (ulong)limits.DrainTimeoutMilliseconds);
}

internal enum HostedUdpSessionLifetimeDecision : byte
{
    Continue = 0,
    BeginDrain = 1,
    Retire = 2,
}
