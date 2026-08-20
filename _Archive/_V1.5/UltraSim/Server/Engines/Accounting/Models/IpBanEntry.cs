using System;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// A single IP ban record. IPs are matched as exact strings (IPv4 or IPv6).
    /// ExpiryUtc == null means permanent.
    /// </summary>
    public sealed record IpBanEntry(
        string Ip,
        string Reason,
        DateTimeOffset BannedUtc,
        DateTimeOffset? ExpiryUtc
    )
    {
        public const byte SchemaVersion = 1;

        /// <summary>True if this ban has passed its expiry time.</summary>
        public bool IsExpired => ExpiryUtc.HasValue && DateTimeOffset.UtcNow > ExpiryUtc.Value;
    }
}
