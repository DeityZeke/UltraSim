using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Plaintext JSON account store. Lower security posture than BinaryAccountStore:
    /// usernames are visible, no encryption. Passwords remain SHA-256 hashed.
    /// Use only when readability/debugging is more important than confidentiality.
    ///
    /// Files:
    ///   accounts.json        — current accounts
    ///   accounts.json.1/.2   — rolling backups
    ///   characters.json      — current characters
    ///   characters.json.1/.2 — rolling backups
    ///   ip_bans.json         — current IP ban list
    ///   ip_bans.json.1/.2    — rolling backups
    /// </summary>
    public sealed class JsonAccountStore : IAccountStore
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };

        private readonly string _dataDirectory;
        private readonly int _backupCount;

        private string AccountsFilePath => Path.Combine(_dataDirectory, "accounts.json");
        private string CharactersFilePath => Path.Combine(_dataDirectory, "characters.json");
        private string IpBansFilePath => Path.Combine(_dataDirectory, "ip_bans.json");

        /// <param name="backupCount">
        /// Number of rolling backup slots. -1=infinite, 0=disabled, N=keep N slots.
        /// See ServerAccountOptions.BackupCount for full documentation.
        /// </param>
        public JsonAccountStore(string dataDirectory, int backupCount = 2)
        {
            _dataDirectory = dataDirectory;
            _backupCount = backupCount;
            Directory.CreateDirectory(_dataDirectory);
        }

        public CharacterModel[] LoadCharacters()
        {
            if (!File.Exists(CharactersFilePath))
                return Array.Empty<CharacterModel>();

            string json = File.ReadAllText(CharactersFilePath);
            return JsonSerializer.Deserialize<CharacterJsonRecord[]>(json, JsonOptions) is { } records
                ? Array.ConvertAll(records, r => r.ToModel())
                : Array.Empty<CharacterModel>();
        }

        public AccountModel[] Load(CharacterModel[] characters)
        {
            if (!File.Exists(AccountsFilePath))
                return Array.Empty<AccountModel>();

            string json = File.ReadAllText(AccountsFilePath);
            return JsonSerializer.Deserialize<AccountJsonRecord[]>(json, JsonOptions) is { } records
                ? Array.ConvertAll(records, r => r.ToModel())
                : Array.Empty<AccountModel>();
        }

        public IpBanEntry[] LoadIpBans()
        {
            if (!File.Exists(IpBansFilePath))
                return Array.Empty<IpBanEntry>();

            string json = File.ReadAllText(IpBansFilePath);
            return JsonSerializer.Deserialize<IpBanJsonRecord[]>(json, JsonOptions) is { } records
                ? Array.ConvertAll(records, r => r.ToModel())
                : Array.Empty<IpBanEntry>();
        }

        public Task WriteAsync(AccountModel[] accounts, CharacterModel[] characters, IpBanEntry[] ipBans)
        {
            AccountModel[] accSnap = accounts;
            CharacterModel[] charSnap = characters;
            IpBanEntry[] banSnap = ipBans;
            int backupCount = _backupCount;
            return Task.Run(() =>
            {
                WriteAtomic(AccountsFilePath,
                    JsonSerializer.Serialize(Array.ConvertAll(accSnap, AccountJsonRecord.FromModel), JsonOptions),
                    backupCount);
                WriteAtomic(CharactersFilePath,
                    JsonSerializer.Serialize(Array.ConvertAll(charSnap, CharacterJsonRecord.FromModel), JsonOptions),
                    backupCount);
                WriteAtomic(IpBansFilePath,
                    JsonSerializer.Serialize(Array.ConvertAll(banSnap, IpBanJsonRecord.FromModel), JsonOptions),
                    backupCount);
            });
        }

        // -------------------------------------------------------------------------
        // Helpers
        // -------------------------------------------------------------------------

        /// <summary>Writes content to path.tmp, rotates backups, then moves into place.</summary>
        private static void WriteAtomic(string path, string content, int backupCount)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            RotateBackups(path, backupCount);
            File.Move(tmp, path, overwrite: true);
        }

        /// <summary>
        /// Rotates backup slots before a new save lands.
        ///
        ///  count == 0  No-op. The live file will be overwritten directly.
        ///  count  > 0  Fixed ring. Slot N is dropped; each slot shifts up by one.
        ///  count == -1 Infinite. Finds the current highest slot and shifts everything
        ///              up by one. Nothing is ever deleted.
        /// </summary>
        private static void RotateBackups(string path, int count)
        {
            if (count == 0)
                return;

            if (count > 0)
            {
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
                int highest = 0;
                while (File.Exists($"{path}.{highest + 1}"))
                    highest++;

                for (int i = highest; i >= 1; i--)
                    File.Move($"{path}.{i}", $"{path}.{i + 1}", overwrite: false);

                if (File.Exists(path))
                    File.Move(path, $"{path}.1", overwrite: false);
            }
        }

        // -------------------------------------------------------------------------
        // JSON DTOs
        // -------------------------------------------------------------------------

        private sealed class AccountJsonRecord
        {
            public ulong AccountId { get; set; }
            public string Username { get; set; } = string.Empty;
            public string PasswordHash { get; set; } = string.Empty;
            public string PasswordSalt { get; set; } = string.Empty;
            public ulong[] CharacterSlots { get; set; } = Array.Empty<ulong>();
            public long CreatedUtcMs { get; set; }
            public long LastLoginUtcMs { get; set; }
            public string[] KnownIps { get; set; } = Array.Empty<string>();
            public bool IsAdmin { get; set; }
            public bool AllowSharedIp { get; set; }
            public bool SharedIpReviewPending { get; set; }
            public bool IsBanned { get; set; }
            public string? BanReason { get; set; }

            public AccountModel ToModel() => new AccountModel(
                AccountId, Username, PasswordHash, PasswordSalt,
                CharacterSlots,
                DateTimeOffset.FromUnixTimeMilliseconds(CreatedUtcMs),
                DateTimeOffset.FromUnixTimeMilliseconds(LastLoginUtcMs),
                KnownIps, IsAdmin, AllowSharedIp, SharedIpReviewPending, IsBanned, BanReason);

            public static AccountJsonRecord FromModel(AccountModel m) => new AccountJsonRecord
            {
                AccountId = m.AccountId,
                Username = m.Username,
                PasswordHash = m.PasswordHash,
                PasswordSalt = m.PasswordSalt,
                CharacterSlots = m.CharacterSlots,
                CreatedUtcMs = m.CreatedUtc.ToUnixTimeMilliseconds(),
                LastLoginUtcMs = m.LastLoginUtc.ToUnixTimeMilliseconds(),
                KnownIps = m.KnownIps,
                IsAdmin = m.IsAdmin,
                AllowSharedIp = m.AllowSharedIp,
                SharedIpReviewPending = m.SharedIpReviewPending,
                IsBanned = m.IsBanned,
                BanReason = m.BanReason,
            };
        }

        private sealed class CharacterJsonRecord
        {
            public ulong CharacterId { get; set; }
            public ulong AccountId { get; set; }
            public byte SlotIndex { get; set; }
            public string Name { get; set; } = string.Empty;
            public ushort ArchetypeId { get; set; }
            public long CreatedUtcMs { get; set; }
            public long LastPlayedUtcMs { get; set; }
            public float X { get; set; }
            public float Y { get; set; }
            public float Z { get; set; }
            public float YawRadians { get; set; }

            public CharacterModel ToModel() => new CharacterModel(
                CharacterId, AccountId, SlotIndex, Name, ArchetypeId,
                DateTimeOffset.FromUnixTimeMilliseconds(CreatedUtcMs),
                DateTimeOffset.FromUnixTimeMilliseconds(LastPlayedUtcMs),
                X,
                Y,
                Z,
                YawRadians);

            public static CharacterJsonRecord FromModel(CharacterModel c) => new CharacterJsonRecord
            {
                CharacterId = c.CharacterId,
                AccountId = c.AccountId,
                SlotIndex = c.SlotIndex,
                Name = c.Name,
                ArchetypeId = c.ArchetypeId,
                CreatedUtcMs = c.CreatedUtc.ToUnixTimeMilliseconds(),
                LastPlayedUtcMs = c.LastPlayedUtc.ToUnixTimeMilliseconds(),
                X = c.X,
                Y = c.Y,
                Z = c.Z,
                YawRadians = c.YawRadians,
            };
        }

        private sealed class IpBanJsonRecord
        {
            public string Ip { get; set; } = string.Empty;
            public string Reason { get; set; } = string.Empty;
            public long BannedUtcMs { get; set; }
            public long? ExpiryUtcMs { get; set; }

            public IpBanEntry ToModel() => new IpBanEntry(
                Ip, Reason,
                DateTimeOffset.FromUnixTimeMilliseconds(BannedUtcMs),
                ExpiryUtcMs.HasValue
                    ? DateTimeOffset.FromUnixTimeMilliseconds(ExpiryUtcMs.Value)
                    : null);

            public static IpBanJsonRecord FromModel(IpBanEntry b) => new IpBanJsonRecord
            {
                Ip = b.Ip,
                Reason = b.Reason,
                BannedUtcMs = b.BannedUtc.ToUnixTimeMilliseconds(),
                ExpiryUtcMs = b.ExpiryUtc?.ToUnixTimeMilliseconds(),
            };
        }
    }
}
