using System.Threading;

namespace UltraSim.ECS;

internal sealed class ManualWorkerPool : IDisposable
{
    private readonly Thread[] _threads;
    private readonly AutoResetEvent[] _workEvents;
    private readonly AutoResetEvent _completeEvent;
    private readonly object _dispatchLock = new();

    private Action<int>? _workAction;
    private int _workCount;
    private int _workIndex;
    private int _completedWorkers;
    private volatile bool _shutdown;
    private Exception? _capturedException;

    public int WorkerCount => _threads.Length;

    public ManualWorkerPool(int workerCount)
    {
        if (workerCount < 1)
            workerCount = 1;

        _threads = new Thread[workerCount];
        _workEvents = new AutoResetEvent[workerCount];
        _completeEvent = new AutoResetEvent(false);

        for (int i = 0; i < workerCount; i++)
        {
            int threadIndex = i;
            _workEvents[i] = new AutoResetEvent(false);
            _threads[i] = new Thread(() => WorkerLoop(threadIndex))
            {
                IsBackground = true,
                Name = $"ECS_Worker_{threadIndex}",
            };
            _threads[i].Start();
        }
    }

    public void ParallelFor(int count, Action<int> action)
    {
        if (count <= 0)
            return;

        if (count == 1)
        {
            action(0);
            return;
        }

        lock (_dispatchLock)
        {
            _capturedException = null;
            _workAction = action;
            _workCount = count;
            _workIndex = 0;
            _completedWorkers = 0;

            for (int i = 0; i < _workEvents.Length; i++)
                _workEvents[i].Set();

            _completeEvent.WaitOne();

            if (_capturedException is not null)
                throw new AggregateException("ManualWorkerPool worker failed.", _capturedException);
        }
    }

    private void WorkerLoop(int threadIndex)
    {
        WorkerThreadContext.SetWorkerIndex(threadIndex);
        while (true)
        {
            _workEvents[threadIndex].WaitOne();
            if (_shutdown)
                break;

            try
            {
                while (true)
                {
                    int index = Interlocked.Increment(ref _workIndex) - 1;
                    if (index >= _workCount)
                        break;

                    _workAction!(index);
                }
            }
            catch (Exception ex)
            {
                Interlocked.CompareExchange(ref _capturedException, ex, null);
            }
            finally
            {
                if (Interlocked.Increment(ref _completedWorkers) == _threads.Length)
                    _completeEvent.Set();
            }
        }

        WorkerThreadContext.ClearWorkerIndex();
    }

    public void Dispose()
    {
        if (_shutdown)
            return;

        _shutdown = true;
        for (int i = 0; i < _workEvents.Length; i++)
            _workEvents[i].Set();

        for (int i = 0; i < _threads.Length; i++)
        {
            try
            {
                _threads[i].Join();
            }
            catch
            {
            }
        }

        for (int i = 0; i < _workEvents.Length; i++)
            _workEvents[i].Dispose();

        _completeEvent.Dispose();
    }
}
