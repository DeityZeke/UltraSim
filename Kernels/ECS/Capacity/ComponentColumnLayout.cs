namespace UltraSim.ECS;

/// <summary>
/// Physical placement of one component in a planned SoA chunk.
/// </summary>
public readonly struct ComponentColumnLayout : IEquatable<ComponentColumnLayout>
{
    public const int NoOffset = -1;

    internal ComponentColumnLayout(
        ComponentId componentId,
        ComponentFlags flags,
        int elementSizeBytes,
        int effectiveAlignmentBytes,
        int dataOffsetBytes = NoOffset,
        int dataBytes = 0,
        int enableBitsOffsetBytes = NoOffset,
        int enableBitsBytes = 0)
    {
        ComponentId = componentId;
        Flags = flags;
        ElementSizeBytes = elementSizeBytes;
        EffectiveAlignmentBytes = effectiveAlignmentBytes;
        DataOffsetBytes = dataOffsetBytes;
        DataBytes = dataBytes;
        EnableBitsOffsetBytes = enableBitsOffsetBytes;
        EnableBitsBytes = enableBitsBytes;
    }

    public ComponentId ComponentId { get; }

    public ComponentFlags Flags { get; }

    public int ElementSizeBytes { get; }

    /// <summary>Maximum of natural component alignment and configured minimum column alignment.</summary>
    public int EffectiveAlignmentBytes { get; }

    public int DataOffsetBytes { get; }

    public int DataBytes { get; }

    public int EnableBitsOffsetBytes { get; }

    public int EnableBitsBytes { get; }

    public bool HasData => ElementSizeBytes != 0;

    public bool HasEnableBits => (Flags & ComponentFlags.Enableable) != 0;

    internal ComponentColumnLayout WithPlacement(
        int dataOffsetBytes,
        int dataBytes,
        int enableBitsOffsetBytes,
        int enableBitsBytes) => new(
            ComponentId,
            Flags,
            ElementSizeBytes,
            EffectiveAlignmentBytes,
            dataOffsetBytes,
            dataBytes,
            enableBitsOffsetBytes,
            enableBitsBytes);

    public bool Equals(ComponentColumnLayout other) =>
        ComponentId == other.ComponentId &&
        Flags == other.Flags &&
        ElementSizeBytes == other.ElementSizeBytes &&
        EffectiveAlignmentBytes == other.EffectiveAlignmentBytes &&
        DataOffsetBytes == other.DataOffsetBytes &&
        DataBytes == other.DataBytes &&
        EnableBitsOffsetBytes == other.EnableBitsOffsetBytes &&
        EnableBitsBytes == other.EnableBitsBytes;

    public override bool Equals(object? obj) => obj is ComponentColumnLayout other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        ComponentId,
        Flags,
        ElementSizeBytes,
        EffectiveAlignmentBytes,
        DataOffsetBytes,
        DataBytes,
        EnableBitsOffsetBytes,
        EnableBitsBytes);
}
