using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V3ComparisonAdapter;

internal sealed class V3BalancedWideChurnWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly EcsWorld _world;
    private readonly MeasurementBoundary _boundary;
    private readonly StructuralCommandBuffer _commands;
    private readonly ComponentLookup<Position> _positions;
    private readonly ComponentLookup<WideBlock64> _wide;
    private readonly Entity[] _entities;
    private readonly Entity[] _targets;
    private readonly int[] _targetOrdinals;
    private readonly ArchetypeHandle[] _createArchetypes;
    private readonly DeferredEntity[] _tokens;
    private bool _recorded;
    private bool _played;

    internal V3BalancedWideChurnWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _boundary = descriptor.Boundary;
        ComponentSchema schema = V3WorldSupport.Schema;
        ComponentId[] signature = V3WorldSupport.Signature(
            LogicalComponents.Position | LogicalComponents.Wide);
        int commandCount = checked(descriptor.Scale / 5);
        _world = new EcsWorld(schema, new EcsWorldOptions(
            new EntityCapacity(descriptor.Scale),
            maximumArchetypes: 1,
            maximumPreparedPlans: 2,
            maximumPreparedTransitions: 0,
            maximumStructuralCommands: commandCount));
        ArchetypeHandle archetype = _world.RegisterArchetype(
            signature,
            ChunkLayoutOptions.KiB16,
            V3WorldSupport.MaximumChunks(schema, signature, descriptor.Scale));
        _positions = _world.PrepareLookup<Position>();
        _wide = _world.PrepareLookup<WideBlock64>();
        _world.Seal();
        _world.Initialize();
        _world.BeginRun();
        _commands = _world.StructuralCommands;
        _entities = new Entity[descriptor.Scale];
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            if (!_world.TryCreate(
                    archetype,
                    _positions,
                    new Position { X = ordinal, Y = ordinal + 1 },
                    out _entities[ordinal]) ||
                !_world.TryWrite(_entities[ordinal], _wide, V3WorldSupport.CreateWide(ordinal)))
            {
                throw new InvalidOperationException("V3 balanced churn exhausted its fixture.");
            }
        }
        int churnCount = descriptor.Scale / 10;
        _targets = new Entity[churnCount];
        _targetOrdinals = new int[churnCount];
        _createArchetypes = new ArchetypeHandle[churnCount];
        _tokens = new DeferredEntity[churnCount];
        Array.Fill(_createArchetypes, archetype);
        for (int index = 0; index < churnCount; index++)
        {
            int ordinal = index * 10;
            _targetOrdinals[index] = ordinal;
            _targets[index] = _entities[ordinal];
        }
        if (_boundary == MeasurementBoundary.PlaybackOnly)
            Record();
    }

    public ScenarioMeasurement Execute()
    {
        if (_boundary is MeasurementBoundary.RecordOnly or MeasurementBoundary.EndToEnd)
            Record();
        StructuralPlaybackResult result = default;
        if (_boundary is MeasurementBoundary.PlaybackOnly or MeasurementBoundary.EndToEnd)
            result = Playback();
        return new ScenarioMeasurement(
            _descriptor.OperationsPerIteration,
            unchecked((long)result.AppliedCount + result.ReusedTemporaryHoleCount));
    }

    private void Record()
    {
        if (_recorded)
            throw new InvalidOperationException("V3 balanced churn was recorded twice.");
        _commands.EnqueueDestroy(_targets);
        _commands.EnqueueCreateDefault(_createArchetypes, _tokens);
        _recorded = true;
    }

    private StructuralPlaybackResult Playback()
    {
        if (!_recorded || _played)
            throw new InvalidOperationException("V3 balanced churn has invalid playback state.");
        StructuralPlaybackResult result = _world.PlaybackStructuralCommands();
        int churnCount = _targets.Length;
        if (result.DestroyedCount != churnCount ||
            result.CreatedCount != churnCount ||
            result.RejectedCount != 0 ||
            result.TemporaryHoleCount != churnCount ||
            result.ReusedTemporaryHoleCount != churnCount ||
            result.CompactedTemporaryHoleCount != 0 ||
            result.TemporaryHoleDonorRelocationCount != 0)
        {
            throw new InvalidOperationException(
                "V3 balanced churn did not use the expected zero-relocation row-reuse plan.");
        }
        for (int index = 0; index < churnCount; index++)
        {
            if (!_commands.TryResolve(_tokens[index], out Entity replacement))
                throw new InvalidOperationException("V3 balanced churn lost a replacement token.");
            _entities[_targetOrdinals[index]] = replacement;
        }
        _played = true;
        return result;
    }

    public CanonicalState CaptureCanonicalState()
    {
        if (!_played)
            _ = Playback();
        var builder = new LogicalStateBuilder(
            _descriptor.Id,
            _descriptor.Scale,
            _entities.Length,
            logicalArchetypes: 1);
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            Entity entity = _entities[ordinal];
            if (!_world.TryRead(entity, _positions, out Position position) ||
                !_world.TryRead(entity, _wide, out WideBlock64 wide))
            {
                throw new InvalidOperationException("V3 balanced churn lost logical state.");
            }
            int revision = ordinal % 10 == 0 ? 1 : 0;
            Position expectedPosition = revision == 0
                ? new Position { X = ordinal, Y = ordinal + 1 }
                : default;
            WideBlock64 expectedWide = revision == 0
                ? V3WorldSupport.CreateWide(ordinal)
                : default;
            if (position.X != expectedPosition.X ||
                position.Y != expectedPosition.Y ||
                wide.Value0 != expectedWide.Value0 ||
                wide.Value1 != expectedWide.Value1 ||
                wide.Value2 != expectedWide.Value2 ||
                wide.Value3 != expectedWide.Value3 ||
                wide.Value4 != expectedWide.Value4 ||
                wide.Value5 != expectedWide.Value5 ||
                wide.Value6 != expectedWide.Value6 ||
                wide.Value7 != expectedWide.Value7)
            {
                throw new InvalidOperationException(
                    $"V3 balanced churn state diverged at logical entity {ordinal}.");
            }
            builder.AddEntity(
                ordinal,
                alive: true,
                revision,
                LogicalComponents.Position | LogicalComponents.Wide,
                position.X,
                position.Y,
                wide0: wide.Value0,
                wide1: wide.Value1,
                wide2: wide.Value2,
                wide3: wide.Value3,
                wide4: wide.Value4,
                wide5: wide.Value5,
                wide6: wide.Value6,
                wide7: wide.Value7);
        }
        builder.AddArchetype(
            LogicalComponents.Position | LogicalComponents.Wide,
            _entities.Length);
        return builder.Finish();
    }

    public void Dispose() => _world.Dispose();
}
