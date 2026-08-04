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

[EcsComponent("benchmark.disabled", IsTag = true, IsEnableable = true)]
internal struct BenchmarkDisabled : IComponent<BenchmarkDisabled>
{
}

[EcsComponent("benchmark.wide")]
internal struct BenchmarkWide : IComponent<BenchmarkWide>
{
    public long A { get; set; }
    public long B { get; set; }
    public long C { get; set; }
    public long D { get; set; }
    public long E { get; set; }
    public long F { get; set; }
    public long G { get; set; }
    public long H { get; set; }
}

[EcsSchema(
    typeof(BenchmarkVelocity),
    typeof(BenchmarkDisabled),
    typeof(BenchmarkWide),
    typeof(BenchmarkPosition))]
internal static partial class BenchmarkSchema
{
}
