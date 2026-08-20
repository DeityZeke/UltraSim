namespace UltraSim.Network
{
    public readonly struct HandshakePolicy
    {
        public readonly uint ProtocolVersion;
        public readonly ulong SchemaHash;
        public readonly ulong ContentHash;
        public readonly FeatureFlags RequiredFeatures;

        public HandshakePolicy(
            uint protocolVersion,
            ulong schemaHash,
            ulong contentHash,
            FeatureFlags requiredFeatures = FeatureFlags.None)
        {
            ProtocolVersion = protocolVersion;
            SchemaHash = schemaHash;
            ContentHash = contentHash;
            RequiredFeatures = requiredFeatures;
        }
    }
}
