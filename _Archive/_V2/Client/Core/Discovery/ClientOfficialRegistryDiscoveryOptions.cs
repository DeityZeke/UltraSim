namespace UltraSim.Client;

public sealed record ClientOfficialRegistryDiscoveryOptions(
    string RegistryUrl = "http://127.0.0.1:5000",
    int Limit = 100,
    string SortBy = "activity",
    bool Descending = true);
