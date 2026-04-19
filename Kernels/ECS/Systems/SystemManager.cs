using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace UltraSim.ECS;

internal sealed class SystemManager
{
    private static readonly int DefaultWorkerCount = Math.Max(1, Environment.ProcessorCount);

    private readonly List<SystemBase> _systems = new();
    private readonly List<SystemDescriptor> _descriptors = new();
    private readonly List<SystemSchedule> _schedules = new();
    private readonly List<ComponentMask> _readMasks = new();
    private readonly List<ComponentMask> _writeMasks = new();
    private readonly List<SystemId[]> _requires = new();
    private readonly List<bool> _manual = new();
    private readonly Dictionary<Type, SystemId> _typeToId = new();

    private SystemId[][] _stageSystems;
    private SystemId[][][] _stageBatches;
    private uint[] _nextDueTick;
    private int[] _dueScheduleVersion;
    private int _scheduleVersion;
    private bool _sealed;
    private SystemExecutionMode _executionMode;
    private bool _phaseStaggeringEnabled;
    private int _maxNonCriticalStagger;
    private ManualWorkerPool? _workerPool;

    internal SystemManager()
    {
        int stageCount = (int)SystemStage.Count;
        _stageSystems = CreateEmptyStageSystems(stageCount);
        _stageBatches = CreateEmptyStageBatches(stageCount);
        _nextDueTick = Array.Empty<uint>();
        _dueScheduleVersion = Array.Empty<int>();
        _scheduleVersion = 1;
        _executionMode = SystemExecutionMode.Serial;
        _phaseStaggeringEnabled = false;
        _maxNonCriticalStagger = 10;
    }

    public int Count
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _systems.Count;
    }

    public bool IsSealed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _sealed;
    }

    public SystemExecutionMode ExecutionMode
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => _executionMode;
    }

    public void SetExecutionMode(SystemExecutionMode mode)
    {
        if (_sealed && mode != _executionMode)
            throw new InvalidOperationException("System manager is sealed.");

        _executionMode = mode;
        if (mode == SystemExecutionMode.ParallelBatches)
            EnsureWorkerPool();
    }

    public void ConfigurePhaseStaggering(bool enabled, int maxNonCriticalStagger = 10)
    {
        if (_sealed && (enabled != _phaseStaggeringEnabled || maxNonCriticalStagger != _maxNonCriticalStagger))
            throw new InvalidOperationException("System manager is sealed.");

        _phaseStaggeringEnabled = enabled;
        _maxNonCriticalStagger = Math.Max(0, maxNonCriticalStagger);
    }

    public void Register(SystemBase system)
    {
        ArgumentNullException.ThrowIfNull(system);

        if (_sealed)
            throw new InvalidOperationException("System manager is sealed.");

        Type type = system.GetType();
        if (_typeToId.ContainsKey(type))
            return;

        var id = new SystemId(_systems.Count);
        system.Id = id;
        _typeToId[type] = id;
        _systems.Add(system);

        SystemDescriptor descriptor = system.Descriptor;
        _descriptors.Add(descriptor);
        _readMasks.Add(descriptor.ReadMask);
        _writeMasks.Add(descriptor.WriteMask);
        _manual.Add(descriptor.Manual);
        _requires.Add(ResolveRequires(descriptor.Requires));

        SystemSchedule schedule = SystemSchedule.FromDescriptor(descriptor);
        system.Settings?.Apply(ref schedule);
        schedule.Enabled = system.Enabled;
        _schedules.Add(schedule);

        EnsureDueCapacity(_systems.Count);
    }

    public void RegisterAll(params SystemBase[] systems)
    {
        if (_sealed)
            throw new InvalidOperationException("System manager is sealed.");

        if (systems is null)
            return;

        for (int i = 0; i < systems.Length; i++)
            Register(systems[i]);

        RebuildSchedules();
    }

    public void Seal()
    {
        if (_sealed)
            return;

        RebuildSchedules();
        _sealed = true;
    }

    public void Initialize(World world)
    {
        for (int i = 0; i < _systems.Count; i++)
            _systems[i].OnInitialize(world);
    }

    public void Shutdown(World world)
    {
        for (int i = 0; i < _systems.Count; i++)
            _systems[i].OnShutdown(world);

        _workerPool?.Dispose();
        _workerPool = null;
    }

    public void ApplySettings(SystemBase system)
    {
        int index = system.Id.Value;
        if ((uint)index >= (uint)_systems.Count)
            return;

        SystemSchedule schedule = SystemSchedule.FromDescriptor(_descriptors[index]);
        system.Settings?.Apply(ref schedule);
        schedule.Enabled = system.Enabled;
        _schedules[index] = schedule;
        _dueScheduleVersion[index] = -1;
    }

    public void RebuildSchedules()
    {
        AdvanceScheduleVersion();
        RefreshRequires();
        AssignOffsets();
        BuildStageSystems();
        BuildStageBatches();
    }

    public void Tick(World world, uint worldTick, float deltaSeconds, IStructuralCommands commands)
    {
        var context = new SystemContext(world, worldTick, deltaSeconds, commands);

        for (int stage = 0; stage < _stageBatches.Length; stage++)
        {
            SystemId[][] batches = _stageBatches[stage];
            for (int batchIndex = 0; batchIndex < batches.Length; batchIndex++)
            {
                SystemId[] batch = batches[batchIndex];
                if (_executionMode == SystemExecutionMode.ParallelBatches && batch.Length > 1)
                {
                    RunBatchParallel(batch, world, worldTick, deltaSeconds, commands);
                    continue;
                }

                for (int i = 0; i < batch.Length; i++)
                {
                    int index = batch[i].Value;
                    SystemSchedule schedule = _schedules[index];
                    if (!schedule.Enabled || !IsDue(index, worldTick, in schedule))
                        continue;

                    _systems[index].Update(in context);
                }
            }
        }
    }

    public void RunManual(SystemId id, World world, uint worldTick, float deltaSeconds, IStructuralCommands commands)
    {
        int index = id.Value;
        if ((uint)index >= (uint)_systems.Count)
            return;

        if (!_manual[index])
            return;

        var context = new SystemContext(world, worldTick, deltaSeconds, commands);
        _systems[index].Update(in context);
    }

    public void RunManual<T>(World world, uint worldTick, float deltaSeconds, IStructuralCommands commands)
        where T : SystemBase
    {
        if (!_typeToId.TryGetValue(typeof(T), out SystemId id))
            return;

        RunManual(id, world, worldTick, deltaSeconds, commands);
    }

    public SystemBase? GetSystem<T>() where T : SystemBase
    {
        return _typeToId.TryGetValue(typeof(T), out SystemId id)
            ? _systems[id.Value]
            : null;
    }

    internal ReadOnlySpan<SystemBase> GetSystemsSpan() => CollectionsMarshal.AsSpan(_systems);

    internal void CopyStartOffsets(Span<int> destination)
    {
        int count = Math.Min(destination.Length, _schedules.Count);
        for (int i = 0; i < count; i++)
            destination[i] = _schedules[i].StartOffset;
    }

    internal SystemId[][] GetStageBatchesSnapshot(SystemStage stage)
    {
        int stageIndex = (int)stage;
        SystemId[][] source = _stageBatches[stageIndex];
        var copy = new SystemId[source.Length][];
        for (int i = 0; i < source.Length; i++)
            copy[i] = source[i].ToArray();

        return copy;
    }

    private SystemId[] ResolveRequires(Type[] requires)
    {
        if (requires.Length == 0)
            return Array.Empty<SystemId>();

        var ids = new SystemId[requires.Length];
        for (int i = 0; i < requires.Length; i++)
        {
            ids[i] = _typeToId.TryGetValue(requires[i], out SystemId id)
                ? id
                : SystemId.Invalid;
        }

        return ids;
    }

    private void RefreshRequires()
    {
        for (int i = 0; i < _systems.Count; i++)
            _requires[i] = ResolveRequires(_descriptors[i].Requires);
    }

    private void BuildStageSystems()
    {
        int stageCount = (int)SystemStage.Count;
        var lists = new List<SystemId>[stageCount];

        for (int i = 0; i < _systems.Count; i++)
        {
            if (_manual[i])
                continue;

            int stage = (int)_schedules[i].Stage;
            lists[stage] ??= new List<SystemId>();
            lists[stage].Add(new SystemId(i));
        }

        for (int stage = 0; stage < stageCount; stage++)
        {
            List<SystemId>? list = lists[stage];
            _stageSystems[stage] = list is null || list.Count == 0
                ? Array.Empty<SystemId>()
                : TopologicalSortStage(list);
        }
    }

    private void AssignOffsets()
    {
        if (!_phaseStaggeringEnabled || _systems.Count == 0)
            return;

        var countersByPeriod = new Dictionary<int, int>(16);
        for (int stage = 0; stage < (int)SystemStage.Count; stage++)
        {
            countersByPeriod.Clear();
            var stageValue = (SystemStage)stage;
            bool critical = stageValue is SystemStage.Movement or SystemStage.Combat;
            int stageMax = critical ? 0 : _maxNonCriticalStagger;

            for (int i = 0; i < _systems.Count; i++)
            {
                if (_manual[i])
                    continue;

                SystemSchedule schedule = _schedules[i];
                if (schedule.Stage != stageValue)
                    continue;

                if (schedule.PeriodTicks <= 1)
                    continue;

                if (schedule.StartOffset != 0)
                    continue;

                int phaseLimit = stageMax <= 0 ? 1 : Math.Min(schedule.PeriodTicks, stageMax);
                if (phaseLimit <= 1)
                {
                    schedule.StartOffset = 0;
                    continue;
                }

                countersByPeriod.TryGetValue(schedule.PeriodTicks, out int counter);
                schedule.StartOffset = counter % phaseLimit;
                countersByPeriod[schedule.PeriodTicks] = counter + 1;
                _schedules[i] = schedule;
            }
        }
    }

    private SystemId[] TopologicalSortStage(List<SystemId> systems)
    {
        int count = systems.Count;
        if (count <= 1)
            return systems.ToArray();

        var indexMap = new int[_systems.Count];
        Array.Fill(indexMap, -1);

        for (int i = 0; i < count; i++)
            indexMap[systems[i].Value] = i;

        var indegree = new int[count];
        var dependents = new List<int>[count];

        for (int i = 0; i < count; i++)
        {
            SystemId systemId = systems[i];
            SystemId[] requires = _requires[systemId.Value];
            for (int r = 0; r < requires.Length; r++)
            {
                int requiredId = requires[r].Value;
                if ((uint)requiredId >= (uint)_systems.Count)
                    continue;

                int dependencyIndex = indexMap[requiredId];
                if (dependencyIndex < 0)
                    continue;

                indegree[i]++;
                dependents[dependencyIndex] ??= new List<int>();
                dependents[dependencyIndex].Add(i);
            }
        }

        var queue = new Queue<int>(count);
        for (int i = 0; i < count; i++)
        {
            if (indegree[i] == 0)
                queue.Enqueue(i);
        }

        var result = new SystemId[count];
        int write = 0;
        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            result[write++] = systems[index];

            List<int>? list = dependents[index];
            if (list is null)
                continue;

            for (int i = 0; i < list.Count; i++)
            {
                int dependent = list[i];
                if (--indegree[dependent] == 0)
                    queue.Enqueue(dependent);
            }
        }

        if (write != count)
        {
            for (int i = 0; i < count; i++)
            {
                if (indegree[i] > 0)
                    result[write++] = systems[i];
            }
        }

        return result;
    }

    private void BuildStageBatches()
    {
        int stageCount = _stageSystems.Length;
        for (int stage = 0; stage < stageCount; stage++)
        {
            SystemId[] systems = _stageSystems[stage];
            if (systems.Length == 0)
            {
                _stageBatches[stage] = Array.Empty<SystemId[]>();
                continue;
            }

            var batches = new List<List<SystemId>>();
            for (int i = 0; i < systems.Length; i++)
            {
                SystemId systemId = systems[i];
                bool placed = false;
                for (int batch = 0; batch < batches.Count; batch++)
                {
                    if (!Conflicts(systemId, batches[batch]))
                    {
                        batches[batch].Add(systemId);
                        placed = true;
                        break;
                    }
                }

                if (!placed)
                    batches.Add(new List<SystemId> { systemId });
            }

            _stageBatches[stage] = batches.Select(static batch => batch.ToArray()).ToArray();
        }
    }

    private bool Conflicts(SystemId systemId, List<SystemId> batch)
    {
        int index = systemId.Value;
        ComponentMask write = _writeMasks[index];
        ComponentMask read = _readMasks[index];

        for (int i = 0; i < batch.Count; i++)
        {
            int other = batch[i].Value;
            ComponentMask otherWrite = _writeMasks[other];
            ComponentMask otherRead = _readMasks[other];

            if (write.Intersects(otherWrite))
                return true;
            if (write.Intersects(otherRead))
                return true;
            if (otherWrite.Intersects(read))
                return true;
        }

        return false;
    }

    private void RunBatchParallel(SystemId[] batch, World world, uint worldTick, float deltaSeconds, IStructuralCommands commands)
    {
        ManualWorkerPool pool = EnsureWorkerPool();
        pool.ParallelFor(batch.Length, i =>
        {
            int index = batch[i].Value;
            SystemSchedule schedule = _schedules[index];
            if (!schedule.Enabled || !IsDue(index, worldTick, in schedule))
                return;

            var context = new SystemContext(world, worldTick, deltaSeconds, commands);
            _systems[index].Update(in context);
        });
    }

    private ManualWorkerPool EnsureWorkerPool()
    {
        _workerPool ??= new ManualWorkerPool(DefaultWorkerCount);
        return _workerPool;
    }

    private void EnsureDueCapacity(int needed)
    {
        if (needed <= _nextDueTick.Length)
            return;

        int oldLength = _nextDueTick.Length;
        int newLength = Math.Max(oldLength == 0 ? 4 : oldLength * 2, needed);
        Array.Resize(ref _nextDueTick, newLength);
        Array.Resize(ref _dueScheduleVersion, newLength);
        Array.Fill(_dueScheduleVersion, -1, oldLength, newLength - oldLength);
    }

    private bool IsDue(int index, uint worldTick, in SystemSchedule schedule)
    {
        if (schedule.PeriodTicks <= 0)
            return false;

        if (_dueScheduleVersion[index] != _scheduleVersion)
        {
            _nextDueTick[index] = ComputeNextDueTick(worldTick, schedule.PeriodTicks, schedule.StartOffset);
            _dueScheduleVersion[index] = _scheduleVersion;
        }

        uint nextDue = _nextDueTick[index];
        if (worldTick < nextDue)
            return false;

        if (worldTick == nextDue)
        {
            AdvanceNextDue(index, nextDue, schedule.PeriodTicks);
            return true;
        }

        nextDue = ComputeNextDueTick(worldTick, schedule.PeriodTicks, schedule.StartOffset);
        _nextDueTick[index] = nextDue;
        if (worldTick == nextDue)
        {
            AdvanceNextDue(index, nextDue, schedule.PeriodTicks);
            return true;
        }

        return false;
    }

    private void AdvanceScheduleVersion()
    {
        if (_scheduleVersion == int.MaxValue)
        {
            _scheduleVersion = 1;
            Array.Fill(_dueScheduleVersion, -1);
            return;
        }

        _scheduleVersion++;
    }

    private static uint ComputeNextDueTick(uint worldTick, int periodTicks, int startOffset)
    {
        if (periodTicks <= 0)
            return uint.MaxValue;

        long period = periodTicks;
        long offset = Math.Max(0, startOffset);
        long tick = worldTick;
        if (tick <= offset)
            return (uint)Math.Min(offset, uint.MaxValue);

        long delta = tick - offset;
        long remainder = delta % period;
        long due = remainder == 0 ? tick : tick + (period - remainder);
        return due <= uint.MaxValue ? (uint)due : uint.MaxValue;
    }

    private void AdvanceNextDue(int index, uint currentDue, int periodTicks)
    {
        ulong next = (ulong)currentDue + (uint)periodTicks;
        _nextDueTick[index] = next <= uint.MaxValue ? (uint)next : uint.MaxValue;
    }

    private static SystemId[][] CreateEmptyStageSystems(int stageCount)
    {
        var result = new SystemId[stageCount][];
        for (int i = 0; i < stageCount; i++)
            result[i] = Array.Empty<SystemId>();
        return result;
    }

    private static SystemId[][][] CreateEmptyStageBatches(int stageCount)
    {
        var result = new SystemId[stageCount][][];
        for (int i = 0; i < stageCount; i++)
            result[i] = Array.Empty<SystemId[]>();
        return result;
    }
}
