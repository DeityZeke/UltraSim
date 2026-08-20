using System.Net;

namespace UltraSim.Network;

public sealed record UdpSocketNetworkTransportOptions(
    IPEndPoint LocalEndPoint,
    PacketMetadataRegistry PacketMetadata,
    IPEndPoint? DefaultRemoteEndPoint = null,
    ConnectionId DefaultConnectionId = default,
    bool AllowDynamicConnections = true,
    int MaxDatagramBytes = 2048,
    bool EnableAsyncFlush = false,
    bool EnableAsyncReceive = false,
    bool EnablePollProfiling = false,
    int SocketReceiveBufferBytes = 0,
    int SocketSendBufferBytes = 0);
