using System;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Persistent account record. Immutable sealed record — mutation produces a new instance.
    /// AccountId is a stable uint64 auto-increment; never reused even for deleted accounts.
    ///
    /// Password storage: SHA-256(username + ":" + password + ":" + PasswordSalt).
    /// The wire protocol adds a per-login server challenge nonce so the client sends
    /// SHA-256(storedHash + ":" + challenge) — preventing replay attacks without
    /// requiring the server to know the original password.
    /// </summary>
    public sealed record AccountModel(
        ulong AccountId,
        string Username,
        string PasswordHash,
        string PasswordSalt,
        ulong[] CharacterSlots,      // CharacterId per slot; 0 = empty. Length = MaxCharactersPerAccount.
        DateTimeOffset CreatedUtc,
        DateTimeOffset LastLoginUtc,
        string[] KnownIps,           // Last ~4 remote IPs for operator audit.
        bool IsAdmin,
        bool AllowSharedIp,
        bool SharedIpReviewPending,
        bool IsBanned,
        string? BanReason
    )
    {
        /// <summary>Current schema version for binary serialization.</summary>
        public const byte SchemaVersion = 2;

        public int CharacterCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < CharacterSlots.Length; i++)
                    if (CharacterSlots[i] != 0) count++;
                return count;
            }
        }

        public bool TryGetSlotIndex(ulong characterId, out byte slotIndex)
        {
            for (int i = 0; i < CharacterSlots.Length; i++)
            {
                if (CharacterSlots[i] == characterId)
                {
                    slotIndex = (byte)i;
                    return true;
                }
            }
            slotIndex = 0;
            return false;
        }

        public int FirstEmptySlot()
        {
            for (int i = 0; i < CharacterSlots.Length; i++)
                if (CharacterSlots[i] == 0) return i;
            return -1;
        }

        public AccountModel WithCharacterInSlot(byte slot, ulong characterId)
        {
            var newSlots = (ulong[])CharacterSlots.Clone();
            newSlots[slot] = characterId;
            return this with { CharacterSlots = newSlots };
        }

        public AccountModel WithCharacterRemoved(ulong characterId)
        {
            var newSlots = (ulong[])CharacterSlots.Clone();
            for (int i = 0; i < newSlots.Length; i++)
                if (newSlots[i] == characterId) { newSlots[i] = 0; break; }
            return this with { CharacterSlots = newSlots };
        }

        public AccountModel WithLastLogin(DateTimeOffset utc, string ip)
        {
            string[] ips = KnownIps;
            if (ip != null && (ips.Length == 0 || ips[^1] != ip))
            {
                int keep = Math.Min(ips.Length, 3);
                var newIps = new string[keep + 1];
                Array.Copy(ips, ips.Length - keep, newIps, 0, keep);
                newIps[keep] = ip;
                ips = newIps;
            }
            return this with { LastLoginUtc = utc, KnownIps = ips };
        }

        public AccountModel WithAllowSharedIp(bool allowSharedIp) =>
            this with { AllowSharedIp = allowSharedIp };

        public AccountModel WithSharedIpReviewPending() =>
            SharedIpReviewPending ? this : this with { SharedIpReviewPending = true };
    }
}
