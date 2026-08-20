using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;

namespace UltraSim.Portal.Pages.Forums;

public sealed class SearchModel : PageModel
{
    private const int ResultsPerPage = 20;

    private readonly ApplicationDbContext _db;
    private readonly ForumIdentityService _forumIdentityService;

    public SearchModel(ApplicationDbContext db, ForumIdentityService forumIdentityService)
    {
        _db = db;
        _forumIdentityService = forumIdentityService;
    }

    [BindProperty(SupportsGet = true)]
    public string? Query { get; set; }

    [BindProperty(SupportsGet = true)]
    public int PageNumber { get; set; } = 1;

    public IReadOnlyList<SearchResultRow> Results { get; private set; } = [];
    public bool CanModerate { get; private set; }
    public string NormalizedQuery => string.IsNullOrWhiteSpace(Query) ? string.Empty : Query.Trim();
    public int TotalResults { get; private set; }
    public int TotalPages { get; private set; }
    public bool HasPreviousPage => PageNumber > 1;
    public bool HasNextPage => PageNumber < TotalPages;

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanModerate = HasForumModeration(User);

        string normalizedQuery = NormalizedQuery;
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            TotalPages = 1;
            PageNumber = 1;
            return;
        }

        string searchTerm = normalizedQuery.ToLowerInvariant();
        string[] matchingAuthorIds = await _db.Users
            .AsNoTracking()
            .Where(x =>
                ((x.DisplayName ?? string.Empty).ToLower().Contains(searchTerm))
                || ((x.UserName ?? string.Empty).ToLower().Contains(searchTerm))
                || ((x.Email ?? string.Empty).ToLower().Contains(searchTerm)))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        IQueryable<ForumThread> visibleThreadQuery = _db.ForumThreads.AsNoTracking();
        if (!CanModerate)
            visibleThreadQuery = visibleThreadQuery.Where(x => !x.IsHidden);

        long[] matchingThreadIdsFromPosts = await (
            from post in _db.ForumPosts.AsNoTracking()
            join thread in _db.ForumThreads.AsNoTracking() on post.ThreadId equals thread.Id
            where (CanModerate || !thread.IsHidden)
                && (CanModerate || !post.IsHidden)
                && (post.Content.ToLower().Contains(searchTerm)
                    || matchingAuthorIds.Contains(post.AuthorUserId))
            select post.ThreadId)
            .Distinct()
            .ToArrayAsync(cancellationToken);

        long[] matchingThreadIdsFromTitle = await visibleThreadQuery
            .Where(x =>
                x.Title.ToLower().Contains(searchTerm)
                || matchingAuthorIds.Contains(x.AuthorUserId)
                || (!string.IsNullOrWhiteSpace(x.LastPostUserId) && matchingAuthorIds.Contains(x.LastPostUserId)))
            .Select(x => x.Id)
            .ToArrayAsync(cancellationToken);

        long[] matchingThreadIds = matchingThreadIdsFromTitle
            .Concat(matchingThreadIdsFromPosts)
            .Distinct()
            .ToArray();

        TotalResults = matchingThreadIds.Length;
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalResults / (double)ResultsPerPage));
        PageNumber = Math.Clamp(PageNumber, 1, TotalPages);

        HashSet<long> titleMatches = matchingThreadIdsFromTitle.ToHashSet();
        HashSet<long> postMatches = matchingThreadIdsFromPosts.ToHashSet();

        List<ThreadProjection> threads = await (
            from thread in _db.ForumThreads.AsNoTracking()
            join section in _db.ForumSections.AsNoTracking() on thread.SectionId equals section.Id
            where matchingThreadIds.Contains(thread.Id)
            orderby thread.LastPostedUtc descending
            select new ThreadProjection(
                thread.Id,
                thread.SectionId,
                section.Slug,
                section.Title,
                thread.Title,
                thread.AuthorUserId,
                thread.LastPostedUtc,
                thread.IsHidden))
            .Skip((PageNumber - 1) * ResultsPerPage)
            .Take(ResultsPerPage)
            .ToListAsync(cancellationToken);

        long[] pageThreadIds = threads.Select(x => x.ThreadId).ToArray();
        List<ForumPost> visiblePosts = pageThreadIds.Length == 0
            ? []
            : await _db.ForumPosts
                .AsNoTracking()
                .Where(x => pageThreadIds.Contains(x.ThreadId) && (CanModerate || !x.IsHidden))
                .OrderByDescending(x => x.CreatedUtc)
                .ThenByDescending(x => x.Id)
                .ToListAsync(cancellationToken);

        Dictionary<long, ForumPost> latestVisiblePostsByThread = visiblePosts
            .GroupBy(x => x.ThreadId)
            .ToDictionary(group => group.Key, group => group.First());

        Dictionary<string, ForumAuthorProfile> authorProfiles = await _forumIdentityService.GetProfilesAsync(
            threads.Select(x => x.AuthorUserId).Concat(visiblePosts.Select(x => x.AuthorUserId)),
            cancellationToken);

        Results = threads
            .Select(thread =>
            {
                authorProfiles.TryGetValue(thread.AuthorUserId, out ForumAuthorProfile? author);
                ForumPost? latestPost = latestVisiblePostsByThread.GetValueOrDefault(thread.ThreadId);

                string matchLabel = titleMatches.Contains(thread.ThreadId) && postMatches.Contains(thread.ThreadId)
                    ? "Title and Posts"
                    : titleMatches.Contains(thread.ThreadId)
                        ? "Title"
                        : "Posts";

                return new SearchResultRow(
                    thread.ThreadId,
                    thread.SectionSlug,
                    thread.SectionTitle,
                    thread.Title,
                    author?.DisplayName ?? thread.AuthorUserId,
                    author?.BadgeLabel,
                    author?.BadgeCssClass,
                    latestPost?.CreatedUtc ?? thread.LastPostedUtc,
                    matchLabel,
                    thread.IsHidden);
            })
            .ToArray();
    }

    public sealed record SearchResultRow(
        long ThreadId,
        string SectionSlug,
        string SectionTitle,
        string Title,
        string AuthorName,
        string? AuthorBadge,
        string? AuthorBadgeCssClass,
        DateTime LastVisibleActivityUtc,
        string MatchLabel,
        bool IsHidden);

    private sealed record ThreadProjection(
        long ThreadId,
        int SectionId,
        string SectionSlug,
        string SectionTitle,
        string Title,
        string AuthorUserId,
        DateTime LastPostedUtc,
        bool IsHidden);

    private static bool HasForumModeration(ClaimsPrincipal user)
    {
        return user.IsInRole(PortalRoles.PrimaryOwner)
            || user.IsInRole(PortalRoles.Owner)
            || user.IsInRole(PortalRoles.Admin)
            || user.IsInRole(PortalRoles.Moderator);
    }
}
