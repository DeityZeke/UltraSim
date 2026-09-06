using System.Buffers.Binary;
using System.Text;

namespace UltraSim.Client.Content;

/// <summary>
/// Canonical passive package envelope used by local files and HTTP byte sources. The manifest and
/// every payload remain subject to their existing semantic hashes and passive-format validators.
/// </summary>
public static class ContentPackageArchive
{
    private const ulong Magic = 0x3147_4B50_4353_55UL; // USCPKG1
    private const uint FormatVersion = 1;
    private const int FixedHeaderBytes = sizeof(ulong) + sizeof(uint) + sizeof(int) + sizeof(int);
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    public static bool TryEncodeCanonical(
        ContentPackageManifest manifest,
        IEnumerable<ContentFilePayload> payloads,
        out byte[] archive,
        ContentLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        ArgumentNullException.ThrowIfNull(payloads);
        archive = [];
        limits = ContentLimits.Resolve(limits);
        ContentFilePayload[] ordered = payloads.Take(limits.MaximumFilesPerPackage + 1).ToArray();
        if (ordered.Length > limits.MaximumFilesPerPackage ||
            !ContentPackageValidator.TryValidatePayloads(manifest, ordered, out _, out _))
            return false;
        Array.Sort(ordered, static (left, right) => left.Path.CompareTo(right.Path));

        byte[] manifestBytes = ContentCacheMetadata.EncodeManifest(manifest);
        long encodedLength = FixedHeaderBytes + manifestBytes.LongLength;
        foreach (ContentFilePayload payload in ordered)
        {
            int pathBytes = StrictUtf8.GetByteCount(payload.Path.Value!);
            encodedLength = checked(encodedLength + sizeof(int) + pathBytes + sizeof(long) + payload.Bytes.Length);
        }
        if (encodedLength > int.MaxValue) return false;

        using var stream = new MemoryStream(checked((int)encodedLength));
        using var writer = new BinaryWriter(stream, StrictUtf8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(manifestBytes.Length);
        writer.Write(ordered.Length);
        writer.Write(manifestBytes);
        foreach (ContentFilePayload payload in ordered)
        {
            WriteString(writer, payload.Path.Value!);
            writer.Write((long)payload.Bytes.Length);
            writer.Write(payload.Bytes.Span);
        }
        writer.Flush();
        archive = stream.ToArray();
        return archive.Length == encodedLength;
    }

    public static bool TryDecodeCanonical(
        ReadOnlyMemory<byte> archive,
        ContentPackageId expectedPackage,
        ContentHash expectedManifestHash,
        out FetchedContentPackage? package,
        ContentLimits limits = default)
    {
        package = null;
        limits = ContentLimits.Resolve(limits);
        if (!expectedPackage.IsValid || !expectedManifestHash.IsValid || archive.Length < FixedHeaderBytes)
            return false;

        var reader = new ArchiveReader(archive);
        if (!reader.TryReadUInt64(out ulong magic) || magic != Magic ||
            !reader.TryReadUInt32(out uint version) || version != FormatVersion ||
            !reader.TryReadInt32(out int manifestLength) || manifestLength <= 0 ||
            !reader.TryReadInt32(out int payloadCount) || payloadCount < 0 ||
            payloadCount > limits.MaximumFilesPerPackage ||
            !reader.TryTake(manifestLength, out ReadOnlyMemory<byte> manifestBytes) ||
            !ContentCacheMetadata.TryDecodeManifest(
                manifestBytes.Span, expectedManifestHash, limits, out ContentPackageManifest? manifest) ||
            manifest!.PackageId != expectedPackage || payloadCount != manifest.Files.Count)
            return false;

        var payloads = new ContentFilePayload[payloadCount];
        long totalPayloadBytes = 0;
        for (int index = 0; index < payloads.Length; index++)
        {
            ContentFileDescriptor expected = manifest.Files[index];
            if (!reader.TryReadString(ContentFilePath.MaximumCharacters, out string pathText) ||
                !ContentFilePath.TryCreate(pathText, out ContentFilePath path) || path != expected.Path ||
                !reader.TryReadInt64(out long length) || length != expected.SizeBytes ||
                length < 0 || length > limits.MaximumFileBytes || length > int.MaxValue ||
                !reader.TryTake(checked((int)length), out ReadOnlyMemory<byte> bytes))
                return false;
            totalPayloadBytes = checked(totalPayloadBytes + length);
            if (totalPayloadBytes > limits.MaximumPackageBytes) return false;
            payloads[index] = new ContentFilePayload(path, bytes);
        }
        if (!reader.IsComplete ||
            !ContentPackageValidator.TryValidatePayloads(manifest, payloads, out _, out _))
            return false;
        package = new FetchedContentPackage(manifest, payloads, limits);
        return true;
    }

    private static void WriteString(BinaryWriter writer, string value)
    {
        byte[] bytes = StrictUtf8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    private ref struct ArchiveReader
    {
        private readonly ReadOnlyMemory<byte> _memory;
        private int _offset;

        internal ArchiveReader(ReadOnlyMemory<byte> memory) => _memory = memory;
        internal bool IsComplete => _offset == _memory.Length;

        internal bool TryReadInt32(out int value)
        {
            value = 0;
            if (!TryTake(sizeof(int), out ReadOnlyMemory<byte> bytes)) return false;
            value = BinaryPrimitives.ReadInt32LittleEndian(bytes.Span);
            return true;
        }

        internal bool TryReadUInt32(out uint value)
        {
            value = 0;
            if (!TryTake(sizeof(uint), out ReadOnlyMemory<byte> bytes)) return false;
            value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Span);
            return true;
        }

        internal bool TryReadInt64(out long value)
        {
            value = 0;
            if (!TryTake(sizeof(long), out ReadOnlyMemory<byte> bytes)) return false;
            value = BinaryPrimitives.ReadInt64LittleEndian(bytes.Span);
            return true;
        }

        internal bool TryReadUInt64(out ulong value)
        {
            value = 0;
            if (!TryTake(sizeof(ulong), out ReadOnlyMemory<byte> bytes)) return false;
            value = BinaryPrimitives.ReadUInt64LittleEndian(bytes.Span);
            return true;
        }

        internal bool TryReadString(int maximumCharacters, out string value)
        {
            value = string.Empty;
            if (!TryReadInt32(out int byteCount) || byteCount < 0 ||
                byteCount > checked(maximumCharacters * 4) ||
                !TryTake(byteCount, out ReadOnlyMemory<byte> bytes))
                return false;
            try { value = StrictUtf8.GetString(bytes.Span); }
            catch (DecoderFallbackException) { return false; }
            return value.Length <= maximumCharacters;
        }

        internal bool TryTake(int length, out ReadOnlyMemory<byte> result)
        {
            result = default;
            if (length < 0 || length > _memory.Length - _offset) return false;
            result = _memory.Slice(_offset, length);
            _offset += length;
            return true;
        }
    }
}
