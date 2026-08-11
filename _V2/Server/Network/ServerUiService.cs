using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Server.Network;

public sealed class ServerUiService
{
    private const int MaxUiDocumentChunkBytes = 1024;
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
        PublishUiDocument(
            connectionId,
            UiDocumentChunkKind.PanelOpen,
            $"open:{document.PanelInstanceId}",
            documentBytes);
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
        PublishUiDocument(
            connectionId,
            UiDocumentChunkKind.ModelUpdate,
            $"update:{update.PanelInstanceId}:{update.Sequence}",
            documentBytes);
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

    private void PublishUiDocument(
        ConnectionId connectionId,
        UiDocumentChunkKind kind,
        string transferId,
        byte[] documentBytes)
    {
        SubscriptionKey scope = ServerUiScopes.ForConnection(connectionId);
        if (documentBytes.Length <= MaxUiDocumentChunkBytes)
        {
            ImmutablePayloadBuffer singlePacket = kind == UiDocumentChunkKind.PanelOpen
                ? PacketCodec.Encode(new UiPanelOpenEventPacket(documentBytes))
                : PacketCodec.Encode(new UiModelUpdateEventPacket(documentBytes));
            _orderedEvents.Publish(scope, PacketClass.ReliableEvent, singlePacket);
            return;
        }

        int chunkCount = (documentBytes.Length + MaxUiDocumentChunkBytes - 1) / MaxUiDocumentChunkBytes;
        if (chunkCount > ushort.MaxValue)
            throw new InvalidOperationException("UI document is too large to chunk.");

        for (int chunkIndex = 0; chunkIndex < chunkCount; chunkIndex++)
        {
            int offset = chunkIndex * MaxUiDocumentChunkBytes;
            int length = Math.Min(MaxUiDocumentChunkBytes, documentBytes.Length - offset);
            var chunk = new byte[length];
            Buffer.BlockCopy(documentBytes, offset, chunk, 0, length);
            _orderedEvents.Publish(
                scope,
                PacketClass.ReliableEvent,
                PacketCodec.Encode(new UiDocumentChunkEventPacket(
                    kind,
                    transferId,
                    checked((ushort)chunkIndex),
                    checked((ushort)chunkCount),
                    chunk)));
        }
    }
}
