namespace UltraSim.Network;

/// <summary>
/// Workload class used for budget and backpressure policy.
/// </summary>
public enum PacketClass : byte
{
    Control = 0,
    Realtime = 1,
    ReliableEvent = 2,
    Bulk = 3,
}
