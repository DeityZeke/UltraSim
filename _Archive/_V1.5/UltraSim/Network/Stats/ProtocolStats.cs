using System.Diagnostics;
using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Threading;

namespace UltraSim.Network
{
    // Mo1: hot write counters each on their own 64-byte cache line to prevent false sharing
    // when multiple threads call RecordDecoded / RecordDecodeFailure / RecordUnknown concurrently.
    [StructLayout(LayoutKind.Explicit)]
    public sealed class ProtocolStats
    {
        // Cold config fields — grouped together at the start; read-only after construction.
        [FieldOffset(0)]  private readonly bool _threadSafe;
        [FieldOffset(1)]  private readonly bool _collectDecodeTimings;
        [FieldOffset(2)]  private readonly bool _trackDecodeFailures;
        [FieldOffset(3)]  private readonly bool _decodeTimingRateIsPowerOfTwo;
        [FieldOffset(4)]  private readonly int _decodeTimingSampleRate;
        [FieldOffset(8)]  private readonly uint _decodeTimingSampleMask;

        // Hot write counters — each on its own cache line.
        [FieldOffset(64)]  private int _decodeTimingSampleCounter;
        [FieldOffset(128)] private long _decodedPackets;
        [FieldOffset(192)] private long _decodedBytes;
        [FieldOffset(256)] private long _decodeFailures;
        [FieldOffset(320)] private long _unknownPackets;
        [FieldOffset(384)] private long _unknownBytes;
        [FieldOffset(448)] private long _decodeTicks;

        public ProtocolStats(
            bool collectDecodeTimings = false,
            int decodeTimingSampleRate = 1,
            bool trackDecodeFailures = true,
            bool threadSafe = true)
        {
            if (decodeTimingSampleRate <= 0)
                throw new ArgumentOutOfRangeException(nameof(decodeTimingSampleRate));

            _threadSafe = threadSafe;
            _collectDecodeTimings = collectDecodeTimings;
            _trackDecodeFailures = trackDecodeFailures;
            _decodeTimingSampleRate = decodeTimingSampleRate;
            _decodeTimingRateIsPowerOfTwo = IsPowerOfTwo(decodeTimingSampleRate);
            _decodeTimingSampleMask = _decodeTimingRateIsPowerOfTwo
                ? (uint)(decodeTimingSampleRate - 1)
                : 0;
        }

        public static ProtocolStats FromProfile(
            NetworkStatsProfile profile,
            bool threadSafe = true) =>
            FromProfile(NetworkStatsProfiles.Resolve(profile), threadSafe);

        public static ProtocolStats FromProfile(
            in NetworkStatsProfileSettings settings,
            bool threadSafe = true) =>
            new ProtocolStats(
                collectDecodeTimings: settings.CollectDecodeTimings,
                decodeTimingSampleRate: settings.DecodeTimingSampleRate,
                trackDecodeFailures: settings.TrackDecodeFailures,
                threadSafe: threadSafe);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long GetTimestamp() => Stopwatch.GetTimestamp();

        public bool CollectDecodeTimings
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _collectDecodeTimings;
        }

        public int DecodeTimingSampleRate
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _decodeTimingSampleRate;
        }

        public bool TrackDecodeFailures
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _trackDecodeFailures;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool ShouldMeasureDecodeTiming()
        {
            if (!_collectDecodeTimings)
                return false;
            if (_decodeTimingSampleRate == 1)
                return true;

            uint counter = _threadSafe
                ? unchecked((uint)Interlocked.Increment(ref _decodeTimingSampleCounter))
                : unchecked((uint)++_decodeTimingSampleCounter);
            if (_decodeTimingRateIsPowerOfTwo)
                return (counter & _decodeTimingSampleMask) == 0;

            return (counter % (uint)_decodeTimingSampleRate) == 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordDecoded(int payloadBytes, long elapsedTicks)
        {
            if (elapsedTicks > 0)
            {
                RecordDecodedWithTiming(payloadBytes, elapsedTicks);
                return;
            }

            RecordDecodedNoTiming(payloadBytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordDecodeFailure(int payloadBytes, long elapsedTicks)
        {
            if (elapsedTicks > 0)
            {
                RecordDecodeFailureWithTiming(payloadBytes, elapsedTicks);
                return;
            }

            RecordDecodeFailureNoTiming(payloadBytes);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordDecodedNoTiming(int payloadBytes)
        {
            if (_threadSafe)
            {
                Interlocked.Increment(ref _decodedPackets);
                Interlocked.Add(ref _decodedBytes, payloadBytes);
                return;
            }

            _decodedPackets++;
            _decodedBytes += payloadBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordDecodedWithTiming(int payloadBytes, long elapsedTicks)
        {
            if (_threadSafe)
            {
                Interlocked.Increment(ref _decodedPackets);
                Interlocked.Add(ref _decodedBytes, payloadBytes);
                Interlocked.Add(ref _decodeTicks, elapsedTicks);
                return;
            }

            _decodedPackets++;
            _decodedBytes += payloadBytes;
            _decodeTicks += elapsedTicks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordDecodeFailureNoTiming(int payloadBytes)
        {
            if (_threadSafe)
            {
                Interlocked.Increment(ref _decodeFailures);
                Interlocked.Add(ref _decodedBytes, payloadBytes);
                return;
            }

            _decodeFailures++;
            _decodedBytes += payloadBytes;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordDecodeFailureWithTiming(int payloadBytes, long elapsedTicks)
        {
            if (_threadSafe)
            {
                Interlocked.Increment(ref _decodeFailures);
                Interlocked.Add(ref _decodedBytes, payloadBytes);
                Interlocked.Add(ref _decodeTicks, elapsedTicks);
                return;
            }

            _decodeFailures++;
            _decodedBytes += payloadBytes;
            _decodeTicks += elapsedTicks;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void RecordUnknown(int payloadBytes)
        {
            if (_threadSafe)
            {
                Interlocked.Increment(ref _unknownPackets);
                Interlocked.Add(ref _unknownBytes, payloadBytes);
                return;
            }

            _unknownPackets++;
            _unknownBytes += payloadBytes;
        }

        public ProtocolStatsSnapshot Snapshot()
        {
            if (_threadSafe)
            {
                return new ProtocolStatsSnapshot(
                    decodedPackets: Volatile.Read(ref _decodedPackets),
                    decodedBytes: Volatile.Read(ref _decodedBytes),
                    decodeFailures: Volatile.Read(ref _decodeFailures),
                    unknownPackets: Volatile.Read(ref _unknownPackets),
                    unknownBytes: Volatile.Read(ref _unknownBytes),
                    decodeTicks: Volatile.Read(ref _decodeTicks));
            }

            Thread.MemoryBarrier();
            return new ProtocolStatsSnapshot(
                decodedPackets: _decodedPackets,
                decodedBytes: _decodedBytes,
                decodeFailures: _decodeFailures,
                unknownPackets: _unknownPackets,
                unknownBytes: _unknownBytes,
                decodeTicks: _decodeTicks);
        }

        public void Reset()
        {
            if (_threadSafe)
            {
                Interlocked.Exchange(ref _decodedPackets, 0);
                Interlocked.Exchange(ref _decodedBytes, 0);
                Interlocked.Exchange(ref _decodeFailures, 0);
                Interlocked.Exchange(ref _unknownPackets, 0);
                Interlocked.Exchange(ref _unknownBytes, 0);
                Interlocked.Exchange(ref _decodeTicks, 0);
                Interlocked.Exchange(ref _decodeTimingSampleCounter, 0);
                return;
            }

            _decodedPackets = 0;
            _decodedBytes = 0;
            _decodeFailures = 0;
            _unknownPackets = 0;
            _unknownBytes = 0;
            _decodeTicks = 0;
            _decodeTimingSampleCounter = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsPowerOfTwo(int value) => (value & (value - 1)) == 0;
    }
}
