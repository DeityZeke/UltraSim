using System;
using System.Buffers.Binary;
using System.Text;

namespace UltraSim.UI.Contracts
{
    /// <summary>
    /// Wire codec for the login challenge-response flow.
    ///
    /// Byte assignments (all on ReliableOrdered channel):
    ///   0xE8 — LoginRequestPacket  (client → server)
    ///   0xE9 — LoginResponsePacket (server → client)
    ///
    /// Challenge-response scheme (prevents replay without sending password over wire):
    ///   Client:  passwordHash      = SHA-256(password)                         [32 bytes]
    ///            challengeResponse = SHA-256(UTF8(Hex(passwordHash) + ":" + Hex(challenge16)))  [32 bytes]
    ///   Server:  For existing account: SHA-256(UTF8(account.PasswordHash + ":" + Hex(challenge16))) == challengeResponse
    ///            For new account: validate CR then store Hex(passwordHash) as PasswordHash.
    ///
    /// LoginPanel open payload (server → client):
    ///   [16] random challenge bytes — read by client binding before computing CR.
    /// </summary>
    public static class LoginPacketCodec
    {
        // ---- Message type bytes ------------------------------------------------

        /// <summary>Client → server login request.</summary>
        public const byte LoginRequestMessageType = 0xE8;

        /// <summary>Server → client login response (failure path; success = EnterGame).</summary>
        public const byte LoginResponseMessageType = 0xE9;

        // ---- Layout constants --------------------------------------------------

        public const int ChallengePlainBytes     = 16;
        public const int PasswordHashBytes       = 32;
        public const int ChallengeResponseBytes  = 32;

        // LoginRequest minimum fixed header: [1] type + [4] instanceId + [1] usernameLen
        private const int LoginRequestFixedHeader = 6;

        // LoginResponse fixed body: [1] type + [4] instanceId + [1] result + [16] newChallenge + [1] detailLen
        private const int LoginResponseFixedSize = 1 + 4 + 1 + ChallengePlainBytes + 1;

        // =========================================================================
        // Open-panel payload — challenge nonce (server → client)
        // =========================================================================

        /// <summary>
        /// Encodes a 16-byte random challenge as the LoginPanel open payload.
        /// The server generates this nonce and stores it; the client uses it to
        /// compute the challenge response before sending <see cref="LoginRequestMessageType"/>.
        /// </summary>
        public static byte[] EncodeChallengePayload(ReadOnlySpan<byte> challenge16)
        {
            if (challenge16.Length < ChallengePlainBytes)
                throw new ArgumentException("Challenge must be at least 16 bytes.", nameof(challenge16));

            byte[] buf = new byte[ChallengePlainBytes];
            challenge16.Slice(0, ChallengePlainBytes).CopyTo(buf);
            return buf;
        }

        /// <summary>
        /// Reads the 16-byte challenge from the panel open payload.
        /// Returns false if the payload is too short.
        /// </summary>
        public static bool TryReadChallengePayload(ReadOnlyMemory<byte> payload, out ReadOnlyMemory<byte> challenge)
        {
            challenge = default;
            if (payload.Length < ChallengePlainBytes)
                return false;
            challenge = payload.Slice(0, ChallengePlainBytes);
            return true;
        }

        // =========================================================================
        // LoginRequestPacket (client → server)
        // Wire: [1] 0xE8 + [4] instanceId LE + [1] usernameLen + [N] username UTF-8
        //       + [32] passwordHash + [32] challengeResponse
        // =========================================================================

        /// <summary>
        /// Encodes a LoginRequestPacket into <paramref name="dest"/>.
        /// Returns false if the username is too long or the destination is too small.
        /// </summary>
        public static bool TryEncodeLoginRequest(
            Span<byte> dest,
            UiPanelInstanceId instanceId,
            ReadOnlySpan<byte> usernameUtf8,
            ReadOnlySpan<byte> passwordHash32,
            ReadOnlySpan<byte> challengeResponse32,
            out int written)
        {
            written = 0;
            if (usernameUtf8.Length > 255) return false;

            int needed = LoginRequestFixedHeader + usernameUtf8.Length + PasswordHashBytes + ChallengeResponseBytes;
            if (dest.Length < needed) return false;

            int pos = 0;
            dest[pos++] = LoginRequestMessageType;
            BinaryPrimitives.WriteUInt32LittleEndian(dest.Slice(pos, 4), instanceId.Value); pos += 4;
            dest[pos++] = (byte)usernameUtf8.Length;
            usernameUtf8.CopyTo(dest.Slice(pos, usernameUtf8.Length)); pos += usernameUtf8.Length;
            passwordHash32.Slice(0, PasswordHashBytes).CopyTo(dest.Slice(pos)); pos += PasswordHashBytes;
            challengeResponse32.Slice(0, ChallengeResponseBytes).CopyTo(dest.Slice(pos)); pos += ChallengeResponseBytes;
            written = pos;
            return true;
        }

        /// <summary>
        /// Decodes a LoginRequestPacket from the given payload.
        /// The <paramref name="passwordHash"/> and <paramref name="challengeResponse"/> spans
        /// are slices of <paramref name="payload"/> — callers must copy if they need to outlive the span.
        /// </summary>
        public static bool TryDecodeLoginRequest(
            ReadOnlySpan<byte> payload,
            out UiPanelInstanceId instanceId,
            out string username,
            out ReadOnlySpan<byte> passwordHash,
            out ReadOnlySpan<byte> challengeResponse)
        {
            instanceId = default;
            username = string.Empty;
            passwordHash = default;
            challengeResponse = default;

            if (payload.Length < LoginRequestFixedHeader || payload[0] != LoginRequestMessageType)
                return false;

            instanceId = new UiPanelInstanceId(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(1, 4)));
            int uLen = payload[5];

            int needed = LoginRequestFixedHeader + uLen + PasswordHashBytes + ChallengeResponseBytes;
            if (payload.Length < needed) return false;

            username = uLen > 0 ? Encoding.UTF8.GetString(payload.Slice(6, uLen)) : string.Empty;
            int offset = 6 + uLen;
            passwordHash = payload.Slice(offset, PasswordHashBytes);
            challengeResponse = payload.Slice(offset + PasswordHashBytes, ChallengeResponseBytes);
            return true;
        }

        // =========================================================================
        // LoginResponsePacket (server → client)
        // Wire: [1] 0xE9 + [4] instanceId LE + [1] result + [16] newChallenge
        //       + [1] detailLen + [N] detail UTF-8
        // =========================================================================

        /// <summary>Result codes for the LoginResponsePacket.</summary>
        public enum LoginResponseResult : byte
        {
            Ok                 = 0,
            InvalidCredentials = 1,
            Banned             = 2,
            Error              = 255,
        }

        /// <summary>
        /// Encodes a LoginResponsePacket.
        /// <paramref name="newChallenge16"/> should be a fresh random nonce for the next attempt.
        /// </summary>
        public static byte[] EncodeLoginResponse(
            UiPanelInstanceId instanceId,
            LoginResponseResult result,
            ReadOnlySpan<byte> newChallenge16,
            string? detail)
        {
            byte[] detailUtf8 = string.IsNullOrEmpty(detail) ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(detail);
            int detailLen = Math.Min(detailUtf8.Length, 255);

            byte[] buf = new byte[LoginResponseFixedSize + detailLen];
            int pos = 0;
            buf[pos++] = LoginResponseMessageType;
            BinaryPrimitives.WriteUInt32LittleEndian(buf.AsSpan(pos, 4), instanceId.Value); pos += 4;
            buf[pos++] = (byte)result;
            newChallenge16.Slice(0, ChallengePlainBytes).CopyTo(buf.AsSpan(pos)); pos += ChallengePlainBytes;
            buf[pos++] = (byte)detailLen;
            if (detailLen > 0)
                detailUtf8.AsSpan(0, detailLen).CopyTo(buf.AsSpan(pos));
            return buf;
        }

        /// <summary>
        /// Decodes a LoginResponsePacket.
        /// <paramref name="newChallenge"/> is a slice of <paramref name="payload"/> — copy if needed.
        /// </summary>
        public static bool TryDecodeLoginResponse(
            ReadOnlySpan<byte> payload,
            out UiPanelInstanceId instanceId,
            out LoginResponseResult result,
            out ReadOnlySpan<byte> newChallenge,
            out string detail)
        {
            instanceId = default;
            result = default;
            newChallenge = default;
            detail = string.Empty;

            if (payload.Length < LoginResponseFixedSize || payload[0] != LoginResponseMessageType)
                return false;

            int pos = 1;
            instanceId = new UiPanelInstanceId(BinaryPrimitives.ReadUInt32LittleEndian(payload.Slice(pos, 4))); pos += 4;
            result = (LoginResponseResult)payload[pos++];
            newChallenge = payload.Slice(pos, ChallengePlainBytes); pos += ChallengePlainBytes;
            int detailLen = payload[pos++];
            if (payload.Length < pos + detailLen)
                return false;

            detail = detailLen > 0 ? Encoding.UTF8.GetString(payload.Slice(pos, detailLen)) : string.Empty;
            return true;
        }
    }
}
