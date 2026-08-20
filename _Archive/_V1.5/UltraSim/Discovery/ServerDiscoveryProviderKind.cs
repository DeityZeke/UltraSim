namespace UltraSim.Discovery
{
    public enum ServerDiscoveryProviderKind : byte
    {
        Official = 0,
        Favorites = 1,
        History = 2,
        Lan = 3,
        Steam = 4,
        Manual = 5,
        Custom = 255,
    }
}
