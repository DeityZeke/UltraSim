namespace UltraSim.Discovery
{
    public enum ServerListingHealthState : byte
    {
        Pending = 0,
        Online = 1,
        Stale = 2,
        Offline = 3,
    }
}
