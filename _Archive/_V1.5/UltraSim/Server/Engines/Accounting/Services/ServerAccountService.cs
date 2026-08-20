using System;
using UltraSim.UI.Contracts;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Business logic for account operations: IP gating, login, account creation.
    /// Stateless — all persistent state lives in <see cref="ServerAccountRegistry"/>.
    /// </summary>
    public sealed class ServerAccountService
    {
        private readonly ServerAccountRegistry _registry;
        private readonly ServerAccountOptions  _options;
        private readonly IAccountAuthenticationProvider _authenticationProvider;

        public ServerAccountService(
            ServerAccountRegistry registry,
            ServerAccountOptions options,
            IAccountAuthenticationProvider? authenticationProvider = null)
        {
            _registry = registry ?? throw new ArgumentNullException(nameof(registry));
            _options  = options  ?? throw new ArgumentNullException(nameof(options));
            _authenticationProvider = authenticationProvider ?? ChallengeResponseAccountAuthenticationProvider.Instance;
        }

        // -------------------------------------------------------------------------
        // IP gating — checked on every connection before any session state advances
        // -------------------------------------------------------------------------

        /// <summary>
        /// Returns true if the remote IP is permitted to connect.
        /// Sets <paramref name="rejectionReason"/> to a human-readable message when false.
        /// </summary>
        public bool IsIpAllowed(string ip, out string rejectionReason)
        {
            // 1. IP ban
            if (_registry.TryGetIpBan(ip, out IpBanEntry ban))
            {
                rejectionReason = string.IsNullOrWhiteSpace(ban.Reason)
                    ? "Your IP address has been banned from this server."
                    : $"Connection refused: {ban.Reason}";
                return false;
            }

            // 2. Whitelist
            if (_options.WhitelistMode)
            {
                string[]? allowed = _options.WhitelistedIps;
                bool onList = false;
                if (allowed != null)
                    foreach (string entry in allowed)
                        if (string.Equals(entry, ip, StringComparison.Ordinal))
                        { onList = true; break; }

                if (!onList)
                {
                    rejectionReason = "This server is not accepting connections from your IP address.";
                    return false;
                }
            }

            rejectionReason = string.Empty;
            return true;
        }

        // -------------------------------------------------------------------------
        // Login / account creation — called after a valid LoginRequestPacket arrives
        // -------------------------------------------------------------------------

        /// <summary>
        /// Validates a login challenge-response and returns the authenticated account.
        ///
        /// <para>
        /// Protocol (see <see cref="LoginPacketCodec"/> for full scheme):
        ///   <c>passwordHash = SHA-256(password)</c>  (computed client-side, 32 bytes)
        ///   <c>challengeResponse = SHA-256(UTF8(Hex(passwordHash) + ":" + Hex(challenge16)))</c>
        /// </para>
        ///
        /// <para>
        /// For existing accounts the server validates the challenge-response against the
        /// stored hash. For new accounts (if <see cref="ServerAccountOptions.AccountsPerIp"/>
        /// permits) the challenge-response is validated and the account is created using
        /// the transmitted password hash.
        /// </para>
        /// </summary>
        /// <param name="username">Plaintext username from the LoginRequestPacket.</param>
        /// <param name="passwordHash32">32-byte SHA-256(password) sent by the client.</param>
        /// <param name="challengeResponse32">32-byte challenge-response sent by the client.</param>
        /// <param name="challenge16">16-byte nonce the server sent in the LoginPanel payload.</param>
        /// <param name="remoteIp">Remote IP for account-per-IP enforcement on new accounts.</param>
        /// <param name="account">Set on success to the authenticated or newly created account.</param>
        /// <param name="isNewAccount">True when a new account was created; false for an existing account login.</param>
        /// <param name="errorMessage">Human-readable failure reason (empty on success).</param>
        /// <returns>True on success; false on failure.</returns>
        public bool TryLogin(
            string username,
            ReadOnlySpan<byte> passwordHash32,
            ReadOnlySpan<byte> challengeResponse32,
            ReadOnlySpan<byte> challenge16,
            string remoteIp,
            out AccountModel? account,
            out bool isNewAccount,
            out string errorMessage)
        {
            account = null;
            isNewAccount = false;
            errorMessage = string.Empty;

            string trimmedUsername = username?.Trim() ?? string.Empty;
            if (!TryValidateAccountUsername(trimmedUsername, out errorMessage))
            {
                return false;
            }

            if (_registry.TryGetByUsername(trimmedUsername, out AccountModel existing))
            {
                // Banned check
                if (existing.IsBanned)
                {
                    errorMessage = string.IsNullOrWhiteSpace(existing.BanReason)
                        ? "Your account has been banned from this server."
                        : $"Account banned: {existing.BanReason}";
                    return false;
                }

                if (!_authenticationProvider.TryAuthenticateExistingAccount(
                    existing,
                    passwordHash32,
                    challengeResponse32,
                    challenge16,
                    out errorMessage))
                {
                    return false;
                }

                // Success — update last login record
                AccountModel updated = existing.WithLastLogin(DateTimeOffset.UtcNow, remoteIp);
                _registry.UpdateAccount(updated);
                FlagSharedIpAccountsForReview(updated, remoteIp);
                account = updated;
                return true;
            }

            // --- New account registration ---

            if (!_authenticationProvider.TryPrepareNewAccount(
                trimmedUsername,
                passwordHash32,
                challengeResponse32,
                challenge16,
                out string passwordHash,
                out string passwordSalt,
                out errorMessage))
            {
                return false;
            }

            // IP account limit
            if (_options.AccountsPerIp > 0 && !string.IsNullOrEmpty(remoteIp))
            {
                int ipCount = _registry.CountByIp(remoteIp);
                if (ipCount >= _options.AccountsPerIp)
                {
                    errorMessage = "Maximum accounts reached for your IP address.";
                    return false;
                }
            }

            AccountModel created = _registry.CreateAccount(
                trimmedUsername,
                passwordHash: passwordHash,
                passwordSalt: passwordSalt,
                maxCharacters: _options.ClampedMaxCharacters);
            created = created.WithLastLogin(DateTimeOffset.UtcNow, remoteIp);
            _registry.UpdateAccount(created);
            FlagSharedIpAccountsForReview(created, remoteIp);

            account = created;
            isNewAccount = true;
            return true;
        }

        public CharacterModel[] GetCharactersForAccount(ulong accountId) =>
            _registry.GetCharactersForAccount(accountId);

        public bool TryGetAccount(ulong accountId, out AccountModel account) =>
            _registry.TryGetById(accountId, out account);

        public bool TryGetCharacter(ulong characterId, out CharacterModel character) =>
            _registry.TryGetCharacter(characterId, out character);

        internal void LoadFromStore(IAccountStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            _registry.LoadFrom(store);
        }

        internal void SaveAllAndWait(IAccountStore store)
        {
            ArgumentNullException.ThrowIfNull(store);
            _registry.SaveAllAsync(store);
            _registry.WaitForPendingSave();
        }

        internal AccountPersistenceSaveSnapshot CaptureSnapshot()
            => _registry.CaptureSnapshot();

        public bool TryUpdateCharacterSnapshot(
            ulong characterId,
            ushort prefabId,
            float x,
            float y,
            float z,
            float yawRadians,
            out CharacterModel updatedCharacter)
        {
            if (!_registry.TryGetCharacter(characterId, out CharacterModel existing))
            {
                updatedCharacter = null!;
                return false;
            }

            updatedCharacter = existing.WithSnapshot(
                DateTimeOffset.UtcNow,
                archetypeId: prefabId,
                x,
                y,
                z,
                yawRadians);
            _registry.UpdateCharacter(updatedCharacter);
            return true;
        }

        internal int ApplyCharacterSnapshots(ReadOnlySpan<CharacterSnapshotUpdate> snapshots)
            => _registry.ApplyCharacterSnapshots(snapshots);

        public bool TryCreateCharacter(
            AccountModel account,
            string name,
            ushort archetypeId,
            out AccountModel? updatedAccount,
            out CharacterModel? createdCharacter,
            out string errorMessage)
        {
            updatedAccount = null;
            createdCharacter = null;
            errorMessage = string.Empty;

            if (!_registry.TryGetById(account.AccountId, out AccountModel currentAccount))
            {
                errorMessage = "Account not found.";
                return false;
            }

            account = currentAccount;

            if (account.IsBanned)
            {
                errorMessage = "Account banned.";
                return false;
            }

            string trimmedName = name?.Trim() ?? string.Empty;
            if (!TryValidateCharacterName(trimmedName, out errorMessage))
            {
                return false;
            }

            int slotIndex = account.FirstEmptySlot();
            if (slotIndex < 0)
            {
                errorMessage = "Character slots full.";
                return false;
            }

            createdCharacter = _registry.CreateCharacter(account.AccountId, (byte)slotIndex, trimmedName, archetypeId);
            if (!_registry.TryGetById(account.AccountId, out AccountModel persisted))
            {
                errorMessage = "Character created but account refresh failed.";
                return false;
            }

            updatedAccount = persisted;
            return true;
        }

        public bool DeleteCharacter(AccountModel account, ulong characterId, out AccountModel? updatedAccount)
        {
            updatedAccount = null;
            if (!_registry.TryGetById(account.AccountId, out AccountModel currentAccount))
                return false;

            account = currentAccount;
            if (!account.TryGetSlotIndex(characterId, out _))
                return false;

            _registry.DeleteCharacter(characterId, account.AccountId);
            if (!_registry.TryGetById(account.AccountId, out AccountModel persisted))
                return false;

            updatedAccount = persisted;
            return true;
        }

        private void FlagSharedIpAccountsForReview(AccountModel account, string remoteIp)
        {
            if (string.IsNullOrWhiteSpace(remoteIp))
                return;

            AccountModel[] matches = _registry.GetAccountsByIp(remoteIp);
            if (matches.Length <= 1)
                return;

            bool exempt = account.AllowSharedIp;
            for (int i = 0; i < matches.Length && exempt; i++)
                exempt = matches[i].AllowSharedIp;

            if (exempt)
                return;

            if (!account.SharedIpReviewPending)
            {
                account = account.WithSharedIpReviewPending();
                _registry.UpdateAccount(account);
            }

            for (int i = 0; i < matches.Length; i++)
            {
                AccountModel other = matches[i];
                if (other.AccountId == account.AccountId || other.SharedIpReviewPending)
                    continue;

                _registry.UpdateAccount(other.WithSharedIpReviewPending());
            }
        }

        private static bool TryValidateAccountUsername(string username, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(username) || username.Length < 3)
            {
                errorMessage = "Username must be a minimum of 3 characters.";
                return false;
            }

            if (username.Length > 32)
            {
                errorMessage = "Username cannot exceed 32 characters.";
                return false;
            }

            if (!IsAlphaNumeric(username))
            {
                errorMessage = "Username cannot contain special characters. A-Z and 0-9 only.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool TryValidateCharacterName(string characterName, out string errorMessage)
        {
            if (string.IsNullOrWhiteSpace(characterName) || characterName.Length < 3)
            {
                errorMessage = "Character name must be a minimum of 3 characters.";
                return false;
            }

            if (characterName.Length > 32)
            {
                errorMessage = "Character name cannot exceed 32 characters.";
                return false;
            }

            if (!IsAlphaNumeric(characterName))
            {
                errorMessage = "Character name cannot contain special characters. A-Z and 0-9 only.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
        }

        private static bool IsAlphaNumeric(string value)
        {
            foreach (char ch in value)
            {
                if (!char.IsLetterOrDigit(ch))
                    return false;
            }

            return true;
        }
    }
}
