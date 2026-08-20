using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Services.Forums;

public sealed class ForumThreadQueryService
{
    public const int PostsPerPage = 20;

    private readonly ApplicationDbContext _db;
    private readonly ForumIdentityService _forumIdentityService;

    public ForumThreadQueryService(ApplicationDbContext db, ForumIdentityService forumIdentityService)
    {
        _db = db;
        _forumIdentityService = forumIdentityService;
    }

    public async Task<ForumThreadPageData?> GetAsync(
        string sectionSlug,
        long threadId,
        int requestedPageNumber,
        ClaimsPrincipal user,
        CancellationToken cancellationToken = default)
    {
        bool canModerate = ForumAccessEvaluator.CanModerate(user);
        string? currentUserId = ForumAccessEvaluator.GetUserId(user);

        ForumThread? thread = await _db.ForumThreads
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);

        if (thread == null)
            return null;

        ForumSection? section = await _db.ForumSections
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == thread.SectionId && x.Slug == sectionSlug, cancellationToken);

        if (section == null)
            return null;

        if (thread.IsHidden && !canModerate)
            return null;

        long? firstPostId = await _db.ForumPosts
            .AsNoTracking()
            .Where(x => x.ThreadId == thread.Id)
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .Select(x => (long?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        IQueryable<ForumPost> visiblePostQuery = _db.ForumPosts
            .AsNoTracking()
            .Where(x => x.ThreadId == thread.Id && (canModerate || !x.IsHidden));

        int totalVisiblePosts = await visiblePostQuery.CountAsync(cancellationToken);
        int totalPages = Math.Max(1, (int)Math.Ceiling(totalVisiblePosts / (double)PostsPerPage));
        int pageNumber = Math.Clamp(requestedPageNumber, 1, totalPages);

        List<ForumPost> posts = await visiblePostQuery
            .OrderBy(x => x.CreatedUtc)
            .ThenBy(x => x.Id)
            .Skip((pageNumber - 1) * PostsPerPage)
            .Take(PostsPerPage)
            .ToListAsync(cancellationToken);

        Dictionary<string, ForumAuthorProfile> authorProfiles = await _forumIdentityService.GetProfilesAsync(
            posts.Select(x => x.AuthorUserId).Append(thread.AuthorUserId).Append(thread.LastPostUserId),
            cancellationToken);

        authorProfiles.TryGetValue(thread.AuthorUserId, out ForumAuthorProfile? threadAuthor);

        ForumThreadView threadView = new(
            thread.Id,
            thread.Title,
            threadAuthor?.DisplayName ?? thread.AuthorUserId,
            threadAuthor?.BadgeLabel,
            threadAuthor?.BadgeCssClass,
            thread.CreatedUtc,
            thread.UpdatedUtc,
            canModerate || (
                string.Equals(thread.AuthorUserId, currentUserId, StringComparison.Ordinal)
                && totalVisiblePosts <= 1
                && !thread.IsHidden),
            thread.IsPinned,
            thread.IsLocked,
            thread.IsHidden);

        ForumPostView[] postViews = posts
            .Select(post =>
            {
                authorProfiles.TryGetValue(post.AuthorUserId, out ForumAuthorProfile? author);
                bool canEdit = canModerate || (
                    string.Equals(post.AuthorUserId, currentUserId, StringComparison.Ordinal)
                    && !post.IsHidden);

                return new ForumPostView(
                    post.Id,
                    author?.DisplayName ?? post.AuthorUserId,
                    author?.BadgeLabel,
                    author?.BadgeCssClass,
                    post.Content,
                    canEdit,
                    canModerate || (
                        string.Equals(post.AuthorUserId, currentUserId, StringComparison.Ordinal)
                        && !post.IsHidden
                        && firstPostId != post.Id),
                    firstPostId == post.Id,
                    post.IsHidden,
                    post.CreatedUtc,
                    post.UpdatedUtc);
            })
            .ToArray();

        return new ForumThreadPageData(
            new ForumSectionView(section.Id, section.Slug, section.Title, section.Description),
            threadView,
            postViews,
            canModerate,
            pageNumber,
            totalPages,
            totalVisiblePosts);
    }
}

public sealed record ForumThreadPageData(
    ForumSectionView Section,
    ForumThreadView Thread,
    IReadOnlyList<ForumPostView> Posts,
    bool CanModerate,
    int PageNumber,
    int TotalPages,
    int TotalVisiblePosts);

public sealed record ForumSectionView(
    int Id,
    string Slug,
    string Title,
    string Description);

public sealed record ForumThreadView(
    long Id,
    string Title,
    string AuthorName,
    string? AuthorBadge,
    string? AuthorBadgeCssClass,
    DateTime CreatedUtc,
    DateTime UpdatedUtc,
    bool CanDelete,
    bool IsPinned,
    bool IsLocked,
    bool IsHidden);

public sealed record ForumPostView(
    long Id,
    string AuthorName,
    string? AuthorBadge,
    string? AuthorBadgeCssClass,
    string Content,
    bool CanEdit,
    bool CanDelete,
    bool IsOpeningPost,
    bool IsHidden,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);
