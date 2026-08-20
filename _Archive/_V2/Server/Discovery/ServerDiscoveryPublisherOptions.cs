namespace UltraSim.Server.Discovery;

public sealed record ServerDiscoveryPublisherOptions(
    bool Enabled = true,
    TimeSpan HeartbeatInterval = default,
    bool SendStartupPresence = true,
    bool SendShutdownPresence = true,
    bool UnpublishOnStop = false)
{
    public TimeSpan EffectiveHeartbeatInterval =>
        HeartbeatInterval <= TimeSpan.Zero ? TimeSpan.FromSeconds(15) : HeartbeatInterval;
}
