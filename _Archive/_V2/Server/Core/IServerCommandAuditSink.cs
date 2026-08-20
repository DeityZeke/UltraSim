namespace UltraSim.Server;

public interface IServerCommandAuditSink
{
    void Record(ServerCommandAuditEntry entry);
}
