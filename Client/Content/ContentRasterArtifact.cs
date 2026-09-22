using System.Buffers.Binary;

namespace UltraSim.Client.Content;

/// <summary>Bounded passive raster facts established before renderer-native decode.</summary>
public readonly record struct ContentRasterArtifactInfo(
    int Width,
    int Height,
    long MaximumDecodedRgbaBytes)
{
    public bool IsValid => Width is > 0 and <= ContentRasterArtifact.MaximumDimension &&
        Height is > 0 and <= ContentRasterArtifact.MaximumDimension &&
        MaximumDecodedRgbaBytes is > 0 and <= ContentRasterArtifact.MaximumDecodedBytes;
}

/// <summary>
/// Strict, allocation-free PNG/WebP envelope and dimension inspection. This is a bounded
/// classification step before the engine decoder, not a claim that arbitrary compressed input is
/// safe. The renderer must still validate the actual decoded dimensions and byte count.
/// </summary>
public static class ContentRasterArtifact
{
    public const int MaximumDimension = 16_384;
    public const long MaximumDecodedBytes = 256L * 1024 * 1024;
    public const long MaximumUserPackFileBytes = 16L * 1024 * 1024;

    private static ReadOnlySpan<byte> PngSignature =>
        [137, 80, 78, 71, 13, 10, 26, 10];

    public static bool TryInspect(
        ContentFileKind kind,
        ReadOnlySpan<byte> bytes,
        out ContentRasterArtifactInfo info)
    {
        info = default;
        if (!TryReadDimensions(kind, bytes, out int width, out int height)) return false;
        long decoded;
        try { decoded = checked((long)width * height * 4); }
        catch (OverflowException) { return false; }
        info = new ContentRasterArtifactInfo(width, height, decoded);
        return info.IsValid;
    }

    private static bool TryReadDimensions(
        ContentFileKind kind,
        ReadOnlySpan<byte> bytes,
        out int width,
        out int height)
    {
        width = 0;
        height = 0;
        if (kind == ContentFileKind.PngTexture)
        {
            if (bytes.Length < 24 || !bytes[..8].SequenceEqual(PngSignature) ||
                !bytes[12..16].SequenceEqual("IHDR"u8)) return false;
            uint rawWidth = BinaryPrimitives.ReadUInt32BigEndian(bytes[16..20]);
            uint rawHeight = BinaryPrimitives.ReadUInt32BigEndian(bytes[20..24]);
            if (rawWidth is 0 or > MaximumDimension || rawHeight is 0 or > MaximumDimension)
                return false;
            width = (int)rawWidth;
            height = (int)rawHeight;
            return true;
        }

        if (kind != ContentFileKind.WebpTexture || bytes.Length < 20 ||
            !bytes[..4].SequenceEqual("RIFF"u8) || !bytes[8..12].SequenceEqual("WEBP"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) + 8UL != (ulong)bytes.Length)
            return false;

        int offset = 12;
        while (offset <= bytes.Length - 8)
        {
            ReadOnlySpan<byte> tag = bytes.Slice(offset, 4);
            uint rawLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset + 4, 4));
            if (rawLength > int.MaxValue) return false;
            int length = (int)rawLength;
            int dataOffset = offset + 8;
            if (length > bytes.Length - dataOffset) return false;
            ReadOnlySpan<byte> data = bytes.Slice(dataOffset, length);
            if (tag.SequenceEqual("VP8X"u8) && data.Length >= 10)
            {
                width = 1 + ReadUInt24LittleEndian(data[4..7]);
                height = 1 + ReadUInt24LittleEndian(data[7..10]);
                return ValidDimensions(width, height);
            }
            if (tag.SequenceEqual("VP8L"u8) && data.Length >= 5 && data[0] == 0x2f)
            {
                width = 1 + data[1] + ((data[2] & 0x3f) << 8);
                height = 1 + (data[2] >> 6) + (data[3] << 2) + ((data[4] & 0x0f) << 10);
                return ValidDimensions(width, height);
            }
            if (tag.SequenceEqual("VP8 "u8) && data.Length >= 10 &&
                data[3] == 0x9d && data[4] == 0x01 && data[5] == 0x2a)
            {
                width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..8]) & 0x3fff;
                height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]) & 0x3fff;
                return ValidDimensions(width, height);
            }
            int padded;
            try { padded = checked(length + (length & 1)); }
            catch (OverflowException) { return false; }
            if (padded > bytes.Length - dataOffset) return false;
            offset = dataOffset + padded;
        }
        return false;
    }

    private static int ReadUInt24LittleEndian(ReadOnlySpan<byte> bytes) =>
        bytes[0] | (bytes[1] << 8) | (bytes[2] << 16);

    private static bool ValidDimensions(int width, int height) =>
        width is > 0 and <= MaximumDimension && height is > 0 and <= MaximumDimension;
}
