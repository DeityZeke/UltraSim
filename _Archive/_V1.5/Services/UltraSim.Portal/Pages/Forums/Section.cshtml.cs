using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;

namespace UltraSim.Portal.Pages.Forums;

public sealed class SectionModel : PageModel
{
    private const int ThreadsPerPage = 20;

    private readonly ApplicationDbContext _db;
    private readonly ForumIdentityService _forumIdentityService;

    public SectionModel(ApplicationDbContext db, ForumIdentityService forumIdentityService)
    {
        _db = db;
        _forumIdentityService = forumIdentityService;
    }

    public ForumSectionView? Section { get; private set; }
    public IReadOnlyList<ForumThreadSummary> Threads { get; private set; } = [];
    public bool CanCreateThread => User.Identity?.IsAuthenticated == true;
    public bool CanModerate { get; private set; }
    [BindProperty(SupportsGet = true)]
    public string? Search { get; set; }
    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;
    public int TotalPages { get; private set; }
    public int TotalThreads { get; private set; }
    public string NormalizedSearch => string.IsNullOrWhiteSpace(Search) ? string.Empty : Search.Trim();

    public async Task<IActionResult> OnGetAsync(string sectionSlug, CancellationToken cancellationToken)
    {
        CanModerate = HasForumModeration(User);

        ForumSection? section = await _db.ForumSections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Slug == sectionSlug, cancellationToken);

        if (section == null)
            return NotFound();

        Section = new ForumSectionView(section.Id, section.Slug, section.Title, section.Description, section.IsReadOnly);

        IQueryable<ForumThread> threadQuery = _db.ForumThreads
            .AsNoTracking()
            .Where(x => x.SectionId == section.Id);

        if (!CanModerate)
            threadQuery = threadQuery.Where(x => !x.IsHidden);

        string normalizedSearch = NormalizedSearch;
        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            string searchTerm = normalizedSearch.ToLowerInvariant();
            string[] matchingAuthorIds = await _db.Users
                .AsNoTracking()
                .Where(x =>
                    ((x.DisplayName ?? string.Empty).ToLower().Contains(searchTerm))
                    || ((x.UserName ?? string.Empty).ToLower().Contains(searchTerm))
                    || ((x.Email ?? string.Empty).ToLower().Contains(searchTerm)))
                .Select(x => x.Id)
                .ToArrayAsync(cancellationToken);

            long[] matchingThreadIdsFromPosts = await (
                from post in _db.ForumPosts.AsNoTracking()
                join thread in _db.ForumThreads.AsNoTracking() on post.ThreadId equals thread.Id
                where thread.SectionId == section.Id
                    && (CanModerate || !thread.IsHidden)
                    && (CanModerate || !post.IsHidden)
                    && (post.Content.ToLower().Contains(searchTerm)
                        || matchingAuthorIds.Contains(post.AuthorUserId))
                select post.ThreadId)
                .Distinct()
                .ToArrayAsync(cancellationToken);

            threadQuery = threadQuery.Where(x =>
                x.Title.ToLower().Contains(searchTerm)
                || matchingThreadIdsFromPosts.Contains(x.Id)
                || matchingAuthorIds.Contains(x.AuthorUserId)
                || (!string.IsNullOrWhiteSpace(x.LastPostUserId) && matchingAuthorIds.Contains(x.LastPostUserId)));
        }

        TotalThreads = await threadQuery.CountAsync(cancellationToken);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalThreads / (double)ThreadsPerPage));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        List<ForumThread> threads = await threadQuery
            .OrderByDescending(x => x.IsPinned)
            .ThenByDescending(x => x.LastPostedUtc)
            .Skip((PageNumber - 1) * ThreadsPerPage)
            .Take(ThreadsPerPage)
            .ToListAsync(cancellationToken);

        long[] threadIds = threads.Select(x => x.Id).ToArray();
        List<ForumPost> visiblePosts = threadIds.Length == 0
            ? []
            : await _db.ForumPosts
                .AsNoTracking()
                .Where(x => threadIds.Contains(x.ThreadId) && (CanModerate || !x.IsHidden))
                .OrderByDescending(x => x.CreatedUtc)
                .ThenByDescending(x => x.Id)
                .ToListAsync(cancellationToken);

        Dictionary<long, int> postCounts = visiblePosts
            .GroupBy(x => x.ThreadId)
            .ToDictionary(group => group.Key, group => group.Count());

        Dictionary<long, ForumPost> latestPostsByThread = visiblePosts
            .GroupBy(x => x.ThreadId)
            .ToDictionary(group => group.Key, group => group.First());

        Dictionary<string, ForumAuthorProfile> authorProfiles = await _forumIdentityService.GetProfilesAsync(
            threads
                .SelectMany(static x => new[] { x.AuthorUserId, x.LastPostUserId })
                .Concat(latestPostsByThread.Values.Select(x => x.AuthorUserId)),
            cancellationToken);

        Threads = threads
            .Select(thread =>
            {
                authorProfiles.TryGetValue(thread.AuthorUserId, out ForumAuthorProfile? author);
                ForumPost? latestVisiblePost = latestPostsByThread.GetValueOrDefault(thread.Id);
                ForumAuthorProfile? lastPoster = null;
                if (!string.IsNullOrWhiteSpace(latestVisiblePost?.AuthorUserId))
                    authorProfiles.TryGetValue(latestVisiblePost.AuthorUserId, out lastPoster);

                int postCount = postCounts.GetValueOrDefault(thread.Id, 0);
                int replyCount = Math.Max(0, postCount - 1);

                return new ForumThreadSummary(
                    thread.Id,
                    thread.Title,
                    author?.DisplayName ?? thread.AuthorUserId,
                    author?.BadgeLabel,
                    author?.BadgeCssClass,
                    lastPoster?.DisplayName ?? author?.DisplayName ?? thread.AuthorUserId,
                    lastPoster?.BadgeLabel,
                    lastPoster?.BadgeCssClass,
                    thread.CreatedUtc,
                    latestVisiblePost?.CreatedUtc ?? thread.CreatedUtc,
                    replyCount,
                    thread.IsPinned,
                    thread.IsLocked,
                    thread.IsHidden);
            })
            .ToArray();

        return Page();
    }

    public sealed record ForumSectionView(
        int Id,
        string Slug,
        string Title,
        string Description,
        bool IsReadOnly);

    public sealed record ForumThreadSummary(
        long Id,
        string Title,
        string AuthorName,
        string? AuthorBadge,
        string? AuthorBadgeCssClass,
        string LastPosterName,
        string? LastPosterBadge,
        string? LastPosterBadgeCssClass,
        DateTime CreatedUtc,
        DateTime LastPostedUtc,
        int ReplyCount,
        bool IsPinned,
        bool IsLocked,
        bool IsHidden);

    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    private static bool HasForumModeration(ClaimsPrincipal user)
    {
        return user.IsInRole(PortalRoles.PrimaryOwner)
            || user.IsInRole(PortalRoles.Owner)
            || user.IsInRole(PortalRoles.Admin)
            || user.IsInRole(PortalRoles.Moderator);
    }
}
