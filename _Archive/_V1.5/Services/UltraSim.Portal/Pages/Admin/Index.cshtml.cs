using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Pages.Admin;

[Authorize(Policy = PortalPolicies.AdminPanel)]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly RegistryQueryClient _registryQueryClient;

    public IndexModel(
        ApplicationDbContext db,
        RegistryQueryClient registryQueryClient)
    {
        _db = db;
        _registryQueryClient = registryQueryClient;
    }

    public PortalRegistryRuntimeSnapshot? RegistryRuntime { get; private set; }
    public string? RegistryError { get; private set; }
    public int TotalAccounts { get; private set; }
    public int PrimaryOwnerCount { get; private set; }
    public int OwnerCount { get; private set; }
    public int AdminCount { get; private set; }
    public int DeveloperCount { get; private set; }
    public int ModeratorCount { get; private set; }
    public int AccountsToday { get; private set; }
    public int Accounts7Days { get; private set; }
    public int Accounts30Days { get; private set; }
    public int ActiveAccounts24Hours { get; private set; }
    public int ActiveAccounts7Days { get; private set; }
    public int ActiveAccounts30Days { get; private set; }
    public int ForumSectionCount { get; private set; }
    public int ForumThreadCount { get; private set; }
    public int ForumThreads7Days { get; private set; }
    public int VisibleForumThreadCount { get; private set; }
    public int HiddenForumThreadCount { get; private set; }
    public int LockedForumThreadCount { get; private set; }
    public int PinnedForumThreadCount { get; private set; }
    public int ForumPostCount { get; private set; }
    public int HiddenForumPostCount { get; private set; }
    public int ForumPosts7Days { get; private set; }
    public IReadOnlyList<RecentForumThreadRow> RecentForumThreads { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        DateTime nowUtc = DateTime.UtcNow;
        DateTime todayThreshold = nowUtc.AddDays(-1);
        DateTime weekThreshold = nowUtc.AddDays(-7);
        DateTime monthThreshold = nowUtc.AddDays(-30);

        TotalAccounts = await _db.Users.CountAsync(cancellationToken);
        PrimaryOwnerCount = await _db.Users.CountAsync(x => x.IsPrimaryOwner, cancellationToken);
        AccountsToday = await _db.Users.CountAsync(x => x.CreatedUtc >= todayThreshold, cancellationToken);
        Accounts7Days = await _db.Users.CountAsync(x => x.CreatedUtc >= weekThreshold, cancellationToken);
        Accounts30Days = await _db.Users.CountAsync(x => x.CreatedUtc >= monthThreshold, cancellationToken);
        ActiveAccounts24Hours = await _db.Users.CountAsync(x => x.LastActiveUtc >= todayThreshold, cancellationToken);
        ActiveAccounts7Days = await _db.Users.CountAsync(x => x.LastActiveUtc >= weekThreshold, cancellationToken);
        ActiveAccounts30Days = await _db.Users.CountAsync(x => x.LastActiveUtc >= monthThreshold, cancellationToken);
        ForumSectionCount = await _db.ForumSections.CountAsync(cancellationToken);
        ForumThreadCount = await _db.ForumThreads.CountAsync(cancellationToken);
        ForumThreads7Days = await _db.ForumThreads.CountAsync(x => x.CreatedUtc >= weekThreshold, cancellationToken);
        VisibleForumThreadCount = await _db.ForumThreads.CountAsync(x => !x.IsHidden, cancellationToken);
        HiddenForumThreadCount = await _db.ForumThreads.CountAsync(x => x.IsHidden, cancellationToken);
        LockedForumThreadCount = await _db.ForumThreads.CountAsync(x => x.IsLocked, cancellationToken);
        PinnedForumThreadCount = await _db.ForumThreads.CountAsync(x => x.IsPinned, cancellationToken);
        ForumPostCount = await _db.ForumPosts.CountAsync(cancellationToken);
        HiddenForumPostCount = await _db.ForumPosts.CountAsync(x => x.IsHidden, cancellationToken);
        ForumPosts7Days = await _db.ForumPosts.CountAsync(x => x.CreatedUtc >= weekThreshold, cancellationToken);
        OwnerCount = await (
            from userRole in _db.UserRoles
            join role in _db.Roles on userRole.RoleId equals role.Id
            where role.Name == PortalRoles.Owner
            select userRole)
            .CountAsync(cancellationToken);
        AdminCount = await (
            from userRole in _db.UserRoles
            join role in _db.Roles on userRole.RoleId equals role.Id
            where role.Name == PortalRoles.Admin
            select userRole)
            .CountAsync(cancellationToken);
        DeveloperCount = await (
            from userRole in _db.UserRoles
            join role in _db.Roles on userRole.RoleId equals role.Id
            where role.Name == PortalRoles.Developer
            select userRole)
            .CountAsync(cancellationToken);
        ModeratorCount = await (
            from userRole in _db.UserRoles
            join role in _db.Roles on userRole.RoleId equals role.Id
            where role.Name == PortalRoles.Moderator
            select userRole)
            .CountAsync(cancellationToken);

        RecentForumThreads = await (
            from thread in _db.ForumThreads.AsNoTracking()
            join section in _db.ForumSections.AsNoTracking() on thread.SectionId equals section.Id
            orderby thread.LastPostedUtc descending
            select new RecentForumThreadRow(
                thread.Id,
                section.Slug,
                section.Title,
                thread.Title,
                thread.IsHidden,
                thread.IsLocked,
                thread.IsPinned,
                thread.LastPostedUtc))
            .Take(10)
            .ToArrayAsync(cancellationToken);

        try
        {
            RegistryRuntime = await _registryQueryClient.GetRuntimeSnapshotAsync(cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            RegistryError = ex.StatusCode switch
            {
                System.Net.HttpStatusCode.Unauthorized =>
                    "Registry runtime endpoint returned HTTP 401. Configure matching Registry:AdminApiToken values in the portal and registry settings.",
                _ when ex.StatusCode.HasValue =>
                    $"Registry runtime endpoint returned HTTP {(int)ex.StatusCode.Value}.",
                _ => "Could not reach the registry runtime endpoint.",
            };
        }
        catch
        {
            RegistryError = "An unexpected error occurred while loading registry runtime data.";
        }
    }

    public sealed record RecentForumThreadRow(
        long ThreadId,
        string SectionSlug,
        string SectionTitle,
        string Title,
        bool IsHidden,
        bool IsLocked,
        bool IsPinned,
        DateTime LastPostedUtc);
}
