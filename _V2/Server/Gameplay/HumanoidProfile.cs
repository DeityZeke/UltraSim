using UltraSim.ECS;

namespace UltraSim.Server.Gameplay;

public readonly record struct HumanoidProfile(byte SkeletonKind) : IComponent<HumanoidProfile>;
