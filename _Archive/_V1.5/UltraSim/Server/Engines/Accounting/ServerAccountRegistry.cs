using System;
using System.Collections.Generic;
using System.Threading;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// In-memory account, character, and IP ban store.
    ///
    /// Thread-safety model:
    ///   - All mutations happen on the server game thread (event-driven: login, create, etc.)
    ///   - The background flush task holds a reference to the immutable snapshot arrays.
    ///   - No lock needed: sealed records are immutable; Volatile.Read/Write fence the array swap.
    ///
    /// ID generation:
    ///   - AccountId and CharacterId are monotonically increasing uint64 counters.
    ///   - IDs are never reused, even for deleted records.
    /// </summary>
    public sealed class ServerAccountRegistry
    {
        private AccountModel[] _accounts;
        private CharacterModel[] _characters;
        private IpBanEntry[] _ipBans;

        private ulong _nextAccountId;
        private ulong _nextCharacterId;

        private bool _dirty;
        private System.Threading.Tasks.Task? _pendingSave;

        public ServerAccountRegistry()
        {
            _accounts = Array.Empty<AccountModel>();
            _characters = Array.Empty<CharacterModel>();
            _ipBans = Array.Empty<IpBanEntry>();
            _nextAccountId = 1;
            _nextCharacterId = 1;
        }

        // -------------------------------------------------------------------------
        // Load
        // -------------------------------------------------------------------------

        /// <summary>
        /// Populates the registry from the store on server startup.
        /// Must be called before the server starts accepting connections.
        /// </summary>
        public void LoadFrom(IAccountStore store)
        {
            CharacterModel[] characters = store.LoadCharacters();
            AccountModel[] accounts = store.Load(characters);
            IpBanEntry[] ipBans = store.LoadIpBans();

            Volatile.Write(ref _accounts, accounts);
            Volatile.Write(ref _characters, characters);
            Volatile.Write(ref _ipBans, ipBans);

            ulong maxAccountId = 0;
            foreach (var a in accounts)
                if (a.AccountId > maxAccountId) maxAccountId = a.AccountId;

            ulong maxCharId = 0;
            foreach (var c in characters)
                if (c.CharacterId > maxCharId) maxCharId = c.CharacterId;

            _nextAccountId = maxAccountId + 1;
            _nextCharacterId = maxCharId + 1;
            _dirty = false;
        }

        // -------------------------------------------------------------------------
        // Account queries
        // -------------------------------------------------------------------------

        public bool TryGetByUsername(string username, out AccountModel account)
        {
            AccountModel[] arr = Volatile.Read(ref _accounts);
            foreach (var a in arr)
            {
                if (string.Equals(a.Username, username, StringComparison.OrdinalIgnoreCase))
                {
                    account = a;
                    return true;
                }
            }
            account = null!;
            return false;
        }

        public bool TryGetById(ulong accountId, out AccountModel account)
        {
            AccountModel[] arr = Volatile.Read(ref _accounts);
            foreach (var a in arr)
            {
                if (a.AccountId == accountId)
                {
                    account = a;
                    return true;
                }
            }
            account = null!;
            return false;
        }

        public int CountByIp(string ip)
        {
            int count = 0;
            AccountModel[] arr = Volatile.Read(ref _accounts);
            foreach (var a in arr)
                foreach (var known in a.KnownIps)
                    if (known == ip) { count++; break; }
            return count;
        }

        // -------------------------------------------------------------------------
        // Character queries
        // -------------------------------------------------------------------------

        public bool TryGetCharacter(ulong characterId, out CharacterModel character)
        {
            CharacterModel[] arr = Volatile.Read(ref _characters);
            foreach (var c in arr)
            {
                if (c.CharacterId == characterId)
                {
                    character = c;
                    return true;
                }
            }
            character = null!;
            return false;
        }

        public CharacterModel[] GetCharactersForAccount(ulong accountId)
        {
            if (!TryGetById(accountId, out AccountModel account))
                return Array.Empty<CharacterModel>();

            CharacterModel[] arr = Volatile.Read(ref _characters);
            var byId = new Dictionary<ulong, CharacterModel>(arr.Length);
            foreach (CharacterModel character in arr)
            {
                if (character.AccountId == accountId)
                    byId[character.CharacterId] = character;
            }

            var result = new List<CharacterModel>(account.CharacterSlots.Length);
            for (int i = 0; i < account.CharacterSlots.Length; i++)
            {
                ulong characterId = account.CharacterSlots[i];
                if (characterId != 0 && byId.TryGetValue(characterId, out CharacterModel? character))
                    result.Add(character);
            }

            return result.ToArray();
        }

        // -------------------------------------------------------------------------
        // IP ban queries
        // -------------------------------------------------------------------------

        /// <summary>
        /// Returns true if ip is actively banned (exists and not expired).
        /// Expired bans are skipped; call PurgeExpiredIpBans() to clean them up.
        /// </summary>
        public bool IsIpBanned(string ip)
        {
            IpBanEntry[] arr = Volatile.Read(ref _ipBans);
            foreach (var b in arr)
                if (b.Ip == ip && !b.IsExpired) return true;
            return false;
        }

        public bool TryGetIpBan(string ip, out IpBanEntry ban)
        {
            IpBanEntry[] arr = Volatile.Read(ref _ipBans);
            foreach (var b in arr)
            {
                if (b.Ip == ip && !b.IsExpired)
                {
                    ban = b;
                    return true;
                }
            }
            ban = null!;
            return false;
        }

        /// <summary>Returns all active (non-expired) IP bans.</summary>
        public IpBanEntry[] GetActiveIpBans()
        {
            IpBanEntry[] arr = Volatile.Read(ref _ipBans);
            return Array.FindAll(arr, b => !b.IsExpired);
        }

        // -------------------------------------------------------------------------
        // Account mutations
        // -------------------------------------------------------------------------

        public AccountModel CreateAccount(
            string username,
            string passwordHash,
            string passwordSalt,
            int maxCharacters)
        {
            var slots = new ulong[Math.Max(1, maxCharacters)];
            var now = DateTimeOffset.UtcNow;
            var account = new AccountModel(
                _nextAccountId++,
                username,
                passwordHash,
                passwordSalt,
                slots,
                now,
                now,
                Array.Empty<string>(),
                IsAdmin: false,
                AllowSharedIp: false,
                SharedIpReviewPending: false,
                IsBanned: false,
                BanReason: null);

            ReplaceOrAppendAccount(account);
            return account;
        }

        public void UpdateAccount(AccountModel updated)
        {
            ReplaceOrAppendAccount(updated);
        }

        public AccountModel[] GetAccountsByIp(string ip)
        {
            if (string.IsNullOrWhiteSpace(ip))
                return Array.Empty<AccountModel>();

            AccountModel[] arr = Volatile.Read(ref _accounts);
            var matches = new List<AccountModel>(4);
            foreach (AccountModel account in arr)
            {
                foreach (string knownIp in account.KnownIps)
                {
                    if (!string.Equals(knownIp, ip, StringComparison.Ordinal))
                        continue;

                    matches.Add(account);
                    break;
                }
            }

            return matches.ToArray();
        }

        public bool SetAllowSharedIp(ulong accountId, bool allowSharedIp)
        {
            if (!TryGetById(accountId, out AccountModel account))
                return false;

            UpdateAccount(account.WithAllowSharedIp(allowSharedIp));
            return true;
        }

        private void ReplaceOrAppendAccount(AccountModel account)
        {
            AccountModel[] current = Volatile.Read(ref _accounts);
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].AccountId == account.AccountId)
                {
                    var newArr = (AccountModel[])current.Clone();
                    newArr[i] = account;
                    Volatile.Write(ref _accounts, newArr);
                    _dirty = true;
                    return;
                }
            }

            var appended = new AccountModel[current.Length + 1];
            Array.Copy(current, appended, current.Length);
            appended[current.Length] = account;
            Volatile.Write(ref _accounts, appended);
            _dirty = true;
        }

        // -------------------------------------------------------------------------
        // Character mutations
        // -------------------------------------------------------------------------

        public CharacterModel CreateCharacter(
            ulong accountId,
            byte slotIndex,
            string name,
            ushort archetypeId)
        {
            var now = DateTimeOffset.UtcNow;
            var character = new CharacterModel(
                _nextCharacterId++,
                accountId,
                slotIndex,
                name,
                archetypeId,
                now,
                now,
                X: 0f,
                Y: 0f,
                Z: 0f,
                YawRadians: 0f);

            AppendCharacter(character);

            if (TryGetById(accountId, out AccountModel account))
                UpdateAccount(account.WithCharacterInSlot(slotIndex, character.CharacterId));

            return character;
        }

        public void UpdateCharacter(CharacterModel updated)
        {
            CharacterModel[] current = Volatile.Read(ref _characters);
            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].CharacterId == updated.CharacterId)
                {
                    var newArr = (CharacterModel[])current.Clone();
                    newArr[i] = updated;
                    Volatile.Write(ref _characters, newArr);
                    _dirty = true;
                    return;
                }
            }
        }

        public void DeleteCharacter(ulong characterId, ulong accountId)
        {
            CharacterModel[] current = Volatile.Read(ref _characters);
            int idx = -1;
            for (int i = 0; i < current.Length; i++)
                if (current[i].CharacterId == characterId) { idx = i; break; }

            if (idx < 0) return;

            var newArr = new CharacterModel[current.Length - 1];
            Array.Copy(current, 0, newArr, 0, idx);
            Array.Copy(current, idx + 1, newArr, idx, current.Length - idx - 1);
            Volatile.Write(ref _characters, newArr);
            _dirty = true;

            if (TryGetById(accountId, out AccountModel account))
                UpdateAccount(account.WithCharacterRemoved(characterId));
        }

        private void AppendCharacter(CharacterModel character)
        {
            CharacterModel[] current = Volatile.Read(ref _characters);
            var newArr = new CharacterModel[current.Length + 1];
            Array.Copy(current, newArr, current.Length);
            newArr[current.Length] = character;
            Volatile.Write(ref _characters, newArr);
            _dirty = true;
        }

        // -------------------------------------------------------------------------
        // IP ban mutations
        // -------------------------------------------------------------------------

        /// <summary>
        /// Adds or replaces the ban for the given IP.
        /// If the IP is already banned, the existing record is replaced.
        /// </summary>
        public IpBanEntry BanIp(string ip, string reason, DateTimeOffset? expiryUtc = null)
        {
            var ban = new IpBanEntry(ip, reason, DateTimeOffset.UtcNow, expiryUtc);
            IpBanEntry[] current = Volatile.Read(ref _ipBans);

            for (int i = 0; i < current.Length; i++)
            {
                if (current[i].Ip == ip)
                {
                    var newArr = (IpBanEntry[])current.Clone();
                    newArr[i] = ban;
                    Volatile.Write(ref _ipBans, newArr);
                    _dirty = true;
                    return ban;
                }
            }

            var appended = new IpBanEntry[current.Length + 1];
            Array.Copy(current, appended, current.Length);
            appended[current.Length] = ban;
            Volatile.Write(ref _ipBans, appended);
            _dirty = true;
            return ban;
        }

        /// <summary>Removes the ban for the given IP. No-op if the IP is not banned.</summary>
        public void UnbanIp(string ip)
        {
            IpBanEntry[] current = Volatile.Read(ref _ipBans);
            int idx = -1;
            for (int i = 0; i < current.Length; i++)
                if (current[i].Ip == ip) { idx = i; break; }

            if (idx < 0) return;

            var newArr = new IpBanEntry[current.Length - 1];
            Array.Copy(current, 0, newArr, 0, idx);
            Array.Copy(current, idx + 1, newArr, idx, current.Length - idx - 1);
            Volatile.Write(ref _ipBans, newArr);
            _dirty = true;
        }

        /// <summary>
        /// Removes all expired IP bans from the live list.
        /// Call periodically (e.g. on server startup or once per hour) to keep the list clean.
        /// </summary>
        public void PurgeExpiredIpBans()
        {
            IpBanEntry[] current = Volatile.Read(ref _ipBans);
            IpBanEntry[] active = Array.FindAll(current, b => !b.IsExpired);
            if (active.Length == current.Length) return;
            Volatile.Write(ref _ipBans, active);
            _dirty = true;
        }

        // -------------------------------------------------------------------------
        // Save
        // -------------------------------------------------------------------------

        public int AccountCount => Volatile.Read(ref _accounts).Length;
        public int CharacterCount => Volatile.Read(ref _characters).Length;
        public int IpBanCount => Volatile.Read(ref _ipBans).Length;

        public AccountPersistenceSaveSnapshot CaptureSnapshot()
        {
            AccountModel[] accounts = Volatile.Read(ref _accounts);
            CharacterModel[] characters = Volatile.Read(ref _characters);
            IpBanEntry[] ipBans = Volatile.Read(ref _ipBans);
            return new AccountPersistenceSaveSnapshot(accounts, characters, ipBans);
        }

        public int ApplyCharacterSnapshots(ReadOnlySpan<CharacterSnapshotUpdate> snapshots)
        {
            if (snapshots.IsEmpty)
                return 0;

            var updatesByCharacterId = new Dictionary<ulong, CharacterSnapshotUpdate>(snapshots.Length);
            for (int i = 0; i < snapshots.Length; i++)
                updatesByCharacterId[snapshots[i].CharacterId] = snapshots[i];

            CharacterModel[] current = Volatile.Read(ref _characters);
            CharacterModel[]? updated = null;
            int applied = 0;

            for (int i = 0; i < current.Length; i++)
            {
                CharacterModel existing = current[i];
                if (!updatesByCharacterId.TryGetValue(existing.CharacterId, out CharacterSnapshotUpdate snapshot))
                    continue;

                updated ??= (CharacterModel[])current.Clone();
                updated[i] = existing.WithSnapshot(
                    snapshot.LastPlayedUtc,
                    snapshot.ArchetypeId,
                    snapshot.X,
                    snapshot.Y,
                    snapshot.Z,
                    snapshot.YawRadians);
                applied++;
            }

            if (updated is null)
                return 0;

            Volatile.Write(ref _characters, updated);
            _dirty = true;
            return applied;
        }

        /// <summary>
        /// Flush dirty state to the store if a mutation has occurred since the last flush.
        /// Safe to call while the previous save task is still running — skips in that case.
        /// </summary>
        public void FlushDirtyAsync(IAccountStore store)
        {
            if (!_dirty) return;
            if (_pendingSave != null && !_pendingSave.IsCompleted) return;

            _dirty = false;
            AccountModel[] accSnap = Volatile.Read(ref _accounts);
            CharacterModel[] charSnap = Volatile.Read(ref _characters);
            IpBanEntry[] banSnap = Volatile.Read(ref _ipBans);
            _pendingSave = store.WriteAsync(accSnap, charSnap, banSnap);
        }

        /// <summary>Forces an unconditional save regardless of dirty flag.</summary>
        public void SaveAllAsync(IAccountStore store)
        {
            _dirty = false;
            AccountModel[] accSnap = Volatile.Read(ref _accounts);
            CharacterModel[] charSnap = Volatile.Read(ref _characters);
            IpBanEntry[] banSnap = Volatile.Read(ref _ipBans);
            _pendingSave = store.WriteAsync(accSnap, charSnap, banSnap);
        }

        /// <summary>Blocks until any in-flight save task completes. Call before server shutdown.</summary>
        public void WaitForPendingSave()
        {
            try { _pendingSave?.GetAwaiter().GetResult(); }
            catch { /* store implementations should log internally */ }
        }
    }
}
