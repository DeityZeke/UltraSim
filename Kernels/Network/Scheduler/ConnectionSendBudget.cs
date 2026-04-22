namespace UltraSim.Network;

public readonly record struct ConnectionSendBudget(
    PacketClassBudget Realtime,
    PacketClassBudget ReliableEvent)
{
    public static ConnectionSendBudget Unlimited => new(
        PacketClassBudget.Unlimited,
        PacketClassBudget.Unlimited);
}
