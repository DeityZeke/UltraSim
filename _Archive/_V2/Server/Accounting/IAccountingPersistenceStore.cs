namespace UltraSim.Server.Accounting;

public interface IAccountingPersistenceStore
{
    AccountingPersistenceSnapshot Load();

    void Save(AccountingPersistenceSnapshot snapshot);
}
