namespace UltraSim.Discovery.Official
{
    public sealed class OfficialRegistryServerQuery
    {
        public string? Search { get; init; }
        public string? Region { get; init; }
        public string? Language { get; init; }
        public ServerListingTrustLevel? TrustLevel { get; init; }
        public bool? PasswordRequired { get; init; }
        public bool? WhitelistOnly { get; init; }
        public string? CompatibilityId { get; init; }
        public string? SortBy { get; init; }
        public bool Descending { get; init; }
        public int Offset { get; init; }
        public int Limit { get; init; } = 100;
    }
}
