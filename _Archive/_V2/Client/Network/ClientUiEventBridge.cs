using UltraSim.Network;
using UltraSim.UI;

namespace UltraSim.Client;

public sealed class ClientUiEventBridge
{
    private readonly Action<UiPanelOpenDocument>? _openPanel;
    private readonly Action<UiModelUpdateDocument>? _modelUpdate;
    private readonly Dictionary<string, PendingUiDocumentChunks> _pendingChunks = new(StringComparer.Ordinal);

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
            ApplyPanelOpen(packet.DocumentBytes);
            return true;
        }

        if (header.PacketId == UiModelUpdateEventPacket.PacketId)
        {
            var reader = new BitReader(innerPayload);
            UiModelUpdateEventPacket packet = UiModelUpdateEventPacket.Deserialize(ref reader);
            ApplyModelUpdate(packet.DocumentBytes);
            return true;
        }

        if (header.PacketId == UiDocumentChunkEventPacket.PacketId)
        {
            var reader = new BitReader(innerPayload);
            UiDocumentChunkEventPacket packet = UiDocumentChunkEventPacket.Deserialize(ref reader);
            ProcessDocumentChunk(packet);
            return true;
        }

        return false;
    }

    private void ProcessDocumentChunk(in UiDocumentChunkEventPacket packet)
    {
        if (string.IsNullOrWhiteSpace(packet.TransferId) ||
            packet.ChunkCount == 0 ||
            packet.ChunkIndex >= packet.ChunkCount)
        {
            return;
        }

        if (!_pendingChunks.TryGetValue(packet.TransferId, out PendingUiDocumentChunks? pending) ||
            pending.Kind != packet.Kind ||
            pending.ChunkCount != packet.ChunkCount)
        {
            pending = new PendingUiDocumentChunks(packet.Kind, packet.ChunkCount);
            _pendingChunks[packet.TransferId] = pending;
        }

        if (!pending.TryAccept(packet.ChunkIndex, packet.ChunkBytes))
            return;
        if (!pending.IsComplete)
            return;

        byte[] documentBytes = pending.Combine();
        _pendingChunks.Remove(packet.TransferId);
        switch (packet.Kind)
        {
            case UiDocumentChunkKind.PanelOpen:
                ApplyPanelOpen(documentBytes);
                break;
            case UiDocumentChunkKind.ModelUpdate:
                ApplyModelUpdate(documentBytes);
                break;
        }
    }

    private void ApplyPanelOpen(byte[] documentBytes)
    {
        UiPanelOpenDocument document = UiContractJsonCodec.Decode<UiPanelOpenDocument>(documentBytes);
        LastReceivedOpenCount++;
        LastAppliedOpenCount++;
        LastPanelInstanceId = document.PanelInstanceId;
        _openPanel?.Invoke(document);
    }

    private void ApplyModelUpdate(byte[] documentBytes)
    {
        UiModelUpdateDocument document = UiContractJsonCodec.Decode<UiModelUpdateDocument>(documentBytes);
        LastReceivedModelUpdateCount++;
        LastAppliedModelUpdateCount++;
        LastPanelInstanceId = document.PanelInstanceId;
        _modelUpdate?.Invoke(document);
    }

    private sealed class PendingUiDocumentChunks
    {
        private readonly byte[][] _chunks;
        private readonly bool[] _received;
        private int _receivedCount;

        public PendingUiDocumentChunks(UiDocumentChunkKind kind, ushort chunkCount)
        {
            Kind = kind;
            ChunkCount = chunkCount;
            _chunks = new byte[chunkCount][];
            _received = new bool[chunkCount];
        }

        public UiDocumentChunkKind Kind { get; }
        public ushort ChunkCount { get; }
        public bool IsComplete => _receivedCount == ChunkCount;

        public bool TryAccept(ushort chunkIndex, byte[] chunkBytes)
        {
            if (chunkIndex >= ChunkCount || _received[chunkIndex])
                return false;

            _chunks[chunkIndex] = chunkBytes ?? [];
            _received[chunkIndex] = true;
            _receivedCount++;
            return true;
        }

        public byte[] Combine()
        {
            int totalLength = 0;
            for (int i = 0; i < _chunks.Length; i++)
            {
                totalLength += _chunks[i].Length;
            }

            var result = new byte[totalLength];
            int offset = 0;
            for (int i = 0; i < _chunks.Length; i++)
            {
                Buffer.BlockCopy(_chunks[i], 0, result, offset, _chunks[i].Length);
                offset += _chunks[i].Length;
            }

            return result;
        }
    }
}
