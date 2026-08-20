namespace UltraSim.Server.Network;

public readonly record struct ServerNetworkSessionRuntimeTickResult(
    int ConnectionCount,
    int BaselineDatagramCount,
    int RetransmitDatagramCount,
    int OrderedEventDatagramCount,
    int DeltaDatagramCount,
    int TotalDatagramCount);
