namespace UltraSim.ECS;

/// <summary>Deterministic broad phase for one prepared structural transition.</summary>
internal enum StructuralTransitionPhase : byte
{
    Remove = 1,
    Add = 2,
}
