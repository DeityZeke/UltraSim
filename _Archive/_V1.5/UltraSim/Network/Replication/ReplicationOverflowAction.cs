namespace UltraSim.Network
{
    public enum ReplicationOverflowAction : byte
    {
        Drop = 0,
        Defer = 1,
        Coalesce = 2,
    }
}
