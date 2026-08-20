using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Pages.Admin.Accounts;

[Authorize(Policy = PortalPolicies.AdminPanel)]
public sealed class DetailModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ApplicationDbContext _db;

    public DetailModel(
        UserManager<ApplicationUser> userManager,
        ApplicationDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    [BindProperty(SupportsGet = true)]
    public string Id { get; set; } = string.Empty;

    public ApplicationUser? TargetUser { get; private set; }
    public string EffectiveRole { get; private set; } = PortalRoles.User;
    public IReadOnlyList<ServerClaim> Claims { get; private set; } = [];
    public IReadOnlyList<AdminAuditRecord> RecentAudit { get; private set; } = [];
    public int ForumThreadCount { get; private set; }
    public int HiddenForumThreadCount { get; private set; }
    public int ForumPostCount { get; private set; }
    public int HiddenForumPostCount { get; private set; }
    public DateTime? LastForumActivityUtc { get; private set; }
    public IReadOnlyList<RecentForumThreadRow> RecentForumThreads { get; private set; } = [];
    public IReadOnlyList<RecentForumPostRow> RecentForumPosts { get; private set; } = [];

    public async Task<IActionResult> OnGetAsync(CancellationToken cancellationToken)
    {
        TargetUser = await _userManager.FindByIdAsync(Id);
        if (TargetUser == null)
            return NotFound();

        EffectiveRole = await PortalRoleResolver.GetEffectiveRoleAsync(_userManager, TargetUser);

        List<ServerClaim> claims = await _db.ServerClaims
            .Where(x => x.OwnerUserId == TargetUser.Id)
            .ToListAsync(cancellationToken);

        Claims = claims
            .OrderByDescending(x => x.UpdatedUtc.UtcDateTime)
            .ThenBy(x => x.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        RecentAudit = await _db.AdminAuditRecords
            .Where(x => x.ActorUserId == TargetUser.Id || (x.TargetType == "account" && x.TargetId == TargetUser.Id))
            // SQLite cannot order by DateTimeOffset directly; audit ids are append-only and monotonic.
            .OrderByDescending(x => x.Id)
            .Take(50)
            .ToArrayAsync(cancellationToken);

        ForumThreadCount = await _db.ForumThreads
            .CountAsync(x => x.AuthorUserId == TargetUser.Id, cancellationToken);

        HiddenForumThreadCount = await _db.ForumThreads
            .CountAsync(x => x.AuthorUserId == TargetUser.Id && x.IsHidden, cancellationToken);

        ForumPostCount = await _db.ForumPosts
            .CountAsync(x => x.AuthorUserId == TargetUser.Id, cancellationToken);

        HiddenForumPostCount = await _db.ForumPosts
            .CountAsync(x => x.AuthorUserId == TargetUser.Id && x.IsHidden, cancellationToken);

        DateTime? lastThreadActivityUtc = await _db.ForumThreads
            .Where(x => x.AuthorUserId == TargetUser.Id)
            .MaxAsync(x => (DateTime?)x.LastPostedUtc, cancellationToken);

        DateTime? lastPostActivityUtc = await _db.ForumPosts
            .Where(x => x.AuthorUserId == TargetUser.Id)
            .MaxAsync(x => (DateTime?)x.UpdatedUtc, cancellationToken);

        LastForumActivityUtc = lastThreadActivityUtc.HasValue && lastPostActivityUtc.HasValue
            ? (lastThreadActivityUtc.Value >= lastPostActivityUtc.Value ? lastThreadActivityUtc : lastPostActivityUtc)
            : lastThreadActivityUtc ?? lastPostActivityUtc;

        RecentForumThreads = await (
            from thread in _db.ForumThreads.AsNoTracking()
            join section in _db.ForumSections.AsNoTracking() on thread.SectionId equals section.Id
            where thread.AuthorUserId == TargetUser.Id
            orderby thread.LastPostedUtc descending
            select new RecentForumThreadRow(
                thread.Id,
                section.Slug,
                section.Title,
                thread.Title,
                thread.IsHidden,
                thread.IsLocked,
                thread.IsPinned,
                thread.CreatedUtc,
                thread.LastPostedUtc))
            .Take(10)
            .ToArrayAsync(cancellationToken);

        RecentForumPosts = await (
            from post in _db.ForumPosts.AsNoTracking()
            join thread in _db.ForumThreads.AsNoTracking() on post.ThreadId equals thread.Id
            join section in _db.ForumSections.AsNoTracking() on thread.SectionId equals section.Id
            where post.AuthorUserId == TargetUser.Id
            orderby post.UpdatedUtc descending, post.CreatedUtc descending
            select new RecentForumPostRow(
                post.Id,
                thread.Id,
                section.Slug,
                section.Title,
                thread.Title,
                post.IsHidden,
                post.CreatedUtc,
                post.UpdatedUtc,
                post.Content.Length > 160 ? post.Content.Substring(0, 160) + "..." : post.Content))
            .Take(10)
            .ToArrayAsync(cancellationToken);

        return Page();
    }

    public sealed record RecentForumThreadRow(
        long ThreadId,
        string SectionSlug,
        string SectionTitle,
        string Title,
        bool IsHidden,
        bool IsLocked,
        bool IsPinned,
        DateTime CreatedUtc,
        DateTime LastPostedUtc);

    public sealed record RecentForumPostRow(
        long PostId,
        long ThreadId,
        string SectionSlug,
        string SectionTitle,
        string ThreadTitle,
        bool IsHidden,
        DateTime CreatedUtc,
        DateTime UpdatedUtc,
        string Preview);
}
