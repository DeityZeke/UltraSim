using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace UltraSim.ECS
{
    internal sealed class SystemManager
    {
        private const int DefaultCapacity = 32;
        private static readonly int DefaultWorkerCount = Math.Max(1, Environment.ProcessorCount);

        private SystemBase[] _systems;
        private SystemDescriptor[] _descriptors;
        private SystemSchedule[] _schedules;
        private ComponentMask[] _readMasks;
        private ComponentMask[] _writeMasks;
        private SystemId[][] _requires;
        private bool[] _manual;

        private SystemId[][] _stageSystems;
        private SystemId[][][] _stageBatches;
        private uint[] _nextDueTick;
        private int[] _dueScheduleVersion;
        private int _scheduleVersion;

        private readonly Dictionary<Type, SystemId> _typeToId;
        private int _systemCount;
        private bool _sealed;
        private SystemExecutionMode _executionMode;
        private bool _phaseStaggeringEnabled;
        private int _maxNonCriticalStagger;
        private ManualWorkerPool? _workerPool;

        public int Count
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _systemCount;
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

        internal int StructuralCaptureWorkerCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                if (_executionMode != SystemExecutionMode.ParallelBatches)
                    return 0;

                return _workerPool?.WorkerCount ?? DefaultWorkerCount;
            }
        }

        internal void ParallelFor(int count, Action<int> action)
        {
            if (count <= 0)
                return;

            if (action == null)
                throw new ArgumentNullException(nameof(action));

            // Avoid nested dispatch deadlocks when called from a worker running a batch.
            if (WorkerThreadContext.TryGetWorkerIndex(out _))
            {
                for (int i = 0; i < count; i++)
                    action(i);
                return;
            }

            EnsureWorkerPool().ParallelFor(count, action);
        }

        internal SystemManager()
        {
            _systems = new SystemBase[DefaultCapacity];
            _descriptors = new SystemDescriptor[DefaultCapacity];
            _schedules = new SystemSchedule[DefaultCapacity];
            _readMasks = new ComponentMask[DefaultCapacity];
            _writeMasks = new ComponentMask[DefaultCapacity];
            _requires = new SystemId[DefaultCapacity][];
            _manual = new bool[DefaultCapacity];
            _nextDueTick = new uint[DefaultCapacity];
            _dueScheduleVersion = new int[DefaultCapacity];
            Array.Fill(_dueScheduleVersion, -1);

            int stageCount = (int)SystemStage.Count;
            _stageSystems = new SystemId[stageCount][];
            _stageBatches = new SystemId[stageCount][][];

            _typeToId = new Dictionary<Type, SystemId>(DefaultCapacity);
            _executionMode = SystemExecutionMode.Serial;
            _phaseStaggeringEnabled = false;
            _maxNonCriticalStagger = 10;
            _scheduleVersion = 1;
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
            if (system == null)
                throw new ArgumentNullException(nameof(system));

            if (_sealed)
                throw new InvalidOperationException("System manager is sealed.");

            var type = system.GetType();
            if (_typeToId.ContainsKey(type))
                return;

            EnsureCapacity(_systemCount + 1);

            var id = new SystemId(_systemCount);
            _systems[_systemCount] = system;
            system.Id = id;
            _typeToId[type] = id;

            var descriptor = system.Descriptor;
            _descriptors[_systemCount] = descriptor;
            _readMasks[_systemCount] = descriptor.ReadMask;
            _writeMasks[_systemCount] = descriptor.WriteMask;
            _manual[_systemCount] = descriptor.Manual;
            _requires[_systemCount] = ResolveRequires(descriptor.Requires);

            var schedule = SystemSchedule.FromDescriptor(descriptor);
            if (system.Settings != null)
                system.Settings.Apply(ref schedule);
            schedule.Enabled = system.Enabled;
            _schedules[_systemCount] = schedule;
            _dueScheduleVersion[_systemCount] = -1;
            _nextDueTick[_systemCount] = 0;

            _systemCount++;
        }

        public void RegisterAll(params SystemBase[] systems)
        {
            if (_sealed)
                throw new InvalidOperationException("System manager is sealed.");

            if (systems == null)
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
            for (int i = 0; i < _systemCount; i++)
                _systems[i].OnInitialize(world);
        }

        public void Shutdown(World world)
        {
            for (int i = 0; i < _systemCount; i++)
                _systems[i].OnShutdown(world);

            _workerPool?.Dispose();
            _workerPool = null;
        }

        /// <summary>
        /// Calls Serialize on every registered system in registration order.
        /// Systems that have no persistent state leave the default no-op override in place.
        /// </summary>
        public void SaveAllSystems(IDataWriter writer)
        {
            for (int i = 0; i < _systemCount; i++)
                _systems[i].Serialize(writer);
        }

        /// <summary>
        /// Calls Deserialize on every registered system in registration order.
        /// Must be called with a reader positioned at the same offset as after SaveAllSystems wrote.
        /// </summary>
        public void LoadAllSystems(IDataReader reader)
        {
            for (int i = 0; i < _systemCount; i++)
                _systems[i].Deserialize(reader);
        }

        public void ApplySettings(SystemBase system)
        {
            int idx = system.Id.Value;
            if ((uint)idx >= (uint)_systemCount)
                return;

            var schedule = SystemSchedule.FromDescriptor(_descriptors[idx]);
            if (system.Settings != null)
                system.Settings.Apply(ref schedule);
            schedule.Enabled = system.Enabled;
            _schedules[idx] = schedule;
            _dueScheduleVersion[idx] = -1;
        }

        public void RebuildSchedules()
        {
            AdvanceScheduleVersion();
            RefreshRequires();
            AssignOffsets();
            BuildStageSystems();
            BuildStageBatches();
        }

        internal void Tick(World world, uint worldTick, float deltaSeconds)
        {
            var ctx = new SystemContext(world, worldTick, deltaSeconds, world.Commands);

            for (int s = 0; s < _stageBatches.Length; s++)
            {
                var batches = _stageBatches[s];
                if (batches == null || batches.Length == 0)
                    continue;

                for (int b = 0; b < batches.Length; b++)
                {
                    var batch = batches[b];
                    if (_executionMode == SystemExecutionMode.ParallelBatches && batch.Length > 1)
                    {
                        RunBatchParallel(batch, world, worldTick, deltaSeconds);
                    }
                    else
                    {
                        for (int i = 0; i < batch.Length; i++)
                        {
                            int idx = batch[i].Value;
                            ref var schedule = ref _schedules[idx];
                            if (!schedule.Enabled)
                                continue;

                            if (!IsDue(idx, worldTick, in schedule))
                                continue;

                            _systems[idx].Update(in ctx);
                        }
                    }
                }
            }
        }

        public void RunManual(SystemId id, World world, uint worldTick, float deltaSeconds)
        {
            int idx = id.Value;
            if ((uint)idx >= (uint)_systemCount)
                return;

            if (!_manual[idx])
                return;

            var ctx = new SystemContext(world, worldTick, deltaSeconds, world.Commands);
            _systems[idx].Update(in ctx);
        }

        public void RunManual<T>(World world, uint worldTick, float deltaSeconds) where T : SystemBase
        {
            if (!_typeToId.TryGetValue(typeof(T), out var id))
                return;

            RunManual(id, world, worldTick, deltaSeconds);
        }

        public SystemBase? GetSystem<T>() where T : SystemBase
        {
            return _typeToId.TryGetValue(typeof(T), out var id) ? _systems[id.Value] : null;
        }

        internal ReadOnlySpan<SystemBase> GetSystemsSpan()
            => _systems.AsSpan(0, _systemCount);

        internal void CopyStartOffsets(Span<int> destination)
        {
            int count = Math.Min(destination.Length, _systemCount);
            for (int i = 0; i < count; i++)
                destination[i] = _schedules[i].StartOffset;
        }

        private SystemId[] ResolveRequires(Type[] requires)
        {
            if (requires.Length == 0)
                return Array.Empty<SystemId>();

            var ids = new SystemId[requires.Length];
            for (int i = 0; i < requires.Length; i++)
            {
                if (_typeToId.TryGetValue(requires[i], out var id))
                    ids[i] = id;
                else
                    ids[i] = SystemId.Invalid;
            }
            return ids;
        }

        private void BuildStageSystems()
        {
            int stageCount = (int)SystemStage.Count;
            var lists = new List<SystemId>[stageCount];

            for (int i = 0; i < _systemCount; i++)
            {
                if (_manual[i])
                    continue;

                int stage = (int)_schedules[i].Stage;
                var list = lists[stage];
                if (list == null)
                {
                    list = new List<SystemId>();
                    lists[stage] = list;
                }

                list.Add(new SystemId(i));
            }

            for (int s = 0; s < stageCount; s++)
            {
                var list = lists[s];
                if (list == null || list.Count == 0)
                {
                    _stageSystems[s] = Array.Empty<SystemId>();
                    continue;
                }

                _stageSystems[s] = TopologicalSortStage(list);
            }
        }

        private void RefreshRequires()
        {
            for (int i = 0; i < _systemCount; i++)
                _requires[i] = ResolveRequires(_descriptors[i].Requires);
        }

        private void AssignOffsets()
        {
            if (!_phaseStaggeringEnabled || _systemCount == 0)
                return;

            var countersByPeriod = new Dictionary<int, int>(16);

            for (int stage = 0; stage < (int)SystemStage.Count; stage++)
            {
                countersByPeriod.Clear();
                var stageValue = (SystemStage)stage;
                bool critical = IsCriticalStage(stageValue);
                int stageMax = critical ? 0 : _maxNonCriticalStagger;

                for (int i = 0; i < _systemCount; i++)
                {
                    if (_manual[i])
                        continue;

                    ref var schedule = ref _schedules[i];
                    if (schedule.Stage != stageValue)
                        continue;

                    if (schedule.PeriodTicks <= 1)
                        continue;

                    // Preserve explicitly configured offsets.
                    if (schedule.StartOffset != 0)
                        continue;

                    int phaseLimit = stageMax <= 0 ? 1 : Math.Min(schedule.PeriodTicks, stageMax);
                    if (phaseLimit <= 1)
                    {
                        schedule.StartOffset = 0;
                        continue;
                    }

                    countersByPeriod.TryGetValue(schedule.PeriodTicks, out var counter);
                    schedule.StartOffset = counter % phaseLimit;
                    countersByPeriod[schedule.PeriodTicks] = counter + 1;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsCriticalStage(SystemStage stage)
            => stage == SystemStage.Movement || stage == SystemStage.Combat;

        private SystemId[] TopologicalSortStage(List<SystemId> systems)
        {
            int count = systems.Count;
            if (count <= 1)
                return systems.ToArray();

            var indexMap = new int[_systemCount];
            for (int i = 0; i < indexMap.Length; i++)
                indexMap[i] = -1;

            for (int i = 0; i < count; i++)
                indexMap[systems[i].Value] = i;

            var indegree = new int[count];
            var dependents = new List<int>[count];

            for (int i = 0; i < count; i++)
            {
                var sysId = systems[i].Value;
                var reqs = _requires[sysId];
                for (int r = 0; r < reqs.Length; r++)
                {
                    int reqId = reqs[r].Value;
                    if ((uint)reqId >= (uint)_systemCount)
                        continue;

                    int depIndex = indexMap[reqId];
                    if (depIndex < 0)
                        continue;

                    indegree[i]++;
                    var list = dependents[depIndex];
                    if (list == null)
                    {
                        list = new List<int>();
                        dependents[depIndex] = list;
                    }
                    list.Add(i);
                }
            }

            var queue = new int[count];
            int head = 0;
            int tail = 0;

            for (int i = 0; i < count; i++)
            {
                if (indegree[i] == 0)
                    queue[tail++] = i;
            }

            var result = new SystemId[count];
            int write = 0;

            while (head < tail)
            {
                int idx = queue[head++];
                result[write++] = systems[idx];

                var list = dependents[idx];
                if (list == null)
                    continue;

                for (int i = 0; i < list.Count; i++)
                {
                    int dep = list[i];
                    if (--indegree[dep] == 0)
                        queue[tail++] = dep;
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
            for (int s = 0; s < stageCount; s++)
            {
                var systems = _stageSystems[s];
                if (systems.Length == 0)
                {
                    _stageBatches[s] = Array.Empty<SystemId[]>();
                    continue;
                }

                var batches = new List<List<SystemId>>();
                for (int i = 0; i < systems.Length; i++)
                {
                    var sys = systems[i];
                    bool placed = false;

                    for (int b = 0; b < batches.Count; b++)
                    {
                        if (!Conflicts(sys, batches[b]))
                        {
                            batches[b].Add(sys);
                            placed = true;
                            break;
                        }
                    }

                    if (!placed)
                        batches.Add(new List<SystemId> { sys });
                }

                var result = new SystemId[batches.Count][];
                for (int i = 0; i < batches.Count; i++)
                    result[i] = batches[i].ToArray();

                _stageBatches[s] = result;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool Conflicts(SystemId systemId, List<SystemId> batch)
        {
            int idx = systemId.Value;
            var write = _writeMasks[idx];
            var read = _readMasks[idx];

            for (int i = 0; i < batch.Count; i++)
            {
                int other = batch[i].Value;
                var otherWrite = _writeMasks[other];
                var otherRead = _readMasks[other];

                if (write.Intersects(otherWrite))
                    return true;

                if (write.Intersects(otherRead))
                    return true;

                if (otherWrite.Intersects(read))
                    return true;
            }

            return false;
        }

        private void RunBatchParallel(SystemId[] batch, World world, uint worldTick, float deltaSeconds)
        {
            var pool = EnsureWorkerPool();

            pool.ParallelFor(batch.Length, i =>
            {
                int idx = batch[i].Value;
                ref var schedule = ref _schedules[idx];
                if (!schedule.Enabled)
                    return;

                if (!IsDue(idx, worldTick, in schedule))
                    return;

                var localCtx = new SystemContext(world, worldTick, deltaSeconds, world.Commands);
                _systems[idx].Update(in localCtx);
            });
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ManualWorkerPool EnsureWorkerPool()
        {
            _workerPool ??= new ManualWorkerPool(DefaultWorkerCount);
            return _workerPool;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void EnsureCapacity(int needed)
        {
            if (needed <= _systems.Length)
                return;

            int newSize = Math.Max(_systems.Length * 2, needed);
            Array.Resize(ref _systems, newSize);
            Array.Resize(ref _descriptors, newSize);
            Array.Resize(ref _schedules, newSize);
            Array.Resize(ref _readMasks, newSize);
            Array.Resize(ref _writeMasks, newSize);
            Array.Resize(ref _requires, newSize);
            Array.Resize(ref _manual, newSize);
            int oldDueLen = _dueScheduleVersion.Length;
            Array.Resize(ref _nextDueTick, newSize);
            Array.Resize(ref _dueScheduleVersion, newSize);
            Array.Fill(_dueScheduleVersion, -1, oldDueLen, newSize - oldDueLen);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool IsDue(int idx, uint worldTick, in SystemSchedule schedule)
        {
            if (schedule.PeriodTicks <= 0)
                return false;

            if (_dueScheduleVersion[idx] != _scheduleVersion)
            {
                _nextDueTick[idx] = ComputeNextDueTick(worldTick, schedule.PeriodTicks, schedule.StartOffset);
                _dueScheduleVersion[idx] = _scheduleVersion;
            }

            uint nextDue = _nextDueTick[idx];
            if (worldTick < nextDue)
                return false;

            if (worldTick == nextDue)
            {
                AdvanceNextDue(idx, nextDue, schedule.PeriodTicks);
                return true;
            }

            nextDue = ComputeNextDueTick(worldTick, schedule.PeriodTicks, schedule.StartOffset);
            _nextDueTick[idx] = nextDue;
            if (worldTick == nextDue)
            {
                AdvanceNextDue(idx, nextDue, schedule.PeriodTicks);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AdvanceNextDue(int idx, uint currentDue, int periodTicks)
        {
            uint period = (uint)periodTicks;
            ulong next = (ulong)currentDue + period;
            _nextDueTick[idx] = next <= uint.MaxValue ? (uint)next : uint.MaxValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint ComputeNextDueTick(uint worldTick, int periodTicks, int startOffset)
        {
            if (periodTicks <= 0)
                return uint.MaxValue;

            long period = periodTicks;
            long offset = startOffset;
            if (offset < 0)
                offset = 0;

            long tick = worldTick;
            if (tick <= offset)
                return (uint)Math.Min(offset, uint.MaxValue);

            long delta = tick - offset;
            long remainder = delta % period;
            long due = remainder == 0 ? tick : tick + (period - remainder);
            return due <= uint.MaxValue ? (uint)due : uint.MaxValue;
        }

        private void AdvanceScheduleVersion()
        {
            if (_scheduleVersion == int.MaxValue)
            {
                _scheduleVersion = 1;
                Array.Fill(_dueScheduleVersion, -1, 0, _dueScheduleVersion.Length);
                return;
            }

            _scheduleVersion++;
        }
    }
}
