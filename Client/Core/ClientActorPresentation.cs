using System.Text;
using UltraSim.Game.Composition;
using UltraSim.Game.Inspection;
using UltraSim.Game.World;

namespace UltraSim.Client.Core;

/// <summary>Finite renderer-neutral actor body presentations supported by the current client.</summary>
public enum ClientActorVisualKind : byte
{
    HumanX = 1,
    HumanY = 2,
    WoodlandAnimal = 3,
}

/// <summary>
/// Cold presentation definition selected by an authoritative public prefab identity. These values
/// are presentation calibration, never Physics body dimensions or Game authority.
/// </summary>
public readonly record struct ClientActorDefinition(
    uint PrefabId,
    ClientActorVisualKind VisualKind,
    ushort VisualScalePermille,
    int CameraPivotHeightMillimeters,
    int NameplateHeightMillimeters,
    int EffectsHeightMillimeters,
    bool NameplateEligible,
    string DefaultDisplayLabel)
{
    public const int MaximumDefaultDisplayLabelUtf8Bytes = 128;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public bool IsValid =>
        PrefabId != 0 &&
        VisualKind is >= ClientActorVisualKind.HumanX and <= ClientActorVisualKind.WoodlandAnimal &&
        VisualScalePermille is >= 100 and <= 10_000 &&
        CameraPivotHeightMillimeters is >= 0 and <= 20_000 &&
        NameplateHeightMillimeters is >= 0 and <= 20_000 &&
        EffectsHeightMillimeters is >= 0 and <= 20_000 &&
        IsCanonicalLabel(DefaultDisplayLabel, MaximumDefaultDisplayLabelUtf8Bytes);

    internal static bool IsCanonicalLabel(string? value, int maximumUtf8Bytes)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim()) return false;
        foreach (char character in value)
            if (char.IsControl(character)) return false;
        try { return StrictUtf8.GetByteCount(value) <= maximumUtf8Bytes; }
        catch (EncoderFallbackException) { return false; }
    }
}

/// <summary>Immutable bounded public-prefab to actor-presentation mapping.</summary>
public sealed class ClientActorDefinitionCatalog
{
    public const int MaximumDefinitions = 64;
    private readonly ClientActorDefinition[] _definitions;

    public ClientActorDefinitionCatalog(IEnumerable<ClientActorDefinition> definitions)
    {
        ArgumentNullException.ThrowIfNull(definitions);
        _definitions = definitions.OrderBy(static value => value.PrefabId).ToArray();
        if (_definitions.Length is 0 or > MaximumDefinitions)
            throw new ArgumentOutOfRangeException(nameof(definitions));
        for (int index = 0; index < _definitions.Length; index++)
        {
            if (!_definitions[index].IsValid ||
                index != 0 && _definitions[index - 1].PrefabId == _definitions[index].PrefabId)
                throw new ArgumentException(
                    "Actor definitions must be valid and unique by public prefab.", nameof(definitions));
        }
    }

    public int Count => _definitions.Length;

    public bool TryGet(uint prefabId, out ClientActorDefinition definition)
    {
        int low = 0;
        int high = _definitions.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            ref readonly ClientActorDefinition candidate = ref _definitions[middle];
            int order = candidate.PrefabId.CompareTo(prefabId);
            if (order == 0)
            {
                definition = candidate;
                return true;
            }
            if (order < 0) low = middle + 1;
            else high = middle - 1;
        }
        definition = default;
        return false;
    }

    public static ClientActorDefinitionCatalog ConceptDemo { get; } = new(
    [
        new ClientActorDefinition(
            GameWorldPublicPrefabIds.Character,
            ClientActorVisualKind.HumanX,
            1_000,
            1_600,
            2_000,
            900,
            true,
            "Human X"),
        new ClientActorDefinition(
            GameWorldPublicPrefabIds.CharacterMale,
            ClientActorVisualKind.HumanY,
            1_000,
            1_600,
            2_000,
            900,
            true,
            "Human Y"),
        new ClientActorDefinition(
            GameWorldPublicPrefabIds.WoodlandAnimal,
            ClientActorVisualKind.WoodlandAnimal,
            1_000,
            650,
            1_350,
            550,
            true,
            "Woodland Animal"),
    ]);
}

[Flags]
public enum ClientPublicPresentationFlags : byte
{
    None = 0,
    NameplateEligible = 1 << 0,
    ItemProperties = 1 << 1,
}

public enum ClientPublicPresentationOperation : byte
{
    Present = 1,
    Remove = 2,
}

/// <summary>
/// Relatively cold authoritative presentation metadata. It is deliberately separate from every
/// hot movement record and is fenced by full public identity plus server generation.
/// </summary>
public readonly record struct ClientPublicPresentationMetadata(
    ulong ServerGenerationId,
    WorldPublicEntityId Id,
    ulong Revision,
    ClientPublicPresentationFlags Flags,
    string DisplayLabel,
    InspectionPropertySet? InspectionProperties = null)
{
    public const int MaximumDisplayLabelUtf8Bytes = 128;
    public const ClientPublicPresentationFlags AdmittedFlags =
        ClientPublicPresentationFlags.NameplateEligible |
        ClientPublicPresentationFlags.ItemProperties;

    public bool IsValid =>
        ServerGenerationId != 0 && Id.IsValid && Revision != 0 &&
        Flags != ClientPublicPresentationFlags.None &&
        (Flags & ~AdmittedFlags) == 0 &&
        ClientActorDefinition.IsCanonicalLabel(DisplayLabel, MaximumDisplayLabelUtf8Bytes) &&
        ((Flags & ClientPublicPresentationFlags.ItemProperties) != 0
            ? InspectionProperties is not null
            : InspectionProperties is null);
}

public enum ClientPublicPresentationApplyStatus : byte
{
    Applied = 1,
    Duplicate = 2,
    Rejected = 3,
    CapacityExceeded = 4,
}

public readonly record struct ClientPublicPresentationMetadataSnapshot(
    ulong ServerGenerationId,
    int CurrentRecords,
    int KnownIdentityFloors,
    int Capacity,
    ulong Applied,
    ulong Removed,
    ulong Duplicates,
    ulong Rejected,
    ulong GenerationResets,
    long ReservedBytes);

/// <summary>
/// Bounded ABA-safe cold metadata cache. Identity-generation floors remain after removal so an old
/// delayed row cannot attach a prior name to a later public-slot lifetime.
/// </summary>
public sealed class ClientPublicPresentationMetadataState
{
    private readonly MetadataEntry[] _entries;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _serverGenerationId;
    private int _known;
    private int _current;
    private ulong _applied;
    private ulong _removed;
    private ulong _duplicates;
    private ulong _rejected;
    private ulong _generationResets;

    public ClientPublicPresentationMetadataState(int capacity = 256)
    {
        if (capacity <= 0) throw new ArgumentOutOfRangeException(nameof(capacity));
        _entries = new MetadataEntry[capacity];
    }

    public void ResetServerGeneration(ulong serverGenerationId)
    {
        EnsureOwner();
        ArgumentOutOfRangeException.ThrowIfZero(serverGenerationId);
        if (_serverGenerationId == serverGenerationId) return;
        Array.Clear(_entries);
        _serverGenerationId = serverGenerationId;
        _known = 0;
        _current = 0;
        SaturatingIncrement(ref _generationResets);
    }

    public ClientPublicPresentationApplyStatus TryApply(
        ClientPublicPresentationOperation operation,
        in ClientPublicPresentationMetadata metadata)
    {
        EnsureOwner();
        if (_serverGenerationId == 0 || metadata.ServerGenerationId != _serverGenerationId ||
            operation is not (ClientPublicPresentationOperation.Present or
                ClientPublicPresentationOperation.Remove) ||
            operation == ClientPublicPresentationOperation.Present && !metadata.IsValid ||
            operation == ClientPublicPresentationOperation.Remove &&
                (!metadata.Id.IsValid || metadata.Revision == 0 ||
                 metadata.ServerGenerationId == 0 || metadata.Flags != 0 ||
                 !string.IsNullOrEmpty(metadata.DisplayLabel) ||
                 metadata.InspectionProperties is not null))
            return Reject();

        int slot = Find(metadata.Id.Index);
        if (slot < 0)
        {
            slot = FindFree();
            if (slot < 0) return ClientPublicPresentationApplyStatus.CapacityExceeded;
            _entries[slot].Used = true;
            _entries[slot].Index = metadata.Id.Index;
            _known++;
        }

        ref MetadataEntry entry = ref _entries[slot];
        int generationOrder = metadata.Id.Generation.CompareTo(entry.Generation);
        if (generationOrder < 0) return Reject();
        if (generationOrder == 0)
        {
            int revisionOrder = metadata.Revision.CompareTo(entry.Revision);
            if (revisionOrder < 0) return Reject();
            if (revisionOrder == 0)
            {
                bool duplicate = entry.Operation == operation &&
                    (operation == ClientPublicPresentationOperation.Remove ||
                     entry.Flags == metadata.Flags &&
                     string.Equals(entry.DisplayLabel, metadata.DisplayLabel,
                         StringComparison.Ordinal) &&
                     Equals(entry.InspectionProperties, metadata.InspectionProperties));
                if (!duplicate) return Reject();
                SaturatingIncrement(ref _duplicates);
                return ClientPublicPresentationApplyStatus.Duplicate;
            }
        }

        bool wasPresent = entry.Operation == ClientPublicPresentationOperation.Present;
        entry.Generation = metadata.Id.Generation;
        entry.Revision = metadata.Revision;
        entry.Operation = operation;
        entry.Flags = operation == ClientPublicPresentationOperation.Present
            ? metadata.Flags
            : ClientPublicPresentationFlags.None;
        entry.DisplayLabel = operation == ClientPublicPresentationOperation.Present
            ? metadata.DisplayLabel
            : string.Empty;
        entry.InspectionProperties = operation == ClientPublicPresentationOperation.Present
            ? metadata.InspectionProperties
            : null;
        if (operation == ClientPublicPresentationOperation.Present)
        {
            if (!wasPresent) _current++;
            SaturatingIncrement(ref _applied);
        }
        else
        {
            if (wasPresent) _current--;
            SaturatingIncrement(ref _removed);
        }
        return ClientPublicPresentationApplyStatus.Applied;
    }

    public bool TryGet(WorldPublicEntityId id, out ClientPublicPresentationMetadata metadata)
    {
        EnsureOwner();
        int slot = Find(id.Index);
        if (slot >= 0)
        {
            ref readonly MetadataEntry entry = ref _entries[slot];
            if (entry.Generation == id.Generation &&
                entry.Operation == ClientPublicPresentationOperation.Present)
            {
                metadata = new ClientPublicPresentationMetadata(
                    _serverGenerationId,
                    id,
                    entry.Revision,
                    entry.Flags,
                    entry.DisplayLabel!,
                    entry.InspectionProperties);
                return true;
            }
        }
        metadata = default;
        return false;
    }

    public ClientPublicPresentationMetadataSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new ClientPublicPresentationMetadataSnapshot(
            _serverGenerationId, _current, _known, _entries.Length,
            _applied, _removed, _duplicates, _rejected, _generationResets,
            checked((long)_entries.Length * 56));
    }

    private int Find(uint index)
    {
        for (int slot = 0; slot < _entries.Length; slot++)
            if (_entries[slot].Used && _entries[slot].Index == index) return slot;
        return -1;
    }

    private int FindFree()
    {
        for (int slot = 0; slot < _entries.Length; slot++)
            if (!_entries[slot].Used) return slot;
        return -1;
    }

    private ClientPublicPresentationApplyStatus Reject()
    {
        SaturatingIncrement(ref _rejected);
        return ClientPublicPresentationApplyStatus.Rejected;
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Public presentation metadata is owner-thread-bound.");
    }

    private struct MetadataEntry
    {
        internal bool Used;
        internal uint Index;
        internal uint Generation;
        internal ulong Revision;
        internal ClientPublicPresentationOperation Operation;
        internal ClientPublicPresentationFlags Flags;
        internal string? DisplayLabel;
        internal InspectionPropertySet? InspectionProperties;
    }
}

public readonly record struct ClientActorPresentationRecord(
    WorldPublicEntityId Id,
    uint PrefabId,
    WorldPosition Position,
    WorldYaw Orientation,
    ClientActorDefinition Definition,
    string DisplayLabel,
    bool NameplateEligible);

public enum ClientActorPresentationChangeKind : byte
{
    Added = 1,
    Updated = 2,
    Removed = 3,
}

public readonly record struct ClientActorPresentationChange(
    ClientActorPresentationChangeKind Kind,
    WorldPublicEntityId Id,
    ClientActorPresentationRecord Current);

public enum ClientActorPresentationSyncStatus : byte
{
    Applied = 1,
    NoChanges = 2,
    DestinationTooSmall = 3,
    CapacityExceeded = 4,
    InvalidInput = 5,
}

public readonly record struct ClientActorPresentationStateSnapshot(
    ulong ServerGenerationId,
    int CurrentActors,
    int Capacity,
    ulong Synchronizations,
    ulong Added,
    ulong Updated,
    ulong Removed,
    long ReservedBytes);

/// <summary>
/// Engine-neutral actor presentation state derived only from already-resident public records and
/// already-authorized cold metadata. Synchronization never requests disclosure or expands AOI.
/// </summary>
public sealed class ClientActorPresentationState
{
    private readonly ClientActorDefinitionCatalog _catalog;
    private readonly ClientActorPresentationRecord[] _current;
    private readonly ClientActorPresentationRecord[] _candidate;
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;
    private ulong _serverGenerationId;
    private int _currentCount;
    private ulong _synchronizations;
    private ulong _added;
    private ulong _updated;
    private ulong _removed;

    public ClientActorPresentationState(
        ClientActorDefinitionCatalog catalog,
        int maximumActors = 256)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        if (maximumActors <= 0) throw new ArgumentOutOfRangeException(nameof(maximumActors));
        _current = new ClientActorPresentationRecord[maximumActors];
        _candidate = new ClientActorPresentationRecord[maximumActors];
    }

    public ClientActorDefinitionCatalog Catalog => _catalog;

    public ClientActorPresentationSyncStatus TrySynchronize(
        ClientWorldSemanticCache cache,
        ReadOnlySpan<ClientSemanticChunkHandle> presentedChunks,
        ClientPublicPresentationMetadataState metadata,
        ulong serverGenerationId,
        Span<ClientActorPresentationChange> changes,
        out int changeCount,
        out int required)
    {
        EnsureOwner();
        ArgumentNullException.ThrowIfNull(cache);
        ArgumentNullException.ThrowIfNull(metadata);
        changeCount = 0;
        required = 0;
        if (serverGenerationId == 0 ||
            metadata.GetSnapshot().ServerGenerationId != serverGenerationId)
            return ClientActorPresentationSyncStatus.InvalidInput;

        int candidateCount = 0;
        for (int chunk = 0; chunk < presentedChunks.Length; chunk++)
        {
            if (!cache.TryGetResidentView(
                    presentedChunks[chunk], out ClientSemanticChunkView view))
                return ClientActorPresentationSyncStatus.InvalidInput;
            foreach (ref readonly WorldPublicRecord record in view.PublicRecords)
            {
                if ((record.StateFlags & WorldPublicStateFlags.Visible) == 0 ||
                    !_catalog.TryGet(record.PrefabId, out ClientActorDefinition definition))
                    continue;
                if (candidateCount == _candidate.Length)
                    return ClientActorPresentationSyncStatus.CapacityExceeded;
                if (Find(_candidate, candidateCount, record.Id) >= 0)
                    return ClientActorPresentationSyncStatus.InvalidInput;
                bool hasMetadata = metadata.TryGet(
                    record.Id, out ClientPublicPresentationMetadata presentation);
                _candidate[candidateCount++] = new ClientActorPresentationRecord(
                    record.Id,
                    record.PrefabId,
                    record.Position,
                    record.Orientation,
                    definition,
                    hasMetadata ? presentation.DisplayLabel : definition.DefaultDisplayLabel,
                    definition.NameplateEligible &&
                        (!hasMetadata ||
                         (presentation.Flags & ClientPublicPresentationFlags.NameplateEligible) != 0));
            }
        }
        InsertionSort(_candidate.AsSpan(0, candidateCount));

        int current = 0;
        int candidate = 0;
        while (current < _currentCount || candidate < candidateCount)
        {
            if (current == _currentCount ||
                candidate < candidateCount &&
                _candidate[candidate].Id.CompareTo(_current[current].Id) < 0)
            {
                required++;
                candidate++;
            }
            else if (candidate == candidateCount ||
                     _current[current].Id.CompareTo(_candidate[candidate].Id) < 0)
            {
                required++;
                current++;
            }
            else
            {
                if (_current[current] != _candidate[candidate]) required++;
                current++;
                candidate++;
            }
        }
        if (changes.Length < required)
            return ClientActorPresentationSyncStatus.DestinationTooSmall;

        current = 0;
        candidate = 0;
        while (current < _currentCount || candidate < candidateCount)
        {
            if (current == _currentCount ||
                candidate < candidateCount &&
                _candidate[candidate].Id.CompareTo(_current[current].Id) < 0)
            {
                ClientActorPresentationRecord value = _candidate[candidate++];
                changes[changeCount++] = new ClientActorPresentationChange(
                    ClientActorPresentationChangeKind.Added, value.Id, value);
                SaturatingIncrement(ref _added);
            }
            else if (candidate == candidateCount ||
                     _current[current].Id.CompareTo(_candidate[candidate].Id) < 0)
            {
                WorldPublicEntityId id = _current[current++].Id;
                changes[changeCount++] = new ClientActorPresentationChange(
                    ClientActorPresentationChangeKind.Removed, id, default);
                SaturatingIncrement(ref _removed);
            }
            else
            {
                if (_current[current] != _candidate[candidate])
                {
                    ClientActorPresentationRecord value = _candidate[candidate];
                    changes[changeCount++] = new ClientActorPresentationChange(
                        ClientActorPresentationChangeKind.Updated, value.Id, value);
                    SaturatingIncrement(ref _updated);
                }
                current++;
                candidate++;
            }
        }

        _candidate.AsSpan(0, candidateCount).CopyTo(_current);
        if (candidateCount < _currentCount)
            _current.AsSpan(candidateCount, _currentCount - candidateCount).Clear();
        _currentCount = candidateCount;
        _serverGenerationId = serverGenerationId;
        SaturatingIncrement(ref _synchronizations);
        return changeCount == 0
            ? ClientActorPresentationSyncStatus.NoChanges
            : ClientActorPresentationSyncStatus.Applied;
    }

    public bool TryGet(WorldPublicEntityId id, out ClientActorPresentationRecord actor)
    {
        EnsureOwner();
        int index = Find(_current, _currentCount, id);
        if (index >= 0)
        {
            actor = _current[index];
            return true;
        }
        actor = default;
        return false;
    }

    public int CopyCurrent(Span<ClientActorPresentationRecord> destination)
    {
        EnsureOwner();
        if (destination.Length < _currentCount) return 0;
        _current.AsSpan(0, _currentCount).CopyTo(destination);
        return _currentCount;
    }

    public ClientActorPresentationStateSnapshot GetSnapshot()
    {
        EnsureOwner();
        return new ClientActorPresentationStateSnapshot(
            _serverGenerationId, _currentCount, _current.Length,
            _synchronizations, _added, _updated, _removed,
            checked((long)(_current.Length + _candidate.Length) * 112));
    }

    private static int Find(
        ClientActorPresentationRecord[] values,
        int count,
        WorldPublicEntityId id)
    {
        for (int index = 0; index < count; index++)
            if (values[index].Id == id) return index;
        return -1;
    }

    private static void InsertionSort(Span<ClientActorPresentationRecord> values)
    {
        for (int index = 1; index < values.Length; index++)
        {
            ClientActorPresentationRecord value = values[index];
            int destination = index;
            while (destination > 0 && values[destination - 1].Id.CompareTo(value.Id) > 0)
            {
                values[destination] = values[destination - 1];
                destination--;
            }
            values[destination] = value;
        }
    }

    private static void SaturatingIncrement(ref ulong value)
    {
        if (value != ulong.MaxValue) value++;
    }

    private void EnsureOwner()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Actor presentation state is owner-thread-bound.");
    }
}
