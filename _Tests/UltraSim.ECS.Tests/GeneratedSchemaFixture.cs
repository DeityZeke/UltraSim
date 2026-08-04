using UltraSim.ECS;

namespace UltraSim.ECS.Tests;

[EcsComponent("core.position")]
internal struct Position : IComponent<Position>
{
    public float X { get; set; }
    public float Y { get; set; }
}

[EcsComponent(
    "core.velocity",
    Version = 2,
    IsEnableable = true,
    PersistenceId = 10)]
internal struct Velocity : IComponent<Velocity>
{
    public double X { get; set; }
    public double Y { get; set; }
}

[EcsComponent("gameplay.disabled", IsTag = true)]
internal struct DisabledTag : IComponent<DisabledTag>
{
}

[EcsComponent("test.not-in-schema")]
internal struct NotInSchema : IComponent<NotInSchema>
{
    public int Value { get; set; }
}

// Deliberately shuffled: generated IDs must depend on stable keys, not declaration order.
[EcsSchema(typeof(DisabledTag), typeof(Velocity), typeof(Position))]
internal static partial class RuntimeTestSchema
{
}

[EcsComponent("index.00")]
internal struct Indexed00 : IComponent<Indexed00> { }
[EcsComponent("index.01")]
internal struct Indexed01 : IComponent<Indexed01> { }
[EcsComponent("index.02")]
internal struct Indexed02 : IComponent<Indexed02> { }
[EcsComponent("index.03")]
internal struct Indexed03 : IComponent<Indexed03> { }
[EcsComponent("index.04")]
internal struct Indexed04 : IComponent<Indexed04> { }
[EcsComponent("index.05", PersistenceId = 105)]
internal struct Indexed05 : IComponent<Indexed05> { }
[EcsComponent("index.06")]
internal struct Indexed06 : IComponent<Indexed06> { }
[EcsComponent("index.07")]
internal struct Indexed07 : IComponent<Indexed07> { }
[EcsComponent("index.08")]
internal struct Indexed08 : IComponent<Indexed08> { }
[EcsComponent("index.09")]
internal struct Indexed09 : IComponent<Indexed09> { }
[EcsComponent("index.10")]
internal struct Indexed10 : IComponent<Indexed10> { }
[EcsComponent("index.11", PersistenceId = 111)]
internal struct Indexed11 : IComponent<Indexed11> { }

[EcsSchema(
    typeof(Indexed11), typeof(Indexed03), typeof(Indexed08), typeof(Indexed00),
    typeof(Indexed06), typeof(Indexed02), typeof(Indexed10), typeof(Indexed05),
    typeof(Indexed01), typeof(Indexed09), typeof(Indexed04), typeof(Indexed07))]
internal static partial class IndexedTestSchema
{
}
