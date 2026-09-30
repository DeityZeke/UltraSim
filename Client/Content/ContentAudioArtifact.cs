using System.Buffers.Binary;

namespace UltraSim.Client.Content;

public readonly record struct ContentAudioArtifactInfo(int SampleRate, int Samples, int DataOffset)
{
    public int DurationMilliseconds => checked(Samples * 1000 / SampleRate);
}

/// <summary>
/// Narrow passive audio admission: exact 44-byte PCM16 mono WAV, 22.05/44.1 kHz,
/// at most 20 seconds and 2 MiB. No chunks, external references, codecs, or native
/// resource types are accepted from an untrusted package.
/// </summary>
public static class ContentAudioArtifact
{
    public const int MaximumBytes = 2 * 1024 * 1024;

    public static bool TryInspect(ReadOnlySpan<byte> bytes, out ContentAudioArtifactInfo info)
    {
        info = default;
        if (bytes.Length is < 46 or > MaximumBytes ||
            !bytes[..4].SequenceEqual("RIFF"u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[4..8]) != bytes.Length - 8 ||
            !bytes[8..12].SequenceEqual("WAVE"u8) ||
            !bytes[12..16].SequenceEqual("fmt "u8) ||
            BinaryPrimitives.ReadUInt32LittleEndian(bytes[16..20]) != 16 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[20..22]) != 1 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[22..24]) != 1 ||
            !bytes[36..40].SequenceEqual("data"u8)) return false;
        int rate = BinaryPrimitives.ReadInt32LittleEndian(bytes[24..28]);
        if (rate is not (22_050 or 44_100) ||
            BinaryPrimitives.ReadInt32LittleEndian(bytes[28..32]) != rate * 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[32..34]) != 2 ||
            BinaryPrimitives.ReadUInt16LittleEndian(bytes[34..36]) != 16) return false;
        uint dataBytes = BinaryPrimitives.ReadUInt32LittleEndian(bytes[40..44]);
        if (dataBytes != bytes.Length - 44 || dataBytes == 0 || (dataBytes & 1) != 0)
            return false;
        int samples = checked((int)dataBytes / 2);
        if (samples > rate * 20) return false;
        info = new(rate, samples, 44);
        return true;
    }
}
