namespace UltraSim.Network;

public readonly record struct OutboundAssemblyResult(
    int Count,
    int CompressedCount,
    int EncryptedCount);
