// Shared/Network/Core/IPacket.cs
// Zero-boxing packet interfaces using static abstract members (C# 11+)

namespace Network.Shared.Network
{
    /// <summary>
    /// Base interface for all packets.
    /// Uses static abstract members for compile-time packet metadata.
    /// </summary>
    public interface IPacket
    {
        /// <summary>
        /// Unique packet identifier. Static per type, no instance needed.
        /// </summary>
        static abstract ushort PacketId { get; }

        /// <summary>
        /// Network channel for this packet type. Determines delivery guarantees.
        /// </summary>
        static abstract ChannelType Channel { get; }

        /// <summary>
        /// Serialize this packet to the writer.
        /// </summary>
        void Serialize(ref BitWriter writer);
    }

    /// <summary>
    /// Packet that can be deserialized. Self-referential generic for type safety.
    /// </summary>
    /// <typeparam name="TSelf">The implementing struct type.</typeparam>
    public interface IPacket<TSelf> : IPacket where TSelf : struct, IPacket<TSelf>
    {
        /// <summary>
        /// Deserialize a packet from the reader. Returns new instance on stack.
        /// </summary>
        static abstract TSelf Deserialize(ref BitReader reader);
    }

    /// <summary>
    /// Marker interface for server-to-client packets.
    /// </summary>
    public interface IServerPacket : IPacket { }

    /// <summary>
    /// Marker interface for client-to-server packets.
    /// </summary>
    public interface IClientPacket : IPacket { }

    /// <summary>
    /// Marker interface for bidirectional packets.
    /// </summary>
    public interface IBidirectionalPacket : IServerPacket, IClientPacket { }

    /// <summary>
    /// Attribute to mark packets that can fallback to TCP if UDP is unavailable.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class FallbackToTcpAttribute : Attribute { }

    /// <summary>
    /// Attribute to specify an estimated packet size for buffer allocation hints.
    /// </summary>
    [AttributeUsage(AttributeTargets.Struct)]
    public sealed class EstimatedSizeAttribute : Attribute
    {
        public int Bytes { get; }

        public EstimatedSizeAttribute(int bytes)
        {
            Bytes = bytes;
        }
    }
}
