namespace UltraSim.Network;

public readonly record struct ConnectionSendBudget(
    PacketClassBudget Realtime,
    PacketClassBudget ReliableEvent,
    PacketClassBudget Bulk)
{
    public static ConnectionSendBudget Unlimited => new(
        PacketClassBudget.Unlimited,
        PacketClassBudget.Unlimited,
        PacketClassBudget.Unlimited);
}
