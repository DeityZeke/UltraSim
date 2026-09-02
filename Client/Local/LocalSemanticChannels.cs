using UltraSim.Client.Core;
using UltraSim.Game.Chat;
using UltraSim.Game.Replication;
using UltraSim.Game.World;
using UltraSim.Spatial;

namespace UltraSim.Client.Local;

internal enum LocalWorldDeliveryKind : byte
{
    Snapshot = 1,
    Delta = 2,
    ChunkAbsent = 3,
    PublicInterestRetired = 4,
    PublicationEpochReset = 5,
}

internal readonly record struct LocalDeliveryProof(ulong DeliveryId, LocalSemanticProofStatus Status);

internal sealed class LocalWorldDeliveryRow
{
    internal LocalWorldDeliveryRow(int maximumRecords)
    {
        Snapshot = new WorldPublicRecord[maximumRecords];
        Deltas = new WorldPublicDeltaRecord[maximumRecords];
    }

    internal ulong DeliveryId;
    internal LocalWorldDeliveryKind Kind;
    internal SpatialChunkKey Key;
    internal PublicationEpoch Epoch;
    internal ChunkPublicationBatch Batch;
    internal OfflineDirectChunkDeliveryTicket Ticket;
    internal int Count;
    internal readonly WorldPublicRecord[] Snapshot;
    internal readonly WorldPublicDeltaRecord[] Deltas;

    internal void Clear()
    {
        DeliveryId = 0;
        Kind = default;
        Key = default;
        Epoch = default;
        Batch = default;
        Ticket = default;
        if (Count != 0)
        {
            Snapshot.AsSpan(0, Count).Clear();
            Deltas.AsSpan(0, Count).Clear();
        }
        Count = 0;
    }
}

internal sealed class LocalWorldSemanticChannel
{
    private readonly object _gate = new();
    private readonly LocalWorldDeliveryRow[] _rows;
    private readonly int[] _free;
    private readonly bool[] _queued;
    private readonly BoundedSpscQueue<int> _ready;
    private readonly BoundedSpscQueue<LocalDeliveryProof> _proofs;
    private int _freeCount;
    private ulong _nextDeliveryId = 1;
    private ulong _applied;
    private ulong _rejected;

    internal LocalWorldSemanticChannel(LocalPlayableSessionLimits limits, int maximumRecords)
    {
        _rows = new LocalWorldDeliveryRow[limits.WorldDeliveryCapacity];
        _free = new int[_rows.Length];
        _queued = new bool[_rows.Length];
        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index] = new LocalWorldDeliveryRow(maximumRecords);
            _free[index] = _rows.Length - index - 1;
        }
        _freeCount = _free.Length;
        _ready = new BoundedSpscQueue<int>(_rows.Length);
        _proofs = new BoundedSpscQueue<LocalDeliveryProof>(limits.WorldProofCapacity);
    }

    internal int Pending { get { lock (_gate) return _rows.Length - _freeCount; } }
    internal int HighWater => _ready.HighWater;
    internal ulong Applied { get { lock (_gate) return _applied; } }
    internal ulong Rejected { get { lock (_gate) return _rejected; } }
    internal long ReservedBytes => checked(
        (long)_rows.Length * (_rows[0].Snapshot.Length *
            (System.Runtime.CompilerServices.Unsafe.SizeOf<WorldPublicRecord>() +
             System.Runtime.CompilerServices.Unsafe.SizeOf<WorldPublicDeltaRecord>()) + 192L) +
        (long)_free.Length * sizeof(int) + _queued.Length);

    internal bool HasOutstanding(SpatialChunkKey key)
    {
        lock (_gate)
        {
            if (_freeCount == _rows.Length) return false;
            for (int index = 0; index < _rows.Length; index++)
                if (_rows[index].DeliveryId != 0 && _rows[index].Key == key) return true;
            return false;
        }
    }

    internal bool CanPublish { get { lock (_gate) return _freeCount != 0; } }

    internal ulong NextDeliveryId
    {
        get
        {
            lock (_gate)
            {
                if (_nextDeliveryId == 0)
                    throw new InvalidOperationException("The local world delivery identity space is exhausted.");
                return _nextDeliveryId;
            }
        }
    }

    internal bool TryPublishRead(
        OfflineDirectChunkReadStatus status,
        in ChunkPublicationBatch batch,
        ReadOnlySpan<WorldPublicRecord> snapshot,
        ReadOnlySpan<WorldPublicDeltaRecord> deltas,
        in OfflineDirectChunkDeliveryTicket ticket)
    {
        LocalWorldDeliveryKind kind = status switch
        {
            OfflineDirectChunkReadStatus.Snapshot => LocalWorldDeliveryKind.Snapshot,
            OfflineDirectChunkReadStatus.Delta => LocalWorldDeliveryKind.Delta,
            OfflineDirectChunkReadStatus.ChunkAbsent => LocalWorldDeliveryKind.ChunkAbsent,
            _ => throw new ArgumentOutOfRangeException(nameof(status)),
        };
        lock (_gate)
        {
            if (_freeCount == 0 || ticket.DeliveryId != _nextDeliveryId) return false;
            int index = _free[--_freeCount];
            LocalWorldDeliveryRow row = _rows[index];
            row.DeliveryId = ticket.DeliveryId;
            row.Kind = kind;
            row.Key = ticket.Key;
            row.Epoch = ticket.SourceEpoch;
            row.Batch = batch;
            row.Ticket = ticket;
            row.Count = batch.Count;
            snapshot.CopyTo(row.Snapshot);
            deltas.CopyTo(row.Deltas);
            if (!PublishReady(index))
            {
                row.Clear();
                _free[_freeCount++] = index;
                return false;
            }
            AdvanceDeliveryId();
            return true;
        }
    }

    internal bool TryPublishControl(
        LocalWorldDeliveryKind kind,
        SpatialChunkKey key,
        PublicationEpoch epoch)
    {
        if (kind is not (LocalWorldDeliveryKind.PublicInterestRetired or
            LocalWorldDeliveryKind.PublicationEpochReset))
            throw new ArgumentOutOfRangeException(nameof(kind));
        lock (_gate)
        {
            if (_freeCount == 0 || _nextDeliveryId == 0) return false;
            int index = _free[--_freeCount];
            LocalWorldDeliveryRow row = _rows[index];
            row.DeliveryId = _nextDeliveryId;
            row.Kind = kind;
            row.Key = key;
            row.Epoch = epoch;
            if (!PublishReady(index))
            {
                row.Clear();
                _free[_freeCount++] = index;
                return false;
            }
            AdvanceDeliveryId();
            return true;
        }
    }

    internal bool TryApplyNext(ClientWorldSemanticCache cache, out SpatialChunkKey changed)
    {
        ArgumentNullException.ThrowIfNull(cache);
        changed = default;
        lock (_gate)
        {
            if (!_ready.TryPeek(out int index)) return false;
            if ((uint)index >= (uint)_rows.Length || !_queued[index]) return false;
            LocalWorldDeliveryRow row = _rows[index];
            bool accepted = Apply(cache, row, out changed);
            var proof = new LocalDeliveryProof(row.DeliveryId,
                accepted ? LocalSemanticProofStatus.Applied : LocalSemanticProofStatus.Rejected);
            if (!_proofs.TryEnqueue(proof)) return false;
            if (!_ready.TryDequeue(out int actual) || actual != index)
                throw new InvalidOperationException("The local world ready queue lost FIFO ownership.");
            _queued[index] = false;
            return true;
        }
    }

    internal bool TryTakeProof(out LocalDeliveryProof proof) => _proofs.TryDequeue(out proof);

    internal bool TryResolveProof(
        in LocalDeliveryProof proof,
        out OfflineDirectChunkDeliveryTicket ticket,
        out bool applied)
    {
        lock (_gate)
        {
            for (int index = 0; index < _rows.Length; index++)
            {
                LocalWorldDeliveryRow row = _rows[index];
                if (row.DeliveryId != proof.DeliveryId) continue;
                ticket = row.Ticket;
                applied = proof.Status == LocalSemanticProofStatus.Applied;
                if (applied)
                {
                    _applied++;
                    Release(index);
                }
                else
                {
                    _rejected++;
                    if (!_queued[index] && PublishReady(index)) { }
                }
                return true;
            }
        }
        ticket = default;
        applied = false;
        return false;
    }

    internal void ResetOutstanding()
    {
        lock (_gate)
        {
            _ready.Clear();
            _proofs.Clear();
            _freeCount = 0;
            for (int index = 0; index < _rows.Length; index++)
            {
                _rows[index].Clear();
                _queued[index] = false;
                _free[_freeCount++] = index;
            }
        }
    }

    private bool PublishReady(int index)
    {
        if (!_ready.TryEnqueue(index)) return false;
        _queued[index] = true;
        return true;
    }

    private void AdvanceDeliveryId() =>
        _nextDeliveryId = _nextDeliveryId == ulong.MaxValue ? 0 : _nextDeliveryId + 1;

    private void Release(int index)
    {
        _rows[index].Clear();
        _queued[index] = false;
        _free[_freeCount++] = index;
    }

    private static bool Apply(
        ClientWorldSemanticCache cache,
        LocalWorldDeliveryRow row,
        out SpatialChunkKey changed)
    {
        changed = row.Key;
        if (row.Kind == LocalWorldDeliveryKind.PublicationEpochReset)
        {
            cache.ResetPublicationEpoch(row.Epoch);
            return true;
        }
        if (row.Kind == LocalWorldDeliveryKind.PublicInterestRetired)
        {
            return !cache.TryGetResidentHandle(row.Key, out ClientSemanticChunkHandle retired) ||
                cache.TryRetirePublicInterest(retired, row.Epoch, out _);
        }
        if (!cache.TryGetResidentHandle(row.Key, out ClientSemanticChunkHandle handle))
        {
            if (row.Kind == LocalWorldDeliveryKind.ChunkAbsent) return true;
            if (!cache.TryRequest(row.Key, out handle, out _) ||
                !cache.TryStageFromBaseWorld(
                    handle,
                    row.Kind == LocalWorldDeliveryKind.Snapshot
                        ? row.Snapshot.AsSpan(0, row.Count)
                        : [],
                    row.Kind == LocalWorldDeliveryKind.Snapshot ? row.Batch.Cursor : default,
                    out _) ||
                !cache.TryPublish(handle, out _))
                return false;
            return row.Kind == LocalWorldDeliveryKind.Snapshot;
        }
        return row.Kind switch
        {
            LocalWorldDeliveryKind.Snapshot => cache.TryApplySnapshot(
                handle, row.Batch, row.Snapshot.AsSpan(0, row.Count), out _),
            LocalWorldDeliveryKind.Delta => cache.TryApplyDelta(
                handle, row.Batch, row.Deltas.AsSpan(0, row.Count), out _),
            LocalWorldDeliveryKind.ChunkAbsent => cache.TryApplyChunkAbsent(
                handle, row.Epoch, row.Batch, out _),
            _ => false,
        };
    }
}

internal sealed class LocalChatDeliveryRow
{
    internal ClientChatOccurrence Occurrence;
    internal bool Queued;
}

internal sealed class LocalChatSemanticChannel
{
    private readonly object _gate = new();
    private readonly ulong _applicationSessionEpoch;
    private readonly LocalChatDeliveryRow[] _rows;
    private readonly int[] _free;
    private readonly BoundedSpscQueue<int> _ready;
    private readonly BoundedSpscQueue<LocalDeliveryProof> _proofs;
    private int _freeCount;
    private ulong _nextDeliveryId = 1;
    private ulong _nextRecipientSequence = 1;
    private ulong _applied;
    private ulong _rejected;

    internal LocalChatSemanticChannel(
        LocalPlayableSessionLimits limits,
        ulong applicationSessionEpoch)
    {
        if (applicationSessionEpoch == 0)
            throw new ArgumentOutOfRangeException(nameof(applicationSessionEpoch));
        _applicationSessionEpoch = applicationSessionEpoch;
        _rows = new LocalChatDeliveryRow[limits.ChatDeliveryCapacity];
        _free = new int[_rows.Length];
        for (int index = 0; index < _rows.Length; index++)
        {
            _rows[index] = new LocalChatDeliveryRow();
            _free[index] = _rows.Length - index - 1;
        }
        _freeCount = _free.Length;
        _ready = new BoundedSpscQueue<int>(_rows.Length);
        _proofs = new BoundedSpscQueue<LocalDeliveryProof>(limits.ChatProofCapacity);
    }

    internal bool CanPublish { get { lock (_gate) return _freeCount != 0; } }
    internal int Pending { get { lock (_gate) return _rows.Length - _freeCount; } }
    internal int HighWater => _ready.HighWater;
    internal ulong Applied { get { lock (_gate) return _applied; } }
    internal ulong Rejected { get { lock (_gate) return _rejected; } }
    internal long ReservedBytes => checked(
        (long)_rows.Length * (System.Runtime.CompilerServices.Unsafe.SizeOf<ClientChatOccurrence>() +
            sizeof(bool) + sizeof(int)));

    internal bool TryPublishSpatial(in GameSpeechFact fact)
    {
        var occurrence = new ClientChatOccurrence(
            fact.GenerationId,
            NextDeliveryId(),
            NextRecipientSequence(),
            fact.Id.Value,
            fact.SimulationTick,
            ClientChatOccurrenceKind.SpatialSpeech,
            (ClientChatSpeechMode)fact.Mode,
            fact.PublicAnchor,
            fact.DisplayLabel,
            fact.Text);
        return TryPublish(occurrence);
    }

    internal bool TryPublishPrivate(
        ulong generationId,
        ulong simulationTick,
        string label,
        string text,
        bool error)
    {
        var occurrence = new ClientChatOccurrence(
            generationId,
            NextDeliveryId(),
            NextRecipientSequence(),
            0,
            simulationTick,
            error ? ClientChatOccurrenceKind.PrivateError : ClientChatOccurrenceKind.PrivateSystem,
            ClientChatSpeechMode.None,
            default,
            label,
            text);
        return TryPublish(occurrence);
    }

    internal bool TryApplyNext(ClientChatState client)
    {
        ArgumentNullException.ThrowIfNull(client);
        lock (_gate)
        {
            if (!_ready.TryPeek(out int index)) return false;
            if ((uint)index >= (uint)_rows.Length || !_rows[index].Queued) return false;
            ClientChatOccurrence occurrence = _rows[index].Occurrence;
            ClientChatApplyStatus result =
                client.GetSnapshot().ApplicationSessionEpoch == _applicationSessionEpoch
                    ? client.TryApply(occurrence)
                    : ClientChatApplyStatus.Rejected;
            LocalSemanticProofStatus status = result is
                ClientChatApplyStatus.Applied or ClientChatApplyStatus.Duplicate
                    ? LocalSemanticProofStatus.Applied
                    : LocalSemanticProofStatus.Rejected;
            if (!_proofs.TryEnqueue(new LocalDeliveryProof(occurrence.DeliveryId, status))) return false;
            if (!_ready.TryDequeue(out int actual) || actual != index)
                throw new InvalidOperationException("The local Chat ready queue lost FIFO ownership.");
            _rows[index].Queued = false;
            return true;
        }
    }

    internal bool TryTakeProof(out LocalDeliveryProof proof) => _proofs.TryDequeue(out proof);

    internal void ResolveProof(in LocalDeliveryProof proof)
    {
        lock (_gate)
        {
            for (int index = 0; index < _rows.Length; index++)
            {
                LocalChatDeliveryRow row = _rows[index];
                if (row.Occurrence.DeliveryId != proof.DeliveryId) continue;
                if (proof.Status == LocalSemanticProofStatus.Applied)
                {
                    _applied++;
                    row.Occurrence = default;
                    row.Queued = false;
                    _free[_freeCount++] = index;
                }
                else
                {
                    _rejected++;
                    if (!row.Queued && _ready.TryEnqueue(index)) row.Queued = true;
                }
                return;
            }
        }
    }

    private bool TryPublish(in ClientChatOccurrence occurrence)
    {
        lock (_gate)
        {
            if (_freeCount == 0) return false;
            int index = _free[--_freeCount];
            LocalChatDeliveryRow row = _rows[index];
            row.Occurrence = occurrence;
            if (!_ready.TryEnqueue(index))
            {
                row.Occurrence = default;
                _free[_freeCount++] = index;
                return false;
            }
            row.Queued = true;
            _nextDeliveryId = _nextDeliveryId == ulong.MaxValue ? 0 : _nextDeliveryId + 1;
            _nextRecipientSequence = _nextRecipientSequence == ulong.MaxValue ? 0 : _nextRecipientSequence + 1;
            return true;
        }
    }

    private ulong NextDeliveryId()
    {
        lock (_gate)
        {
            if (_nextDeliveryId == 0)
                throw new InvalidOperationException("The local Chat delivery identity space is exhausted.");
            return _nextDeliveryId;
        }
    }

    private ulong NextRecipientSequence()
    {
        lock (_gate)
        {
            if (_nextRecipientSequence == 0)
                throw new InvalidOperationException("The local Chat sequence identity space is exhausted.");
            return _nextRecipientSequence;
        }
    }
}
