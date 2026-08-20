using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using System.Text.RegularExpressions;
using UltraSim.Discovery;
using UltraSim.Discovery.Official;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Pages.Servers;

public sealed class IndexModel : PageModel
{
    private readonly RegistryQueryClient _registryQueryClient;

    public IndexModel(RegistryQueryClient registryQueryClient)
    {
        _registryQueryClient = registryQueryClient;
    }

    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }

    [BindProperty(SupportsGet = true)]
    public string SortBy { get; set; } = "votes";

    [BindProperty(SupportsGet = true)]
    public bool Descending { get; set; } = true;

    public IReadOnlyList<ServerListEntry> Entries { get; private set; } = [];

    public string? ErrorMessage { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        try
        {
            IReadOnlyList<ServerDiscoveryRow> rows = await _registryQueryClient.GetServersAsync(
                new OfficialRegistryServerQuery
                {
                    Search = string.IsNullOrWhiteSpace(Search) ? null : Search,
                    Limit = 200
                },
                cancellationToken);

            Task<ServerDiscoveryDetail?>[] detailTasks = rows
                .Select(row => _registryQueryClient.GetServerAsync(row.ServerId, cancellationToken))
                .ToArray();

            ServerDiscoveryDetail?[] details = await Task.WhenAll(detailTasks);
            Entries = ApplySort(rows
                .Select((row, index) => new ServerListEntry(row, details[index]))
                .ToArray());
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Unable to load registry data: {ex.Message}";
        }
    }

    public bool IsActiveSort(string sortBy)
    {
        return string.Equals(SortBy, sortBy, StringComparison.OrdinalIgnoreCase);
    }

    public string GetSortArrow(string sortBy)
    {
        if (!IsActiveSort(sortBy))
            return string.Empty;

        return Descending ? "↓" : "↑";
    }

    public bool GetNextDescending(string sortBy)
    {
        if (!IsActiveSort(sortBy))
        {
            return !string.Equals(sortBy, "name", StringComparison.OrdinalIgnoreCase);
        }

        return !Descending;
    }

    public string GetPublicState(ServerListEntry entry)
    {
        ServerDiscoveryDetail? detail = entry.Detail;
        if (detail != null && detail.Row.LifecycleState == ServerListingLifecycleState.Delisted)
            return "Delisted";

        return entry.Row.HealthState switch
        {
            ServerListingHealthState.Online => "Online",
            ServerListingHealthState.Offline => "Offline",
            ServerListingHealthState.Stale => "Stale",
            _ => entry.Row.HealthState.ToString()
        };
    }

    public string GetPublicStateSummary(ServerListEntry entry)
    {
        string state = GetPublicState(entry);
        if (!string.IsNullOrWhiteSpace(entry.Detail?.Status))
            return $"{state} - {NormalizeStatusLabel(entry.Detail.Status)}";

        return state;
    }

    public string GetPublicStateDetail(ServerListEntry entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.Detail?.Status))
            return $"Server: {NormalizeStatusLabel(entry.Detail.Status)}";

        return GetPublicState(entry) == "Online"
            ? "Server: Reachable"
            : $"Last seen: {FormatTimestamp(entry.Row.LastSeenUtc)}";
    }

    public string GetPlayersSummary(ServerListEntry entry)
    {
        string current = entry.Row.CurrentUsers?.ToString() ?? "-";
        string average = entry.Detail?.HistorySummary != null
            ? entry.Detail.HistorySummary.AverageCurrentUsers.ToString("0.#")
            : "-";
        string max = entry.Detail?.AdvancedMetrics?.AllTimePeakPlayers.ToString()
            ?? entry.Detail?.HistorySummary?.MaxPeakUsers.ToString()
            ?? "-";

        return $"{current} / {average} / {max}";
    }

    public string GetRegistrationState(ServerListEntry entry)
    {
        if (entry.Detail?.VerifiedAtUtc.HasValue == true)
            return "Verified";

        if (entry.Detail?.OwnershipClaimRequestedUtc.HasValue == true)
        {
            return string.IsNullOrWhiteSpace(entry.Detail.OwnershipVerificationError)
                ? "Pending"
                : "Failed";
        }

        return entry.Row.TrustLevel is ServerListingTrustLevel.Verified or ServerListingTrustLevel.Official
            ? "Verified"
            : "Unverified";
    }

    public string FormatDuration(TimeSpan? value)
    {
        if (!value.HasValue)
            return "-";

        TimeSpan span = value.Value;
        if (span.TotalDays >= 1d)
            return $"{(int)span.TotalDays}d {span:hh\\:mm\\:ss}";

        return span.ToString(@"hh\:mm\:ss");
    }

    private IReadOnlyList<ServerListEntry> ApplySort(IReadOnlyList<ServerListEntry> entries)
    {
        string sortBy = string.IsNullOrWhiteSpace(SortBy) ? "votes" : SortBy.Trim().ToLowerInvariant();

        Func<ServerListEntry, object> keySelector = sortBy switch
        {
            "name" => entry => entry.Row.DisplayName,
            "state" => entry => GetStateSortWeight(entry),
            "uptime" => entry => entry.Row.Uptime ?? TimeSpan.Zero,
            "registration" => entry => GetRegistrationSortWeight(entry),
            _ => entry => entry.Row.VoteCount
        };

        IOrderedEnumerable<ServerListEntry> ordered = Descending
            ? entries.OrderByDescending(keySelector)
            : entries.OrderBy(keySelector);

        return ordered
            .ThenBy(entry => entry.Row.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private int GetStateSortWeight(ServerListEntry entry)
    {
        return GetPublicState(entry) switch
        {
            "Online" => 4,
            "Stale" => 3,
            "Offline" => 2,
            "Delisted" => 1,
            _ => 0
        };
    }

    private int GetRegistrationSortWeight(ServerListEntry entry)
    {
        return GetRegistrationState(entry) switch
        {
            "Verified" => 3,
            "Pending" => 2,
            "Failed" => 1,
            _ => 0
        };
    }

    private static string FormatTimestamp(DateTimeOffset? value)
    {
        return value?.ToString("u") ?? "-";
    }

    private static string NormalizeStatusLabel(string value)
    {
        string trimmed = value.Trim();
        if (trimmed.Length == 0)
            return "Unknown";

        if (!trimmed.Contains(' ') && !trimmed.Contains('-') && !trimmed.Contains('_'))
            return char.ToUpperInvariant(trimmed[0]) + trimmed[1..].ToLowerInvariant();

        string normalized = Regex.Replace(trimmed, @"[_\-]+", " ");
        string[] parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (int i = 0; i < parts.Length; i++)
        {
            string part = parts[i];
            parts[i] = char.ToUpperInvariant(part[0]) + part[1..].ToLowerInvariant();
        }

        return string.Join(' ', parts);
    }

    public sealed record ServerListEntry(
        ServerDiscoveryRow Row,
        ServerDiscoveryDetail? Detail)
    {
        public string? Description => Detail?.Description;
    }
}
