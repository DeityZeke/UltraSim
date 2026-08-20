namespace UltraSim.Server;

public enum ServerCommandResultKind : byte
{
    Accepted = 0,
    Rejected = 1,
    NoOp = 2,
    Unknown = 3,
}
