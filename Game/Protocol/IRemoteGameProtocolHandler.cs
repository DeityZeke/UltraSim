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

/// <summary>
/// Optional owner-pumped progress for bounded application state retained after
/// transport backpressure, such as application acknowledgements.
/// </summary>
public interface IRemoteGameProtocolProgressor
{
    /// <returns><see langword="true"/> when no retained progress remains blocked.</returns>
    bool TryProgress();
}
