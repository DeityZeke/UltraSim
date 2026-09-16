using System.Diagnostics;
using System.Runtime.CompilerServices;
using UltraSim.Game.Protocol;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Game.Replication;

public enum RemoteChunkDeltaBodySharingMode : byte
{
    Disabled = 0,
    CensusOnly = 1,
    Enabled = 2,
}

public readonly record struct RemoteChunkDeltaBodyStoreLimits(
    int BodyCapacity,
    int MaximumRecordsPerBody,
    int Associativity)
{
    public const int MaximumBodyCapacity = 4_096;
    public static RemoteChunkDeltaBodyStoreLimits Default => new(
        1_024, UltraSimGameProtocol.MaximumChunkPublicationRecords, 4);

    internal void Validate()
    {
        if (BodyCapacity is <= 0 or > MaximumBodyCapacity ||
            (BodyCapacity & (BodyCapacity - 1)) != 0 ||
            MaximumRecordsPerBody is <= 0 or > UltraSimGameProtocol.MaximumChunkPublicationRecords ||
            Associativity <= 0 || (Associativity & (Associativity - 1)) != 0 ||
            Associativity > BodyCapacity || BodyCapacity % Associativity != 0)
            throw new ArgumentOutOfRangeException(nameof(BodyCapacity),
                "Body storage requires power-of-two capacity/associativity and protocol-bounded records.");
    }
}

/// <summary>
/// Exact source-derived identity of one immutable protocol Delta body. Recipient/session/delivery
/// state is deliberately absent. The owning store additionally fences identity to one physical
/// <see cref="ChunkPublicationStore"/> lifetime.
/// </summary>
public readonly record struct RemoteChunkDeltaBodyIdentity(
    PublicationEpoch SourceEpoch,
    SpatialChunkKey Key,
    ulong BaseRevision,
    ulong CurrentRevision,
    ulong HistoryFloor)
{
    public bool IsValid => SourceEpoch.IsValid && Key.Space.IsValid && BaseRevision != 0 &&
        CurrentRevision > BaseRevision && HistoryFloor != 0;
}

public readonly record struct RemoteChunkDeltaBodyStoreSnapshot(
    RemoteChunkDeltaBodySharingMode Mode,
    int BodyCapacity,
    int RecordCapacity,
    int MaximumRecordsPerBody,
    int Associativity,
    int CurrentBodies,
    int HighWaterBodies,
    int CurrentRecords,
    int HighWaterRecords,
    ulong BodyRequests,
    ulong BodyHits,
    ulong BodyMisses,
    ulong BodyFills,
    ulong BodyFillFailures,
    ulong BodyEvictions,
    ulong BodyIdentityCollisions,
    ulong BodyRecordsRequested,
    ulong DistinctBodyIdentities,
    ulong BodyRecordsBuilt,
    ulong BodyRecordsServed,
    ulong MaterializationsAvoided,
    ulong ValidationHashCallsAvoided,
    ulong HashRecordsAvoided,
    ulong BodyCopies,
    ulong BodyCopyRecords,
    ulong EncodedBodyBuilds,
    ulong EncodedBodyCopies,
    ulong EncodedBodyBytesCopied,
    ulong LookupTicks,
    ulong FillTicks,
    ulong CopyTicks,
    ulong MaximumRequestsPerBody,
    ulong RequestsPerBodyP50,
    ulong RequestsPerBodyP95,
    ulong RequestsPerBodyP99,
    int LookupProbeHighWater,
    long MetadataBytes,
    long RecordStorageBytes,
    long EncodedRecordStorageBytes,
    long DiagnosticBytes,
    long ReservedBytes);

internal enum RemoteChunkDeltaBodyPublishStatus : byte
{
    Published = 1,
    ExistingExact = 2,
    IdentityConflict = 3,
    CapacityExceeded = 4,
}

/// <summary>
/// One owner-thread, fixed-capacity, four-way-by-default immutable Delta-body store. It is an
/// optimization copy only: it owns no recipient knowledge and cannot pin source history.
/// </summary>
public sealed class RemoteChunkDeltaBodyStore
{
    private readonly ChunkPublicationStore _source;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private readonly RemoteChunkDeltaBodyStoreLimits _limits;
    private readonly RemoteChunkDeltaBodyIdentity[] _identities;
    private readonly byte[] _encodedRecords;
    private readonly ulong[] _contentHashes;
    private readonly ulong[] _requestsPerBody;
    private readonly ushort[] _recordCounts;
    private readonly bool[] _valid;
    private readonly ushort[] _nextVictim;
    private readonly RemoteChunkDeltaBodySharingMode _mode;
    private bool _detailedTiming;
    private int _currentBodies;
    private int _highWaterBodies;
    private int _currentRecords;
    private int _highWaterRecords;
    private ulong _bodyRequests;
    private ulong _bodyHits;
    private ulong _bodyMisses;
    private ulong _bodyFills;
    private ulong _bodyFillFailures;
    private ulong _bodyEvictions;
    private ulong _bodyIdentityCollisions;
    private ulong _bodyRecordsRequested;
    private ulong _distinctBodyIdentities;
    private ulong _bodyRecordsBuilt;
    private ulong _bodyRecordsServed;
    private ulong _materializationsAvoided;
    private ulong _validationHashCallsAvoided;
    private ulong _hashRecordsAvoided;
    private ulong _bodyCopies;
    private ulong _bodyCopyRecords;
    private ulong _encodedBodyBuilds;
    private ulong _encodedBodyCopies;
    private ulong _encodedBodyBytesCopied;
    private ulong _lookupTicks;
    private ulong _fillTicks;
    private ulong _copyTicks;
    private int _lookupProbeHighWater;

    public RemoteChunkDeltaBodyStore(
        ChunkPublicationStore source,
        RemoteChunkDeltaBodySharingMode mode = RemoteChunkDeltaBodySharingMode.Enabled,
        RemoteChunkDeltaBodyStoreLimits limits = default)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (limits == default) limits = RemoteChunkDeltaBodyStoreLimits.Default;
        limits.Validate();
        _mode = mode;
        if (mode == RemoteChunkDeltaBodySharingMode.Disabled)
        {
            _limits = default;
            _identities = [];
            _encodedRecords = [];
            _contentHashes = [];
            _requestsPerBody = [];
            _recordCounts = [];
            _valid = [];
            _nextVictim = [];
            return;
        }
        _limits = limits;
        _identities = new RemoteChunkDeltaBodyIdentity[limits.BodyCapacity];
        _encodedRecords = new byte[checked(
            limits.BodyCapacity * limits.MaximumRecordsPerBody *
            RemoteChunkDeltaRecord.EncodedSize)];
        _contentHashes = new ulong[limits.BodyCapacity];
        _requestsPerBody = new ulong[limits.BodyCapacity];
        _recordCounts = new ushort[limits.BodyCapacity];
        _valid = new bool[limits.BodyCapacity];
        _nextVictim = new ushort[limits.BodyCapacity / limits.Associativity];
    }

    public RemoteChunkDeltaBodySharingMode Mode => _mode;
    public bool IsForSource(ChunkPublicationStore source) => ReferenceEquals(_source, source);

    public void ConfigureDetailedTiming(bool detailedTiming)
    {
        EnsureOwner();
        _detailedTiming = detailedTiming;
    }

    public void ResetStatistics()
    {
        EnsureOwner();
        _bodyRequests = _bodyHits = _bodyMisses = 0;
        _bodyFills = _bodyFillFailures = _bodyEvictions = 0;
        _bodyIdentityCollisions = 0;
        _bodyRecordsRequested = _bodyRecordsBuilt = _bodyRecordsServed = 0;
        _distinctBodyIdentities = 0;
        _materializationsAvoided = _validationHashCallsAvoided = _hashRecordsAvoided = 0;
        _bodyCopies = _bodyCopyRecords = 0;
        _encodedBodyBuilds = _encodedBodyCopies = _encodedBodyBytesCopied = 0;
        _lookupTicks = _fillTicks = _copyTicks = 0;
        _lookupProbeHighWater = 0;
        Array.Clear(_requestsPerBody);
    }

    internal bool TryCopy(
        ChunkPublicationStore source,
        in RemoteChunkDeltaBodyIdentity identity,
        Span<RemoteChunkDeltaRecord> destination,
        out int recordCount,
        out ulong contentHash)
    {
        int maximumBytes = checked(
            _limits.MaximumRecordsPerBody * RemoteChunkDeltaRecord.EncodedSize);
        Span<byte> encoded = maximumBytes <= 4_096
            ? stackalloc byte[maximumBytes]
            : new byte[maximumBytes];
        if (!TryCopyEncoded(
                source, in identity, encoded, out recordCount,
                out int encodedByteCount, out contentHash))
            return false;
        if (destination.Length < recordCount ||
            !UltraSimGameProtocol.TryReadPreparedDeltaRecordBytes(
                encoded[..encodedByteCount], destination, out int decoded) ||
            decoded != recordCount)
            return false;
        return true;
    }

    internal bool TryCopyEncoded(
        ChunkPublicationStore source,
        in RemoteChunkDeltaBodyIdentity identity,
        Span<byte> encodedDestination,
        out int recordCount,
        out int encodedByteCount,
        out ulong contentHash)
    {
        EnsureOwner();
        recordCount = 0;
        encodedByteCount = 0;
        contentHash = 0;
        if (_mode != RemoteChunkDeltaBodySharingMode.Enabled ||
            !ReferenceEquals(_source, source) || !identity.IsValid ||
            identity.SourceEpoch != source.Epoch ||
            encodedDestination.IsEmpty)
            return false;
        SaturatingIncrement(ref _bodyRequests);
        long lookupStarted = Timestamp();
        int slot = Find(identity, out int probes);
        Elapsed(ref _lookupTicks, lookupStarted);
        _lookupProbeHighWater = Math.Max(_lookupProbeHighWater, probes);
        if (slot < 0)
        {
            SaturatingIncrement(ref _bodyMisses);
            return false;
        }
        int retainedRecordCount = _recordCounts[slot];
        int retainedEncodedBytes = checked(
            retainedRecordCount * RemoteChunkDeltaRecord.EncodedSize);
        if (retainedRecordCount <= 0 || retainedRecordCount > _limits.MaximumRecordsPerBody ||
            encodedDestination.Length < retainedEncodedBytes)
        {
            SaturatingIncrement(ref _bodyIdentityCollisions);
            SaturatingIncrement(ref _bodyMisses);
            return false;
        }
        SaturatingAdd(ref _bodyRecordsRequested, checked((ulong)retainedRecordCount));
        if (_requestsPerBody[slot] == 0)
            SaturatingIncrement(ref _distinctBodyIdentities);
        long copyStarted = Timestamp();
        EncodedRecords(slot, retainedEncodedBytes).CopyTo(encodedDestination);
        SaturatingIncrement(ref _encodedBodyCopies);
        SaturatingAdd(ref _encodedBodyBytesCopied, checked((ulong)retainedEncodedBytes));
        Elapsed(ref _copyTicks, copyStarted);
        recordCount = retainedRecordCount;
        encodedByteCount = retainedEncodedBytes;
        contentHash = _contentHashes[slot];
        SaturatingIncrement(ref _bodyHits);
        SaturatingIncrement(ref _requestsPerBody[slot]);
        SaturatingAdd(ref _bodyRecordsServed, checked((ulong)retainedRecordCount));
        SaturatingIncrement(ref _materializationsAvoided);
        SaturatingIncrement(ref _validationHashCallsAvoided);
        SaturatingAdd(ref _hashRecordsAvoided, checked((ulong)retainedRecordCount));
        SaturatingIncrement(ref _bodyCopies);
        SaturatingAdd(ref _bodyCopyRecords, checked((ulong)retainedRecordCount));
        return true;
    }

    internal bool TryCopy(
        ChunkPublicationStore source,
        in RemoteChunkDeltaBodyIdentity identity,
        int expectedRecordCount,
        Span<RemoteChunkDeltaRecord> destination,
        out ulong contentHash)
    {
        bool copied = TryCopy(
            source, in identity, destination, out int recordCount, out contentHash);
        if (!copied || recordCount == expectedRecordCount) return copied;
        SaturatingIncrement(ref _bodyIdentityCollisions);
        return false;
    }

    internal RemoteChunkDeltaBodyPublishStatus PublishPrepared(
        ChunkPublicationStore source,
        in RemoteChunkDeltaBodyIdentity identity,
        ReadOnlySpan<RemoteChunkDeltaRecord> records,
        ulong contentHash,
        bool requestAlreadyCounted) => PublishPreparedCore(
            source, in identity, records, default, contentHash, requestAlreadyCounted);

    internal RemoteChunkDeltaBodyPublishStatus PublishPrepared(
        ChunkPublicationStore source,
        in RemoteChunkDeltaBodyIdentity identity,
        ReadOnlySpan<RemoteChunkDeltaRecord> records,
        ReadOnlySpan<byte> encodedRecords,
        ulong contentHash,
        bool requestAlreadyCounted) => PublishPreparedCore(
            source, in identity, records, encodedRecords, contentHash, requestAlreadyCounted);

    private RemoteChunkDeltaBodyPublishStatus PublishPreparedCore(
        ChunkPublicationStore source,
        in RemoteChunkDeltaBodyIdentity identity,
        ReadOnlySpan<RemoteChunkDeltaRecord> records,
        ReadOnlySpan<byte> encodedRecords,
        ulong contentHash,
        bool requestAlreadyCounted)
    {
        EnsureOwner();
        if (_mode == RemoteChunkDeltaBodySharingMode.Disabled ||
            !ReferenceEquals(_source, source) || !identity.IsValid || records.IsEmpty ||
            identity.SourceEpoch != source.Epoch ||
            records.Length > _limits.MaximumRecordsPerBody)
        {
            SaturatingIncrement(ref _bodyFillFailures);
            return RemoteChunkDeltaBodyPublishStatus.CapacityExceeded;
        }
        int encodedByteCount = checked(records.Length * RemoteChunkDeltaRecord.EncodedSize);
        bool callerEncoded = !encodedRecords.IsEmpty;
        if (callerEncoded && encodedRecords.Length != encodedByteCount)
        {
            SaturatingIncrement(ref _bodyFillFailures);
            return RemoteChunkDeltaBodyPublishStatus.CapacityExceeded;
        }
        Span<byte> synthesized = stackalloc byte[
            _limits.MaximumRecordsPerBody * RemoteChunkDeltaRecord.EncodedSize];
        int synthesizedBytes = 0;
        if (!callerEncoded)
        {
            if (!UltraSimGameProtocol.TryWritePreparedDeltaRecordBytes(
                    records, synthesized, out synthesizedBytes) ||
                synthesizedBytes != encodedByteCount)
                throw new InvalidOperationException(
                    "Validated Delta records failed canonical retained byte encoding.");
        }
        if (!requestAlreadyCounted)
        {
            SaturatingIncrement(ref _bodyRequests);
            SaturatingAdd(ref _bodyRecordsRequested, checked((ulong)records.Length));
        }
        long fillStarted = Timestamp();
        int existing = Find(identity, out int probes);
        _lookupProbeHighWater = Math.Max(_lookupProbeHighWater, probes);
        if (existing >= 0)
        {
            bool exact = _recordCounts[existing] == records.Length &&
                _contentHashes[existing] == contentHash &&
                (callerEncoded
                    ? EncodedRecords(existing, encodedByteCount).SequenceEqual(encodedRecords)
                    : EncodedRecords(existing, encodedByteCount).SequenceEqual(
                        synthesized[..synthesizedBytes]));
            if (!exact)
            {
                SaturatingIncrement(ref _bodyIdentityCollisions);
                SaturatingIncrement(ref _bodyFillFailures);
                Elapsed(ref _fillTicks, fillStarted);
                return RemoteChunkDeltaBodyPublishStatus.IdentityConflict;
            }
            if (_requestsPerBody[existing] == 0)
                SaturatingIncrement(ref _distinctBodyIdentities);
            SaturatingIncrement(ref _bodyHits);
            SaturatingIncrement(ref _requestsPerBody[existing]);
            SaturatingAdd(ref _bodyRecordsServed, checked((ulong)records.Length));
            Elapsed(ref _fillTicks, fillStarted);
            return RemoteChunkDeltaBodyPublishStatus.ExistingExact;
        }

        if (!requestAlreadyCounted) SaturatingIncrement(ref _bodyMisses);
        int set = Set(identity);
        int start = checked(set * _limits.Associativity);
        int slot = -1;
        for (int way = 0; way < _limits.Associativity; way++)
            if (!_valid[start + way])
            {
                slot = start + way;
                break;
            }
        if (slot < 0)
        {
            int victim = _nextVictim[set] & (_limits.Associativity - 1);
            _nextVictim[set] = checked((ushort)((victim + 1) & (_limits.Associativity - 1)));
            slot = start + victim;
            _currentRecords -= _recordCounts[slot];
            SaturatingIncrement(ref _bodyEvictions);
        }
        else
        {
            _currentBodies++;
            _highWaterBodies = Math.Max(_highWaterBodies, _currentBodies);
        }

        // Publish validity last. No reader can observe the partially copied replacement.
        _valid[slot] = false;
        Span<byte> encodedSlot = _encodedRecords.AsSpan(
            checked(slot * _limits.MaximumRecordsPerBody *
                RemoteChunkDeltaRecord.EncodedSize), encodedByteCount);
        if (callerEncoded) encodedRecords.CopyTo(encodedSlot);
        else synthesized[..synthesizedBytes].CopyTo(encodedSlot);
        _identities[slot] = identity;
        _contentHashes[slot] = contentHash;
        _recordCounts[slot] = checked((ushort)records.Length);
        _requestsPerBody[slot] = 1;
        SaturatingIncrement(ref _distinctBodyIdentities);
        _valid[slot] = true;
        SaturatingIncrement(ref _encodedBodyBuilds);
        _currentRecords += records.Length;
        _highWaterRecords = Math.Max(_highWaterRecords, _currentRecords);
        SaturatingIncrement(ref _bodyFills);
        SaturatingAdd(ref _bodyRecordsBuilt, checked((ulong)records.Length));
        Elapsed(ref _fillTicks, fillStarted);
        return RemoteChunkDeltaBodyPublishStatus.Published;
    }

    public RemoteChunkDeltaBodyStoreSnapshot GetSnapshot()
    {
        EnsureOwner();
        Span<ulong> requests = _limits.BodyCapacity <= 1_024
            ? stackalloc ulong[_limits.BodyCapacity]
            : new ulong[_limits.BodyCapacity];
        int count = 0;
        ulong maximum = 0;
        for (int index = 0; index < _valid.Length; index++)
            if (_valid[index] && _requestsPerBody[index] != 0)
            {
                ulong value = _requestsPerBody[index];
                requests[count++] = value;
                maximum = Math.Max(maximum, value);
            }
        requests[..count].Sort();
        ulong p50 = Percentile(requests[..count], 50);
        ulong p95 = Percentile(requests[..count], 95);
        ulong p99 = Percentile(requests[..count], 99);
        long metadata = checked(
            (long)_identities.Length * Unsafe.SizeOf<RemoteChunkDeltaBodyIdentity>() +
            (long)_contentHashes.Length * sizeof(ulong) +
            (long)(_recordCounts.Length + _nextVictim.Length) * sizeof(ushort) +
            _valid.LongLength);
        long recordStorage = 0;
        long encodedRecordStorage = _encodedRecords.LongLength;
        long diagnostics = checked((long)_requestsPerBody.Length * sizeof(ulong));
        return new RemoteChunkDeltaBodyStoreSnapshot(
            _mode, _limits.BodyCapacity,
            checked(_limits.BodyCapacity * _limits.MaximumRecordsPerBody),
            _limits.MaximumRecordsPerBody,
            _limits.Associativity, _currentBodies, _highWaterBodies, _currentRecords,
            _highWaterRecords, _bodyRequests, _bodyHits, _bodyMisses, _bodyFills,
            _bodyFillFailures, _bodyEvictions, _bodyIdentityCollisions,
            _bodyRecordsRequested, _distinctBodyIdentities,
            _bodyRecordsBuilt, _bodyRecordsServed,
            _materializationsAvoided, _validationHashCallsAvoided, _hashRecordsAvoided,
            _bodyCopies, _bodyCopyRecords,
            _encodedBodyBuilds, _encodedBodyCopies, _encodedBodyBytesCopied,
            _lookupTicks, _fillTicks, _copyTicks,
            maximum, p50, p95, p99, _lookupProbeHighWater, metadata, recordStorage,
            encodedRecordStorage, diagnostics,
            checked(metadata + recordStorage + encodedRecordStorage + diagnostics));
    }

    private ReadOnlySpan<byte> EncodedRecords(int slot, int byteCount) =>
        _encodedRecords.AsSpan(checked(
            slot * _limits.MaximumRecordsPerBody * RemoteChunkDeltaRecord.EncodedSize), byteCount);

    private int Find(in RemoteChunkDeltaBodyIdentity identity, out int probes)
    {
        int start = checked(Set(identity) * _limits.Associativity);
        probes = 0;
        for (int way = 0; way < _limits.Associativity; way++)
        {
            probes++;
            int slot = start + way;
            if (_valid[slot] && _identities[slot] == identity) return slot;
        }
        return -1;
    }

    private int Set(in RemoteChunkDeltaBodyIdentity identity)
    {
        ulong value = identity.SourceEpoch.Value;
        Mix(ref value, identity.Key.Space.Value);
        Mix(ref value, unchecked((uint)identity.Key.Coord.X));
        Mix(ref value, unchecked((uint)identity.Key.Coord.Z));
        Mix(ref value, identity.BaseRevision);
        Mix(ref value, identity.CurrentRevision);
        Mix(ref value, identity.HistoryFloor);
        unchecked
        {
            value ^= value >> 33;
            value *= 0xff51afd7ed558ccdUL;
            value ^= value >> 33;
        }
        return (int)(value & checked((ulong)(_limits.BodyCapacity / _limits.Associativity - 1)));
    }

    private static void Mix(ref ulong value, ulong item)
    {
        unchecked
        {
            value ^= item + 0x9E3779B97F4A7C15UL + (value << 6) + (value >> 2);
        }
    }

    private long Timestamp() => _detailedTiming ? Stopwatch.GetTimestamp() : 0;
    private void Elapsed(ref ulong destination, long started)
    {
        if (!_detailedTiming) return;
        SaturatingAdd(ref destination, checked((ulong)Math.Max(0, Stopwatch.GetTimestamp() - started)));
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("The shared Delta-body store belongs to another thread.");
    }

    private static ulong Percentile(ReadOnlySpan<ulong> sorted, int percentile)
    {
        if (sorted.IsEmpty) return 0;
        int index = checked((int)Math.Ceiling(sorted.Length * percentile / 100d) - 1);
        return sorted[Math.Clamp(index, 0, sorted.Length - 1)];
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private static void SaturatingAdd(ref ulong value, ulong addend) =>
        value = ulong.MaxValue - value < addend ? ulong.MaxValue : value + addend;
}
