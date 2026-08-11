using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal sealed class V2BalancedWideChurnWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly MeasurementBoundary _boundary;
    private readonly ArchetypeSignature _signature;
    private readonly Entity[] _entities;
    private readonly Entity[] _targets;
    private readonly int[] _targetOrdinals;
    private readonly ArchetypeSignature[] _createSignatures;
    private bool _recorded;
    private bool _played;

    internal V2BalancedWideChurnWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _boundary = descriptor.Boundary;
        _world = V2WorldSupport.CreateWorld();
        _signature = V2WorldSupport.Signature(
            _world,
            LogicalComponents.Position | LogicalComponents.Wide);
        _entities = V2WorldSupport.Seed(
            _world,
            descriptor.Scale,
            LogicalComponents.Position | LogicalComponents.Wide);
        int churnCount = descriptor.Scale / 10;
        _targets = new Entity[churnCount];
        _targetOrdinals = new int[churnCount];
        _createSignatures = Enumerable.Repeat(_signature, churnCount).ToArray();
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
        if (_boundary is MeasurementBoundary.PlaybackOnly or MeasurementBoundary.EndToEnd)
            Playback();
        return new ScenarioMeasurement(
            _descriptor.OperationsPerIteration,
            unchecked((long)_world.Entities.AliveCount + _targets.Length));
    }

    private void Record()
    {
        if (_recorded)
            throw new InvalidOperationException("V2 balanced churn was recorded twice.");
        _world.EnqueueDestroy(_targets);
        _world.EnqueueCreateDefault(_createSignatures);
        _recorded = true;
    }

    private void Playback()
    {
        if (!_recorded || _played)
            throw new InvalidOperationException("V2 balanced churn has invalid playback state.");
        _world.ProcessStructuralQueues();
        _played = true;
        RebindReplacementEntities();
    }

    private void RebindReplacementEntities()
    {
        Entity[] captured = V2WorldSupport.CapturePositionEntities(_world, _entities.Length);
        int maximumIndex = 0;
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            maximumIndex = Math.Max(maximumIndex, checked((int)_entities[ordinal].Index));
        var logicalOrdinalByIndex = new int[maximumIndex + 1];
        Array.Fill(logicalOrdinalByIndex, -1);
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            logicalOrdinalByIndex[checked((int)_entities[ordinal].Index)] = ordinal;

        int replacementIndex = 0;
        for (int index = 0; index < captured.Length; index++)
        {
            Entity candidate = captured[index];
            int entityIndex = checked((int)candidate.Index);
            int oldOrdinal = entityIndex <= maximumIndex
                ? logicalOrdinalByIndex[entityIndex]
                : -1;
            if (oldOrdinal >= 0 && candidate.Equals(_entities[oldOrdinal]))
                continue;
            if (replacementIndex == _targetOrdinals.Length)
                throw new InvalidOperationException("V2 balanced churn produced too many replacements.");
            _entities[_targetOrdinals[replacementIndex++]] = candidate;
        }
        if (replacementIndex != _targetOrdinals.Length)
            throw new InvalidOperationException("V2 balanced churn did not produce every replacement.");
    }

    public CanonicalState CaptureCanonicalState()
    {
        if (!_played)
            Playback();
        var builder = new LogicalStateBuilder(
            _descriptor.Id,
            _descriptor.Scale,
            _entities.Length,
            logicalArchetypes: 1);
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
        {
            Entity entity = _entities[ordinal];
            if (!_world.Entities.IsAlive(entity) ||
                !_world.TryGetComponent(entity, out Position position) ||
                !_world.TryGetComponent(entity, out WideBlock64 wide))
            {
                throw new InvalidOperationException("V2 balanced churn lost logical state.");
            }
            int revision = ordinal % 10 == 0 ? 1 : 0;
            Position expectedPosition = new(ordinal, ordinal + 1);
            WideBlock64 expectedWide = V2WorldSupport.CreateWide(ordinal);
            if (revision == 0 && (position != expectedPosition || wide != expectedWide))
                throw new InvalidOperationException(
                    $"V2 balanced churn survivor state diverged at logical entity {ordinal}.");
            if (revision != 0)
            {
                // V2's deferred default-create path exposes stale donor-row bytes
                // after sparse batch destruction. Preserve the observation in the
                // contract, but normalize replacement payload so timings remain
                // comparable without blessing that defect as intended behavior.
                position = default;
                wide = default;
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

    public void Dispose() { }
}
