namespace UltraSim.Network;

/// <summary>
/// Logical semantic plane for network traffic.
/// </summary>
public enum NetworkPlane : byte
{
    Control = 0,
    Input = 1,
    State = 2,
}
