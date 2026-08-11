namespace UltraSim.Network;

public readonly record struct HandshakePolicy(
    uint ProtocolVersion,
    ulong SchemaHash,
    ulong ContentHash,
    FeatureFlags RequiredFeatures);
