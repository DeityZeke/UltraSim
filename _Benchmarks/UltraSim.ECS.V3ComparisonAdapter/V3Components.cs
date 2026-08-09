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

[EcsSchema(typeof(Position), typeof(Velocity), typeof(Health), typeof(Scalar))]
internal static partial class ComparisonSchema
{
}
