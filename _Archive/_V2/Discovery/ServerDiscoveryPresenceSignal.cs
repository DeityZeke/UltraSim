namespace UltraSim.Discovery
{
    public enum ServerDiscoveryPresenceSignal : byte
    {
        Startup = 0,
        Online = 1,
        Offline = 2,
        Shutdown = 3,
    }
}
