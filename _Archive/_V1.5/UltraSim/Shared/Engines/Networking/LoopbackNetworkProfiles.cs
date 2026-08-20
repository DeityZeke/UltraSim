using UltraSim.Network;

namespace UltraSim.Shared.Engines.Networking
{
    public enum LoopbackNetworkPreset
    {
        Custom = 0,
        Local = 1,
        Lan = 2,
        Wan = 3,
        Harsh = 4,
    }

    public readonly struct LoopbackNetworkPairProfile
    {
        public readonly LoopbackLinkProfile ClientToServer;
        public readonly LoopbackLinkProfile ServerToClient;

        public LoopbackNetworkPairProfile(
            in LoopbackLinkProfile clientToServer,
            in LoopbackLinkProfile serverToClient)
        {
            ClientToServer = clientToServer;
            ServerToClient = serverToClient;
        }
    }

    public static class LoopbackNetworkProfiles
    {
        public static LoopbackNetworkPairProfile Resolve(LoopbackNetworkPreset preset) =>
            preset switch
            {
                LoopbackNetworkPreset.Local => new LoopbackNetworkPairProfile(
                    new LoopbackLinkProfile(baseLatencyMs: 0, jitterMs: 0, dropChance: 0d),
                    new LoopbackLinkProfile(baseLatencyMs: 0, jitterMs: 0, dropChance: 0d)),
                LoopbackNetworkPreset.Lan => new LoopbackNetworkPairProfile(
                    new LoopbackLinkProfile(baseLatencyMs: 2, jitterMs: 1, dropChance: 0d),
                    new LoopbackLinkProfile(baseLatencyMs: 2, jitterMs: 1, dropChance: 0d)),
                LoopbackNetworkPreset.Wan => new LoopbackNetworkPairProfile(
                    new LoopbackLinkProfile(baseLatencyMs: 40, jitterMs: 8, dropChance: 0.003d),
                    new LoopbackLinkProfile(baseLatencyMs: 40, jitterMs: 8, dropChance: 0.003d)),
                LoopbackNetworkPreset.Harsh => new LoopbackNetworkPairProfile(
                    new LoopbackLinkProfile(baseLatencyMs: 90, jitterMs: 20, dropChance: 0.015d),
                    new LoopbackLinkProfile(baseLatencyMs: 90, jitterMs: 20, dropChance: 0.015d)),
                _ => new LoopbackNetworkPairProfile(default, default),
            };
    }
}
