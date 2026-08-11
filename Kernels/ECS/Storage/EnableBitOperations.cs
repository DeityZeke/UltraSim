using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Threading;

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

    /// <summary>
    /// Supplemental random-access mutation may target different logical rows
    /// that alias one physical enable word. Keep the generated chunk-owned path
    /// non-atomic and pay CAS only for this exceptional access mode.
    /// </summary>
    internal static void SetAtomic(Span<byte> chunk, in ComponentColumnLayout column, int row, bool enabled)
    {
        Span<ulong> words = GetWords(chunk, column);
        ref long target = ref Unsafe.As<ulong, long>(ref words[row >> 6]);
        ulong mask = 1UL << (row & 63);
        long observed = Volatile.Read(ref target);
        while (true)
        {
            ulong current = unchecked((ulong)observed);
            long desired = unchecked((long)(enabled ? current | mask : current & ~mask));
            long exchanged = Interlocked.CompareExchange(ref target, desired, observed);
            if (exchanged == observed)
                return;
            observed = exchanged;
        }
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

    internal static void CopyRange(
        Span<byte> sourceChunk,
        in ComponentColumnLayout sourceColumn,
        int sourceStartRow,
        Span<byte> destinationChunk,
        in ComponentColumnLayout destinationColumn,
        int destinationStartRow,
        int count)
    {
        if (count == 0)
            return;

        Span<ulong> sourceWords = GetWords(sourceChunk, sourceColumn);
        Span<ulong> destinationWords = GetWords(destinationChunk, destinationColumn);
        int sourceRow = sourceStartRow;
        int destinationRow = destinationStartRow;
        int remaining = count;
        while (remaining != 0)
        {
            int sourceBit = sourceRow & 63;
            int destinationBit = destinationRow & 63;
            int copied = Math.Min(
                remaining,
                Math.Min(64 - sourceBit, 64 - destinationBit));
            ulong lowMask = copied == 64
                ? ulong.MaxValue
                : (1UL << copied) - 1UL;
            ulong bits = (sourceWords[sourceRow >> 6] >> sourceBit) & lowMask;
            ulong destinationMask = lowMask << destinationBit;
            ref ulong destinationWord = ref destinationWords[destinationRow >> 6];
            destinationWord =
                (destinationWord & ~destinationMask) |
                (bits << destinationBit);
            sourceRow += copied;
            destinationRow += copied;
            remaining -= copied;
        }
    }

    private static Span<ulong> GetWords(Span<byte> chunk, in ComponentColumnLayout column) =>
        MemoryMarshal.Cast<byte, ulong>(
            chunk.Slice(column.EnableBitsOffsetBytes, column.EnableBitsBytes));

    private static ulong CreateMask(int offset, int count) =>
        count == 64 ? ulong.MaxValue : ((1UL << count) - 1UL) << offset;

    private static void ApplyMask(ref ulong word, ulong mask, bool enabled) =>
        word = enabled ? word | mask : word & ~mask;
}
