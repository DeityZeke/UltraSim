namespace UltraSim.Network;

[Flags]
public enum PacketHeaderFlags : byte
{
    None = 0,
    Compressed = 1 << 0,
    Encrypted = 1 << 1,
    HighPriorityHint = 1 << 2,
}
