using System.Collections.ObjectModel;
using System.Text;

namespace UltraSim.Client.Content;

/// <summary>Exact immutable identity of one explicitly installed user presentation pack.</summary>
public readonly record struct UserResourcePackIdentity(
    ContentPackageId PackageId,
    ContentHash ManifestHash) : IComparable<UserResourcePackIdentity>
{
    public bool IsValid => PackageId.IsValid && ManifestHash.IsValid;

    public int CompareTo(UserResourcePackIdentity other)
    {
        int package = PackageId.CompareTo(other.PackageId);
        return package != 0 ? package : ManifestHash.CompareTo(other.ManifestHash);
    }
}

public readonly record struct InstalledUserResourcePack(
    UserResourcePackIdentity Identity,
    int BlobCount,
    long BlobBytes)
{
    public bool IsValid => Identity.IsValid && BlobCount >= 0 && BlobBytes >= 0;
}

/// <summary>Explicit rooted local-file locator for one exact user-pack archive.</summary>
public readonly record struct UserResourcePackFetchRequest(
    UserResourcePackIdentity Identity,
    ContentFilePath Locator)
{
    public bool IsValid => Identity.IsValid && Locator.IsValid;
}

/// <summary>
/// Bounded exact enabled order. Entries are low-to-high priority; the last compatible replacement
/// wins. This is local user policy, not a content-authorization scope or semantic activation.
/// </summary>
public sealed class UserResourcePackStackLease : IDisposable
{
    private ContentCache? _owner;
    private readonly UserResourcePackIdentity[] _identities;
    private readonly ContentPackageManifest[] _manifests;
    private readonly ContentHash[] _blobs;

    internal UserResourcePackStackLease(
        ContentCache owner,
        UserResourcePackIdentity[] identities,
        ContentPackageManifest[] manifests,
        ContentHash[] blobs,
        ContentHash selectionFingerprint)
    {
        _owner = owner;
        _identities = identities;
        _manifests = manifests;
        _blobs = blobs;
        Identities = Array.AsReadOnly(identities);
        SelectionFingerprint = selectionFingerprint;
    }

    public ReadOnlyCollection<UserResourcePackIdentity> Identities { get; }
    public ContentHash SelectionFingerprint { get; }
    public bool IsDisposed => Volatile.Read(ref _owner) is null;

    internal ContentCache? Owner => Volatile.Read(ref _owner);
    internal ReadOnlySpan<ContentPackageManifest> Manifests => _manifests;
    internal ReadOnlySpan<ContentHash> Blobs => _blobs;

    internal bool TryFindHighest(
        LogicalContentId id,
        out UserResourcePackIdentity identity,
        out ContentPackageManifest? manifest,
        out ContentDefinitionDescriptor? definition)
    {
        identity = default;
        manifest = null;
        definition = null;
        if (IsDisposed || !id.IsValid) return false;
        for (int packageIndex = _manifests.Length - 1; packageIndex >= 0; packageIndex--)
        {
            ContentPackageManifest candidate = _manifests[packageIndex];
            foreach (ContentDefinitionDescriptor entry in candidate.Definitions)
            {
                int comparison = entry.Id.CompareTo(id);
                if (comparison < 0) continue;
                if (comparison > 0) break;
                identity = _identities[packageIndex];
                manifest = candidate;
                definition = entry;
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        ContentCache? owner = Interlocked.Exchange(ref _owner, null);
        owner?.ReleaseUserResourcePacks(_identities, _blobs);
    }
}

internal static class UserResourcePackSelectionCodec
{
    private const ulong Magic = 0x3153_4B50_5253_55UL; // USRPKS1
    private const uint Version = 1;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static byte[] Encode(ReadOnlySpan<UserResourcePackIdentity> identities)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(Version);
        writer.Write(identities.Length);
        Span<byte> digest = stackalloc byte[ContentHash.ByteLength];
        foreach (UserResourcePackIdentity identity in identities)
        {
            byte[] package = StrictUtf8.GetBytes(identity.PackageId.Value!);
            writer.Write(package.Length);
            writer.Write(package);
            identity.ManifestHash.WriteDigest(digest);
            writer.Write(digest);
        }
        writer.Flush();
        return stream.ToArray();
    }

    internal static bool TryDecode(
        ReadOnlySpan<byte> bytes,
        out UserResourcePackIdentity[] identities)
    {
        identities = [];
        var reader = new Reader(bytes);
        if (!reader.TryUInt64(out ulong magic) || magic != Magic ||
            !reader.TryUInt32(out uint version) || version != Version ||
            !reader.TryInt32(out int count) || count is < 0 or > 16)
            return false;
        var result = new UserResourcePackIdentity[count];
        var unique = new HashSet<UserResourcePackIdentity>();
        var uniquePackages = new HashSet<ContentPackageId>();
        for (int index = 0; index < result.Length; index++)
        {
            if (!reader.TryString(ContentPackageId.MaximumCharacters, out string packageText) ||
                !ContentPackageId.TryCreate(packageText, out ContentPackageId package) ||
                !reader.TryHash(out ContentHash manifest))
                return false;
            result[index] = new UserResourcePackIdentity(package, manifest);
            if (!result[index].IsValid || !unique.Add(result[index]) ||
                !uniquePackages.Add(package)) return false;
        }
        if (!reader.Complete || !bytes.SequenceEqual(Encode(result))) return false;
        identities = result;
        return true;
    }

    internal static ContentHash Fingerprint(ReadOnlySpan<UserResourcePackIdentity> identities) =>
        ContentHash.Compute(Encode(identities));

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _bytes;
        private int _offset;
        internal Reader(ReadOnlySpan<byte> bytes) => _bytes = bytes;
        internal bool Complete => _offset == _bytes.Length;
        internal bool TryInt32(out int value)
        {
            value = 0;
            if (!Take(sizeof(int), out ReadOnlySpan<byte> bytes)) return false;
            value = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(bytes);
            return true;
        }
        internal bool TryUInt32(out uint value)
        {
            value = 0;
            if (!Take(sizeof(uint), out ReadOnlySpan<byte> bytes)) return false;
            value = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(bytes);
            return true;
        }
        internal bool TryUInt64(out ulong value)
        {
            value = 0;
            if (!Take(sizeof(ulong), out ReadOnlySpan<byte> bytes)) return false;
            value = System.Buffers.Binary.BinaryPrimitives.ReadUInt64LittleEndian(bytes);
            return true;
        }
        internal bool TryHash(out ContentHash hash)
        {
            hash = default;
            if (!Take(ContentHash.ByteLength, out ReadOnlySpan<byte> bytes)) return false;
            try { hash = ContentHash.FromDigest(bytes); }
            catch (ArgumentException) { return false; }
            return true;
        }
        internal bool TryString(int maximum, out string value)
        {
            value = string.Empty;
            if (!TryInt32(out int byteCount) || byteCount < 0 || byteCount > maximum * 4 ||
                !Take(byteCount, out ReadOnlySpan<byte> bytes)) return false;
            try { value = StrictUtf8.GetString(bytes); }
            catch (DecoderFallbackException) { return false; }
            return value.Length <= maximum;
        }
        private bool Take(int length, out ReadOnlySpan<byte> value)
        {
            value = default;
            if (length < 0 || length > _bytes.Length - _offset) return false;
            value = _bytes.Slice(_offset, length);
            _offset += length;
            return true;
        }
    }
}
