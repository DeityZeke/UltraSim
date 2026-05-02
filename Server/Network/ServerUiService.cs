using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Server.Network;

public sealed class ServerUiService
{
    private readonly OrderedEventStream _orderedEvents;

    public ServerUiService(OrderedEventStream orderedEvents)
    {
        _orderedEvents = orderedEvents ?? throw new ArgumentNullException(nameof(orderedEvents));
    }

    public long PublishedPanelOpens { get; private set; }
    public long PublishedModelUpdates { get; private set; }
    public long RejectedPanelOpens { get; private set; }
    public long RejectedModelUpdates { get; private set; }
    public long ReceivedActions { get; private set; }
    public long RejectedActions { get; private set; }

    public event Action<ConnectionId, UiActionRequestDocument>? ActionReceived;

    public bool TryOpenPanel(ConnectionId connectionId, UiBuiltPanel panel, string panelInstanceId)
    {
        ArgumentNullException.ThrowIfNull(panel);

        return TryOpenPanel(connectionId, panel.ToColdOpen(panelInstanceId));
    }

    public bool TryOpenPanel(ConnectionId connectionId, UiPanelOpenDocument document)
    {
        if (!connectionId.IsValid)
        {
            RejectedPanelOpens++;
            return false;
        }

        byte[] documentBytes = UiContractJsonCodec.Encode(document);
        _orderedEvents.Publish(
            ServerUiScopes.ForConnection(connectionId),
            PacketClass.ReliableEvent,
            PacketCodec.Encode(new UiPanelOpenEventPacket(documentBytes)));
        PublishedPanelOpens++;
        return true;
    }

    public bool TryUpdatePanel(ConnectionId connectionId, UiModelUpdateDocument update)
    {
        if (!connectionId.IsValid)
        {
            RejectedModelUpdates++;
            return false;
        }

        byte[] documentBytes = UiContractJsonCodec.Encode(update);
        _orderedEvents.Publish(
            ServerUiScopes.ForConnection(connectionId),
            PacketClass.ReliableEvent,
            PacketCodec.Encode(new UiModelUpdateEventPacket(documentBytes)));
        PublishedModelUpdates++;
        return true;
    }

    public bool TryProcessAction(ConnectionId connectionId, ReadOnlySpan<byte> payload, out UiActionRequestDocument? document)
    {
        document = null;
        if (!connectionId.IsValid)
        {
            RejectedActions++;
            return false;
        }

        var reader = new BitReader(payload);
        UiActionRequestPacket packet = UiActionRequestPacket.Deserialize(ref reader);
        document = UiContractJsonCodec.Decode<UiActionRequestDocument>(packet.DocumentBytes);
        ReceivedActions++;
        ActionReceived?.Invoke(connectionId, document);
        return true;
    }
}
