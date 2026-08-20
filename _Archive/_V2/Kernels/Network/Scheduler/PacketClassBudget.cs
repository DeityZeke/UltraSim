namespace UltraSim.Network;

public readonly record struct PacketClassBudget(
    int MaxItems,
    int MaxPayloadBytes)
{
    public static PacketClassBudget Unlimited => new(int.MaxValue, int.MaxValue);
}
