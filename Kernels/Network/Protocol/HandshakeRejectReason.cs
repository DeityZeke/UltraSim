namespace UltraSim.Network;

public enum HandshakeRejectReason : byte
{
    None = 0,
    ProtocolVersionMismatch = 1,
    SchemaHashMismatch = 2,
    ContentHashMismatch = 3,
    MissingRequiredFeatures = 4,
}
