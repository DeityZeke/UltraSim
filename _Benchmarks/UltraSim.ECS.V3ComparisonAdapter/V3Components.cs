using UltraSim.ECS;

namespace UltraSim.ECS.V3ComparisonAdapter;

[EcsComponent("comparison.position")]
internal struct Position : IComponent<Position>
{
    public int X;
    public int Y;
}

[EcsComponent("comparison.velocity")]
internal struct Velocity : IComponent<Velocity>
{
    public int X;
    public int Y;
}

[EcsComponent("comparison.health")]
internal struct Health : IComponent<Health>
{
    public int Value { get; set; }
}

[EcsComponent("comparison.scalar")]
internal struct Scalar : IComponent<Scalar>
{
    public int Value;
}

// A deliberately wide, blittable component used to measure bytes avoided by
// barrier-local row reuse rather than a narrow-row metadata microbenchmark.
[EcsComponent("comparison.wide-block-64")]
internal struct WideBlock64 : IComponent<WideBlock64>
{
    public long Value0;
    public long Value1;
    public long Value2;
    public long Value3;
    public long Value4;
    public long Value5;
    public long Value6;
    public long Value7;
}

[EcsSchema(typeof(Position), typeof(Velocity), typeof(Health), typeof(Scalar), typeof(WideBlock64))]
internal static partial class ComparisonSchema
{
}
