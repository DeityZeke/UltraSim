using UltraSim.Server.Accounting;
using UltraSim.Network;
using UltraSim.Server.Admission;
using UltraSim.UI;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(Pbkdf2PasswordHashService_VerifiesRejectsAndFlagsWeakEnvelope), Pbkdf2PasswordHashService_VerifiesRejectsAndFlagsWeakEnvelope);
        Run(nameof(Pbkdf2PasswordHashService_EnvelopeRoundTripsThroughAccountRecord), Pbkdf2PasswordHashService_EnvelopeRoundTripsThroughAccountRecord);
        Run(nameof(DefaultCredentialService_SuccessUpdatesAccountSessionAndAudit), DefaultCredentialService_SuccessUpdatesAccountSessionAndAudit);
        Run(nameof(DefaultCredentialService_RejectsBannedAccountDistinctly), DefaultCredentialService_RejectsBannedAccountDistinctly);
        Run(nameof(DefaultCredentialService_RateLimitsRepeatedFailures), DefaultCredentialService_RateLimitsRepeatedFailures);
        Run(nameof(DefaultCredentialService_DeniesSecondSessionByDefault), DefaultCredentialService_DeniesSecondSessionByDefault);
        Run(nameof(DefaultCredentialService_CanKickExistingSessionWhenConfigured), DefaultCredentialService_CanKickExistingSessionWhenConfigured);
        Run(nameof(DefaultCredentialService_AutoCreateUsesConfigurableRemoteAddressQuota), DefaultCredentialService_AutoCreateUsesConfigurableRemoteAddressQuota);
        Run(nameof(DefaultCredentialService_EmitsOperatorEventsForCreateSuccessAndFailure), DefaultCredentialService_EmitsOperatorEventsForCreateSuccessAndFailure);
        Run(nameof(DefaultCredentialService_RejectsPortalTokenMaterial), DefaultCredentialService_RejectsPortalTokenMaterial);
        Run(nameof(PortalCredentialAuthenticationProvider_AcceptsTokenWithoutLocalPassword), PortalCredentialAuthenticationProvider_AcceptsTokenWithoutLocalPassword);
        Run(nameof(AccountAccessAdministrationService_ReplacesMetadataAndAudits), AccountAccessAdministrationService_ReplacesMetadataAndAudits);
        Run(nameof(AccountCharacterService_CreatesListsAndDeletesCharactersBySlot), AccountCharacterService_CreatesListsAndDeletesCharactersBySlot);
        Run(nameof(AccountCharacterService_EnforcesCharacterLimitAndDeletePolicy), AccountCharacterService_EnforcesCharacterLimitAndDeletePolicy);
        Run(nameof(AccountCharacterService_AutoJoinRequiresSingleCharacterMode), AccountCharacterService_AutoJoinRequiresSingleCharacterMode);
        Run(nameof(AccountCharacterService_UpdatesPrefabKeyAndPositionSnapshot), AccountCharacterService_UpdatesPrefabKeyAndPositionSnapshot);
        Run(nameof(AccountingPersistenceSnapshot_CapturesAndLoadsRegistriesWithoutCompactingSlots), AccountingPersistenceSnapshot_CapturesAndLoadsRegistriesWithoutCompactingSlots);
        Run(nameof(JsonAccountingPersistenceStore_RoundTripsAccountsCharactersAndIndex), JsonAccountingPersistenceStore_RoundTripsAccountsCharactersAndIndex);
        Run(nameof(JsonAccountingPersistenceStore_PrunesDeletedCharacterFiles), JsonAccountingPersistenceStore_PrunesDeletedCharacterFiles);
        Run(nameof(JsonAccountingPersistenceStore_RotatesBackups), JsonAccountingPersistenceStore_RotatesBackups);
        Run(nameof(JsonAccountingPersistenceStore_RejectsInvalidSnapshotBeforeWriting), JsonAccountingPersistenceStore_RejectsInvalidSnapshotBeforeWriting);
        Run(nameof(AccountingAdmissionCredentialValidator_AcceptsMatchingLocalPasswordPacket), AccountingAdmissionCredentialValidator_AcceptsMatchingLocalPasswordPacket);
        Run(nameof(AccountingAdmissionCredentialValidator_RejectsWrongLocalPasswordPacket), AccountingAdmissionCredentialValidator_RejectsWrongLocalPasswordPacket);
        Run(nameof(AccountingAdmissionCredentialValidator_RejectsGenericUiActionAndWrongMaterial), AccountingAdmissionCredentialValidator_RejectsGenericUiActionAndWrongMaterial);

        Console.WriteLine("All Server accounting tests passed.");
    }

    private static void Pbkdf2PasswordHashService_VerifiesRejectsAndFlagsWeakEnvelope()
    {
        var weak = new Pbkdf2PasswordHashService(iterations: 100_000);
        string hash = weak.HashPassword("correct horse battery staple");

        Assert.True(weak.VerifyPassword("correct horse battery staple", hash));
        Assert.False(weak.VerifyPassword("wrong password", hash));
        Assert.False(weak.NeedsRehash(hash));

        var stronger = new Pbkdf2PasswordHashService(iterations: 120_000);
        Assert.True(stronger.NeedsRehash(hash));
    }

    private static void Pbkdf2PasswordHashService_EnvelopeRoundTripsThroughAccountRecord()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        string hash = passwords.HashPassword("correct horse battery staple");
        var account = new ServerAccountRecord(
            7,
            "Alice",
            hash,
            AccountStatus.Active,
            now,
            DateTimeOffset.MinValue,
            []);

        Assert.True(Pbkdf2PasswordHashService.TryReadEnvelope(account.PasswordHash, out Pbkdf2PasswordHashEnvelope envelope));
        Assert.Equal(1, envelope.Version);
        Assert.Equal(100_000, envelope.Iterations);
        Assert.Equal(32, envelope.SaltLength);
        Assert.Equal(32, envelope.HashLength);
        Assert.True(passwords.VerifyPassword("correct horse battery staple", account.PasswordHash));
    }

    private static void DefaultCredentialService_SuccessUpdatesAccountSessionAndAudit()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        ServerAccountRecord created = accounts.CreateAccount("Alice", passwords.HashPassword("swordfish"), now);
        var audit = new InMemoryAccountAuditSink();
        var service = new DefaultCredentialService(
            accounts,
            passwords,
            new InMemoryLoginRateLimiter(),
            new InMemoryAccountSessionTracker(),
            audit);

        CredentialValidationResult result = service.ValidateLogin(new CredentialValidationRequest(
            "alice",
            "swordfish",
            "127.0.0.1",
            "session-a",
            now.AddMinutes(1)));

        Assert.True(result.Succeeded);
        Assert.Equal(created.AccountId, result.Account!.AccountId);
        Assert.True(accounts.TryGetById(created.AccountId, out ServerAccountRecord updated));
        Assert.Equal(now.AddMinutes(1), updated.LastLoginUtc);
        Assert.Equal("127.0.0.1", updated.KnownRemoteAddresses[0]);
        Assert.Equal(1, audit.Entries.Count);
        Assert.Equal(CredentialValidationResultKind.Success, audit.Entries[0].ResultKind);
    }

    private static void DefaultCredentialService_RejectsBannedAccountDistinctly()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        accounts.CreateAccount("Alice", passwords.HashPassword("swordfish"), now, AccountStatus.Banned);
        var service = CreateService(accounts, passwords);

        CredentialValidationResult result = service.ValidateLogin(new CredentialValidationRequest(
            "Alice",
            "swordfish",
            "127.0.0.1",
            "session-a",
            now));

        Assert.False(result.Succeeded);
        Assert.Equal(CredentialValidationResultKind.Banned, result.Kind);
    }

    private static void DefaultCredentialService_RateLimitsRepeatedFailures()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        accounts.CreateAccount("Alice", passwords.HashPassword("swordfish"), now);
        var service = new DefaultCredentialService(
            accounts,
            passwords,
            new InMemoryLoginRateLimiter(maxFailures: 2, lockout: TimeSpan.FromMinutes(3)),
            new InMemoryAccountSessionTracker());

        CredentialValidationRequest bad = new("Alice", "wrong", "127.0.0.1", "session-a", now);
        Assert.Equal(CredentialValidationResultKind.InvalidPassword, service.ValidateLogin(bad).Kind);
        Assert.Equal(CredentialValidationResultKind.InvalidPassword, service.ValidateLogin(bad with { NowUtc = now.AddSeconds(1) }).Kind);

        CredentialValidationResult limited = service.ValidateLogin(bad with { NowUtc = now.AddSeconds(2) });
        Assert.Equal(CredentialValidationResultKind.RateLimited, limited.Kind);
        Assert.True(limited.RetryAfter.HasValue);
    }

    private static void DefaultCredentialService_DeniesSecondSessionByDefault()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        accounts.CreateAccount("Alice", passwords.HashPassword("swordfish"), now);
        var service = CreateService(accounts, passwords);

        Assert.True(service.ValidateLogin(new CredentialValidationRequest("Alice", "swordfish", "127.0.0.1", "session-a", now)).Succeeded);
        CredentialValidationResult second = service.ValidateLogin(new CredentialValidationRequest("Alice", "swordfish", "127.0.0.2", "session-b", now.AddSeconds(1)));

        Assert.Equal(CredentialValidationResultKind.AlreadyLoggedIn, second.Kind);
    }

    private static void DefaultCredentialService_CanKickExistingSessionWhenConfigured()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        accounts.CreateAccount("Alice", passwords.HashPassword("swordfish"), now);
        var service = new DefaultCredentialService(
            accounts,
            passwords,
            new InMemoryLoginRateLimiter(),
            new InMemoryAccountSessionTracker(),
            sessionConflictMode: AccountSessionConflictMode.KickExisting);

        Assert.True(service.ValidateLogin(new CredentialValidationRequest("Alice", "swordfish", "127.0.0.1", "session-a", now)).Succeeded);
        CredentialValidationResult second = service.ValidateLogin(new CredentialValidationRequest("Alice", "swordfish", "127.0.0.2", "session-b", now.AddSeconds(1)));

        Assert.True(second.Succeeded);
    }

    private static void DefaultCredentialService_AutoCreateUsesConfigurableRemoteAddressQuota()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        var sessions = new InMemoryAccountSessionTracker();
        accounts.CreateAccount("StaffCreated", passwords.HashPassword("temporary"), now.AddMinutes(-1));
        var service = new DefaultCredentialService(
            accounts,
            passwords,
            new InMemoryLoginRateLimiter(),
            sessions,
            autoCreateUnknownAccounts: true,
            maxAutoCreatedAccountsPerRemoteAddress: 1);

        CredentialValidationResult first = service.ValidateLogin(new CredentialValidationRequest(
            "Alice",
            "swordfish",
            "127.0.0.1",
            "session-a",
            now));
        Assert.True(first.Succeeded);

        sessions.EndSession(first.Account!.AccountId, "session-a");
        CredentialValidationResult second = service.ValidateLogin(new CredentialValidationRequest(
            "Bob",
            "swordfish",
            "127.0.0.1",
            "session-b",
            now.AddSeconds(1)));

        Assert.False(second.Succeeded);
        Assert.Equal(CredentialValidationResultKind.UnknownAccount, second.Kind);
        Assert.Equal("This IP address has reached the account creation limit (1).", second.PublicMessage);

        CredentialValidationResult staffCreated = service.ValidateLogin(new CredentialValidationRequest(
            "StaffCreated",
            "temporary",
            "127.0.0.1",
            "session-c",
            now.AddSeconds(2)));

        Assert.True(staffCreated.Succeeded);
    }

    private static void DefaultCredentialService_EmitsOperatorEventsForCreateSuccessAndFailure()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        var sessions = new InMemoryAccountSessionTracker();
        var events = new List<AccountOperatorEvent>();
        var service = new DefaultCredentialService(
            accounts,
            passwords,
            new InMemoryLoginRateLimiter(),
            sessions,
            autoCreateUnknownAccounts: true,
            maxAutoCreatedAccountsPerRemoteAddress: 2,
            operatorEvents: events.Add);

        CredentialValidationResult created = service.ValidateLogin(new CredentialValidationRequest(
            "Alice",
            "swordfish",
            "127.0.0.1",
            "session-a",
            now));

        Assert.True(created.Succeeded);
        Assert.Equal(2, events.Count);
        Assert.Equal(AccountOperatorEventKind.AccountCreated, events[0].Kind);
        Assert.Equal(AccountOperatorEventKind.LoginSucceeded, events[1].Kind);
        Assert.Equal("Alice", events[0].Username);
        Assert.Equal("127.0.0.1", events[0].RemoteAddress);

        sessions.EndSession(created.Account!.AccountId, "session-a");
        CredentialValidationResult failed = service.ValidateLogin(new CredentialValidationRequest(
            "Alice",
            "wrong",
            "127.0.0.1",
            "session-b",
            now.AddSeconds(1)));

        Assert.False(failed.Succeeded);
        Assert.Equal(3, events.Count);
        Assert.Equal(AccountOperatorEventKind.LoginFailed, events[2].Kind);
        Assert.Equal(CredentialValidationResultKind.InvalidPassword, events[2].ResultKind);
        Assert.Equal("Invalid username or password.", events[2].PublicMessage);
    }

    private static void DefaultCredentialService_RejectsPortalTokenMaterial()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        accounts.CreateAccount("Alice", passwords.HashPassword("swordfish"), now);
        var service = CreateService(accounts, passwords);

        CredentialValidationResult result = service.ValidateLogin(new CredentialValidationRequest(
            "Alice",
            "portal-token",
            "127.0.0.1",
            "session-a",
            now,
            CredentialMaterialKind.PortalSessionToken));

        Assert.Equal(CredentialValidationResultKind.UnsupportedCredentialMaterial, result.Kind);
    }

    private static void PortalCredentialAuthenticationProvider_AcceptsTokenWithoutLocalPassword()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var portal = new FakePortalAuthenticator("token-123", "platform-account-7");
        var provider = new PortalCredentialAuthenticationProvider(
            portal,
            new InMemoryLoginRateLimiter(),
            "server-instance-1");

        CredentialValidationResult result = provider.ValidateLogin(new CredentialValidationRequest(
            "ignored-by-portal",
            "token-123",
            "127.0.0.1",
            "session-a",
            now,
            CredentialMaterialKind.PortalSessionToken));

        Assert.True(result.Succeeded);
        Assert.Equal("platform-account-7", result.Account!.Username);
        Assert.Equal(AccountAuthenticationMode.UltraSimPortalAccounts, provider.Mode);
        Assert.Equal("server-instance-1", portal.LastServerInstanceId);
    }

    private static void AccountAccessAdministrationService_ReplacesMetadataAndAudits()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var accounts = new InMemoryAccountRegistry();
        ServerAccountRecord admin = accounts.CreateAccount("Admin", "hash", now);
        ServerAccountRecord player = accounts.CreateAccount("Alice", "hash", now);
        var audit = new InMemoryAccountAccessAuditSink();
        var service = new AccountAccessAdministrationService(accounts, audit, () => now.AddMinutes(1));

        AccountAccessUpdateResult result = service.ReplaceAccessMetadata(
            player.AccountId,
            accessRuleKeys: ["Staff", "builder", "staff"],
            directPermissionGrants: ["Command.Spawn", "admin.entity.inspect"],
            directPermissionDenies: ["ADMIN.Account.Delete"],
            actorAccountId: admin.AccountId,
            reason: " promotion ");

        Assert.True(result.Succeeded);
        Assert.True(accounts.TryGetById(player.AccountId, out ServerAccountRecord updated));
        Assert.True(updated.AccessRuleKeys.SequenceEqual(["builder", "staff"]));
        Assert.True(updated.DirectPermissionGrants.SequenceEqual(["admin.entity.inspect", "command.spawn"]));
        Assert.True(updated.DirectPermissionDenies.SequenceEqual(["admin.account.delete"]));
        Assert.Equal(1, audit.Entries.Count);
        AccountAccessAuditEntry entry = audit.Entries[0];
        Assert.Equal(now.AddMinutes(1), entry.TimestampUtc);
        Assert.Equal(admin.AccountId, entry.ActorAccountId);
        Assert.Equal(player.AccountId, entry.TargetAccountId);
        Assert.Equal("Alice", entry.TargetUsername);
        Assert.Equal(AccountAccessAuditAction.MetadataReplaced, entry.Action);
        Assert.True(entry.AccessRuleKeys.SequenceEqual(["builder", "staff"]));
        Assert.True(entry.DirectPermissionGrants.SequenceEqual(["admin.entity.inspect", "command.spawn"]));
        Assert.True(entry.DirectPermissionDenies.SequenceEqual(["admin.account.delete"]));
        Assert.Equal("promotion", entry.Reason);

        AccountAccessUpdateResult missing = service.ReplaceAccessMetadata(999, accessRuleKeys: ["staff"]);

        Assert.False(missing.Succeeded);
        Assert.Contains("not found", missing.Error);
        Assert.Equal(1, audit.Entries.Count);
    }

    private static void AccountCharacterService_CreatesListsAndDeletesCharactersBySlot()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var accounts = new InMemoryAccountRegistry();
        ServerAccountRecord account = accounts.CreateAccount("Alice", "hash", now);
        var characters = new InMemoryCharacterRegistry();
        var service = new AccountCharacterService(
            accounts,
            characters,
            new AccountCharacterOptions(MaxCharactersPerAccount: 3));

        CharacterCreateResult first = service.TryCreateCharacter(
            account.AccountId,
            "Asha",
            "mobiles/human",
            now);
        CharacterCreateResult second = service.TryCreateCharacter(
            account.AccountId,
            "Borin",
            "mobiles/human",
            now.AddSeconds(1));

        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        IReadOnlyList<ServerCharacterRecord> listed = service.GetCharactersForAccount(account.AccountId);
        Assert.Equal(2, listed.Count);
        Assert.Equal(first.Character!.CharacterId, listed[0].CharacterId);
        Assert.Equal(second.Character!.CharacterId, listed[1].CharacterId);
        Assert.Equal("mobiles/human", listed[0].PrefabKey);

        Assert.True(service.TryDeleteCharacter(account.AccountId, first.Character.CharacterId, out ServerAccountRecord? afterDelete));
        Assert.True(afterDelete is not null);
        Assert.Equal(0UL, afterDelete!.CharacterSlots[0]);
        Assert.Equal(second.Character.CharacterId, afterDelete.CharacterSlots[1]);

        IReadOnlyList<ServerCharacterRecord> afterList = service.GetCharactersForAccount(account.AccountId);
        Assert.Equal(1, afterList.Count);
        Assert.Equal(second.Character.CharacterId, afterList[0].CharacterId);
    }

    private static void AccountCharacterService_EnforcesCharacterLimitAndDeletePolicy()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var accounts = new InMemoryAccountRegistry();
        ServerAccountRecord account = accounts.CreateAccount("Alice", "hash", now);
        var characters = new InMemoryCharacterRegistry();
        var service = new AccountCharacterService(
            accounts,
            characters,
            new AccountCharacterOptions(MaxCharactersPerAccount: 1, AllowCharacterDeletion: false));

        CharacterCreateResult first = service.TryCreateCharacter(
            account.AccountId,
            "Asha",
            "mobiles/human",
            now);
        CharacterCreateResult second = service.TryCreateCharacter(
            account.AccountId,
            "Borin",
            "mobiles/human",
            now);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Equal("Character slots full.", second.Error);
        Assert.False(service.TryDeleteCharacter(account.AccountId, first.Character!.CharacterId, out _));
    }

    private static void AccountCharacterService_UpdatesPrefabKeyAndPositionSnapshot()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var accounts = new InMemoryAccountRegistry();
        ServerAccountRecord account = accounts.CreateAccount("Alice", "hash", now);
        var characters = new InMemoryCharacterRegistry();
        var service = new AccountCharacterService(accounts, characters);
        CharacterCreateResult created = service.TryCreateCharacter(
            account.AccountId,
            "Asha",
            "mobiles/human",
            now);
        Assert.True(created.Succeeded);

        var snapshot = new CharacterPositionSnapshot(10.5f, 2.25f, -8.75f, 1.5f);
        Assert.True(service.TryUpdateCharacterSnapshot(
            created.Character!.CharacterId,
            now.AddMinutes(1),
            "mobiles/human_veteran",
            snapshot,
            out ServerCharacterRecord updated));

        Assert.Equal("mobiles/human_veteran", updated.PrefabKey);
        Assert.Equal(snapshot, updated.Position);
        Assert.Equal(now.AddMinutes(1), updated.LastPlayedUtc);
    }

    private static void AccountingPersistenceSnapshot_CapturesAndLoadsRegistriesWithoutCompactingSlots()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var accounts = new InMemoryAccountRegistry();
        ServerAccountRecord account = accounts.CreateAccount("Alice", "hash", now);
        accounts.Update(account.WithAccessMetadata(
            ruleKeys: ["Staff", "builder"],
            directGrants: ["Command.Spawn"],
            directDenies: ["admin.account.delete"]));
        var characters = new InMemoryCharacterRegistry();
        var service = new AccountCharacterService(accounts, characters, new AccountCharacterOptions(MaxCharactersPerAccount: 3));

        CharacterCreateResult first = service.TryCreateCharacter(account.AccountId, "Asha", "mobiles/human", now);
        CharacterCreateResult second = service.TryCreateCharacter(account.AccountId, "Borin", "mobiles/human", now);
        Assert.True(first.Succeeded);
        Assert.True(second.Succeeded);
        Assert.True(service.TryDeleteCharacter(account.AccountId, first.Character!.CharacterId, out _));

        AccountingPersistenceSnapshot snapshot = AccountingPersistenceSnapshot.Capture(accounts, characters);
        var loadedAccounts = new InMemoryAccountRegistry();
        var loadedCharacters = new InMemoryCharacterRegistry();
        loadedAccounts.LoadSnapshot(snapshot.Accounts);
        loadedCharacters.LoadSnapshot(snapshot.Characters);
        var loadedService = new AccountCharacterService(loadedAccounts, loadedCharacters, new AccountCharacterOptions(MaxCharactersPerAccount: 3));

        Assert.True(loadedAccounts.TryGetById(account.AccountId, out ServerAccountRecord loadedAccount));
        Assert.Equal(0UL, loadedAccount.CharacterSlots[0]);
        Assert.Equal(second.Character!.CharacterId, loadedAccount.CharacterSlots[1]);
        Assert.True(loadedAccount.AccessRuleKeys.SequenceEqual(["builder", "staff"]));
        Assert.True(loadedAccount.DirectPermissionGrants.SequenceEqual(["command.spawn"]));
        Assert.True(loadedAccount.DirectPermissionDenies.SequenceEqual(["admin.account.delete"]));
        IReadOnlyList<ServerCharacterRecord> listed = loadedService.GetCharactersForAccount(account.AccountId);
        Assert.Equal(1, listed.Count);
        Assert.Equal(second.Character.CharacterId, listed[0].CharacterId);
    }

    private static void JsonAccountingPersistenceStore_RoundTripsAccountsCharactersAndIndex()
    {
        string root = CreateTempRoot("acct_json_roundtrip");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var accounts = new InMemoryAccountRegistry();
            ServerAccountRecord account = accounts.CreateAccount("Alice", "hash", now);
            accounts.Update(account.WithAccessMetadata(
                ruleKeys: ["staff"],
                directGrants: ["command.panel"],
                directDenies: ["command.shutdown"]));
            var characters = new InMemoryCharacterRegistry();
            var service = new AccountCharacterService(accounts, characters);
            CharacterCreateResult created = service.TryCreateCharacter(
                account.AccountId,
                "Asha",
                "mobiles/human",
                now,
                new CharacterPositionSnapshot(1, 2, 3, 4));
            Assert.True(created.Succeeded);

            var store = new JsonAccountingPersistenceStore(root);
            store.Save(AccountingPersistenceSnapshot.Capture(accounts, characters));
            AccountingPersistenceSnapshot loaded = store.Load();

            Assert.Equal(1, loaded.Accounts.Count);
            Assert.Equal(1, loaded.Characters.Count);
            Assert.Equal("Alice", loaded.Accounts[0].Username);
            Assert.Equal(created.Character!.CharacterId, loaded.Accounts[0].CharacterSlots[0]);
            Assert.True(loaded.Accounts[0].AccessRuleKeys.SequenceEqual(["staff"]));
            Assert.True(loaded.Accounts[0].DirectPermissionGrants.SequenceEqual(["command.panel"]));
            Assert.True(loaded.Accounts[0].DirectPermissionDenies.SequenceEqual(["command.shutdown"]));
            Assert.Equal("mobiles/human", loaded.Characters[0].PrefabKey);
            Assert.Equal(new CharacterPositionSnapshot(1, 2, 3, 4), loaded.Characters[0].Position);
            Assert.True(File.Exists(Path.Combine(root, "accounts", "index.json")));
            string accountJson = File.ReadAllText(Path.Combine(root, "accounts", "00000000000000000001.json"));
            Assert.Contains("\"accessRuleKeys\"", accountJson);
            Assert.Contains("\"directPermissionGrants\"", accountJson);
            Assert.Contains("\"directPermissionDenies\"", accountJson);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void JsonAccountingPersistenceStore_RotatesBackups()
    {
        string root = CreateTempRoot("acct_json_backups");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var store = new JsonAccountingPersistenceStore(root, new AccountingPersistenceOptions(BackupCount: 1));
            var accounts = new InMemoryAccountRegistry();
            var characters = new InMemoryCharacterRegistry();
            ServerAccountRecord account = accounts.CreateAccount("Alice", "hash-1", now);

            store.Save(AccountingPersistenceSnapshot.Capture(accounts, characters));
            accounts.Update(account with { PasswordHash = "hash-2" });
            store.Save(AccountingPersistenceSnapshot.Capture(accounts, characters));
            accounts.Update(account with { PasswordHash = "hash-3" });
            store.Save(AccountingPersistenceSnapshot.Capture(accounts, characters));

            string accountPath = Path.Combine(root, "accounts", "00000000000000000001.json");
            string[] backups = Directory.GetFiles(Path.GetDirectoryName(accountPath)!, "00000000000000000001.json.*.bak");
            Assert.Equal(1, backups.Length);
            Assert.Contains("\"passwordHash\": \"hash-3\"", File.ReadAllText(accountPath));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void JsonAccountingPersistenceStore_PrunesDeletedCharacterFiles()
    {
        string root = CreateTempRoot("acct_json_prune");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var accounts = new InMemoryAccountRegistry();
            ServerAccountRecord account = accounts.CreateAccount("Alice", "hash", now);
            var characters = new InMemoryCharacterRegistry();
            var service = new AccountCharacterService(accounts, characters);
            CharacterCreateResult created = service.TryCreateCharacter(
                account.AccountId,
                "Asha",
                "mobiles/human",
                now);
            Assert.True(created.Succeeded);

            var store = new JsonAccountingPersistenceStore(root);
            store.Save(AccountingPersistenceSnapshot.Capture(accounts, characters));
            Assert.True(service.TryDeleteCharacter(account.AccountId, created.Character!.CharacterId, out _));
            store.Save(AccountingPersistenceSnapshot.Capture(accounts, characters));

            AccountingPersistenceSnapshot loaded = store.Load();
            Assert.Equal(1, loaded.Accounts.Count);
            Assert.Equal(0, loaded.Characters.Count);
            Assert.Equal(0UL, loaded.Accounts[0].CharacterSlots[0]);
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void JsonAccountingPersistenceStore_RejectsInvalidSnapshotBeforeWriting()
    {
        string root = CreateTempRoot("acct_json_invalid");
        try
        {
            DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
            var store = new JsonAccountingPersistenceStore(root);
            var snapshot = new AccountingPersistenceSnapshot(
                [
                    new ServerAccountRecord(1, "Alice", "hash", AccountStatus.Active, now, DateTimeOffset.MinValue, []),
                    new ServerAccountRecord(1, "Bob", "hash", AccountStatus.Active, now, DateTimeOffset.MinValue, [])
                ],
                []);

            Assert.Throws<InvalidOperationException>(() => store.Save(snapshot));
            Assert.False(Directory.Exists(Path.Combine(root, "accounts")));
        }
        finally
        {
            DeleteTempRoot(root);
        }
    }

    private static void AccountCharacterService_AutoJoinRequiresSingleCharacterMode()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var accounts = new InMemoryAccountRegistry();
        ServerAccountRecord account = accounts.CreateAccount("Alice", "hash", now);
        var characters = new InMemoryCharacterRegistry();
        var service = new AccountCharacterService(
            accounts,
            characters,
            new AccountCharacterOptions(MaxCharactersPerAccount: 1, AutoJoinSingleCharacter: true));

        Assert.False(service.TryGetAutoJoinCharacter(account.AccountId, out _));
        CharacterCreateResult created = service.TryCreateCharacter(
            account.AccountId,
            "Asha",
            "mobiles/human",
            now);

        Assert.True(created.Succeeded);
        Assert.True(service.TryGetAutoJoinCharacter(account.AccountId, out ServerCharacterRecord autoJoin));
        Assert.Equal(created.Character!.CharacterId, autoJoin.CharacterId);
    }

    private static void AccountingAdmissionCredentialValidator_AcceptsMatchingLocalPasswordPacket()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        ServerAccountRecord account = accounts.CreateAccount("Alice", passwords.HashPassword("swordfish"), now);
        var validator = new AccountingAdmissionCredentialValidator(
            CreateService(accounts, passwords),
            remoteAddressResolver: static _ => "127.0.0.1",
            sessionKeyResolver: static _ => "session-a",
            clock: () => now.AddSeconds(1));

        ServerAdmissionCredentialValidationResult result = validator.ValidateTrustedCredential(
            new ConnectionId(7),
            new AdmissionCredentialRequest(
                "panel-1",
                "login.submit",
                "Alice",
                LoginCredentialMaterialKind.PlainPassword,
                "swordfish"u8.ToArray()));

        Assert.True(result.Accepted);
        Assert.Equal(account.AccountId, result.AccountId);
    }

    private static void AccountingAdmissionCredentialValidator_RejectsGenericUiActionAndWrongMaterial()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        accounts.CreateAccount("Alice", passwords.HashPassword("swordfish"), now);
        var validator = new AccountingAdmissionCredentialValidator(
            CreateService(accounts, passwords),
            clock: () => now);

        var genericAction = new UiActionRequestDocument(
            "panel-1",
            default,
            null,
            1,
            "login.submit",
            UiActionContextKind.TrustedCredential,
            null,
            []);
        ServerAdmissionCredentialValidationResult genericResult = validator.ValidateTrustedCredential(new ConnectionId(7), genericAction);
        Assert.False(genericResult.Accepted);
        Assert.Equal(AdmissionCredentialFailureReason.UnsupportedCredential, genericResult.FailureReason);

        ServerAdmissionCredentialValidationResult tokenResult = validator.ValidateTrustedCredential(
            new ConnectionId(7),
            new AdmissionCredentialRequest(
                "panel-1",
                "login.submit",
                "Alice",
                LoginCredentialMaterialKind.PortalSessionToken,
                "portal-token"u8.ToArray()));
        Assert.False(tokenResult.Accepted);
        Assert.Equal(AdmissionCredentialFailureReason.UnsupportedCredential, tokenResult.FailureReason);
        Assert.True(tokenResult.Error.Length > 0);
    }

    private static void AccountingAdmissionCredentialValidator_RejectsWrongLocalPasswordPacket()
    {
        DateTimeOffset now = new(2026, 5, 2, 12, 0, 0, TimeSpan.Zero);
        var passwords = new Pbkdf2PasswordHashService(iterations: 100_000);
        var accounts = new InMemoryAccountRegistry();
        accounts.CreateAccount("Alice", passwords.HashPassword("swordfish"), now);
        var validator = new AccountingAdmissionCredentialValidator(
            CreateService(accounts, passwords),
            remoteAddressResolver: static _ => "127.0.0.1",
            sessionKeyResolver: static _ => "session-a",
            clock: () => now.AddSeconds(1));

        ServerAdmissionCredentialValidationResult result = validator.ValidateTrustedCredential(
            new ConnectionId(7),
            new AdmissionCredentialRequest(
                "panel-1",
                "login.submit",
                "Alice",
                LoginCredentialMaterialKind.PlainPassword,
                "wrong"u8.ToArray()));

        Assert.False(result.Accepted);
        Assert.Equal(AdmissionCredentialFailureReason.InvalidCredentials, result.FailureReason);
    }

    private static DefaultCredentialService CreateService(IAccountRegistry accounts, IPasswordHashService passwords)
    {
        return new DefaultCredentialService(
            accounts,
            passwords,
            new InMemoryLoginRateLimiter(),
            new InMemoryAccountSessionTracker());
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {name}: {ex}");
            Environment.ExitCode = 1;
            throw;
        }
    }

    private static string CreateTempRoot(string prefix)
    {
        return Path.Combine(Path.GetTempPath(), $"{prefix}_{Guid.NewGuid():N}");
    }

    private static void DeleteTempRoot(string root)
    {
        if (Directory.Exists(root))
            Directory.Delete(root, recursive: true);
    }
}

internal sealed class FakePortalAuthenticator : IPortalAccountAuthenticator
{
    private readonly string _acceptedToken;
    private readonly string _platformAccountId;

    public FakePortalAuthenticator(string acceptedToken, string platformAccountId)
    {
        _acceptedToken = acceptedToken;
        _platformAccountId = platformAccountId;
    }

    public string? LastServerInstanceId { get; private set; }

    public PortalAccountAuthenticationResult ValidatePortalSession(
        string portalSessionToken,
        string serverInstanceId,
        string remoteAddress,
        DateTimeOffset nowUtc)
    {
        LastServerInstanceId = serverInstanceId;
        return string.Equals(portalSessionToken, _acceptedToken, StringComparison.Ordinal)
            ? PortalAccountAuthenticationResult.Accept(_platformAccountId)
            : PortalAccountAuthenticationResult.Reject(AccountStatus.LockedOut, "Portal token rejected.");
    }
}

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
            throw new InvalidOperationException(message ?? "Expected condition to be true.");
    }

    public static void False(bool condition)
    {
        if (condition)
            throw new InvalidOperationException("Expected condition to be false.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }

    public static void Contains(string expectedSubstring, string actual)
    {
        if (!actual.Contains(expectedSubstring, StringComparison.Ordinal))
            throw new InvalidOperationException($"Expected text to contain '{expectedSubstring}'.");
    }

    public static void Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            return;
        }

        throw new InvalidOperationException($"Expected exception '{typeof(TException).Name}'.");
    }
}
