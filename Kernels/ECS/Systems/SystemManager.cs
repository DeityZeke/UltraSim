using System.Runtime.InteropServices;

namespace UltraSim.ECS;

internal sealed class SystemManager
{
    private readonly List<SystemBase> _systems = new();
    private readonly List<SystemDescriptor> _descriptors = new();
    private readonly List<SystemSchedule> _schedules = new();
    private readonly Dictionary<Type, SystemId> _typeToId = new();
    private SystemId[][] _stageSystems = CreateEmptyStageBuckets();
    private bool _sealed;

    public int Count => _systems.Count;
    public bool IsSealed => _sealed;
    public SystemExecutionMode ExecutionMode => SystemExecutionMode.Serial;

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

        SystemSchedule schedule = SystemSchedule.FromDescriptor(descriptor);
        system.Settings?.Apply(ref schedule);
        schedule.Enabled = system.Enabled;
        _schedules.Add(schedule);
    }

    public void RegisterAll(params SystemBase[] systems)
    {
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
        RebuildSchedules();
    }

    public void RebuildSchedules()
    {
        _stageSystems = CreateEmptyStageBuckets();
        var stageLists = new List<SystemId>[(int)SystemStage.Count];

        for (int i = 0; i < _systems.Count; i++)
        {
            if (_descriptors[i].Manual)
                continue;

            int stage = (int)_schedules[i].Stage;
            stageLists[stage] ??= new List<SystemId>();
            stageLists[stage].Add(new SystemId(i));
        }

        for (int i = 0; i < stageLists.Length; i++)
            _stageSystems[i] = stageLists[i]?.ToArray() ?? Array.Empty<SystemId>();
    }

    public void Tick(World world, uint worldTick, float deltaSeconds, IStructuralCommands commands)
    {
        var context = new SystemContext(world, worldTick, deltaSeconds, commands);

        for (int stage = 0; stage < _stageSystems.Length; stage++)
        {
            SystemId[] stageSystems = _stageSystems[stage];
            for (int i = 0; i < stageSystems.Length; i++)
            {
                int index = stageSystems[i].Value;
                SystemSchedule schedule = _schedules[index];
                if (!schedule.Enabled || !IsDue(worldTick, schedule.PeriodTicks, schedule.StartOffset))
                    continue;

                _systems[index].Update(in context);
            }
        }
    }

    public void RunManual(SystemId id, World world, uint worldTick, float deltaSeconds, IStructuralCommands commands)
    {
        int index = id.Value;
        if ((uint)index >= (uint)_systems.Count)
            return;

        if (!_descriptors[index].Manual)
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

    private static bool IsDue(uint worldTick, int periodTicks, int startOffset)
    {
        if (periodTicks <= 0)
            return false;

        uint offset = (uint)Math.Max(0, startOffset);
        return (worldTick - offset) % (uint)periodTicks == 0;
    }

    private static SystemId[][] CreateEmptyStageBuckets()
    {
        var buckets = new SystemId[(int)SystemStage.Count][];
        for (int i = 0; i < buckets.Length; i++)
            buckets[i] = Array.Empty<SystemId>();

        return buckets;
    }
}
