using System.Runtime.InteropServices;

namespace UltraSim.ECS;

/// <summary>Allocation-free scalar and range operations over one planned enable bitset.</summary>
internal static class EnableBitOperations
{
    internal static bool Get(Span<byte> chunk, in ComponentColumnLayout column, int row)
    {
        Span<ulong> words = GetWords(chunk, column);
        return (words[row >> 6] & (1UL << (row & 63))) != 0;
    }

    internal static void Set(Span<byte> chunk, in ComponentColumnLayout column, int row, bool enabled)
    {
        Span<ulong> words = GetWords(chunk, column);
        ref ulong word = ref words[row >> 6];
        ulong mask = 1UL << (row & 63);
        word = enabled ? word | mask : word & ~mask;
    }

    internal static void SetRange(
        Span<byte> chunk,
        in ComponentColumnLayout column,
        int startRow,
        int count,
        bool enabled)
    {
        if (count == 0)
            return;

        Span<ulong> words = GetWords(chunk, column);
        int wordIndex = startRow >> 6;
        int bitOffset = startRow & 63;
        int remaining = count;

        if (bitOffset != 0)
        {
            int firstCount = Math.Min(64 - bitOffset, remaining);
            ApplyMask(ref words[wordIndex], CreateMask(bitOffset, firstCount), enabled);
            wordIndex++;
            remaining -= firstCount;
        }

        int wholeWordCount = remaining >> 6;
        if (wholeWordCount != 0)
        {
            words.Slice(wordIndex, wholeWordCount).Fill(enabled ? ulong.MaxValue : 0UL);
            wordIndex += wholeWordCount;
            remaining -= wholeWordCount << 6;
        }

        if (remaining != 0)
            ApplyMask(ref words[wordIndex], CreateMask(0, remaining), enabled);
    }

    private static Span<ulong> GetWords(Span<byte> chunk, in ComponentColumnLayout column) =>
        MemoryMarshal.Cast<byte, ulong>(
            chunk.Slice(column.EnableBitsOffsetBytes, column.EnableBitsBytes));

    private static ulong CreateMask(int offset, int count) =>
        count == 64 ? ulong.MaxValue : ((1UL << count) - 1UL) << offset;

    private static void ApplyMask(ref ulong word, ulong mask, bool enabled) =>
        word = enabled ? word | mask : word & ~mask;
}
