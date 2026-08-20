using System.Runtime.CompilerServices;

namespace UltraSim.Network;

/// <summary>
/// Lightweight packet-id metadata table used by transports that must classify inbound payloads
/// before higher-layer registries decode the typed packet bodies.
/// </summary>
public sealed class PacketMetadataRegistry
{
    private ChannelType[] _channels = new ChannelType[64];
    private NetworkPlane[] _planes = new NetworkPlane[64];
    private PacketClass[] _classes = new PacketClass[64];
    private bool[] _registered = new bool[64];
    private readonly object _registrationGate = new();
    private volatile bool _sealed;

    public bool IsSealed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _sealed;
    }

    public void Register<TPacket>()
        where TPacket : struct, IPacket<TPacket>
    {
        lock (_registrationGate)
        {
            if (_sealed)
                throw new InvalidOperationException("Packet metadata registry is sealed.");

            ushort packetId = TPacket.PacketId;
            EnsureCapacity(packetId);

            if (_registered[packetId])
                throw new InvalidOperationException($"Packet id 0x{packetId:X4} is already registered.");

            _channels[packetId] = TPacket.Channel;
            _planes[packetId] = TPacket.Plane;
            _classes[packetId] = TPacket.PacketClass;
            _registered[packetId] = true;
        }
    }

    public void Seal()
    {
        lock (_registrationGate)
            _sealed = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public bool TryGet(ushort packetId, out NetworkPlane plane, out PacketClass packetClass, out ChannelType channel)
    {
        bool[] registered = Volatile.Read(ref _registered);
        if ((uint)packetId >= (uint)registered.Length || !registered[packetId])
        {
            plane = default;
            packetClass = default;
            channel = default;
            return false;
        }

        plane = Volatile.Read(ref _planes)[packetId];
        packetClass = Volatile.Read(ref _classes)[packetId];
        channel = Volatile.Read(ref _channels)[packetId];
        return true;
    }

    private void EnsureCapacity(int packetId)
    {
        if ((uint)packetId < (uint)_registered.Length)
            return;

        int newSize = _registered.Length;
        while (packetId >= newSize)
        {
            if (newSize > int.MaxValue / 2)
            {
                newSize = packetId + 1;
                break;
            }

            newSize <<= 1;
        }

        var nextRegistered = new bool[newSize];
        Array.Copy(_registered, nextRegistered, _registered.Length);

        var nextChannels = new ChannelType[newSize];
        Array.Copy(_channels, nextChannels, _channels.Length);

        var nextPlanes = new NetworkPlane[newSize];
        Array.Copy(_planes, nextPlanes, _planes.Length);

        var nextClasses = new PacketClass[newSize];
        Array.Copy(_classes, nextClasses, _classes.Length);

        Volatile.Write(ref _channels, nextChannels);
        Volatile.Write(ref _planes, nextPlanes);
        Volatile.Write(ref _classes, nextClasses);
        Volatile.Write(ref _registered, nextRegistered);
    }
}
