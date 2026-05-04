using UltraSim.Server;

namespace UltraSim.Server.Accounting;

public sealed record ServerAccountRecord(
    ulong AccountId,
    string Username,
    string PasswordHash,
    AccountStatus Status,
    DateTimeOffset CreatedUtc,
    DateTimeOffset LastLoginUtc,
    string[] KnownRemoteAddresses,
    bool IsAdmin = false,
    string? StatusReason = null)
{
    public ulong[] CharacterSlots { get; init; } = [];

    public string[] AccessRuleKeys { get; init; } = [];

    public string[] DirectPermissionGrants { get; init; } = [];

    public string[] DirectPermissionDenies { get; init; } = [];

    public int CharacterCount
    {
        get
        {
            int count = 0;
            for (int i = 0; i < CharacterSlots.Length; i++)
            {
                if (CharacterSlots[i] != 0)
                    count++;
            }

            return count;
        }
    }

    public ServerAccountRecord Normalize()
    {
        string username = string.IsNullOrWhiteSpace(Username)
            ? throw new ArgumentException("Account username is required.", nameof(Username))
            : Username.Trim();
        string hash = string.IsNullOrWhiteSpace(PasswordHash)
            ? throw new ArgumentException("Account password hash is required.", nameof(PasswordHash))
            : PasswordHash.Trim();
        string[] known = KnownRemoteAddresses
            .Where(static value => !string.IsNullOrWhiteSpace(value))
            .Select(static value => value.Trim())
            .Distinct(StringComparer.Ordinal)
            .Take(8)
            .ToArray();
        return this with
        {
            Username = username,
            PasswordHash = hash,
            KnownRemoteAddresses = known,
            CharacterSlots = CharacterSlots.ToArray(),
            AccessRuleKeys = ServerAccessKey.NormalizeRuleSet(AccessRuleKeys),
            DirectPermissionGrants = ServerAccessKey.NormalizePermissionSet(DirectPermissionGrants),
            DirectPermissionDenies = ServerAccessKey.NormalizePermissionSet(DirectPermissionDenies)
        };
    }

    public ServerAccountRecord WithAccessMetadata(
        IEnumerable<string>? ruleKeys = null,
        IEnumerable<string>? directGrants = null,
        IEnumerable<string>? directDenies = null)
    {
        return this with
        {
            AccessRuleKeys = ServerAccessKey.NormalizeRuleSet(ruleKeys),
            DirectPermissionGrants = ServerAccessKey.NormalizePermissionSet(directGrants),
            DirectPermissionDenies = ServerAccessKey.NormalizePermissionSet(directDenies)
        };
    }

    public ServerAccountRecord WithLoginSuccess(DateTimeOffset nowUtc, string remoteAddress)
    {
        string[] known = AppendKnownRemoteAddress(remoteAddress);
        return this with
        {
            LastLoginUtc = nowUtc,
            KnownRemoteAddresses = known
        };
    }

    private string[] AppendKnownRemoteAddress(string remoteAddress)
    {
        if (string.IsNullOrWhiteSpace(remoteAddress))
            return KnownRemoteAddresses.ToArray();

        string normalized = remoteAddress.Trim();
        var values = new List<string>(KnownRemoteAddresses.Length + 1) { normalized };
        for (int i = 0; i < KnownRemoteAddresses.Length && values.Count < 8; i++)
        {
            string existing = KnownRemoteAddresses[i];
            if (!string.Equals(existing, normalized, StringComparison.Ordinal))
                values.Add(existing);
        }

        return values.ToArray();
    }

    public int FirstEmptySlot(int maxCharacters)
    {
        if (maxCharacters <= 0)
            return -1;

        int searchLength = Math.Max(CharacterSlots.Length, maxCharacters);
        for (int i = 0; i < searchLength && i < maxCharacters; i++)
        {
            if (i >= CharacterSlots.Length || CharacterSlots[i] == 0)
                return i;
        }

        return -1;
    }

    public bool TryGetSlotIndex(ulong characterId, out int slotIndex)
    {
        for (int i = 0; i < CharacterSlots.Length; i++)
        {
            if (CharacterSlots[i] == characterId)
            {
                slotIndex = i;
                return true;
            }
        }

        slotIndex = -1;
        return false;
    }

    public ServerAccountRecord WithCharacterInSlot(int slotIndex, ulong characterId, int maxCharacters)
    {
        if (slotIndex < 0 || slotIndex >= maxCharacters)
            throw new ArgumentOutOfRangeException(nameof(slotIndex));
        if (characterId == 0)
            throw new ArgumentOutOfRangeException(nameof(characterId));

        ulong[] slots = ResizeSlots(maxCharacters);
        slots[slotIndex] = characterId;
        return this with { CharacterSlots = slots };
    }

    public ServerAccountRecord WithCharacterRemoved(ulong characterId)
    {
        ulong[] slots = CharacterSlots.ToArray();
        for (int i = 0; i < slots.Length; i++)
        {
            if (slots[i] == characterId)
            {
                slots[i] = 0;
                break;
            }
        }

        return this with { CharacterSlots = slots };
    }

    private ulong[] ResizeSlots(int maxCharacters)
    {
        int length = Math.Max(maxCharacters, CharacterSlots.Length);
        var slots = new ulong[length];
        Array.Copy(CharacterSlots, slots, Math.Min(CharacterSlots.Length, slots.Length));
        return slots;
    }
}
