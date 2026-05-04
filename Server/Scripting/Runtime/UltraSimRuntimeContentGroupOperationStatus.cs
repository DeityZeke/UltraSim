namespace UltraSim.Server.Scripting;

public enum UltraSimRuntimeContentGroupOperationStatus : byte
{
    Accepted = 0,
    DuplicateKey = 1,
    UnknownKey = 2,
    AlreadyMounted = 3,
    AlreadyUnmounted = 4,
}
