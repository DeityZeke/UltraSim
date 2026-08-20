namespace UltraSim.Network;

/// <summary>
/// Scheduling priority tier for a connection's outbound service.
/// </summary>
public enum NetworkServiceClass : byte
{
    A = 0,
    B = 1,
    C = 2,
    D = 3,
}
