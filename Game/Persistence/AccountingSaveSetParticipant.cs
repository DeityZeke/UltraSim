using System.Buffers.Binary;
using System.Text;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Server.Persistence;

namespace UltraSim.Game.Persistence;

/// <summary>Fixed bounds for the minimal durable Accounting artifact.</summary>
public sealed class AccountingPersistenceOptions
{
    public const int HardMaximumRecordsPerSection = 100_000;

    public AccountingPersistenceOptions(
        long maximumArtifactBytes = 16 * 1024 * 1024,
        int maximumAccounts = 100_000,
        int maximumCharacters = 100_000,
        int maximumTrustedIdentities = 100_000)
    {
        if (maximumArtifactBytes <= 0 || maximumArtifactBytes > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(maximumArtifactBytes));
        if (maximumAccounts <= 0 || maximumAccounts > HardMaximumRecordsPerSection)
            throw new ArgumentOutOfRangeException(nameof(maximumAccounts));
        if (maximumCharacters <= 0 || maximumCharacters > HardMaximumRecordsPerSection)
            throw new ArgumentOutOfRangeException(nameof(maximumCharacters));
        if (maximumTrustedIdentities <= 0 || maximumTrustedIdentities > HardMaximumRecordsPerSection)
            throw new ArgumentOutOfRangeException(nameof(maximumTrustedIdentities));
        MaximumArtifactBytes = maximumArtifactBytes;
        MaximumAccounts = maximumAccounts;
        MaximumCharacters = maximumCharacters;
        MaximumTrustedIdentities = maximumTrustedIdentities;
    }

    public long MaximumArtifactBytes { get; }

    public int MaximumAccounts { get; }

    public int MaximumCharacters { get; }

    public int MaximumTrustedIdentities { get; }
}

/// <summary>
/// Deterministic save-set participant for the game-owned Accounting domain.
/// It persists identity/linkage facts only; live ECS state remains the ECS
/// participant's responsibility.
/// </summary>
public sealed class AccountingSaveSetParticipant : ISaveSetParticipant
{
    private const uint Magic = 0x43415355; // "USAC" in little-endian order.
    private const ushort FormatVersion = 1;
    private readonly AccountingStore _source;
    private readonly AccountingPersistenceOptions _options;

    public AccountingSaveSetParticipant(
        AccountingStore source,
        AccountingPersistenceOptions? options = null,
        string key = "accounting")
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _options = options ?? new AccountingPersistenceOptions();
        if (string.IsNullOrEmpty(key))
            throw new ArgumentException("A save-set participant key is required.", nameof(key));
        Key = key;
    }

    public string Key { get; }

    public long MaximumArtifactBytes => _options.MaximumArtifactBytes;

    public ICapturedSaveSetParticipant Capture(in SaveSetContext context)
    {
        if (context.Sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(context));
        AccountingSnapshot snapshot = _source.CaptureSnapshot();
        ValidateSnapshotBounds(snapshot);
        return new CapturedAccountingParticipant(Key, context.Sequence, snapshot);
    }

    public IPreparedLoadParticipant Prepare(ReadOnlyMemory<byte> artifact, in SaveSetContext context)
    {
        if (context.Sequence == 0)
            throw new ArgumentOutOfRangeException(nameof(context));
        if (artifact.Length == 0 || artifact.Length > _options.MaximumArtifactBytes)
            throw new PersistenceValidationException("The Accounting artifact size is outside its configured bound.");
        AccountingSnapshot snapshot;
        try
        {
            snapshot = Parse(artifact.Span, context.Sequence, _options);
        }
        catch (PersistenceException)
        {
            throw;
        }
        catch (ArgumentException exception)
        {
            throw new PersistenceValidationException(
                $"The Accounting artifact contains an invalid durable identity: {exception.Message}");
        }
        ValidateSnapshotBounds(snapshot);
        return new PreparedAccountingParticipant(Key, snapshot);
    }

    private void ValidateSnapshotBounds(AccountingSnapshot snapshot)
    {
        if (snapshot.Accounts.Count > _options.MaximumAccounts)
            throw new PersistenceValidationException("The Accounting account limit was exceeded.");
        if (snapshot.Characters.Count > _options.MaximumCharacters)
            throw new PersistenceValidationException("The Accounting character limit was exceeded.");
        if (snapshot.TrustedIdentities.Count > _options.MaximumTrustedIdentities)
            throw new PersistenceValidationException("The Accounting trusted-identity limit was exceeded.");
    }

    private static AccountingSnapshot Parse(
        ReadOnlySpan<byte> bytes,
        ulong expectedSequence,
        AccountingPersistenceOptions options)
    {
        var reader = new ArtifactReader(bytes);
        if (reader.ReadUInt32() != Magic || reader.ReadUInt16() != FormatVersion)
            throw new PersistenceCompatibilityException("The Accounting artifact format is unsupported.");
        ulong sequence = reader.ReadUInt64();
        if (sequence != expectedSequence)
            throw new PersistenceValidationException("Accounting artifact sequence disagrees with the save-set generation.");

        int accountCount = reader.ReadCount("account");
        int characterCount = reader.ReadCount("character");
        int identityCount = reader.ReadCount("trusted identity");
        if (accountCount > options.MaximumAccounts)
            throw new PersistenceValidationException("The Accounting account limit was exceeded.");
        if (characterCount > options.MaximumCharacters)
            throw new PersistenceValidationException("The Accounting character limit was exceeded.");
        if (identityCount > options.MaximumTrustedIdentities)
            throw new PersistenceValidationException("The Accounting trusted-identity limit was exceeded.");
        reader.EnsureRemaining(
            checked((long)accountCount * 9L +
                (long)identityCount * 12L +
                (long)characterCount * 44L));

        var accounts = new AccountRecord[accountCount];
        ulong previousAccountId = 0;
        for (int index = 0; index < accounts.Length; index++)
        {
            AccountId accountId = new(reader.ReadUInt64());
            AccountStatus status = (AccountStatus)reader.ReadByte();
            if (accountId.Value <= previousAccountId)
                throw new PersistenceValidationException("Accounting account records are not in canonical order.");
            if (status is not (AccountStatus.Disabled or AccountStatus.Enabled))
                throw new PersistenceValidationException("The Accounting artifact contains an invalid account status.");
            accounts[index] = new AccountRecord(accountId, status);
            previousAccountId = accountId.Value;
        }

        var identities = new TrustedIdentityBinding[identityCount];
        string? previousIdentity = null;
        for (int index = 0; index < identities.Length; index++)
        {
            string encodedIdentity = reader.ReadString(TrustedLocalIdentity.MaximumLength);
            TrustedLocalIdentity identity = new(encodedIdentity);
            if (!StringComparer.Ordinal.Equals(identity.Value, encodedIdentity) ||
                (previousIdentity is not null &&
                    StringComparer.Ordinal.Compare(previousIdentity, identity.Value) >= 0))
            {
                throw new PersistenceValidationException(
                    "Accounting trusted identities are not canonically encoded and ordered.");
            }
            identities[index] = new TrustedIdentityBinding(identity, new AccountId(reader.ReadUInt64()));
            previousIdentity = identity.Value;
        }

        var characters = new CharacterRecord[characterCount];
        ulong previousCharacterId = 0;
        for (int index = 0; index < characters.Length; index++)
        {
            CharacterId characterId = new(reader.ReadUInt64());
            AccountId accountId = new(reader.ReadUInt64());
            int slot = reader.ReadInt32();
            string displayName = reader.ReadString(128);
            var prefabKey = new PrefabKey(reader.ReadString(PrefabKey.MaximumLength));
            var persistentKey = new PersistentEntityKey(
                new PersistentWorldId(reader.ReadUInt64()),
                new PersistentEntityId(reader.ReadUInt64()));
            var character = new CharacterRecord(
                characterId,
                accountId,
                slot,
                displayName,
                prefabKey,
                persistentKey);
            if (characterId.Value <= previousCharacterId ||
                !StringComparer.Ordinal.Equals(character.DisplayName, displayName))
            {
                throw new PersistenceValidationException(
                    "Accounting character records are not canonically encoded and ordered.");
            }
            characters[index] = character;
            previousCharacterId = characterId.Value;
        }

        reader.EnsureEnd();
        return new AccountingSnapshot(accounts, characters, identities);
    }

    private sealed class CapturedAccountingParticipant : ICapturedSaveSetParticipant
    {
        private readonly ulong _sequence;
        private AccountingSnapshot? _snapshot;

        internal CapturedAccountingParticipant(string key, ulong sequence, AccountingSnapshot snapshot)
        {
            Key = key;
            _sequence = sequence;
            _snapshot = snapshot;
        }

        public string Key { get; }

        public void WriteArtifact(Stream destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            AccountingSnapshot snapshot = _snapshot ??
                throw new InvalidOperationException("The Accounting capture was already consumed.");
            AccountingSaveSetParticipant.WriteArtifact(destination, _sequence, snapshot);
            _snapshot = null;
        }

        public void Dispose() => _snapshot = null;
    }

    private static void WriteArtifact(Stream destination, ulong sequence, AccountingSnapshot snapshot)
    {
        using var writer = new BinaryWriter(destination, Encoding.UTF8, leaveOpen: true);
        writer.Write(Magic);
        writer.Write(FormatVersion);
        writer.Write(sequence);
        writer.Write(snapshot.Accounts.Count);
        writer.Write(snapshot.Characters.Count);
        writer.Write(snapshot.TrustedIdentities.Count);
        foreach (AccountRecord account in snapshot.Accounts)
        {
            writer.Write(account.AccountId.Value);
            writer.Write((byte)account.Status);
        }
        foreach (TrustedIdentityBinding identity in snapshot.TrustedIdentities)
        {
            WriteString(writer, identity.Identity.Value, TrustedLocalIdentity.MaximumLength);
            writer.Write(identity.AccountId.Value);
        }
        foreach (CharacterRecord character in snapshot.Characters)
        {
            writer.Write(character.CharacterId.Value);
            writer.Write(character.AccountId.Value);
            writer.Write(character.Slot);
            WriteString(writer, character.DisplayName, 128);
            WriteString(writer, character.PrefabKey.Value, PrefabKey.MaximumLength);
            writer.Write(character.PersistentEntityKey.World.Value);
            writer.Write(character.PersistentEntityKey.Entity.Value);
        }
        writer.Flush();
    }

    private static void WriteString(BinaryWriter writer, string value, int maximumCharacters)
    {
        if (value.Length > maximumCharacters)
            throw new PersistenceValidationException("The Accounting artifact contains an oversized string.");
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }
}

/// <summary>Prepared Accounting state whose durable links target staged ECS.</summary>
public sealed class PreparedAccountingParticipant : IPreparedLoadParticipant
{
    private readonly AccountingSnapshot _snapshot;
    private readonly Dictionary<CharacterId, Entity> _resolvedEntities = [];
    private AccountingStore? _store;
    private bool _taken;

    internal PreparedAccountingParticipant(string key, AccountingSnapshot snapshot)
    {
        Key = key;
        _snapshot = snapshot;
    }

    public string Key { get; }

    public AccountingStore Store => _store ??
        throw new InvalidOperationException("The Accounting participant has not completed Resolve.");

    public IReadOnlyDictionary<CharacterId, Entity> ResolvedEntities => _resolvedEntities;

    public void Resolve(IPersistentEntityResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        if (_store is not null)
            throw new InvalidOperationException("The Accounting participant was already resolved.");
        AccountingStore store;
        try
        {
            store = AccountingStore.FromSnapshot(_snapshot);
        }
        catch (ArgumentException exception)
        {
            throw new PersistenceValidationException(
                $"The prepared Accounting state violates a domain invariant: {exception.Message}");
        }
        catch (InvalidOperationException exception)
        {
            throw new PersistenceValidationException(
                $"The prepared Accounting state violates a domain invariant: {exception.Message}");
        }
        catch (KeyNotFoundException exception)
        {
            throw new PersistenceValidationException(
                $"The prepared Accounting state violates a domain invariant: {exception.Message}");
        }
        foreach (CharacterRecord character in _snapshot.Characters)
        {
            if (!resolver.TryResolve(character.PersistentEntityKey, out object? value) ||
                value is not Entity entity ||
                !entity.IsValid)
            {
                throw new PersistenceValidationException(
                    $"Accounting character '{character.CharacterId}' references a missing staged ECS entity.");
            }
            _resolvedEntities.Add(character.CharacterId, entity);
        }
        _store = store;
    }

    public void Validate()
    {
        if (_store is null || _resolvedEntities.Count != _snapshot.Characters.Count)
            throw new PersistenceValidationException("The prepared Accounting state is incomplete.");
        foreach (CharacterRecord character in _snapshot.Characters)
        {
            if (!_resolvedEntities.TryGetValue(character.CharacterId, out Entity entity) || !entity.IsValid)
                throw new PersistenceValidationException("Accounting durable-link validation failed.");
        }
    }

    public AccountingStore TakeStore()
    {
        if (_store is null || _taken)
            throw new InvalidOperationException("The prepared Accounting store is unavailable.");
        _taken = true;
        AccountingStore store = _store;
        _store = null;
        return store;
    }

    public void Dispose()
    {
        _store = null;
        _resolvedEntities.Clear();
    }
}

/// <summary>Small bounded little-endian reader for the Accounting artifact.</summary>
internal ref struct ArtifactReader
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly ReadOnlySpan<byte> _bytes;
    private int _offset;

    internal ArtifactReader(ReadOnlySpan<byte> bytes)
    {
        _bytes = bytes;
        _offset = 0;
    }

    internal byte ReadByte()
    {
        Ensure(sizeof(byte));
        return _bytes[_offset++];
    }

    internal ushort ReadUInt16()
    {
        Ensure(sizeof(ushort));
        ushort value = BinaryPrimitives.ReadUInt16LittleEndian(_bytes[_offset..]);
        _offset += sizeof(ushort);
        return value;
    }

    internal uint ReadUInt32()
    {
        Ensure(sizeof(uint));
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(_bytes[_offset..]);
        _offset += sizeof(uint);
        return value;
    }

    internal ulong ReadUInt64()
    {
        Ensure(sizeof(ulong));
        ulong value = BinaryPrimitives.ReadUInt64LittleEndian(_bytes[_offset..]);
        _offset += sizeof(ulong);
        return value;
    }

    internal int ReadInt32()
    {
        Ensure(sizeof(int));
        int value = BinaryPrimitives.ReadInt32LittleEndian(_bytes[_offset..]);
        _offset += sizeof(int);
        return value;
    }

    internal int ReadCount(string kind)
    {
        int count = ReadInt32();
        if (count < 0)
            throw new PersistenceValidationException($"The Accounting {kind} count is negative.");
        return count;
    }

    internal string ReadString(int maximumCharacters)
    {
        int byteCount = ReadInt32();
        if (byteCount < 0 || byteCount > checked(maximumCharacters * 4))
            throw new PersistenceValidationException("The Accounting artifact contains an oversized string.");
        Ensure(byteCount);
        string value;
        try
        {
            value = StrictUtf8.GetString(_bytes.Slice(_offset, byteCount));
        }
        catch (DecoderFallbackException)
        {
            throw new PersistenceValidationException("The Accounting artifact contains invalid UTF-8.");
        }
        _offset += byteCount;
        if (value.Length > maximumCharacters)
            throw new PersistenceValidationException("The Accounting artifact contains an oversized string.");
        return value;
    }

    internal void EnsureEnd()
    {
        if (_offset != _bytes.Length)
            throw new PersistenceValidationException("The Accounting artifact contains trailing bytes.");
    }

    internal void EnsureRemaining(long count)
    {
        if (count < 0 || count > _bytes.Length - _offset)
            throw new PersistenceValidationException("The Accounting artifact counts exceed its remaining bytes.");
    }

    private void Ensure(int count)
    {
        if (count < 0 || count > _bytes.Length - _offset)
            throw new PersistenceValidationException("The Accounting artifact is truncated.");
    }
}
