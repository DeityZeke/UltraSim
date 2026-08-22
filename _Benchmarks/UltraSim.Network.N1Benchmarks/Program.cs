using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using UltraSim.ECS;
using UltraSim.Game.Composition;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Network;
using UltraSim.Spatial;
using static BenchmarkHarness;

int warmups = ReadInt(args, "--warmups=", 4);
int samples = ReadInt(args, "--samples=", 15);
int rounds = ReadInt(args, "--rounds=", 4);
string output = ReadString(args, "--output=") ?? Path.GetFullPath(Path.Combine(
    AppContext.BaseDirectory, "../../../../results/network-n1.2-independent-chunk-delivery.json"));
if (warmups < 1 || samples < 5 || rounds < 1) throw new ArgumentOutOfRangeException(nameof(args));

int[] populations = [1, 32, 128, 256];
var scales = new ScaleEvidence[populations.Length];
for (int index = 0; index < populations.Length; index++)
{
    using var fixture = new ScaleFixture(populations[index]);
    scales[index] = fixture.Measure(warmups, samples, rounds);
    Console.WriteLine(
        $"recipients={scales[index].Recipients} subscriptions={scales[index].SubscriptionsPerRecipient} " +
        $"reserved={scales[index].TotalRecipientReservedBytes} " +
        $"stable_p95={scales[index].StableReconcile.P95NanosecondsPerRecipient:F3} ns/recipient " +
        $"cross_p95={scales[index].CrossingReconcile.P95NanosecondsPerRecipient:F3} ns/recipient " +
        $"cross_changes={scales[index].CrossingChangesPerRecipient}");
}

using var cadence = new CadenceFixture();
TimingEvidence cadenceTiming = cadence.Measure(warmups, samples, rounds);
OfflineDirectChunkRecipientSnapshot cadenceSnapshot = cadence.Recipient.GetSnapshot();
Console.WriteLine(
    $"cadence_pass_p95={cadenceTiming.P95NanosecondsPerRecipient:F3} ns/pass " +
    $"subscriptions={cadenceSnapshot.SubscriptionCount} snapshots={cadenceSnapshot.SnapshotReads} " +
    $"absent={cadenceSnapshot.ChunkAbsentReads} no_change={cadenceSnapshot.NoChangeReads}");

var codecs = new CodecFixture();
CodecEvidence codecEvidence = codecs.Measure(warmups, samples, checked(rounds * 8));
Console.WriteLine(
    $"snapshot_bytes={codecEvidence.SnapshotBytes} " +
    $"snapshot_encode_p95={codecEvidence.SnapshotEncode.P95NanosecondsPerRecipient:F3} ns " +
    $"snapshot_decode_p95={codecEvidence.SnapshotDecode.P95NanosecondsPerRecipient:F3} ns " +
    $"delta_bytes={codecEvidence.DeltaBytes} " +
    $"delta_encode_p95={codecEvidence.DeltaEncode.P95NanosecondsPerRecipient:F3} ns " +
    $"delta_decode_p95={codecEvidence.DeltaDecode.P95NanosecondsPerRecipient:F3} ns");

const int defaultUdpMaximumDatagramBytes = 1_472;
TransportEvidence transportEvidence;
Require(ProtocolLoopback.TryCreate(
    NetworkProtocolLimits.Default,
    new DeterministicDatagramLimits(2_048, defaultUdpMaximumDatagramBytes),
    default,
    out ProtocolLoopback? transportLoopback,
    out _,
    UltraSimGameProtocol.Manifest));
ProtocolLoopback establishedTransport = transportLoopback ??
    throw new InvalidOperationException("N1.2 transport evidence loopback was not created.");
using (establishedTransport)
{
    var transport = new RemoteChunkWorldStateServer(
        establishedTransport.Server,
        cadence.Recipient,
        cadence.Source,
        applicationSessionEpoch: 77,
        limits: new RemoteChunkWorldStateTransportLimits(128));
    RemoteChunkWorldStateServerSnapshot snapshot = transport.GetSnapshot();
    transportEvidence = new TransportEvidence(
        defaultUdpMaximumDatagramBytes,
        snapshot.MaximumUnfragmentedMessageBytes,
        snapshot.MaximumDeltaRecordsPerDatagram,
        UltraSimGameProtocol.GetChunkDeltaEncodedBytes(snapshot.MaximumDeltaRecordsPerDatagram),
        UltraSimGameProtocol.GetChunkDeltaEncodedBytes(snapshot.MaximumDeltaRecordsPerDatagram + 1),
        snapshot.OutstandingDeliveryCapacity,
        snapshot.ReservedBytes,
        "WorldState is independent-unreliable/nonfragmenting. Oversize deltas fail closed and " +
        "defer complete recovery to N1.3; tests, not this census, prove impairment convergence.");
    Console.WriteLine(
        $"udp_datagram={transportEvidence.DatagramBytes} " +
        $"unfragmented_message={transportEvidence.MaximumUnfragmentedMessageBytes} " +
        $"delta_rows={transportEvidence.MaximumDeltaRecordsPerDatagram} " +
        $"safe_delta_bytes={transportEvidence.MaximumAdmittedDeltaBytes} " +
        $"first_oversize_bytes={transportEvidence.FirstRejectedDeltaBytes}");
}

var report = new Report(
    "network-n1.2-independent-chunk-delivery-v1",
    DateTimeOffset.UtcNow,
    Environment.Version.ToString(),
    RuntimeInformation.FrameworkDescription,
    RuntimeInformation.OSDescription,
    RuntimeInformation.ProcessArchitecture.ToString(),
    Environment.ProcessorCount,
    GCSettings.IsServerGC,
    Stopwatch.Frequency,
    warmups,
    samples,
    rounds,
    new ProfileEvidence(
        "N1.0 recipient control, N1.1 codecs, and N1.2 real-transport MTU/ledger capacity " +
        "evidence; not N1.3 snapshot recovery, N0 replacement, public WAN CCU, or production adoption evidence",
        UltraSimGameProtocol.Manifest.Version,
        UltraSimGameProtocol.Manifest.Fingerprint,
        UltraSimGameProtocol.ChunkPublicationSchemaFingerprint,
        GameWorldSpatialProfile.WorldChunkEdgeUnits,
        GameChunkInterestProfile.ImmediateEnterRadiusMillimeters,
        GameChunkInterestProfile.NearEnterRadiusMillimeters,
        GameChunkInterestProfile.FarEnterRadiusMillimeters,
        OfflineDirectChunkCadence.EvidenceProfile.ImmediateSteps,
        OfflineDirectChunkCadence.EvidenceProfile.NearSteps,
        OfflineDirectChunkCadence.EvidenceProfile.FarSteps,
        Marshal.SizeOf<OfflineDirectChunkSubscription>(),
        Marshal.SizeOf<ChunkPublicationCursor>(),
        Marshal.SizeOf<OfflineDirectChunkSubscriptionChange>()),
    scales,
    cadenceTiming,
    cadenceSnapshot,
    codecEvidence,
    transportEvidence);
Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
File.WriteAllText(output, JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }) +
    Environment.NewLine);
Console.WriteLine(Path.GetFullPath(output));

static int ReadInt(string[] values, string prefix, int fallback) =>
    int.TryParse(values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?
        [prefix.Length..], out int parsed) ? parsed : fallback;
static string? ReadString(string[] values, string prefix) =>
    values.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))?[prefix.Length..];

sealed class ScaleFixture : IDisposable
{
    private readonly ChunkPublicationStore _source = new(
        PublicationEpoch.Create(), new ChunkPublicationLimits(8, 16, 2, 16));
    private readonly OfflineDirectChunkRecipient[] _recipients;
    private readonly WorldPosition[] _stableA;
    private readonly WorldPosition[] _stableB;
    private readonly WorldPosition[] _crossA;
    private readonly WorldPosition[] _crossB;
    private readonly OfflineDirectChunkSubscriptionChange[] _changes = new OfflineDirectChunkSubscriptionChange[1_024];
    private bool _stableSide;
    private bool _crossSide;

    internal ScaleFixture(int recipients)
    {
        _recipients = new OfflineDirectChunkRecipient[recipients];
        _stableA = new WorldPosition[recipients];
        _stableB = new WorldPosition[recipients];
        _crossA = new WorldPosition[recipients];
        _crossB = new WorldPosition[recipients];
        for (int index = 0; index < recipients; index++)
        {
            _recipients[index] = NewRecipient();
            int x = checked(index * 3);
            int z = checked((index % 7) * 3);
            _stableA[index] = new WorldPosition(x, z, 8_000, 8_000, 0);
            _stableB[index] = new WorldPosition(x, z, 8_100, 8_000, 0);
            _crossA[index] = _stableA[index];
            _crossB[index] = new WorldPosition(x + 1, z, 8_000, 8_000, 0);
            Require(_recipients[index].Reconcile(
                _source, GameWorldState.DefaultSpace, _stableA[index], _changes, out _) ==
                OfflineDirectChunkReconcileStatus.Reconciled);
        }
    }

    internal ScaleEvidence Measure(int warmups, int samples, int rounds)
    {
        TimingEvidence stable = MeasureTiming(warmups, samples, rounds, _recipients.Length, RunStable);
        TimingEvidence crossing = MeasureTiming(warmups, samples, rounds, _recipients.Length, RunCrossing);
        OfflineDirectChunkRecipientSnapshot one = _recipients[0].GetSnapshot();
        long totalReserved = 0;
        for (int index = 0; index < _recipients.Length; index++)
            totalReserved = checked(totalReserved + _recipients[index].GetSnapshot().ReservedBytes);
        return new ScaleEvidence(
            _recipients.Length,
            one.SubscriptionCount,
            totalReserved,
            one.ReservedBytes,
            CrossingChanges(_crossA[0], _crossB[0]),
            stable,
            crossing);
    }

    private ulong RunStable(int rounds)
    {
        ulong checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            WorldPosition[] anchors = _stableSide ? _stableA : _stableB;
            for (int index = 0; index < _recipients.Length; index++)
            {
                Require(_recipients[index].Reconcile(
                    _source, GameWorldState.DefaultSpace, anchors[index], _changes,
                    out OfflineDirectChunkReconcileResult result) == OfflineDirectChunkReconcileStatus.Reconciled);
                Require(result.ChangeCount == 0);
                checksum = unchecked(checksum + (uint)result.SubscriptionCount);
            }
            _stableSide = !_stableSide;
        }
        return checksum;
    }

    private ulong RunCrossing(int rounds)
    {
        ulong checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            WorldPosition[] anchors = _crossSide ? _crossA : _crossB;
            for (int index = 0; index < _recipients.Length; index++)
            {
                Require(_recipients[index].Reconcile(
                    _source, GameWorldState.DefaultSpace, anchors[index], _changes,
                    out OfflineDirectChunkReconcileResult result) == OfflineDirectChunkReconcileStatus.Reconciled);
                checksum = unchecked(checksum + (uint)result.ChangeCount);
            }
            _crossSide = !_crossSide;
        }
        return checksum;
    }

    private int CrossingChanges(WorldPosition first, WorldPosition second)
    {
        var probe = NewRecipient();
        Require(probe.Reconcile(_source, GameWorldState.DefaultSpace, first, _changes, out _) ==
            OfflineDirectChunkReconcileStatus.Reconciled);
        Require(probe.Reconcile(_source, GameWorldState.DefaultSpace, second, _changes,
            out OfflineDirectChunkReconcileResult result) == OfflineDirectChunkReconcileStatus.Reconciled);
        return result.ChangeCount;
    }

    private static OfflineDirectChunkRecipient NewRecipient() => new(
        new OfflineDirectChunkRecipientLimits(1_024, 64, 64),
        GameChunkInterestProfile.Default,
        OfflineDirectChunkCadence.EvidenceProfile);

    public void Dispose() { }
}

sealed class CadenceFixture : IDisposable
{
    private readonly EcsWorld _world;
    private readonly ChunkPublicationStore _source;
    private readonly OfflineDirectChunkSubscriptionChange[] _changes = new OfflineDirectChunkSubscriptionChange[1_024];
    private ulong _step;

    internal CadenceFixture()
    {
        _world = new EcsWorld(
            GameSchema.CreateSchema(),
            new EcsWorldOptions(new EntityCapacity(8), 2, 4, lifecycleRecordCapacity: 16));
        ArchetypeHandle empty = _world.RegisterArchetype(
            ReadOnlySpan<ComponentId>.Empty, ChunkLayoutOptions.KiB16, 1);
        _world.Seal();
        _world.Initialize();
        Require(_world.TryCreate(empty, out Entity entity));
        _source = new ChunkPublicationStore(
            PublicationEpoch.Create(), new ChunkPublicationLimits(8, 16, 2, 16));
        var descriptor = new WorldPublicDescriptor(
            GameWorldPublicPrefabIds.Character, WorldYaw.Zero, WorldPublicStateFlags.Visible);
        Require(_source.TryCommit([
            WorldPublicChange.Present(entity, new WorldPosition(0, 0, 8_000, 8_000, 0), descriptor)
        ], out _));
        Recipient = new OfflineDirectChunkRecipient(
            new OfflineDirectChunkRecipientLimits(1_024, 8, 8),
            GameChunkInterestProfile.Default,
            OfflineDirectChunkCadence.EvidenceProfile);
        Require(Recipient.Reconcile(
            _source, GameWorldState.DefaultSpace, new WorldPosition(0, 0, 8_000, 8_000, 0),
            _changes, out _) == OfflineDirectChunkReconcileStatus.Reconciled);
        Drain();
    }

    internal OfflineDirectChunkRecipient Recipient { get; }
    internal ChunkPublicationStore Source => _source;

    internal TimingEvidence Measure(int warmups, int samples, int rounds) =>
        MeasureTiming(warmups, samples, rounds, 1, Run);

    private ulong Run(int rounds)
    {
        ulong checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            _step++;
            Require(Recipient.Reconcile(
                _source, GameWorldState.DefaultSpace, new WorldPosition(0, 0, 8_000, 8_000, 0),
                _changes, out OfflineDirectChunkReconcileResult result) ==
                OfflineDirectChunkReconcileStatus.Reconciled);
            Drain();
            checksum = unchecked(checksum + (uint)result.SubscriptionCount);
        }
        return checksum;
    }

    private void Drain()
    {
        for (int index = 0; index < Recipient.Subscriptions.Length; index++)
        {
            OfflineDirectChunkReadStatus status = Recipient.TryBeginRead(_source, index, _step, out _);
            if (status == OfflineDirectChunkReadStatus.NotDue) continue;
            Require(status is OfflineDirectChunkReadStatus.Snapshot or
                OfflineDirectChunkReadStatus.ChunkAbsent or
                OfflineDirectChunkReadStatus.Delta or
                OfflineDirectChunkReadStatus.NoChanges);
            Recipient.CommitRead();
        }
    }

    public void Dispose() => _world.Dispose();
}

sealed class CodecFixture
{
    private const ulong ApplicationEpoch = 71;
    private const ulong PublicationEpoch = 72;
    private const ulong Space = 1;
    private const ulong BaseRevision = 100;
    private const ulong CurrentRevision = 101;
    private readonly byte[] _snapshotBytes = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly byte[] _deltaBytes = new byte[UltraSimGameProtocol.MaximumMessageBytes];
    private readonly byte[] _absenceBytes = new byte[256];
    private readonly byte[] _proofBytes = new byte[256];
    private readonly RemoteChunkPublicRecord[] _snapshot =
        new RemoteChunkPublicRecord[UltraSimGameProtocol.MaximumChunkPublicationRecords];
    private readonly RemoteChunkPublicRecord[] _snapshotDecoded =
        new RemoteChunkPublicRecord[UltraSimGameProtocol.MaximumChunkPublicationRecords];
    private readonly RemoteChunkDeltaRecord[] _delta =
        new RemoteChunkDeltaRecord[UltraSimGameProtocol.MaximumChunkPublicationRecords];
    private readonly RemoteChunkDeltaRecord[] _deltaDecoded =
        new RemoteChunkDeltaRecord[UltraSimGameProtocol.MaximumChunkPublicationRecords];
    private readonly int _snapshotWritten;
    private readonly int _deltaWritten;
    private readonly int _absenceWritten;
    private readonly int _proofWritten;

    internal CodecFixture()
    {
        for (int index = 0; index < _snapshot.Length; index++)
        {
            uint identity = checked((uint)(index + 1));
            _snapshot[index] = new RemoteChunkPublicRecord(
                identity, 1, checked((ulong)(index + 1)), checked((uint)(1_000 + index)),
                checked((ushort)(index * 127)), checked((ushort)(index * 193)), index - 32,
                checked((ushort)(index * 977)), (ushort)(index & 1), BaseRevision);
            bool remove = (index & 3) == 3;
            _delta[index] = remove
                ? new RemoteChunkDeltaRecord(
                    RemoteChunkPublicOperation.Remove, RemoteChunkPublicFieldMask.None,
                    identity, 1, checked((ulong)(index + 65)), 0, 0, 0, 0, 0, 0, 0)
                : new RemoteChunkDeltaRecord(
                    RemoteChunkPublicOperation.Present, RemoteChunkPublicFieldMask.Complete,
                    identity, 1, checked((ulong)(index + 65)), checked((uint)(1_000 + index)),
                    checked((ushort)(index * 127)), checked((ushort)(index * 193)), index - 31,
                    checked((ushort)(index * 977)), (ushort)(index & 1), CurrentRevision);
        }
        Require(UltraSimGameProtocol.TryWriteChunkSnapshot(
            _snapshotBytes, ApplicationEpoch, PublicationEpoch, 1, Space, -8, 9,
            BaseRevision, 1, _snapshot, out _snapshotWritten));
        Require(UltraSimGameProtocol.TryWriteChunkDelta(
            _deltaBytes, ApplicationEpoch, PublicationEpoch, 2, Space, -8, 9,
            BaseRevision, CurrentRevision, 1, _delta, out _deltaWritten));
        Require(UltraSimGameProtocol.TryWriteChunkAbsence(
            _absenceBytes, ApplicationEpoch, PublicationEpoch, 3, Space, -8, 9,
            out _absenceWritten));
        var proof = new RemoteChunkAppliedMessage
        {
            ApplicationSessionEpoch = ApplicationEpoch,
            PublicationEpoch = PublicationEpoch,
            DeliveryId = 2,
            SpatialSpaceId = Space,
            ChunkX = -8,
            ChunkZ = 9,
            Revision = CurrentRevision,
            Kind = RemoteChunkPublicationKind.Delta,
            Status = RemoteChunkPublicationApplyStatus.Applied,
        };
        Require(UltraSimGameProtocol.TryWriteChunkApplied(_proofBytes, proof, out _proofWritten));
    }

    internal CodecEvidence Measure(int warmups, int samples, int rounds)
    {
        TimingEvidence snapshotEncode = MeasureTiming(warmups, samples, rounds, 1, EncodeSnapshot);
        TimingEvidence snapshotDecode = MeasureTiming(warmups, samples, rounds, 1, DecodeSnapshot);
        TimingEvidence deltaEncode = MeasureTiming(warmups, samples, rounds, 1, EncodeDelta);
        TimingEvidence deltaDecode = MeasureTiming(warmups, samples, rounds, 1, DecodeDelta);
        TimingEvidence absenceDecode = MeasureTiming(warmups, samples, rounds, 1, DecodeAbsence);
        TimingEvidence proofDecode = MeasureTiming(warmups, samples, rounds, 1, DecodeProof);
        return new CodecEvidence(
            _snapshot.Length, _snapshotWritten, _deltaWritten, _absenceWritten, _proofWritten,
            snapshotEncode, snapshotDecode, deltaEncode, deltaDecode, absenceDecode, proofDecode);
    }

    private ulong EncodeSnapshot(int rounds)
    {
        ulong checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            Require(UltraSimGameProtocol.TryWriteChunkSnapshot(
                _snapshotBytes, ApplicationEpoch, PublicationEpoch, checked((ulong)round + 1),
                Space, -8, 9, BaseRevision, 1, _snapshot, out int written));
            checksum = unchecked(checksum + (uint)written);
        }
        return checksum;
    }

    private ulong DecodeSnapshot(int rounds)
    {
        ulong checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            Require(UltraSimGameProtocol.TryReadChunkSnapshot(
                _snapshotBytes.AsSpan(0, _snapshotWritten), out RemoteChunkSnapshotHeader header,
                _snapshotDecoded, out int count));
            checksum = unchecked(checksum + header.ContentHash + (uint)count);
        }
        return checksum;
    }

    private ulong EncodeDelta(int rounds)
    {
        ulong checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            Require(UltraSimGameProtocol.TryWriteChunkDelta(
                _deltaBytes, ApplicationEpoch, PublicationEpoch, checked((ulong)round + 1),
                Space, -8, 9, BaseRevision, CurrentRevision, 1, _delta, out int written));
            checksum = unchecked(checksum + (uint)written);
        }
        return checksum;
    }

    private ulong DecodeDelta(int rounds)
    {
        ulong checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            Require(UltraSimGameProtocol.TryReadChunkDelta(
                _deltaBytes.AsSpan(0, _deltaWritten), out RemoteChunkDeltaHeader header,
                _deltaDecoded, out int count));
            checksum = unchecked(checksum + header.ContentHash + (uint)count);
        }
        return checksum;
    }

    private ulong DecodeAbsence(int rounds)
    {
        ulong checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            Require(UltraSimGameProtocol.TryReadChunkAbsence(
                _absenceBytes.AsSpan(0, _absenceWritten), out RemoteChunkAbsenceMessage message));
            checksum = unchecked(checksum + message.DeliveryId);
        }
        return checksum;
    }

    private ulong DecodeProof(int rounds)
    {
        ulong checksum = 0;
        for (int round = 0; round < rounds; round++)
        {
            Require(UltraSimGameProtocol.TryReadChunkApplied(
                _proofBytes.AsSpan(0, _proofWritten), out RemoteChunkAppliedMessage message));
            checksum = unchecked(checksum + message.Revision);
        }
        return checksum;
    }
}

static class BenchmarkHarness
{
    internal static TimingEvidence MeasureTiming(
        int warmups,
        int samples,
        int operations,
        int recipients,
        Func<int, ulong> body)
    {
        ulong checksum = 0;
        for (int index = 0; index < warmups; index++) checksum = unchecked(checksum + body(operations));
        var elapsed = new double[samples];
        var allocated = new long[samples];
        for (int index = 0; index < samples; index++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long started = Stopwatch.GetTimestamp();
            checksum = unchecked(checksum + body(operations));
            long stopped = Stopwatch.GetTimestamp();
            allocated[index] = GC.GetAllocatedBytesForCurrentThread() - before;
            elapsed[index] = (stopped - started) * 1_000_000_000d / Stopwatch.Frequency /
                operations / recipients;
        }
        if (allocated.Max() != 0)
            throw new InvalidOperationException(
                $"A warmed N1.0 recipient path allocated {allocated.Max()} bytes.");
        Array.Sort(elapsed);
        return new TimingEvidence(
            elapsed.Average(),
            elapsed[Percentile(samples, .50)],
            elapsed[Percentile(samples, .95)],
            elapsed[^1],
            allocated.Max(),
            checksum);
    }

    internal static void Require(bool condition)
    {
        if (!condition) throw new InvalidOperationException("N1.0 benchmark semantic guard failed.");
    }

    private static int Percentile(int count, double fraction) =>
        Math.Clamp((int)Math.Ceiling(count * fraction) - 1, 0, count - 1);
}

readonly record struct TimingEvidence(
    double MeanNanosecondsPerRecipient,
    double P50NanosecondsPerRecipient,
    double P95NanosecondsPerRecipient,
    double MaximumNanosecondsPerRecipient,
    long MaximumAllocatedBytes,
    ulong Checksum);

readonly record struct ScaleEvidence(
    int Recipients,
    int SubscriptionsPerRecipient,
    long TotalRecipientReservedBytes,
    long ReservedBytesPerRecipient,
    int CrossingChangesPerRecipient,
    TimingEvidence StableReconcile,
    TimingEvidence CrossingReconcile);

readonly record struct ProfileEvidence(
    string Scope,
    ushort ProtocolManifestVersion,
    ulong ProtocolManifestFingerprint,
    ulong ChunkPublicationSchemaFingerprint,
    int ChunkEdgeMillimeters,
    int ImmediateRadiusMillimeters,
    int NearRadiusMillimeters,
    int FarRadiusMillimeters,
    uint ImmediateCadenceSteps,
    uint NearCadenceSteps,
    uint FarCadenceSteps,
    int SubscriptionBytes,
    int CursorBytes,
    int ChangeBytes);

readonly record struct CodecEvidence(
    int Records,
    int SnapshotBytes,
    int DeltaBytes,
    int AbsenceBytes,
    int ProofBytes,
    TimingEvidence SnapshotEncode,
    TimingEvidence SnapshotDecode,
    TimingEvidence DeltaEncode,
    TimingEvidence DeltaDecode,
    TimingEvidence AbsenceDecode,
    TimingEvidence ProofDecode);

readonly record struct TransportEvidence(
    int DatagramBytes,
    int MaximumUnfragmentedMessageBytes,
    int MaximumDeltaRecordsPerDatagram,
    int MaximumAdmittedDeltaBytes,
    int FirstRejectedDeltaBytes,
    int OutstandingDeliveryCapacity,
    long ServerAdapterReservedBytes,
    string Notes);

readonly record struct Report(
    string Workload,
    DateTimeOffset CapturedUtc,
    string RuntimeVersion,
    string Framework,
    string OperatingSystem,
    string Architecture,
    int LogicalProcessors,
    bool ServerGc,
    long StopwatchFrequency,
    int Warmups,
    int Samples,
    int RoundsPerSample,
    ProfileEvidence Profile,
    ScaleEvidence[] Scales,
    TimingEvidence CadencePass,
    OfflineDirectChunkRecipientSnapshot CadenceCounters,
    CodecEvidence Codec,
    TransportEvidence Transport);
