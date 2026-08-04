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
[EcsComponent("index.12")]
internal struct Indexed12 : IComponent<Indexed12> { }
[EcsComponent("index.13")]
internal struct Indexed13 : IComponent<Indexed13> { }
[EcsComponent("index.14")]
internal struct Indexed14 : IComponent<Indexed14> { }
[EcsComponent("index.15")]
internal struct Indexed15 : IComponent<Indexed15> { }
[EcsComponent("index.16")]
internal struct Indexed16 : IComponent<Indexed16> { }
[EcsComponent("index.17")]
internal struct Indexed17 : IComponent<Indexed17> { }
[EcsComponent("index.18")]
internal struct Indexed18 : IComponent<Indexed18> { }
[EcsComponent("index.19")]
internal struct Indexed19 : IComponent<Indexed19> { }

[EcsSchema(
    typeof(Indexed11), typeof(Indexed03), typeof(Indexed08), typeof(Indexed00),
    typeof(Indexed06), typeof(Indexed02), typeof(Indexed10), typeof(Indexed05),
    typeof(Indexed01), typeof(Indexed09), typeof(Indexed04), typeof(Indexed07),
    typeof(Indexed19), typeof(Indexed12), typeof(Indexed17), typeof(Indexed14),
    typeof(Indexed16), typeof(Indexed13), typeof(Indexed18), typeof(Indexed15))]
internal static partial class IndexedTestSchema
{
}

[EcsComponent("layout.byte")]
internal struct LayoutByte : IComponent<LayoutByte>
{
    public byte Value { get; set; }
}

[EcsComponent("layout.enable", IsEnableable = true)]
internal struct LayoutEnableLong : IComponent<LayoutEnableLong>
{
    public long Value { get; set; }
}

[EcsComponent("layout.int")]
internal struct LayoutInt : IComponent<LayoutInt>
{
    public int Value { get; set; }
}

[EcsComponent("layout.tag", IsTag = true, IsEnableable = true)]
internal struct LayoutTag : IComponent<LayoutTag>
{
}

[EcsComponent("layout.wide")]
internal struct LayoutWide : IComponent<LayoutWide>
{
    public long A { get; set; }
    public long B { get; set; }
    public long C { get; set; }
    public long D { get; set; }
}

[EcsSchema(
    typeof(LayoutTag),
    typeof(LayoutInt),
    typeof(LayoutWide),
    typeof(LayoutByte),
    typeof(LayoutEnableLong))]
internal static partial class LayoutTestSchema
{
}
