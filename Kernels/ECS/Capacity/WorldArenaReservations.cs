namespace UltraSim.ECS;

/// <summary>
/// Explicit bounded reservations whose concrete arena implementations arrive in a later slice.
/// </summary>
public readonly struct WorldArenaReservations : IEquatable<WorldArenaReservations>
{
    public WorldArenaReservations(
        long structuralCommandBytes,
        long changeTrackingBytes,
        long scratchBytes)
    {
        if (structuralCommandBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(structuralCommandBytes));
        if (changeTrackingBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(changeTrackingBytes));
        if (scratchBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(scratchBytes));

        checked
        {
            StructuralCommandBytes = structuralCommandBytes;
            ChangeTrackingBytes = changeTrackingBytes;
            ScratchBytes = scratchBytes;
            TotalBytes = structuralCommandBytes + changeTrackingBytes + scratchBytes;
        }
    }

    public long StructuralCommandBytes { get; }

    public long ChangeTrackingBytes { get; }

    public long ScratchBytes { get; }

    public long TotalBytes { get; }

    public bool Equals(WorldArenaReservations other) =>
        StructuralCommandBytes == other.StructuralCommandBytes &&
        ChangeTrackingBytes == other.ChangeTrackingBytes &&
        ScratchBytes == other.ScratchBytes;

    public override bool Equals(object? obj) => obj is WorldArenaReservations other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(
        StructuralCommandBytes,
        ChangeTrackingBytes,
        ScratchBytes);
}
