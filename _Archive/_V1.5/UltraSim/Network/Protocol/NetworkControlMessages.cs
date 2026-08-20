using UltraSim.Network;

namespace UltraSim.Network.Protocol
{
    public readonly record struct ServerChatMessage(
        byte Scope,
        NetworkEntityId SenderEntityId,
        string Text);

    public readonly record struct ProbePongMessage(
        ushort Sequence,
        long ClientTick);

    public readonly record struct ServerNameplateMessage(
        NetworkEntityId EntityId,
        string PrimaryText);
}
