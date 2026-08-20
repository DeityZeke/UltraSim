namespace UltraSim.Network;

public readonly record struct LoginCredentialSubmitPacket(
    string PanelInstanceId,
    string ActionId,
    string Username,
    LoginCredentialMaterialKind MaterialKind,
    byte[] MaterialBytes) : IPacket<LoginCredentialSubmitPacket>
{
    public static ushort PacketId => 0x0402;
    public static NetworkPlane Plane => NetworkPlane.Input;
    public static PacketClass PacketClass => PacketClass.ReliableEvent;
    public static ChannelType Channel => ChannelType.ReliableOrdered;

    public void Serialize(ref BitWriter writer)
    {
        writer.WriteString(PanelInstanceId ?? string.Empty);
        writer.WriteString(ActionId ?? string.Empty);
        writer.WriteString(Username ?? string.Empty);
        writer.WriteByte((byte)MaterialKind);
        ReadOnlySpan<byte> material = MaterialBytes ?? [];
        writer.WriteUInt16(checked((ushort)material.Length));
        writer.WriteBytes(material);
    }

    public static LoginCredentialSubmitPacket Deserialize(ref BitReader reader)
    {
        string panelInstanceId = reader.ReadString();
        string actionId = reader.ReadString();
        string username = reader.ReadString();
        var materialKind = (LoginCredentialMaterialKind)reader.ReadByte();
        ushort materialLength = reader.ReadUInt16();
        return new LoginCredentialSubmitPacket(
            panelInstanceId,
            actionId,
            username,
            materialKind,
            reader.ReadBytes(materialLength).ToArray());
    }
}
