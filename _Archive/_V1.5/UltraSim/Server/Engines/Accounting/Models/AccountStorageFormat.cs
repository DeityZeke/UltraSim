namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Named aliases for the built-in storage profile names.
    /// Prefer these constants over raw strings when referencing built-in backends.
    /// Custom backends registered via AccountStoreRegistry use their own names.
    /// </summary>
    public static class AccountStorageFormat
    {
        /// <summary>AES-256-GCM encrypted binary. Recommended default.</summary>
        public const string EncryptedBinary = AccountStoreRegistry.EncryptedBinary;

        /// <summary>Plaintext JSON. Debug/inspection use only.</summary>
        public const string Json = AccountStoreRegistry.Json;
    }
}
