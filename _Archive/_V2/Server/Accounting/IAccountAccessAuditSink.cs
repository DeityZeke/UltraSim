namespace UltraSim.Server.Accounting;

public interface IAccountAccessAuditSink
{
    void Record(AccountAccessAuditEntry entry);
}
