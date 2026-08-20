namespace UltraSim.Network
{
    public enum SessionLifecycleEventKind : byte
    {
        Created = 0,
        StateChanged = 1,
        Removed = 2,
        Rejected = 3,
    }
}
