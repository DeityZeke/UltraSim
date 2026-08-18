using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Game.Protocol;

public enum RemoteAuthenticationStatus : byte
{
    Accepted = 0,
    Rejected = 1,
}

public enum RemoteUiOperationKind : byte
{
    ColdOpen = 0,
    HotOpen = 1,
    Update = 2,
    Close = 3,
}

public enum RemoteUiApplyStatus : byte
{
    Applied = 0,
    Duplicate = 1,
    Rejected = 2,
}

public enum RemoteWorldStateApplyStatus : byte
{
    Applied = 0,
    Rejected = 1,
    CapacityExceeded = 2,
}

public enum RemoteWorldBaselineApplyStatus : byte
{
    Applied = 0,
    Rejected = 1,
    CapacityExceeded = 2,
    Prepared = 3,
}

public enum RemoteApplicationEventApplyStatus : byte
{
    Applied = 0,
    Rejected = 1,
    CapacityExceeded = 2,
}

[NetworkProtocol(7)]
public static partial class GeneratedUltraSimGameProtocol
{
}

[NetworkMessage(100, "ultrasim.game.authentication-request", 1, NetworkLane.Control)]
public partial struct RemoteAuthenticationRequestHeader
{
    public ulong AttemptId;
}

[NetworkMessage(101, "ultrasim.game.authentication-result", 1, NetworkLane.Control)]
public partial struct RemoteAuthenticationResultMessage
{
    public ulong AttemptId;
    public RemoteAuthenticationStatus Status;
    public ulong ParticipantId;
    public ulong ApplicationSessionEpoch;
}

[NetworkMessage(102, "ultrasim.game.ui-operation", 1, NetworkLane.Bulk)]
public partial struct RemoteUiOperationHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong OperationId;
    public RemoteUiOperationKind Kind;
}

[NetworkMessage(103, "ultrasim.game.ui-apply-result", 1, NetworkLane.Control)]
public partial struct RemoteUiApplyResultMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong OperationId;
    public ulong ViewInstanceId;
    public ulong ModelSequence;
    public RemoteUiApplyStatus Status;
}

[NetworkMessage(104, "ultrasim.game.ui-action", 1, NetworkLane.Control)]
public partial struct RemoteUiActionHeader
{
    public ulong ApplicationSessionEpoch;
}

[NetworkMessage(105, "ultrasim.game.session-detach", 1, NetworkLane.Control)]
public partial struct RemoteSessionDetachMessage
{
    public ulong ApplicationSessionEpoch;
}

/// <summary>Header for one complete bounded redundant input bundle.</summary>
[NetworkMessage(106, "ultrasim.game.authoritative-input", 2, NetworkLane.Input)]
public partial struct RemoteAuthoritativeInputBundleHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public byte SampleCount;
}

/// <summary>One complete velocity state repeated in newest bundles until owner acknowledgement.</summary>
/// <remarks>
/// This sample is carried by a superseding unreliable Input stream. It remains
/// correct when every older unprocessed sample is skipped and only the newest
/// sample survives. Non-coalescible facts require reliable application Event
/// semantics instead.
/// </remarks>
public readonly record struct RemoteAuthoritativeInputSample(
    ulong Sequence,
    ulong TargetSimulationTick,
    long ExpiresAtWorldTimeMicroseconds,
    int InputVelocity);

/// <summary>Latest authoritative state for the one controlled N0.6 entity.</summary>
[NetworkMessage(107, "ultrasim.game.owner-state", 1, NetworkLane.OwnerState)]
public partial struct RemoteOwnerStateMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public uint EntityIndex;
    public uint EntityGeneration;
    public ulong SimulationTick;
    public ulong FrameSequence;
    public ulong LastProcessedInputSequence;
    public int TickMarker;
    public int InputVelocity;
    public uint ClockRevision;
    public bool IsPresent;
}

/// <summary>Reliable cadence/time interpretation effective from a declared authoritative tick.</summary>
[NetworkMessage(108, "ultrasim.game.clock-anchor", 1, NetworkLane.Control)]
public partial struct RemoteClockAnchorMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public uint ClockRevision;
    public ulong EffectiveFromSimulationTick;
    public int SimulationFrequencyHz;
    public long WorldTimeMicroseconds;
    public int WorldTimeFractionQuanta;
}

/// <summary>Envelope for one recipient-relative complete WorldState bundle.</summary>
[NetworkMessage(109, "ultrasim.game.world-state", 1, NetworkLane.WorldState)]
public partial struct RemoteWorldStateBundleHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong BundleId;
    public byte RecordCount;
}

/// <summary>
/// Reconstructible application-level proof that a WorldState bundle was or was not committed.
/// Loss leaves server knowledge unchanged; ordinary state retry regenerates an equivalent proof.
/// </summary>
[NetworkMessage(110, "ultrasim.game.world-state-applied", 2, NetworkLane.WorldState)]
public partial struct RemoteWorldStateAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong BundleId;
    public RemoteWorldStateApplyStatus Status;
}

/// <summary>Declares one bounded atomic replacement of the relevant WorldState scope.</summary>
[NetworkMessage(111, "ultrasim.game.world-baseline-begin", 1, NetworkLane.Control)]
public partial struct RemoteWorldBaselineBeginMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong BaselineId;
    public ulong BaselineRevision;
    public ulong EventFloor;
    public ulong RelationSequenceFloor;
    public ulong SchemaFingerprint;
    public ulong ContentHash;
    public uint RecordCount;
    public ushort SegmentCount;
    public ushort RecordsPerSegment;
}

/// <summary>Envelope for one independently retained Bulk segment of a baseline candidate.</summary>
[NetworkMessage(112, "ultrasim.game.world-baseline-segment", 1, NetworkLane.Bulk)]
public partial struct RemoteWorldBaselineSegmentHeader
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong BaselineId;
    public ushort SegmentIndex;
    public ushort SegmentCount;
    public ushort RecordCount;
}

/// <summary>Signals that every declared segment may now be atomically validated and committed.</summary>
[NetworkMessage(113, "ultrasim.game.world-baseline-commit", 1, NetworkLane.Control)]
public partial struct RemoteWorldBaselineCommitMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong BaselineId;
    public ulong BaselineRevision;
    public ulong ContentHash;
}

/// <summary>Application proof that a complete baseline candidate committed or failed.</summary>
[NetworkMessage(114, "ultrasim.game.world-baseline-applied", 1, NetworkLane.Control)]
public partial struct RemoteWorldBaselineAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong BaselineId;
    public ulong BaselineRevision;
    public RemoteWorldBaselineApplyStatus Status;
}

/// <summary>
/// Reliably retires one server-abandoned baseline candidate without changing
/// the client's currently published projection.
/// </summary>
[NetworkMessage(117, "ultrasim.game.world-baseline-cancel", 1, NetworkLane.Control)]
public partial struct RemoteWorldBaselineCancelMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong BaselineId;
}

/// <summary>
/// Reliable client-originated pressure signal for the retained WorldState
/// stale-delivery ledger. It grants no authority and is idempotent by sequence.
/// </summary>
[NetworkMessage(118, "ultrasim.game.world-compaction-request", 1, NetworkLane.Control)]
public partial struct RemoteWorldStateCompactionRequestMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong RequestSequence;
    public uint KnownIdentityCount;
    public uint KnownIdentityCapacity;
    public uint ActiveEntityCount;
}

/// <summary>One retained non-coalescible game fact on the reliable Event lane.</summary>
[NetworkMessage(115, "ultrasim.game.application-event", 1, NetworkLane.Event)]
public partial struct RemoteApplicationEventMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong Sequence;
    public ulong ParticipantId;
    public ushort Kind;
    public ulong Subject;
    public ulong PayloadHash;
}

/// <summary>Application proof that one ordered game fact was committed.</summary>
[NetworkMessage(116, "ultrasim.game.application-event-applied", 1, NetworkLane.Control)]
public partial struct RemoteApplicationEventAppliedMessage
{
    public ulong ApplicationSessionEpoch;
    public ulong StateEpoch;
    public ulong NextSequence;
    public RemoteApplicationEventApplyStatus Status;
}

/// <summary>One complete present or absent dynamic-state operation.</summary>
public readonly record struct RemoteWorldStateRecord(
    ulong RelationSequence,
    uint EntityIndex,
    uint EntityGeneration,
    ulong CanonicalVersion,
    bool IsPresent,
    byte Horizon,
    int XMillimeters,
    int ZMillimeters,
    int TickMarker,
    int InputVelocity)
{
    public const int EncodedSize =
        sizeof(ulong) + sizeof(uint) + sizeof(uint) + sizeof(ulong) +
        sizeof(byte) + sizeof(byte) + sizeof(int) * 4;
}

/// <summary>Accepted game application protocol manifest and bounded dynamic codecs through N0.9.</summary>
public static class UltraSimGameProtocol
{
    public const int MaximumMessageBytes = 16_384;
    public const int MaximumIdentityBytes = 1_024;
    public const int MaximumInputRedundancy = 3;
    public const int MaximumWorldStateRecords = 64;
    public const ulong WorldStateBaselineSchemaFingerprint = 0x5553_5742_4C30_3031UL;

    /// <summary>Default semantic UI payload bound after reserving the largest application envelope.</summary>
    public static UiSemanticWireLimits DefaultUiWireLimits => new(
        MaximumMessageBytes - RemoteUiOperationHeaderCodec.EncodedSize,
        UiCompileLimits.Default);

    private static readonly NetworkProtocolManifest s_manifest = NetworkProtocolManifest.Create(
        version: 9,
        [
            RemoteAuthenticationRequestHeaderCodec.Descriptor with
                { MaximumEncodedBytes = MaximumMessageBytes },
            RemoteAuthenticationResultMessageCodec.Descriptor,
            RemoteUiOperationHeaderCodec.Descriptor with
                { MaximumEncodedBytes = MaximumMessageBytes },
            RemoteUiApplyResultMessageCodec.Descriptor,
            RemoteUiActionHeaderCodec.Descriptor with
                { MaximumEncodedBytes = MaximumMessageBytes },
            RemoteSessionDetachMessageCodec.Descriptor,
            RemoteAuthoritativeInputBundleHeaderCodec.Descriptor with
                { MaximumEncodedBytes = RemoteAuthoritativeInputBundleHeaderCodec.EncodedSize +
                    MaximumInputRedundancy * (sizeof(ulong) * 2 + sizeof(long) + sizeof(int)) },
            RemoteOwnerStateMessageCodec.Descriptor,
            RemoteClockAnchorMessageCodec.Descriptor,
            RemoteWorldStateBundleHeaderCodec.Descriptor with
                { MaximumEncodedBytes = RemoteWorldStateBundleHeaderCodec.EncodedSize +
                    MaximumWorldStateRecords * RemoteWorldStateRecord.EncodedSize },
            RemoteWorldStateAppliedMessageCodec.Descriptor,
            RemoteWorldBaselineBeginMessageCodec.Descriptor,
            RemoteWorldBaselineSegmentHeaderCodec.Descriptor with
                { MaximumEncodedBytes = MaximumMessageBytes },
            RemoteWorldBaselineCommitMessageCodec.Descriptor,
            RemoteWorldBaselineAppliedMessageCodec.Descriptor,
            RemoteApplicationEventMessageCodec.Descriptor,
            RemoteApplicationEventAppliedMessageCodec.Descriptor,
            RemoteWorldBaselineCancelMessageCodec.Descriptor,
            RemoteWorldStateCompactionRequestMessageCodec.Descriptor,
        ]);

    public static NetworkProtocolManifest Manifest => s_manifest;

    public static bool TryWriteAuthenticationRequest(
        Span<byte> destination,
        ulong attemptId,
        string provider,
        string credential,
        out int written)
    {
        written = 0;
        var writer = new NetworkPacketWriter(destination);
        var header = new RemoteAuthenticationRequestHeader { AttemptId = attemptId };
        if (!RemoteAuthenticationRequestHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBoundedUtf8(provider, MaximumIdentityBytes) ||
            !writer.TryWriteBoundedUtf8(credential, MaximumIdentityBytes)) return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadAuthenticationRequest(
        ReadOnlySpan<byte> source,
        out RemoteAuthenticationRequestHeader header,
        out string provider,
        out string credential)
    {
        header = default;
        provider = credential = string.Empty;
        var reader = new NetworkPacketReader(source);
        if (!RemoteAuthenticationRequestHeaderCodec.TryRead(ref reader, out header) ||
            header.AttemptId == 0 ||
            !reader.TryReadBoundedUtf8Bytes(MaximumIdentityBytes, out ReadOnlySpan<byte> providerBytes) ||
            !reader.TryReadBoundedUtf8Bytes(MaximumIdentityBytes, out ReadOnlySpan<byte> credentialBytes) ||
            !reader.IsComplete) return false;
        provider = System.Text.Encoding.UTF8.GetString(providerBytes);
        credential = System.Text.Encoding.UTF8.GetString(credentialBytes);
        return provider.Length != 0 && credential.Length != 0;
    }

    public static bool TryWriteUiOperation(
        Span<byte> destination,
        in RemoteUiOperationHeader header,
        ReadOnlySpan<byte> semanticPayload,
        out int written)
    {
        written = 0;
        var writer = new NetworkPacketWriter(destination);
        if (!RemoteUiOperationHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBytes(semanticPayload)) return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadUiOperation(
        ReadOnlySpan<byte> source,
        out RemoteUiOperationHeader header,
        out ReadOnlySpan<byte> semanticPayload)
    {
        var reader = new NetworkPacketReader(source);
        if (!RemoteUiOperationHeaderCodec.TryRead(ref reader, out header) ||
            header.ApplicationSessionEpoch == 0 || header.OperationId == 0 ||
            !reader.TryReadBytes(reader.Remaining, out semanticPayload) || semanticPayload.Length == 0)
        {
            header = default;
            semanticPayload = default;
            return false;
        }
        return true;
    }

    public static bool TryWriteUiAction(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        ReadOnlySpan<byte> semanticPayload,
        out int written)
    {
        written = 0;
        var writer = new NetworkPacketWriter(destination);
        var header = new RemoteUiActionHeader { ApplicationSessionEpoch = applicationSessionEpoch };
        if (!RemoteUiActionHeaderCodec.TryWrite(ref writer, in header) ||
            !writer.TryWriteBytes(semanticPayload)) return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadUiAction(
        ReadOnlySpan<byte> source,
        out RemoteUiActionHeader header,
        out ReadOnlySpan<byte> semanticPayload)
    {
        var reader = new NetworkPacketReader(source);
        if (!RemoteUiActionHeaderCodec.TryRead(ref reader, out header) ||
            header.ApplicationSessionEpoch == 0 ||
            !reader.TryReadBytes(reader.Remaining, out semanticPayload) || semanticPayload.Length == 0)
        {
            header = default;
            semanticPayload = default;
            return false;
        }
        return true;
    }

    public static bool TryWriteAuthoritativeInputBundle(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        StateEpoch stateEpoch,
        ReadOnlySpan<RemoteAuthoritativeInputSample> samples,
        out int written)
    {
        written = 0;
        if (applicationSessionEpoch == 0 || !stateEpoch.IsValid || samples.IsEmpty ||
            samples.Length > MaximumInputRedundancy) return false;
        var writer = new NetworkPacketWriter(destination);
        var header = new RemoteAuthoritativeInputBundleHeader
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            StateEpoch = stateEpoch.Value,
            SampleCount = checked((byte)samples.Length),
        };
        if (!RemoteAuthoritativeInputBundleHeaderCodec.TryWrite(ref writer, in header)) return false;
        ulong previous = 0;
        foreach (ref readonly RemoteAuthoritativeInputSample sample in samples)
        {
            if (sample.Sequence == 0 || sample.Sequence <= previous ||
                sample.TargetSimulationTick == ulong.MaxValue ||
                sample.ExpiresAtWorldTimeMicroseconds < 0 ||
                !writer.TryWriteUInt64(sample.Sequence) ||
                !writer.TryWriteUInt64(sample.TargetSimulationTick) ||
                !writer.TryWriteInt64(sample.ExpiresAtWorldTimeMicroseconds) ||
                !writer.TryWriteInt32(sample.InputVelocity)) return false;
            previous = sample.Sequence;
        }
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadAuthoritativeInputBundle(
        ReadOnlySpan<byte> source,
        out RemoteAuthoritativeInputBundleHeader header,
        Span<RemoteAuthoritativeInputSample> samples,
        out int count)
    {
        header = default;
        count = 0;
        var reader = new NetworkPacketReader(source);
        if (!RemoteAuthoritativeInputBundleHeaderCodec.TryRead(ref reader, out header) ||
            header.ApplicationSessionEpoch == 0 || header.StateEpoch == 0 ||
            header.SampleCount == 0 || header.SampleCount > MaximumInputRedundancy ||
            header.SampleCount > samples.Length) return false;
        ulong previous = 0;
        for (int index = 0; index < header.SampleCount; index++)
        {
            if (!reader.TryReadUInt64(out ulong sequence) || sequence == 0 || sequence <= previous ||
                !reader.TryReadUInt64(out ulong targetTick) || targetTick == ulong.MaxValue ||
                !reader.TryReadInt64(out long expiry) || expiry < 0 ||
                !reader.TryReadInt32(out int inputVelocity))
            {
                samples[..index].Clear();
                header = default;
                return false;
            }
            samples[index] = new(sequence, targetTick, expiry, inputVelocity);
            previous = sequence;
        }
        if (!reader.IsComplete)
        {
            samples[..header.SampleCount].Clear();
            header = default;
            return false;
        }
        count = header.SampleCount;
        return true;
    }

    public static bool TryWriteWorldStateBundle(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        ulong stateEpoch,
        ulong bundleId,
        ReadOnlySpan<RemoteWorldStateRecord> records,
        out int written)
    {
        written = 0;
        if (applicationSessionEpoch == 0 || stateEpoch == 0 || bundleId == 0 ||
            records.IsEmpty || records.Length > MaximumWorldStateRecords) return false;
        var writer = new NetworkPacketWriter(destination);
        var header = new RemoteWorldStateBundleHeader
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            StateEpoch = stateEpoch,
            BundleId = bundleId,
            RecordCount = checked((byte)records.Length),
        };
        if (!RemoteWorldStateBundleHeaderCodec.TryWrite(ref writer, in header)) return false;
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly RemoteWorldStateRecord record = ref records[index];
            if (record.RelationSequence == 0 || record.EntityGeneration == 0 ||
                record.CanonicalVersion == 0 || record.Horizon > 3 ||
                (record.IsPresent ? record.Horizon == 0 :
                    record.Horizon != 0 || record.XMillimeters != 0 || record.ZMillimeters != 0 ||
                    record.TickMarker != 0 || record.InputVelocity != 0))
                return false;
            for (int prior = 0; prior < index; prior++)
                if (records[prior].EntityIndex == record.EntityIndex)
                    return false;
            if (!writer.TryWriteUInt64(record.RelationSequence) ||
                !writer.TryWriteUInt32(record.EntityIndex) ||
                !writer.TryWriteUInt32(record.EntityGeneration) ||
                !writer.TryWriteUInt64(record.CanonicalVersion) ||
                !writer.TryWriteBoolean(record.IsPresent) ||
                !writer.TryWriteByte(record.Horizon) ||
                !writer.TryWriteInt32(record.XMillimeters) ||
                !writer.TryWriteInt32(record.ZMillimeters) ||
                !writer.TryWriteInt32(record.TickMarker) ||
                !writer.TryWriteInt32(record.InputVelocity)) return false;
        }
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadWorldStateBundle(
        ReadOnlySpan<byte> source,
        out RemoteWorldStateBundleHeader header,
        Span<RemoteWorldStateRecord> records,
        out int count)
    {
        header = default;
        count = 0;
        var reader = new NetworkPacketReader(source);
        if (!RemoteWorldStateBundleHeaderCodec.TryRead(ref reader, out RemoteWorldStateBundleHeader stagedHeader) ||
            stagedHeader.ApplicationSessionEpoch == 0 || stagedHeader.StateEpoch == 0 ||
            stagedHeader.BundleId == 0 || stagedHeader.RecordCount == 0 ||
            stagedHeader.RecordCount > MaximumWorldStateRecords ||
            stagedHeader.RecordCount > records.Length) return false;
        for (int index = 0; index < stagedHeader.RecordCount; index++)
        {
            if (!reader.TryReadUInt64(out ulong relationSequence) || relationSequence == 0 ||
                !reader.TryReadUInt32(out uint entityIndex) ||
                !reader.TryReadUInt32(out uint entityGeneration) || entityGeneration == 0 ||
                !reader.TryReadUInt64(out ulong canonicalVersion) || canonicalVersion == 0 ||
                !reader.TryReadBoolean(out bool present) ||
                !reader.TryReadByte(out byte horizon) || horizon > 3 ||
                !reader.TryReadInt32(out int x) || !reader.TryReadInt32(out int z) ||
                !reader.TryReadInt32(out int marker) || !reader.TryReadInt32(out int velocity) ||
                (present ? horizon == 0 : horizon != 0 || x != 0 || z != 0 || marker != 0 || velocity != 0))
            {
                records[..index].Clear();
                return false;
            }
            for (int prior = 0; prior < index; prior++)
            {
                if (records[prior].EntityIndex != entityIndex) continue;
                records[..index].Clear();
                return false;
            }
            records[index] = new(
                relationSequence, entityIndex, entityGeneration, canonicalVersion,
                present, horizon, x, z, marker, velocity);
        }
        if (!reader.IsComplete)
        {
            records[..stagedHeader.RecordCount].Clear();
            return false;
        }
        header = stagedHeader;
        count = stagedHeader.RecordCount;
        return true;
    }

    public static bool TryWriteWorldBaselineSegment(
        Span<byte> destination,
        ulong applicationSessionEpoch,
        ulong stateEpoch,
        ulong baselineId,
        ushort segmentIndex,
        ushort segmentCount,
        ReadOnlySpan<RemoteWorldStateRecord> records,
        out int written)
    {
        written = 0;
        if (applicationSessionEpoch == 0 || stateEpoch == 0 || baselineId == 0 ||
            segmentCount == 0 || segmentIndex >= segmentCount || records.IsEmpty ||
            records.Length > MaximumWorldStateRecords) return false;
        var writer = new NetworkPacketWriter(destination);
        var header = new RemoteWorldBaselineSegmentHeader
        {
            ApplicationSessionEpoch = applicationSessionEpoch,
            StateEpoch = stateEpoch,
            BaselineId = baselineId,
            SegmentIndex = segmentIndex,
            SegmentCount = segmentCount,
            RecordCount = checked((ushort)records.Length),
        };
        if (!RemoteWorldBaselineSegmentHeaderCodec.TryWrite(ref writer, in header) ||
            !TryWriteWorldRecords(ref writer, records)) return false;
        written = writer.WrittenCount;
        return true;
    }

    public static bool TryReadWorldBaselineSegment(
        ReadOnlySpan<byte> source,
        out RemoteWorldBaselineSegmentHeader header,
        Span<RemoteWorldStateRecord> records,
        out int count)
    {
        header = default;
        count = 0;
        var reader = new NetworkPacketReader(source);
        RemoteWorldBaselineSegmentHeader staged = default;
        if (!RemoteWorldBaselineSegmentHeaderCodec.TryRead(ref reader, out staged) ||
            staged.ApplicationSessionEpoch == 0 || staged.StateEpoch == 0 ||
            staged.BaselineId == 0 || staged.SegmentCount == 0 ||
            staged.SegmentIndex >= staged.SegmentCount || staged.RecordCount == 0 ||
            staged.RecordCount > MaximumWorldStateRecords || staged.RecordCount > records.Length ||
            !TryReadWorldRecords(ref reader, records, staged.RecordCount) || !reader.IsComplete)
        {
            records[..Math.Min(staged.RecordCount, (ushort)records.Length)].Clear();
            return false;
        }
        header = staged;
        count = staged.RecordCount;
        return true;
    }

    /// <summary>Computes the canonical content hash over records already sorted by entity index.</summary>
    public static ulong ComputeWorldBaselineHash(ReadOnlySpan<RemoteWorldStateRecord> records)
    {
        const ulong offset = 14695981039346656037UL;
        const ulong prime = 1099511628211UL;
        ulong hash = offset;
        foreach (ref readonly RemoteWorldStateRecord record in records)
        {
            Mix(record.RelationSequence);
            Mix(record.EntityIndex);
            Mix(record.EntityGeneration);
            Mix(record.CanonicalVersion);
            Mix(record.IsPresent ? 1UL : 0UL);
            Mix(record.Horizon);
            Mix(unchecked((uint)record.XMillimeters));
            Mix(unchecked((uint)record.ZMillimeters));
            Mix(unchecked((uint)record.TickMarker));
            Mix(unchecked((uint)record.InputVelocity));
        }
        return hash;

        void Mix(ulong value)
        {
            for (int shift = 0; shift < 64; shift += 8)
            {
                hash ^= unchecked((byte)(value >> shift));
                hash = unchecked(hash * prime);
            }
        }
    }

    private static bool TryWriteWorldRecords(
        ref NetworkPacketWriter writer,
        ReadOnlySpan<RemoteWorldStateRecord> records)
    {
        for (int index = 0; index < records.Length; index++)
        {
            ref readonly RemoteWorldStateRecord record = ref records[index];
            if (record.RelationSequence == 0 || record.EntityGeneration == 0 ||
                record.CanonicalVersion == 0 || !record.IsPresent || record.Horizon == 0 ||
                record.Horizon > 3) return false;
            for (int prior = 0; prior < index; prior++)
                if (records[prior].EntityIndex == record.EntityIndex) return false;
            if (!writer.TryWriteUInt64(record.RelationSequence) ||
                !writer.TryWriteUInt32(record.EntityIndex) ||
                !writer.TryWriteUInt32(record.EntityGeneration) ||
                !writer.TryWriteUInt64(record.CanonicalVersion) ||
                !writer.TryWriteBoolean(true) || !writer.TryWriteByte(record.Horizon) ||
                !writer.TryWriteInt32(record.XMillimeters) ||
                !writer.TryWriteInt32(record.ZMillimeters) ||
                !writer.TryWriteInt32(record.TickMarker) ||
                !writer.TryWriteInt32(record.InputVelocity)) return false;
        }
        return true;
    }

    private static bool TryReadWorldRecords(
        ref NetworkPacketReader reader,
        Span<RemoteWorldStateRecord> records,
        int count)
    {
        for (int index = 0; index < count; index++)
        {
            if (!reader.TryReadUInt64(out ulong relationSequence) || relationSequence == 0 ||
                !reader.TryReadUInt32(out uint entityIndex) ||
                !reader.TryReadUInt32(out uint entityGeneration) || entityGeneration == 0 ||
                !reader.TryReadUInt64(out ulong canonicalVersion) || canonicalVersion == 0 ||
                !reader.TryReadBoolean(out bool present) || !present ||
                !reader.TryReadByte(out byte horizon) || horizon is 0 or > 3 ||
                !reader.TryReadInt32(out int x) || !reader.TryReadInt32(out int z) ||
                !reader.TryReadInt32(out int marker) || !reader.TryReadInt32(out int velocity))
            {
                records[..index].Clear();
                return false;
            }
            records[index] = new(relationSequence, entityIndex, entityGeneration,
                canonicalVersion, true, horizon, x, z, marker, velocity);
        }
        return true;
    }
}
