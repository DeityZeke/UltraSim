using System;
using System.Security.Cryptography;
using System.Text;
using UltraSim.Server.Engines.Accounting;
using Xunit;

namespace Server.Tests.Accounting
{
    public sealed class ServerAccountServiceTests
    {
        private sealed class FakeAuthProvider : IAccountAuthenticationProvider
        {
            public int ExistingCalls { get; private set; }
            public int NewCalls { get; private set; }

            public bool TryAuthenticateExistingAccount(
                AccountModel account,
                ReadOnlySpan<byte> passwordHash32,
                ReadOnlySpan<byte> challengeResponse32,
                ReadOnlySpan<byte> challenge16,
                out string errorMessage)
            {
                ExistingCalls++;
                errorMessage = account.PasswordHash == "CUSTOM_HASH" ? string.Empty : "Unexpected account.";
                return errorMessage.Length == 0;
            }

            public bool TryPrepareNewAccount(
                string username,
                ReadOnlySpan<byte> passwordHash32,
                ReadOnlySpan<byte> challengeResponse32,
                ReadOnlySpan<byte> challenge16,
                out string passwordHash,
                out string passwordSalt,
                out string errorMessage)
            {
                NewCalls++;
                passwordHash = "CUSTOM_HASH";
                passwordSalt = "CUSTOM_SALT";
                errorMessage = string.Empty;
                return true;
            }
        }

        [Fact]
        public void TryLogin_DefaultProvider_CreatesAndAuthenticatesAccount()
        {
            var registry = new ServerAccountRegistry();
            var service = new ServerAccountService(registry, ServerAccountOptions.Default);

            byte[] passwordHash = SHA256.HashData(Encoding.UTF8.GetBytes("hunter2"));
            byte[] challenge = Encoding.ASCII.GetBytes("0123456789ABCDEF");
            byte[] challengeResponse = ComputeChallengeResponse(passwordHash, challenge);

            Assert.True(service.TryLogin("alice", passwordHash, challengeResponse, challenge, "10.0.0.1",
                out AccountModel? created, out bool isNew, out string error));
            Assert.True(isNew);
            Assert.NotNull(created);
            Assert.Equal("alice", created!.Username);
            Assert.Equal("10.0.0.1", created.KnownIps[^1]);

            Assert.True(service.TryLogin("alice", passwordHash, challengeResponse, challenge, "10.0.0.1",
                out AccountModel? existing, out bool existingIsNew, out string secondError));
            Assert.False(existingIsNew);
            Assert.NotNull(existing);
            Assert.Equal(string.Empty, error);
            Assert.Equal(string.Empty, secondError);
        }

        [Fact]
        public void TryLogin_CustomProvider_CreatesAccountAndAuthenticatesThroughProvider()
        {
            var registry = new ServerAccountRegistry();
            var provider = new FakeAuthProvider();
            var service = new ServerAccountService(registry, ServerAccountOptions.Default, provider);
            byte[] payload = new byte[32];
            byte[] challenge = new byte[16];

            Assert.True(service.TryLogin("bob", payload, payload, challenge, "10.0.0.2",
                out AccountModel? created, out bool isNew, out string error));
            Assert.True(isNew);
            Assert.NotNull(created);
            Assert.Equal("CUSTOM_HASH", created!.PasswordHash);
            Assert.Equal("CUSTOM_SALT", created.PasswordSalt);
            Assert.Equal(1, provider.NewCalls);

            Assert.True(service.TryLogin("bob", payload, payload, challenge, "10.0.0.2",
                out AccountModel? existing, out bool existingIsNew, out string secondError));
            Assert.False(existingIsNew);
            Assert.NotNull(existing);
            Assert.Equal(1, provider.ExistingCalls);
            Assert.Equal(string.Empty, error);
            Assert.Equal(string.Empty, secondError);
        }

        [Fact]
        public void TryLogin_SharedIpWithoutException_FlagsAccountsForReview()
        {
            var registry = new ServerAccountRegistry();
            var service = new ServerAccountService(
                registry,
                ServerAccountOptions.Default with { AccountsPerIp = 0 },
                new FakeAuthProvider());
            byte[] payload = new byte[32];
            byte[] challenge = new byte[16];

            Assert.True(service.TryLogin("alice", payload, payload, challenge, "10.0.0.1",
                out AccountModel? first, out _, out _));
            Assert.True(service.TryLogin("bob", payload, payload, challenge, "10.0.0.2",
                out AccountModel? second, out _, out _));
            Assert.NotNull(first);
            Assert.NotNull(second);

            Assert.True(service.TryLogin("bob", payload, payload, challenge, "10.0.0.1",
                out _, out _, out _));

            Assert.True(registry.TryGetById(first!.AccountId, out AccountModel flaggedFirst));
            Assert.True(registry.TryGetById(second!.AccountId, out AccountModel flaggedSecond));
            Assert.True(flaggedFirst.SharedIpReviewPending);
            Assert.True(flaggedSecond.SharedIpReviewPending);
        }

        [Fact]
        public void TryLogin_SharedIpWithException_DoesNotFlagAccountsForReview()
        {
            var registry = new ServerAccountRegistry();
            var service = new ServerAccountService(
                registry,
                ServerAccountOptions.Default with { AccountsPerIp = 0 },
                new FakeAuthProvider());
            byte[] payload = new byte[32];
            byte[] challenge = new byte[16];

            Assert.True(service.TryLogin("alice", payload, payload, challenge, "10.0.0.1",
                out AccountModel? first, out _, out _));
            Assert.True(service.TryLogin("bob", payload, payload, challenge, "10.0.0.2",
                out AccountModel? second, out _, out _));
            Assert.NotNull(first);
            Assert.NotNull(second);
            Assert.True(registry.SetAllowSharedIp(first!.AccountId, true));
            Assert.True(registry.SetAllowSharedIp(second!.AccountId, true));

            Assert.True(service.TryLogin("bob", payload, payload, challenge, "10.0.0.1",
                out _, out _, out _));

            Assert.True(registry.TryGetById(first.AccountId, out AccountModel exemptFirst));
            Assert.True(registry.TryGetById(second.AccountId, out AccountModel exemptSecond));
            Assert.False(exemptFirst.SharedIpReviewPending);
            Assert.False(exemptSecond.SharedIpReviewPending);
        }

        private static byte[] ComputeChallengeResponse(ReadOnlySpan<byte> passwordHash32, ReadOnlySpan<byte> challenge16)
        {
            string input = Convert.ToHexString(passwordHash32) + ":" + Convert.ToHexString(challenge16);
            return SHA256.HashData(Encoding.UTF8.GetBytes(input));
        }
    }
}
