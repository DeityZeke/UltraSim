using UltraSim.ECS;

namespace UltraSim.Game.Accounting;

/// <summary>Stable account-domain identity.</summary>
public readonly struct AccountId : IEquatable<AccountId>, IComparable<AccountId>
{
    public AccountId(ulong value)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }

    public bool IsValid => Value != 0;

    public int CompareTo(AccountId other) => Value.CompareTo(other.Value);

    public bool Equals(AccountId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is AccountId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => IsValid ? $"Account({Value})" : "Account(Invalid)";

    public static bool operator ==(AccountId left, AccountId right) => left.Equals(right);

    public static bool operator !=(AccountId left, AccountId right) => !left.Equals(right);
}

/// <summary>Stable character-domain identity.</summary>
public readonly struct CharacterId : IEquatable<CharacterId>, IComparable<CharacterId>
{
    public CharacterId(ulong value)
    {
        if (value == 0)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = value;
    }

    public ulong Value { get; }

    public bool IsValid => Value != 0;

    public int CompareTo(CharacterId other) => Value.CompareTo(other.Value);

    public bool Equals(CharacterId other) => Value == other.Value;

    public override bool Equals(object? obj) => obj is CharacterId other && Equals(other);

    public override int GetHashCode() => Value.GetHashCode();

    public override string ToString() => IsValid ? $"Character({Value})" : "Character(Invalid)";

    public static bool operator ==(CharacterId left, CharacterId right) => left.Equals(right);

    public static bool operator !=(CharacterId left, CharacterId right) => !left.Equals(right);
}

/// <summary>Small trusted local-identity fixture used by account-backed admission.</summary>
public readonly struct TrustedLocalIdentity : IEquatable<TrustedLocalIdentity>
{
    public const int MaximumLength = 256;

    public TrustedLocalIdentity(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        string normalized = value.Trim();
        if (normalized.Length > MaximumLength)
            throw new ArgumentOutOfRangeException(nameof(value));
        Value = normalized;
    }

    public string Value { get; }

    public bool IsValid => !string.IsNullOrEmpty(Value);

    public bool Equals(TrustedLocalIdentity other) =>
        string.Equals(Value, other.Value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is TrustedLocalIdentity other && Equals(other);

    public override int GetHashCode() => StringComparer.Ordinal.GetHashCode(Value ?? string.Empty);

    public override string ToString() => Value ?? string.Empty;

    public static bool operator ==(TrustedLocalIdentity left, TrustedLocalIdentity right) => left.Equals(right);

    public static bool operator !=(TrustedLocalIdentity left, TrustedLocalIdentity right) => !left.Equals(right);
}

public enum AccountStatus : byte
{
    Disabled = 0,
    Enabled = 1,
}

/// <summary>Minimal account metadata; gameplay state remains in ECS.</summary>
public readonly record struct AccountRecord(AccountId AccountId, AccountStatus Status = AccountStatus.Enabled)
{
    public bool IsEnabled => Status == AccountStatus.Enabled;
}

/// <summary>
/// Minimal character ownership/linkage record. It intentionally contains no
/// transform, health, inventory, or other duplicated ECS state.
/// </summary>
public readonly record struct CharacterRecord
{
    public CharacterRecord(
        CharacterId characterId,
        AccountId accountId,
        int slot,
        string displayName,
        PrefabKey prefabKey,
        PersistentEntityKey persistentEntityKey)
    {
        if (!characterId.IsValid)
            throw new ArgumentException("A valid character ID is required.", nameof(characterId));
        if (!accountId.IsValid)
            throw new ArgumentException("A valid account ID is required.", nameof(accountId));
        if (slot < 0)
            throw new ArgumentOutOfRangeException(nameof(slot));
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        string normalizedName = displayName.Trim();
        if (normalizedName.Length > 128)
            throw new ArgumentOutOfRangeException(nameof(displayName));
        if (!prefabKey.IsValid)
            throw new ArgumentException("A valid prefab key is required.", nameof(prefabKey));
        if (!persistentEntityKey.IsValid)
            throw new ArgumentException("A valid persistent entity key is required.", nameof(persistentEntityKey));

        CharacterId = characterId;
        AccountId = accountId;
        Slot = slot;
        DisplayName = normalizedName;
        PrefabKey = prefabKey;
        PersistentEntityKey = persistentEntityKey;
    }

    public CharacterId CharacterId { get; }

    public AccountId AccountId { get; }

    public int Slot { get; }

    public string DisplayName { get; }

    public PrefabKey PrefabKey { get; }

    public PersistentEntityKey PersistentEntityKey { get; }
}

/// <summary>One durable trusted-local-identity to account binding.</summary>
public readonly record struct TrustedIdentityBinding(
    TrustedLocalIdentity Identity,
    AccountId AccountId);

/// <summary>
/// Immutable logical Accounting state captured for one save-set epoch. The
/// lists are sorted by their durable identities and contain no runtime ECS
/// handles.
/// </summary>
public sealed class AccountingSnapshot
{
    public AccountingSnapshot(
        IReadOnlyList<AccountRecord> accounts,
        IReadOnlyList<CharacterRecord> characters,
        IReadOnlyList<TrustedIdentityBinding> trustedIdentities)
    {
        ArgumentNullException.ThrowIfNull(accounts);
        ArgumentNullException.ThrowIfNull(characters);
        ArgumentNullException.ThrowIfNull(trustedIdentities);
        Accounts = Array.AsReadOnly(accounts.ToArray());
        Characters = Array.AsReadOnly(characters.ToArray());
        TrustedIdentities = Array.AsReadOnly(trustedIdentities.ToArray());
    }

    public IReadOnlyList<AccountRecord> Accounts { get; }

    public IReadOnlyList<CharacterRecord> Characters { get; }

    public IReadOnlyList<TrustedIdentityBinding> TrustedIdentities { get; }
}

/// <summary>
/// Stable game-owned account store. It is intentionally a domain collection,
/// not a second ECS or a duplicate gameplay-state root.
/// </summary>
public sealed class AccountingStore
{
    private Dictionary<AccountId, AccountRecord> _accounts = [];
    private Dictionary<CharacterId, CharacterRecord> _characters = [];
    private Dictionary<TrustedLocalIdentity, AccountId> _trustedIdentities = [];
    private HashSet<(AccountId Account, int Slot)> _characterSlots = [];
    private HashSet<PersistentEntityKey> _persistentCharacters = [];
    private readonly int _ownerThreadId = Environment.CurrentManagedThreadId;

    public int AccountCount
    {
        get
        {
            EnsureOwnerThread();
            return _accounts.Count;
        }
    }

    public int CharacterCount
    {
        get
        {
            EnsureOwnerThread();
            return _characters.Count;
        }
    }

    public void AddAccount(AccountRecord account)
    {
        EnsureOwnerThread();
        if (!account.AccountId.IsValid)
            throw new ArgumentException("A valid account ID is required.", nameof(account));
        if (account.Status is not (AccountStatus.Disabled or AccountStatus.Enabled))
            throw new ArgumentOutOfRangeException(nameof(account), "The account status is invalid.");
        if (!_accounts.TryAdd(account.AccountId, account))
            throw new InvalidOperationException($"The account '{account.AccountId}' already exists.");
    }

    public void AddCharacter(CharacterRecord character)
    {
        EnsureOwnerThread();
        if (!_accounts.ContainsKey(character.AccountId))
        {
            throw new InvalidOperationException(
                $"The owning account '{character.AccountId}' must exist before its character is added.");
        }
        if (_characters.ContainsKey(character.CharacterId))
            throw new InvalidOperationException($"The character '{character.CharacterId}' already exists.");
        if (!_characterSlots.Add((character.AccountId, character.Slot)))
        {
            throw new InvalidOperationException(
                $"Account '{character.AccountId}' already owns character slot {character.Slot}.");
        }
        if (!_persistentCharacters.Add(character.PersistentEntityKey))
        {
            _characterSlots.Remove((character.AccountId, character.Slot));
            throw new InvalidOperationException(
                $"Persistent entity '{character.PersistentEntityKey}' is already linked to a character.");
        }

        try
        {
            _characters.Add(character.CharacterId, character);
        }
        catch
        {
            _characterSlots.Remove((character.AccountId, character.Slot));
            _persistentCharacters.Remove(character.PersistentEntityKey);
            throw;
        }
    }

    public void BindTrustedIdentity(TrustedLocalIdentity identity, AccountId accountId)
    {
        EnsureOwnerThread();
        if (!identity.IsValid)
            throw new ArgumentException("A valid trusted local identity is required.", nameof(identity));
        if (!_accounts.ContainsKey(accountId))
            throw new KeyNotFoundException($"The account '{accountId}' does not exist.");
        if (!_trustedIdentities.TryAdd(identity, accountId))
        {
            throw new InvalidOperationException(
                $"The trusted local identity '{identity}' is already bound.");
        }
    }

    public bool TryGetAccount(AccountId accountId, out AccountRecord account)
    {
        EnsureOwnerThread();
        return _accounts.TryGetValue(accountId, out account);
    }

    public bool TryGetCharacter(CharacterId characterId, out CharacterRecord character)
    {
        EnsureOwnerThread();
        return _characters.TryGetValue(characterId, out character);
    }

    public bool TryResolveAccount(TrustedLocalIdentity identity, out AccountRecord account)
    {
        EnsureOwnerThread();
        if (_trustedIdentities.TryGetValue(identity, out AccountId accountId) &&
            _accounts.TryGetValue(accountId, out account))
        {
            return true;
        }

        account = default;
        return false;
    }

    public IReadOnlyList<CharacterRecord> GetCharacters(AccountId accountId)
    {
        EnsureOwnerThread();
        var characters = new List<CharacterRecord>();
        foreach (CharacterRecord character in _characters.Values)
        {
            if (character.AccountId == accountId)
                characters.Add(character);
        }

        characters.Sort(static (left, right) =>
        {
            int slot = left.Slot.CompareTo(right.Slot);
            return slot != 0 ? slot : left.CharacterId.CompareTo(right.CharacterId);
        });
        return characters;
    }

    /// <summary>Captures a deterministic immutable domain snapshot.</summary>
    public AccountingSnapshot CaptureSnapshot()
    {
        EnsureOwnerThread();
        AccountRecord[] accounts = _accounts.Values
            .OrderBy(static account => account.AccountId)
            .ToArray();
        CharacterRecord[] characters = _characters.Values
            .OrderBy(static character => character.CharacterId)
            .ToArray();
        TrustedIdentityBinding[] identities = _trustedIdentities
            .Select(static pair => new TrustedIdentityBinding(pair.Key, pair.Value))
            .OrderBy(static binding => binding.Identity.Value, StringComparer.Ordinal)
            .ToArray();
        return new AccountingSnapshot(accounts, characters, identities);
    }

    /// <summary>Builds an isolated store from a validated durable snapshot.</summary>
    public static AccountingStore FromSnapshot(AccountingSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var store = new AccountingStore();
        foreach (AccountRecord account in snapshot.Accounts)
            store.AddAccount(account);
        foreach (TrustedIdentityBinding identity in snapshot.TrustedIdentities)
            store.BindTrustedIdentity(identity.Identity, identity.AccountId);
        foreach (CharacterRecord character in snapshot.Characters)
            store.AddCharacter(character);
        return store;
    }

    /// <summary>
    /// Atomically adopts another owner-thread-bound store's domain state. The
    /// caller uses this only at a game/runtime generation publication boundary;
    /// the store object itself remains stable for existing Admission clients.
    /// </summary>
    public void Adopt(AccountingStore source)
    {
        EnsureOwnerThread();
        ArgumentNullException.ThrowIfNull(source);
        source.EnsureOwnerThread();
        _accounts = new Dictionary<AccountId, AccountRecord>(source._accounts);
        _characters = new Dictionary<CharacterId, CharacterRecord>(source._characters);
        _trustedIdentities = new Dictionary<TrustedLocalIdentity, AccountId>(source._trustedIdentities);
        _characterSlots = new HashSet<(AccountId Account, int Slot)>(source._characterSlots);
        _persistentCharacters = new HashSet<PersistentEntityKey>(source._persistentCharacters);
    }

    private void EnsureOwnerThread()
    {
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Accounting is bound to its creating owner thread.");
    }
}
