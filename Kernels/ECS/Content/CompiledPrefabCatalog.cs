using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 32)]
internal readonly struct CompiledPrefabEntry
{
    internal CompiledPrefabEntry(
        uint prefabId,
        ArchetypeId archetypeId,
        int descriptorStart,
        int descriptorCount,
        int defaultByteCount,
        int keyIndex,
        ulong signatureHash)
    {
        PrefabId = prefabId;
        ArchetypeId = archetypeId;
        DescriptorStart = descriptorStart;
        DescriptorCount = descriptorCount;
        DefaultByteCount = defaultByteCount;
        KeyIndex = keyIndex;
        SignatureHash = signatureHash;
    }

    internal uint PrefabId { get; }
    internal ArchetypeId ArchetypeId { get; }
    internal int DescriptorStart { get; }
    internal int DescriptorCount { get; }
    internal int DefaultByteCount { get; }
    internal int KeyIndex { get; }
    internal ulong SignatureHash { get; }
}

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 12)]
internal readonly struct PrefabDefaultDescriptor
{
    internal PrefabDefaultDescriptor(ComponentId componentId, ushort columnIndex, int payloadOffset, int byteCount)
    {
        ComponentId = componentId;
        ColumnIndex = columnIndex;
        PayloadOffset = payloadOffset;
        ByteCount = byteCount;
    }

    internal ComponentId ComponentId { get; }
    internal ushort ColumnIndex { get; }
    internal int PayloadOffset { get; }
    internal int ByteCount { get; }
}

/// <summary>Cold diagnostic description of one immutable compiled prefab.</summary>
public readonly struct CompiledPrefabInfo
{
    internal CompiledPrefabInfo(
        PrefabHandle handle,
        PrefabKey key,
        string? displayName,
        int explicitDefaultCount,
        int defaultByteCount,
        ulong signatureHash)
    {
        Handle = handle;
        Key = key;
        DisplayName = displayName;
        ExplicitDefaultCount = explicitDefaultCount;
        DefaultByteCount = defaultByteCount;
        SignatureHash = signatureHash;
    }

    public PrefabHandle Handle { get; }
    public PrefabKey Key { get; }
    public string? DisplayName { get; }
    public int ExplicitDefaultCount { get; }
    public int DefaultByteCount { get; }
    public ulong SignatureHash { get; }
}

/// <summary>Exact bounded payload accounting for one sealed compiled content catalog.</summary>
public readonly struct CompiledContentSnapshot
{
    internal CompiledContentSnapshot(
        int prefabCount,
        int uniqueSignatureCount,
        int archetypesRegistered,
        long entryBytes,
        long descriptorBytes,
        long defaultBytes,
        long stableKeyBytes,
        long displayMetadataBytes,
        ulong diagnosticHash)
    {
        PrefabCount = prefabCount;
        UniqueSignatureCount = uniqueSignatureCount;
        ArchetypesRegistered = archetypesRegistered;
        EntryBytes = entryBytes;
        DescriptorBytes = descriptorBytes;
        DefaultBytes = defaultBytes;
        StableKeyBytes = stableKeyBytes;
        DisplayMetadataBytes = displayMetadataBytes;
        DiagnosticHash = diagnosticHash;
        TotalKnownPayloadBytes = checked(
            entryBytes + descriptorBytes + defaultBytes + stableKeyBytes + displayMetadataBytes);
    }

    public int PrefabCount { get; }
    public int UniqueSignatureCount { get; }
    public int ArchetypesRegistered { get; }
    public long EntryBytes { get; }
    public long DescriptorBytes { get; }
    public long DefaultBytes { get; }
    public long StableKeyBytes { get; }
    public long DisplayMetadataBytes { get; }
    public long TotalKnownPayloadBytes { get; }
    public ulong DiagnosticHash { get; }

    public const string Exclusions =
        "CLR object/array/string headers and alignment, temporary compiler objects, shared schema/archetype storage, and allocator overhead.";
}

/// <summary>World-bound immutable direct-index catalog produced by cold content compilation.</summary>
public sealed class CompiledPrefabCatalog
{
    private readonly CompiledPrefabEntry[] _entries;
    private readonly PrefabDefaultDescriptor[] _descriptors;
    private readonly byte[] _defaultBytes;
    private readonly string[] _keys;
    private readonly string?[] _displayNames;
    private readonly CompiledContentSnapshot _snapshot;

    internal CompiledPrefabCatalog(
        WorldId worldId,
        CompiledPrefabEntry[] entries,
        PrefabDefaultDescriptor[] descriptors,
        byte[] defaultBytes,
        string[] keys,
        string?[] displayNames,
        int uniqueSignatureCount,
        int archetypesRegistered,
        ulong diagnosticHash)
    {
        WorldId = worldId;
        _entries = entries;
        _descriptors = descriptors;
        _defaultBytes = defaultBytes;
        _keys = keys;
        _displayNames = displayNames;
        long keyBytes = 0;
        long displayBytes = 0;
        for (int index = 0; index < keys.Length; index++)
        {
            keyBytes = checked(keyBytes + (long)keys[index].Length * sizeof(char));
            if (displayNames[index] is { } display)
                displayBytes = checked(displayBytes + (long)display.Length * sizeof(char));
        }
        _snapshot = new CompiledContentSnapshot(
            entries.Length,
            uniqueSignatureCount,
            archetypesRegistered,
            checked((long)entries.Length * Unsafe.SizeOf<CompiledPrefabEntry>()),
            checked((long)descriptors.Length * Unsafe.SizeOf<PrefabDefaultDescriptor>()),
            defaultBytes.Length,
            keyBytes,
            displayBytes,
            diagnosticHash);
    }

    public WorldId WorldId { get; }
    public int Count => _entries.Length;
    public ulong DiagnosticHash => _snapshot.DiagnosticHash;

    /// <summary>Looks up a compact handle by stable key through the sorted cold key table.</summary>
    public bool TryGetHandle(PrefabKey key, out PrefabHandle handle)
    {
        if (!key.IsValid)
        {
            handle = PrefabHandle.Invalid;
            return false;
        }
        int low = 0;
        int high = _keys.Length - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int comparison = StringComparer.Ordinal.Compare(_keys[middle], key.Value);
            if (comparison == 0)
            {
                ref readonly CompiledPrefabEntry entry = ref _entries[middle];
                handle = new PrefabHandle(WorldId, entry.PrefabId, entry.ArchetypeId);
                return true;
            }
            if (comparison < 0)
                low = middle + 1;
            else
                high = middle - 1;
        }
        handle = PrefabHandle.Invalid;
        return false;
    }

    /// <summary>Resolves a stable key or throws when it is absent from this catalog.</summary>
    public PrefabHandle GetHandle(PrefabKey key) => TryGetHandle(key, out PrefabHandle handle)
        ? handle
        : throw new KeyNotFoundException($"Compiled prefab key '{key}' was not found.");

    /// <summary>Returns cold diagnostic metadata after validating world/catalog ownership.</summary>
    public CompiledPrefabInfo GetInfo(PrefabHandle handle)
    {
        ref readonly CompiledPrefabEntry entry = ref ValidateHandle(handle);
        int index = checked((int)entry.PrefabId - 1);
        return new CompiledPrefabInfo(
            handle,
            new PrefabKey(_keys[index]),
            _displayNames[index],
            entry.DescriptorCount,
            entry.DefaultByteCount,
            entry.SignatureHash);
    }

    /// <summary>Returns exact known catalog payload accounting and its deterministic hash.</summary>
    public CompiledContentSnapshot GetSnapshot() => _snapshot;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void Validate(PrefabHandle handle) => _ = ValidateHandle(handle);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal ref readonly CompiledPrefabEntry GetEntry(uint prefabId)
    {
        uint index = prefabId - 1;
        if (index >= unchecked((uint)_entries.Length))
            throw new ArgumentOutOfRangeException(nameof(prefabId));
        return ref _entries[index];
    }

    internal ReadOnlySpan<PrefabDefaultDescriptor> GetDescriptors(in CompiledPrefabEntry entry) =>
        _descriptors.AsSpan(entry.DescriptorStart, entry.DescriptorCount);

    internal ReadOnlySpan<byte> GetDefaultBytes(in PrefabDefaultDescriptor descriptor) =>
        _defaultBytes.AsSpan(descriptor.PayloadOffset, descriptor.ByteCount);

    private ref readonly CompiledPrefabEntry ValidateHandle(PrefabHandle handle)
    {
        if (!handle.IsValid || handle.World != WorldId)
            throw new ArgumentException("The prefab handle belongs to a different compiled catalog.", nameof(handle));
        ref readonly CompiledPrefabEntry entry = ref GetEntry(handle.PrefabId);
        if (entry.ArchetypeId != handle.ArchetypeId)
            throw new ArgumentException("The prefab handle's archetype binding is invalid.", nameof(handle));
        return ref entry;
    }
}
