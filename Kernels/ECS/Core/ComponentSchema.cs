using System.ComponentModel;

namespace UltraSim.ECS;

/// <summary>
/// Immutable, instance-owned component schema. Construction is cold; ID and metadata
/// access are allocation-free and require no process-global registry.
/// </summary>
public sealed class ComponentSchema
{
    public const int MaximumComponentCount = ushort.MaxValue;
    private const int LinearLookupThreshold = 8;
    private const ComponentFlags KnownComponentFlags =
        ComponentFlags.Tag | ComponentFlags.Enableable | ComponentFlags.Persistent |
        ComponentFlags.PersistEnableState;

    private readonly ComponentMetadata[] _metadata;
    private readonly ulong[] _stableKeyHashes;
    private readonly string[] _canonicalKeys;
    private readonly RuntimeTypeHandle[] _typeHandles;
    private readonly int[] _stableHashLookup;
    private readonly int[] _typeLookup;
    private readonly int[] _persistenceLookup;

    private ComponentSchema(SchemaId id, ReadOnlySpan<ComponentRegistration> registrations)
    {
        if (!id.IsValid)
            throw new ArgumentException("A generated schema ID cannot be invalid.", nameof(id));
        if (registrations.Length > MaximumComponentCount)
            throw new ArgumentOutOfRangeException(
                nameof(registrations),
                registrations.Length,
                $"A schema supports at most {MaximumComponentCount} components.");

        Id = id;
        _metadata = new ComponentMetadata[registrations.Length];
        _stableKeyHashes = new ulong[registrations.Length];
        _canonicalKeys = new string[registrations.Length];
        _typeHandles = new RuntimeTypeHandle[registrations.Length];

        if (registrations.Length > LinearLookupThreshold)
        {
            int lookupSize = GetLookupTableSize(registrations.Length);
            _stableHashLookup = new int[lookupSize];
            _typeLookup = new int[lookupSize];

            int persistentCount = 0;
            for (int i = 0; i < registrations.Length; i++)
            {
                if (registrations[i].Metadata.PersistenceId != 0)
                    persistentCount++;
            }

            _persistenceLookup = persistentCount == 0
                ? Array.Empty<int>()
                : new int[GetLookupTableSize(persistentCount)];
        }
        else
        {
            _stableHashLookup = Array.Empty<int>();
            _typeLookup = Array.Empty<int>();
            _persistenceLookup = Array.Empty<int>();
        }

        for (int i = 0; i < registrations.Length; i++)
        {
            ComponentRegistration registration = registrations[i];
            ValidateAndIndex(registrations, i, registration);
            _metadata[i] = registration.Metadata;
            _stableKeyHashes[i] = registration.StableKeyHash;
            _canonicalKeys[i] = registration.CanonicalKey;
            _typeHandles[i] = registration.TypeHandle;
        }
    }

    public SchemaId Id { get; }

    public int ComponentCount => _metadata.Length;

    public ReadOnlySpan<ComponentMetadata> Components => _metadata;

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static ComponentSchema Create(
        SchemaId id,
        ReadOnlySpan<ComponentRegistration> registrations) => new(id, registrations);

    public ref readonly ComponentMetadata GetMetadata(ComponentId id)
    {
        if (!id.IsValid || id.Value >= _metadata.Length)
            throw new ArgumentOutOfRangeException(nameof(id), id, "Component ID is not in this schema.");

        return ref _metadata[id.Value];
    }

    public bool TryGetMetadata(ComponentId id, out ComponentMetadata metadata)
    {
        if (!id.IsValid || id.Value >= _metadata.Length)
        {
            metadata = default;
            return false;
        }

        metadata = _metadata[id.Value];
        return true;
    }

    public string GetCanonicalKey(ComponentId id)
    {
        _ = GetMetadata(id);
        return _canonicalKeys[id.Value];
    }

    public ulong GetStableKeyHash(ComponentId id)
    {
        _ = GetMetadata(id);
        return _stableKeyHashes[id.Value];
    }

    public bool TryGetComponentId<T>(out ComponentId id)
        where T : unmanaged, IComponent<T>
    {
        RuntimeTypeHandle requested = typeof(T).TypeHandle;
        if (_typeLookup.Length != 0)
        {
            int mask = _typeLookup.Length - 1;
            int slot = GetTypeSlot(requested, mask);
            while (true)
            {
                int entry = _typeLookup[slot];
                if (entry == 0)
                    break;

                int componentIndex = entry - 1;
                if (_typeHandles[componentIndex].Equals(requested))
                {
                    id = new ComponentId((ushort)componentIndex);
                    return true;
                }

                slot = (slot + 1) & mask;
            }
        }
        else
        {
            for (ushort i = 0; i < _typeHandles.Length; i++)
            {
                if (_typeHandles[i].Equals(requested))
                {
                    id = new ComponentId(i);
                    return true;
                }
            }
        }

        id = ComponentId.Invalid;
        return false;
    }

    public bool TryGetComponentId(ReadOnlySpan<char> canonicalKey, out ComponentId id)
    {
        if (_stableHashLookup.Length != 0)
        {
            if (StableHash64.TryCompute(canonicalKey, out ulong stableHash) &&
                TryGetComponentId(stableHash, out ComponentId candidate) &&
                canonicalKey.SequenceEqual(_canonicalKeys[candidate.Value]))
            {
                id = candidate;
                return true;
            }
        }
        else
        {
            for (ushort i = 0; i < _canonicalKeys.Length; i++)
            {
                if (canonicalKey.SequenceEqual(_canonicalKeys[i]))
                {
                    id = new ComponentId(i);
                    return true;
                }
            }
        }

        id = ComponentId.Invalid;
        return false;
    }

    public bool TryGetComponentId(ulong stableKeyHash, out ComponentId id)
    {
        if (_stableHashLookup.Length != 0)
        {
            int mask = _stableHashLookup.Length - 1;
            int slot = GetStableHashSlot(stableKeyHash, mask);
            while (true)
            {
                int entry = _stableHashLookup[slot];
                if (entry == 0)
                    break;

                int componentIndex = entry - 1;
                if (_stableKeyHashes[componentIndex] == stableKeyHash)
                {
                    id = new ComponentId((ushort)componentIndex);
                    return true;
                }

                slot = (slot + 1) & mask;
            }
        }
        else
        {
            for (ushort i = 0; i < _stableKeyHashes.Length; i++)
            {
                if (_stableKeyHashes[i] == stableKeyHash)
                {
                    id = new ComponentId(i);
                    return true;
                }
            }
        }

        id = ComponentId.Invalid;
        return false;
    }

    public bool TryGetComponentIdByPersistenceId(uint persistenceId, out ComponentId id)
    {
        if (persistenceId == 0)
        {
            id = ComponentId.Invalid;
            return false;
        }

        if (_persistenceLookup.Length != 0)
        {
            int mask = _persistenceLookup.Length - 1;
            int slot = GetPersistenceSlot(persistenceId, mask);
            while (true)
            {
                int entry = _persistenceLookup[slot];
                if (entry == 0)
                    break;

                int componentIndex = entry - 1;
                if (_metadata[componentIndex].PersistenceId == persistenceId)
                {
                    id = new ComponentId((ushort)componentIndex);
                    return true;
                }

                slot = (slot + 1) & mask;
            }
        }
        else
        {
            for (ushort i = 0; i < _metadata.Length; i++)
            {
                if (_metadata[i].PersistenceId == persistenceId)
                {
                    id = new ComponentId(i);
                    return true;
                }
            }
        }

        id = ComponentId.Invalid;
        return false;
    }

    private void ValidateAndIndex(
        ReadOnlySpan<ComponentRegistration> registrations,
        int index,
        ComponentRegistration current)
    {
        ComponentMetadata metadata = current.Metadata;
        if (!metadata.Id.IsValid || metadata.Id.Value != index)
            throw new ArgumentException("Component registrations must use dense IDs in array order.");
        if (metadata.Version == 0)
            throw new ArgumentException("Component version zero is reserved.");
        if ((metadata.Flags & ~KnownComponentFlags) != 0)
            throw new ArgumentException("Component metadata contains unknown flags.");
        if (metadata.Alignment == 0 || (metadata.Alignment & (metadata.Alignment - 1)) != 0)
            throw new ArgumentException("Component alignment must be a nonzero power of two.");
        if (metadata.IsTag != (metadata.Size == 0))
            throw new ArgumentException("Only tag components may have zero storage size.");
        if (!metadata.IsTag && (metadata.Size % metadata.Alignment) != 0)
            throw new ArgumentException("Component size must be a multiple of its natural alignment.");
        if (!Enum.IsDefined(metadata.PersistenceMode) || metadata.PersistenceMode == ComponentPersistenceMode.Auto)
            throw new ArgumentException("Component persistence mode must be normalized by schema generation.");
        if (metadata.PersistenceMode == ComponentPersistenceMode.None && metadata.PersistenceId != 0)
            throw new ArgumentException("A non-persisted component cannot declare a persistence ID.");
        if (metadata.PersistenceMode != ComponentPersistenceMode.None && metadata.PersistenceId == 0)
            throw new ArgumentException("A persisted shape member requires a nonzero persistence ID.");
        if (metadata.IsPersistent != (metadata.PersistenceMode == ComponentPersistenceMode.Value))
            throw new ArgumentException("Persistent payload flags and persistence mode must agree.");
        if ((metadata.Flags & ComponentFlags.PersistEnableState) != 0 && !metadata.IsEnableable)
            throw new ArgumentException("Only enableable components can persist enable state.");
        if (!IsCanonicalKey(current.CanonicalKey))
            throw new ArgumentException($"Invalid canonical component key '{current.CanonicalKey}'.");
        if (StableHash64.Compute(current.CanonicalKey) != current.StableKeyHash)
            throw new ArgumentException("Stable component key hash does not match its canonical key.");

        if (index != 0 && StringComparer.Ordinal.Compare(
                registrations[index - 1].CanonicalKey,
                current.CanonicalKey) >= 0)
        {
            throw new ArgumentException(
                "Component registrations must be in unique ascending canonical-key order.");
        }

        if (_stableHashLookup.Length != 0)
        {
            InsertStableHash(registrations, index, current.StableKeyHash);
            InsertType(registrations, index, current.TypeHandle);
            if (metadata.PersistenceId != 0)
                InsertPersistenceId(registrations, index, metadata.PersistenceId);
        }
        else
        {
            for (int previousIndex = 0; previousIndex < index; previousIndex++)
            {
                ComponentRegistration previous = registrations[previousIndex];
                if (previous.StableKeyHash == current.StableKeyHash)
                    throw new ArgumentException("Stable component key hash collision.");
                if (previous.TypeHandle.Equals(current.TypeHandle))
                    throw new ArgumentException("A component type can appear only once in a schema.");
                if (metadata.PersistenceId != 0 && previous.Metadata.PersistenceId == metadata.PersistenceId)
                    throw new ArgumentException($"Duplicate persistence ID {metadata.PersistenceId}.");
            }
        }
    }

    private void InsertStableHash(
        ReadOnlySpan<ComponentRegistration> registrations,
        int componentIndex,
        ulong stableHash)
    {
        int mask = _stableHashLookup.Length - 1;
        int slot = GetStableHashSlot(stableHash, mask);
        while (_stableHashLookup[slot] != 0)
        {
            int existingIndex = _stableHashLookup[slot] - 1;
            if (registrations[existingIndex].StableKeyHash == stableHash)
                throw new ArgumentException("Stable component key hash collision.");
            slot = (slot + 1) & mask;
        }

        _stableHashLookup[slot] = componentIndex + 1;
    }

    private void InsertType(
        ReadOnlySpan<ComponentRegistration> registrations,
        int componentIndex,
        RuntimeTypeHandle typeHandle)
    {
        int mask = _typeLookup.Length - 1;
        int slot = GetTypeSlot(typeHandle, mask);
        while (_typeLookup[slot] != 0)
        {
            int existingIndex = _typeLookup[slot] - 1;
            if (registrations[existingIndex].TypeHandle.Equals(typeHandle))
                throw new ArgumentException("A component type can appear only once in a schema.");
            slot = (slot + 1) & mask;
        }

        _typeLookup[slot] = componentIndex + 1;
    }

    private void InsertPersistenceId(
        ReadOnlySpan<ComponentRegistration> registrations,
        int componentIndex,
        uint persistenceId)
    {
        int mask = _persistenceLookup.Length - 1;
        int slot = GetPersistenceSlot(persistenceId, mask);
        while (_persistenceLookup[slot] != 0)
        {
            int existingIndex = _persistenceLookup[slot] - 1;
            if (registrations[existingIndex].Metadata.PersistenceId == persistenceId)
                throw new ArgumentException($"Duplicate persistence ID {persistenceId}.");
            slot = (slot + 1) & mask;
        }

        _persistenceLookup[slot] = componentIndex + 1;
    }

    private static bool IsCanonicalKey(string? value)
    {
        if (string.IsNullOrEmpty(value) || !IsLowerAlphaNumeric(value[0]))
            return false;

        for (int i = 1; i < value.Length; i++)
        {
            char character = value[i];
            if (!IsLowerAlphaNumeric(character) &&
                character != '.' &&
                character != '_' &&
                character != '/' &&
                character != '-')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsLowerAlphaNumeric(char value) =>
        (value >= 'a' && value <= 'z') || (value >= '0' && value <= '9');

    private static int GetLookupTableSize(int count)
    {
        int required = checked(count * 2);
        int size = 2;
        while (size < required)
            size <<= 1;
        return size;
    }

    private static int GetStableHashSlot(ulong value, int mask)
    {
        value ^= value >> 33;
        value = unchecked(value * 0xff51afd7ed558ccdUL);
        value ^= value >> 33;
        return unchecked((int)value) & mask;
    }

    private static int GetTypeSlot(RuntimeTypeHandle typeHandle, int mask)
    {
        uint value = unchecked((uint)typeHandle.GetHashCode());
        value ^= value >> 16;
        value = unchecked(value * 0x7feb352dU);
        value ^= value >> 15;
        return unchecked((int)value) & mask;
    }

    private static int GetPersistenceSlot(uint persistenceId, int mask)
    {
        uint value = persistenceId;
        value ^= value >> 16;
        value = unchecked(value * 0x7feb352dU);
        value ^= value >> 15;
        return unchecked((int)value) & mask;
    }
}
