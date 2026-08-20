using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.Network
{
    public sealed class PacketRegistry<TSession>
    {
        private delegate void PacketDispatch(TSession session, ReadOnlySpan<byte> payload);
        private delegate void DispatchInvoker(
            PacketDispatch dispatch,
            TSession session,
            ReadOnlySpan<byte> payload,
            int payloadLength);

        private PacketDispatch?[] _dispatch = new PacketDispatch?[64];
        private ChannelType[] _channels = new ChannelType[64];
        private readonly Dictionary<Type, ushort> _packetIdsByType = new Dictionary<Type, ushort>();
        private readonly object _registrationGate = new object();
        private readonly ProtocolStats? _stats;
        private readonly DispatchInvoker _dispatchInvoker;
        private volatile bool _sealed;

        public PacketRegistry(ProtocolStats? stats = null)
        {
            _stats = stats;
            if (stats == null)
            {
                _dispatchInvoker = DispatchNoStats;
                return;
            }

            bool trackDecodeFailures = stats.TrackDecodeFailures;
            if (!stats.CollectDecodeTimings)
            {
                _dispatchInvoker = trackDecodeFailures
                    ? DispatchCountersOnlyTracked
                    : DispatchCountersOnlyNoFailureTracking;
                return;
            }

            int sampleRate = stats.DecodeTimingSampleRate;
            if (sampleRate == 1)
            {
                _dispatchInvoker = trackDecodeFailures
                    ? DispatchTimedEveryPacketTracked
                    : DispatchTimedEveryPacketNoFailureTracking;
            }
            else
            {
                _dispatchInvoker = trackDecodeFailures
                    ? DispatchTimedSampledTracked
                    : DispatchTimedSampledNoFailureTracking;
            }
        }

        public bool IsSealed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _sealed;
        }

        public void Seal()
        {
            lock (_registrationGate)
                _sealed = true;
        }

        public void Register<TPacket>(PacketHandler<TSession, TPacket> handler)
            where TPacket : struct, IPacket<TPacket>
        {
            lock (_registrationGate)
            {
                if (_sealed)
                    ThrowSealed();

                ushort packetId = TPacket.PacketId;
                Type packetType = typeof(TPacket);

                EnsurePacketCapacity(packetId);

                if (_dispatch[packetId] != null)
                    ThrowDuplicatePacketId(packetId, packetType);

                if (_packetIdsByType.ContainsKey(packetType))
                    ThrowDuplicatePacketType(packetType);

                // Publish channel first, then dispatch. Readers only consider channels
                // when dispatch is non-null, so this avoids observing a default channel.
                _channels[packetId] = TPacket.Channel;
                _dispatch[packetId] = (session, payload) =>
                {
                    var reader = new BitReader(payload);
                    var packet = TPacket.Deserialize(ref reader);
                    if (reader.Remaining != 0)
                        throw new InvalidDataException(
                            $"Packet 0x{packetId:X4} left {reader.Remaining} unread payload bytes after deserialization.");
                    handler(session, in packet);
                };
                _packetIdsByType.Add(packetType, packetId);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool TryDispatch(TSession session, ushort packetId, ReadOnlySpan<byte> payload)
        {
            int payloadLength = payload.Length;
            PacketDispatch?[] dispatchTable = Volatile.Read(ref _dispatch);
            if ((uint)packetId >= (uint)dispatchTable.Length)
            {
                _stats?.RecordUnknown(payloadLength);
                return false;
            }

            var dispatch = dispatchTable[packetId];
            if (dispatch == null)
            {
                _stats?.RecordUnknown(payloadLength);
                return false;
            }

            _dispatchInvoker(dispatch, session, payload, payloadLength);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ShouldSampleDecodeTiming()
        {
            return _stats!.ShouldMeasureDecodeTiming();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DispatchNoStats(
            PacketDispatch dispatch,
            TSession session,
            ReadOnlySpan<byte> payload,
            int payloadLength)
        {
            dispatch(session, payload);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DispatchCountersOnlyTracked(
            PacketDispatch dispatch,
            TSession session,
            ReadOnlySpan<byte> payload,
            int payloadLength)
        {
            var stats = _stats!;
            try
            {
                dispatch(session, payload);
            }
            catch
            {
                stats.RecordDecodeFailureNoTiming(payloadLength);
                throw;
            }

            stats.RecordDecodedNoTiming(payloadLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DispatchCountersOnlyNoFailureTracking(
            PacketDispatch dispatch,
            TSession session,
            ReadOnlySpan<byte> payload,
            int payloadLength)
        {
            dispatch(session, payload);
            _stats!.RecordDecodedNoTiming(payloadLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DispatchTimedEveryPacketTracked(
            PacketDispatch dispatch,
            TSession session,
            ReadOnlySpan<byte> payload,
            int payloadLength)
        {
            var stats = _stats!;
            long startTimestamp = ProtocolStats.GetTimestamp();
            try
            {
                dispatch(session, payload);
            }
            catch
            {
                stats.RecordDecodeFailureWithTiming(payloadLength, ProtocolStats.GetTimestamp() - startTimestamp);
                throw;
            }

            stats.RecordDecodedWithTiming(payloadLength, ProtocolStats.GetTimestamp() - startTimestamp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DispatchTimedEveryPacketNoFailureTracking(
            PacketDispatch dispatch,
            TSession session,
            ReadOnlySpan<byte> payload,
            int payloadLength)
        {
            long startTimestamp = ProtocolStats.GetTimestamp();
            dispatch(session, payload);
            _stats!.RecordDecodedWithTiming(payloadLength, ProtocolStats.GetTimestamp() - startTimestamp);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DispatchTimedSampledNoFailureTracking(
            PacketDispatch dispatch,
            TSession session,
            ReadOnlySpan<byte> payload,
            int payloadLength)
        {
            var stats = _stats!;
            if (ShouldSampleDecodeTiming())
            {
                long sampleStartTimestamp = ProtocolStats.GetTimestamp();
                dispatch(session, payload);
                stats.RecordDecodedWithTiming(payloadLength, ProtocolStats.GetTimestamp() - sampleStartTimestamp);
                return;
            }

            dispatch(session, payload);
            stats.RecordDecodedNoTiming(payloadLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DispatchTimedSampledTracked(
            PacketDispatch dispatch,
            TSession session,
            ReadOnlySpan<byte> payload,
            int payloadLength)
        {
            var stats = _stats!;
            bool measureDecodeTiming = ShouldSampleDecodeTiming();
            long sampleStartTimestamp = measureDecodeTiming ? ProtocolStats.GetTimestamp() : 0;
            try
            {
                dispatch(session, payload);
            }
            catch
            {
                if (measureDecodeTiming)
                {
                    stats.RecordDecodeFailureWithTiming(
                        payloadLength,
                        ProtocolStats.GetTimestamp() - sampleStartTimestamp);
                }
                else
                {
                    stats.RecordDecodeFailureNoTiming(payloadLength);
                }

                throw;
            }

            if (measureDecodeTiming)
            {
                stats.RecordDecodedWithTiming(
                    payloadLength,
                    ProtocolStats.GetTimestamp() - sampleStartTimestamp);
            }
            else
            {
                stats.RecordDecodedNoTiming(payloadLength);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool HasHandler(ushort packetId)
        {
            PacketDispatch?[] dispatchTable = Volatile.Read(ref _dispatch);
            return (uint)packetId < (uint)dispatchTable.Length && dispatchTable[packetId] != null;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ushort GetPacketId<TPacket>() where TPacket : struct, IPacket<TPacket> => TPacket.PacketId;

        public bool TryGetPacketId(Type packetType, out ushort packetId)
        {
            ArgumentNullException.ThrowIfNull(packetType);

            return _packetIdsByType.TryGetValue(packetType, out packetId);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ChannelType GetChannel(ushort packetId)
        {
            PacketDispatch?[] dispatchTable = Volatile.Read(ref _dispatch);
            if ((uint)packetId >= (uint)dispatchTable.Length || dispatchTable[packetId] == null)
                ThrowMissingPacket(packetId);

            return Volatile.Read(ref _channels)[packetId];
        }

        private void EnsurePacketCapacity(int packetId)
        {
            if ((uint)packetId < (uint)_dispatch.Length)
                return;

            int newSize = _dispatch.Length;
            while (packetId >= newSize)
            {
                if (newSize > int.MaxValue / 2)
                {
                    newSize = packetId + 1;
                    break;
                }

                newSize <<= 1;
            }

            var nextChannels = new ChannelType[newSize];
            Array.Copy(_channels, nextChannels, _channels.Length);

            var nextDispatch = new PacketDispatch?[newSize];
            Array.Copy(_dispatch, nextDispatch, _dispatch.Length);

            // Publish channels before dispatch so readers never observe
            // dispatch-length > channels-length.
            Volatile.Write(ref _channels, nextChannels);
            Volatile.Write(ref _dispatch, nextDispatch);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowSealed() => throw new InvalidOperationException("Packet registry is sealed.");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowDuplicatePacketId(ushort packetId, Type packetType) =>
            throw new InvalidOperationException($"Packet id 0x{packetId:X4} is already registered. Type: {packetType.FullName}");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowDuplicatePacketType(Type packetType) =>
            throw new InvalidOperationException($"Packet type already registered: {packetType.FullName}");

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowMissingPacket(ushort packetId) =>
            throw new KeyNotFoundException($"No packet registration found for id 0x{packetId:X4}.");

    }
}
