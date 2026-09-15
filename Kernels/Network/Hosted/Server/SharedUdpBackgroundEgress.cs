using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;

namespace UltraSim.Network;

/// <summary>Lifecycle of the optional listener-owned UDP egress worker.</summary>
public enum SharedUdpBackgroundEgressState : byte
{
    Disabled = 0,
    Running = 1,
    Stopping = 2,
    Disposed = 3,
    Faulted = 4,
}

/// <summary>Cold immutable evidence for one bounded background-egress lifetime.</summary>
public readonly record struct SharedUdpBackgroundEgressSnapshot(
    SharedUdpBackgroundEgressState State,
    bool UsesNativeBatching,
    bool TimingEnabled,
    int SenderThreads,
    int CapacityDatagrams,
    long CapacityBytes,
    long ReservedBytes,
    int CurrentDatagrams,
    long CurrentBytes,
    int HighWaterDatagrams,
    long HighWaterBytes,
    ulong AcceptedDatagrams,
    ulong AcceptedBytes,
    ulong PhysicallySentDatagrams,
    ulong PhysicallySentBytes,
    ulong StalePathDrops,
    ulong PhysicalSendFailures,
    ulong CapacityRefusals,
    ulong WorkerWakeups,
    ulong DrainPasses,
    ulong ScalarSendCalls,
    ulong NativeSendCalls,
    ulong PartialNativeSendCalls,
    ulong WouldBlockEvents,
    ulong NoBufferEvents,
    int DrainBatchP50,
    int DrainBatchP95,
    int MaximumBatch,
    int MaximumPhysicallySentDatagramBytes,
    NetworkTimingDistribution ProducerEnqueue,
    NetworkTimingDistribution QueueResidence,
    ulong OldestQueuedStopwatchTicks,
    ulong PhysicalSendSystemCallStopwatchTicks,
    ulong WorkerServiceStopwatchTicks,
    bool HasWorkerFailure,
    SocketError LastSocketError);

internal interface ISharedUdpBackgroundEgressTestHook
{
    void BeforePhysicalSend();

    bool TryOverrideSend(int requested, out int sent, out int error);
}

/// <summary>
/// One fixed-capacity SPSC queue and persistent sender per shared listener.
/// The listener owner is the only producer; the worker is the only consumer.
/// Enqueue acceptance transfers ownership of a copied, finalized datagram.
/// </summary>
internal sealed class SharedUdpBackgroundEgress : IDisposable
{
    private const int Running = 0;
    private const int Stopping = 1;
    private const int Disposed = 2;
    private const int Faulted = 3;

    private readonly Socket _socket;
    private readonly int _socketDescriptor;
    private readonly int _maximumDatagramBytes;
    private readonly int _maximumBatch;
    private readonly int _drainTimeoutMilliseconds;
    private readonly bool _usesNativeBatching;
    private readonly bool _timingEnabled;
    private readonly object _pathGate;
    private readonly ISharedUdpBackgroundEgressTestHook? _testHook;
    private readonly byte[] _payloads;
    private readonly byte[] _addresses;
    private readonly int[] _lengths;
    private readonly SharedUdpServerEndpoint?[] _endpoints;
    private readonly int[] _pathGenerations;
    private readonly long[] _enqueuedAt;
    private readonly long[] _delayBuckets = new long[64];
    private readonly long[] _enqueueBuckets = new long[64];
    private readonly long[] _batchHistogram;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;
    private readonly SocketAddress _scalarAddress = new(AddressFamily.InterNetwork, 16);
    private int _head;
    private int _tail;
    private int _count;
    private long _currentBytes;
    private int _highWaterDatagrams;
    private long _highWaterBytes;
    private int _state;
    private long _acceptedDatagrams;
    private long _acceptedBytes;
    private long _physicallySentDatagrams;
    private long _physicallySentBytes;
    private long _stalePathDrops;
    private long _physicalSendFailures;
    private long _capacityRefusals;
    private long _workerWakeups;
    private long _drainPasses;
    private long _scalarSendCalls;
    private long _nativeSendCalls;
    private long _partialNativeSendCalls;
    private long _wouldBlockEvents;
    private long _noBufferEvents;
    private int _maximumObservedBatch;
    private int _maximumPhysicallySentDatagramBytes;
    private long _enqueueCount;
    private long _enqueueTotal;
    private long _enqueueMaximum;
    private long _delayCount;
    private long _delayTotal;
    private long _delayMaximum;
    private long _workerServiceTicks;
    private long _physicalSendSystemCallTicks;
    private int _lastSocketError;
    private ExceptionDispatchInfo? _workerFailure;
    private bool _disposed;

    internal SharedUdpBackgroundEgress(
        Socket socket,
        int socketDescriptor,
        int maximumDatagramBytes,
        int capacityDatagrams,
        int maximumBatch,
        int drainTimeoutMilliseconds,
        bool usesNativeBatching,
        object pathGate,
        bool timingEnabled,
        ISharedUdpBackgroundEgressTestHook? testHook)
    {
        ArgumentNullException.ThrowIfNull(socket);
        ArgumentNullException.ThrowIfNull(pathGate);
        if (maximumDatagramBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumDatagramBytes));
        if (capacityDatagrams <= 0) throw new ArgumentOutOfRangeException(nameof(capacityDatagrams));
        if (maximumBatch is < 2 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumBatch));
        if (drainTimeoutMilliseconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(drainTimeoutMilliseconds));
        _socket = socket;
        _socketDescriptor = socketDescriptor;
        _maximumDatagramBytes = maximumDatagramBytes;
        _maximumBatch = maximumBatch;
        _drainTimeoutMilliseconds = drainTimeoutMilliseconds;
        _usesNativeBatching = usesNativeBatching;
        _timingEnabled = timingEnabled;
        _pathGate = pathGate;
        _testHook = testHook;
        _payloads = new byte[checked(capacityDatagrams * maximumDatagramBytes)];
        _addresses = new byte[checked(capacityDatagrams * 16)];
        _lengths = new int[capacityDatagrams];
        _endpoints = new SharedUdpServerEndpoint[capacityDatagrams];
        _pathGenerations = new int[capacityDatagrams];
        _enqueuedAt = new long[capacityDatagrams];
        _batchHistogram = new long[maximumBatch + 1];
        _worker = new Thread(Run)
        {
            IsBackground = true,
            Name = "UltraSim shared UDP egress",
        };
        _worker.Start();
    }

    internal int CapacityDatagrams => _lengths.Length;
    internal long CapacityBytes => checked((long)_lengths.Length * _maximumDatagramBytes);
    internal long ReservedBytes => checked(
        CapacityBytes +
        (long)_lengths.Length * (16 + sizeof(int) + IntPtr.Size + sizeof(int) + sizeof(long)) +
        (long)_batchHistogram.Length * sizeof(long) +
        (long)(_delayBuckets.Length + _enqueueBuckets.Length) * sizeof(long));

    internal DatagramSendStatus TryEnqueue(
        SharedUdpServerEndpoint endpoint,
        ReadOnlySpan<byte> datagram)
    {
        ThrowIfFaulted();
        long started = _timingEnabled ? Stopwatch.GetTimestamp() : 0;
        if (Volatile.Read(ref _state) != Running ||
            Volatile.Read(ref _count) == _lengths.Length)
        {
            SaturatingIncrement(ref _capacityRefusals);
            return DatagramSendStatus.CapacityExceeded;
        }

        int slot = _tail;
        datagram.CopyTo(_payloads.AsSpan(slot * _maximumDatagramBytes));
        SocketAddress address = endpoint.RemoteAddress;
        Span<byte> destination = _addresses.AsSpan(slot * 16, 16);
        for (int index = 0; index < destination.Length; index++)
            destination[index] = (byte)address[index];
        _lengths[slot] = datagram.Length;
        _endpoints[slot] = endpoint;
        _pathGenerations[slot] = endpoint.CaptureEgressPathGeneration();
        _enqueuedAt[slot] = _timingEnabled ? Stopwatch.GetTimestamp() : 0;
        _tail = (slot + 1) % _lengths.Length;
        Interlocked.Add(ref _currentBytes, datagram.Length);
        int count = Interlocked.Increment(ref _count);
        AtomicMaximum(ref _highWaterDatagrams, count);
        AtomicMaximum(ref _highWaterBytes, Volatile.Read(ref _currentBytes));
        SaturatingIncrement(ref _acceptedDatagrams);
        SaturatingAdd(ref _acceptedBytes, datagram.Length);
        if (_timingEnabled)
            RecordDistribution(
                Stopwatch.GetTimestamp() - started,
                _enqueueBuckets,
                ref _enqueueCount,
                ref _enqueueTotal,
                ref _enqueueMaximum);
        if (count == 1) _wake.Set();
        return DatagramSendStatus.Accepted;
    }

    internal SharedUdpBackgroundEgressSnapshot GetSnapshot()
    {
        int count = Volatile.Read(ref _count);
        ulong oldest = 0;
        if (_timingEnabled && count > 0)
        {
            long enqueued = Volatile.Read(ref _enqueuedAt[Volatile.Read(ref _head)]);
            long age = Stopwatch.GetTimestamp() - enqueued;
            oldest = checked((ulong)Math.Max(0, age));
        }
        return new(
            MapState(Volatile.Read(ref _state)),
            _usesNativeBatching,
            _timingEnabled,
            Volatile.Read(ref _state) is Running or Stopping ? 1 : 0,
            _lengths.Length,
            CapacityBytes,
            ReservedBytes,
            count,
            Volatile.Read(ref _currentBytes),
            Volatile.Read(ref _highWaterDatagrams),
            Volatile.Read(ref _highWaterBytes),
            ReadUnsigned(ref _acceptedDatagrams),
            ReadUnsigned(ref _acceptedBytes),
            ReadUnsigned(ref _physicallySentDatagrams),
            ReadUnsigned(ref _physicallySentBytes),
            ReadUnsigned(ref _stalePathDrops),
            ReadUnsigned(ref _physicalSendFailures),
            ReadUnsigned(ref _capacityRefusals),
            ReadUnsigned(ref _workerWakeups),
            ReadUnsigned(ref _drainPasses),
            ReadUnsigned(ref _scalarSendCalls),
            ReadUnsigned(ref _nativeSendCalls),
            ReadUnsigned(ref _partialNativeSendCalls),
            ReadUnsigned(ref _wouldBlockEvents),
            ReadUnsigned(ref _noBufferEvents),
            BatchPercentile(50),
            BatchPercentile(95),
            Volatile.Read(ref _maximumObservedBatch),
            Volatile.Read(ref _maximumPhysicallySentDatagramBytes),
            SnapshotDistribution(
                _enqueueBuckets, ref _enqueueCount, ref _enqueueTotal, ref _enqueueMaximum),
            SnapshotDistribution(
                _delayBuckets, ref _delayCount, ref _delayTotal, ref _delayMaximum),
            oldest,
            ReadUnsigned(ref _physicalSendSystemCallTicks),
            ReadUnsigned(ref _workerServiceTicks),
            Volatile.Read(ref _workerFailure) is not null,
            (SocketError)Volatile.Read(ref _lastSocketError));
    }

    internal void ThrowIfFaulted()
    {
        Volatile.Read(ref _workerFailure)?.Throw();
    }

    private void Run()
    {
        try
        {
            long stopStarted = 0;
            while (true)
            {
                int state = Volatile.Read(ref _state);
                int count = Volatile.Read(ref _count);
                if (state == Stopping)
                {
                    if (stopStarted == 0) stopStarted = Stopwatch.GetTimestamp();
                    if (count == 0) break;
                    if (Stopwatch.GetElapsedTime(stopStarted).TotalMilliseconds >=
                        _drainTimeoutMilliseconds)
                    {
                        DiscardAllAsPhysicalFailure();
                        break;
                    }
                }
                else if (state != Running)
                {
                    break;
                }

                if (count == 0)
                {
                    _wake.WaitOne();
                    SaturatingIncrement(ref _workerWakeups);
                    continue;
                }

                long started = _timingEnabled ? Stopwatch.GetTimestamp() : 0;
                SaturatingIncrement(ref _drainPasses);
                bool madeProgress = _usesNativeBatching
                    ? DrainNative()
                    : DrainScalar();
                if (_timingEnabled)
                    SaturatingAdd(ref _workerServiceTicks, Stopwatch.GetTimestamp() - started);
                if (!madeProgress) _wake.WaitOne(1);
            }
            Volatile.Write(ref _state, Disposed);
        }
        catch (Exception exception)
        {
            Volatile.Write(ref _workerFailure, ExceptionDispatchInfo.Capture(exception));
            Volatile.Write(ref _state, Faulted);
            _wake.Set();
        }
    }

    private bool DrainScalar()
    {
        int slot = Volatile.Read(ref _head);
        SharedUdpServerEndpoint? endpoint = _endpoints[slot];
        if (!IsCurrent(slot, endpoint))
        {
            SaturatingIncrement(ref _stalePathDrops);
            Consume(1, physicallySent: false);
            return true;
        }

        _testHook?.BeforePhysicalSend();
        int sent;
        int error;
        lock (_pathGate)
        {
            if (!IsCurrent(slot, endpoint))
            {
                SaturatingIncrement(ref _stalePathDrops);
                Consume(1, physicallySent: false);
                return true;
            }
            if (_testHook?.TryOverrideSend(1, out sent, out error) != true)
            {
                for (int index = 0; index < 16; index++)
                    _scalarAddress[index] = _addresses[slot * 16 + index];
                long sendStarted = _timingEnabled ? Stopwatch.GetTimestamp() : 0;
                try
                {
                    SaturatingIncrement(ref _scalarSendCalls);
                    sent = _socket.SendTo(
                        _payloads.AsSpan(slot * _maximumDatagramBytes, _lengths[slot]),
                        SocketFlags.None,
                        _scalarAddress);
                    error = sent == _lengths[slot] ? 0 : 105;
                }
                catch (SocketException exception)
                {
                    sent = -1;
                    error = (int)exception.SocketErrorCode;
                }
                if (_timingEnabled)
                    SaturatingAdd(ref _physicalSendSystemCallTicks,
                        Stopwatch.GetTimestamp() - sendStarted);
            }
        }
        if (sent == _lengths[slot])
        {
            AtomicMaximum(ref _maximumObservedBatch, 1);
            SaturatingIncrement(ref _batchHistogram[1]);
            Consume(1, physicallySent: true);
            return true;
        }
        return HandleSendFailure(error);
    }

    private bool DrainNative()
    {
        _testHook?.BeforePhysicalSend();
        int requested = Math.Min(Volatile.Read(ref _count), _maximumBatch);
        int head = Volatile.Read(ref _head);
        for (int offset = 0; offset < requested; offset++)
        {
            int slot = (head + offset) % _lengths.Length;
            if (IsCurrent(slot, _endpoints[slot])) continue;
            if (offset == 0)
            {
                SaturatingIncrement(ref _stalePathDrops);
                Consume(1, physicallySent: false);
                return true;
            }
            requested = offset;
            break;
        }

        int sent;
        int error;
        lock (_pathGate)
        {
            for (int offset = 0; offset < requested; offset++)
            {
                int slot = (head + offset) % _lengths.Length;
                if (IsCurrent(slot, _endpoints[slot])) continue;
                requested = offset;
                break;
            }
            if (requested == 0) return true;
            if (_testHook?.TryOverrideSend(requested, out sent, out error) != true)
            {
                SaturatingIncrement(ref _nativeSendCalls);
                long sendStarted = _timingEnabled ? Stopwatch.GetTimestamp() : 0;
                sent = LinuxUdpBatch.Send(
                    _socketDescriptor,
                    _payloads,
                    _maximumDatagramBytes,
                    _addresses,
                    16,
                    _lengths,
                    head,
                    _lengths.Length,
                    requested,
                    out error);
                if (_timingEnabled)
                    SaturatingAdd(ref _physicalSendSystemCallTicks,
                        Stopwatch.GetTimestamp() - sendStarted);
            }
        }
        if (sent > 0)
        {
            if (sent < requested) SaturatingIncrement(ref _partialNativeSendCalls);
            AtomicMaximum(ref _maximumObservedBatch, sent);
            SaturatingIncrement(ref _batchHistogram[sent]);
            Consume(sent, physicallySent: true);
            return true;
        }
        if (sent == 0) return false;
        return HandleSendFailure(error);
    }

    private bool HandleSendFailure(int error)
    {
        Volatile.Write(ref _lastSocketError, (int)MapSocketError(error));
        if (error is 11 or (int)SocketError.WouldBlock or (int)SocketError.IOPending)
        {
            SaturatingIncrement(ref _wouldBlockEvents);
            return false;
        }
        if (error is 105 or (int)SocketError.NoBufferSpaceAvailable)
        {
            SaturatingIncrement(ref _noBufferEvents);
            return false;
        }
        SaturatingIncrement(ref _physicalSendFailures);
        Consume(1, physicallySent: false);
        return true;
    }

    private bool IsCurrent(int slot, SharedUdpServerEndpoint? endpoint) =>
        endpoint is not null &&
        endpoint.IsEgressPathGenerationCurrent(_pathGenerations[slot]);

    private void Consume(int count, bool physicallySent)
    {
        for (int offset = 0; offset < count; offset++)
        {
            int slot = _head;
            int length = _lengths[slot];
            SharedUdpServerEndpoint? endpoint = _endpoints[slot];
            if (physicallySent)
            {
                endpoint?.RecordPhysicalSentFromEgress(length);
                AtomicMaximum(ref _maximumPhysicallySentDatagramBytes, length);
                SaturatingIncrement(ref _physicallySentDatagrams);
                SaturatingAdd(ref _physicallySentBytes, length);
            }
            if (_timingEnabled)
                RecordDelay(Stopwatch.GetTimestamp() - _enqueuedAt[slot]);
            CryptographicOperations.ZeroMemory(
                _payloads.AsSpan(slot * _maximumDatagramBytes, length));
            _addresses.AsSpan(slot * 16, 16).Clear();
            _lengths[slot] = 0;
            _endpoints[slot] = null;
            _pathGenerations[slot] = 0;
            _enqueuedAt[slot] = 0;
            _head = (slot + 1) % _lengths.Length;
            Interlocked.Add(ref _currentBytes, -length);
            Interlocked.Decrement(ref _count);
        }
    }

    private void DiscardAllAsPhysicalFailure()
    {
        while (Volatile.Read(ref _count) != 0)
        {
            SaturatingIncrement(ref _physicalSendFailures);
            Consume(1, physicallySent: false);
        }
    }

    private void RecordDelay(long elapsedTicks)
    {
        RecordDistribution(elapsedTicks, _delayBuckets,
            ref _delayCount, ref _delayTotal, ref _delayMaximum);
    }

    private static void RecordDistribution(
        long elapsedTicks,
        long[] buckets,
        ref long count,
        ref long total,
        ref long maximum)
    {
        long elapsed = Math.Max(0, elapsedTicks);
        int bucket = elapsed == 0 ? 0 : BitOperations.Log2((ulong)elapsed) + 1;
        if (bucket >= buckets.Length) bucket = buckets.Length - 1;
        Interlocked.Increment(ref buckets[bucket]);
        SaturatingIncrement(ref count);
        SaturatingAdd(ref total, elapsed);
        AtomicMaximum(ref maximum, elapsed);
    }

    private static NetworkTimingDistribution SnapshotDistribution(
        long[] buckets,
        ref long countSource,
        ref long total,
        ref long maximum)
    {
        ulong count = ReadUnsigned(ref countSource);
        return new(
            count,
            ReadUnsigned(ref total),
            Percentile(buckets, count, 50, ref maximum),
            Percentile(buckets, count, 95, ref maximum),
            Percentile(buckets, count, 99, ref maximum),
            ReadUnsigned(ref maximum));
    }

    private static ulong Percentile(
        long[] buckets,
        ulong count,
        int percentage,
        ref long maximum)
    {
        if (count == 0) return 0;
        ulong target = (ulong)(((UInt128)count * (uint)percentage + 99) / 100);
        ulong cumulative = 0;
        for (int index = 0; index < buckets.Length; index++)
        {
            cumulative = SaturatingAdd(cumulative, ReadUnsigned(ref buckets[index]));
            if (cumulative < target) continue;
            if (index == 0) return 0;
            ulong upper = index == 63 ? ulong.MaxValue : (1UL << index) - 1;
            return Math.Min(upper, ReadUnsigned(ref maximum));
        }
        return ReadUnsigned(ref maximum);
    }

    private int BatchPercentile(int percentage)
    {
        ulong total = 0;
        for (int index = 1; index < _batchHistogram.Length; index++)
            total = SaturatingAdd(total, ReadUnsigned(ref _batchHistogram[index]));
        if (total == 0) return 0;
        ulong target = (ulong)(((UInt128)total * (uint)percentage + 99) / 100);
        ulong cumulative = 0;
        for (int index = 1; index < _batchHistogram.Length; index++)
        {
            cumulative = SaturatingAdd(cumulative, ReadUnsigned(ref _batchHistogram[index]));
            if (cumulative >= target) return index;
        }
        return Volatile.Read(ref _maximumObservedBatch);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Interlocked.CompareExchange(ref _state, Stopping, Running);
        _wake.Set();
        if (!_worker.Join(checked(_drainTimeoutMilliseconds + 1_000)))
            throw new TimeoutException("The shared UDP egress worker did not retire within its bounded drain interval.");
        ExceptionDispatchInfo? failure = Volatile.Read(ref _workerFailure);
        CryptographicOperations.ZeroMemory(_payloads);
        CryptographicOperations.ZeroMemory(_addresses);
        Array.Clear(_lengths);
        Array.Clear(_endpoints);
        Array.Clear(_pathGenerations);
        Array.Clear(_enqueuedAt);
        _wake.Dispose();
        failure?.Throw();
    }

    private static SharedUdpBackgroundEgressState MapState(int state) => state switch
    {
        Running => SharedUdpBackgroundEgressState.Running,
        Stopping => SharedUdpBackgroundEgressState.Stopping,
        Disposed => SharedUdpBackgroundEgressState.Disposed,
        Faulted => SharedUdpBackgroundEgressState.Faulted,
        _ => SharedUdpBackgroundEgressState.Faulted,
    };

    private static SocketError MapSocketError(int error) => error switch
    {
        0 => SocketError.Success,
        11 => SocketError.WouldBlock,
        105 => SocketError.NoBufferSpaceAvailable,
        _ when Enum.IsDefined((SocketError)error) => (SocketError)error,
        _ => SocketError.SocketError,
    };

    private static ulong ReadUnsigned(ref long value) =>
        checked((ulong)Math.Max(0, Volatile.Read(ref value)));

    private static void SaturatingIncrement(ref long value)
    {
        while (true)
        {
            long current = Volatile.Read(ref value);
            if (current == long.MaxValue) return;
            if (Interlocked.CompareExchange(ref value, current + 1, current) == current) return;
        }
    }

    private static void SaturatingAdd(ref long value, long amount)
    {
        if (amount <= 0) return;
        while (true)
        {
            long current = Volatile.Read(ref value);
            long next = long.MaxValue - current < amount ? long.MaxValue : current + amount;
            if (Interlocked.CompareExchange(ref value, next, current) == current) return;
        }
    }

    private static ulong SaturatingAdd(ulong left, ulong right) =>
        ulong.MaxValue - left < right ? ulong.MaxValue : left + right;

    private static void AtomicMaximum(ref int target, int candidate)
    {
        int current = Volatile.Read(ref target);
        while (candidate > current)
        {
            int observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current) return;
            current = observed;
        }
    }

    private static void AtomicMaximum(ref long target, long candidate)
    {
        long current = Volatile.Read(ref target);
        while (candidate > current)
        {
            long observed = Interlocked.CompareExchange(ref target, candidate, current);
            if (observed == current) return;
            current = observed;
        }
    }
}
