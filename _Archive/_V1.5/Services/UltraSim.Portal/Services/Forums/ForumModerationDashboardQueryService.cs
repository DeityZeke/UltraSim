using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Services.Forums;

public sealed class ForumModerationDashboardQueryService
{
    private readonly ApplicationDbContext _db;
    private readonly ForumIdentityService _forumIdentityService;

    public ForumModerationDashboardQueryService(ApplicationDbContext db, ForumIdentityService forumIdentityService)
    {
        _db = db;
        _forumIdentityService = forumIdentityService;
    }

    public async Task<ForumModerationDashboardData?> GetAsync(
        string? sectionSlug,
        CancellationToken cancellationToken = default)
    {
        List<ForumSectionOption> sections = await _db.ForumSections
            .AsNoTracking()
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.Title)
            .Select(x => new ForumSectionOption(x.Id, x.Slug, x.Title))
            .ToListAsync(cancellationToken);

        int? sectionId = null;
        string currentFilterLabel = "All Sections";
        if (!string.IsNullOrWhiteSpace(sectionSlug))
        {
            ForumSectionOption? selectedSection = sections.FirstOrDefault(x =>
                string.Equals(x.Slug, sectionSlug.Trim(), StringComparison.OrdinalIgnoreCase));

            if (selectedSection == null)
                return null;

            sectionId = selectedSection.Id;
            currentFilterLabel = selectedSection.Title;
        }

        IQueryable<ForumThread> threadQuery = _db.ForumThreads.AsNoTracking();
        if (sectionId.HasValue)
            threadQuery = threadQuery.Where(x => x.SectionId == sectionId.Value);

        int hiddenThreadCount = await threadQuery.CountAsync(x => x.IsHidden, cancellationToken);
        int lockedThreadCount = await threadQuery.CountAsync(x => x.IsLocked, cancellationToken);
        int pinnedThreadCount = await threadQuery.CountAsync(x => x.IsPinned, cancellationToken);

        List<ForumThreadRecord> threadRecords = await (
            from thread in threadQuery
            join section in _db.ForumSections.AsNoTracking() on thread.SectionId equals section.Id
            where thread.IsHidden || thread.IsLocked || thread.IsPinned
            orderby thread.UpdatedUtc descending, thread.LastPostedUtc descending
            select new ForumThreadRecord(
                thread.Id,
                thread.SectionId,
                section.Slug,
                section.Title,
                thread.Title,
                thread.AuthorUserId,
                thread.IsPinned,
                thread.IsLocked,
                thread.IsHidden,
                thread.LastPostedUtc,
                thread.UpdatedUtc))
            .Take(40)
            .ToListAsync(cancellationToken);

        IQueryable<ForumPost> hiddenPostQuery = _db.ForumPosts.AsNoTracking().Where(x => x.IsHidden);
        if (sectionId.HasValue)
        {
            hiddenPostQuery = hiddenPostQuery.Where(post =>
                _db.ForumThreads.Any(thread => thread.Id == post.ThreadId && thread.SectionId == sectionId.Value));
        }

        int hiddenPostCount = await hiddenPostQuery.CountAsync(cancellationToken);

        List<ForumPostRecord> hiddenPostRecords = await (
            from post in hiddenPostQuery
            join thread in _db.ForumThreads.AsNoTracking() on post.ThreadId equals thread.Id
            join section in _db.ForumSections.AsNoTracking() on thread.SectionId equals section.Id
            orderby post.UpdatedUtc descending, post.CreatedUtc descending
            select new ForumPostRecord(
                post.Id,
                post.ThreadId,
                section.Slug,
                section.Title,
                thread.Title,
                post.AuthorUserId,
                post.CreatedUtc,
                post.UpdatedUtc))
            .Take(40)
            .ToListAsync(cancellationToken);

        Dictionary<string, ForumAuthorProfile> authorProfiles = await _forumIdentityService.GetProfilesAsync(
            threadRecords.Select(x => x.AuthorUserId).Concat(hiddenPostRecords.Select(x => x.AuthorUserId)),
            cancellationToken);

        IReadOnlyList<ModerationThreadRow> threads = threadRecords
            .Select(record =>
            {
                authorProfiles.TryGetValue(record.AuthorUserId, out ForumAuthorProfile? author);
                return new ModerationThreadRow(
                    record.ThreadId,
                    record.SectionSlug,
                    record.SectionTitle,
                    record.Title,
                    author?.DisplayName ?? record.AuthorUserId,
                    author?.BadgeLabel,
                    author?.BadgeCssClass,
                    record.IsPinned,
                    record.IsLocked,
                    record.IsHidden,
                    record.LastPostedUtc,
                    record.UpdatedUtc);
            })
            .ToArray();

        IReadOnlyList<ModerationPostRow> hiddenPosts = hiddenPostRecords
            .Select(record =>
            {
                authorProfiles.TryGetValue(record.AuthorUserId, out ForumAuthorProfile? author);
                return new ModerationPostRow(
                    record.PostId,
                    record.ThreadId,
                    record.SectionSlug,
                    record.SectionTitle,
                    record.ThreadTitle,
                    author?.DisplayName ?? record.AuthorUserId,
                    author?.BadgeLabel,
                    author?.BadgeCssClass,
                    record.CreatedUtc,
                    record.UpdatedUtc);
            })
            .ToArray();

        IReadOnlyList<RecentAuditRow> recentAudit = await GetRecentAuditAsync(sectionId, cancellationToken);

        return new ForumModerationDashboardData(
            sections,
            threads,
            hiddenPosts,
            recentAudit,
            hiddenThreadCount,
            lockedThreadCount,
            pinnedThreadCount,
            hiddenPostCount,
            currentFilterLabel);
    }

    private async Task<IReadOnlyList<RecentAuditRow>> GetRecentAuditAsync(int? sectionId, CancellationToken cancellationToken)
    {
        List<AdminAuditRecord> recentAudit = await _db.AdminAuditRecords
            .AsNoTracking()
            .Where(x => x.ActionType.StartsWith("forum."))
            .OrderByDescending(x => x.Id)
            .Take(120)
            .ToListAsync(cancellationToken);

        HashSet<long> auditThreadIds = recentAudit
            .Where(x => x.TargetType == "ForumThread")
            .Select(x => TryParseTargetId(x.TargetId))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();

        HashSet<long> auditPostIds = recentAudit
            .Where(x => x.TargetType == "ForumPost")
            .Select(x => TryParseTargetId(x.TargetId))
            .Where(x => x.HasValue)
            .Select(x => x!.Value)
            .ToHashSet();

        Dictionary<long, AuditThreadTarget> auditThreads = await (
            from thread in _db.ForumThreads.AsNoTracking()
            join section in _db.ForumSections.AsNoTracking() on thread.SectionId equals section.Id
            where auditThreadIds.Contains(thread.Id)
            select new AuditThreadTarget(
                thread.Id,
                section.Id,
                section.Slug,
                section.Title,
                thread.Title))
            .ToDictionaryAsync(x => x.ThreadId, cancellationToken);

        Dictionary<long, AuditPostTarget> auditPosts = await (
            from post in _db.ForumPosts.AsNoTracking()
            join thread in _db.ForumThreads.AsNoTracking() on post.ThreadId equals thread.Id
            join section in _db.ForumSections.AsNoTracking() on thread.SectionId equals section.Id
            where auditPostIds.Contains(post.Id)
            select new AuditPostTarget(
                post.Id,
                thread.Id,
                section.Id,
                section.Slug,
                section.Title,
                thread.Title))
            .ToDictionaryAsync(x => x.PostId, cancellationToken);

        List<RecentAuditRow> recentAuditRows = [];
        foreach (AdminAuditRecord record in recentAudit)
        {
            RecentAuditRow? row = CreateAuditRow(record, sectionId, auditThreads, auditPosts);
            if (row != null)
                recentAuditRows.Add(row);

            if (recentAuditRows.Count >= 20)
                break;
        }

        return recentAuditRows;
    }

    private static long? TryParseTargetId(string? targetId)
    {
        return long.TryParse(targetId, out long parsed)
            ? parsed
            : null;
    }

    private static RecentAuditRow? CreateAuditRow(
        AdminAuditRecord record,
        int? sectionId,
        IReadOnlyDictionary<long, AuditThreadTarget> auditThreads,
        IReadOnlyDictionary<long, AuditPostTarget> auditPosts)
    {
        long? parsedTargetId = TryParseTargetId(record.TargetId);

        if (string.Equals(record.TargetType, "ForumThread", StringComparison.Ordinal) && parsedTargetId.HasValue)
        {
            if (auditThreads.TryGetValue(parsedTargetId.Value, out AuditThreadTarget? thread))
            {
                if (sectionId.HasValue && thread.SectionId != sectionId.Value)
                    return null;

                return new RecentAuditRow(
                    thread.SectionSlug,
                    thread.SectionTitle,
                    thread.ThreadId,
                    record.ActorEmail ?? record.ActorUserId,
                    record.ActorRole,
                    record.ActionType,
                    record.Result,
                    record.Reason,
                    thread.Title,
                    record.CreatedUtc);
            }

            if (sectionId.HasValue)
                return null;

            return new RecentAuditRow(
                null,
                null,
                null,
                record.ActorEmail ?? record.ActorUserId,
                record.ActorRole,
                record.ActionType,
                record.Result,
                record.Reason,
                $"Deleted thread #{parsedTargetId.Value}",
                record.CreatedUtc);
        }

        if (string.Equals(record.TargetType, "ForumPost", StringComparison.Ordinal) && parsedTargetId.HasValue)
        {
            if (auditPosts.TryGetValue(parsedTargetId.Value, out AuditPostTarget? post))
            {
                if (sectionId.HasValue && post.SectionId != sectionId.Value)
                    return null;

                return new RecentAuditRow(
                    post.SectionSlug,
                    post.SectionTitle,
                    post.ThreadId,
                    record.ActorEmail ?? record.ActorUserId,
                    record.ActorRole,
                    record.ActionType,
                    record.Result,
                    record.Reason,
                    $"Post #{post.PostId} in {post.ThreadTitle}",
                    record.CreatedUtc);
            }

            if (sectionId.HasValue)
                return null;

            return new RecentAuditRow(
                null,
                null,
                null,
                record.ActorEmail ?? record.ActorUserId,
                record.ActorRole,
                record.ActionType,
                record.Result,
                record.Reason,
                $"Deleted post #{parsedTargetId.Value}",
                record.CreatedUtc);
        }

        return sectionId.HasValue
            ? null
            : new RecentAuditRow(
                null,
                null,
                null,
                record.ActorEmail ?? record.ActorUserId,
                record.ActorRole,
                record.ActionType,
                record.Result,
                record.Reason,
                record.TargetType,
                record.CreatedUtc);
    }

    private sealed record ForumThreadRecord(
        long ThreadId,
        int SectionId,
        string SectionSlug,
        string SectionTitle,
        string Title,
        string AuthorUserId,
        bool IsPinned,
        bool IsLocked,
        bool IsHidden,
        DateTime LastPostedUtc,
        DateTime UpdatedUtc);

    private sealed record ForumPostRecord(
        long PostId,
        long ThreadId,
        string SectionSlug,
        string SectionTitle,
        string ThreadTitle,
        string AuthorUserId,
        DateTime CreatedUtc,
        DateTime UpdatedUtc);

    private sealed record AuditThreadTarget(
        long ThreadId,
        int SectionId,
        string SectionSlug,
        string SectionTitle,
        string Title);

    private sealed record AuditPostTarget(
        long PostId,
        long ThreadId,
        int SectionId,
        string SectionSlug,
        string SectionTitle,
        string ThreadTitle);
}

public sealed record ForumModerationDashboardData(
    IReadOnlyList<ForumSectionOption> Sections,
    IReadOnlyList<ModerationThreadRow> Threads,
    IReadOnlyList<ModerationPostRow> HiddenPosts,
    IReadOnlyList<RecentAuditRow> RecentAudit,
    int HiddenThreadCount,
    int LockedThreadCount,
    int PinnedThreadCount,
    int HiddenPostCount,
    string CurrentFilterLabel);

public sealed record ForumSectionOption(int Id, string Slug, string Title);

public sealed record ModerationThreadRow(
    long ThreadId,
    string SectionSlug,
    string SectionTitle,
    string Title,
    string AuthorName,
    string? AuthorBadge,
    string? AuthorBadgeCssClass,
    bool IsPinned,
    bool IsLocked,
    bool IsHidden,
    DateTime LastPostedUtc,
    DateTime UpdatedUtc);

public sealed record ModerationPostRow(
    long PostId,
    long ThreadId,
    string SectionSlug,
    string SectionTitle,
    string ThreadTitle,
    string AuthorName,
    string? AuthorBadge,
    string? AuthorBadgeCssClass,
    DateTime CreatedUtc,
    DateTime UpdatedUtc);

public sealed record RecentAuditRow(
    string? SectionSlug,
    string? SectionTitle,
    long? ThreadId,
    string Actor,
    string ActorRole,
    string ActionType,
    string Result,
    string Reason,
    string TargetLabel,
    DateTimeOffset CreatedUtc);
