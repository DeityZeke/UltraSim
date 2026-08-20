using UltraSim.Server.Accounting;

namespace UltraSim.Server.Persistence;

public sealed class AccountingPersistenceParticipant : IServerPersistenceParticipant
{
    private readonly IAccountRegistry _accounts;
    private readonly ICharacterRegistry _characters;
    private readonly AccountingPersistenceOptions _options;

    public AccountingPersistenceParticipant(
        IAccountRegistry accounts,
        ICharacterRegistry characters,
        AccountingPersistenceOptions? options = null)
    {
        _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
        _characters = characters ?? throw new ArgumentNullException(nameof(characters));
        _options = options ?? new AccountingPersistenceOptions();
    }

    public string Name => "accounting";

    public ServerPersistenceArtifact Save(string saveSetRoot, ServerPersistenceMetadata metadata)
    {
        var store = new JsonAccountingPersistenceStore(saveSetRoot, _options);
        store.Save(AccountingPersistenceSnapshot.Capture(_accounts, _characters));
        return new ServerPersistenceArtifact(Name, _options.AccountsDirectory);
    }

    public void Load(string saveSetRoot, ServerPersistenceMetadata metadata, ServerPersistenceArtifact artifact)
    {
        if (!string.Equals(artifact.Name, Name, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected accounting artifact, got '{artifact.Name}'.");

        var store = new JsonAccountingPersistenceStore(saveSetRoot, _options);
        AccountingPersistenceSnapshot snapshot = store.Load();
        _accounts.LoadSnapshot(snapshot.Accounts);
        _characters.LoadSnapshot(snapshot.Characters);
    }
}
