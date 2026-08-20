namespace UltraSim.Network;

[Flags]
public enum FeatureFlags : uint
{
    None = 0,
    Compression = 1 << 0,
    Encryption = 1 << 1,
    SharedPayloadFanout = 1 << 2,
    BulkTransfer = 1 << 3,
}
