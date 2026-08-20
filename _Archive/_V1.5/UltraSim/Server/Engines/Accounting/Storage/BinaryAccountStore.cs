using System;
using System.Buffers;
using System.IO;
using System.Security.Cryptography;
using System.Threading.Tasks;
using UltraSim.Network;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// AES-256-GCM encrypted binary account store using BitWriter/BitReader.
    ///
    /// File layout (accounts.dat):
    ///   [4 bytes]  magic "ACCT"
    ///   [1 byte]   file format version
    ///   [12 bytes] AES-256-GCM nonce (random per save)
    ///   [4 bytes]  ciphertext length
    ///   [N bytes]  AES-256-GCM ciphertext of plaintext payload
    ///   [16 bytes] AES-256-GCM authentication tag
    ///
    /// Plaintext payload:
    ///   [4 bytes]  account record count
    ///   per account:
    ///     [1 byte]  schema version
    ///     [4 bytes] payload length
    ///     [N bytes] BitWriter-serialized AccountModel
    ///   [4 bytes]  character record count
    ///   per character:
    ///     [1 byte]  schema version
    ///     [4 bytes] payload length
    ///     [N bytes] BitWriter-serialized CharacterModel
    ///   [4 bytes]  IP ban record count
    ///   per IP ban:
    ///     [1 byte]  schema version
    ///     [4 bytes] payload length
    ///     [N bytes] BitWriter-serialized IpBanEntry
    ///
    /// Backup rotation:
    ///   accounts.dat        — current (newest)
    ///   accounts.dat.1      — one save ago
    ///   accounts.dat.2      — two saves ago  (oldest kept)
    ///
    /// server.key: 32 random bytes, generated on first boot, stored separately.
    /// Losing server.key makes accounts.dat unrecoverable by design.
    /// </summary>
    public sealed class BinaryAccountStore : IAccountStore
    {
        private const int FileFormatVersion = 1;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;
        private static readonly byte[] Magic = { (byte)'A', (byte)'C', (byte)'C', (byte)'T' };

        private const int AccountRecordEstimate = 256;
        private const int CharacterRecordEstimate = 128;
        private const int IpBanRecordEstimate = 128;

        private readonly string _dataDirectory;
        private readonly byte[] _key;
        private readonly int _backupCount;

        private string AccountsFilePath => Path.Combine(_dataDirectory, "accounts.dat");
        private string KeyFilePath => Path.Combine(_dataDirectory, "server.key");

        /// <param name="backupCount">
        /// Number of rolling backup slots. -1=infinite, 0=disabled, N=keep N slots.
        /// See ServerAccountOptions.BackupCount for full documentation.
        /// </param>
        public BinaryAccountStore(string dataDirectory, int backupCount = 2)
        {
            _dataDirectory = dataDirectory;
            _backupCount = backupCount;
            Directory.CreateDirectory(_dataDirectory);
            _key = LoadOrCreateKey();
        }

        // -------------------------------------------------------------------------
        // IAccountStore
        // -------------------------------------------------------------------------

        public CharacterModel[] LoadCharacters()
        {
            if (!File.Exists(AccountsFilePath))
                return Array.Empty<CharacterModel>();

            (_, CharacterModel[] characters, _) = ReadFile();
            return characters;
        }

        public AccountModel[] Load(CharacterModel[] characters)
        {
            if (!File.Exists(AccountsFilePath))
                return Array.Empty<AccountModel>();

            (AccountModel[] accounts, _, _) = ReadFile();
            return accounts;
        }

        public IpBanEntry[] LoadIpBans()
        {
            if (!File.Exists(AccountsFilePath))
                return Array.Empty<IpBanEntry>();

            (_, _, IpBanEntry[] bans) = ReadFile();
            return bans;
        }

        public Task WriteAsync(AccountModel[] accounts, CharacterModel[] characters, IpBanEntry[] ipBans)
        {
            AccountModel[] snap = accounts;
            CharacterModel[] charSnap = characters;
            IpBanEntry[] banSnap = ipBans;
            string path = AccountsFilePath;
            byte[] key = _key;
            int backupCount = _backupCount;
            return Task.Run(() => WriteFile(path, key, snap, charSnap, banSnap, backupCount));
        }

        // -------------------------------------------------------------------------
        // Write
        // -------------------------------------------------------------------------

        private static void WriteFile(
            string path,
            byte[] key,
            AccountModel[] accounts,
            CharacterModel[] characters,
            IpBanEntry[] ipBans,
            int backupCount)
        {
            int estimatedSize = 12
                + accounts.Length * AccountRecordEstimate
                + characters.Length * CharacterRecordEstimate
                + ipBans.Length * IpBanRecordEstimate;

            byte[] plainBuffer = ArrayPool<byte>.Shared.Rent(Math.Max(estimatedSize, 1024));
            int plainLength;
            try
            {
                plainLength = SerializePlaintext(plainBuffer, accounts, characters, ipBans);
            }
            catch (Exception)
            {
                ArrayPool<byte>.Shared.Return(plainBuffer);
                throw;
            }

            byte[] ciphertext = new byte[plainLength];
            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] tag = new byte[TagSize];

            try
            {
                using var aes = new AesGcm(key, TagSize);
                aes.Encrypt(nonce, plainBuffer.AsSpan(0, plainLength), ciphertext, tag);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(plainBuffer);
            }

            // Write to .tmp first, then rotate backups, then move into place.
            string tmpPath = path + ".tmp";
            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var bw = new BinaryWriter(fs))
            {
                bw.Write(Magic);
                bw.Write((byte)FileFormatVersion);
                bw.Write(nonce);
                bw.Write(ciphertext.Length);
                bw.Write(ciphertext);
                bw.Write(tag);
            }

            RotateBackups(path, backupCount);
            File.Move(tmpPath, path, overwrite: true);
        }

        /// <summary>
        /// Rotates backup slots before a new save lands.
        ///
        ///  count == 0  No-op. The live file will be overwritten directly.
        ///  count  > 0  Fixed ring. Slot N is dropped; each slot shifts up by one.
        ///              With count=2: .1 → .2 (drop old .2), live → .1
        ///  count == -1 Infinite. Find the current highest slot N, shift everything
        ///              up by one (.N → .N+1, … live → .1). Nothing is ever deleted.
        /// </summary>
        private static void RotateBackups(string path, int count)
        {
            if (count == 0)
                return;

            if (count > 0)
            {
                // Walk from oldest to newest to avoid clobbering a slot we still need.
                // Slot beyond count is overwritten (discarded) by the move.
                for (int i = count; i >= 1; i--)
                {
                    string src = i == 1 ? path : $"{path}.{i - 1}";
                    string dst = $"{path}.{i}";
                    if (File.Exists(src))
                        File.Move(src, dst, overwrite: true);
                }
            }
            else // count == -1, infinite
            {
                // Find the highest existing backup slot (contiguous scan).
                int highest = 0;
                while (File.Exists($"{path}.{highest + 1}"))
                    highest++;

                // Shift everything up by one, oldest first.
                for (int i = highest; i >= 1; i--)
                    File.Move($"{path}.{i}", $"{path}.{i + 1}", overwrite: false);

                if (File.Exists(path))
                    File.Move(path, $"{path}.1", overwrite: false);
            }
        }

        // -------------------------------------------------------------------------
        // Plaintext serialization
        // -------------------------------------------------------------------------

        private static int SerializePlaintext(
            byte[] buffer,
            AccountModel[] accounts,
            CharacterModel[] characters,
            IpBanEntry[] ipBans)
        {
            while (true)
            {
                var writer = new BitWriter(buffer);
                bool ok = TrySerializePlaintext(ref writer, accounts, characters, ipBans);
                if (ok)
                    return writer.Position;

                int newSize = buffer.Length * 2;
                ArrayPool<byte>.Shared.Return(buffer);
                buffer = ArrayPool<byte>.Shared.Rent(newSize);
            }
        }

        private static bool TrySerializePlaintext(
            ref BitWriter writer,
            AccountModel[] accounts,
            CharacterModel[] characters,
            IpBanEntry[] ipBans)
        {
            try
            {
                writer.WriteInt32(accounts.Length);
                foreach (var acc in accounts)
                {
                    byte[] recBuf = ArrayPool<byte>.Shared.Rent(AccountRecordEstimate * 2);
                    try
                    {
                        var recWriter = new BitWriter(recBuf);
                        WriteAccount(ref recWriter, acc);
                        int recLen = recWriter.Position;
                        writer.WriteByte(AccountModel.SchemaVersion);
                        writer.WriteInt32(recLen);
                        writer.WriteBytes(recBuf.AsSpan(0, recLen));
                    }
                    finally { ArrayPool<byte>.Shared.Return(recBuf); }
                }

                writer.WriteInt32(characters.Length);
                foreach (var ch in characters)
                {
                    byte[] recBuf = ArrayPool<byte>.Shared.Rent(CharacterRecordEstimate * 2);
                    try
                    {
                        var recWriter = new BitWriter(recBuf);
                        WriteCharacter(ref recWriter, ch);
                        int recLen = recWriter.Position;
                        writer.WriteByte(CharacterModel.SchemaVersion);
                        writer.WriteInt32(recLen);
                        writer.WriteBytes(recBuf.AsSpan(0, recLen));
                    }
                    finally { ArrayPool<byte>.Shared.Return(recBuf); }
                }

                writer.WriteInt32(ipBans.Length);
                foreach (var ban in ipBans)
                {
                    byte[] recBuf = ArrayPool<byte>.Shared.Rent(IpBanRecordEstimate * 2);
                    try
                    {
                        var recWriter = new BitWriter(recBuf);
                        WriteIpBan(ref recWriter, ban);
                        int recLen = recWriter.Position;
                        writer.WriteByte(IpBanEntry.SchemaVersion);
                        writer.WriteInt32(recLen);
                        writer.WriteBytes(recBuf.AsSpan(0, recLen));
                    }
                    finally { ArrayPool<byte>.Shared.Return(recBuf); }
                }

                return true;
            }
            catch (IndexOutOfRangeException)
            {
                return false;
            }
        }

        private static void WriteAccount(ref BitWriter w, AccountModel a)
        {
            w.WriteUInt64(a.AccountId);
            w.WriteString(a.Username);
            w.WriteString(a.PasswordHash);
            w.WriteString(a.PasswordSalt);

            w.WriteInt32(a.CharacterSlots.Length);
            foreach (var slot in a.CharacterSlots)
                w.WriteUInt64(slot);

            w.WriteInt64(a.CreatedUtc.ToUnixTimeMilliseconds());
            w.WriteInt64(a.LastLoginUtc.ToUnixTimeMilliseconds());

            w.WriteByte((byte)Math.Min(a.KnownIps.Length, 255));
            foreach (var ip in a.KnownIps)
                w.WriteString(ip);

            w.WriteBool(a.IsAdmin);
            w.WriteBool(a.AllowSharedIp);
            w.WriteBool(a.SharedIpReviewPending);
            w.WriteBool(a.IsBanned);
            w.WriteString(a.BanReason ?? string.Empty);
        }

        private static void WriteCharacter(ref BitWriter w, CharacterModel c)
        {
            w.WriteUInt64(c.CharacterId);
            w.WriteUInt64(c.AccountId);
            w.WriteByte(c.SlotIndex);
            w.WriteString(c.Name);
            w.WriteUInt16(c.ArchetypeId);
            w.WriteInt64(c.CreatedUtc.ToUnixTimeMilliseconds());
            w.WriteInt64(c.LastPlayedUtc.ToUnixTimeMilliseconds());
            w.WriteSingle(c.X);
            w.WriteSingle(c.Y);
            w.WriteSingle(c.Z);
            w.WriteSingle(c.YawRadians);
        }

        private static void WriteIpBan(ref BitWriter w, IpBanEntry b)
        {
            w.WriteString(b.Ip);
            w.WriteString(b.Reason);
            w.WriteInt64(b.BannedUtc.ToUnixTimeMilliseconds());
            w.WriteBool(b.ExpiryUtc.HasValue);
            if (b.ExpiryUtc.HasValue)
                w.WriteInt64(b.ExpiryUtc.Value.ToUnixTimeMilliseconds());
        }

        // -------------------------------------------------------------------------
        // Read
        // -------------------------------------------------------------------------

        private (AccountModel[] accounts, CharacterModel[] characters, IpBanEntry[] ipBans) ReadFile()
        {
            byte[] fileBytes = File.ReadAllBytes(AccountsFilePath);

            if (fileBytes.Length < Magic.Length + 1)
                throw new InvalidDataException("accounts.dat is too short to contain a valid header.");

            for (int i = 0; i < Magic.Length; i++)
                if (fileBytes[i] != Magic[i])
                    throw new InvalidDataException("accounts.dat magic mismatch.");

            byte fileVersion = fileBytes[4];
            if (fileVersion != FileFormatVersion)
                throw new NotSupportedException($"Unsupported accounts.dat format version {fileVersion}.");

            int offset = 5;
            byte[] nonce = fileBytes.AsSpan(offset, NonceSize).ToArray();
            offset += NonceSize;

            int ciphertextLen = BitConverter.ToInt32(fileBytes, offset);
            offset += 4;

            byte[] ciphertext = fileBytes.AsSpan(offset, ciphertextLen).ToArray();
            offset += ciphertextLen;

            byte[] tag = fileBytes.AsSpan(offset, TagSize).ToArray();

            byte[] plaintext = new byte[ciphertextLen];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(nonce, ciphertext, tag, plaintext);

            return DeserializePlaintext(plaintext);
        }

        private static (AccountModel[] accounts, CharacterModel[] characters, IpBanEntry[] ipBans)
            DeserializePlaintext(byte[] plaintext)
        {
            var reader = new BitReader(plaintext);

            int accountCount = reader.ReadInt32();
            var accounts = new AccountModel[accountCount];
            for (int i = 0; i < accountCount; i++)
            {
                byte schemaVersion = reader.ReadByte();
                int recLen = reader.ReadInt32();
                if (schemaVersion > AccountModel.SchemaVersion)
                {
                    reader.ReadBytes(recLen);
                    accounts[i] = null!;
                    continue;
                }
                var recReader = new BitReader(reader.ReadBytes(recLen));
                accounts[i] = ReadAccount(ref recReader, schemaVersion);
            }

            int characterCount = reader.ReadInt32();
            var characters = new CharacterModel[characterCount];
            for (int i = 0; i < characterCount; i++)
            {
                byte schemaVersion = reader.ReadByte();
                int recLen = reader.ReadInt32();
                if (schemaVersion > CharacterModel.SchemaVersion)
                {
                    reader.ReadBytes(recLen);
                    characters[i] = null!;
                    continue;
                }
                var recReader = new BitReader(reader.ReadBytes(recLen));
                characters[i] = ReadCharacter(ref recReader, schemaVersion);
            }

            // IP bans are optional — older files won't have this section.
            IpBanEntry[] ipBans = Array.Empty<IpBanEntry>();
            if (reader.Remaining >= 4)
            {
                int banCount = reader.ReadInt32();
                ipBans = new IpBanEntry[banCount];
                for (int i = 0; i < banCount; i++)
                {
                    byte schemaVersion = reader.ReadByte();
                    int recLen = reader.ReadInt32();
                    if (schemaVersion > IpBanEntry.SchemaVersion)
                    {
                        reader.ReadBytes(recLen);
                        ipBans[i] = null!;
                        continue;
                    }
                    var recReader = new BitReader(reader.ReadBytes(recLen));
                    ipBans[i] = ReadIpBan(ref recReader);
                }
                ipBans = Array.FindAll(ipBans, b => b != null);
            }

            accounts = Array.FindAll(accounts, a => a != null);
            characters = Array.FindAll(characters, c => c != null);
            return (accounts, characters, ipBans);
        }

        private static AccountModel ReadAccount(ref BitReader r, byte schemaVersion)
        {
            ulong accountId = r.ReadUInt64();
            string username = r.ReadString();
            string passwordHash = r.ReadString();
            string passwordSalt = r.ReadString();

            int slotCount = r.ReadInt32();
            var slots = new ulong[slotCount];
            for (int i = 0; i < slotCount; i++)
                slots[i] = r.ReadUInt64();

            var createdUtc = DateTimeOffset.FromUnixTimeMilliseconds(r.ReadInt64());
            var lastLoginUtc = DateTimeOffset.FromUnixTimeMilliseconds(r.ReadInt64());

            int ipCount = r.ReadByte();
            var ips = new string[ipCount];
            for (int i = 0; i < ipCount; i++)
                ips[i] = r.ReadString();

            bool isAdmin = r.ReadBool();
            bool allowSharedIp = false;
            bool sharedIpReviewPending = false;
            if (schemaVersion >= 2)
            {
                allowSharedIp = r.ReadBool();
                sharedIpReviewPending = r.ReadBool();
            }
            bool isBanned = r.ReadBool();
            string banReason = r.ReadString();

            return new AccountModel(
                accountId, username, passwordHash, passwordSalt,
                slots, createdUtc, lastLoginUtc, ips,
                isAdmin, allowSharedIp, sharedIpReviewPending, isBanned, string.IsNullOrEmpty(banReason) ? null : banReason);
        }

        private static CharacterModel ReadCharacter(ref BitReader r, byte schemaVersion)
        {
            ulong characterId = r.ReadUInt64();
            ulong accountId = r.ReadUInt64();
            byte slotIndex = r.ReadByte();
            string name = r.ReadString();
            ushort archetypeId = r.ReadUInt16();
            var createdUtc = DateTimeOffset.FromUnixTimeMilliseconds(r.ReadInt64());
            var lastPlayedUtc = DateTimeOffset.FromUnixTimeMilliseconds(r.ReadInt64());
            float x = 0f;
            float y = 0f;
            float z = 0f;
            float yawRadians = 0f;
            if (schemaVersion >= 2)
            {
                x = r.ReadSingle();
                y = r.ReadSingle();
                z = r.ReadSingle();
                yawRadians = r.ReadSingle();
            }

            return new CharacterModel(characterId, accountId, slotIndex, name, archetypeId, createdUtc, lastPlayedUtc, x, y, z, yawRadians);
        }

        private static IpBanEntry ReadIpBan(ref BitReader r)
        {
            string ip = r.ReadString();
            string reason = r.ReadString();
            var bannedUtc = DateTimeOffset.FromUnixTimeMilliseconds(r.ReadInt64());
            bool hasExpiry = r.ReadBool();
            DateTimeOffset? expiryUtc = hasExpiry
                ? DateTimeOffset.FromUnixTimeMilliseconds(r.ReadInt64())
                : null;
            return new IpBanEntry(ip, reason, bannedUtc, expiryUtc);
        }

        // -------------------------------------------------------------------------
        // Key management
        // -------------------------------------------------------------------------

        private byte[] LoadOrCreateKey()
        {
            if (File.Exists(KeyFilePath))
            {
                byte[] existing = File.ReadAllBytes(KeyFilePath);
                if (existing.Length != KeySize)
                    throw new InvalidOperationException(
                        $"server.key has invalid length {existing.Length}; expected {KeySize} bytes. " +
                        "Do not modify server.key manually.");
                return existing;
            }

            byte[] newKey = RandomNumberGenerator.GetBytes(KeySize);
            File.WriteAllBytes(KeyFilePath, newKey);
            return newKey;
        }
    }
}
