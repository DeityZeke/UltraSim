namespace UltraSim.ECS.Comparison;

[Flags]
public enum LogicalComponents : uint
{
    None = 0,
    Position = 1 << 0,
    Velocity = 1 << 1,
    Health = 1 << 2,
    Scalar = 1 << 3,
}

public struct LogicalStateBuilder
{
    private CanonicalStateHasher _hash;
    private readonly int _livingEntities;
    private readonly int _logicalArchetypes;
    private readonly int _changedEntities;
    private readonly int _reusedSlots;
    private int _nextEntityOrdinal;
    private int _nextChangedOrdinal;

    public LogicalStateBuilder(
        string scenarioId,
        int scale,
        int livingEntities,
        int logicalArchetypes,
        int changedEntities = 0,
        int reusedSlots = 0)
    {
        _hash = new CanonicalStateHasher();
        _hash.AddString("UltraSim.ECS.LogicalState.v1");
        _hash.AddString(scenarioId);
        _hash.AddInt32(scale);
        _hash.AddInt32(livingEntities);
        _hash.AddInt32(logicalArchetypes);
        _hash.AddInt32(changedEntities);
        _hash.AddInt32(reusedSlots);
        _livingEntities = livingEntities;
        _logicalArchetypes = logicalArchetypes;
        _changedEntities = changedEntities;
        _reusedSlots = reusedSlots;
        _nextEntityOrdinal = 0;
        _nextChangedOrdinal = 0;
    }

    public void AddEntity(
        int ordinal,
        bool alive,
        int lifecycleRevision,
        LogicalComponents components,
        int positionX = 0,
        int positionY = 0,
        int velocityX = 0,
        int velocityY = 0,
        int health = 0,
        int scalar = 0,
        uint enabledComponents = uint.MaxValue)
    {
        if (ordinal != _nextEntityOrdinal++)
            throw new InvalidOperationException("Canonical entities must be written in ascending logical order.");
        _hash.AddByte(0xE1);
        _hash.AddInt32(ordinal);
        _hash.AddBoolean(alive);
        _hash.AddInt32(lifecycleRevision);
        _hash.AddUInt32((uint)components);
        _hash.AddUInt32(enabledComponents);
        if ((components & LogicalComponents.Position) != 0)
        {
            _hash.AddInt32(positionX);
            _hash.AddInt32(positionY);
        }
        if ((components & LogicalComponents.Velocity) != 0)
        {
            _hash.AddInt32(velocityX);
            _hash.AddInt32(velocityY);
        }
        if ((components & LogicalComponents.Health) != 0)
            _hash.AddInt32(health);
        if ((components & LogicalComponents.Scalar) != 0)
            _hash.AddInt32(scalar);
    }

    public void AddArchetype(LogicalComponents signature, int population)
    {
        _hash.AddByte(0xA7);
        _hash.AddUInt32((uint)signature);
        _hash.AddInt32(population);
    }

    public void AddChangedEntity(int ordinal)
    {
        if (ordinal < _nextChangedOrdinal)
            throw new InvalidOperationException("Changed logical entities must be written in ascending order.");
        _nextChangedOrdinal = ordinal + 1;
        _hash.AddByte(0xC3);
        _hash.AddInt32(ordinal);
    }

    public CanonicalState Finish()
    {
        _hash.AddByte(0xFF);
        return new CanonicalState(
            _hash.Finish(),
            _livingEntities,
            _logicalArchetypes,
            _changedEntities,
            _reusedSlots);
    }
}
