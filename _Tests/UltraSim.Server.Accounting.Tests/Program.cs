using UltraSim.Server.Accounting;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(Pbkdf2PasswordHashService_VerifiesRejectsAndFlagsWeakEnvelope), Pbkdf2PasswordHashService_VerifiesRejectsAndFlagsWeakEnvelope);
        Run(nameof(DefaultCredentialService_SuccessUpdatesAccountSessionAndAudit), DefaultCredentialService_SuccessUpdatesAccountSessionAndAudit);
        Run(nameof(DefaultCredentialService_RejectsBannedAccountDistinctly), DefaultCredentialService_RejectsBannedAccountDistinctly);
        Run(nameof(DefaultCredentialService_RateLimitsRepeatedFailures), DefaultCredentialService_RateLimitsRepeatedFailures);
        Run(nameof(DefaultCredentialService_DeniesSecondSessionByDefault), DefaultCredentialService_DeniesSecondSessionByDefault);
        Run(nameof(DefaultCredentialService_CanKickExistingSessionWhenConfigured), DefaultCredentialService_CanKickExistingSessionWhenConfigured);

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
}
