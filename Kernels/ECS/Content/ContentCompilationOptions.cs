namespace UltraSim.ECS;

/// <summary>Fallback storage geometry for signatures not already registered by the world.</summary>
public readonly struct ContentCompilationOptions : IEquatable<ContentCompilationOptions>
{
    public ContentCompilationOptions(ChunkLayoutOptions layout, int maximumChunksPerNewArchetype)
    {
        if (!layout.IsValid)
            throw new ArgumentException("Content chunk layout is invalid or default initialized.", nameof(layout));
        if (maximumChunksPerNewArchetype <= 0)
            throw new ArgumentOutOfRangeException(nameof(maximumChunksPerNewArchetype));
        Layout = layout;
        MaximumChunksPerNewArchetype = maximumChunksPerNewArchetype;
    }

    public ChunkLayoutOptions Layout { get; }
    public int MaximumChunksPerNewArchetype { get; }
    public bool IsValid => Layout.IsValid && MaximumChunksPerNewArchetype > 0;

    public bool Equals(ContentCompilationOptions other) =>
        Layout == other.Layout && MaximumChunksPerNewArchetype == other.MaximumChunksPerNewArchetype;
    public override bool Equals(object? obj) => obj is ContentCompilationOptions other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Layout, MaximumChunksPerNewArchetype);
    public static bool operator ==(ContentCompilationOptions left, ContentCompilationOptions right) => left.Equals(right);
    public static bool operator !=(ContentCompilationOptions left, ContentCompilationOptions right) => !left.Equals(right);
}
