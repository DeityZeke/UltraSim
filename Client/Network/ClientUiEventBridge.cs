using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Client;

public sealed class ClientUiEventBridge
{
    private readonly Action<UiPanelOpenDocument>? _openPanel;

    public ClientUiEventBridge(Action<UiPanelOpenDocument>? openPanel = null)
    {
        _openPanel = openPanel;
    }

    public int LastReceivedOpenCount { get; private set; }
    public int LastAppliedOpenCount { get; private set; }
    public string LastPanelInstanceId { get; private set; } = string.Empty;

    public void BeginPump()
    {
        LastReceivedOpenCount = 0;
        LastAppliedOpenCount = 0;
    }

    public bool TryProcessEventPayload(ReadOnlySpan<byte> payload)
    {
        if (!PacketCodec.TryDecodeEnvelope(payload, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> innerPayload) ||
            header.PacketId != UiPanelOpenEventPacket.PacketId)
        {
            return false;
        }

        var reader = new BitReader(innerPayload);
        UiPanelOpenEventPacket packet = UiPanelOpenEventPacket.Deserialize(ref reader);
        UiPanelOpenDocument document = UiContractJsonCodec.Decode<UiPanelOpenDocument>(packet.DocumentBytes);
        LastReceivedOpenCount++;
        LastAppliedOpenCount++;
        LastPanelInstanceId = document.PanelInstanceId;
        _openPanel?.Invoke(document);
        return true;
    }
}
