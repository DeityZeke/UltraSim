using UltraSim.Network;

namespace UltraSim.Game.Protocol;

/// <summary>
/// Application-owned extension handler for authenticated game messages not
/// consumed by the established Admission/UI binding.
/// </summary>
public interface IRemoteGameProtocolHandler
{
    /// <summary>Returns whether the message family belonged to this handler.</summary>
    bool TryProcess(in NetworkDeliveredMessage message, ReadOnlySpan<byte> payload);
}
