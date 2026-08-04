using UltraSim.ECS;

namespace UltraSim.ECS.Benchmarks;

[EcsComponent("benchmark.position")]
internal struct BenchmarkPosition : IComponent<BenchmarkPosition>
{
    public float X { get; set; }
    public float Y { get; set; }
}

[EcsComponent("benchmark.velocity", IsEnableable = true, PersistenceId = 1)]
internal struct BenchmarkVelocity : IComponent<BenchmarkVelocity>
{
    public float X { get; set; }
    public float Y { get; set; }
}

[EcsComponent("benchmark.disabled", IsTag = true)]
internal struct BenchmarkDisabled : IComponent<BenchmarkDisabled>
{
}

[EcsSchema(typeof(BenchmarkVelocity), typeof(BenchmarkDisabled), typeof(BenchmarkPosition))]
internal static partial class BenchmarkSchema
{
}
