using System.Runtime.CompilerServices;

namespace UltraSim.Network;

public sealed class PacketRegistry<TContext>
{
    private delegate void PacketDispatch(TContext context, ReadOnlySpan<byte> payload);

    private PacketDispatch?[] _dispatch = new PacketDispatch?[64];
    private ChannelType[] _channels = new ChannelType[64];
    private NetworkPlane[] _planes = new NetworkPlane[64];
    private PacketClass[] _classes = new PacketClass[64];
    private readonly Dictionary<Type, ushort> _packetIdsByType = new();
    private readonly object _registrationGate = new();
    private volatile bool _sealed;

    public bool IsSealed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _sealed;
    }

    public void Register<TPacket>(PacketHandler<TContext, TPacket> handler)
        where TPacket : struct, IPacket<TPacket>
    {
        ArgumentNullException.ThrowIfNull(handler);

        lock (_registrationGate)
        {
            if (_sealed)
                throw new InvalidOperationException("Packet registry is sealed.");

            ushort packetId = TPacket.PacketId;
            Type packetType = typeof(TPacket);
            EnsureCapacity(packetId);

            if (_dispatch[packetId] is not null)
                throw new InvalidOperationException($"Packet id 0x{packetId:X4} is already registered.");
            if (_packetIdsByType.ContainsKey(packetType))
                throw new InvalidOperationException($"Packet type already registered: {packetType.FullName}");

            _channels[packetId] = TPacket.Channel;
            _planes[packetId] = TPacket.Plane;
            _classes[packetId] = TPacket.PacketClass;
            _dispatch[packetId] = (context, payload) =>
            {
                var reader = new BitReader(payload);
                TPacket packet = TPacket.Deserialize(ref reader);
                if (reader.Remaining != 0)
                    throw new InvalidOperationException(
                        $"Packet 0x{TPacket.PacketId:X4} left {reader.Remaining} unread payload bytes.");

                handler(context, in packet);
            };

            _packetIdsByType.Add(packetType, packetId);
        }
    }

    public void Seal()
    {
        lock (_registrationGate)
            _sealed = true;
    }

    public bool TryDispatch(TContext context, in PacketEnvelopeHeader header, ReadOnlySpan<byte> payload)
    {
        PacketDispatch?[] dispatch = Volatile.Read(ref _dispatch);
        if ((uint)header.PacketId >= (uint)dispatch.Length)
            return false;

        PacketDispatch? handler = dispatch[header.PacketId];
        if (handler is null)
            return false;

        handler(context, payload);
        return true;
    }

    public bool TryGetPacketId(Type packetType, out ushort packetId)
    {
        ArgumentNullException.ThrowIfNull(packetType);
        return _packetIdsByType.TryGetValue(packetType, out packetId);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ushort GetPacketId<TPacket>()
        where TPacket : struct, IPacket<TPacket> => TPacket.PacketId;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public ChannelType GetChannel(ushort packetId)
    {
        PacketDispatch?[] dispatch = Volatile.Read(ref _dispatch);
        if ((uint)packetId >= (uint)dispatch.Length || dispatch[packetId] is null)
            throw new KeyNotFoundException($"No packet registered for id 0x{packetId:X4}.");

        return Volatile.Read(ref _channels)[packetId];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public NetworkPlane GetPlane(ushort packetId)
    {
        PacketDispatch?[] dispatch = Volatile.Read(ref _dispatch);
        if ((uint)packetId >= (uint)dispatch.Length || dispatch[packetId] is null)
            throw new KeyNotFoundException($"No packet registered for id 0x{packetId:X4}.");

        return Volatile.Read(ref _planes)[packetId];
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public PacketClass GetPacketClass(ushort packetId)
    {
        PacketDispatch?[] dispatch = Volatile.Read(ref _dispatch);
        if ((uint)packetId >= (uint)dispatch.Length || dispatch[packetId] is null)
            throw new KeyNotFoundException($"No packet registered for id 0x{packetId:X4}.");

        return Volatile.Read(ref _classes)[packetId];
    }

    private void EnsureCapacity(int packetId)
    {
        if ((uint)packetId < (uint)_dispatch.Length)
            return;

        int newSize = _dispatch.Length;
        while (packetId >= newSize)
        {
            if (newSize > int.MaxValue / 2)
            {
                newSize = packetId + 1;
                break;
            }

            newSize <<= 1;
        }

        var nextDispatch = new PacketDispatch?[newSize];
        Array.Copy(_dispatch, nextDispatch, _dispatch.Length);

        var nextChannels = new ChannelType[newSize];
        Array.Copy(_channels, nextChannels, _channels.Length);

        var nextPlanes = new NetworkPlane[newSize];
        Array.Copy(_planes, nextPlanes, _planes.Length);

        var nextClasses = new PacketClass[newSize];
        Array.Copy(_classes, nextClasses, _classes.Length);

        Volatile.Write(ref _channels, nextChannels);
        Volatile.Write(ref _planes, nextPlanes);
        Volatile.Write(ref _classes, nextClasses);
        Volatile.Write(ref _dispatch, nextDispatch);
    }
}
