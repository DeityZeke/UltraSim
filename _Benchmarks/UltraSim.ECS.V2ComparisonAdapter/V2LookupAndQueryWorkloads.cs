using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal sealed class V2LookupWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly Entity[] _entities;
    private readonly bool _write;

    internal V2LookupWorkload(ScenarioDescriptor descriptor, bool write)
    {
        _descriptor = descriptor;
        _world = V2WorldSupport.CreateWorld();
        _entities = V2WorldSupport.Seed(_world, descriptor.Scale, LogicalComponents.Position);
        _write = write;
    }

    public ScenarioMeasurement Execute()
    {
        long checksum = 0;
        if (_write)
        {
            for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            {
                var value = new Position(ordinal + 7, ordinal * 2 + 11);
                if (!_world.SetComponent(_entities[ordinal], value))
                    throw new InvalidOperationException("V2 lookup write lost an entity.");
                checksum = unchecked(checksum + value.X + value.Y);
            }
        }
        else
        {
            for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            {
                if (!_world.TryGetComponent(_entities[ordinal], out Position value))
                    throw new InvalidOperationException("V2 lookup read lost an entity.");
                checksum = unchecked(checksum + value.X + value.Y);
            }
        }
        return new ScenarioMeasurement(_entities.Length, checksum);
    }

    public CanonicalState CaptureCanonicalState() =>
        V2WorldSupport.CaptureState(_world, _descriptor, _entities);

    public void Dispose() { }
}

internal sealed class V2ReadQueryWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly Entity[] _entities;
    private readonly int _componentCount;

    internal V2ReadQueryWorkload(ScenarioDescriptor descriptor, int componentCount)
    {
        _descriptor = descriptor;
        _componentCount = componentCount;
        LogicalComponents components = LogicalComponents.Position;
        if (componentCount >= 2)
            components |= LogicalComponents.Velocity;
        if (componentCount >= 3)
            components |= LogicalComponents.Health;
        _world = V2WorldSupport.CreateWorld();
        _entities = V2WorldSupport.Seed(_world, descriptor.Scale, components);
        _ = ExecuteCore();
    }

    public ScenarioMeasurement Execute()
    {
        long checksum = ExecuteCore();
        return new ScenarioMeasurement(_entities.Length, checksum);
    }

    private long ExecuteCore()
    {
        long checksum = 0;
        int visited = 0;
        if (_componentCount == 1)
        {
            foreach (World.QueryChunk<Position> chunk in _world.QueryChunks<Position>())
            {
                for (int row = 0; row < chunk.Component1.Length; row++)
                {
                    checksum = unchecked(checksum + chunk.Component1[row].X + chunk.Component1[row].Y);
                    visited++;
                }
            }
        }
        else if (_componentCount == 2)
        {
            foreach (World.QueryChunk<Position, Velocity> chunk in _world.QueryChunks<Position, Velocity>())
            {
                for (int row = 0; row < chunk.Component1.Length; row++)
                {
                    checksum = unchecked(checksum + chunk.Component1[row].X + chunk.Component1[row].Y + chunk.Component2[row].X + chunk.Component2[row].Y);
                    visited++;
                }
            }
        }
        else
        {
            foreach (World.QueryChunk<Position, Velocity, Health> chunk in _world.QueryChunks<Position, Velocity, Health>())
            {
                for (int row = 0; row < chunk.Component1.Length; row++)
                {
                    checksum = unchecked(checksum + chunk.Component1[row].X + chunk.Component1[row].Y + chunk.Component2[row].X + chunk.Component2[row].Y + chunk.Component3[row].Value);
                    visited++;
                }
            }
        }
        if (visited != _entities.Length)
            throw new InvalidOperationException("V2 query traversal lost rows.");
        return checksum;
    }

    public CanonicalState CaptureCanonicalState() =>
        V2WorldSupport.CaptureState(_world, _descriptor, _entities);

    public void Dispose() { }
}

internal sealed class V2WriteQueryWorkload : IComparisonWorkload
{
    private readonly ScenarioDescriptor _descriptor;
    private readonly World _world;
    private readonly Entity[] _entities;
    private readonly PatchChunkAction<Position, Velocity> _patch;
    private long _checksum;

    internal V2WriteQueryWorkload(ScenarioDescriptor descriptor)
    {
        _descriptor = descriptor;
        _world = V2WorldSupport.CreateWorld();
        _entities = V2WorldSupport.Seed(
            _world,
            descriptor.Scale,
            LogicalComponents.Position | LogicalComponents.Velocity);
        _patch = Patch;
        _ = _world.PatchChunks(_patch);
        for (int ordinal = 0; ordinal < _entities.Length; ordinal++)
            _world.SetComponent(_entities[ordinal], new Velocity(ordinal + 2, ordinal + 3));
        V2WorldSupport.DrainAllDirty(_world, descriptor.Scale);
    }

    public ScenarioMeasurement Execute()
    {
        _checksum = 0;
        int visited = _world.PatchChunks(_patch);
        if (visited != _entities.Length)
            throw new InvalidOperationException("V2 write query lost rows.");
        return new ScenarioMeasurement(visited, _checksum);
    }

    private void Patch(ReadOnlySpan<Entity> entities, ReadOnlySpan<Position> positions, Span<Velocity> velocities)
    {
        long checksum = 0;
        for (int row = 0; row < positions.Length; row++)
        {
            Position position = positions[row];
            velocities[row] = new Velocity(position.X * 2, position.Y * 2);
            checksum = unchecked(checksum + position.X + position.Y);
        }
        _checksum = unchecked(_checksum + checksum);
    }

    public CanonicalState CaptureCanonicalState() =>
        V2WorldSupport.CaptureState(_world, _descriptor, _entities);

    public void Dispose() { }
}
