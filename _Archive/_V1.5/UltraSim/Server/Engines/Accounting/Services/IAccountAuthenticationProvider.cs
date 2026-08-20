using System;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Validates login credentials for account-backed authentication flows.
    /// Implementations may vary, but they must still resolve into the shared account model.
    /// </summary>
    public interface IAccountAuthenticationProvider
    {
        bool TryAuthenticateExistingAccount(
            AccountModel account,
            ReadOnlySpan<byte> passwordHash32,
            ReadOnlySpan<byte> challengeResponse32,
            ReadOnlySpan<byte> challenge16,
            out string errorMessage);

        bool TryPrepareNewAccount(
            string username,
            ReadOnlySpan<byte> passwordHash32,
            ReadOnlySpan<byte> challengeResponse32,
            ReadOnlySpan<byte> challenge16,
            out string passwordHash,
            out string passwordSalt,
            out string errorMessage);
    }
}
