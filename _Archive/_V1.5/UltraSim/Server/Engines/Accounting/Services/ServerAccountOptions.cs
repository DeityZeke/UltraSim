namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Server-owner configuration for the account and character system.
    /// Injected into ServerAccountSystem at construction time.
    /// </summary>
    public sealed record ServerAccountOptions(
        /// <summary>
        /// Maximum accounts allowed from a single remote IP address.
        /// -1 or 0 = unlimited. 1+ = hard cap.
        /// </summary>
        int AccountsPerIp = 1,

        /// <summary>
        /// Maximum characters per account. 1–16. Fits in nibble range 0x0–0xF.
        /// </summary>
        int MaxCharactersPerAccount = 5,

        /// <summary>
        /// If true AND MaxCharactersPerAccount == 1 AND the account already has exactly
        /// one character: skip the character select screen and enter the world directly.
        /// If the account has 0 characters, always go to character create regardless.
        /// </summary>
        bool AutoJoinSingleCharacter = false,

        /// <summary>
        /// If false, the delete option is hidden/rejected in character select.
        /// Combine with AutoJoinSingleCharacter + MaxCharactersPerAccount=1 for a
        /// "permanent single character" mode.
        /// </summary>
        bool AllowCharacterDeletion = true,

        /// <summary>
        /// Name of the registered storage backend to use.
        /// Must match a profile name in AccountStoreRegistry.
        /// Defaults to "EncryptedBinary" (AES-256-GCM).
        /// Custom backends: AccountStoreRegistry.Register(new AccountStoreProfile(...))
        /// </summary>
        string StorageProfile = AccountStoreRegistry.EncryptedBinary,

        /// <summary>
        /// How many rolling backup copies to keep alongside the live save file.
        ///
        ///  -1  Infinite — backups are never deleted. Each save shifts all existing
        ///      backups one slot higher (.1 → .2 → .3 …) indefinitely. Disk space
        ///      grows without bound; useful for audit trails or short test sessions.
        ///
        ///   0  Disabled — no backups are written. The live file is overwritten
        ///      atomically (.tmp rename). Suitable for ephemeral / dev environments
        ///      where save history is not needed.
        ///
        ///  1+  Fixed ring — keeps exactly N backup slots. On each save, slot N is
        ///      overwritten by slot N-1, slot N-1 by slot N-2, …, slot 1 by the
        ///      previous live file. The oldest backup beyond slot N is discarded.
        ///      Default is 2 (live + two backups = three total generations).
        /// </summary>
        int BackupCount = 2,

        /// <summary>
        /// Directory path (relative to server data root) where account files are written.
        /// </summary>
        string AccountDataPath = "accounts",

        /// <summary>
        /// If true, only IPs in <see cref="WhitelistedIps"/> are allowed to connect.
        /// All others receive a rejection message and are disconnected immediately.
        /// </summary>
        bool WhitelistMode = false,

        /// <summary>
        /// IPs allowed to connect when <see cref="WhitelistMode"/> is true.
        /// Ignored when WhitelistMode is false.
        /// </summary>
        string[]? WhitelistedIps = null
    )
    {
        public static readonly ServerAccountOptions Default = new ServerAccountOptions();

        /// <summary>Clamps MaxCharactersPerAccount to the valid 1–16 range.</summary>
        public int ClampedMaxCharacters => System.Math.Clamp(MaxCharactersPerAccount, 1, 16);
    }
}
