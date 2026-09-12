using UltraSim.Game.Protocol;

namespace UltraSim.Server.ConceptDemo;

/// <summary>Finite PF3 execution/delivery policy for the current Concept Demo deployment.</summary>
public static class ConceptDemoServerPerformanceProfiles
{
    public const ulong Revision = 1;
    public const int DefaultMaximumNetworkServiceIntervalMicroseconds = 2_000;

    public static RemoteServerPerformanceProfile Create(
        int simulationFrequencyHz,
        int maximumInputHz = 60,
        int recommendedInputHz = 30,
        int inputBurstCapacity = 10,
        int inputBurstWindowMilliseconds = 100,
        int maximumNetworkServiceIntervalMicroseconds =
            DefaultMaximumNetworkServiceIntervalMicroseconds)
    {
        if (!RemoteServerPerformanceProfile.TryCreate(
                Revision,
                simulationFrequencyHz,
                maximumNetworkServiceIntervalMicroseconds,
                maximumInputHz,
                recommendedInputHz,
                inputBurstCapacity,
                inputBurstWindowMilliseconds,
                simulationFrequencyHz,
                simulationFrequencyHz,
                simulationFrequencyHz,
                simulationFrequencyHz,
                interpolationBufferMilliseconds: 100,
                out RemoteServerPerformanceProfile? profile))
            throw new ArgumentException(
                "The requested Concept Demo server performance profile is invalid.");
        return profile!;
    }
}
