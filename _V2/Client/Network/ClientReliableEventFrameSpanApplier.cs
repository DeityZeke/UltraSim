namespace UltraSim.Client;

public delegate void ClientReliableEventFrameSpanApplier(
    ulong sequence,
    ReadOnlySpan<byte> payload);
