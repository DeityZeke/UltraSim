using UltraSim.ECS;

namespace UltraSim.Server.Gameplay;

public readonly record struct HumanProfile(byte Variant) : IComponent<HumanProfile>;
