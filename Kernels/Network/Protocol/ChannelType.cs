namespace UltraSim.Network;

/// <summary>
/// Delivery behavior requested for a payload.
/// The transport decides how to realize the contract.
/// </summary>
public enum ChannelType : byte
{
    ReliableOrdered = 0,
    ReliableUnordered = 1,
    UnreliableSequenced = 2,
    Unreliable = 3,
}
