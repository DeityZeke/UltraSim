using UltraSim.ECS;

namespace UltraSim.ECS.ExtremeSphere.V3;

[EcsComponent("benchmark.extreme-sphere.position")]
internal struct SpherePosition : IComponent<SpherePosition>
{
    public float X;
    public float Y;
    public float Z;
}

[EcsComponent("benchmark.extreme-sphere.velocity")]
internal struct SphereVelocity : IComponent<SphereVelocity>
{
    public float X;
    public float Y;
    public float Z;
}

[EcsComponent("benchmark.extreme-sphere.pulse")]
internal struct SpherePulse : IComponent<SpherePulse>
{
    public float Speed;
    public float Frequency;
    public float Phase;
}

[EcsComponent("benchmark.extreme-sphere.render-visible")]
internal struct SphereRenderVisible : IComponent<SphereRenderVisible>
{
    public byte Value;
}

[EcsSchema(typeof(SpherePosition), typeof(SphereVelocity), typeof(SpherePulse), typeof(SphereRenderVisible))]
internal static partial class ExtremeSphereSchema
{
}
