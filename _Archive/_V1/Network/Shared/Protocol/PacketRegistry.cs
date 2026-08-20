// Shared/Network/Protocol/PacketRegistry.cs
// Zero-boxing packet registration and dispatch system

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Network.Shared.Network
{
    /// <summary>
    /// Delegate for handling packets. Receives packet by readonly reference for zero-copy.
    /// </summary>
    /// <typeparam name="TSession">Session type.</typeparam>
    /// <typeparam name="TPacket">Packet type.</typeparam>
    public delegate void PacketHandler<TSession, TPacket>(TSession session, in TPacket packet)
        where TPacket : struct, IPacket;

    /// <summary>
    /// Delegate for raw packet processing (deserialize + dispatch).
    /// </summary>
    /// <typeparam name="TSession">Session type.</typeparam>
    public delegate void PacketProcessor<TSession>(TSession session, ReadOnlySpan<byte> payload);

    /// <summary>
    /// Information about a registered packet type.
    /// </summary>
    public readonly struct PacketInfo
    {
        public readonly ushort PacketId;
        public readonly ChannelType Channel;
        public readonly Type PacketType;
        public readonly string Name;
        public readonly bool FallbackToTcp;
        public readonly int EstimatedSize;

        public PacketInfo(ushort packetId, ChannelType channel, Type packetType, bool fallbackToTcp, int estimatedSize)
        {
            PacketId = packetId;
            Channel = channel;
            PacketType = packetType;
            Name = packetType.Name;
            FallbackToTcp = fallbackToTcp;
            EstimatedSize = estimatedSize;
        }

        public bool IsValid => PacketType != null;
    }

    /// <summary>
    /// Generic packet registry. Type parameter allows different registries for client/server.
    /// Zero-boxing through generic constraints and delegates.
    /// </summary>
    /// <typeparam name="TSession">The session type for handlers.</typeparam>
    public sealed class PacketRegistry<TSession>
    {
        // Indexed by packet ID for O(1) lookup
        private readonly PacketProcessor<TSession>?[] _processors;
        private readonly PacketInfo[] _packetInfo;
        private readonly object _lock = new();

        // For debugging/introspection
        private readonly ConcurrentDictionary<ushort, string> _registeredNames = new();

        public PacketRegistry()
        {
            _processors = new PacketProcessor<TSession>?[ushort.MaxValue + 1];
            _packetInfo = new PacketInfo[ushort.MaxValue + 1];
        }

        /// <summary>
        /// Register a packet type with its handler.
        /// Uses generic constraints to ensure zero-boxing at runtime.
        /// </summary>
        /// <typeparam name="TPacket">The packet struct type.</typeparam>
        /// <param name="handler">Handler called when packet is received.</param>
        public void Register<TPacket>(PacketHandler<TSession, TPacket> handler)
            where TPacket : struct, IPacket<TPacket>
        {
            ushort packetId = TPacket.PacketId;
            ChannelType channel = TPacket.Channel;

            // Check for attributes
            bool fallbackToTcp = typeof(TPacket).GetCustomAttribute<FallbackToTcpAttribute>() != null;
            int estimatedSize = typeof(TPacket).GetCustomAttribute<EstimatedSizeAttribute>()?.Bytes
                ?? NetworkConstants.DefaultPoolBufferSize;

            lock (_lock)
            {
                if (_processors[packetId] != null)
                {
                    throw new InvalidOperationException(
                        $"Packet 0x{packetId:X4} already registered as {_registeredNames[packetId]}");
                }

                // Create processor closure - JIT will specialize for TPacket
                _processors[packetId] = (session, payload) =>
                {
                    var reader = new BitReader(payload);
                    var packet = TPacket.Deserialize(ref reader);  // Stack allocated
                    handler(session, in packet);                    // Passed by ref
                };

                _packetInfo[packetId] = new PacketInfo(
                    packetId,
                    channel,
                    typeof(TPacket),
                    fallbackToTcp,
                    estimatedSize
                );

                _registeredNames[packetId] = typeof(TPacket).Name;
            }

#if DEBUG
            Console.WriteLine($"[PacketRegistry] Registered 0x{packetId:X4} ({typeof(TPacket).Name}) on {channel}");
#endif
        }

        /// <summary>
        /// Dispatch an incoming packet to its registered handler.
        /// </summary>
        /// <param name="session">The session that received the packet.</param>
        /// <param name="packetId">The packet identifier.</param>
        /// <param name="payload">The packet payload (without header).</param>
        /// <returns>True if packet was handled, false if no handler registered.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Dispatch(TSession session, ushort packetId, ReadOnlySpan<byte> payload)
        {
            var processor = _processors[packetId];
            if (processor == null)
                return false;

            processor(session, payload);
            return true;
        }

        /// <summary>
        /// Check if a handler is registered for a packet ID.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasHandler(ushort packetId)
        {
            return _processors[packetId] != null;
        }

        /// <summary>
        /// Get packet info for a registered packet ID.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public PacketInfo GetPacketInfo(ushort packetId)
        {
            return _packetInfo[packetId];
        }

        /// <summary>
        /// Get channel type for a packet ID.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChannelType GetChannel(ushort packetId)
        {
            return _packetInfo[packetId].Channel;
        }

        /// <summary>
        /// Unregister a packet handler.
        /// </summary>
        public bool Unregister(ushort packetId)
        {
            lock (_lock)
            {
                if (_processors[packetId] == null)
                    return false;

                _processors[packetId] = null;
                _packetInfo[packetId] = default;
                _registeredNames.TryRemove(packetId, out _);
                return true;
            }
        }

        /// <summary>
        /// Get all registered packet IDs.
        /// </summary>
        public IEnumerable<ushort> GetRegisteredIds()
        {
            return _registeredNames.Keys;
        }

        /// <summary>
        /// Get all registered packet names for debugging.
        /// </summary>
        public IReadOnlyDictionary<ushort, string> GetRegisteredPackets()
        {
            return _registeredNames;
        }

        /// <summary>
        /// Auto-register all packets from an assembly using reflection.
        /// Looks for static Register methods on packet types.
        /// </summary>
        public void RegisterFromAssembly(Assembly assembly)
        {
            var packetInterface = typeof(IPacket<>);

            foreach (var type in assembly.GetTypes())
            {
                if (!type.IsValueType || type.IsAbstract)
                    continue;

                // Check if type implements IPacket<T>
                var interfaces = type.GetInterfaces();
                bool isPacket = interfaces.Any(i =>
                    i.IsGenericType &&
                    i.GetGenericTypeDefinition() == packetInterface &&
                    i.GetGenericArguments()[0] == type);

                if (!isPacket)
                    continue;

                // Look for static RegisterHandler method
                var registerMethod = type.GetMethod("RegisterHandler",
                    BindingFlags.Static | BindingFlags.Public);

                if (registerMethod != null)
                {
                    try
                    {
                        registerMethod.Invoke(null, new object[] { this });
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"[PacketRegistry] Failed to register {type.Name}: {ex.Message}");
                    }
                }
            }
        }
    }

    /// <summary>
    /// Extension methods for sending packets from sessions.
    /// </summary>
    public static class PacketSendExtensions
    {
        /// <summary>
        /// Serialize a packet to a buffer. Returns bytes written.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Serialize<TPacket>(in TPacket packet, Span<byte> buffer)
            where TPacket : struct, IPacket
        {
            var writer = new BitWriter(buffer);
            packet.Serialize(ref writer);
            return writer.BytesWritten;
        }

        /// <summary>
        /// Get the packet ID for a packet type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ushort GetPacketId<TPacket>() where TPacket : struct, IPacket
        {
            return TPacket.PacketId;
        }

        /// <summary>
        /// Get the channel for a packet type.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ChannelType GetChannel<TPacket>() where TPacket : struct, IPacket
        {
            return TPacket.Channel;
        }
    }
}
