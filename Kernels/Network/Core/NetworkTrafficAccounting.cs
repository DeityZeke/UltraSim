namespace UltraSim.Network;

/// <summary>
/// Exact authenticated UDP payload accounting at the secure-protocol boundary.
/// These values are UDP datagram payload lengths; they exclude IP, UDP, link-layer,
/// socket-buffer, HTTP, and content-store bytes.
/// </summary>
public readonly record struct NetworkTransportTrafficSnapshot(
    ulong DataDatagramsSent,
    ulong DataUdpPayloadBytesSent,
    ulong DataDatagramsReceived,
    ulong DataUdpPayloadBytesReceived,
    ulong AcknowledgementDatagramsSent,
    ulong AcknowledgementUdpPayloadBytesSent,
    ulong AcknowledgementDatagramsReceived,
    ulong AcknowledgementUdpPayloadBytesReceived,
    ulong KeepAliveDatagramsSent,
    ulong KeepAliveUdpPayloadBytesSent,
    ulong KeepAliveDatagramsReceived,
    ulong KeepAliveUdpPayloadBytesReceived,
    ulong RetransmittedDataDatagrams,
    ulong RetransmittedDataUdpPayloadBytes)
{
    public NetworkTransportTrafficSnapshot Add(in NetworkTransportTrafficSnapshot other) => new(
        Add(DataDatagramsSent, other.DataDatagramsSent),
        Add(DataUdpPayloadBytesSent, other.DataUdpPayloadBytesSent),
        Add(DataDatagramsReceived, other.DataDatagramsReceived),
        Add(DataUdpPayloadBytesReceived, other.DataUdpPayloadBytesReceived),
        Add(AcknowledgementDatagramsSent, other.AcknowledgementDatagramsSent),
        Add(AcknowledgementUdpPayloadBytesSent, other.AcknowledgementUdpPayloadBytesSent),
        Add(AcknowledgementDatagramsReceived, other.AcknowledgementDatagramsReceived),
        Add(AcknowledgementUdpPayloadBytesReceived, other.AcknowledgementUdpPayloadBytesReceived),
        Add(KeepAliveDatagramsSent, other.KeepAliveDatagramsSent),
        Add(KeepAliveUdpPayloadBytesSent, other.KeepAliveUdpPayloadBytesSent),
        Add(KeepAliveDatagramsReceived, other.KeepAliveDatagramsReceived),
        Add(KeepAliveUdpPayloadBytesReceived, other.KeepAliveUdpPayloadBytesReceived),
        Add(RetransmittedDataDatagrams, other.RetransmittedDataDatagrams),
        Add(RetransmittedDataUdpPayloadBytes, other.RetransmittedDataUdpPayloadBytes));

    public ulong SecureDatagramsSent => SaturatingAdd(
        SaturatingAdd(DataDatagramsSent, AcknowledgementDatagramsSent),
        KeepAliveDatagramsSent);
    public ulong SecureUdpPayloadBytesSent => SaturatingAdd(
        SaturatingAdd(DataUdpPayloadBytesSent, AcknowledgementUdpPayloadBytesSent),
        KeepAliveUdpPayloadBytesSent);
    public ulong SecureDatagramsReceived => SaturatingAdd(
        SaturatingAdd(DataDatagramsReceived, AcknowledgementDatagramsReceived),
        KeepAliveDatagramsReceived);
    public ulong SecureUdpPayloadBytesReceived => SaturatingAdd(
        SaturatingAdd(DataUdpPayloadBytesReceived, AcknowledgementUdpPayloadBytesReceived),
        KeepAliveUdpPayloadBytesReceived);

    internal static NetworkTransportTrafficSnapshot Delta(
        in NetworkTransportTrafficSnapshot start,
        in NetworkTransportTrafficSnapshot end) => new(
        Difference(start.DataDatagramsSent, end.DataDatagramsSent),
        Difference(start.DataUdpPayloadBytesSent, end.DataUdpPayloadBytesSent),
        Difference(start.DataDatagramsReceived, end.DataDatagramsReceived),
        Difference(start.DataUdpPayloadBytesReceived, end.DataUdpPayloadBytesReceived),
        Difference(start.AcknowledgementDatagramsSent, end.AcknowledgementDatagramsSent),
        Difference(start.AcknowledgementUdpPayloadBytesSent, end.AcknowledgementUdpPayloadBytesSent),
        Difference(start.AcknowledgementDatagramsReceived, end.AcknowledgementDatagramsReceived),
        Difference(start.AcknowledgementUdpPayloadBytesReceived, end.AcknowledgementUdpPayloadBytesReceived),
        Difference(start.KeepAliveDatagramsSent, end.KeepAliveDatagramsSent),
        Difference(start.KeepAliveUdpPayloadBytesSent, end.KeepAliveUdpPayloadBytesSent),
        Difference(start.KeepAliveDatagramsReceived, end.KeepAliveDatagramsReceived),
        Difference(start.KeepAliveUdpPayloadBytesReceived, end.KeepAliveUdpPayloadBytesReceived),
        Difference(start.RetransmittedDataDatagrams, end.RetransmittedDataDatagrams),
        Difference(start.RetransmittedDataUdpPayloadBytes, end.RetransmittedDataUdpPayloadBytes));

    private static ulong Difference(ulong start, ulong end) => end >= start
        ? end - start
        : throw new InvalidOperationException("A cumulative Network traffic counter moved backwards.");
    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
    private static ulong Add(ulong left, ulong right) => SaturatingAdd(left, right);
}

/// <summary>
/// Logical production-message accounting. Application payload and data-frame bytes
/// are not physical UDP bytes and must not be added to transport totals.
/// </summary>
public readonly record struct NetworkLogicalTrafficSnapshot(
    ulong MessagesSent,
    ulong ApplicationPayloadBytesSent,
    ulong DataFrameBytesSent,
    ulong MessagesReceived,
    ulong ApplicationPayloadBytesReceived,
    ulong DataFrameBytesReceived,
    ulong RetransmittedDatagrams,
    ulong RetransmittedUdpPayloadBytes)
{
    internal static NetworkLogicalTrafficSnapshot Delta(
        in NetworkLogicalTrafficSnapshot start,
        in NetworkLogicalTrafficSnapshot end) => new(
        Difference(start.MessagesSent, end.MessagesSent),
        Difference(start.ApplicationPayloadBytesSent, end.ApplicationPayloadBytesSent),
        Difference(start.DataFrameBytesSent, end.DataFrameBytesSent),
        Difference(start.MessagesReceived, end.MessagesReceived),
        Difference(start.ApplicationPayloadBytesReceived, end.ApplicationPayloadBytesReceived),
        Difference(start.DataFrameBytesReceived, end.DataFrameBytesReceived),
        Difference(start.RetransmittedDatagrams, end.RetransmittedDatagrams),
        Difference(start.RetransmittedUdpPayloadBytes, end.RetransmittedUdpPayloadBytes));

    public NetworkLogicalTrafficSnapshot Add(in NetworkLogicalTrafficSnapshot other) => new(
        Add(MessagesSent, other.MessagesSent),
        Add(ApplicationPayloadBytesSent, other.ApplicationPayloadBytesSent),
        Add(DataFrameBytesSent, other.DataFrameBytesSent),
        Add(MessagesReceived, other.MessagesReceived),
        Add(ApplicationPayloadBytesReceived, other.ApplicationPayloadBytesReceived),
        Add(DataFrameBytesReceived, other.DataFrameBytesReceived),
        Add(RetransmittedDatagrams, other.RetransmittedDatagrams),
        Add(RetransmittedUdpPayloadBytes, other.RetransmittedUdpPayloadBytes));

    private static ulong Difference(ulong start, ulong end) => end >= start
        ? end - start
        : throw new InvalidOperationException("A cumulative Network traffic counter moved backwards.");
    private static ulong Add(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

/// <summary>One manifest message and its immutable cumulative counters.</summary>
public readonly record struct NetworkMessageTrafficSnapshot(
    NetworkMessageId MessageId,
    NetworkLane Lane,
    NetworkLogicalTrafficSnapshot Traffic);

/// <summary>
/// Cold immutable traffic snapshot for one secure peer lifetime. Snapshot creation
/// copies finite lane/message arrays; packet processing performs only owner-local
/// primitive counter updates.
/// </summary>
public sealed class NetworkTrafficSnapshot
{
    private readonly NetworkLogicalTrafficSnapshot[] _lanes;
    private readonly NetworkMessageTrafficSnapshot[] _messages;

    internal NetworkTrafficSnapshot(
        ushort protocolVersion,
        ulong protocolFingerprint,
        NetworkTransportTrafficSnapshot transport,
        NetworkLogicalTrafficSnapshot[] lanes,
        NetworkMessageTrafficSnapshot[] messages,
        NetworkProtocolStageTiming protocolTiming = default)
    {
        ProtocolVersion = protocolVersion;
        ProtocolFingerprint = protocolFingerprint;
        Transport = transport;
        _lanes = lanes;
        _messages = messages;
        ProtocolTiming = protocolTiming;
    }

    public ushort ProtocolVersion { get; }
    public ulong ProtocolFingerprint { get; }
    public NetworkTransportTrafficSnapshot Transport { get; }
    public NetworkProtocolStageTiming ProtocolTiming { get; }
    public int LaneCount => _lanes.Length;
    public int MessageCount => _messages.Length;

    public NetworkLogicalTrafficSnapshot GetLane(NetworkLane lane)
    {
        if ((uint)lane >= (uint)_lanes.Length)
            throw new ArgumentOutOfRangeException(nameof(lane));
        return _lanes[(int)lane];
    }

    public NetworkMessageTrafficSnapshot GetMessage(int ordinal)
    {
        if ((uint)ordinal >= (uint)_messages.Length)
            throw new ArgumentOutOfRangeException(nameof(ordinal));
        return _messages[ordinal];
    }

    public bool TryGetMessage(NetworkMessageId messageId, out NetworkMessageTrafficSnapshot value)
    {
        int low = 0;
        int high = _messages.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            NetworkMessageTrafficSnapshot candidate = _messages[middle];
            int comparison = candidate.MessageId.CompareTo(messageId);
            if (comparison == 0) { value = candidate; return true; }
            if (comparison < 0) low = middle + 1;
            else high = middle - 1;
        }
        value = default;
        return false;
    }

    public NetworkLogicalTrafficSnapshot SumMessages()
    {
        NetworkLogicalTrafficSnapshot sum = default;
        for (int index = 0; index < _messages.Length; index++)
            sum = sum.Add(_messages[index].Traffic);
        return sum;
    }

    public static NetworkTrafficSnapshot Delta(
        NetworkTrafficSnapshot start,
        NetworkTrafficSnapshot end)
    {
        ArgumentNullException.ThrowIfNull(start);
        ArgumentNullException.ThrowIfNull(end);
        if (start.ProtocolVersion != end.ProtocolVersion ||
            start.ProtocolFingerprint != end.ProtocolFingerprint ||
            start._lanes.Length != end._lanes.Length ||
            start._messages.Length != end._messages.Length)
            throw new ArgumentException("Traffic snapshots do not describe the same protocol manifest.");

        var lanes = new NetworkLogicalTrafficSnapshot[end._lanes.Length];
        for (int index = 0; index < lanes.Length; index++)
            lanes[index] = NetworkLogicalTrafficSnapshot.Delta(start._lanes[index], end._lanes[index]);
        var messages = new NetworkMessageTrafficSnapshot[end._messages.Length];
        for (int index = 0; index < messages.Length; index++)
        {
            NetworkMessageTrafficSnapshot before = start._messages[index];
            NetworkMessageTrafficSnapshot after = end._messages[index];
            if (before.MessageId != after.MessageId || before.Lane != after.Lane)
                throw new ArgumentException("Traffic snapshots contain different message catalogs.");
            messages[index] = after with
            {
                Traffic = NetworkLogicalTrafficSnapshot.Delta(before.Traffic, after.Traffic),
            };
        }
        return new(end.ProtocolVersion, end.ProtocolFingerprint,
            NetworkTransportTrafficSnapshot.Delta(start.Transport, end.Transport),
            lanes, messages,
            NetworkProtocolStageTiming.Delta(start.ProtocolTiming, end.ProtocolTiming));
    }

    public static NetworkTrafficSnapshot Add(
        NetworkTrafficSnapshot left,
        NetworkTrafficSnapshot right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        if (left.ProtocolVersion != right.ProtocolVersion ||
            left.ProtocolFingerprint != right.ProtocolFingerprint ||
            left._lanes.Length != right._lanes.Length ||
            left._messages.Length != right._messages.Length)
            throw new ArgumentException("Traffic snapshots do not describe the same protocol manifest.");
        var lanes = new NetworkLogicalTrafficSnapshot[left._lanes.Length];
        for (int index = 0; index < lanes.Length; index++)
            lanes[index] = left._lanes[index].Add(right._lanes[index]);
        var messages = new NetworkMessageTrafficSnapshot[left._messages.Length];
        for (int index = 0; index < messages.Length; index++)
        {
            NetworkMessageTrafficSnapshot a = left._messages[index];
            NetworkMessageTrafficSnapshot b = right._messages[index];
            if (a.MessageId != b.MessageId || a.Lane != b.Lane)
                throw new ArgumentException("Traffic snapshots contain different message catalogs.");
            messages[index] = a with { Traffic = a.Traffic.Add(b.Traffic) };
        }
        return new(left.ProtocolVersion, left.ProtocolFingerprint,
            left.Transport.Add(right.Transport), lanes, messages,
            left.ProtocolTiming.Add(right.ProtocolTiming));
    }
}

internal struct NetworkLogicalTrafficCounter
{
    internal ulong MessagesSent;
    internal ulong ApplicationPayloadBytesSent;
    internal ulong DataFrameBytesSent;
    internal ulong MessagesReceived;
    internal ulong ApplicationPayloadBytesReceived;
    internal ulong DataFrameBytesReceived;
    internal ulong RetransmittedDatagrams;
    internal ulong RetransmittedUdpPayloadBytes;

    internal readonly NetworkLogicalTrafficSnapshot Snapshot() => new(
        MessagesSent, ApplicationPayloadBytesSent, DataFrameBytesSent,
        MessagesReceived, ApplicationPayloadBytesReceived, DataFrameBytesReceived,
        RetransmittedDatagrams, RetransmittedUdpPayloadBytes);
}
