namespace UltraSim.ECS;

/// <summary>Location update produced when swap removal moves a donor entity.</summary>
public readonly struct ChunkRowRelocation
{
    internal ChunkRowRelocation(uint entityIndex, ChunkRow source, ChunkRow destination)
    {
        EntityIndex = entityIndex;
        Source = source;
        Destination = destination;
        HasValue = true;
    }

    public bool HasValue { get; }

    public uint EntityIndex { get; }

    public ChunkRow Source { get; }

    public ChunkRow Destination { get; }
}
