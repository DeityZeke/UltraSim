namespace UltraSim.Network;

[Flags]
public enum ChatMessageFlags : ushort
{
    None = 0,
    System = 1 << 0,
    Local = 1 << 1,
    Party = 1 << 2,
    Guild = 1 << 3,
    Whisper = 1 << 4,
    Staff = 1 << 5,
    Error = 1 << 6,
    Overhead = 1 << 7,
    Announcement = 1 << 8,
}
