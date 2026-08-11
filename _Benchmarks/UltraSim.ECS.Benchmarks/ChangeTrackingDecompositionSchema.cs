using UltraSim.ECS;

namespace UltraSim.ECS.Benchmarks;

[EcsComponent("benchmark.change-decomposition.identity")]
internal struct ChangeDecompositionIdentity : IComponent<ChangeDecompositionIdentity>
{
    public int Value { get; set; }
}

[EcsComponent("benchmark.change-decomposition.raw")]
internal struct ChangeDecompositionRaw : IComponent<ChangeDecompositionRaw>
{
    public int Value { get; set; }
}

[EcsComponent("benchmark.change-decomposition.dense")]
internal struct ChangeDecompositionDense : IComponent<ChangeDecompositionDense>
{
    public int Value { get; set; }
}

[EcsComponent("benchmark.change-decomposition.exact")]
internal struct ChangeDecompositionExact : IComponent<ChangeDecompositionExact>
{
    public int Value { get; set; }
}

[EcsSchema(
    typeof(ChangeDecompositionIdentity),
    typeof(ChangeDecompositionRaw),
    typeof(ChangeDecompositionDense),
    typeof(ChangeDecompositionExact))]
internal static partial class ChangeTrackingDecompositionSchema
{
}
