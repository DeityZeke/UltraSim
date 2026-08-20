namespace UltraSim.Network
{
    public interface IPacket
    {
        static abstract ushort PacketId { get; }
        static abstract ChannelType Channel { get; }

        void Serialize(ref BitWriter writer);
    }

    public interface IPacket<TSelf> : IPacket where TSelf : struct, IPacket<TSelf>
    {
        static abstract TSelf Deserialize(ref BitReader reader);
    }

    public delegate void PacketHandler<TSession, TPacket>(TSession session, in TPacket packet)
        where TPacket : struct, IPacket<TPacket>;
}

