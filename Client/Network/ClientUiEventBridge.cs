using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Client;

public sealed class ClientUiEventBridge
{
    private readonly Action<UiPanelOpenDocument>? _openPanel;
    private readonly Action<UiModelUpdateDocument>? _modelUpdate;

    public ClientUiEventBridge(
        Action<UiPanelOpenDocument>? openPanel = null,
        Action<UiModelUpdateDocument>? modelUpdate = null)
    {
        _openPanel = openPanel;
        _modelUpdate = modelUpdate;
    }

    public int LastReceivedOpenCount { get; private set; }
    public int LastAppliedOpenCount { get; private set; }
    public int LastReceivedModelUpdateCount { get; private set; }
    public int LastAppliedModelUpdateCount { get; private set; }
    public string LastPanelInstanceId { get; private set; } = string.Empty;

    public void BeginPump()
    {
        LastReceivedOpenCount = 0;
        LastAppliedOpenCount = 0;
        LastReceivedModelUpdateCount = 0;
        LastAppliedModelUpdateCount = 0;
    }

    public bool TryProcessEventPayload(ReadOnlySpan<byte> payload)
    {
        if (!PacketCodec.TryDecodeEnvelope(payload, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> innerPayload))
        {
            return false;
        }

        if (header.PacketId == UiPanelOpenEventPacket.PacketId)
        {
            var reader = new BitReader(innerPayload);
            UiPanelOpenEventPacket packet = UiPanelOpenEventPacket.Deserialize(ref reader);
            UiPanelOpenDocument document = UiContractJsonCodec.Decode<UiPanelOpenDocument>(packet.DocumentBytes);
            LastReceivedOpenCount++;
            LastAppliedOpenCount++;
            LastPanelInstanceId = document.PanelInstanceId;
            _openPanel?.Invoke(document);
            return true;
        }

        if (header.PacketId == UiModelUpdateEventPacket.PacketId)
        {
            var reader = new BitReader(innerPayload);
            UiModelUpdateEventPacket packet = UiModelUpdateEventPacket.Deserialize(ref reader);
            UiModelUpdateDocument document = UiContractJsonCodec.Decode<UiModelUpdateDocument>(packet.DocumentBytes);
            LastReceivedModelUpdateCount++;
            LastAppliedModelUpdateCount++;
            LastPanelInstanceId = document.PanelInstanceId;
            _modelUpdate?.Invoke(document);
            return true;
        }

        return false;
    }
}
