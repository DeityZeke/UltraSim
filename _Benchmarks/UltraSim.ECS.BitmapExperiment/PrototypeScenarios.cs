namespace UltraSim.ECS.BitmapExperiment;

internal enum HolePattern : byte
{
    Tail,
    Random,
    Clustered,
    OnePerWord,
    MixedSparseAndEmptyWords,
}

internal abstract class PrototypeScenarioBase : IExperimentScenario
{
    protected PrototypeScenarioBase(
        string logicalCaseId,
        string category,
        PrototypeStorageKind kind,
        int scale,
        int archetypeColumns,
        int queryBytesPerRow,
        PrototypeWorld world,
        int topologyStoreId = 0)
    {
        LogicalCaseId = logicalCaseId;
        Category = category;
        Kind = kind;
        Scale = scale;
        ArchetypeBytesPerRow = checked(archetypeColumns * sizeof(ulong));
        QueryBytesPerRow = queryBytesPerRow;
        World = world;
        TopologyStoreId = topologyStoreId;
        Name = $"{logicalCaseId}/{kind}";
    }

    protected PrototypeWorld World { get; }

    protected int TopologyStoreId { get; }

    public string LogicalCaseId { get; }

    public string Name { get; }

    public string Category { get; }

    public PrototypeStorageKind Kind { get; }

    public int Scale { get; }

    public int ArchetypeBytesPerRow { get; }

    public int QueryBytesPerRow { get; }

    public long PayloadBytes => World.PayloadBytes;

    public long OccupancyMetadataBytes => World.OccupancyMetadataBytes;

    public long RowEntityMetadataBytes => World.RowEntityMetadataBytes;

    public abstract void Prepare();

    public abstract ExperimentMeasurement Execute();

    public ulong CaptureCanonicalHash() => World.CaptureCanonicalHash();

    public PrototypeTopology CaptureTopology() => World.GetTopology(TopologyStoreId);

    public void Dispose() => World.Dispose();

    protected static void Fill(PrototypeWorld world, int storeId, PrototypeEntity[] entities, int count, ulong seedBase = 0)
    {
        for (int index = 0; index < count; index++)
        {
            if (!world.TryCreate(storeId, seedBase + (ulong)index, out entities[index]))
                throw new InvalidOperationException($"Prototype fill failed at {index}/{count}.");
        }
    }

    protected static long Pack(in PrototypeEntity entity) =>
        unchecked(((long)entity.Generation << 32) | (uint)entity.Index);
}

internal sealed class CreateScenario : PrototypeScenarioBase
{
    private readonly int _count;
    private readonly bool _bulk;

    internal CreateScenario(PrototypeStorageKind kind, int count, int columns, bool bulk)
        : base(
            $"{(bulk ? "create-bulk" : "create")}/n={count}/width={columns * 8}",
            bulk ? "create-bulk" : "create",
            kind,
            count,
            columns,
            0,
            new PrototypeWorld(kind, count, count, columns))
    {
        _count = count;
        _bulk = bulk;
    }

    public override void Prepare() => World.Reset();

    public override ExperimentMeasurement Execute()
    {
        long checksum = 0;
        if (_bulk)
            World.BeginBulk(0);
        for (int index = 0; index < _count; index++)
        {
            if (!World.TryCreate(0, (ulong)index, out PrototypeEntity entity, bulk: _bulk))
                throw new InvalidOperationException("Create scenario exhausted capacity unexpectedly.");
            checksum = unchecked(checksum + Pack(entity));
        }
        if (_bulk)
            World.EndBulk(0);
        return new ExperimentMeasurement(_count, checksum);
    }
}

internal sealed class DestroyScenario : PrototypeScenarioBase
{
    private readonly PrototypeEntity[] _entities;
    private readonly int[] _targets;

    internal DestroyScenario(
        PrototypeStorageKind kind,
        int count,
        int columns,
        int percentage,
        HolePattern pattern)
        : base(
            $"destroy/n={count}/width={columns * 8}/pct={percentage}/pattern={pattern}",
            "destroy",
            kind,
            count,
            columns,
            0,
            new PrototypeWorld(kind, count, count, columns))
    {
        _entities = new PrototypeEntity[count];
        _targets = ScenarioSelection.Build(count, percentage, pattern);
    }

    public override void Prepare()
    {
        World.Reset();
        Fill(World, 0, _entities, _entities.Length);
    }

    public override ExperimentMeasurement Execute()
    {
        long checksum = 0;
        foreach (int target in _targets)
        {
            PrototypeEntity entity = _entities[target];
            if (!World.TryDestroy(entity))
                throw new InvalidOperationException($"Destroy scenario rejected target {target}.");
            checksum = unchecked(checksum + Pack(entity));
        }
        checksum = unchecked(checksum + World.EndStructuralBarrier());
        return new ExperimentMeasurement(_targets.Length, checksum);
    }
}

internal sealed class ChurnScenario : PrototypeScenarioBase
{
    private readonly PrototypeEntity[] _entities;
    private readonly int[] _targets;
    private readonly int _scanCount;

    internal ChurnScenario(
        PrototypeStorageKind kind,
        int count,
        int columns,
        int percentage,
        int scanCount)
        : base(
            $"churn/n={count}/width={columns * 8}/pct={percentage}/scans={scanCount}",
            scanCount == 0 ? "churn" : "mixed-tick",
            kind,
            count,
            columns,
            0,
            new PrototypeWorld(kind, count, count, columns))
    {
        _entities = new PrototypeEntity[count];
        _targets = ScenarioSelection.Build(count, percentage, HolePattern.Random);
        _scanCount = scanCount;
    }

    public override void Prepare()
    {
        World.Reset();
        Fill(World, 0, _entities, _entities.Length);
    }

    public override ExperimentMeasurement Execute()
    {
        long checksum = 0;
        foreach (int target in _targets)
        {
            PrototypeEntity old = _entities[target];
            if (!World.TryDestroy(old))
                throw new InvalidOperationException("Churn destroy failed.");
            checksum = unchecked(checksum + Pack(old));
        }
        foreach (int target in _targets)
        {
            if (!World.TryCreate(0, (ulong)(1_000_000 + target), out PrototypeEntity replacement))
                throw new InvalidOperationException("Churn create failed.");
            _entities[target] = replacement;
            checksum = unchecked(checksum + Pack(replacement));
        }
        checksum = unchecked(checksum + World.EndStructuralBarrier());
        for (int scan = 0; scan < _scanCount; scan++)
        {
            checksum = unchecked(checksum + (long)World.Traverse(0, Math.Min(2, ArchetypeBytesPerRow / 8), out int visited));
            if (visited != Scale)
                throw new InvalidOperationException("Churn traversal missed live rows.");
        }
        long operations = _scanCount == 0 ? _targets.Length * 2L : 1L;
        return new ExperimentMeasurement(operations, checksum);
    }
}

internal sealed class TraversalScenario : PrototypeScenarioBase
{
    private readonly PrototypeEntity[] _entities;
    private readonly int[] _targets;
    private readonly int _queryColumns;
    private readonly bool _mutate;

    internal TraversalScenario(
        PrototypeStorageKind kind,
        int count,
        int columns,
        int queryColumns,
        int percentageRemoved,
        HolePattern pattern,
        bool mutate)
        : base(
            $"traverse/n={count}/width={columns * 8}/query={queryColumns * 8}/removed={percentageRemoved}/pattern={pattern}/mode={(mutate ? "rw" : "ro")}",
            mutate ? "traversal-rw" : "traversal-ro",
            kind,
            count,
            columns,
            (queryColumns + (mutate ? 1 : 0)) * sizeof(ulong),
            new PrototypeWorld(kind, count, count, columns))
    {
        _entities = new PrototypeEntity[count];
        _targets = percentageRemoved == 0
            ? Array.Empty<int>()
            : ScenarioSelection.Build(count, percentageRemoved, pattern);
        _queryColumns = queryColumns;
        _mutate = mutate;
    }

    public override void Prepare()
    {
        World.Reset();
        Fill(World, 0, _entities, _entities.Length);
        foreach (int target in _targets)
        {
            if (!World.TryDestroy(_entities[target]))
                throw new InvalidOperationException("Traversal topology setup failed.");
        }
        World.EndStructuralBarrier();
    }

    public override ExperimentMeasurement Execute()
    {
        ulong checksum = _mutate
            ? World.TraverseAndMutate(0, _queryColumns, out int visited)
            : World.Traverse(0, _queryColumns, out visited);
        int expected = Scale - _targets.Length;
        if (visited != expected)
            throw new InvalidOperationException($"Traversal visited {visited}, expected {expected}.");
        return new ExperimentMeasurement(visited, unchecked((long)checksum));
    }
}

internal sealed class OccupancySweepScenario : PrototypeScenarioBase
{
    private readonly PrototypeEntity[] _entities;
    private readonly int _livePerWord;
    private readonly int _queryColumns;
    private readonly bool _mutate;

    internal OccupancySweepScenario(
        PrototypeStorageKind kind,
        int liveCount,
        int columns,
        int queryColumns,
        int livePerWord,
        bool mutate)
        : base(
            $"occupancy/live={liveCount}/width={columns * 8}/query={queryColumns * 8}/live-per-word={livePerWord}/mode={(mutate ? "rw" : "ro")}",
            mutate ? "occupancy-rw" : "occupancy-ro",
            kind,
            liveCount,
            columns,
            (queryColumns + (mutate ? 1 : 0)) * sizeof(ulong),
            CreateWorld(kind, liveCount, livePerWord, columns))
    {
        if (livePerWord is not (8 or 16 or 32 or 64))
            throw new ArgumentOutOfRangeException(nameof(livePerWord));
        _livePerWord = livePerWord;
        _queryColumns = queryColumns;
        _mutate = mutate;
        _entities = new PrototypeEntity[PhysicalCount(liveCount, livePerWord)];
    }

    public override void Prepare()
    {
        World.Reset();
        Fill(World, 0, _entities, _entities.Length);
        for (int wordBase = 0; wordBase < _entities.Length; wordBase += 64)
        {
            int wordEnd = Math.Min(wordBase + 64, _entities.Length);
            for (int row = wordBase + _livePerWord; row < wordEnd; row++)
            {
                if (!World.TryDestroy(_entities[row]))
                    throw new InvalidOperationException("Occupancy sweep setup failed.");
            }
        }
        int excess = World.AliveCount - Scale;
        int finalWordBase = ((_entities.Length - 1) >> 6) << 6;
        for (int offset = 0; offset < excess; offset++)
        {
            if (!World.TryDestroy(_entities[finalWordBase + offset]))
                throw new InvalidOperationException("Occupancy sweep remainder trimming failed.");
        }
        World.EndStructuralBarrier();
        if (World.AliveCount != Scale)
            throw new InvalidOperationException($"Occupancy sweep has {World.AliveCount} live rows, expected {Scale}.");
    }

    public override ExperimentMeasurement Execute()
    {
        ulong checksum = _mutate
            ? World.TraverseAndMutate(0, _queryColumns, out int visited)
            : World.Traverse(0, _queryColumns, out visited);
        if (visited != Scale)
            throw new InvalidOperationException("Occupancy sweep traversal missed rows.");
        return new ExperimentMeasurement(visited, unchecked((long)checksum));
    }

    private static PrototypeWorld CreateWorld(PrototypeStorageKind kind, int liveCount, int livePerWord, int columns)
    {
        int physical = PhysicalCount(liveCount, livePerWord);
        return new PrototypeWorld(kind, physical, physical, columns);
    }

    private static int PhysicalCount(int liveCount, int livePerWord) =>
        checked(((liveCount + livePerWord - 1) / livePerWord) * 64);
}

internal sealed class RandomAccessScenario : PrototypeScenarioBase
{
    private readonly PrototypeEntity[] _entities;
    private readonly bool[] _removed;
    private readonly int[] _targets;
    private readonly bool _write;

    internal RandomAccessScenario(
        PrototypeStorageKind kind,
        int count,
        int columns,
        int percentageRemoved,
        bool write)
        : base(
            $"random-access/n={count}/width={columns * 8}/removed={percentageRemoved}/mode={(write ? "rw" : "ro")}",
            write ? "random-access-rw" : "random-access-ro",
            kind,
            count,
            columns,
            write ? 16 : 8,
            new PrototypeWorld(kind, count, count, columns))
    {
        _entities = new PrototypeEntity[count];
        _write = write;
        _removed = new bool[count];
        int[] removals = ScenarioSelection.Build(count, percentageRemoved, HolePattern.Random);
        foreach (int removal in removals)
            _removed[removal] = true;
        _targets = new int[count - removals.Length];
        int written = 0;
        for (int index = count - 1; index >= 0; index--)
        {
            if (!_removed[index])
                _targets[written++] = index;
        }
    }

    public override void Prepare()
    {
        World.Reset();
        Fill(World, 0, _entities, _entities.Length);
        for (int index = 0; index < _removed.Length; index++)
        {
            if (_removed[index] && !World.TryDestroy(_entities[index]))
                throw new InvalidOperationException("Random access topology setup failed.");
        }
        World.EndStructuralBarrier();
    }

    public override ExperimentMeasurement Execute()
    {
        long checksum = 0;
        foreach (int target in _targets)
        {
            PrototypeEntity entity = _entities[target];
            if (!World.TryRead(entity, 0, out ulong value))
                throw new InvalidOperationException("Random read failed.");
            if (_write && !World.TryWrite(entity, 0, unchecked(value + 1UL)))
                throw new InvalidOperationException("Random write failed.");
            checksum = unchecked(checksum + (long)value);
        }
        return new ExperimentMeasurement(_targets.Length, checksum);
    }
}

internal enum DestinationShape : byte
{
    Empty,
    RandomHoles,
    MixedSparseAndEmptyWords,
}

internal sealed class TransitionScenario : PrototypeScenarioBase
{
    private readonly PrototypeEntity[] _source;
    private readonly PrototypeEntity[] _destination;
    private readonly int[] _destinationRemovals;
    private readonly int[] _sourceTargets;
    private readonly int _sourceColumns;
    private readonly DestinationShape _shape;

    internal TransitionScenario(
        PrototypeStorageKind kind,
        int count,
        int sourceColumns,
        DestinationShape shape,
        HolePattern sourcePattern)
        : base(
            $"transition/n={count}/width={sourceColumns * 8}/destination={shape}/source={sourcePattern}",
            "transition",
            kind,
            count,
            sourceColumns + 1,
            0,
            new PrototypeWorld(kind, checked(count * 2 + 64), checked(count * 2 + 64), sourceColumns, sourceColumns + 1),
            topologyStoreId: 1)
    {
        _shape = shape;
        _sourceColumns = sourceColumns;
        _source = new PrototypeEntity[count];
        _destination = new PrototypeEntity[count];
        if (shape == DestinationShape.Empty)
        {
            _destinationRemovals = Array.Empty<int>();
            _sourceTargets = ScenarioSelection.Build(count, 100, sourcePattern);
        }
        else
        {
            _destinationRemovals = ScenarioSelection.Build(
                count,
                50,
                shape == DestinationShape.RandomHoles ? HolePattern.Random : HolePattern.MixedSparseAndEmptyWords);
            _sourceTargets = ScenarioSelection.Build(count, 50, sourcePattern);
        }
    }

    public override void Prepare()
    {
        World.Reset();
        Fill(World, 0, _source, _source.Length);
        if (_shape != DestinationShape.Empty)
        {
            Fill(World, 1, _destination, _destination.Length, 10_000_000);
            foreach (int target in _destinationRemovals)
            {
                if (!World.TryDestroy(_destination[target]))
                    throw new InvalidOperationException("Destination fragmentation failed.");
            }
        }
    }

    public override ExperimentMeasurement Execute()
    {
        long checksum = 0;
        World.BeginBulk(1);
        foreach (int target in _sourceTargets)
        {
            PrototypeEntity entity = _source[target];
            if (!World.TryTransition(entity, 1, _sourceColumns, bulk: true))
                throw new InvalidOperationException("Transition failed.");
            checksum = unchecked(checksum + Pack(entity));
        }
        World.EndBulk(1);
        checksum = unchecked(checksum + World.EndStructuralBarrier());
        return new ExperimentMeasurement(_sourceTargets.Length, checksum);
    }
}

internal static class ScenarioSelection
{
    internal static int[] Build(int count, int percentage, HolePattern pattern)
    {
        int targetCount = pattern == HolePattern.OnePerWord
            ? (count + 63) / 64
            : Math.Clamp((int)((long)count * percentage / 100), 1, count);
        var result = new int[targetCount];
        switch (pattern)
        {
            case HolePattern.Tail:
                for (int index = 0; index < targetCount; index++)
                    result[index] = count - 1 - index;
                break;
            case HolePattern.Clustered:
                int start = (count - targetCount) / 2;
                for (int index = 0; index < targetCount; index++)
                    result[index] = start + index;
                break;
            case HolePattern.OnePerWord:
                for (int index = 0; index < targetCount; index++)
                    result[index] = Math.Min(index * 64 + 31, count - 1);
                break;
            case HolePattern.MixedSparseAndEmptyWords:
                BuildMixed(result, count);
                break;
            default:
                BuildRandom(result, count);
                break;
        }
        return result;
    }

    private static void BuildRandom(int[] result, int count)
    {
        var values = new int[count];
        for (int index = 0; index < count; index++)
            values[index] = index;
        uint state = 0xC001_D00DU;
        for (int index = 0; index < result.Length; index++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            int selected = index + (int)(state % (uint)(count - index));
            (values[index], values[selected]) = (values[selected], values[index]);
            result[index] = values[index];
        }
    }

    private static void BuildMixed(int[] result, int count)
    {
        var used = new bool[count];
        int written = 0;
        int wordCount = (count + 63) / 64;
        for (int word = 0; word < wordCount / 2 && written < result.Length; word++)
        {
            int row = Math.Min((word << 6) + 3, count - 1);
            if (!used[row])
            {
                used[row] = true;
                result[written++] = row;
            }
        }
        for (int row = count - 1; row >= 0 && written < result.Length; row--)
        {
            if (used[row])
                continue;
            used[row] = true;
            result[written++] = row;
        }
    }
}
