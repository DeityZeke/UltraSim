using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using UltraSim.Discovery;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Pages.Servers;

public sealed class DetailModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RegistryQueryClient _registryQueryClient;
    private readonly VoteProjectionService _voteProjectionService;

    public DetailModel(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        RegistryQueryClient registryQueryClient,
        VoteProjectionService voteProjectionService)
    {
        _db = db;
        _userManager = userManager;
        _registryQueryClient = registryQueryClient;
        _voteProjectionService = voteProjectionService;
    }

    [BindProperty(SupportsGet = true, Name = "serverId")]
    public string ServerId { get; set; } = string.Empty;

    public ServerDiscoveryDetail? Detail { get; private set; }
    public bool HasCurrentUserVoted { get; private set; }

    public string? ErrorMessage { get; private set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [TempData]
    public string? StatusKind { get; set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        await LoadAsync(cancellationToken);
    }

    public async Task<IActionResult> OnPostVoteAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ServerId))
            return RedirectToPage("/Servers/Index");

        ApplicationUser? user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Challenge();

        ServerDiscoveryDetail? detail;
        try
        {
            detail = await _registryQueryClient.GetServerAsync(ServerId, cancellationToken);
        }
        catch (HttpRequestException)
        {
            StatusKind = "warning";
            StatusMessage = "Unable to reach the registry right now. Try again later.";
            return RedirectToPage(new { serverId = ServerId });
        }

        if (detail == null)
            return NotFound();

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;
        ServerVote? existingVote = await _db.ServerVotes
            .FirstOrDefaultAsync(x => x.UserId == user.Id && x.ServerId == ServerId, cancellationToken);

        if (existingVote == null)
        {
            _db.ServerVotes.Add(new ServerVote
            {
                UserId = user.Id,
                ServerId = ServerId,
                VotedUtc = nowUtc,
                LastChangedUtc = nowUtc
            });
        }
        else
        {
            existingVote.LastChangedUtc = nowUtc;
        }

        await _db.SaveChangesAsync(cancellationToken);

        try
        {
            await _voteProjectionService.PushServerVoteCountAsync(ServerId, cancellationToken);
            StatusKind = "success";
            StatusMessage = $"Vote recorded for {detail.Row.DisplayName}.";
        }
        catch (HttpRequestException)
        {
            StatusKind = "warning";
            StatusMessage = $"Vote saved for {detail.Row.DisplayName}, but the registry vote total could not be updated yet.";
        }
        catch
        {
            StatusKind = "warning";
            StatusMessage = $"Vote saved for {detail.Row.DisplayName}, but the registry vote total could not be updated yet.";
        }

        return RedirectToPage(new { serverId = ServerId });
    }

    public async Task<IActionResult> OnPostWithdrawVoteAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ServerId))
            return RedirectToPage("/Servers/Index");

        ApplicationUser? user = await _userManager.GetUserAsync(User);
        if (user == null)
            return Challenge();

        ServerVote? existingVote = await _db.ServerVotes
            .FirstOrDefaultAsync(x => x.UserId == user.Id && x.ServerId == ServerId, cancellationToken);

        if (existingVote != null)
        {
            _db.ServerVotes.Remove(existingVote);
            await _db.SaveChangesAsync(cancellationToken);
        }

        try
        {
            await _voteProjectionService.PushServerVoteCountAsync(ServerId, cancellationToken);
            StatusKind = "success";
            StatusMessage = "Vote removed.";
        }
        catch (HttpRequestException)
        {
            StatusKind = "warning";
            StatusMessage = "Vote removal saved locally, but the registry vote total could not be updated yet.";
        }
        catch
        {
            StatusKind = "warning";
            StatusMessage = "Vote removal saved locally, but the registry vote total could not be updated yet.";
        }

        return RedirectToPage(new { serverId = ServerId });
    }

    private async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ServerId))
        {
            ErrorMessage = "Missing server id.";
            return;
        }

        try
        {
            Detail = await _registryQueryClient.GetServerAsync(ServerId, cancellationToken);

            if (Detail != null && User.Identity?.IsAuthenticated == true)
            {
                ApplicationUser? user = await _userManager.GetUserAsync(User);
                if (user != null)
                {
                    HasCurrentUserVoted = await _db.ServerVotes
                        .AsNoTracking()
                        .AnyAsync(x => x.UserId == user.Id && x.ServerId == ServerId, cancellationToken);
                }
            }
        }
        catch (HttpRequestException ex)
        {
            ErrorMessage = $"Unable to load server detail: {ex.Message}";
        }
    }

    public static string FormatTimestamp(DateTimeOffset? value)
    {
        return value?.ToString("u") ?? "-";
    }

    public static string FormatDuration(TimeSpan? value)
    {
        if (!value.HasValue)
            return "-";

        TimeSpan span = value.Value;
        if (span.TotalDays >= 1d)
            return $"{(int)span.TotalDays}d {span:hh\\:mm\\:ss}";

        return span.ToString(@"hh\:mm\:ss");
    }

    public static string FormatPercent(double value)
    {
        return $"{value * 100d:0.#}%";
    }

    public static string FormatNumber(double value)
    {
        return value.ToString("0.##");
    }

    public static string GetRegistrationStatus(ServerDiscoveryDetail detail)
    {
        string lifecycleLabel = detail.Row.LifecycleState switch
        {
            ServerListingLifecycleState.Delisted => "Delisted",
            ServerListingLifecycleState.Disabled => "Disabled",
            _ => "Active"
        };

        if (detail.VerifiedAtUtc.HasValue)
            return $"Registered - {lifecycleLabel}";

        if (detail.OwnershipClaimRequestedUtc.HasValue)
        {
            return string.IsNullOrWhiteSpace(detail.OwnershipVerificationError)
                ? "Pending Registration"
                : "Registration Failed";
        }

        return detail.Row.TrustLevel switch
        {
            ServerListingTrustLevel.Official => $"Registered - {lifecycleLabel}",
            ServerListingTrustLevel.Verified => $"Registered - {lifecycleLabel}",
            _ => "Unregistered"
        };
    }

    public static string? GetRegistrationDetail(ServerDiscoveryDetail detail)
    {
        if (detail.VerifiedAtUtc.HasValue)
            return detail.VerifiedAtUtc.Value.ToString("u");

        if (detail.OwnershipClaimRequestedUtc.HasValue)
        {
            return string.IsNullOrWhiteSpace(detail.OwnershipVerificationError)
                ? $"Requested {detail.OwnershipClaimRequestedUtc.Value:u}"
                : detail.OwnershipVerificationError;
        }

        return detail.Row.TrustLevel switch
        {
            ServerListingTrustLevel.Official => "Official listing",
            ServerListingTrustLevel.Verified => "Registry-trusted listing",
            _ => null
        };
    }

    public static string GetPublicState(ServerDiscoveryDetail detail)
    {
        if (detail.Row.LifecycleState == ServerListingLifecycleState.Delisted)
            return "Delisted";

        return detail.Row.HealthState switch
        {
            ServerListingHealthState.Online => "Online",
            ServerListingHealthState.Offline => "Offline",
            ServerListingHealthState.Stale => "Stale",
            _ => detail.Row.HealthState.ToString()
        };
    }

    public static string GetPublicStateDetail(ServerDiscoveryDetail detail)
    {
        if (!string.IsNullOrWhiteSpace(detail.Status))
            return $"Server: {NormalizeStatusLabel(detail.Status)}";

        return GetPublicState(detail) == "Online"
            ? "Server: Reachable"
            : $"Last seen: {FormatTimestamp(detail.Row.LastSeenUtc)}";
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
}
