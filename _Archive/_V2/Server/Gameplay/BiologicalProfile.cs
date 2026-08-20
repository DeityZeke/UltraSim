using UltraSim.ECS;

namespace UltraSim.Server.Gameplay;

public enum BiologicalSex : byte
{
    Unknown = 0,
    Male = 1,
    Female = 2,
    Neuter = 3,
}

public readonly record struct BiologicalProfile(
    BiologicalSex Sex,
    bool CanBreed,
    float Maturity,
    ulong LineageSeed) : IComponent<BiologicalProfile>;
