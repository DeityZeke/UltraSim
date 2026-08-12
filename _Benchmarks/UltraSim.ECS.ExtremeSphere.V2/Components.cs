using UltraSim.ECS;

namespace UltraSim.ECS.ExtremeSphere.V2;

internal struct SpherePosition : IComponent<SpherePosition>
{
    public float X;
    public float Y;
    public float Z;
}

internal struct SphereVelocity : IComponent<SphereVelocity>
{
    public float X;
    public float Y;
    public float Z;
}

internal struct SpherePulse : IComponent<SpherePulse>
{
    public float Speed;
    public float Frequency;
    public float Phase;
}
