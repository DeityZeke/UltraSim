namespace UltraSim.Server.Accounting;

public interface IAccountAuditSink
{
    void Record(AccountLoginAuditEntry entry);
}
