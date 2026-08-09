namespace UltraSim.ECS;

/// <summary>Raised when a fixed structural command arena cannot accept another record.</summary>
public sealed class StructuralCommandCapacityExceededException : InvalidOperationException
{
    public StructuralCommandCapacityExceededException(int capacity)
        : base($"Structural command capacity {capacity} is exhausted.")
    {
        Capacity = capacity;
    }

    public int Capacity { get; }
}
