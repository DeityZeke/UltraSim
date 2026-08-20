// Shared/Network/Core/ChannelType.cs
// Channel types for the hybrid TCP/UDP network layer

namespace Network.Shared.Network
{
    /// <summary>
    /// Defines the delivery guarantees for a packet.
    /// Game logic uses these channels without knowing the underlying protocol.
    /// </summary>
    public enum ChannelType : byte
    {
        /// <summary>
        /// TCP - Guaranteed delivery, ordered.
        /// Use for: Login, inventory, chat, trading, housing, world state.
        /// Highest latency but most reliable.
        /// </summary>
        ReliableOrdered = 0,

        /// <summary>
        /// UDP with ACK - Guaranteed delivery, may arrive out of order.
        /// Use for: Skill activations, important combat events, item pickups.
        /// Lower latency than TCP, still guaranteed.
        /// </summary>
        ReliableUnordered = 1,

        /// <summary>
        /// UDP with sequence number - No guarantee, but ordered (drops stale packets).
        /// Use for: Position updates, animation states, entity updates.
        /// Low latency, automatically discards outdated information.
        /// </summary>
        UnreliableSequenced = 2,

        /// <summary>
        /// Raw UDP - No guarantee, no ordering.
        /// Use for: Particle effects, ambient sounds, damage numbers, non-critical VFX.
        /// Lowest latency, fire and forget.
        /// </summary>
        Unreliable = 3
    }

    /// <summary>
    /// Underlying transport protocol.
    /// </summary>
    public enum TransportProtocol : byte
    {
        TCP = 0,
        UDP = 1
    }

    /// <summary>
    /// Configuration for each channel type.
    /// </summary>
    public readonly struct ChannelConfig
    {
        public readonly TransportProtocol Protocol;
        public readonly bool RequiresAck;
        public readonly bool RequiresOrdering;
        public readonly int MaxRetries;
        public readonly int RetryIntervalMs;
        public readonly int SequenceWindow;

        public ChannelConfig(
            TransportProtocol protocol,
            bool requiresAck,
            bool requiresOrdering,
            int maxRetries = 0,
            int retryIntervalMs = 0,
            int sequenceWindow = 0)
        {
            Protocol = protocol;
            RequiresAck = requiresAck;
            RequiresOrdering = requiresOrdering;
            MaxRetries = maxRetries;
            RetryIntervalMs = retryIntervalMs;
            SequenceWindow = sequenceWindow;
        }

        /// <summary>
        /// Get configuration for a channel type.
        /// </summary>
        public static ChannelConfig Get(ChannelType channel) => channel switch
        {
            ChannelType.ReliableOrdered => new ChannelConfig(
                protocol: TransportProtocol.TCP,
                requiresAck: true,
                requiresOrdering: true),

            ChannelType.ReliableUnordered => new ChannelConfig(
                protocol: TransportProtocol.UDP,
                requiresAck: true,
                requiresOrdering: false,
                maxRetries: 10,
                retryIntervalMs: 100),

            ChannelType.UnreliableSequenced => new ChannelConfig(
                protocol: TransportProtocol.UDP,
                requiresAck: false,
                requiresOrdering: true,
                sequenceWindow: 32),

            ChannelType.Unreliable => new ChannelConfig(
                protocol: TransportProtocol.UDP,
                requiresAck: false,
                requiresOrdering: false),

            _ => throw new ArgumentOutOfRangeException(nameof(channel))
        };
    }
}
