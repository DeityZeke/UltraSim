using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using UltraSim.ECS;

namespace UltraSim.ECS.SimulationSlice0;

internal enum RunMode
{
    Both,
    Serial,
    Parallel,
}

internal sealed record SimulationOptions(
    int Population,
    int WarmupTicks,
    int MeasuredTicks,
    int Workers,
    int ValidationEvery,
    RunMode Mode,
    string OutputPath,
    bool Quick,
    bool ProbeRandomWrites,
    bool DeferredValidation,
    bool AllActive)
{
    internal static SimulationOptions Parse(string[] args)
    {
        bool quick = Has(args, "--quick");
        int population = GetInt(args, "--population", quick ? 10_000 : 100_000);
        int warmup = GetInt(args, "--warmup", quick ? 8 : 256);
        int ticks = GetInt(args, "--ticks", quick ? 80 : 10_000);
        int workers = GetInt(args, "--workers", 7);
        int validationEvery = GetInt(args, "--validate-every", quick ? 1 : 250);
        string modeText = GetValue(args, "--mode") ?? "both";
        RunMode mode = modeText.ToLowerInvariant() switch
        {
            "serial" => RunMode.Serial,
            "parallel" or "c8" => RunMode.Parallel,
            "both" => RunMode.Both,
            _ => throw new ArgumentException("--mode must be serial, parallel, or both.")
        };

        string defaultOutput = Path.Combine(
            FindRepositoryRoot(), "_Benchmarks", "results", "simulation-slice-0",
            quick ? "quick.json" : "simulation-slice-0.json");
        return new SimulationOptions(
            Population: Math.Max(256, population),
            WarmupTicks: Math.Max(0, warmup),
            MeasuredTicks: Math.Max(1, ticks),
            Workers: Math.Max(0, workers),
            ValidationEvery: Math.Max(1, validationEvery),
            Mode: mode,
            OutputPath: Path.GetFullPath(GetValue(args, "--output") ?? defaultOutput),
            Quick: quick,
            ProbeRandomWrites: Has(args, "--probe-random-writes"),
            DeferredValidation: Has(args, "--deferred-validation"),
            AllActive: Has(args, "--all-active"));
    }

    private static bool Has(string[] args, string name) =>
        args.Any(value => string.Equals(value, name, StringComparison.OrdinalIgnoreCase));

    private static string? GetValue(string[] args, string name)
    {
        string prefix = name + "=";
        string? inline = args.FirstOrDefault(value => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        if (inline is not null)
            return inline[prefix.Length..];

        for (int index = 0; index + 1 < args.Length; index++)
        {
            if (string.Equals(args[index], name, StringComparison.OrdinalIgnoreCase))
                return args[index + 1];
        }
        return null;
    }

    private static int GetInt(string[] args, string name, int fallback) =>
        int.TryParse(GetValue(args, name), out int value) ? value : fallback;

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ECS_NEXT_DESIGN.md")) &&
                Directory.Exists(Path.Combine(current.FullName, "Kernels", "ECS")))
                return current.FullName;
            current = current.Parent;
        }
        return Environment.CurrentDirectory;
    }
}

internal sealed class SimulationProfile
{
    internal SimulationProfile(SimulationOptions options)
    {
        Population = options.Population;
        EffectSlots = Population >= 50_000 ? 2_048 : Population >= 5_000 ? 256 : 64;
        EffectLifetime = EffectSlots >= 2_048 ? 64 : EffectSlots >= 256 ? 32 : 16;
        EffectsSpawnedPerTick = EffectSlots / EffectLifetime;
        ItemCount = Population * 8 / 100;
        ActorCount = Population - ItemCount - EffectSlots;
        if (ActorCount < 128)
            throw new ArgumentException("Population is too small for the Slice 0 resident mix.", nameof(options));

        Tier0Count = ActorCount / 90;
        Tier1Count = ActorCount / 10;
        Tier2Count = ActorCount * 2 / 9;
        Tier3Count = ActorCount - Tier0Count - Tier1Count - Tier2Count;
        TierCounts = [Tier0Count, Tier1Count, Tier2Count, Tier3Count];
        TierPeriods = options.AllActive ? [1, 1, 1, 1] : [1, 2, 8, 60];
        PartitionSafeRandomWrites = options.ProbeRandomWrites;
        AllActive = options.AllActive;
        ActorTier = new int[ActorCount];
        int cursor = 0;
        for (int tier = 0; tier < TierCounts.Length; tier++)
        {
            for (int index = 0; index < TierCounts[tier]; index++)
                ActorTier[cursor++] = tier;
        }
    }

    internal int Population { get; }
    internal int ActorCount { get; }
    internal int ItemCount { get; }
    internal int EffectSlots { get; }
    internal int EffectLifetime { get; }
    internal int EffectsSpawnedPerTick { get; }
    internal int Tier0Count { get; }
    internal int Tier1Count { get; }
    internal int Tier2Count { get; }
    internal int Tier3Count { get; }
    internal int[] TierCounts { get; }
    internal int[] TierPeriods { get; }
    internal int[] ActorTier { get; }
    internal int TotalCapacity => checked(ActorCount + ItemCount + EffectSlots);
    internal int TargetOperations => Math.Min(512, ActorCount);
    internal int DamageOperations => Math.Min(256, ActorCount);
    // One tick can retire and respawn every bounded damage source. Each actor
    // transition publishes five removals plus one addition. Effects contribute
    // one destroy and one create per recycled slot. Sixty ticks is the longest
    // normal observer cadence; four extra ticks provide an explicit scheduling
    // and final-drain margin without making retention population-dependent.
    internal int MaximumLifecycleRecordsPerTick => checked(
        DamageOperations * 12 + EffectsSpawnedPerTick * 2);
    internal int LifecycleRetentionTicks => 64;
    internal int LifecycleRecordCapacity => Math.Max(
        4_096,
        checked(MaximumLifecycleRecordsPerTick * LifecycleRetentionTicks));
    internal int RespawnDelay => 20;
    internal bool PartitionSafeRandomWrites { get; }
    internal bool AllActive { get; }

    internal string TierDistribution =>
        $"T0={Tier0Count:N0}, T1={Tier1Count:N0}, T2={Tier2Count:N0}, T3={Tier3Count:N0}";

    internal int TargetSource(int operation, ulong tick) => PartitionSafeRandomWrites
        ? operation
        : unchecked(((int)tick * 17 + operation) % ActorCount);
}

internal static class SimulationMath
{
    internal static Position InitialActorPosition(int logicalId) => new()
    {
        X = unchecked((logicalId * 97) % 4096 - 2048),
        Y = unchecked((logicalId * 193) % 4096 - 2048),
    };

    internal static Activity InitialActivity(int logicalId) => new()
    {
        Value = unchecked(logicalId * 1_103_515_245 + 12_345),
    };

    internal static Health InitialHealth(int logicalId) => new() { Value = 32 + (logicalId & 15) };

    internal static Velocity Steering(int logicalId, in Needs needs, in Activity activity, ulong tick) => new()
    {
        X = unchecked(((logicalId + (int)tick + needs.Hunger + activity.Value) & 7) - 3),
        Y = unchecked(((logicalId * 3 + (int)tick + needs.Fatigue + (activity.Value >> 3)) & 7) - 3),
    };

    internal static SpatialCell Cell(in Position position) => new()
    {
        X = FloorDiv(position.X, 32),
        Y = FloorDiv(position.Y, 32),
    };

    internal static Position EffectPosition(int slot, int generation, ulong tick) => new()
    {
        X = unchecked((slot * 131 + generation * 17 + (int)tick * 3) % 8192 - 4096),
        Y = unchecked((slot * 47 + generation * 29 + (int)tick * 5) % 8192 - 4096),
    };

    internal static Velocity EffectVelocity(int slot, int generation) => new()
    {
        X = unchecked(((slot + generation * 3) & 3) - 1),
        Y = unchecked(((slot * 3 + generation * 5) & 3) - 1),
    };

    internal static int ItemValue(int logicalId, in Position position, ulong tick) =>
        unchecked(logicalId * 17 + position.X * 3 + position.Y * 5 + (int)tick);

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }
}

// Intentionally independent from SimulationMath. The semantic oracle must not
// inherit an ECS-job arithmetic defect merely because both sides call one helper.
internal static class OracleMath
{
    internal static Position InitialActorPosition(int logicalId) => new()
    {
        X = unchecked((logicalId * 97) % 4096 - 2048),
        Y = unchecked((logicalId * 193) % 4096 - 2048),
    };

    internal static Activity InitialActivity(int logicalId) => new()
    {
        Value = unchecked(logicalId * 1_103_515_245 + 12_345),
    };

    internal static Health InitialHealth(int logicalId) => new() { Value = 32 + (logicalId & 15) };

    internal static Velocity Steering(int logicalId, in Needs needs, in Activity activity, ulong tick) => new()
    {
        X = unchecked(((logicalId + (int)tick + needs.Hunger + activity.Value) & 7) - 3),
        Y = unchecked(((logicalId * 3 + (int)tick + needs.Fatigue + (activity.Value >> 3)) & 7) - 3),
    };

    internal static SpatialCell Cell(in Position position) => new()
    {
        X = FloorDiv(position.X, 32),
        Y = FloorDiv(position.Y, 32),
    };

    internal static Position EffectPosition(int slot, int generation, ulong tick) => new()
    {
        X = unchecked((slot * 131 + generation * 17 + (int)tick * 3) % 8192 - 4096),
        Y = unchecked((slot * 47 + generation * 29 + (int)tick * 5) % 8192 - 4096),
    };

    internal static Velocity EffectVelocity(int slot, int generation) => new()
    {
        X = unchecked(((slot + generation * 3) & 3) - 1),
        Y = unchecked(((slot * 3 + generation * 5) & 3) - 1),
    };

    internal static int ItemValue(int logicalId, in Position position, ulong tick) =>
        unchecked(logicalId * 17 + position.X * 3 + position.Y * 5 + (int)tick);

    private static int FloorDiv(int value, int divisor)
    {
        int quotient = value / divisor;
        return value < 0 && value % divisor != 0 ? quotient - 1 : quotient;
    }
}

internal struct ActorSnapshot
{
    internal bool Alive;
    internal int Tier;
    internal Position Position;
    internal Velocity Velocity;
    internal Needs Needs;
    internal Activity Activity;
    internal Health Health;
    internal Cooldown Cooldown;
    internal Target Target;
    internal SpatialCell Cell;
}

internal struct EffectSnapshot
{
    internal bool Active;
    internal int Generation;
    internal Position Position;
    internal Velocity Velocity;
    internal int Lifetime;
}

internal struct ItemSnapshot
{
    internal Position Position;
    internal int Value;
}

internal sealed class SimulationSnapshot
{
    internal SimulationSnapshot(SimulationProfile profile)
    {
        Actors = new ActorSnapshot[profile.ActorCount];
        Effects = new EffectSnapshot[profile.EffectSlots];
        Items = new ItemSnapshot[profile.ItemCount];
    }

    internal ActorSnapshot[] Actors { get; }
    internal EffectSnapshot[] Effects { get; }
    internal ItemSnapshot[] Items { get; }
    internal ulong Hash { get; private set; }
    internal int ExpectedAliveEntityCount
    {
        get
        {
            int activeEffects = 0;
            for (int index = 0; index < Effects.Length; index++)
                if (Effects[index].Active)
                    activeEffects++;
            return checked(Actors.Length + Items.Length + activeEffects);
        }
    }

    internal void ComputeHash()
    {
        ulong hash = 1469598103934665603UL;
        foreach (ActorSnapshot actor in Actors)
        {
            hash = Add(hash, actor.Alive ? 1 : 0);
            hash = Add(hash, actor.Tier);
            hash = Add(hash, actor.Position.X); hash = Add(hash, actor.Position.Y);
            hash = Add(hash, actor.Velocity.X); hash = Add(hash, actor.Velocity.Y);
            hash = Add(hash, actor.Needs.Hunger); hash = Add(hash, actor.Needs.Fatigue);
            hash = Add(hash, actor.Activity.Value); hash = Add(hash, actor.Health.Value);
            hash = Add(hash, actor.Cooldown.Value); hash = Add(hash, actor.Target.LogicalId);
            hash = Add(hash, actor.Cell.X); hash = Add(hash, actor.Cell.Y);
        }
        foreach (EffectSnapshot effect in Effects)
        {
            hash = Add(hash, effect.Active ? 1 : 0);
            hash = Add(hash, effect.Generation);
            hash = Add(hash, effect.Position.X); hash = Add(hash, effect.Position.Y);
            hash = Add(hash, effect.Velocity.X); hash = Add(hash, effect.Velocity.Y);
            hash = Add(hash, effect.Lifetime);
        }
        foreach (ItemSnapshot item in Items)
        {
            hash = Add(hash, item.Position.X); hash = Add(hash, item.Position.Y);
            hash = Add(hash, item.Value);
        }
        Hash = hash;
    }

    internal string? FirstDifference(SimulationSnapshot other)
    {
        for (int index = 0; index < Actors.Length; index++)
        {
            if (!Same(Actors[index], other.Actors[index]))
                return $"actor {index}: {Describe(Actors[index])} != {Describe(other.Actors[index])}";
        }
        for (int index = 0; index < Effects.Length; index++)
        {
            if (!Same(Effects[index], other.Effects[index]))
                return $"effect slot {index}: {Describe(Effects[index])} != {Describe(other.Effects[index])}";
        }
        for (int index = 0; index < Items.Length; index++)
        {
            if (!Same(Items[index], other.Items[index]))
                return $"item {index}: {Describe(Items[index])} != {Describe(other.Items[index])}";
        }
        return null;
    }

    private static bool Same(in ActorSnapshot left, in ActorSnapshot right) =>
        left.Alive == right.Alive && left.Tier == right.Tier &&
        left.Position.X == right.Position.X && left.Position.Y == right.Position.Y &&
        left.Velocity.X == right.Velocity.X && left.Velocity.Y == right.Velocity.Y &&
        left.Needs.Hunger == right.Needs.Hunger && left.Needs.Fatigue == right.Needs.Fatigue &&
        left.Activity.Value == right.Activity.Value && left.Health.Value == right.Health.Value &&
        left.Cooldown.Value == right.Cooldown.Value && left.Target.LogicalId == right.Target.LogicalId &&
        left.Cell.X == right.Cell.X && left.Cell.Y == right.Cell.Y;

    private static bool Same(in EffectSnapshot left, in EffectSnapshot right) =>
        left.Active == right.Active && left.Generation == right.Generation &&
        left.Position.X == right.Position.X && left.Position.Y == right.Position.Y &&
        left.Velocity.X == right.Velocity.X && left.Velocity.Y == right.Velocity.Y &&
        left.Lifetime == right.Lifetime;

    private static bool Same(in ItemSnapshot left, in ItemSnapshot right) =>
        left.Position.X == right.Position.X && left.Position.Y == right.Position.Y &&
        left.Value == right.Value;

    private static ulong Add(ulong hash, int value) =>
        unchecked((hash ^ (uint)value) * 1099511628211UL);

    private static string Describe(ActorSnapshot value) =>
        $"alive={value.Alive},tier={value.Tier},pos=({value.Position.X},{value.Position.Y})," +
        $"vel=({value.Velocity.X},{value.Velocity.Y}),need=({value.Needs.Hunger},{value.Needs.Fatigue})," +
        $"health={value.Health.Value},cd={value.Cooldown.Value},target={value.Target.LogicalId}," +
        $"cell=({value.Cell.X},{value.Cell.Y})";

    private static string Describe(EffectSnapshot value) =>
        $"active={value.Active},gen={value.Generation},pos=({value.Position.X},{value.Position.Y})," +
        $"life={value.Lifetime}";

    private static string Describe(ItemSnapshot value) =>
        $"pos=({value.Position.X},{value.Position.Y}),value={value.Value}";
}

internal sealed class SemanticOracle
{
    private readonly SimulationProfile _profile;
    private readonly ActorSnapshot[] _actors;
    private readonly EffectSnapshot[] _effects;
    private readonly ItemSnapshot[] _items;
    private readonly bool[] _justDied;
    private readonly bool[] _justRespawned;
    private readonly int[] _effectGenerations;
    private readonly int[] _deathsThisTick;
    private readonly int[] _respawnsThisTick;
    private readonly int[] _destroyedEffectsThisTick;
    private readonly int[] _destroyedEffectGenerationsThisTick;
    private readonly int[] _createdEffectsThisTick;
    private readonly int[] _effectIterationSlots;
    private readonly int[] _effectRows;
    private int _effectIterationCount;
    private readonly ComponentId[] _deathRemoved;
    private readonly ComponentId[] _deathAdded;
    private readonly ComponentId[] _respawnRemoved;
    private readonly ComponentId[] _respawnAdded;
    private readonly int[][] _aliveIteration;
    private readonly int[][] _deadIteration;
    private readonly int[] _aliveCounts;
    private readonly int[] _deadCounts;
    private readonly int[] _actorRows;

    internal SemanticOracle(SimulationProfile profile)
    {
        _profile = profile;
        _actors = new ActorSnapshot[profile.ActorCount];
        _effects = new EffectSnapshot[profile.EffectSlots];
        _items = new ItemSnapshot[profile.ItemCount];
        _justDied = new bool[profile.ActorCount];
        _justRespawned = new bool[profile.ActorCount];
        _effectGenerations = new int[profile.EffectSlots];
        _deathsThisTick = new int[profile.ActorCount];
        _respawnsThisTick = new int[profile.ActorCount];
        _destroyedEffectsThisTick = new int[profile.EffectSlots];
        _destroyedEffectGenerationsThisTick = new int[profile.EffectSlots];
        _createdEffectsThisTick = new int[profile.EffectSlots];
        _effectIterationSlots = new int[profile.EffectSlots];
        _effectRows = new int[profile.EffectSlots];
        ComponentId dead = SimulationSchema.GetComponentId<DeadState>();
        _deathRemoved = Sorted(
            SimulationSchema.GetComponentId<Activity>(),
            SimulationSchema.GetComponentId<Needs>(),
            SimulationSchema.GetComponentId<SpatialCell>(),
            SimulationSchema.GetComponentId<Target>(),
            SimulationSchema.GetComponentId<Velocity>());
        _deathAdded = [dead];
        _respawnRemoved = [dead];
        _respawnAdded = (ComponentId[])_deathRemoved.Clone();
        _aliveIteration = new int[4][];
        _deadIteration = new int[4][];
        _aliveCounts = new int[4];
        _deadCounts = new int[4];
        _actorRows = new int[profile.ActorCount];
        for (int tier = 0; tier < 4; tier++)
        {
            _aliveIteration[tier] = new int[profile.TierCounts[tier]];
            _deadIteration[tier] = new int[profile.TierCounts[tier]];
        }
        Initialize();
    }

    internal SimulationSnapshot Snapshot { get; } = null!;
    internal DamageOwnershipCollision? LastDamageOwnershipCollision { get; private set; }
    internal long ActorDeaths { get; private set; }
    internal long ActorRespawns { get; private set; }
    internal long EffectsCreated { get; private set; }
    internal long EffectsDestroyed { get; private set; }
    internal ulong LifecycleHash { get; private set; } = LifecycleHashing.Offset;
    internal ulong OrderedLifecycleHash { get; private set; } = LifecycleHashing.Offset;
    internal long LifecycleEvents { get; private set; }

    internal void Initialize()
    {
        for (int id = 0; id < _actors.Length; id++)
        {
            Position position = OracleMath.InitialActorPosition(id);
            _actors[id] = new ActorSnapshot
            {
                Alive = true,
                Tier = _profile.ActorTier[id],
                Position = position,
                Health = OracleMath.InitialHealth(id),
                Activity = OracleMath.InitialActivity(id),
                Target = new Target { LogicalId = 0 },
                Cell = OracleMath.Cell(position),
            };
            int tier = _profile.ActorTier[id];
            _actorRows[id] = _aliveCounts[tier];
            _aliveIteration[tier][_aliveCounts[tier]++] = id;
        }

        for (int slot = 0; slot < _effects.Length; slot++)
        {
            _effectIterationSlots[slot] = slot;
            _effectRows[slot] = slot;
            _effectGenerations[slot] = 1;
            _effects[slot] = new EffectSnapshot
            {
                Active = true,
                Generation = 1,
                Position = OracleMath.EffectPosition(slot, 1, 0),
                Velocity = OracleMath.EffectVelocity(slot, 1),
                Lifetime = _profile.EffectLifetime,
            };
        }
        _effectIterationCount = _effects.Length;

        for (int item = 0; item < _items.Length; item++)
        {
            Position position = new() { X = item * 11 - 2_000, Y = item * 7 - 1_000 };
            _items[item] = new ItemSnapshot
            {
                Position = position,
                Value = OracleMath.ItemValue(item, position, 0),
            };
        }
    }

    internal void Step(ulong tick)
    {
        int deathCount = 0;
        int respawnCount = 0;
        int destroyedEffectCount = 0;
        int createdEffectCount = 0;
        LastDamageOwnershipCollision = null;
        Array.Clear(_justDied);
        Array.Clear(_justRespawned);
        for (int id = 0; id < _actors.Length; id++)
        {
            ActorSnapshot actor = _actors[id];
            int period = _profile.TierPeriods[actor.Tier];
            if (actor.Alive && tick % unchecked((ulong)period) == 0)
            {
                int activityMix = unchecked(id + actor.Activity.Value + (int)tick);
                int fatigueMix = unchecked(id + (int)tick);
                actor.Needs.Hunger = unchecked((actor.Needs.Hunger + 1 + (activityMix & 3)) % 1_000);
                actor.Needs.Fatigue = unchecked((actor.Needs.Fatigue + 2 + (fatigueMix & 1)) % 1_000);
                actor.Velocity = OracleMath.Steering(id, actor.Needs, actor.Activity, tick);
                actor.Position.X = unchecked(actor.Position.X + actor.Velocity.X);
                actor.Position.Y = unchecked(actor.Position.Y + actor.Velocity.Y);
                actor.Cell = OracleMath.Cell(actor.Position);
            }
            _actors[id] = actor;
        }

        for (int operation = 0; operation < _profile.TargetOperations; operation++)
        {
            int source = _profile.PartitionSafeRandomWrites
                ? operation
                : unchecked(((int)tick * 17 + operation) % _actors.Length);
            ActorSnapshot actor = _actors[source];
            if (actor.Alive)
            {
                actor.Target.LogicalId = unchecked((source + 97 + (int)tick * 3) % _actors.Length);
                _actors[source] = actor;
            }
        }

        for (int left = 0; left < _profile.DamageOperations; left++)
        {
            if (!_actors[left].Alive)
                continue;
            int target = _actors[left].Target.LogicalId;
            if ((uint)target >= unchecked((uint)_actors.Length) || !_actors[target].Alive)
                continue;
            for (int right = left + 1; right < _profile.DamageOperations; right++)
            {
                if (_actors[right].Alive && _actors[right].Target.LogicalId == target)
                {
                    LastDamageOwnershipCollision = new DamageOwnershipCollision(
                        tick, left, right, target);
                    left = _profile.DamageOperations;
                    break;
                }
            }
        }

        for (int operation = 0; operation < _profile.DamageOperations; operation++)
        {
            int source = operation;
            ActorSnapshot attacker = _actors[source];
            if (!attacker.Alive)
                continue;
            int target = attacker.Target.LogicalId;
            ActorSnapshot victim = _actors[target];
            if (victim.Alive)
            {
                victim.Health.Value -= 1 + ((int)tick + source & 3);
                _actors[target] = victim;
            }
        }

        for (int id = 0; id < _actors.Length; id++)
        {
            ActorSnapshot actor = _actors[id];
            if (actor.Alive && actor.Health.Value <= 0)
            {
                actor.Alive = false;
                actor.Cooldown.Value = _profile.RespawnDelay;
                actor.Velocity = default;
                actor.Needs = default;
                actor.Activity = default;
                actor.Target = default;
                actor.Cell = default;
                _justDied[id] = true;
                _actors[id] = actor;
                ActorDeaths++;
            }
        }

        for (int id = 0; id < _actors.Length; id++)
        {
            ActorSnapshot actor = _actors[id];
            if (!actor.Alive && !_justDied[id])
            {
                if (actor.Cooldown.Value <= 1)
                {
                    actor.Alive = true;
                    actor.Health = OracleMath.InitialHealth(id);
                    actor.Velocity = default;
                    actor.Needs = default;
                    actor.Activity = default;
                    actor.Target = default;
                    actor.Cell = default;
                    actor.Cooldown = default;
                    ActorRespawns++;
                    _justRespawned[id] = true;
                }
                else
                {
                    actor.Cooldown.Value--;
                }
                _actors[id] = actor;
            }
        }

        for (int tier = 0; tier < 4; tier++)
        {
            int aliveCount = _aliveCounts[tier];
            for (int row = 0; row < aliveCount; row++)
            {
                int id = _aliveIteration[tier][row];
                if (_justDied[id])
                    _deathsThisTick[deathCount++] = id;
            }
            int deadCount = _deadCounts[tier];
            for (int row = 0; row < deadCount; row++)
            {
                int id = _deadIteration[tier][row];
                if (_justRespawned[id])
                    _respawnsThisTick[respawnCount++] = id;
            }
        }

        for (int index = 0; index < deathCount; index++)
            MoveActor(_deathsThisTick[index], toAlive: false);
        for (int index = 0; index < respawnCount; index++)
            MoveActor(_respawnsThisTick[index], toAlive: true);

        int effectRowsAtTickStart = _effectIterationCount;
        for (int row = 0; row < effectRowsAtTickStart; row++)
        {
            int slot = _effectIterationSlots[row];
            EffectSnapshot effect = _effects[slot];
            if (effect.Active && --effect.Lifetime <= 0)
            {
                effect = default;
                EffectsDestroyed++;
                _destroyedEffectsThisTick[destroyedEffectCount] = slot;
                _destroyedEffectGenerationsThisTick[destroyedEffectCount] = _effectGenerations[slot];
                destroyedEffectCount++;
            }
            _effects[slot] = effect;
        }

        // Independently model the logical iteration container's packed removal
        // order so expected lifecycle ordering follows command record order.
        for (int index = 0; index < destroyedEffectCount; index++)
        {
            int slot = _destroyedEffectsThisTick[index];
            int row = _effectRows[slot];
            int donorRow = --_effectIterationCount;
            int donorSlot = _effectIterationSlots[donorRow];
            _effectIterationSlots[row] = donorSlot;
            _effectRows[donorSlot] = row;
            _effectRows[slot] = -1;
        }

        if (tick + 1 >= unchecked((ulong)_profile.EffectLifetime))
        {
            int cycle = (int)(tick - unchecked((ulong)(_profile.EffectLifetime - 1))) % _profile.EffectLifetime;
            for (int index = 0; index < _profile.EffectsSpawnedPerTick; index++)
            {
                int slot = cycle * _profile.EffectsSpawnedPerTick + index;
                int generation = ++_effectGenerations[slot];
                _effects[slot] = new EffectSnapshot
                {
                    Active = true,
                    Generation = generation,
                    Position = OracleMath.EffectPosition(slot, generation, tick),
                    Velocity = OracleMath.EffectVelocity(slot, generation),
                    Lifetime = _profile.EffectLifetime,
                };
                EffectsCreated++;
                _createdEffectsThisTick[createdEffectCount++] = slot;
                if (_effectRows[slot] < 0)
                {
                    _effectRows[slot] = _effectIterationCount;
                    _effectIterationSlots[_effectIterationCount++] = slot;
                }
            }
        }

        if (tick % 60 == 0)
        {
            for (int item = 0; item < _items.Length; item++)
            {
                ItemSnapshot value = _items[item];
                value.Value = OracleMath.ItemValue(item, value.Position, tick);
                _items[item] = value;
            }
        }

        // Canonical structural playback commits live destroys, creates, remove
        // transitions, then add transitions. The oracle independently emits
        // normalized logical facts in that same public order.
        for (int index = 0; index < destroyedEffectCount; index++)
        {
            int slot = _destroyedEffectsThisTick[index];
            AddLifecycle(
                LifecycleKind.Destroyed,
                LifecycleDomain.Effect,
                slot,
                _destroyedEffectGenerationsThisTick[index],
                ComponentId.Invalid);
        }
        for (int index = 0; index < createdEffectCount; index++)
        {
            int slot = _createdEffectsThisTick[index];
            AddLifecycle(
                LifecycleKind.Created,
                LifecycleDomain.Effect,
                slot,
                _effectGenerations[slot],
                ComponentId.Invalid);
        }
        for (int index = 0; index < deathCount; index++)
            AddTransitionLifecycle(_deathsThisTick[index], _deathRemoved, _deathAdded);
        for (int index = 0; index < respawnCount; index++)
            AddTransitionLifecycle(_respawnsThisTick[index], _respawnRemoved, _respawnAdded);
    }

    internal void CopyTo(SimulationSnapshot destination)
    {
        _actors.CopyTo(destination.Actors, 0);
        _effects.CopyTo(destination.Effects, 0);
        _items.CopyTo(destination.Items, 0);
        destination.ComputeHash();
    }

    internal void SetActorHealthForTest(int logicalId, int value)
    {
        ActorSnapshot actor = _actors[logicalId];
        actor.Health.Value = value;
        _actors[logicalId] = actor;
    }

    internal void SetActorCooldownForTest(int logicalId, int value)
    {
        ActorSnapshot actor = _actors[logicalId];
        actor.Cooldown.Value = value;
        _actors[logicalId] = actor;
    }

    internal void SetEffectLifetimeForTest(int slot, int value)
    {
        EffectSnapshot effect = _effects[slot];
        effect.Lifetime = value;
        _effects[slot] = effect;
    }

    internal void ResetMetrics()
    {
        (ActorDeaths, ActorRespawns, EffectsCreated, EffectsDestroyed) = (0, 0, 0, 0);
        LifecycleHash = LifecycleHashing.Offset;
        OrderedLifecycleHash = LifecycleHashing.Offset;
        LifecycleEvents = 0;
    }

    private void AddTransitionLifecycle(
        int logicalId,
        ReadOnlySpan<ComponentId> removed,
        ReadOnlySpan<ComponentId> added)
    {
        for (int index = 0; index < removed.Length; index++)
            AddLifecycle(LifecycleKind.ComponentRemoved, LifecycleDomain.Actor, logicalId, 0, removed[index]);
        for (int index = 0; index < added.Length; index++)
            AddLifecycle(LifecycleKind.ComponentAdded, LifecycleDomain.Actor, logicalId, 0, added[index]);
    }

    private void MoveActor(int logicalId, bool toAlive)
    {
        int tier = _profile.ActorTier[logicalId];
        int[] source = toAlive ? _deadIteration[tier] : _aliveIteration[tier];
        int[] destination = toAlive ? _aliveIteration[tier] : _deadIteration[tier];
        ref int sourceCount = ref (toAlive ? ref _deadCounts[tier] : ref _aliveCounts[tier]);
        ref int destinationCount = ref (toAlive ? ref _aliveCounts[tier] : ref _deadCounts[tier]);
        int row = _actorRows[logicalId];
        int donorRow = --sourceCount;
        int donorId = source[donorRow];
        source[row] = donorId;
        _actorRows[donorId] = row;
        _actorRows[logicalId] = destinationCount;
        destination[destinationCount++] = logicalId;
    }

    private void AddLifecycle(
        LifecycleKind kind,
        LifecycleDomain domain,
        int logicalId,
        int logicalGeneration,
        ComponentId component)
    {
        LifecycleHash = LifecycleHashing.Add(
            LifecycleHash,
            kind,
            domain,
            logicalId,
            logicalGeneration,
            component);
        OrderedLifecycleHash = LifecycleHashing.AddOrdered(
            OrderedLifecycleHash,
            kind,
            domain,
            logicalId,
            logicalGeneration,
            component);
        LifecycleEvents++;
    }

    private static ComponentId[] Sorted(params ComponentId[] values)
    {
        Array.Sort(values);
        return values;
    }
}

internal readonly record struct DamageOwnershipCollision(
    ulong Tick,
    int FirstSource,
    int SecondSource,
    int Target);

internal static class SliceSelfTests
{
    internal static DamageOwnershipCollision OriginalRandomWriteCollision { get; private set; }
    internal static bool LifecycleOverflowProbePassed { get; private set; }

    internal static void Run()
    {
        Position initial = SimulationMath.InitialActorPosition(0);
        Assert(initial.X == -2_048 && initial.Y == -2_048,
            "Actor-zero initial position is not the independently derived value.");
        Assert(SimulationMath.InitialActivity(0).Value == 12_345,
            "Actor-zero activity seed is not the independently derived value.");
        Assert(SimulationMath.InitialHealth(0).Value == 32,
            "Actor-zero initial health is not the independently derived value.");

        SimulationProfile tiered = new(CreateOptions(256, probeRandomWrites: false, allActive: false));
        ulong[] ticks = [0, 1, 2, 7, 8, 59, 60, 61];
        int[] expectedDueTierCounts = [4, 1, 2, 1, 3, 1, 3, 1];
        for (int sample = 0; sample < ticks.Length; sample++)
        {
            int due = 0;
            for (int tier = 0; tier < tiered.TierPeriods.Length; tier++)
                if (ticks[sample] % unchecked((ulong)tiered.TierPeriods[tier]) == 0)
                    due++;
            Assert(due == expectedDueTierCounts[sample],
                $"Tier cadence mismatch at tick {ticks[sample]}: {due} != {expectedDueTierCounts[sample]}.");
        }

        var deathOracle = new SemanticOracle(tiered);
        var snapshot = new SimulationSnapshot(tiered);
        deathOracle.SetActorHealthForTest(0, 1);
        deathOracle.Step(0);
        deathOracle.CopyTo(snapshot);
        Assert(!snapshot.Actors[0].Alive && snapshot.Actors[0].Cooldown.Value == tiered.RespawnDelay,
            "A forced tick-zero death did not enter dead state with the complete cooldown.");
        deathOracle.SetActorCooldownForTest(0, 1);
        deathOracle.Step(1);
        deathOracle.CopyTo(snapshot);
        Assert(snapshot.Actors[0].Alive && snapshot.Actors[0].Cooldown.Value == 0 &&
            snapshot.Actors[0].Health.Value == 32,
            "A dead actor was not first eligible to respawn on the following tick.");

        var expirationOracle = new SemanticOracle(tiered);
        expirationOracle.SetEffectLifetimeForTest(0, 1);
        expirationOracle.Step(0);
        expirationOracle.CopyTo(snapshot);
        Assert(!snapshot.Effects[0].Active,
            "An expired effect remained active before the spawn-ring activation boundary.");

        var replacementOracle = new SemanticOracle(tiered);
        replacementOracle.SetEffectLifetimeForTest(0, 1);
        replacementOracle.Step(unchecked((ulong)(tiered.EffectLifetime - 1)));
        replacementOracle.CopyTo(snapshot);
        Assert(snapshot.Effects[0].Active && snapshot.Effects[0].Generation == 2 &&
            snapshot.Effects[0].Lifetime == tiered.EffectLifetime,
            "Effect expiration and same-tick post-barrier slot replacement diverged.");

        SimulationProfile originalProfile = new(
            CreateOptions(10_000, probeRandomWrites: false, allActive: false));
        var originalOwnership = new SemanticOracle(originalProfile);
        DamageOwnershipCollision? collision = null;
        for (ulong tick = 0; tick < 256 && collision is null; tick++)
        {
            originalOwnership.Step(tick);
            collision = originalOwnership.LastDamageOwnershipCollision;
        }
        Assert(collision.HasValue,
            "The original rolling-target damage workload no longer demonstrates its ownership collision.");
        OriginalRandomWriteCollision = collision.GetValueOrDefault();

        SimulationProfile safe = new(
            CreateOptions(10_000, probeRandomWrites: true, allActive: false));
        var safeOwnership = new SemanticOracle(safe);
        for (ulong tick = 0; tick < 256; tick++)
        {
            safeOwnership.Step(tick);
            Assert(!safeOwnership.LastDamageOwnershipCollision.HasValue,
                $"The corrected PartitionDisjoint probe produced a target collision at tick {tick}.");
        }

        RunLifecycleOverflowProbe();
    }

    private static void RunLifecycleOverflowProbe()
    {
        using var world = new EcsWorld(
            SimulationSchema.CreateSchema(),
            new EcsWorldOptions(
                new EntityCapacity(4),
                maximumArchetypes: 1,
                maximumPreparedPlans: 0,
                lifecycleRecordCapacity: 4));
        ArchetypeHandle archetype = world.RegisterArchetype(
            [SimulationSchema.GetComponentId<Position>()],
            ChunkLayoutOptions.KiB16,
            maximumChunks: 1);
        world.Seal();
        world.Initialize();
        world.BeginRun();
        LifecycleCursor cursor = world.CaptureLifecycleCursorFromBeginning();
        for (int index = 0; index < 3; index++)
        {
            Assert(world.TryCreate(archetype, out Entity entity),
                "Lifecycle overflow probe create failed.");
            Assert(world.TryDestroy(entity),
                "Lifecycle overflow probe destroy failed.");
        }
        ulong originalSequence = cursor.NextSequence;
        Span<LifecycleRecord> records = stackalloc LifecycleRecord[4];
        LifecycleReadResult read = world.ReadLifecycle(ref cursor, records);
        Assert(read.Status == LifecycleReadStatus.ResyncRequired,
            "A deliberately lagged Slice lifecycle cursor did not require resynchronization.");
        Assert(cursor.NextSequence == originalSequence,
            "Resync-required advanced the deliberately lagged Slice lifecycle cursor.");
        LifecycleOverflowProbePassed = true;
    }

    private static SimulationOptions CreateOptions(
        int population,
        bool probeRandomWrites,
        bool allActive) => new(
        Population: population,
        WarmupTicks: 0,
        MeasuredTicks: 1,
        Workers: 7,
        ValidationEvery: 1,
        Mode: RunMode.Serial,
        OutputPath: Path.GetTempPath(),
        Quick: true,
        ProbeRandomWrites: probeRandomWrites,
        DeferredValidation: false,
        AllActive: allActive);

    private static void Assert(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException($"Slice 0 deterministic self-test failed: {message}");
    }
}

internal sealed class SimulationWorld : IDisposable
{
    private readonly SimulationOptions _options;
    internal readonly SimulationProfile Profile;
    internal readonly EcsWorld World;
    internal readonly Entity[] ActorEntities;
    internal readonly Entity[] ItemEntities;
    internal readonly int[] ActorTier;
    internal readonly int[] EffectGenerations;

    internal readonly ComponentLookup<LogicalIdentity> Identity;
    internal readonly ComponentLookup<Position> Position;
    internal readonly ComponentLookup<Velocity> Velocity;
    internal readonly ComponentLookup<Needs> Needs;
    internal readonly ComponentLookup<Activity> Activity;
    internal readonly ComponentLookup<Health> Health;
    internal readonly ComponentLookup<Cooldown> Cooldown;
    internal readonly ComponentLookup<Target> Target;
    internal readonly ComponentLookup<SpatialCell> Cell;
    internal readonly ComponentLookup<DeadState> Dead;
    internal readonly ComponentLookup<EffectIdentity> Effect;
    internal readonly ComponentLookup<Lifetime> Lifetime;
    internal readonly ComponentLookup<WorldItemState> ItemState;

    internal readonly TierBinding<Tier0Marker> Tier0;
    internal readonly TierBinding<Tier1Marker> Tier1;
    internal readonly TierBinding<Tier2Marker> Tier2;
    internal readonly TierBinding<Tier3Marker> Tier3;
    internal readonly TierBindingBase[] Tiers;
    internal readonly ArchetypeHandle EffectArchetype;
    internal readonly ArchetypeHandle ItemArchetype;
    internal readonly CompiledPrefabCatalog Content;
    internal readonly PrefabHandle EffectPrefab;
    internal readonly PrefabHandle ItemPrefab;
    internal readonly ReadWriteJobPlan<EffectIdentity, Lifetime> EffectLifetimePlan;
    internal readonly ReadReadWriteJobPlan<LogicalIdentity, Position, WorldItemState> ItemPlan;
    internal readonly ReadReadReadWriteJobPlan<LogicalIdentity, Needs, Health, Cooldown> DeathPlan;
    internal readonly ReadReadWriteWriteJobPlan<LogicalIdentity, DeadState, Cooldown, Health> RespawnPlan;
    internal readonly ChangedRowsJobPlan<Position, Position> PositionChangedPlan;
    internal readonly ChangedRowsJobPlan<Health, Health> HealthChangedPlan;

    internal readonly PositionObserverRuntime PresentationObserver;
    internal readonly HealthObserverRuntime ReplicationObserver;
    internal readonly PositionObserverRuntime PersistenceObserver;
    internal readonly StructuralCounters Counters = new();
    internal readonly RandomOperationCounters RandomOperations = new();
    internal readonly LifecycleObserver PresentationLifecycle;
    internal readonly LifecycleObserver ReplicationLifecycle;
    internal readonly LifecycleObserver PersistenceLifecycle;
    internal readonly double ContentCompileMicroseconds;
    internal double BootstrapMicroseconds { get; private set; }

    private readonly ArchetypeTransition[] _toDead;
    private readonly ArchetypeTransition[] _toAlive;
    private readonly bool[] _effectSeen;
    private readonly int _participants;

    private SimulationWorld(SimulationOptions options, SimulationProfile profile, EcsWorld world)
    {
        _options = options;
        Profile = profile;
        World = world;
        _participants = options.Workers + 1;
        ActorEntities = new Entity[profile.ActorCount];
        ItemEntities = new Entity[profile.ItemCount];
        ActorTier = profile.ActorTier;
        EffectGenerations = new int[profile.EffectSlots];
        _effectSeen = new bool[profile.EffectSlots];
        _toDead = new ArchetypeTransition[4];
        _toAlive = new ArchetypeTransition[4];

        Identity = world.PrepareLookup<LogicalIdentity>();
        Position = world.PrepareLookup<Position>();
        Velocity = world.PrepareLookup<Velocity>();
        Needs = world.PrepareLookup<Needs>();
        Activity = world.PrepareLookup<Activity>();
        Health = world.PrepareLookup<Health>();
        Cooldown = world.PrepareLookup<Cooldown>();
        Target = world.PrepareLookup<Target>();
        Cell = world.PrepareLookup<SpatialCell>();
        Dead = world.PrepareLookup<DeadState>();
        Effect = world.PrepareLookup<EffectIdentity>();
        Lifetime = world.PrepareLookup<Lifetime>();
        ItemState = world.PrepareLookup<WorldItemState>();

        Tier0 = new TierBinding<Tier0Marker>(this, 0);
        Tier1 = new TierBinding<Tier1Marker>(this, 1);
        Tier2 = new TierBinding<Tier2Marker>(this, 2);
        Tier3 = new TierBinding<Tier3Marker>(this, 3);
        Tiers = [Tier0, Tier1, Tier2, Tier3];

        EffectArchetype = RegisterEffectArchetype(world, profile);
        ItemArchetype = RegisterItemArchetype(world, profile);
        long contentCompileStarted = Stopwatch.GetTimestamp();
        Content = CompileContent(world, profile);
        ContentCompileMicroseconds = Stopwatch.GetElapsedTime(
            contentCompileStarted, Stopwatch.GetTimestamp()).TotalMicroseconds;
        EffectPrefab = Content.GetHandle(new PrefabKey("simulation/effects/temporary"));
        ItemPrefab = Content.GetHandle(new PrefabKey("simulation/items/world-item"));
        for (int tier = 0; tier < Tiers.Length; tier++)
        {
            Tiers[tier].AlivePrefab = Content.GetHandle(
                new PrefabKey($"simulation/actors/tier-{tier}/alive"));
            Tiers[tier].DeadPrefab = Content.GetHandle(
                new PrefabKey($"simulation/actors/tier-{tier}/dead"));
        }
        EffectLifetimePlan = world.PrepareReadWriteJob<EffectIdentity, Lifetime>();
        ItemPlan = world.PrepareReadReadWriteJob<LogicalIdentity, Position, WorldItemState>();
        DeathPlan = world.PrepareReadReadReadWriteJob<LogicalIdentity, Needs, Health, Cooldown>();
        RespawnPlan = world.PrepareReadReadWriteWriteJob<LogicalIdentity, DeadState, Cooldown, Health>();
        PositionChangedPlan = world.PrepareChangedRowsJob<Position, Position>();
        HealthChangedPlan = world.PrepareChangedRowsJob<Health, Health>();

        PresentationObserver = new PositionObserverRuntime();
        ReplicationObserver = new HealthObserverRuntime();
        PersistenceObserver = new PositionObserverRuntime();
        PresentationLifecycle = new LifecycleObserver(period: 1, profile.TotalCapacity);
        ReplicationLifecycle = new LifecycleObserver(period: 2, profile.TotalCapacity);
        PersistenceLifecycle = new LifecycleObserver(period: 60, profile.TotalCapacity);
    }

    internal static SimulationWorld Create(SimulationOptions options, bool parallel)
    {
        SimulationProfile profile = new(options);
        int workers = parallel ? options.Workers : 0;
        int participants = workers + 1;
        int maximumPartitions = Math.Max(1, participants * 2);
        int maximumSystems = 32;
        int structuralCommandsPerProducer = checked(
            profile.EffectSlots +
            profile.EffectsSpawnedPerTick * 4 +
            profile.DamageOperations * 2 +
            64);
        var scheduler = new EcsSchedulerOptions(
            maximumSystems,
            maximumDependencies: 64,
            maximumWorkItems: maximumSystems * maximumPartitions,
            maximumStructuralSegments: maximumSystems * maximumPartitions,
            backgroundWorkerCount: workers,
            callerParticipates: true,
            structuralCommandsPerProducer: structuralCommandsPerProducer,
            structuralPayloadBytesPerProducer: 64 * 1_024,
            maximumPartitionsPerSystem: maximumPartitions,
            profilingEnabled: true,
            phaseStaggeringEnabled: false);
        var world = new EcsWorld(
            SimulationSchema.CreateSchema(),
            new EcsWorldOptions(
                new EntityCapacity(profile.TotalCapacity),
                maximumArchetypes: 10,
                maximumPreparedPlans: 64,
                maximumPreparedTransitions: 8,
                maximumStructuralCommands: 131_072,
                scheduler: scheduler,
                lifecycleRecordCapacity: profile.LifecycleRecordCapacity));

        var simulation = new SimulationWorld(options, profile, world);
        simulation.RegisterSystems();
        world.Seal();
        world.Initialize();
        long bootstrapStarted = Stopwatch.GetTimestamp();
        simulation.Seed();
        simulation.BootstrapMicroseconds = Stopwatch.GetElapsedTime(
            bootstrapStarted, Stopwatch.GetTimestamp()).TotalMicroseconds;
        simulation.SetObserverCursorsFromNow();
        world.BeginRun();
        world.SchedulerRunMode = parallel ? SchedulerRunMode.Parallel : SchedulerRunMode.Serial;
        return simulation;
    }

    private void RegisterSystems()
    {
        Tier0.RegisterSystems();
        Tier1.RegisterSystems();
        Tier2.RegisterSystems();
        Tier3.RegisterSystems();

        SystemId target = World.RegisterRangeSystem<TargetRuntime, TargetJob>(
            new SystemDefinition(
                "simulation.slice0.target-acquisition",
                SystemStage.Perception,
                executionPolicy: SystemExecutionPolicy.Parallel,
                additionalReads: [SimulationSchema.GetComponentId<Needs>()],
                additionalWrites: [SimulationSchema.GetComponentId<Target>()],
                parallelRandomAccessPolicy: ParallelRandomAccessPolicy.PartitionDisjoint,
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 32),
            new TargetRuntime(
                ActorEntities, Profile.TargetOperations, Profile.PartitionSafeRandomWrites,
                Needs, Target, RandomOperations));

        SystemId damage = World.RegisterRangeSystem<DamageRuntime, DamageJob>(
            new SystemDefinition(
                "simulation.slice0.damage",
                SystemStage.Combat,
                executionPolicy: SystemExecutionPolicy.Parallel,
                requires: [target],
                additionalReads: [SimulationSchema.GetComponentId<Needs>(), SimulationSchema.GetComponentId<Target>()],
                additionalWrites: [SimulationSchema.GetComponentId<Health>()],
                // The default is the documented safe policy. The explicit probe
                // preserves a reduced KERNEL-CANDIDATE reproduction for review.
                parallelRandomAccessPolicy: _options.ProbeRandomWrites
                    ? ParallelRandomAccessPolicy.PartitionDisjoint
                    : ParallelRandomAccessPolicy.SerialOnly,
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 32),
            new DamageRuntime(
                ActorEntities, Profile.DamageOperations, Needs, Target, Health, RandomOperations));

        SystemId death = World.RegisterTrackedReadReadReadWriteSystem<LogicalIdentity, Needs, Health, Cooldown,
            DeathRuntime, DeathJob>(
            new SystemDefinition(
                "simulation.slice0.death",
                SystemStage.Combat,
                executionPolicy: SystemExecutionPolicy.Auto,
                requires: [damage],
                structuralComponents: [SimulationSchema.GetComponentId<DeadState>()],
                parallelRowThreshold: 8_192,
                minimumRowsPerPartition: 2_048),
            DeathPlan,
            new DeathRuntime(ActorTier, _toDead, Profile.RespawnDelay, Counters));

        SystemId respawn = World.RegisterTrackedReadReadWriteWriteSystem<LogicalIdentity, DeadState, Cooldown, Health,
            RespawnRuntime, RespawnJob>(
            new SystemDefinition(
                "simulation.slice0.respawn",
                SystemStage.Cleanup,
                executionPolicy: SystemExecutionPolicy.Auto,
                requires: [death],
                structuralComponents: [SimulationSchema.GetComponentId<DeadState>()],
                parallelRowThreshold: 8_192,
                minimumRowsPerPartition: 2_048),
            RespawnPlan,
            new RespawnRuntime(ActorTier, _toAlive, Profile, Counters));

        SystemId effectLifetime = World.RegisterReadWriteSystem<EffectIdentity, Lifetime,
            EffectLifetimeRuntime, EffectLifetimeJob>(
            new SystemDefinition(
                "simulation.slice0.effect-lifetime",
                SystemStage.Cleanup,
                executionPolicy: SystemExecutionPolicy.Parallel,
                structuralComponents: [SimulationSchema.GetComponentId<EffectIdentity>()],
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 32),
            EffectLifetimePlan,
            new EffectLifetimeRuntime(Counters));

        SystemId effectSpawn = World.RegisterRangeSystem<EffectSpawnRuntime, EffectSpawnJob>(
            new SystemDefinition(
                "simulation.slice0.effect-spawn",
                SystemStage.Cleanup,
                executionPolicy: SystemExecutionPolicy.Parallel,
                requires: [effectLifetime],
                structuralComponents:
                [
                    SimulationSchema.GetComponentId<EffectIdentity>(), SimulationSchema.GetComponentId<Position>(),
                    SimulationSchema.GetComponentId<Velocity>(), SimulationSchema.GetComponentId<Lifetime>()
                ],
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 4),
            new EffectSpawnRuntime(
                EffectPrefab, Profile, EffectGenerations, Counters));

        World.RegisterReadReadWriteSystem<LogicalIdentity, Position, WorldItemState,
            ItemRuntime, ItemJob>(
            new SystemDefinition(
                "simulation.slice0.world-items",
                SystemStage.WorldSim,
                new SystemSchedule(true, 60, 0),
                executionPolicy: SystemExecutionPolicy.Parallel,
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 32),
            ItemPlan,
            new ItemRuntime());

        World.RegisterChangedRowsSystem<Position, Position, PositionObserverRuntime, ObservePositionJob>(
            new SystemDefinition(
                "simulation.slice0.presentation-observer",
                SystemStage.Cleanup,
                executionPolicy: SystemExecutionPolicy.Serial,
                requires: [respawn, effectSpawn],
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 1),
            PositionChangedPlan,
            PresentationObserver);

        World.RegisterChangedRowsSystem<Health, Health, HealthObserverRuntime, ObserveHealthJob>(
            new SystemDefinition(
                "simulation.slice0.replication-observer",
                SystemStage.Cleanup,
                new SystemSchedule(true, 2, 0),
                executionPolicy: SystemExecutionPolicy.Serial,
                requires: [respawn, effectSpawn],
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 1),
            HealthChangedPlan,
            ReplicationObserver);

        World.RegisterChangedRowsSystem<Position, Position, PositionObserverRuntime, ObservePositionJob>(
            new SystemDefinition(
                "simulation.slice0.persistence-observer",
                SystemStage.Cleanup,
                new SystemSchedule(true, 60, 0),
                executionPolicy: SystemExecutionPolicy.Serial,
                requires: [respawn, effectSpawn],
                parallelRowThreshold: 1,
                minimumRowsPerPartition: 1),
            PositionChangedPlan,
            PersistenceObserver);
    }

    private void Seed()
    {
        int actorIndex = 0;
        for (int tier = 0; tier < Tiers.Length; tier++)
        {
            TierBindingBase binding = Tiers[tier];
            for (int local = 0; local < Profile.TierCounts[tier]; local++)
            {
                int logicalId = actorIndex++;
                if (!World.TryInstantiate(binding.AlivePrefab, out Entity entity))
                    throw new InvalidOperationException($"Actor creation failed at {logicalId}.");
                ActorEntities[logicalId] = entity;
                PresentationLifecycle.RegisterActor(entity, logicalId);
                ReplicationLifecycle.RegisterActor(entity, logicalId);
                PersistenceLifecycle.RegisterActor(entity, logicalId);
                WriteActorInitial(entity, logicalId);
            }
        }

        for (int item = 0; item < Profile.ItemCount; item++)
        {
            if (!World.TryInstantiate(ItemPrefab, out Entity entity))
                throw new InvalidOperationException($"Item creation failed at {item}.");
            ItemEntities[item] = entity;
            PresentationLifecycle.RegisterItem(entity, item);
            ReplicationLifecycle.RegisterItem(entity, item);
            PersistenceLifecycle.RegisterItem(entity, item);
            Position position = new() { X = item * 11 - 2_000, Y = item * 7 - 1_000 };
            Write(entity, Identity, new LogicalIdentity { Value = item });
            Write(entity, Position, position);
            Write(entity, ItemState, new WorldItemState { Value = SimulationMath.ItemValue(item, position, 0) });
        }

        for (int slot = 0; slot < Profile.EffectSlots; slot++)
        {
            EffectGenerations[slot] = 1;
            if (!World.TryInstantiate(EffectPrefab, out Entity entity))
                throw new InvalidOperationException($"Effect creation failed at {slot}.");
            PresentationLifecycle.RegisterEffect(entity, slot, 1);
            ReplicationLifecycle.RegisterEffect(entity, slot, 1);
            PersistenceLifecycle.RegisterEffect(entity, slot, 1);
            Position position = SimulationMath.EffectPosition(slot, 1, 0);
            Write(entity, Effect, new EffectIdentity { Slot = slot, Generation = 1 });
            Write(entity, Position, position);
            Write(entity, Velocity, SimulationMath.EffectVelocity(slot, 1));
            Write(entity, Lifetime, new Lifetime { Remaining = Profile.EffectLifetime });
        }
    }

    private void WriteActorInitial(Entity entity, int logicalId)
    {
        Position position = SimulationMath.InitialActorPosition(logicalId);
        Write(entity, Identity, new LogicalIdentity { Value = logicalId });
        Write(entity, Position, position);
        Write(entity, Velocity, default);
        Write(entity, Needs, default);
        Write(entity, Activity, SimulationMath.InitialActivity(logicalId));
        Write(entity, Health, SimulationMath.InitialHealth(logicalId));
        Write(entity, Cooldown, default);
        Write(entity, Target, default);
        Write(entity, Cell, SimulationMath.Cell(position));
    }

    private void Write<T>(Entity entity, in ComponentLookup<T> lookup, in T value)
        where T : unmanaged, IComponent<T>
    {
        // This helper is used only during owner-thread bootstrap, outside timing.
        if (!World.TryWrite(entity, lookup, value))
            throw new InvalidOperationException($"Bootstrap write of {typeof(T).Name} failed.");
    }

    private void SetObserverCursorsFromNow()
    {
        ChangeCursor cursor = World.CreateChangeCursorFromNow();
        PresentationObserver.Cursor = cursor;
        ReplicationObserver.Cursor = cursor;
        PersistenceObserver.Cursor = cursor;
        LifecycleCursor lifecycle = World.CaptureLifecycleCursorFromNow();
        PresentationLifecycle.Cursor = lifecycle;
        ReplicationLifecycle.Cursor = lifecycle;
        PersistenceLifecycle.Cursor = lifecycle;
    }

    private void ConsumeLifecycle(ulong tick, bool force = false)
    {
        PresentationLifecycle.ConsumeIfDue(World, Effect, tick, force);
        ReplicationLifecycle.ConsumeIfDue(World, Effect, tick, force);
        PersistenceLifecycle.ConsumeIfDue(World, Effect, tick, force);
    }

    internal SimulationSnapshot Capture(SimulationSnapshot destination)
    {
        Array.Clear(destination.Effects);
        Array.Clear(_effectSeen);
        for (int id = 0; id < ActorEntities.Length; id++)
        {
            Entity entity = ActorEntities[id];
            bool alive = World.Has(entity, Needs);
            ActorSnapshot actor = new() { Alive = alive, Tier = ActorTier[id] };
            if (!World.TryRead(entity, Position, out actor.Position) ||
                !World.TryRead(entity, Health, out actor.Health) ||
                !World.TryRead(entity, Cooldown, out actor.Cooldown))
                throw new InvalidOperationException($"Actor {id} lost retained state.");
            if (alive)
            {
                if (!World.TryRead(entity, Velocity, out actor.Velocity) ||
                    !World.TryRead(entity, Needs, out actor.Needs) ||
                    !World.TryRead(entity, Activity, out actor.Activity) ||
                    !World.TryRead(entity, Target, out actor.Target) ||
                    !World.TryRead(entity, Cell, out actor.Cell))
                    throw new InvalidOperationException($"Alive actor {id} lost state.");
            }
            destination.Actors[id] = actor;
        }

        for (int item = 0; item < ItemEntities.Length; item++)
        {
            Entity entity = ItemEntities[item];
            if (!World.TryRead(entity, Position, out Position position) ||
                !World.TryRead(entity, ItemState, out WorldItemState state))
                throw new InvalidOperationException($"Item {item} lost state.");
            destination.Items[item] = new ItemSnapshot { Position = position, Value = state.Value };
        }

        for (uint local = 0; local < unchecked((uint)Profile.TotalCapacity); local++)
        {
            if (!World.TryGetEntity(local, out Entity entity) || !World.Has(entity, Effect))
                continue;
            if (!World.TryRead(entity, Effect, out EffectIdentity identity) ||
                !World.TryRead(entity, Position, out Position position) ||
                !World.TryRead(entity, Velocity, out Velocity velocity) ||
                !World.TryRead(entity, Lifetime, out Lifetime lifetime))
                throw new InvalidOperationException("Effect lost a required component.");
            if ((uint)identity.Slot >= unchecked((uint)destination.Effects.Length))
                throw new InvalidOperationException("Effect slot escaped the fixed logical range.");
            if (_effectSeen[identity.Slot])
            {
                throw new InvalidOperationException(
                    $"More than one live ECS entity owns effect slot {identity.Slot}.");
            }
            _effectSeen[identity.Slot] = true;
            destination.Effects[identity.Slot] = new EffectSnapshot
            {
                Active = true,
                Generation = identity.Generation,
                Position = position,
                Velocity = velocity,
                Lifetime = lifetime.Remaining,
            };
        }
        destination.ComputeHash();
        return destination;
    }

    internal RunMetrics Run(SemanticOracle oracle)
    {
        var oracleSnapshot = new SimulationSnapshot(Profile);
        oracle.CopyTo(oracleSnapshot);
        SimulationSnapshot ecsSnapshot = new(Profile);
        string? initialDifference = Capture(ecsSnapshot).FirstDifference(oracleSnapshot);
        if (initialDifference is not null)
            throw new InvalidOperationException($"Initial semantic mismatch: {initialDifference}");

        if (_options.DeferredValidation)
        {
            ulong firstWarmupTick = World.TickCount;
            for (int warmup = 0; warmup < _options.WarmupTicks; warmup++)
            {
                ulong tick = World.TickCount;
                World.Tick(1d / 60d);
                ConsumeLifecycle(tick);
            }
            for (int warmup = 0; warmup < _options.WarmupTicks; warmup++)
                oracle.Step(firstWarmupTick + unchecked((ulong)warmup));
            if (_options.WarmupTicks != 0)
            {
                Validate(oracle, ecsSnapshot, oracleSnapshot,
                    firstWarmupTick + unchecked((ulong)_options.WarmupTicks) - 1);
            }
        }
        else
        {
            for (int warmup = 0; warmup < _options.WarmupTicks; warmup++)
            {
                ulong tick = World.TickCount;
                World.Tick(1d / 60d);
                ConsumeLifecycle(tick);
                oracle.Step(tick);
                if ((warmup + 1) % _options.ValidationEvery == 0)
                    Validate(oracle, ecsSnapshot, oracleSnapshot, tick);
            }
        }

        long[] samples = new long[_options.MeasuredTicks];
        SystemRuntimeSnapshot[] systemsBefore = new SystemRuntimeSnapshot[World.SystemCount];
        SystemRuntimeSnapshot[] systemsAfter = new SystemRuntimeSnapshot[World.SystemCount];
        int systemsBeforeCount = World.CopySystemRuntimeSnapshots(systemsBefore);
        Counters.Reset();
        RandomOperations.Reset();
        oracle.ResetMetrics();
        PresentationObserver.ResetMetrics();
        ReplicationObserver.ResetMetrics();
        PersistenceObserver.ResetMetrics();
        // Align every independent lifecycle cursor to the measured interval.
        // Cadenced consumers may otherwise retain a warmup-only suffix after
        // their counters and the semantic oracle have been reset.
        ConsumeLifecycle(World.TickCount, force: true);
        PresentationLifecycle.ResetMetrics();
        ReplicationLifecycle.ResetMetrics();
        PersistenceLifecycle.ResetMetrics();
        long callerAllocatedBefore = GC.GetAllocatedBytesForCurrentThread();
        int gen0Before = GC.CollectionCount(0);
        int gen1Before = GC.CollectionCount(1);
        int gen2Before = GC.CollectionCount(2);
        SchedulerRuntimeSnapshot schedulerBefore = World.GetSchedulerRuntimeSnapshot();
        long ecsTickCallerAllocated = 0;
        ulong firstMeasuredTick = World.TickCount;
        for (int measured = 0; measured < samples.Length; measured++)
        {
            ulong tick = World.TickCount;
            long tickAllocationBefore = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            World.Tick(1d / 60d);
            samples[measured] = Stopwatch.GetTimestamp() - start;
            ecsTickCallerAllocated += GC.GetAllocatedBytesForCurrentThread() - tickAllocationBefore;
            ConsumeLifecycle(tick);
            if (!_options.DeferredValidation)
            {
                oracle.Step(tick);
                if ((measured + 1) % _options.ValidationEvery == 0)
                    Validate(oracle, ecsSnapshot, oracleSnapshot, tick);
            }
        }
        long callerAllocatedAfter = GC.GetAllocatedBytesForCurrentThread();
        SchedulerRuntimeSnapshot schedulerAfter = World.GetSchedulerRuntimeSnapshot();
        int systemsAfterCount = World.CopySystemRuntimeSnapshots(systemsAfter);
        if (systemsAfterCount != systemsBeforeCount)
            throw new InvalidOperationException("The sealed system graph changed during measurement.");
        if (_options.DeferredValidation)
        {
            for (int measured = 0; measured < samples.Length; measured++)
                oracle.Step(firstMeasuredTick + unchecked((ulong)measured));
        }
        ConsumeLifecycle(World.TickCount, force: true);
        Validate(oracle, ecsSnapshot, oracleSnapshot, World.TickCount == 0 ? 0 : World.TickCount - 1);
        if (Counters.CommandsAttempted !=
            schedulerAfter.MergedCommandCount - schedulerBefore.MergedCommandCount)
        {
            throw new InvalidOperationException(
                "Logical structural command attempts did not match scheduler-merged commands.");
        }
        if (Counters.ActorDeaths != oracle.ActorDeaths ||
            Counters.ActorRespawns != oracle.ActorRespawns ||
            Counters.EffectsCreated != oracle.EffectsCreated ||
            Counters.EffectsDestroyed != oracle.EffectsDestroyed)
        {
            throw new InvalidOperationException(
                "ECS structural logical counters diverged from the independent oracle.");
        }
        if (World.AliveCount != oracleSnapshot.ExpectedAliveEntityCount)
        {
            throw new InvalidOperationException(
                "Final ECS alive population diverged from the canonical oracle population.");
        }
        if (PresentationLifecycle.Hash != oracle.LifecycleHash ||
            ReplicationLifecycle.Hash != oracle.LifecycleHash ||
            PersistenceLifecycle.Hash != oracle.LifecycleHash)
        {
            throw new InvalidOperationException(
                $"Lifecycle oracle mismatch: expected=0x{oracle.LifecycleHash:X16}, " +
                $"presentation=0x{PresentationLifecycle.Hash:X16}, " +
                $"replication=0x{ReplicationLifecycle.Hash:X16}, " +
                $"persistence=0x{PersistenceLifecycle.Hash:X16}, " +
                $"expected-events={oracle.LifecycleEvents}, actual-events={PresentationLifecycle.TotalRecords}, " +
                $"actual=(C{PresentationLifecycle.Created},D{PresentationLifecycle.Destroyed}," +
                $"A{PresentationLifecycle.ComponentAdded},R{PresentationLifecycle.ComponentRemoved}).");
        }
        if (PresentationLifecycle.OrderedHash != ReplicationLifecycle.OrderedHash ||
            PresentationLifecycle.OrderedHash != PersistenceLifecycle.OrderedHash)
        {
            throw new InvalidOperationException("Independent lifecycle cursors observed different ordered streams.");
        }

        return RunMetrics.Create(
            samples,
            callerAllocatedAfter - callerAllocatedBefore,
            ecsTickCallerAllocated,
            GC.CollectionCount(0) - gen0Before,
            GC.CollectionCount(1) - gen1Before,
            GC.CollectionCount(2) - gen2Before,
            schedulerBefore,
            schedulerAfter,
            systemsBefore,
            systemsAfter,
            systemsAfterCount,
            Counters,
            RandomOperations,
            PresentationObserver,
            ReplicationObserver,
            PersistenceObserver,
            PresentationLifecycle,
            ReplicationLifecycle,
            PersistenceLifecycle,
            oracle.LifecycleHash,
            oracle.LifecycleEvents,
            ecsSnapshot.Hash,
            oracleSnapshot.Hash,
            Profile,
            World,
            new ContentIntegrationMetrics(
                ContentCompileMicroseconds,
                BootstrapMicroseconds,
                Content.GetSnapshot()),
            CaptureArchetypeMetrics(),
            _options.DeferredValidation);
    }

    private IReadOnlyList<ArchetypeMetrics> CaptureArchetypeMetrics()
    {
        var metrics = new List<ArchetypeMetrics>(10);
        for (int tier = 0; tier < Tiers.Length; tier++)
        {
            Add($"tier{tier}.alive", Tiers[tier].Alive);
            Add($"tier{tier}.dead", Tiers[tier].Dead);
        }
        Add("effects", EffectArchetype);
        Add("items", ItemArchetype);
        return metrics;

        void Add(string name, ArchetypeHandle archetype)
        {
            ArchetypeChunkSnapshot snapshot = World.GetArchetypeSnapshot(archetype);
            metrics.Add(new ArchetypeMetrics(
                name,
                snapshot.EntityCount,
                snapshot.RowCapacity,
                snapshot.MaximumChunks,
                snapshot.AcquiredChunkCount,
                snapshot.OccupiedChunkCount,
                snapshot.FullChunkCount,
                snapshot.PartialChunkCount,
                snapshot.EmptyChunkCount,
                snapshot.UnallocatedChunkCount,
                snapshot.UnusedAcquiredRows,
                snapshot.TotalMetadataBytes));
        }
    }

    private void Validate(
        SemanticOracle oracle,
        SimulationSnapshot ecsSnapshot,
        SimulationSnapshot oracleSnapshot,
        ulong tick)
    {
        oracle.CopyTo(oracleSnapshot);
        Capture(ecsSnapshot);
        string? difference = ecsSnapshot.FirstDifference(oracleSnapshot);
        if (difference is not null)
            throw new InvalidOperationException($"Semantic mismatch after tick {tick}: {difference}");
    }

    public void Dispose() => World.Dispose();

    private static ArchetypeHandle RegisterEffectArchetype(EcsWorld world, SimulationProfile profile)
    {
        ComponentId[] signature =
        [
            SimulationSchema.GetComponentId<EffectIdentity>(),
            SimulationSchema.GetComponentId<Position>(),
            SimulationSchema.GetComponentId<Velocity>(),
            SimulationSchema.GetComponentId<Lifetime>(),
        ];
        Array.Sort(signature);
        return world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            SimulationCapacity.RequiredChunks(
                world.Schema, signature, ChunkLayoutOptions.KiB16, profile.EffectSlots));
    }

    private static ArchetypeHandle RegisterItemArchetype(EcsWorld world, SimulationProfile profile)
    {
        ComponentId[] signature =
        [
            SimulationSchema.GetComponentId<LogicalIdentity>(),
            SimulationSchema.GetComponentId<Position>(),
            SimulationSchema.GetComponentId<WorldItemState>(),
        ];
        Array.Sort(signature);
        return world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            SimulationCapacity.RequiredChunks(
                world.Schema, signature, ChunkLayoutOptions.KiB16, profile.ItemCount));
    }

    private static CompiledPrefabCatalog CompileContent(EcsWorld world, SimulationProfile profile)
    {
        var content = new ContentCatalogBuilder(world.Schema);
        DefineTier<Tier0Marker>(content, 0);
        DefineTier<Tier1Marker>(content, 1);
        DefineTier<Tier2Marker>(content, 2);
        DefineTier<Tier3Marker>(content, 3);

        PrefabBuilder effect = content.Define(new PrefabKey("simulation/effects/temporary"));
        effect.DisplayName("Temporary simulation effect");
        effect.With<EffectIdentity>();
        effect.With<Position>();
        effect.With<Velocity>();
        effect.With(new Lifetime { Remaining = profile.EffectLifetime });

        PrefabBuilder item = content.Define(new PrefabKey("simulation/items/world-item"));
        item.DisplayName("World item");
        item.With<LogicalIdentity>();
        item.With<Position>();
        item.With<WorldItemState>();

        return content.Compile(
            world,
            new ContentCompilationOptions(ChunkLayoutOptions.KiB16, maximumChunksPerNewArchetype: 1));
    }

    private static void DefineTier<TMarker>(ContentCatalogBuilder content, int tier)
        where TMarker : unmanaged, IComponent<TMarker>
    {
        PrefabBuilder alive = content.Define(new PrefabKey($"simulation/actors/tier-{tier}/alive"));
        alive.DisplayName($"Tier {tier} actor");
        alive.With<LogicalIdentity>();
        alive.With<Position>();
        alive.With<Velocity>();
        alive.With<Needs>();
        alive.With<Activity>();
        alive.With<Health>();
        alive.With<Cooldown>();
        alive.With<Target>();
        alive.With<SpatialCell>();
        alive.With<TMarker>();

        PrefabBuilder dead = content.Define(new PrefabKey($"simulation/actors/tier-{tier}/dead"));
        dead.DisplayName($"Dead tier {tier} actor");
        dead.With<LogicalIdentity>();
        dead.With<Position>();
        dead.With<Health>();
        dead.With<Cooldown>();
        dead.With<DeadState>();
        dead.With<TMarker>();
    }

    internal void SetTierTransitions(int tier, ArchetypeHandle alive, ArchetypeHandle dead)
    {
        _toDead[tier] = World.PrepareTransition(alive, dead);
        _toAlive[tier] = World.PrepareTransition(dead, alive);
    }
}

internal static class SimulationCapacity
{
    internal static int RequiredChunks(
        ComponentSchema schema,
        ReadOnlySpan<ComponentId> signature,
        ChunkLayoutOptions options,
        int expectedPopulation,
        int spareChunks = 2)
    {
        Span<ComponentColumnLayout> columns = stackalloc ComponentColumnLayout[signature.Length];
        ChunkLayoutPlan layout = ChunkLayoutPlanner.Plan(schema, signature, options, columns);
        var capacity = new ArchetypeCapacityPlan(layout, expectedPopulation);
        return checked(Math.Max(1, capacity.RequiredChunkCount + spareChunks));
    }
}

internal abstract class TierBindingBase
{
    protected TierBindingBase(SimulationWorld owner, int tier)
    {
        Owner = owner;
        Tier = tier;
    }

    protected SimulationWorld Owner { get; }
    internal int Tier { get; }
    internal ArchetypeHandle Alive { get; set; }
    internal ArchetypeHandle Dead { get; set; }
    internal PrefabHandle AlivePrefab { get; set; }
    internal PrefabHandle DeadPrefab { get; set; }
    internal ComponentId MarkerId { get; set; }
    internal abstract void RegisterSystems();
}

internal sealed class TierBinding<TMarker> : TierBindingBase
    where TMarker : unmanaged, IComponent<TMarker>
{
    internal TierBinding(SimulationWorld owner, int tier)
        : base(owner, tier)
    {
        MarkerId = SimulationSchema.GetComponentId<TMarker>();
        ComponentId[] aliveSignature = Sorted(
                SimulationSchema.GetComponentId<LogicalIdentity>(),
                SimulationSchema.GetComponentId<Position>(),
                SimulationSchema.GetComponentId<Velocity>(),
                SimulationSchema.GetComponentId<Needs>(),
                SimulationSchema.GetComponentId<Activity>(),
                SimulationSchema.GetComponentId<Health>(),
                SimulationSchema.GetComponentId<Cooldown>(),
                SimulationSchema.GetComponentId<Target>(),
                SimulationSchema.GetComponentId<SpatialCell>(),
                MarkerId);
        Alive = owner.World.RegisterArchetype(
            aliveSignature,
            ChunkLayoutOptions.KiB16,
            SimulationCapacity.RequiredChunks(
                owner.World.Schema,
                aliveSignature,
                ChunkLayoutOptions.KiB16,
                owner.Profile.TierCounts[tier]));
        ComponentId[] deadSignature = Sorted(
                SimulationSchema.GetComponentId<LogicalIdentity>(),
                SimulationSchema.GetComponentId<Position>(),
                SimulationSchema.GetComponentId<Health>(),
                SimulationSchema.GetComponentId<Cooldown>(),
                SimulationSchema.GetComponentId<DeadState>(),
                MarkerId);
        Dead = owner.World.RegisterArchetype(
            deadSignature,
            ChunkLayoutOptions.KiB16,
            SimulationCapacity.RequiredChunks(
                owner.World.Schema,
                deadSignature,
                ChunkLayoutOptions.KiB16,
                owner.Profile.TierCounts[tier]));
        owner.SetTierTransitions(tier, Alive, Dead);

        EcsQueryFilter filter = owner.World.PrepareQueryFilter([MarkerId], []);
        NeedsPlan = owner.World.PrepareReadReadWriteJob<LogicalIdentity, Activity, Needs>(filter);
        SteeringPlan = owner.World.PrepareReadReadReadWriteJob<LogicalIdentity, Needs, Activity, Velocity>(filter);
        MovementPlan = owner.World.PrepareReadReadWriteJob<LogicalIdentity, Velocity, Position>(filter);
        SpatialPlan = owner.World.PrepareReadReadWriteJob<LogicalIdentity, Position, SpatialCell>(filter);
    }

    internal ReadReadWriteJobPlan<LogicalIdentity, Activity, Needs> NeedsPlan { get; }
    internal ReadReadReadWriteJobPlan<LogicalIdentity, Needs, Activity, Velocity> SteeringPlan { get; }
    internal ReadReadWriteJobPlan<LogicalIdentity, Velocity, Position> MovementPlan { get; }
    internal ReadReadWriteJobPlan<LogicalIdentity, Position, SpatialCell> SpatialPlan { get; }

    internal override void RegisterSystems()
    {
        int period = Owner.Profile.TierPeriods[Tier];
        var schedule = new SystemSchedule(true, period, 0);
        SystemDefinition needsDefinition = new(
            $"simulation.slice0.tier{Tier}.needs",
            SystemStage.Input,
            schedule,
            executionPolicy: SystemExecutionPolicy.Parallel,
            parallelRowThreshold: 1,
            minimumRowsPerPartition: 64);
        NeedsSystem = Owner.World.RegisterReadReadWriteSystem<LogicalIdentity, Activity, Needs,
            NeedsRuntime<TMarker>, NeedsJob<TMarker>>(needsDefinition, NeedsPlan, new NeedsRuntime<TMarker>());

        SystemDefinition steeringDefinition = new(
            $"simulation.slice0.tier{Tier}.steering",
            SystemStage.Movement,
            schedule,
            executionPolicy: SystemExecutionPolicy.Parallel,
            requires: [NeedsSystem],
            parallelRowThreshold: 1,
            minimumRowsPerPartition: 64);
        SteeringSystem = Owner.World.RegisterReadReadReadWriteSystem<LogicalIdentity, Needs, Activity, Velocity,
            SteeringRuntime<TMarker>, SteeringJob<TMarker>>(steeringDefinition, SteeringPlan,
            new SteeringRuntime<TMarker>());

        SystemDefinition movementDefinition = new(
            $"simulation.slice0.tier{Tier}.movement",
            SystemStage.Movement,
            schedule,
            executionPolicy: SystemExecutionPolicy.Parallel,
            requires: [SteeringSystem],
            parallelRowThreshold: 1,
            minimumRowsPerPartition: 64);
        MovementSystem = Owner.World.RegisterReadReadWriteSystem<LogicalIdentity, Velocity, Position,
            MovementRuntime<TMarker>, MovementJob<TMarker>>(movementDefinition, MovementPlan,
            new MovementRuntime<TMarker>());

        SystemDefinition spatialDefinition = new(
            $"simulation.slice0.tier{Tier}.spatial-cell",
            SystemStage.SpatialIndex,
            schedule,
            executionPolicy: SystemExecutionPolicy.Parallel,
            requires: [MovementSystem],
            parallelRowThreshold: 1,
            minimumRowsPerPartition: 64);
        SpatialSystem = Owner.World.RegisterReadReadWriteSystem<LogicalIdentity, Position, SpatialCell,
            SpatialRuntime<TMarker>, SpatialJob<TMarker>>(spatialDefinition, SpatialPlan,
            new SpatialRuntime<TMarker>());
    }

    internal SystemId NeedsSystem { get; private set; }
    internal SystemId SteeringSystem { get; private set; }
    internal SystemId MovementSystem { get; private set; }
    internal SystemId SpatialSystem { get; private set; }

    private static ComponentId[] Sorted(params ComponentId[] values)
    {
        Array.Sort(values);
        return values;
    }
}

internal sealed class NeedsRuntime<TMarker> : ISystemRuntime<NeedsJob<TMarker>>
    where TMarker : unmanaged, IComponent<TMarker>
{
    public NeedsJob<TMarker> Prepare(in SystemPrepareContext context) => new();
}

internal struct NeedsJob<TMarker> : IScheduledReadReadWriteChunkJob<LogicalIdentity, Activity, Needs>
    where TMarker : unmanaged, IComponent<TMarker>
{
    public void Execute(in SystemJobContext context,
        in ReadReadWriteComponentChunk<LogicalIdentity, Activity, Needs> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
        {
            LogicalIdentity identity = chunk.ReadComponents1[row];
            Activity activity = chunk.ReadComponents2[row];
            ref Needs needs = ref chunk.WriteComponents[row];
            int activityMix = unchecked(identity.Value + activity.Value + (int)context.Tick);
            int fatigueMix = unchecked(identity.Value + (int)context.Tick);
            needs.Hunger = unchecked((needs.Hunger + 1 + (activityMix & 3)) % 1_000);
            needs.Fatigue = unchecked((needs.Fatigue + 2 + (fatigueMix & 1)) % 1_000);
        }
    }

}

internal sealed class SteeringRuntime<TMarker> : ISystemRuntime<SteeringJob<TMarker>>
    where TMarker : unmanaged, IComponent<TMarker>
{
    public SteeringJob<TMarker> Prepare(in SystemPrepareContext context) => new();
}

internal struct SteeringJob<TMarker> : IScheduledReadReadReadWriteChunkJob<LogicalIdentity, Needs, Activity, Velocity>
    where TMarker : unmanaged, IComponent<TMarker>
{
    public void Execute(in SystemJobContext context,
        in ReadReadReadWriteComponentChunk<LogicalIdentity, Needs, Activity, Velocity> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
        {
            int id = chunk.ReadComponents1[row].Value;
            chunk.WriteComponents[row] = SimulationMath.Steering(
                id, chunk.ReadComponents2[row], chunk.ReadComponents3[row], context.Tick);
        }
    }

}

internal sealed class MovementRuntime<TMarker> : ISystemRuntime<MovementJob<TMarker>>
    where TMarker : unmanaged, IComponent<TMarker>
{
    public MovementJob<TMarker> Prepare(in SystemPrepareContext context) => new();
}

internal struct MovementJob<TMarker> : IScheduledReadReadWriteChunkJob<LogicalIdentity, Velocity, Position>
    where TMarker : unmanaged, IComponent<TMarker>
{
    public void Execute(in SystemJobContext context,
        in ReadReadWriteComponentChunk<LogicalIdentity, Velocity, Position> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
        {
            Velocity velocity = chunk.ReadComponents2[row];
            ref Position position = ref chunk.WriteComponents[row];
            position.X = unchecked(position.X + velocity.X);
            position.Y = unchecked(position.Y + velocity.Y);
        }
    }
}

internal sealed class SpatialRuntime<TMarker> : ISystemRuntime<SpatialJob<TMarker>>
    where TMarker : unmanaged, IComponent<TMarker>
{
    public SpatialJob<TMarker> Prepare(in SystemPrepareContext context) => new();
}

internal struct SpatialJob<TMarker> : IScheduledReadReadWriteChunkJob<LogicalIdentity, Position, SpatialCell>
    where TMarker : unmanaged, IComponent<TMarker>
{
    public void Execute(in SystemJobContext context,
        in ReadReadWriteComponentChunk<LogicalIdentity, Position, SpatialCell> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
            chunk.WriteComponents[row] = SimulationMath.Cell(chunk.ReadComponents2[row]);
    }
}

internal sealed class StructuralCounters
{
    internal long ActorDeaths;
    internal long ActorRespawns;
    internal long EffectsCreated;
    internal long EffectsDestroyed;
    internal long CommandsAttempted;

    internal void Reset()
    {
        ActorDeaths = 0;
        ActorRespawns = 0;
        EffectsCreated = 0;
        EffectsDestroyed = 0;
        CommandsAttempted = 0;
    }
}

internal enum LifecycleDomain : byte
{
    Actor = 1,
    Effect = 2,
    Item = 3,
}

internal static class LifecycleHashing
{
    internal const ulong Offset = 1469598103934665603UL;

    internal static ulong Add(
        ulong hash,
        LifecycleKind kind,
        LifecycleDomain domain,
        int logicalId,
        int logicalGeneration,
        ComponentId component)
    {
        ulong fact = Offset;
        fact = Mix(fact, (uint)kind);
        fact = Mix(fact, (uint)domain);
        fact = Mix(fact, unchecked((uint)logicalId));
        fact = Mix(fact, unchecked((uint)logicalGeneration));
        fact = Mix(fact, component.Value);
        // Independent oracle comparison canonicalizes away packed physical row
        // order while retaining every normalized logical fact and multiplicity.
        return unchecked(hash + RotateLeft(fact, logicalId & 63));
    }

    internal static ulong AddOrdered(
        ulong hash,
        LifecycleKind kind,
        LifecycleDomain domain,
        int logicalId,
        int logicalGeneration,
        ComponentId component)
    {
        hash = Mix(hash, (uint)kind);
        hash = Mix(hash, (uint)domain);
        hash = Mix(hash, unchecked((uint)logicalId));
        hash = Mix(hash, unchecked((uint)logicalGeneration));
        return Mix(hash, component.Value);
    }

    private static ulong Mix(ulong hash, uint value) =>
        unchecked((hash ^ value) * 1099511628211UL);

    private static ulong RotateLeft(ulong value, int count) =>
        (value << count) | (value >> ((64 - count) & 63));
}

internal sealed class LifecycleIdentityMap
{
    private readonly byte[] _domains;
    private readonly int[] _logicalIds;
    private readonly int[] _logicalGenerations;
    private readonly uint[] _entityGenerations;

    internal LifecycleIdentityMap(int capacity)
    {
        _domains = new byte[capacity];
        _logicalIds = new int[capacity];
        _logicalGenerations = new int[capacity];
        _entityGenerations = new uint[capacity];
    }

    internal void RegisterActor(Entity entity, int logicalId) =>
        Register(entity, LifecycleDomain.Actor, logicalId, 0);

    internal void RegisterEffect(Entity entity, int slot, int generation) =>
        Register(entity, LifecycleDomain.Effect, slot, generation);

    internal void RegisterItem(Entity entity, int logicalId) =>
        Register(entity, LifecycleDomain.Item, logicalId, 0);

    internal void Register(
        Entity entity,
        LifecycleDomain domain,
        int logicalId,
        int logicalGeneration)
    {
        int index = checked((int)entity.Index);
        _domains[index] = (byte)domain;
        _logicalIds[index] = logicalId;
        _logicalGenerations[index] = logicalGeneration;
        _entityGenerations[index] = entity.Generation;
    }

    internal bool TryResolve(
        Entity entity,
        out LifecycleDomain domain,
        out int logicalId,
        out int logicalGeneration)
    {
        uint index = entity.Index;
        if (index >= unchecked((uint)_domains.Length) ||
            _domains[index] == 0 ||
            _entityGenerations[index] != entity.Generation)
        {
            domain = default;
            logicalId = 0;
            logicalGeneration = 0;
            return false;
        }

        domain = (LifecycleDomain)_domains[index];
        logicalId = _logicalIds[index];
        logicalGeneration = _logicalGenerations[index];
        return true;
    }

    internal void Clear(Entity entity)
    {
        uint index = entity.Index;
        if (index < unchecked((uint)_domains.Length) &&
            _entityGenerations[index] == entity.Generation)
        {
            _domains[index] = 0;
            _entityGenerations[index] = 0;
        }
    }
}

internal sealed class LifecycleObserver
{
    private readonly int _period;
    private readonly LifecycleRecord[] _scratch = new LifecycleRecord[2_048];
    private readonly LifecycleIdentityMap _identities;

    internal LifecycleObserver(int period, int entityCapacity)
    {
        _period = period;
        _identities = new LifecycleIdentityMap(entityCapacity);
    }

    internal void RegisterActor(Entity entity, int logicalId) =>
        _identities.RegisterActor(entity, logicalId);

    internal void RegisterEffect(Entity entity, int slot, int generation) =>
        _identities.RegisterEffect(entity, slot, generation);

    internal void RegisterItem(Entity entity, int logicalId) =>
        _identities.RegisterItem(entity, logicalId);

    internal LifecycleCursor Cursor;
    internal long TotalRecords;
    internal long Calls;
    internal int MaximumRecordsPerConsume;
    internal ulong MaximumLag;
    internal long ResyncRequiredCount;
    internal long Created;
    internal long Destroyed;
    internal long ComponentAdded;
    internal long ComponentRemoved;
    internal ulong Hash = LifecycleHashing.Offset;
    internal ulong OrderedHash = LifecycleHashing.Offset;
    internal long DrainTimestampTicks;

    internal void ResetMetrics()
    {
        TotalRecords = 0;
        Calls = 0;
        MaximumRecordsPerConsume = 0;
        MaximumLag = 0;
        ResyncRequiredCount = 0;
        Created = 0;
        Destroyed = 0;
        ComponentAdded = 0;
        ComponentRemoved = 0;
        Hash = LifecycleHashing.Offset;
        OrderedHash = LifecycleHashing.Offset;
        DrainTimestampTicks = 0;
    }

    internal void ConsumeIfDue(
        EcsWorld world,
        in ComponentLookup<EffectIdentity> effectLookup,
        ulong tick,
        bool force)
    {
        if (!force && tick % unchecked((ulong)_period) != 0)
            return;

        long drainStart = Stopwatch.GetTimestamp();
        int consumed = 0;
        while (true)
        {
            LifecycleReadResult result = world.ReadLifecycle(ref Cursor, _scratch);
            if (result.Status == LifecycleReadStatus.ResyncRequired)
            {
                ResyncRequiredCount++;
                throw new InvalidOperationException(
                    $"Lifecycle consumer period {_period} exceeded retained history; " +
                    $"cursor={Cursor.NextSequence}, oldest={result.OldestRetainedSequence}, " +
                    $"next={result.NextPublishedSequence}.");
            }
            if (result.Status == LifecycleReadStatus.NoData)
                break;

            ulong lag = unchecked((ulong)result.Count) + result.RemainingCount;
            if (lag > MaximumLag)
                MaximumLag = lag;
            for (int index = 0; index < result.Count; index++)
                ConsumeRecord(world, effectLookup, _scratch[index]);
            consumed += result.Count;
            if (result.RemainingCount == 0 || result.Count == 0)
                break;
        }

        Calls++;
        MaximumRecordsPerConsume = Math.Max(MaximumRecordsPerConsume, consumed);
        DrainTimestampTicks += Stopwatch.GetTimestamp() - drainStart;
    }

    private void ConsumeRecord(
        EcsWorld world,
        in ComponentLookup<EffectIdentity> effectLookup,
        in LifecycleRecord record)
    {
        if (!_identities.TryResolve(
                record.Entity,
                out LifecycleDomain domain,
                out int logicalId,
                out int logicalGeneration))
        {
            if (record.Kind != LifecycleKind.Created ||
                !world.TryRead(record.Entity, effectLookup, out EffectIdentity effect))
            {
                throw new InvalidOperationException(
                    $"Lifecycle record {record.Sequence} could not be normalized to a logical identity.");
            }
            domain = LifecycleDomain.Effect;
            logicalId = effect.Slot;
            logicalGeneration = effect.Generation;
            _identities.Register(record.Entity, domain, logicalId, logicalGeneration);
        }

        Hash = LifecycleHashing.Add(
            Hash,
            record.Kind,
            domain,
            logicalId,
            logicalGeneration,
            record.ComponentId);
        OrderedHash = LifecycleHashing.AddOrdered(
            OrderedHash,
            record.Kind,
            domain,
            logicalId,
            logicalGeneration,
            record.ComponentId);
        TotalRecords++;
        switch (record.Kind)
        {
            case LifecycleKind.Created: Created++; break;
            case LifecycleKind.Destroyed:
                Destroyed++;
                _identities.Clear(record.Entity);
                break;
            case LifecycleKind.ComponentAdded: ComponentAdded++; break;
            case LifecycleKind.ComponentRemoved: ComponentRemoved++; break;
            default: throw new InvalidOperationException("Unknown lifecycle record kind.");
        }
    }
}

internal sealed class RandomOperationCounters
{
    internal long TargetAttempts;
    internal long TargetWrites;
    internal long TargetDeadSkips;
    internal long DamageAttempts;
    internal long DamageAliveSources;
    internal long DamageAliveVictims;
    internal long DamageWrites;

    internal void Reset()
    {
        TargetAttempts = 0;
        TargetWrites = 0;
        TargetDeadSkips = 0;
        DamageAttempts = 0;
        DamageAliveSources = 0;
        DamageAliveVictims = 0;
        DamageWrites = 0;
    }
}

internal sealed class TargetRuntime : IRangeSystemRuntime<TargetJob>
{
    private readonly Entity[] _actors;
    private readonly int _count;
    private readonly bool _partitionSafe;
    private readonly ComponentLookup<Needs> _needs;
    private readonly ComponentLookup<Target> _target;
    private readonly RandomOperationCounters _counters;

    internal TargetRuntime(Entity[] actors, int count, bool partitionSafe,
        ComponentLookup<Needs> needs, ComponentLookup<Target> target,
        RandomOperationCounters counters) =>
        (_actors, _count, _partitionSafe, _needs, _target, _counters) =
        (actors, count, partitionSafe, needs, target, counters);

    public int GetRangeCount(in SystemPrepareContext context) => _count;

    public TargetJob Prepare(in SystemPrepareContext context) =>
        new(_actors, _partitionSafe, _needs, _target);

    public void Complete(in SystemCompleteContext context, ReadOnlySpan<TargetJob> participantResults)
    {
        for (int index = 0; index < participantResults.Length; index++)
        {
            _counters.TargetAttempts += participantResults[index].Attempts;
            _counters.TargetWrites += participantResults[index].Writes;
            _counters.TargetDeadSkips += participantResults[index].DeadSkips;
        }
    }
}

internal struct TargetJob : IRangeJob
{
    private readonly Entity[] _actors;
    private readonly bool _partitionSafe;
    private readonly ComponentLookup<Needs> _needs;
    private readonly ComponentLookup<Target> _target;
    internal int Attempts;
    internal int Writes;
    internal int DeadSkips;

    internal TargetJob(Entity[] actors, bool partitionSafe, ComponentLookup<Needs> needs,
        ComponentLookup<Target> target) =>
        (_actors, _partitionSafe, _needs, _target, Attempts, Writes, DeadSkips) =
        (actors, partitionSafe, needs, target, 0, 0, 0);

    public void Execute(in SystemJobContext context, int start, int count)
    {
        for (int operation = start; operation < start + count; operation++)
        {
            Attempts++;
            int source = _partitionSafe
                ? operation
                : unchecked(((int)context.Tick * 17 + operation) % _actors.Length);
            Entity entity = _actors[source];
            if (!context.Has(entity, _needs))
            {
                DeadSkips++;
                continue;
            }
            int target = unchecked((source + 97 + (int)context.Tick * 3) % _actors.Length);
            if (!context.TryWrite(entity, _target, new Target { LogicalId = target }))
                throw new InvalidOperationException("Target acquisition lost an alive actor.");
            Writes++;
        }
    }
}

internal sealed class DamageRuntime : IRangeSystemRuntime<DamageJob>
{
    private readonly Entity[] _actors;
    private readonly int _count;
    private readonly ComponentLookup<Needs> _needs;
    private readonly ComponentLookup<Target> _target;
    private readonly ComponentLookup<Health> _health;
    private readonly RandomOperationCounters _counters;

    internal DamageRuntime(Entity[] actors, int count,
        ComponentLookup<Needs> needs, ComponentLookup<Target> target,
        ComponentLookup<Health> health, RandomOperationCounters counters) =>
        (_actors, _count, _needs, _target, _health, _counters) =
        (actors, count, needs, target, health, counters);

    public int GetRangeCount(in SystemPrepareContext context) => _count;

    public DamageJob Prepare(in SystemPrepareContext context) =>
        new(_actors, _needs, _target, _health);

    public void Complete(in SystemCompleteContext context, ReadOnlySpan<DamageJob> participantResults)
    {
        for (int index = 0; index < participantResults.Length; index++)
        {
            _counters.DamageAttempts += participantResults[index].Attempts;
            _counters.DamageAliveSources += participantResults[index].AliveSources;
            _counters.DamageAliveVictims += participantResults[index].AliveVictims;
            _counters.DamageWrites += participantResults[index].Writes;
        }
    }
}

internal struct DamageJob : IRangeJob
{
    private readonly Entity[] _actors;
    private readonly ComponentLookup<Needs> _needs;
    private readonly ComponentLookup<Target> _target;
    private readonly ComponentLookup<Health> _health;
    internal int Attempts;
    internal int AliveSources;
    internal int AliveVictims;
    internal int Writes;

    internal DamageJob(Entity[] actors, ComponentLookup<Needs> needs,
        ComponentLookup<Target> target, ComponentLookup<Health> health) =>
        (_actors, _needs, _target, _health, Attempts, AliveSources, AliveVictims, Writes) =
        (actors, needs, target, health, 0, 0, 0, 0);

    public void Execute(in SystemJobContext context, int start, int count)
    {
        for (int operation = start; operation < start + count; operation++)
        {
            Attempts++;
            Entity source = _actors[operation];
            if (!context.Has(source, _needs) ||
                !context.TryRead(source, _target, out Target target))
                continue;
            AliveSources++;
            Entity victim = _actors[target.LogicalId];
            if (!context.Has(victim, _needs) ||
                !context.TryRead(victim, _health, out Health health))
                continue;
            AliveVictims++;
            health.Value -= 1 + ((int)context.Tick + operation & 3);
            if (!context.TryWrite(victim, _health, health))
                throw new InvalidOperationException("Damage lost a target health write.");
            Writes++;
        }
    }
}

internal sealed class DeathRuntime : ISystemRuntime<DeathJob>
{
    private readonly int[] _tiers;
    private readonly ArchetypeTransition[] _transitions;
    private readonly int _respawnDelay;
    private readonly StructuralCounters _counters;

    internal DeathRuntime(int[] tiers, ArchetypeTransition[] transitions,
        int respawnDelay, StructuralCounters counters) =>
        (_tiers, _transitions, _respawnDelay, _counters) =
        (tiers, transitions, respawnDelay, counters);

    public DeathJob Prepare(in SystemPrepareContext context) =>
        new(_tiers, _transitions, _respawnDelay);

    public void Complete(in SystemCompleteContext context, ReadOnlySpan<DeathJob> participantResults)
    {
        for (int index = 0; index < participantResults.Length; index++)
        {
            _counters.ActorDeaths += participantResults[index].Deaths;
            _counters.CommandsAttempted += participantResults[index].Commands;
        }
    }
}

internal struct DeathJob :
    IScheduledTrackedReadReadReadWriteChunkJob<LogicalIdentity, Needs, Health, Cooldown>
{
    private readonly int[] _tiers;
    private readonly ArchetypeTransition[] _transitions;
    private readonly int _respawnDelay;
    internal int Deaths;
    internal int Commands;

    internal DeathJob(int[] tiers, ArchetypeTransition[] transitions, int respawnDelay) =>
        (_tiers, _transitions, _respawnDelay, Deaths, Commands) =
        (tiers, transitions, respawnDelay, 0, 0);

    public void Execute(in SystemJobContext context,
        in TrackedReadReadReadWriteComponentChunk<LogicalIdentity, Needs, Health, Cooldown> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
        {
            if (chunk.ReadComponents3[row].Value > 0)
                continue;
            int id = chunk.ReadComponents1[row].Value;
            chunk.Write.GetTracked(row) = new Cooldown { Value = _respawnDelay };
            if (!context.TryGetEntity(chunk.EntityIndices[row], out Entity entity))
                throw new InvalidOperationException("Death could not reconstruct its actor entity.");
            ArchetypeTransition transition = _transitions[_tiers[id]];
            if (!context.Commands.TryTransition(entity, transition))
                throw new InvalidOperationException("Death structural transition was rejected.");
            Deaths++;
            Commands++;
        }
    }
}

internal sealed class RespawnRuntime : ISystemRuntime<RespawnJob>
{
    private readonly int[] _tiers;
    private readonly ArchetypeTransition[] _transitions;
    private readonly SimulationProfile _profile;
    private readonly StructuralCounters _counters;

    internal RespawnRuntime(int[] tiers, ArchetypeTransition[] transitions,
        SimulationProfile profile, StructuralCounters counters) =>
        (_tiers, _transitions, _profile, _counters) =
        (tiers, transitions, profile, counters);

    public RespawnJob Prepare(in SystemPrepareContext context) =>
        new(_tiers, _transitions, _profile);

    public void Complete(in SystemCompleteContext context, ReadOnlySpan<RespawnJob> participantResults)
    {
        for (int index = 0; index < participantResults.Length; index++)
        {
            _counters.ActorRespawns += participantResults[index].Respawns;
            _counters.CommandsAttempted += participantResults[index].Commands;
        }
    }
}

internal struct RespawnJob :
    IScheduledTrackedReadReadWriteWriteChunkJob<LogicalIdentity, DeadState, Cooldown, Health>
{
    private readonly int[] _tiers;
    private readonly ArchetypeTransition[] _transitions;
    private readonly SimulationProfile _profile;
    internal int Respawns;
    internal int Commands;

    internal RespawnJob(int[] tiers, ArchetypeTransition[] transitions, SimulationProfile profile) =>
        (_tiers, _transitions, _profile, Respawns, Commands) =
        (tiers, transitions, profile, 0, 0);

    public void Execute(in SystemJobContext context,
        in TrackedReadReadWriteWriteComponentChunk<LogicalIdentity, DeadState, Cooldown, Health> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
        {
            ref Cooldown cooldown = ref chunk.Write1.GetTracked(row);
            if (cooldown.Value > 1)
            {
                cooldown.Value--;
                continue;
            }
            int id = chunk.ReadComponents1[row].Value;
            chunk.Write2.GetTracked(row) = SimulationMath.InitialHealth(id);
            cooldown = default;
            if (!context.TryGetEntity(chunk.EntityIndices[row], out Entity entity) ||
                !context.Commands.TryTransition(entity, _transitions[_tiers[id]]))
                throw new InvalidOperationException("Respawn structural transition was rejected.");
            Respawns++;
            Commands++;
        }
    }
}

internal sealed class EffectLifetimeRuntime : ISystemRuntime<EffectLifetimeJob>
{
    private readonly StructuralCounters _counters;
    internal EffectLifetimeRuntime(StructuralCounters counters) => _counters = counters;

    public EffectLifetimeJob Prepare(in SystemPrepareContext context) => new();

    public void Complete(in SystemCompleteContext context, ReadOnlySpan<EffectLifetimeJob> participantResults)
    {
        for (int index = 0; index < participantResults.Length; index++)
        {
            _counters.EffectsDestroyed += participantResults[index].Destroyed;
            _counters.CommandsAttempted += participantResults[index].Commands;
        }
    }
}

internal struct EffectLifetimeJob : IScheduledReadWriteChunkJob<EffectIdentity, Lifetime>
{
    internal int Destroyed;
    internal int Commands;

    public void Execute(in SystemJobContext context,
        in ReadWriteComponentChunk<EffectIdentity, Lifetime> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
        {
            ref Lifetime lifetime = ref chunk.WriteComponents[row];
            lifetime.Remaining--;
            if (lifetime.Remaining > 0)
                continue;
            if (!context.TryGetEntity(chunk.EntityIndices[row], out Entity entity) ||
                !context.Commands.TryDestroy(entity))
                throw new InvalidOperationException("Effect destruction was rejected.");
            Destroyed++;
            Commands++;
        }
    }
}

internal sealed class EffectSpawnRuntime : IRangeSystemRuntime<EffectSpawnJob>
{
    private readonly PrefabHandle _prefab;
    private readonly SimulationProfile _profile;
    private readonly int[] _generations;
    private readonly StructuralCounters _counters;

    internal EffectSpawnRuntime(PrefabHandle prefab, SimulationProfile profile,
        int[] generations, StructuralCounters counters) =>
        (_prefab, _profile, _generations, _counters) =
        (prefab, profile, generations, counters);

    public int GetRangeCount(in SystemPrepareContext context) => _profile.EffectsSpawnedPerTick;

    public EffectSpawnJob Prepare(in SystemPrepareContext context) =>
        new(_prefab, _profile.EffectSlots, _profile.EffectLifetime,
            _profile.EffectsSpawnedPerTick, _generations);

    public void Complete(in SystemCompleteContext context, ReadOnlySpan<EffectSpawnJob> participantResults)
    {
        for (int index = 0; index < participantResults.Length; index++)
        {
            _counters.EffectsCreated += participantResults[index].Created;
            _counters.CommandsAttempted += participantResults[index].Commands;
        }
    }
}

internal struct EffectSpawnJob : IRangeJob
{
    private readonly PrefabHandle _prefab;
    private readonly int _slots;
    private readonly int _lifetime;
    private readonly int _spawnCount;
    private readonly int[] _generations;
    internal int Created;
    internal int Commands;

    internal EffectSpawnJob(PrefabHandle prefab, int slots, int lifetime,
        int spawnCount, int[] generations) =>
        (_prefab, _slots, _lifetime, _spawnCount, _generations, Created, Commands) =
        (prefab, slots, lifetime, spawnCount, generations, 0, 0);

    public void Execute(in SystemJobContext context, int start, int count)
    {
        if (context.Tick + 1 < unchecked((ulong)_lifetime))
            return;
        int cycle = (int)(context.Tick - unchecked((ulong)(_lifetime - 1))) % _lifetime;
        for (int index = start; index < start + count; index++)
        {
            int slot = cycle * _spawnCount + index;
            if ((uint)slot >= unchecked((uint)_slots))
                throw new InvalidOperationException("Effect slot ring calculation escaped capacity.");
            int generation = ++_generations[slot];
            DeferredEntity deferred = context.Commands.Instantiate(_prefab);
            if (!context.Commands.TryAdd(deferred, new EffectIdentity { Slot = slot, Generation = generation }) ||
                !context.Commands.TryAdd(deferred, new Position
                {
                    X = SimulationMath.EffectPosition(slot, generation, context.Tick).X,
                    Y = SimulationMath.EffectPosition(slot, generation, context.Tick).Y,
                }) ||
                !context.Commands.TryAdd(deferred, SimulationMath.EffectVelocity(slot, generation)))
                throw new InvalidOperationException("Effect typed replacement Add was rejected.");
            Created++;
            Commands += 4;
        }
    }
}

internal sealed class ItemRuntime : ISystemRuntime<ItemJob>
{
    public ItemJob Prepare(in SystemPrepareContext context) => new();
}

internal struct ItemJob : IScheduledReadReadWriteChunkJob<LogicalIdentity, Position, WorldItemState>
{
    public void Execute(in SystemJobContext context,
        in ReadReadWriteComponentChunk<LogicalIdentity, Position, WorldItemState> chunk)
    {
        for (int row = 0; row < chunk.Count; row++)
        {
            int id = chunk.ReadComponents1[row].Value;
            chunk.WriteComponents[row].Value = SimulationMath.ItemValue(
                id, chunk.ReadComponents2[row], context.Tick);
        }
    }
}

internal sealed class PositionObserverRuntime : IChangedRowsSystemRuntime<ObservePositionJob>
{
    public ChangeCursor Cursor { get; set; }
    internal long TotalRows;
    internal long Calls;
    internal int MaximumRowsPerConsume;
    internal ulong Hash;

    internal void ResetMetrics() =>
        (TotalRows, Calls, MaximumRowsPerConsume, Hash) = (0, 0, 0, 0);

    public ObservePositionJob Prepare(in SystemPrepareContext context) => new();

    public void Complete(in SystemCompleteContext context, ReadOnlySpan<ObservePositionJob> participantResults)
    {
        for (int index = 0; index < participantResults.Length; index++)
        {
            ObservePositionJob result = participantResults[index];
            TotalRows += result.Rows;
            MaximumRowsPerConsume = Math.Max(MaximumRowsPerConsume, result.Rows);
            Hash ^= result.Hash;
        }
        Calls++;
    }
}

internal struct ObservePositionJob : IScheduledChangedRowsChunkJob<Position>
{
    internal int Rows;
    internal ulong Hash;

    public void Execute(in SystemJobContext context, in ReadOnlyComponentChunk<Position> chunk)
    {
        ulong hash = Hash;
        for (int row = 0; row < chunk.Count; row++)
        {
            Position position = chunk.Components[row];
            hash = unchecked((hash ^ (uint)chunk.EntityIndices[row] ^ (uint)position.X) * 1099511628211UL);
            Rows++;
        }
        Hash = hash;
    }
}

internal sealed class HealthObserverRuntime : IChangedRowsSystemRuntime<ObserveHealthJob>
{
    public ChangeCursor Cursor { get; set; }
    internal long TotalRows;
    internal long Calls;
    internal int MaximumRowsPerConsume;
    internal ulong Hash;

    internal void ResetMetrics() =>
        (TotalRows, Calls, MaximumRowsPerConsume, Hash) = (0, 0, 0, 0);

    public ObserveHealthJob Prepare(in SystemPrepareContext context) => new();

    public void Complete(in SystemCompleteContext context, ReadOnlySpan<ObserveHealthJob> participantResults)
    {
        for (int index = 0; index < participantResults.Length; index++)
        {
            ObserveHealthJob result = participantResults[index];
            TotalRows += result.Rows;
            MaximumRowsPerConsume = Math.Max(MaximumRowsPerConsume, result.Rows);
            Hash ^= result.Hash;
        }
        Calls++;
    }
}

internal struct ObserveHealthJob : IScheduledChangedRowsChunkJob<Health>
{
    internal int Rows;
    internal ulong Hash;

    public void Execute(in SystemJobContext context, in ReadOnlyComponentChunk<Health> chunk)
    {
        ulong hash = Hash;
        for (int row = 0; row < chunk.Count; row++)
        {
            hash = unchecked((hash ^ (uint)chunk.EntityIndices[row] ^ (uint)chunk.Components[row].Value) *
                1099511628211UL);
            Rows++;
        }
        Hash = hash;
    }
}

internal sealed record RunMetrics(
    string Mode,
    string ValidationMode,
    TimingMetrics Timing,
    AllocationMetrics Allocations,
    MemoryMetrics Memory,
    ContentIntegrationMetrics Content,
    SchedulerMetrics Scheduler,
    IReadOnlyList<SystemMetrics> Systems,
    RandomOperationMetrics RandomOperations,
    ObserverMetrics PresentationObserver,
    ObserverMetrics ReplicationObserver,
    ObserverMetrics PersistenceObserver,
    LifecycleObserverMetrics PresentationLifecycle,
    LifecycleObserverMetrics ReplicationLifecycle,
    LifecycleObserverMetrics PersistenceLifecycle,
    ulong OracleLifecycleHash,
    long OracleLifecycleEvents,
    LifecycleHistoryMetrics LifecycleHistory,
    long ActorDeaths,
    long ActorRespawns,
    long EffectsCreated,
    long EffectsDestroyed,
    long StructuralCommandsAttempted,
    ulong EcsCanonicalHash,
    ulong OracleCanonicalHash,
    int AliveEntities,
    int ArchetypeCount,
    long SchedulerReservedBytes,
    long KnownEcsReservedBytes,
    int OccupiedChunks,
    IReadOnlyList<ArchetypeMetrics> Archetypes)
{
    internal static RunMetrics Create(
        long[] samples,
        long callerAllocated,
        long ecsTickCallerAllocated,
        int gen0,
        int gen1,
        int gen2,
        SchedulerRuntimeSnapshot before,
        SchedulerRuntimeSnapshot after,
        SystemRuntimeSnapshot[] systemsBefore,
        SystemRuntimeSnapshot[] systemsAfter,
        int snapshotCount,
        StructuralCounters counters,
        RandomOperationCounters randomOperations,
        PositionObserverRuntime presentation,
        HealthObserverRuntime replication,
        PositionObserverRuntime persistence,
        LifecycleObserver presentationLifecycle,
        LifecycleObserver replicationLifecycle,
        LifecycleObserver persistenceLifecycle,
        ulong oracleLifecycleHash,
        long oracleLifecycleEvents,
        ulong ecsHash,
        ulong oracleHash,
        SimulationProfile profile,
        EcsWorld world,
        ContentIntegrationMetrics content,
        IReadOnlyList<ArchetypeMetrics> archetypes,
        bool deferredValidation)
    {
        var systemMetrics = new List<SystemMetrics>(snapshotCount);
        for (int index = 0; index < snapshotCount; index++)
        {
            SystemRuntimeSnapshot start = systemsBefore[index];
            SystemRuntimeSnapshot snapshot = systemsAfter[index];
            if (start.Id != snapshot.Id)
                throw new InvalidOperationException("System snapshot order changed during measurement.");
            ulong measuredRuns = snapshot.RunCount - start.RunCount;
            double measuredTotal = snapshot.AverageMicroseconds * snapshot.RunCount -
                start.AverageMicroseconds * start.RunCount;
            systemMetrics.Add(new SystemMetrics(
                snapshot.Id.Value,
                snapshot.Key,
                snapshot.Stage.ToString(),
                snapshot.PeriodTicks,
                snapshot.StartOffset,
                snapshot.CompiledWave,
                measuredRuns,
                snapshot.LastMicroseconds,
                measuredRuns == 0 ? 0d : measuredTotal / measuredRuns,
                snapshot.PeakMicroseconds,
                snapshot.LastRows,
                snapshot.LastChunks,
                snapshot.LastWorkItems,
                snapshot.LastExecutionWasParallel,
                snapshot.ExecutionPolicy.ToString()));
        }

        WorldReservedPayloadSnapshot reserved = world.GetReservedPayloadSnapshot();
        LifecycleHistorySnapshot lifecycleHistory = world.GetLifecycleHistorySnapshot();
        using Process process = Process.GetCurrentProcess();
        GCMemoryInfo gc = GC.GetGCMemoryInfo();
        return new RunMetrics(
            world.SchedulerRunMode.ToString(),
            deferredValidation ? "DeferredFinalValidation" : "InterleavedValidation",
            TimingMetrics.From(samples),
            new AllocationMetrics(callerAllocated, ecsTickCallerAllocated,
                checked(callerAllocated - ecsTickCallerAllocated),
                after.WorkerManagedAllocationBytes - before.WorkerManagedAllocationBytes,
                gen0, gen1, gen2),
            new MemoryMetrics(
                process.WorkingSet64,
                process.PrivateMemorySize64,
                GC.GetTotalMemory(forceFullCollection: false),
                gc.HeapSizeBytes,
                gc.FragmentedBytes,
                reserved.TotalKnownPayloadBytes,
                profile.TotalCapacity == 0
                    ? 0d
                    : reserved.TotalKnownPayloadBytes / (double)profile.TotalCapacity),
            content,
            new SchedulerMetrics(
                after.BackgroundWorkerCount,
                after.ExecutionConcurrency,
                after.DispatchCount - before.DispatchCount,
                after.WorkerWakeCount - before.WorkerWakeCount,
                after.WorkerParkCount - before.WorkerParkCount,
                after.MergedSegmentCount - before.MergedSegmentCount,
                after.MergedCommandCount - before.MergedCommandCount,
                after.MergedPayloadBytes - before.MergedPayloadBytes,
                after.FullEntityResolutionCount - before.FullEntityResolutionCount,
                ToMicroseconds(after.PreBarrierTimestampTicks - before.PreBarrierTimestampTicks),
                ToMicroseconds(after.GraphTimestampTicks - before.GraphTimestampTicks),
                ToMicroseconds(after.StructuralMergeTimestampTicks - before.StructuralMergeTimestampTicks),
                ToMicroseconds(after.PostBarrierTimestampTicks - before.PostBarrierTimestampTicks)),
            systemMetrics,
            new RandomOperationMetrics(
                randomOperations.TargetAttempts,
                randomOperations.TargetWrites,
                randomOperations.TargetDeadSkips,
                randomOperations.DamageAttempts,
                randomOperations.DamageAliveSources,
                randomOperations.DamageAliveVictims,
                randomOperations.DamageWrites),
            new ObserverMetrics(presentation.TotalRows, presentation.Calls,
                presentation.MaximumRowsPerConsume, presentation.Hash),
            new ObserverMetrics(replication.TotalRows, replication.Calls,
                replication.MaximumRowsPerConsume, replication.Hash),
            new ObserverMetrics(persistence.TotalRows, persistence.Calls,
                persistence.MaximumRowsPerConsume, persistence.Hash),
            LifecycleObserverMetrics.From(
                presentationLifecycle, lifecycleHistory.NextPublishedSequence, samples.Length),
            LifecycleObserverMetrics.From(
                replicationLifecycle, lifecycleHistory.NextPublishedSequence, samples.Length),
            LifecycleObserverMetrics.From(
                persistenceLifecycle, lifecycleHistory.NextPublishedSequence, samples.Length),
            oracleLifecycleHash,
            oracleLifecycleEvents,
            new LifecycleHistoryMetrics(
                lifecycleHistory.Capacity,
                lifecycleHistory.RetainedCount,
                lifecycleHistory.HighWatermark,
                lifecycleHistory.OverwrittenCount,
                lifecycleHistory.OldestRetainedSequence,
                lifecycleHistory.NextPublishedSequence,
                lifecycleHistory.ReservedBytes),
            counters.ActorDeaths,
            counters.ActorRespawns,
            counters.EffectsCreated,
            counters.EffectsDestroyed,
            counters.CommandsAttempted,
            ecsHash,
            oracleHash,
            world.AliveCount,
            world.ArchetypeCount,
            world.SchedulerReservedBytes,
            reserved.TotalKnownPayloadBytes,
            archetypes.Sum(static value => value.OccupiedChunks),
            archetypes);
    }

    private static double ToMicroseconds(long ticks) =>
        ticks * 1_000_000d / Stopwatch.Frequency;
}

internal sealed record LifecycleObserverMetrics(
    long TotalRecords,
    long Calls,
    int MaximumRecordsPerConsume,
    double RecordsPerTick,
    double BytesPerTick,
    ulong CurrentLag,
    ulong MaximumLag,
    long ResyncRequiredCount,
    long Created,
    long Destroyed,
    long ComponentAdded,
    long ComponentRemoved,
    double TotalDrainMicroseconds,
    double AverageDrainMicroseconds,
    ulong Hash,
    ulong OrderedHash)
{
    internal static LifecycleObserverMetrics From(
        LifecycleObserver value,
        ulong nextPublishedSequence,
        int measuredTicks) => new(
        value.TotalRecords,
        value.Calls,
        value.MaximumRecordsPerConsume,
        measuredTicks == 0 ? 0d : value.TotalRecords / (double)measuredTicks,
        measuredTicks == 0
            ? 0d
            : value.TotalRecords * (double)Unsafe.SizeOf<LifecycleRecord>() / measuredTicks,
        unchecked(nextPublishedSequence - value.Cursor.NextSequence),
        value.MaximumLag,
        value.ResyncRequiredCount,
        value.Created,
        value.Destroyed,
        value.ComponentAdded,
        value.ComponentRemoved,
        value.DrainTimestampTicks * 1_000_000d / Stopwatch.Frequency,
        value.Calls == 0
            ? 0d
            : value.DrainTimestampTicks * 1_000_000d / Stopwatch.Frequency / value.Calls,
        value.Hash,
        value.OrderedHash);
}

internal sealed record LifecycleHistoryMetrics(
    int Capacity,
    int RetainedCount,
    int HighWatermark,
    ulong OverwrittenCount,
    ulong OldestRetainedSequence,
    ulong NextPublishedSequence,
    long ReservedBytes);

internal sealed record TimingMetrics(
    double MinimumMicroseconds,
    double MeanMicroseconds,
    double P50Microseconds,
    double P95Microseconds,
    double P99Microseconds,
    double P999Microseconds,
    double MaxMicroseconds,
    double StandardDeviationMicroseconds)
{
    internal static TimingMetrics From(long[] samples)
    {
        double[] micros = new double[samples.Length];
        for (int index = 0; index < samples.Length; index++)
            micros[index] = samples[index] * 1_000_000d / Stopwatch.Frequency;
        Array.Sort(micros);
        double mean = micros.Average();
        double squaredDeviation = 0d;
        for (int index = 0; index < micros.Length; index++)
        {
            double deviation = micros[index] - mean;
            squaredDeviation += deviation * deviation;
        }
        return new TimingMetrics(
            micros[0],
            mean,
            Percentile(micros, 0.50),
            Percentile(micros, 0.95),
            Percentile(micros, 0.99),
            Percentile(micros, 0.999),
            micros[^1],
            Math.Sqrt(squaredDeviation / micros.Length));
    }

    private static double Percentile(double[] values, double percentile)
    {
        int index = Math.Clamp((int)Math.Ceiling(values.Length * percentile) - 1, 0, values.Length - 1);
        return values[index];
    }
}

internal sealed record AllocationMetrics(
    long CallerThreadBytes,
    long EcsTickCallerThreadBytes,
    long OracleAndValidationBytes,
    long SchedulerWorkerBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections);

internal sealed record MemoryMetrics(
    long WorkingSetBytes,
    long PrivateBytes,
    long ManagedBytes,
    long ManagedHeapBytes,
    long ManagedFragmentedBytes,
    long KnownEcsReservedBytes,
    double KnownEcsBytesPerCapacitySlot);

internal sealed record ContentIntegrationMetrics(
    double CompileMicroseconds,
    double BootstrapMicroseconds,
    CompiledContentSnapshot Catalog);

internal sealed record SchedulerMetrics(
    int BackgroundWorkers,
    int ExecutionConcurrency,
    long Dispatches,
    long WorkerWakes,
    long WorkerParks,
    long MergedSegments,
    long MergedCommands,
    long MergedPayloadBytes,
    long FullEntityResolutions,
    double PreBarrierMicroseconds,
    double GraphMicroseconds,
    double StructuralMergeMicroseconds,
    double PostBarrierMicroseconds);

internal sealed record RandomOperationMetrics(
    long TargetAttempts,
    long TargetWrites,
    long TargetDeadSkips,
    long DamageAttempts,
    long DamageAliveSources,
    long DamageAliveVictims,
    long DamageWrites);

internal sealed record ArchetypeMetrics(
    string Name,
    int EntityCount,
    int RowCapacity,
    int MaximumChunks,
    int AcquiredChunks,
    int OccupiedChunks,
    int FullChunks,
    int PartialChunks,
    int EmptyChunks,
    int UnallocatedChunks,
    long UnusedAcquiredRows,
    long MetadataBytes);

internal sealed record SystemMetrics(
    int Id,
    string Key,
    string Stage,
    int PeriodTicks,
    int StartOffset,
    int CompiledWave,
    ulong MeasuredRunCount,
    double LastMicroseconds,
    double MeasuredAverageMicroseconds,
    double CumulativePeakMicroseconds,
    int LastRows,
    int LastChunks,
    int LastWorkItems,
    bool LastExecutionWasParallel,
    string ExecutionPolicy);

internal sealed record ObserverMetrics(
    long RowsDelivered,
    long ConsumeCalls,
    int MaximumRowsPerConsume,
    ulong AggregateHash);
