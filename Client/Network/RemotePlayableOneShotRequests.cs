using UltraSim.Game.Protocol;

namespace UltraSim.Client.Network;

/// <summary>
/// Retains one-shot playable requests until the ordinary client command queue
/// accepts the command which carries them. This is client-local input state;
/// it neither proves delivery nor changes Game authority.
/// </summary>
internal struct RemotePlayableOneShotRequests
{
    private RemotePlayableCommandFlags _pending;

    internal readonly RemotePlayableCommandFlags Pending => _pending;

    internal void RequestInventory() =>
        _pending = RemotePlayableCommandFlags.OpenInventoryRequested;

    internal void RequestServerMonitor() =>
        _pending = RemotePlayableCommandFlags.OpenServerMonitorRequested;

    internal void ObserveSubmission(bool submitted)
    {
        if (submitted) _pending = RemotePlayableCommandFlags.None;
    }
}
