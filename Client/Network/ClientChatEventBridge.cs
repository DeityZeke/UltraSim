using UltraSim.Network;

namespace UltraSim.Client;

public sealed class ClientChatEventBridge
{
    private readonly Action<ChatMessageEventPacket>? _applyMessage;

    public ClientChatEventBridge(Action<ChatMessageEventPacket>? applyMessage = null)
    {
        _applyMessage = applyMessage;
    }

    public int LastReceivedMessageCount { get; private set; }
    public int LastAppliedMessageCount { get; private set; }
    public ulong LastMessageId { get; private set; }

    public void BeginPump()
    {
        LastReceivedMessageCount = 0;
        LastAppliedMessageCount = 0;
    }

    public bool TryProcessEventPayload(ReadOnlySpan<byte> payload)
    {
        if (!PacketCodec.TryDecodeEnvelope(payload, out PacketEnvelopeHeader header, out ReadOnlySpan<byte> innerPayload) ||
            header.PacketId != ChatMessageEventPacket.PacketId)
        {
            return false;
        }

        var reader = new BitReader(innerPayload);
        ChatMessageEventPacket packet = ChatMessageEventPacket.Deserialize(ref reader);
        LastReceivedMessageCount++;
        LastAppliedMessageCount++;
        LastMessageId = packet.MessageId;
        _applyMessage?.Invoke(packet);
        return true;
    }
}
