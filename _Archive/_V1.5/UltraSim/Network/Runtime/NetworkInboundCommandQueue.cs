using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;

namespace UltraSim.Network
{
    public sealed class NetworkInboundCommandQueue<TCommand>
    {
        private static readonly bool s_clearEntries =
            RuntimeHelpers.IsReferenceOrContainsReferences<QueuedInboundCommand<TCommand>>();

        private readonly bool _threadSafe;
        private readonly object _sync = new object();
        private QueuedInboundCommand<TCommand>[] _writeBuffer;
        private int _writeCount;
        private QueuedInboundCommand<TCommand>[] _drainBuffer;
        private int _drainCount;
        private int _drainIndex;
        private int _drainInProgress;

        public NetworkInboundCommandQueue(int initialCapacity = 256, bool threadSafe = true)
        {
            if (initialCapacity <= 0)
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));

            _threadSafe = threadSafe;
            _writeBuffer = new QueuedInboundCommand<TCommand>[initialCapacity];
            _drainBuffer = new QueuedInboundCommand<TCommand>[initialCapacity];
            _writeCount = 0;
            _drainCount = 0;
            _drainIndex = 0;
        }

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (!_threadSafe)
                    return _writeCount + (_drainCount - _drainIndex);

                lock (_sync)
                    return _writeCount + (_drainCount - _drainIndex);
            }
        }

        public void Enqueue(long sessionId, in TCommand command)
        {
            if (!_threadSafe)
            {
                EnqueueNoLock(sessionId, in command);
                return;
            }

            lock (_sync)
            {
                EnqueueNoLock(sessionId, in command);
            }
        }

        public int Drain(InboundCommandHandler<TCommand> handler)
        {
            ArgumentNullException.ThrowIfNull(handler);

            if (!_threadSafe)
                return DrainNoLock(handler);

            if (Interlocked.Exchange(ref _drainInProgress, 1) != 0)
            {
                Debug.Assert(false, "NetworkInboundCommandQueue.Drain() called concurrently; commands will be missed this tick.");
                return 0;
            }

            try
            {
                int count;
                lock (_sync)
                {
                    if (_drainIndex >= _drainCount && _writeCount == 0)
                        return 0;

                    if (_drainIndex >= _drainCount)
                        SwapBuffers_NoLock();

                    count = _drainCount - _drainIndex;
                }

                return DrainCore(handler, count);
            }
            finally
            {
                Volatile.Write(ref _drainInProgress, 0);
            }
        }

        public void Clear()
        {
            if (!_threadSafe)
            {
                ClearNoLock();
                return;
            }

            while (Volatile.Read(ref _drainInProgress) != 0)
                Thread.Yield();

            lock (_sync)
            {
                ClearNoLock();
            }
        }

        private void EnqueueNoLock(long sessionId, in TCommand command)
        {
            if (_writeCount == _writeBuffer.Length)
                GrowWriteBuffer_NoLock();

            _writeBuffer[_writeCount++] = new QueuedInboundCommand<TCommand>(sessionId, in command);
        }

        private int DrainNoLock(InboundCommandHandler<TCommand> handler)
        {
            if (_drainIndex >= _drainCount && _writeCount == 0)
                return 0;

            if (_drainIndex >= _drainCount)
                SwapBuffers_NoLock();

            int count = _drainCount - _drainIndex;
            return DrainCore(handler, count);
        }

        private int DrainCore(InboundCommandHandler<TCommand> handler, int count)
        {
            int drained = 0;
            int clearStart = _drainIndex;
            while (drained < count)
            {
                int index = _drainIndex;
                handler(in _drainBuffer[index]);
                _drainIndex++;
                drained++;
            }

            if (s_clearEntries && drained > 0)
                Array.Clear(_drainBuffer, clearStart, drained);

            if (_drainIndex >= _drainCount)
            {
                _drainCount = 0;
                _drainIndex = 0;
            }

            return drained;
        }

        private void ClearNoLock()
        {
            if (s_clearEntries && _writeCount > 0)
            {
                Array.Clear(_writeBuffer, 0, _writeCount);
            }
            _writeCount = 0;

            if (s_clearEntries && _drainCount > 0)
            {
                Array.Clear(_drainBuffer, 0, _drainCount);
            }

            _drainCount = 0;
            _drainIndex = 0;
        }

        private void GrowWriteBuffer_NoLock()
        {
            int newSize = _writeBuffer.Length * 2;
            if (newSize < 4)
                newSize = 4;

            var newBuffer = new QueuedInboundCommand<TCommand>[newSize];
            Array.Copy(_writeBuffer, newBuffer, _writeCount);
            _writeBuffer = newBuffer;
        }

        private void SwapBuffers_NoLock()
        {
            var tempBuffer = _drainBuffer;
            _drainBuffer = _writeBuffer;
            _drainCount = _writeCount;
            _drainIndex = 0;
            _writeBuffer = tempBuffer;
            _writeCount = 0;
        }
    }
}
