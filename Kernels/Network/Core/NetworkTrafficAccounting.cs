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

/// <summary>
/// Bounded sent application-payload size distribution for one manifest message.
/// Bucket counts are exclusive. The derived less-than-or-equal counts are
/// cumulative and therefore must not be added to one another.
/// </summary>
public readonly record struct NetworkMessagePayloadSizeSnapshot(
    ulong Count,
    ulong PayloadBytes,
    ulong Bytes0To128,
    ulong Bytes129To256,
    ulong Bytes257To512,
    ulong Bytes513To1024,
    ulong BytesOver1024,
    int MaximumPayloadBytes,
    bool MaximumIsExact)
{
    public ulong CountLessThanOrEqual128 => Bytes0To128;
    public ulong CountLessThanOrEqual256 => Add(Bytes0To128, Bytes129To256);
    public ulong CountLessThanOrEqual512 => Add(CountLessThanOrEqual256, Bytes257To512);
    public ulong CountLessThanOrEqual1024 => Add(CountLessThanOrEqual512, Bytes513To1024);
    public double AveragePayloadBytes => Count == 0 ? 0d : PayloadBytes / (double)Count;
    public int P50PayloadBytesUpperBound => PercentileUpperBound(50);
    public int P95PayloadBytesUpperBound => PercentileUpperBound(95);
    public int P99PayloadBytesUpperBound => PercentileUpperBound(99);

    internal static NetworkMessagePayloadSizeSnapshot Delta(
        in NetworkMessagePayloadSizeSnapshot start,
        in NetworkMessagePayloadSizeSnapshot end)
    {
        ulong count = Difference(start.Count, end.Count);
        ulong zeroTo128 = Difference(start.Bytes0To128, end.Bytes0To128);
        ulong oneTwentyNineTo256 = Difference(start.Bytes129To256, end.Bytes129To256);
        ulong twoFiftySevenTo512 = Difference(start.Bytes257To512, end.Bytes257To512);
        ulong fiveThirteenTo1024 = Difference(start.Bytes513To1024, end.Bytes513To1024);
        ulong over1024 = Difference(start.BytesOver1024, end.BytesOver1024);
        if (count != Add(Add(zeroTo128, oneTwentyNineTo256),
                Add(Add(twoFiftySevenTo512, fiveThirteenTo1024), over1024)))
            throw new InvalidOperationException("Network message payload-size buckets do not reconcile.");

        int maximum;
        bool maximumIsExact;
        if (count == 0)
        {
            maximum = 0;
            maximumIsExact = true;
        }
        else if (start.Count == 0 || end.MaximumPayloadBytes > start.MaximumPayloadBytes)
        {
            maximum = end.MaximumPayloadBytes;
            maximumIsExact = end.MaximumIsExact;
        }
        else
        {
            maximum = HighestNonEmptyBucketUpperBound(
                zeroTo128, oneTwentyNineTo256, twoFiftySevenTo512,
                fiveThirteenTo1024, over1024, end.MaximumPayloadBytes);
            maximumIsExact = false;
        }

        return new(count,
            Difference(start.PayloadBytes, end.PayloadBytes),
            zeroTo128, oneTwentyNineTo256, twoFiftySevenTo512,
            fiveThirteenTo1024, over1024, maximum, maximumIsExact);
    }

    public NetworkMessagePayloadSizeSnapshot Add(
        in NetworkMessagePayloadSizeSnapshot other) => new(
        Add(Count, other.Count),
        Add(PayloadBytes, other.PayloadBytes),
        Add(Bytes0To128, other.Bytes0To128),
        Add(Bytes129To256, other.Bytes129To256),
        Add(Bytes257To512, other.Bytes257To512),
        Add(Bytes513To1024, other.Bytes513To1024),
        Add(BytesOver1024, other.BytesOver1024),
        Math.Max(MaximumPayloadBytes, other.MaximumPayloadBytes),
        MaximumIsExact && other.MaximumIsExact);

    private int PercentileUpperBound(int percentile)
    {
        if (Count == 0) return 0;
        ulong rank = checked((Count * (ulong)percentile + 99UL) / 100UL);
        ulong cumulative = Bytes0To128;
        if (rank <= cumulative) return 128;
        cumulative = Add(cumulative, Bytes129To256);
        if (rank <= cumulative) return 256;
        cumulative = Add(cumulative, Bytes257To512);
        if (rank <= cumulative) return 512;
        cumulative = Add(cumulative, Bytes513To1024);
        if (rank <= cumulative) return 1024;
        return MaximumPayloadBytes;
    }

    private static int HighestNonEmptyBucketUpperBound(
        ulong zeroTo128,
        ulong oneTwentyNineTo256,
        ulong twoFiftySevenTo512,
        ulong fiveThirteenTo1024,
        ulong over1024,
        int cumulativeMaximum) =>
        over1024 != 0 ? cumulativeMaximum :
        fiveThirteenTo1024 != 0 ? 1024 :
        twoFiftySevenTo512 != 0 ? 512 :
        oneTwentyNineTo256 != 0 ? 256 :
        zeroTo128 != 0 ? 128 : 0;

    private static ulong Difference(ulong start, ulong end) => end >= start
        ? end - start
        : throw new InvalidOperationException("A cumulative Network payload-size counter moved backwards.");
    private static ulong Add(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}

/// <summary>One manifest message and its immutable cumulative counters.</summary>
public readonly record struct NetworkMessageTrafficSnapshot(
    NetworkMessageId MessageId,
    NetworkLane Lane,
    NetworkLogicalTrafficSnapshot Traffic,
    NetworkMessagePayloadSizeSnapshot SentPayloadSizes = default);

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
        NetworkProtocolStageTiming protocolTiming = default,
        NetworkUnreliableAggregationSnapshot unreliableAggregation = default,
        NetworkUnreliableOpportunitySnapshot unreliableOpportunity = default)
    {
        ProtocolVersion = protocolVersion;
        ProtocolFingerprint = protocolFingerprint;
        Transport = transport;
        _lanes = lanes;
        _messages = messages;
        ProtocolTiming = protocolTiming;
        UnreliableAggregation = unreliableAggregation;
        UnreliableOpportunity = unreliableOpportunity;
    }

    public ushort ProtocolVersion { get; }
    public ulong ProtocolFingerprint { get; }
    public NetworkTransportTrafficSnapshot Transport { get; }
    public NetworkProtocolStageTiming ProtocolTiming { get; }
    public NetworkUnreliableAggregationSnapshot UnreliableAggregation { get; }
    public NetworkUnreliableOpportunitySnapshot UnreliableOpportunity { get; }
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
                SentPayloadSizes = NetworkMessagePayloadSizeSnapshot.Delta(
                    before.SentPayloadSizes, after.SentPayloadSizes),
            };
        }
        return new(end.ProtocolVersion, end.ProtocolFingerprint,
            NetworkTransportTrafficSnapshot.Delta(start.Transport, end.Transport),
            lanes, messages,
            NetworkProtocolStageTiming.Delta(start.ProtocolTiming, end.ProtocolTiming),
            NetworkUnreliableAggregationSnapshot.Delta(
                start.UnreliableAggregation, end.UnreliableAggregation),
            NetworkUnreliableOpportunitySnapshot.Delta(
                start.UnreliableOpportunity, end.UnreliableOpportunity));
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
            messages[index] = a with
            {
                Traffic = a.Traffic.Add(b.Traffic),
                SentPayloadSizes = a.SentPayloadSizes.Add(b.SentPayloadSizes),
            };
        }
        return new(left.ProtocolVersion, left.ProtocolFingerprint,
            left.Transport.Add(right.Transport), lanes, messages,
            left.ProtocolTiming.Add(right.ProtocolTiming),
            left.UnreliableAggregation.Add(right.UnreliableAggregation),
            left.UnreliableOpportunity.Add(right.UnreliableOpportunity));
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

internal struct NetworkMessagePayloadSizeCounter
{
    internal ulong Count;
    internal ulong PayloadBytes;
    internal ulong Bytes0To128;
    internal ulong Bytes129To256;
    internal ulong Bytes257To512;
    internal ulong Bytes513To1024;
    internal ulong BytesOver1024;
    internal int MaximumPayloadBytes;

    internal void Record(int payloadBytes)
    {
        Count = SaturatingIncrement(Count);
        PayloadBytes = SaturatingAdd(PayloadBytes, checked((ulong)payloadBytes));
        if (payloadBytes <= 128) Bytes0To128 = SaturatingIncrement(Bytes0To128);
        else if (payloadBytes <= 256) Bytes129To256 = SaturatingIncrement(Bytes129To256);
        else if (payloadBytes <= 512) Bytes257To512 = SaturatingIncrement(Bytes257To512);
        else if (payloadBytes <= 1024) Bytes513To1024 = SaturatingIncrement(Bytes513To1024);
        else BytesOver1024 = SaturatingIncrement(BytesOver1024);
        MaximumPayloadBytes = Math.Max(MaximumPayloadBytes, payloadBytes);
    }

    internal readonly NetworkMessagePayloadSizeSnapshot Snapshot() => new(
        Count, PayloadBytes, Bytes0To128, Bytes129To256, Bytes257To512,
        Bytes513To1024, BytesOver1024, MaximumPayloadBytes,
        MaximumIsExact: true);

    private static ulong SaturatingIncrement(ulong value) =>
        value == ulong.MaxValue ? value : value + 1;
    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}
