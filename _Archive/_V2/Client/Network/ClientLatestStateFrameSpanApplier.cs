namespace UltraSim.Client;

public delegate void ClientLatestStateFrameSpanApplier(ulong itemKey, ulong sequence, ReadOnlySpan<byte> payload);
