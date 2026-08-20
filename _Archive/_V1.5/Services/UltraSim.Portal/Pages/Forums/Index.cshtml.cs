using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;

namespace UltraSim.Portal.Pages.Forums;

public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly ForumIdentityService _forumIdentityService;

    public IndexModel(ApplicationDbContext db, ForumIdentityService forumIdentityService)
    {
        _db = db;
        _forumIdentityService = forumIdentityService;
    }

    public IReadOnlyList<ForumSectionSummary> Sections { get; private set; } = [];
    public bool CanModerate { get; private set; }

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        CanModerate = HasForumModeration(User);

        List<ForumSection> sections = await _db.ForumSections
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Title)
            .ToListAsync(cancellationToken);

        List<SectionActivityRow> activityRows = new(sections.Count);
        for (int i = 0; i < sections.Count; i++)
        {
            ForumSection section = sections[i];

            List<ForumThread> visibleThreads = await _db.ForumThreads
                .AsNoTracking()
                .Where(x => x.SectionId == section.Id && !x.IsHidden)
                .ToListAsync(cancellationToken);

            long[] threadIds = visibleThreads.Select(x => x.Id).ToArray();
            List<ForumPost> visiblePosts = threadIds.Length == 0
                ? []
                : await _db.ForumPosts
                    .AsNoTracking()
                    .Where(x => threadIds.Contains(x.ThreadId) && !x.IsHidden)
                    .OrderByDescending(x => x.CreatedUtc)
                    .ThenByDescending(x => x.Id)
                    .ToListAsync(cancellationToken);

            int threadCount = visibleThreads.Count;
            int postCount = visiblePosts.Count;
            Dictionary<long, ForumPost> latestPostsByThread = visiblePosts
                .GroupBy(x => x.ThreadId)
                .ToDictionary(group => group.Key, group => group.First());

            LatestThreadRow? latestThread = visibleThreads
                .Select(thread =>
                {
                    ForumPost? latestPost = latestPostsByThread.GetValueOrDefault(thread.Id);
                    return new LatestThreadRow(
                        thread.Id,
                        thread.Title,
                        latestPost?.CreatedUtc ?? thread.CreatedUtc,
                        latestPost?.AuthorUserId ?? thread.AuthorUserId);
                })
                .OrderByDescending(x => x.LastPostedUtc)
                .FirstOrDefault();

            activityRows.Add(new SectionActivityRow(section, threadCount, postCount, latestThread));
        }

        Dictionary<string, ForumAuthorProfile> authorProfiles = await _forumIdentityService.GetProfilesAsync(
            activityRows.Select(x => x.LatestThread?.AuthorUserId),
            cancellationToken);

        Sections = activityRows
            .Select(x =>
            {
                ForumAuthorProfile? latestAuthor = null;
                if (!string.IsNullOrWhiteSpace(x.LatestThread?.AuthorUserId))
                    authorProfiles.TryGetValue(x.LatestThread.AuthorUserId, out latestAuthor);

                return new ForumSectionSummary(
                    x.Section.Id,
                    x.Section.Slug,
                    x.Section.Title,
                    x.Section.Description,
                    x.ThreadCount,
                    x.PostCount,
                    x.LatestThread?.ThreadId,
                    x.LatestThread?.Title,
                    latestAuthor?.DisplayName,
                    latestAuthor?.BadgeLabel,
                    latestAuthor?.BadgeCssClass,
                    x.LatestThread?.LastPostedUtc);
            })
            .ToArray();
    }

    public sealed record ForumSectionSummary(
        int Id,
        string Slug,
        string Title,
        string Description,
        int ThreadCount,
        int PostCount,
        long? LatestThreadId,
        string? LatestThreadTitle,
        string? LatestAuthorName,
        string? LatestAuthorBadge,
        string? LatestAuthorBadgeCssClass,
        DateTime? LatestPostedUtc);

    private sealed record SectionActivityRow(
        ForumSection Section,
        int ThreadCount,
        int PostCount,
        LatestThreadRow? LatestThread);

    private sealed record LatestThreadRow(
        long ThreadId,
        string Title,
        DateTime LastPostedUtc,
        string AuthorUserId);

    private static bool HasForumModeration(ClaimsPrincipal user)
    {
        return user.IsInRole(PortalRoles.PrimaryOwner)
            || user.IsInRole(PortalRoles.Owner)
            || user.IsInRole(PortalRoles.Admin)
            || user.IsInRole(PortalRoles.Moderator);
    }
}
