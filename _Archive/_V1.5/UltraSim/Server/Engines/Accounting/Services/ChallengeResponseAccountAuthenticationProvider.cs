using System;
using System.Security.Cryptography;
using System.Text;
using UltraSim.UI.Contracts;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Default account authentication provider using the existing login challenge-response flow.
    /// </summary>
    public sealed class ChallengeResponseAccountAuthenticationProvider : IAccountAuthenticationProvider
    {
        public static ChallengeResponseAccountAuthenticationProvider Instance { get; } = new();

        private ChallengeResponseAccountAuthenticationProvider()
        {
        }

        public bool TryAuthenticateExistingAccount(
            AccountModel account,
            ReadOnlySpan<byte> passwordHash32,
            ReadOnlySpan<byte> challengeResponse32,
            ReadOnlySpan<byte> challenge16,
            out string errorMessage)
        {
            string challengeHex = Convert.ToHexString(challenge16.Slice(0, LoginPacketCodec.ChallengePlainBytes));
            string expectedCr = ComputeChallengeResponse(account.PasswordHash, challengeHex);
            string actualCr = Convert.ToHexString(challengeResponse32.Slice(0, LoginPacketCodec.ChallengeResponseBytes));
            if (!string.Equals(expectedCr, actualCr, StringComparison.OrdinalIgnoreCase))
            {
                errorMessage = "Invalid username or password.";
                return false;
            }

            errorMessage = string.Empty;
            return true;
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
            string challengeHex = Convert.ToHexString(challenge16.Slice(0, LoginPacketCodec.ChallengePlainBytes));
            string newHashHex = Convert.ToHexString(passwordHash32.Slice(0, LoginPacketCodec.PasswordHashBytes));
            string expectedCr = ComputeChallengeResponse(newHashHex, challengeHex);
            string actualCr = Convert.ToHexString(challengeResponse32.Slice(0, LoginPacketCodec.ChallengeResponseBytes));
            if (!string.Equals(expectedCr, actualCr, StringComparison.OrdinalIgnoreCase))
            {
                passwordHash = string.Empty;
                passwordSalt = string.Empty;
                errorMessage = "Invalid credentials.";
                return false;
            }

            passwordHash = newHashHex;
            passwordSalt = string.Empty;
            errorMessage = string.Empty;
            return true;
        }

        private static string ComputeChallengeResponse(string storedHashHex, string challengeHex)
        {
            string input = storedHashHex + ":" + challengeHex;
            byte[] inputBytes = Encoding.UTF8.GetBytes(input);
            byte[] hash = SHA256.HashData(inputBytes);
            return Convert.ToHexString(hash);
        }
    }
}
