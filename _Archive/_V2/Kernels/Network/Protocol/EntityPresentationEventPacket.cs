namespace UltraSim.Network;

public readonly record struct EntityPresentationEventPacket(
    ulong EntityId,
    string DisplayName,
    string DefinitionId,
    ulong ServerTick,
    bool IsInCombat = false,
    bool HasHealth = false,
    float HealthFraction = 0.0f) : IPacket<EntityPresentationEventPacket>
{
    public static ushort PacketId => 0x0302;
    public static NetworkPlane Plane => NetworkPlane.State;
    public static PacketClass PacketClass => PacketClass.ReliableEvent;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteUInt64(EntityId);
        writer.WriteString(DisplayName ?? string.Empty);
        writer.WriteString(DefinitionId ?? string.Empty);
        writer.WriteUInt64(ServerTick);
        writer.WriteBool(IsInCombat);
        writer.WriteBool(HasHealth);
        writer.WriteSingle(HealthFraction);
    }

    public static EntityPresentationEventPacket Deserialize(ref BitReader reader)
    {
        return new EntityPresentationEventPacket(
            reader.ReadUInt64(),
            reader.ReadString(),
            reader.ReadString(),
            reader.ReadUInt64(),
            reader.ReadBool(),
            reader.ReadBool(),
            reader.ReadSingle());
    }
}
