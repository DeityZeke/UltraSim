using System.Diagnostics;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Server.ConceptDemo;
using UltraSim.Server.Hosting;

internal sealed record DirectConnectTrafficRateDistribution(
    double AverageBytesPerSecond,
    double P50BytesPerSecond,
    double P95BytesPerSecond,
    double MaximumBytesPerSecond,
    double AverageDatagramsPerSecond,
    double P50DatagramsPerSecond,
    double P95DatagramsPerSecond,
    double MaximumDatagramsPerSecond);

internal sealed record DirectConnectConnectionTrafficReport(
    uint ConnectionSlot,
    uint ConnectionGeneration,
    bool Finalized,
    UdpPayloadTrafficSnapshot UdpPayload,
    NetworkTransportTrafficSnapshot SecureProtocol,
    NetworkProtocolSnapshot Protocol);

internal sealed record DirectConnectLaneTrafficReport(
    string Lane,
    NetworkLogicalTrafficSnapshot Traffic);

internal sealed record DirectConnectFamilyTrafficReport(
    string Family,
    NetworkLogicalTrafficSnapshot Traffic);

internal sealed record DirectConnectMessageTrafficReport(
    ushort MessageId,
    string CanonicalKey,
    ushort Version,
    string Lane,
    string Delivery,
    string Retention,
    NetworkLogicalTrafficSnapshot Traffic);

internal sealed record DirectConnectHttpBodyTrafficReport(
    ulong Requests,
    ulong Served,
    ulong Rejected,
    long ResponseBodyBytes);

internal sealed record DirectConnectPublicationTrafficReport(
    ulong CommitCount,
    ulong PublishedChunkRevisions,
    ulong HistoryInvalidations,
    ulong SnapshotReads,
    ulong DeltaReads,
    ulong SnapshotFallbacks);

internal sealed record DirectConnectWireAccountingSummary(
    string PhysicalMetric,
    string HttpMetric,
    long DurationMilliseconds,
    ulong AuthorityTicks,
    int ParticipantsAtStart,
    int ParticipantsAtEnd,
    int ConnectionsAtStart,
    int ConnectionsAtEnd,
    int ConnectionLifetimes,
    UdpPayloadTrafficSnapshot ListenerUdpPayload,
    UdpPayloadTrafficSnapshot EstablishedConnectionUdpPayload,
    UdpPayloadTrafficSnapshot PreConnectionOrUnattributedUdpPayload,
    NetworkTransportTrafficSnapshot SecureProtocol,
    DirectConnectTrafficRateDistribution ServerToClientPerConnection,
    DirectConnectTrafficRateDistribution ClientToServerPerConnection,
    DirectConnectConnectionTrafficReport[] Connections,
    DirectConnectLaneTrafficReport[] Lanes,
    DirectConnectFamilyTrafficReport[] Families,
    DirectConnectMessageTrafficReport[] Messages,
    DirectConnectHttpBodyTrafficReport HttpBody,
    HostedUdpHostTelemetrySnapshot NetworkTiming,
    GameTickTimingSnapshot AuthorityTiming,
    DirectConnectPublicationTrafficReport AuthoritativePublications,
    RemoteGuestPlayableServerSnapshot[] RemoteApplications,
    double ProcessCpuMilliseconds,
    double CpuSecondsPerWallSecond,
    long ProcessWorkingSetHighWaterBytes,
    long ManagedHeapHighWaterBytes,
    long AllocatedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    int ThreadHighWater,
    bool PhysicalTransportReconciled,
    bool LogicalMessageReconciled);

/// <summary>
/// Cold snapshot-delta collector for explicit PF2 measurement windows. The
/// protocol hot path owns only primitive per-peer counters; this collector may
/// allocate and aggregate on the Direct Connect owner thread.
/// </summary>
internal sealed class DirectConnectWireAccounting
{
    private readonly Dictionary<ConnectionId, HostedUdpServerConnectionTrafficSnapshot> _start = [];
    private readonly HashSet<ConnectionId> _retiredBeforeStart = [];
    private readonly UdpPayloadTrafficSnapshot _listenerStart;
    private readonly StaticContentHttpHostSnapshot _httpStart;
    private readonly ulong _tickStart;
    private readonly int _participantsStart;
    private readonly int _connectionsStart;
    private readonly ChunkPublicationStoreSnapshot _publicationStart;
    private readonly long _started;
    private readonly Process _process;
    private readonly double _cpuStart;
    private readonly long _allocatedStart;
    private readonly int _gen0Start;
    private readonly int _gen1Start;
    private readonly int _gen2Start;
    private long _workingSetHighWater;
    private long _managedHeapHighWater;
    private int _threadHighWater;
    private long _nextResourceSample;

    internal DirectConnectWireAccounting(
        HostedUdpAdmissionHost host,
        SharedPlayableAuthority authority,
        ConceptDemoContentDeployment content)
    {
        ArgumentNullException.ThrowIfNull(host);
        ArgumentNullException.ThrowIfNull(authority);
        ArgumentNullException.ThrowIfNull(content);
        host.ResetTelemetryWindow();
        authority.ResetTelemetryWindow();
        HostedUdpAdmissionSnapshot hostSnapshot = host.GetSnapshot();
        SharedPlayableAuthoritySnapshot authoritySnapshot = authority.GetSnapshot();
        _listenerStart = ListenerTraffic(hostSnapshot.Listener);
        _httpStart = content.GetHttpSnapshot();
        _tickStart = authoritySnapshot.SimulationTick;
        _participantsStart = authoritySnapshot.ParticipantCount;
        _connectionsStart = hostSnapshot.EstablishedSessions;
        _publicationStart = authority.GetPublicationSnapshot();
        CaptureActive(host, _start);
        for (int ordinal = 0; host.TryGetRetiredTraffic(ordinal, out var retired); ordinal++)
            _retiredBeforeStart.Add(retired.Connection);
        _process = Process.GetCurrentProcess();
        _process.Refresh();
        _cpuStart = _process.TotalProcessorTime.TotalMilliseconds;
        _allocatedStart = GC.GetTotalAllocatedBytes(precise: false);
        _gen0Start = GC.CollectionCount(0);
        _gen1Start = GC.CollectionCount(1);
        _gen2Start = GC.CollectionCount(2);
        _workingSetHighWater = _process.WorkingSet64;
        _managedHeapHighWater = GC.GetGCMemoryInfo().HeapSizeBytes;
        _threadHighWater = _process.Threads.Count;
        _started = Stopwatch.GetTimestamp();
        _nextResourceSample = _started;
    }

    internal void Sample(bool force = false)
    {
        long now = Stopwatch.GetTimestamp();
        if (!force && now < _nextResourceSample) return;
        _process.Refresh();
        _workingSetHighWater = Math.Max(_workingSetHighWater, _process.WorkingSet64);
        _managedHeapHighWater = Math.Max(
            _managedHeapHighWater, GC.GetGCMemoryInfo().HeapSizeBytes);
        _threadHighWater = Math.Max(_threadHighWater, _process.Threads.Count);
        _nextResourceSample = checked(now + Math.Max(1, Stopwatch.Frequency / 4));
    }

    internal DirectConnectWireAccountingSummary Complete(
        HostedUdpAdmissionHost host,
        SharedPlayableAuthority authority,
        ConceptDemoContentDeployment content)
    {
        Sample(force: true);
        long completed = Stopwatch.GetTimestamp();
        long durationMilliseconds = Math.Max(1,
            checked((long)((Int128)(completed - _started) * 1_000 / Stopwatch.Frequency)));
        double seconds = durationMilliseconds / 1_000d;
        HostedUdpAdmissionSnapshot hostEnd = host.GetSnapshot();
        SharedPlayableAuthoritySnapshot authorityEnd = authority.GetSnapshot();
        ChunkPublicationStoreSnapshot publicationEnd = authority.GetPublicationSnapshot();

        var end = new Dictionary<ConnectionId, HostedUdpServerConnectionTrafficSnapshot>();
        for (int ordinal = 0; host.TryGetRetiredTraffic(ordinal, out var retired); ordinal++)
            end[retired.Connection] = retired;
        CaptureActive(host, end);

        var reports = new List<DirectConnectConnectionTrafficReport>(end.Count);
        NetworkTrafficSnapshot? secureAggregate = null;
        UdpPayloadTrafficSnapshot establishedAggregate = default;
        var sentBytes = new List<double>(end.Count);
        var receivedBytes = new List<double>(end.Count);
        var sentDatagrams = new List<double>(end.Count);
        var receivedDatagrams = new List<double>(end.Count);
        foreach ((ConnectionId id, HostedUdpServerConnectionTrafficSnapshot after) in end)
        {
            if (_retiredBeforeStart.Contains(id)) continue;
            UdpPayloadTrafficSnapshot physical;
            NetworkTrafficSnapshot secure;
            if (_start.TryGetValue(id, out HostedUdpServerConnectionTrafficSnapshot before))
            {
                physical = UdpPayloadTrafficSnapshot.Delta(before.UdpPayload, after.UdpPayload);
                secure = NetworkTrafficSnapshot.Delta(before.SecureProtocol, after.SecureProtocol);
            }
            else
            {
                physical = after.UdpPayload;
                secure = after.SecureProtocol;
            }
            establishedAggregate = establishedAggregate.Add(physical);
            secureAggregate = secureAggregate is null
                ? secure
                : NetworkTrafficSnapshot.Add(secureAggregate, secure);
            reports.Add(new(id.Slot, id.Generation, after.IsFinalized,
                physical, secure.Transport, after.Protocol));
            sentBytes.Add(physical.PayloadBytesSent / seconds);
            receivedBytes.Add(physical.PayloadBytesReceived / seconds);
            sentDatagrams.Add(physical.DatagramsSent / seconds);
            receivedDatagrams.Add(physical.DatagramsReceived / seconds);
        }

        UdpPayloadTrafficSnapshot listener = UdpPayloadTrafficSnapshot.Delta(
            _listenerStart, ListenerTraffic(hostEnd.Listener));
        UdpPayloadTrafficSnapshot residual = Residual(listener, establishedAggregate);
        NetworkTransportTrafficSnapshot secureTransport =
            secureAggregate?.Transport ?? default;
        bool physicalReconciled =
            listener.PayloadBytesSent >= establishedAggregate.PayloadBytesSent &&
            listener.PayloadBytesReceived >= establishedAggregate.PayloadBytesReceived &&
            listener.DatagramsSent >= establishedAggregate.DatagramsSent &&
            listener.DatagramsReceived >= establishedAggregate.DatagramsReceived &&
            establishedAggregate.PayloadBytesSent >= secureTransport.SecureUdpPayloadBytesSent &&
            establishedAggregate.PayloadBytesReceived >= secureTransport.SecureUdpPayloadBytesReceived;

        var lanes = new DirectConnectLaneTrafficReport[NetworkLaneContracts.Count];
        NetworkLogicalTrafficSnapshot laneSum = default;
        for (int index = 0; index < lanes.Length; index++)
        {
            NetworkLogicalTrafficSnapshot traffic = secureAggregate?.GetLane((NetworkLane)index) ?? default;
            lanes[index] = new(((NetworkLane)index).ToString(), traffic);
            laneSum = laneSum.Add(traffic);
        }
        NetworkLogicalTrafficSnapshot messageSum = secureAggregate?.SumMessages() ?? default;
        var families = new DirectConnectFamilyTrafficReport[
            GameProtocolTrafficClassification.FamilyCount];
        NetworkLogicalTrafficSnapshot familySum = default;
        if (secureAggregate is not null)
        {
            var classified = new GameProtocolTrafficFamilySnapshot[families.Length];
            GameProtocolTrafficClassification.Aggregate(secureAggregate, classified);
            for (int index = 0; index < families.Length; index++)
            {
                families[index] = new(classified[index].Family.ToString(), classified[index].Traffic);
                familySum = familySum.Add(classified[index].Traffic);
            }
        }
        else
        {
            for (int index = 0; index < families.Length; index++)
                families[index] = new(((GameProtocolTrafficFamily)index).ToString(), default);
        }

        ReadOnlySpan<NetworkMessageDescriptor> descriptors = UltraSimGameProtocol.Manifest.Messages;
        var messages = new DirectConnectMessageTrafficReport[descriptors.Length];
        for (int index = 0; index < messages.Length; index++)
        {
            NetworkMessageDescriptor descriptor = descriptors[index];
            NetworkLaneContract lane = NetworkLaneContracts.Get(descriptor.Lane);
            messages[index] = new DirectConnectMessageTrafficReport(
                descriptor.Id.Value,
                descriptor.CanonicalKey,
                descriptor.Version,
                descriptor.Lane.ToString(),
                lane.Delivery.ToString(),
                lane.Retention.ToString(),
                secureAggregate?.GetMessage(index).Traffic ?? default);
        }

        StaticContentHttpHostSnapshot httpEnd = content.GetHttpSnapshot();
        var applicationSnapshots = new RemoteGuestPlayableServerSnapshot[
            authorityEnd.MaximumParticipants];
        int applicationCount = authority.CopyRemoteSessionSnapshots(applicationSnapshots);
        if (applicationCount < 0)
            throw new InvalidOperationException(
                "The bounded remote-application diagnostic destination was too small.");
        Array.Resize(ref applicationSnapshots, applicationCount);
        _process.Refresh();
        double cpuMilliseconds = Math.Max(
            0, _process.TotalProcessorTime.TotalMilliseconds - _cpuStart);
        return new(
            "Exact UDP datagram payload bytes at the socket boundary; excludes IP/UDP/link framing",
            "HTTP response body bytes; excludes HTTP/TCP/IP framing",
            durationMilliseconds,
            Difference(_tickStart, authorityEnd.SimulationTick),
            _participantsStart,
            authorityEnd.ParticipantCount,
            _connectionsStart,
            hostEnd.EstablishedSessions,
            reports.Count,
            listener,
            establishedAggregate,
            residual,
            secureTransport,
            Distribution(sentBytes, sentDatagrams),
            Distribution(receivedBytes, receivedDatagrams),
            reports.ToArray(),
            lanes,
            families,
            messages,
            new(
                Difference(_httpStart.Requests, httpEnd.Requests),
                Difference(_httpStart.Served, httpEnd.Served),
                Difference(_httpStart.Rejected, httpEnd.Rejected),
                Difference(_httpStart.BytesServed, httpEnd.BytesServed)),
            host.GetTelemetrySnapshot(),
            authorityEnd.Timing,
            new DirectConnectPublicationTrafficReport(
                Difference(_publicationStart.CommitCount, publicationEnd.CommitCount),
                Difference(_publicationStart.PublishedChunkRevisions,
                    publicationEnd.PublishedChunkRevisions),
                Difference(_publicationStart.HistoryInvalidations,
                    publicationEnd.HistoryInvalidations),
                Difference(_publicationStart.SnapshotReads, publicationEnd.SnapshotReads),
                Difference(_publicationStart.DeltaReads, publicationEnd.DeltaReads),
                Difference(_publicationStart.SnapshotFallbacks,
                    publicationEnd.SnapshotFallbacks)),
            applicationSnapshots,
            cpuMilliseconds,
            cpuMilliseconds / durationMilliseconds,
            _workingSetHighWater,
            _managedHeapHighWater,
            Math.Max(0, GC.GetTotalAllocatedBytes(precise: false) - _allocatedStart),
            Math.Max(0, GC.CollectionCount(0) - _gen0Start),
            Math.Max(0, GC.CollectionCount(1) - _gen1Start),
            Math.Max(0, GC.CollectionCount(2) - _gen2Start),
            _threadHighWater,
            physicalReconciled,
            laneSum == messageSum && familySum == messageSum);
    }

    private static void CaptureActive(
        HostedUdpAdmissionHost host,
        Dictionary<ConnectionId, HostedUdpServerConnectionTrafficSnapshot> destination)
    {
        for (int ordinal = 0; host.TryGetSession(ordinal, out HostedUdpProtocolSession? session); ordinal++)
        {
            HostedUdpServerConnectionTrafficSnapshot value = session!.GetTrafficSnapshot();
            destination[value.Connection] = value;
        }
    }

    private static UdpPayloadTrafficSnapshot ListenerTraffic(in SharedUdpListenerSnapshot value) =>
        new(value.Sent, value.Received, value.SentBytes, value.ReceivedBytes);

    private static UdpPayloadTrafficSnapshot Residual(
        in UdpPayloadTrafficSnapshot physical,
        in UdpPayloadTrafficSnapshot attributed) => new(
        Subtract(attributed.DatagramsSent, physical.DatagramsSent),
        Subtract(attributed.DatagramsReceived, physical.DatagramsReceived),
        Subtract(attributed.PayloadBytesSent, physical.PayloadBytesSent),
        Subtract(attributed.PayloadBytesReceived, physical.PayloadBytesReceived));

    private static ulong Subtract(ulong classified, ulong physical) =>
        physical >= classified ? physical - classified : 0;

    private static ulong Difference(ulong start, ulong end) => end >= start
        ? end - start
        : throw new InvalidOperationException("A cumulative server counter moved backwards.");

    private static long Difference(long start, long end) => end >= start
        ? end - start
        : throw new InvalidOperationException("A cumulative HTTP counter moved backwards.");

    private static DirectConnectTrafficRateDistribution Distribution(
        List<double> bytes,
        List<double> datagrams)
    {
        if (bytes.Count == 0) return new(0, 0, 0, 0, 0, 0, 0, 0);
        bytes.Sort();
        datagrams.Sort();
        return new(
            bytes.Average(), Percentile(bytes, 0.50), Percentile(bytes, 0.95), bytes[^1],
            datagrams.Average(), Percentile(datagrams, 0.50),
            Percentile(datagrams, 0.95), datagrams[^1]);
    }

    private static double Percentile(List<double> sorted, double percentile)
    {
        int index = Math.Clamp(
            checked((int)Math.Ceiling(sorted.Count * percentile)) - 1,
            0, sorted.Count - 1);
        return sorted[index];
    }
}
