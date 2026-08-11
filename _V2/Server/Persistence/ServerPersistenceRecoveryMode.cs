namespace UltraSim.Server.Persistence;

public enum ServerPersistenceRecoveryMode
{
    CurrentThenBackups = 0,
    CurrentOnly = 1,
    RequireCurrent = 2,
}
