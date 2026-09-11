using UltraSim.Client.Content;
using UltraSim.Game.Protocol;
using UltraSim.Network;

internal sealed record BotRateDistribution(
    double AverageBytesPerSecond,
    double P50BytesPerSecond,
    double P95BytesPerSecond,
    double MaximumBytesPerSecond,
    double AverageDatagramsPerSecond,
    double P50DatagramsPerSecond,
    double P95DatagramsPerSecond,
    double MaximumDatagramsPerSecond);

internal sealed record BotLaneTrafficReport(
    string Lane,
    NetworkLogicalTrafficSnapshot Traffic);

internal sealed record BotFamilyTrafficReport(
    string Family,
    NetworkLogicalTrafficSnapshot Traffic);

internal sealed record BotMessageTrafficReport(
    ushort MessageId,
    string CanonicalKey,
    ushort Version,
    string Lane,
    string Delivery,
    string Retention,
    NetworkLogicalTrafficSnapshot Traffic,
    double AverageSentApplicationBytes,
    double AverageReceivedApplicationBytes);

internal sealed record BotHttpBodyTrafficReport(
    ulong Requests,
    ulong Completed,
    ulong Rejected,
    long ArchiveBodyBytes,
    long ManifestBodyBytes,
    long BlobBodyBytes)
{
    internal static BotHttpBodyTrafficReport Delta(
        in ContentPackageSourceSnapshot start,
        in ContentPackageSourceSnapshot end) => new(
        Difference(start.Requests, end.Requests),
        Difference(start.Completed, end.Completed),
        Difference(start.Rejected, end.Rejected),
        Difference(start.ArchiveBytesRead, end.ArchiveBytesRead),
        Difference(start.ManifestBytesRead, end.ManifestBytesRead),
        Difference(start.PayloadBytesRead, end.PayloadBytesRead));

    private static ulong Difference(ulong start, ulong end) => end >= start
        ? end - start
        : throw new InvalidOperationException("An HTTP source counter moved backwards.");
    private static long Difference(long start, long end) => end >= start
        ? end - start
        : throw new InvalidOperationException("An HTTP source byte counter moved backwards.");
}

internal sealed record BotCacheTrafficReport(
    ulong Installs,
    ulong ReusedBlobs,
    int BlobCountAtEnd,
    long BlobBytesAtEnd)
{
    internal static BotCacheTrafficReport Delta(
        in ContentCacheSnapshot start,
        in ContentCacheSnapshot end) => new(
        Difference(start.Installs, end.Installs),
        Difference(start.ReusedBlobs, end.ReusedBlobs),
        end.BlobCount,
        end.BlobBytes);

    private static ulong Difference(ulong start, ulong end) => end >= start
        ? end - start
        : throw new InvalidOperationException("A content-cache counter moved backwards.");
}

internal sealed record BotWireMeasurementSummary
{
    public required long DurationMilliseconds { get; init; }
    public required ulong ClientToServerUdpPayloadBytes { get; init; }
    public required ulong ServerToClientUdpPayloadBytes { get; init; }
    public required ulong ClientToServerDatagrams { get; init; }
    public required ulong ServerToClientDatagrams { get; init; }
    public required double AggregateClientToServerBytesPerSecond { get; init; }
    public required double AggregateServerToClientBytesPerSecond { get; init; }
    public required double AggregateClientToServerDatagramsPerSecond { get; init; }
    public required double AggregateServerToClientDatagramsPerSecond { get; init; }
    public required BotRateDistribution ClientToServerPerClient { get; init; }
    public required BotRateDistribution ServerToClientPerClient { get; init; }
    public required double PeakOneSecondClientToServerBytesPerSecond { get; init; }
    public required double P95OneSecondClientToServerBytesPerSecond { get; init; }
    public required double PeakOneSecondServerToClientBytesPerSecond { get; init; }
    public required double P95OneSecondServerToClientBytesPerSecond { get; init; }
    public required int OneSecondSamples { get; init; }
    public required bool OneSecondSamplesSaturated { get; init; }
    public required ulong EstimatedIpv4UdpHeaderBytes { get; init; }
    public required NetworkTransportTrafficSnapshot SecureProtocol { get; init; }
    public required ulong NonSecureOrUnattributedUdpPayloadBytesSent { get; init; }
    public required ulong NonSecureOrUnattributedUdpPayloadBytesReceived { get; init; }
    public required BotLaneTrafficReport[] Lanes { get; init; }
    public required BotFamilyTrafficReport[] Families { get; init; }
    public required BotMessageTrafficReport[] Messages { get; init; }
    public required BotHttpBodyTrafficReport HttpBody { get; init; }
    public required BotCacheTrafficReport ContentCache { get; init; }
    public required int ApplicationLifetimesAtStart { get; init; }
    public required int ApplicationLifetimesAtEnd { get; init; }
    public required bool PhysicalTransportReconciled { get; init; }
    public required bool LogicalMessageReconciled { get; init; }
}

internal sealed class BotWireMeasurement
{
    private const int MaximumOneSecondSamples = 4_096;
    private readonly BotCumulativeTrafficSnapshot[] _start;
    private readonly BotCumulativeTrafficSnapshot[] _lastSample;
    private readonly double[] _clientToServerSamples;
    private readonly double[] _serverToClientSamples;
    private readonly ContentPackageSourceSnapshot _sourceStart;
    private readonly ContentCacheSnapshot _cacheStart;
    private readonly long _started;
    private long _lastSampleAt;
    private long _nextSampleAt;
    private int _sampleCount;
    private bool _sampleSaturated;

    internal BotWireMeasurement(
        BotClient[] bots,
        HttpContentPackageSource source,
        ContentCache cache,
        int requestedDurationMilliseconds,
        long started)
    {
        ArgumentNullException.ThrowIfNull(bots);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(cache);
        if (bots.Length == 0) throw new ArgumentException("At least one bot is required.", nameof(bots));
        _start = Capture(bots);
        _lastSample = (BotCumulativeTrafficSnapshot[])_start.Clone();
        int sampleCapacity = Math.Min(MaximumOneSecondSamples,
            Math.Max(2, requestedDurationMilliseconds / 1_000 + 2));
        _clientToServerSamples = new double[sampleCapacity];
        _serverToClientSamples = new double[sampleCapacity];
        _sourceStart = source.GetSnapshot();
        _cacheStart = cache.GetSnapshot();
        _started = started;
        _lastSampleAt = started;
        _nextSampleAt = BotMonotonicClock.AddMilliseconds(started, 1_000);
    }

    internal void Sample(BotClient[] bots, long now)
    {
        if (now < _nextSampleAt) return;
        BotCumulativeTrafficSnapshot[] current = Capture(bots);
        double seconds = Math.Max(0.001d,
            BotMonotonicClock.TicksToFloatingMilliseconds(now - _lastSampleAt) / 1_000d);
        ulong sent = 0;
        ulong received = 0;
        for (int index = 0; index < current.Length; index++)
        {
            UdpPayloadTrafficSnapshot delta = UdpPayloadTrafficSnapshot.Delta(
                _lastSample[index].UdpPayload, current[index].UdpPayload);
            sent = Add(sent, delta.PayloadBytesSent);
            received = Add(received, delta.PayloadBytesReceived);
            _lastSample[index] = current[index];
        }
        if (_sampleCount < _clientToServerSamples.Length)
        {
            _clientToServerSamples[_sampleCount] = sent / seconds;
            _serverToClientSamples[_sampleCount] = received / seconds;
            _sampleCount++;
        }
        else _sampleSaturated = true;
        _lastSampleAt = now;
        _nextSampleAt = BotMonotonicClock.AddMilliseconds(now, 1_000);
    }

    internal BotWireMeasurementSummary Complete(
        BotClient[] bots,
        HttpContentPackageSource source,
        ContentCache cache,
        long completed)
    {
        Sample(bots, completed);
        BotCumulativeTrafficSnapshot[] end = Capture(bots);
        long durationMilliseconds = Math.Max(1,
            BotMonotonicClock.TicksToMilliseconds(completed - _started));
        double seconds = durationMilliseconds / 1_000d;
        var sentBytes = new double[bots.Length];
        var receivedBytes = new double[bots.Length];
        var sentDatagrams = new double[bots.Length];
        var receivedDatagrams = new double[bots.Length];
        ulong totalSentBytes = 0;
        ulong totalReceivedBytes = 0;
        ulong totalSentDatagrams = 0;
        ulong totalReceivedDatagrams = 0;
        NetworkTrafficSnapshot? secure = null;
        int startLifetimes = 0;
        int endLifetimes = 0;
        for (int index = 0; index < end.Length; index++)
        {
            UdpPayloadTrafficSnapshot physical = UdpPayloadTrafficSnapshot.Delta(
                _start[index].UdpPayload, end[index].UdpPayload);
            totalSentBytes = Add(totalSentBytes, physical.PayloadBytesSent);
            totalReceivedBytes = Add(totalReceivedBytes, physical.PayloadBytesReceived);
            totalSentDatagrams = Add(totalSentDatagrams, physical.DatagramsSent);
            totalReceivedDatagrams = Add(totalReceivedDatagrams, physical.DatagramsReceived);
            sentBytes[index] = physical.PayloadBytesSent / seconds;
            receivedBytes[index] = physical.PayloadBytesReceived / seconds;
            sentDatagrams[index] = physical.DatagramsSent / seconds;
            receivedDatagrams[index] = physical.DatagramsReceived / seconds;
            startLifetimes += _start[index].ApplicationLifetimes;
            endLifetimes += end[index].ApplicationLifetimes;
            if (_start[index].SecureProtocol is null || end[index].SecureProtocol is null)
                throw new InvalidOperationException("An active measured bot has no secure traffic snapshot.");
            NetworkTrafficSnapshot item = NetworkTrafficSnapshot.Delta(
                _start[index].SecureProtocol!, end[index].SecureProtocol!);
            secure = secure is null ? item : NetworkTrafficSnapshot.Add(secure, item);
        }
        NetworkTrafficSnapshot aggregate = secure!;
        NetworkTransportTrafficSnapshot transport = aggregate.Transport;
        bool physicalReconciled =
            totalSentBytes >= transport.SecureUdpPayloadBytesSent &&
            totalReceivedBytes >= transport.SecureUdpPayloadBytesReceived &&
            transport.SecureUdpPayloadBytesSent ==
                transport.DataUdpPayloadBytesSent +
                transport.AcknowledgementUdpPayloadBytesSent +
                transport.KeepAliveUdpPayloadBytesSent &&
            transport.SecureUdpPayloadBytesReceived ==
                transport.DataUdpPayloadBytesReceived +
                transport.AcknowledgementUdpPayloadBytesReceived +
                transport.KeepAliveUdpPayloadBytesReceived;

        var lanes = new BotLaneTrafficReport[NetworkLaneContracts.Count];
        NetworkLogicalTrafficSnapshot laneSum = default;
        for (int index = 0; index < lanes.Length; index++)
        {
            NetworkLogicalTrafficSnapshot traffic = aggregate.GetLane((NetworkLane)index);
            lanes[index] = new(((NetworkLane)index).ToString(), traffic);
            laneSum = laneSum.Add(traffic);
        }
        NetworkLogicalTrafficSnapshot messageSum = aggregate.SumMessages();
        bool logicalReconciled = laneSum == messageSum;

        var classified = new GameProtocolTrafficFamilySnapshot[
            GameProtocolTrafficClassification.FamilyCount];
        GameProtocolTrafficClassification.Aggregate(aggregate, classified);
        var families = new BotFamilyTrafficReport[classified.Length];
        NetworkLogicalTrafficSnapshot familySum = default;
        for (int index = 0; index < classified.Length; index++)
        {
            families[index] = new(classified[index].Family.ToString(), classified[index].Traffic);
            familySum = familySum.Add(classified[index].Traffic);
        }
        logicalReconciled &= familySum == messageSum;

        ReadOnlySpan<NetworkMessageDescriptor> descriptors = UltraSimGameProtocol.Manifest.Messages;
        var messages = new BotMessageTrafficReport[descriptors.Length];
        for (int index = 0; index < messages.Length; index++)
        {
            NetworkMessageDescriptor descriptor = descriptors[index];
            NetworkLogicalTrafficSnapshot traffic = aggregate.GetMessage(index).Traffic;
            NetworkLaneContract lane = NetworkLaneContracts.Get(descriptor.Lane);
            messages[index] = new BotMessageTrafficReport(
                descriptor.Id.Value,
                descriptor.CanonicalKey,
                descriptor.Version,
                descriptor.Lane.ToString(),
                lane.Delivery.ToString(),
                lane.Retention.ToString(),
                traffic,
                traffic.MessagesSent == 0 ? 0 :
                    (double)traffic.ApplicationPayloadBytesSent / traffic.MessagesSent,
                traffic.MessagesReceived == 0 ? 0 :
                    (double)traffic.ApplicationPayloadBytesReceived / traffic.MessagesReceived);
        }

        return new BotWireMeasurementSummary
        {
            DurationMilliseconds = durationMilliseconds,
            ClientToServerUdpPayloadBytes = totalSentBytes,
            ServerToClientUdpPayloadBytes = totalReceivedBytes,
            ClientToServerDatagrams = totalSentDatagrams,
            ServerToClientDatagrams = totalReceivedDatagrams,
            AggregateClientToServerBytesPerSecond = totalSentBytes / seconds,
            AggregateServerToClientBytesPerSecond = totalReceivedBytes / seconds,
            AggregateClientToServerDatagramsPerSecond = totalSentDatagrams / seconds,
            AggregateServerToClientDatagramsPerSecond = totalReceivedDatagrams / seconds,
            ClientToServerPerClient = Distribution(sentBytes, sentDatagrams),
            ServerToClientPerClient = Distribution(receivedBytes, receivedDatagrams),
            PeakOneSecondClientToServerBytesPerSecond = Maximum(_clientToServerSamples, _sampleCount),
            P95OneSecondClientToServerBytesPerSecond = Percentile(
                _clientToServerSamples, _sampleCount, 0.95),
            PeakOneSecondServerToClientBytesPerSecond = Maximum(_serverToClientSamples, _sampleCount),
            P95OneSecondServerToClientBytesPerSecond = Percentile(
                _serverToClientSamples, _sampleCount, 0.95),
            OneSecondSamples = _sampleCount,
            OneSecondSamplesSaturated = _sampleSaturated,
            EstimatedIpv4UdpHeaderBytes = checked(
                Add(totalSentDatagrams, totalReceivedDatagrams) * 28UL),
            SecureProtocol = transport,
            NonSecureOrUnattributedUdpPayloadBytesSent =
                totalSentBytes - transport.SecureUdpPayloadBytesSent,
            NonSecureOrUnattributedUdpPayloadBytesReceived =
                totalReceivedBytes - transport.SecureUdpPayloadBytesReceived,
            Lanes = lanes,
            Families = families,
            Messages = messages,
            HttpBody = BotHttpBodyTrafficReport.Delta(_sourceStart, source.GetSnapshot()),
            ContentCache = BotCacheTrafficReport.Delta(_cacheStart, cache.GetSnapshot()),
            ApplicationLifetimesAtStart = startLifetimes,
            ApplicationLifetimesAtEnd = endLifetimes,
            PhysicalTransportReconciled = physicalReconciled,
            LogicalMessageReconciled = logicalReconciled,
        };
    }

    private static BotCumulativeTrafficSnapshot[] Capture(BotClient[] bots)
    {
        var snapshots = new BotCumulativeTrafficSnapshot[bots.Length];
        for (int index = 0; index < bots.Length; index++)
            snapshots[index] = bots[index].GetCumulativeTrafficSnapshot();
        return snapshots;
    }

    private static BotRateDistribution Distribution(double[] bytes, double[] datagrams)
    {
        Array.Sort(bytes);
        Array.Sort(datagrams);
        return new(
            bytes.Average(), PercentileSorted(bytes, 0.50), PercentileSorted(bytes, 0.95), bytes[^1],
            datagrams.Average(), PercentileSorted(datagrams, 0.50),
            PercentileSorted(datagrams, 0.95), datagrams[^1]);
    }

    private static double Percentile(double[] values, int count, double percentile)
    {
        if (count == 0) return 0;
        double[] copy = values.AsSpan(0, count).ToArray();
        Array.Sort(copy);
        return PercentileSorted(copy, percentile);
    }

    private static double PercentileSorted(double[] sorted, double percentile)
    {
        if (sorted.Length == 0) return 0;
        int index = Math.Clamp(
            checked((int)Math.Ceiling(sorted.Length * percentile)) - 1,
            0, sorted.Length - 1);
        return sorted[index];
    }

    private static double Maximum(double[] values, int count)
    {
        double maximum = 0;
        for (int index = 0; index < count; index++) maximum = Math.Max(maximum, values[index]);
        return maximum;
    }

    private static ulong Add(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;
}
