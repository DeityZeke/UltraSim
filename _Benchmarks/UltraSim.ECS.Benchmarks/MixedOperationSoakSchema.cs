using UltraSim.ECS;

namespace UltraSim.ECS.Benchmarks;

[EcsComponent("soak.identity", PersistenceId = 101)]
internal struct SoakIdentity : IComponent<SoakIdentity>
{
    public int Value { get; set; }
}

[EcsComponent("soak.position", PersistenceId = 102)]
internal struct SoakPosition : IComponent<SoakPosition>
{
    public float X { get; set; }
    public float Y { get; set; }
}

[EcsComponent("soak.velocity", IsEnableable = true, PersistenceId = 103)]
internal struct SoakVelocity : IComponent<SoakVelocity>
{
    public float X { get; set; }
    public float Y { get; set; }
}

[EcsComponent("soak.health", PersistenceId = 104)]
internal struct SoakHealth : IComponent<SoakHealth>
{
    public int Value { get; set; }
}

[EcsComponent("soak.activity", PersistenceId = 105)]
internal struct SoakActivity : IComponent<SoakActivity>
{
    public int Value { get; set; }
}

[EcsComponent("soak.wide", PersistenceId = 106)]
internal struct SoakWide : IComponent<SoakWide>
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
    typeof(SoakIdentity),
    typeof(SoakPosition),
    typeof(SoakVelocity),
    typeof(SoakHealth),
    typeof(SoakActivity),
    typeof(SoakWide))]
internal static partial class MixedOperationSoakSchema
{
}
