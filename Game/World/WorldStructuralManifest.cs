using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace UltraSim.Game.World;

/// <summary>Canonical, fixed-grid structural-manifest v1 layout. Limits are admission policy, not bytes.</summary>
public static class WorldStructuralManifestFormat
{
    public const ulong Magic = 0x3130_4E41_4D57_5355UL; // USWMAN01, little endian.
    public const uint Version = 1;
    public const uint CenteredWorldPackProfile = 1;
    public const int HeaderBytes = 64;
    public const int EntryBytes = 40;
    public const int FingerprintOffset = 32;
    public const int FingerprintBytes = 32;
    public const int MaximumPacks = 65_536;
    public const int MaximumManifestBytes = HeaderBytes + MaximumPacks * EntryBytes;
}

/// <summary>SHA-256 identity of canonical immutable authored Pack mapping, not a World/save revision.</summary>
[StructLayout(LayoutKind.Sequential, Size = 32)]
public readonly struct WorldStructuralManifestFingerprint : IEquatable<WorldStructuralManifestFingerprint>
{
    private readonly ulong _part0;
    private readonly ulong _part1;
    private readonly ulong _part2;
    private readonly ulong _part3;

    private WorldStructuralManifestFingerprint(ReadOnlySpan<byte> digest)
    {
        _part0 = BinaryPrimitives.ReadUInt64LittleEndian(digest);
        _part1 = BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]);
        _part2 = BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]);
        _part3 = BinaryPrimitives.ReadUInt64LittleEndian(digest[24..]);
    }

    public bool IsValid => _part0 != 0 || _part1 != 0 || _part2 != 0 || _part3 != 0;
    internal static WorldStructuralManifestFingerprint Read(ReadOnlySpan<byte> digest) => new(digest);
    public bool Equals(WorldStructuralManifestFingerprint other) =>
        _part0 == other._part0 && _part1 == other._part1 &&
        _part2 == other._part2 && _part3 == other._part3;
    public override bool Equals(object? obj) => obj is WorldStructuralManifestFingerprint other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_part0, _part1, _part2, _part3);
    public override string ToString() => $"{_part3:X16}{_part2:X16}{_part1:X16}{_part0:X16}";
    public static bool operator ==(WorldStructuralManifestFingerprint left, WorldStructuralManifestFingerprint right) => left.Equals(right);
    public static bool operator !=(WorldStructuralManifestFingerprint left, WorldStructuralManifestFingerprint right) => !left.Equals(right);
}

/// <summary>Monotonic identity of a runtime/save structural-composition publication, not manifest content.</summary>
[StructLayout(LayoutKind.Sequential, Size = 8)]
public readonly record struct WorldCompositionRevision(ulong Value) : IComparable<WorldCompositionRevision>
{
    public bool IsValid => Value != 0;
    public int CompareTo(WorldCompositionRevision other) => Value.CompareTo(other.Value);
}

/// <summary>One immutable authored address-to-artifact identity; it carries no Pack bytes or residency.</summary>
public readonly record struct WorldStructuralManifestEntry(WorldPackCoord Coord, WorldPackFingerprint Fingerprint);

/// <summary>Finite caller admission policy; never serialized into manifest identity.</summary>
public readonly record struct WorldStructuralManifestLimits
{
    public static WorldStructuralManifestLimits Default { get; } = new(
        WorldStructuralManifestFormat.MaximumPacks,
        WorldStructuralManifestFormat.MaximumManifestBytes);

    public int MaximumPacks { get; }
    public int MaximumManifestBytes { get; }

    public WorldStructuralManifestLimits(int maximumPacks, int maximumManifestBytes)
    {
        if (maximumPacks is < 1 or > WorldStructuralManifestFormat.MaximumPacks)
            throw new ArgumentOutOfRangeException(nameof(maximumPacks));
        if (maximumManifestBytes < WorldStructuralManifestFormat.HeaderBytes ||
            maximumManifestBytes > WorldStructuralManifestFormat.MaximumManifestBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumManifestBytes));
        MaximumPacks = maximumPacks;
        MaximumManifestBytes = maximumManifestBytes;
    }

    internal static WorldStructuralManifestLimits Resolve(WorldStructuralManifestLimits limits) =>
        limits == default ? Default : limits;
}

public enum WorldStructuralManifestError
{
    None,
    InvalidEntries,
    CapacityExceeded,
    DuplicatePackCoord,
    InvalidPackFingerprint,
    Truncated,
    InvalidMagic,
    UnsupportedVersion,
    UnsupportedProfile,
    LengthMismatch,
    NonCanonicalEntries,
    HashMismatch,
}

/// <summary>
/// Immutable sparse authored Pack index. Its one private canonical byte array is also its flat
/// sorted lookup table; no per-entry object graph, mutable collection, or Pack payload is retained.
/// </summary>
public sealed class WorldStructuralManifest
{
    private readonly byte[] _bytes;

    private WorldStructuralManifest(byte[] bytes, int entryCount, WorldStructuralManifestFingerprint fingerprint)
    {
        _bytes = bytes;
        EntryCount = entryCount;
        Fingerprint = fingerprint;
    }

    public int EntryCount { get; }
    public int Length => _bytes.Length;
    public WorldStructuralManifestFingerprint Fingerprint { get; }

    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < _bytes.Length)
            throw new ArgumentException("Destination is too small.", nameof(destination));
        _bytes.CopyTo(destination);
    }

    public bool TryGetEntry(int index, out WorldStructuralManifestEntry entry)
    {
        if ((uint)index >= (uint)EntryCount)
        {
            entry = default;
            return false;
        }
        entry = ReadEntry(_bytes, index);
        return true;
    }

    /// <summary>Exact binary search over canonical Pack coordinates; no allocation after opening.</summary>
    public bool TryGetPack(WorldPackCoord coord, out WorldPackFingerprint fingerprint)
    {
        int low = 0;
        int high = EntryCount - 1;
        while (low <= high)
        {
            int middle = low + ((high - low) >> 1);
            int offset = WorldStructuralManifestFormat.HeaderBytes + middle * WorldStructuralManifestFormat.EntryBytes;
            int comparison = coord.CompareTo(ReadCoord(_bytes.AsSpan(offset)));
            if (comparison == 0)
            {
                fingerprint = WorldPackFingerprint.Read(_bytes.AsSpan(offset + 8, 32));
                return true;
            }
            if (comparison < 0) high = middle - 1;
            else low = middle + 1;
        }
        fingerprint = default;
        return false;
    }

    /// <summary>Copies a bounded source, sorts it canonically, then publishes exact immutable bytes.</summary>
    public static bool TryCreate(
        IEnumerable<WorldStructuralManifestEntry>? source,
        out WorldStructuralManifest? manifest,
        out WorldStructuralManifestError error,
        WorldStructuralManifestLimits limits = default)
    {
        manifest = null;
        limits = WorldStructuralManifestLimits.Resolve(limits);
        if (source is null)
        {
            error = WorldStructuralManifestError.InvalidEntries;
            return false;
        }

        var entries = new List<WorldStructuralManifestEntry>();
        foreach (WorldStructuralManifestEntry entry in source)
        {
            if (entries.Count == limits.MaximumPacks)
            {
                error = WorldStructuralManifestError.CapacityExceeded;
                return false;
            }
            if (!entry.Fingerprint.IsValid)
            {
                error = WorldStructuralManifestError.InvalidPackFingerprint;
                return false;
            }
            entries.Add(entry);
        }
        int length = checked(WorldStructuralManifestFormat.HeaderBytes +
            entries.Count * WorldStructuralManifestFormat.EntryBytes);
        if (length > limits.MaximumManifestBytes)
        {
            error = WorldStructuralManifestError.CapacityExceeded;
            return false;
        }

        entries.Sort(static (left, right) => left.Coord.CompareTo(right.Coord));
        for (int index = 1; index < entries.Count; index++)
            if (entries[index - 1].Coord == entries[index].Coord)
            {
                error = WorldStructuralManifestError.DuplicatePackCoord;
                return false;
            }

        var bytes = new byte[length];
        WriteHeader(bytes, entries.Count);
        for (int index = 0; index < entries.Count; index++)
            WriteEntry(bytes.AsSpan(WorldStructuralManifestFormat.HeaderBytes +
                index * WorldStructuralManifestFormat.EntryBytes), entries[index]);
        WorldStructuralManifestFingerprint fingerprint = ComputeAndWriteFingerprint(bytes);
        manifest = new WorldStructuralManifest(bytes, entries.Count, fingerprint);
        error = WorldStructuralManifestError.None;
        return true;
    }

    /// <summary>Validates untrusted canonical bytes before copying/publishing the immutable manifest.</summary>
    public static bool TryOpen(
        ReadOnlySpan<byte> source,
        out WorldStructuralManifest? manifest,
        out WorldStructuralManifestError error,
        WorldStructuralManifestLimits limits = default)
    {
        manifest = null;
        limits = WorldStructuralManifestLimits.Resolve(limits);
        if (source.Length < WorldStructuralManifestFormat.HeaderBytes)
        {
            error = WorldStructuralManifestError.Truncated;
            return false;
        }
        if (source.Length > limits.MaximumManifestBytes)
        {
            error = WorldStructuralManifestError.CapacityExceeded;
            return false;
        }
        if (BinaryPrimitives.ReadUInt64LittleEndian(source) != WorldStructuralManifestFormat.Magic)
        {
            error = WorldStructuralManifestError.InvalidMagic;
            return false;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(source[8..]) != WorldStructuralManifestFormat.Version)
        {
            error = WorldStructuralManifestError.UnsupportedVersion;
            return false;
        }
        if (BinaryPrimitives.ReadUInt32LittleEndian(source[12..]) != WorldStructuralManifestFormat.CenteredWorldPackProfile ||
            BinaryPrimitives.ReadUInt32LittleEndian(source[16..]) != GameWorldSpatialProfile.WorldChunkEdgeUnits ||
            BinaryPrimitives.ReadUInt32LittleEndian(source[20..]) != WorldPackAddressing.ChunksPerWorldPackEdge ||
            BinaryPrimitives.ReadUInt32LittleEndian(source[24..]) != WorldPackAddressing.CenteringBiasChunks)
        {
            error = WorldStructuralManifestError.UnsupportedProfile;
            return false;
        }

        uint count = BinaryPrimitives.ReadUInt32LittleEndian(source[28..]);
        if (count > limits.MaximumPacks || count > WorldStructuralManifestFormat.MaximumPacks)
        {
            error = WorldStructuralManifestError.CapacityExceeded;
            return false;
        }
        int expectedLength = checked(WorldStructuralManifestFormat.HeaderBytes +
            (int)count * WorldStructuralManifestFormat.EntryBytes);
        if (expectedLength > limits.MaximumManifestBytes)
        {
            error = WorldStructuralManifestError.CapacityExceeded;
            return false;
        }
        if (source.Length != expectedLength)
        {
            error = source.Length < expectedLength
                ? WorldStructuralManifestError.Truncated : WorldStructuralManifestError.LengthMismatch;
            return false;
        }

        WorldPackCoord previous = default;
        for (int index = 0; index < count; index++)
        {
            WorldStructuralManifestEntry entry = ReadEntry(source, index);
            if (!entry.Fingerprint.IsValid)
            {
                error = WorldStructuralManifestError.InvalidPackFingerprint;
                return false;
            }
            if (index != 0 && previous.CompareTo(entry.Coord) >= 0)
            {
                error = WorldStructuralManifestError.NonCanonicalEntries;
                return false;
            }
            previous = entry.Coord;
        }

        Span<byte> digest = stackalloc byte[WorldStructuralManifestFormat.FingerprintBytes];
        ComputeDigest(source, digest);
        if (!CryptographicOperations.FixedTimeEquals(
                source.Slice(WorldStructuralManifestFormat.FingerprintOffset,
                    WorldStructuralManifestFormat.FingerprintBytes), digest))
        {
            error = WorldStructuralManifestError.HashMismatch;
            return false;
        }
        byte[] owned = source.ToArray();
        manifest = new WorldStructuralManifest(owned, (int)count,
            WorldStructuralManifestFingerprint.Read(digest));
        error = WorldStructuralManifestError.None;
        return true;
    }

    private static void WriteHeader(Span<byte> destination, int count)
    {
        BinaryPrimitives.WriteUInt64LittleEndian(destination, WorldStructuralManifestFormat.Magic);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], WorldStructuralManifestFormat.Version);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[12..], WorldStructuralManifestFormat.CenteredWorldPackProfile);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[16..], GameWorldSpatialProfile.WorldChunkEdgeUnits);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[20..], WorldPackAddressing.ChunksPerWorldPackEdge);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[24..], WorldPackAddressing.CenteringBiasChunks);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[28..], (uint)count);
    }

    private static WorldStructuralManifestFingerprint ComputeAndWriteFingerprint(Span<byte> bytes)
    {
        Span<byte> digest = stackalloc byte[WorldStructuralManifestFormat.FingerprintBytes];
        ComputeDigest(bytes, digest);
        digest.CopyTo(bytes[WorldStructuralManifestFormat.FingerprintOffset..]);
        return WorldStructuralManifestFingerprint.Read(digest);
    }

    private static void ComputeDigest(ReadOnlySpan<byte> bytes, Span<byte> destination)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(bytes[..WorldStructuralManifestFormat.FingerprintOffset]);
        hash.AppendData(bytes[WorldStructuralManifestFormat.HeaderBytes..]);
        if (!hash.TryGetHashAndReset(destination, out int written) || written != destination.Length)
            throw new InvalidOperationException("SHA-256 did not produce a complete manifest digest.");
    }

    private static WorldStructuralManifestEntry ReadEntry(ReadOnlySpan<byte> bytes, int index)
    {
        ReadOnlySpan<byte> row = bytes.Slice(WorldStructuralManifestFormat.HeaderBytes +
            index * WorldStructuralManifestFormat.EntryBytes, WorldStructuralManifestFormat.EntryBytes);
        return new WorldStructuralManifestEntry(ReadCoord(row), WorldPackFingerprint.Read(row[8..]));
    }

    private static WorldPackCoord ReadCoord(ReadOnlySpan<byte> row) => new(
        BinaryPrimitives.ReadInt32LittleEndian(row), BinaryPrimitives.ReadInt32LittleEndian(row[4..]));

    private static void WriteEntry(Span<byte> row, WorldStructuralManifestEntry entry)
    {
        BinaryPrimitives.WriteInt32LittleEndian(row, entry.Coord.X);
        BinaryPrimitives.WriteInt32LittleEndian(row[4..], entry.Coord.Z);
        entry.Fingerprint.Write(row[8..]);
    }
}
