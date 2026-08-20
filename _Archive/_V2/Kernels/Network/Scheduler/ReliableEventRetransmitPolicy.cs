namespace UltraSim.Network;

public readonly record struct ReliableEventRetransmitPolicy(
    int RetransmitThresholdTicks,
    int MaxRetransmitItemsPerTick)
{
    public static readonly ReliableEventRetransmitPolicy Default = new(
        RetransmitThresholdTicks: 3,
        MaxRetransmitItemsPerTick: 8);
}
