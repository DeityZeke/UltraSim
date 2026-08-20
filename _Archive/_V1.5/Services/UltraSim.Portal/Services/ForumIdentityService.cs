using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;

namespace UltraSim.Portal.Services;

public sealed class ForumIdentityService
{
    private readonly ApplicationDbContext _db;

    public ForumIdentityService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<Dictionary<string, ForumAuthorProfile>> GetProfilesAsync(
        IEnumerable<string?> userIds,
        CancellationToken cancellationToken = default)
    {
        string[] ids = userIds
            .Where(static x => !string.IsNullOrWhiteSpace(x))
            .Select(static x => x!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (ids.Length == 0)
            return [];

        List<AuthorUserRow> users = await _db.Users
            .Where(x => ids.Contains(x.Id))
            .Select(x => new AuthorUserRow(
                x.Id,
                x.DisplayName,
                x.UserName,
                x.Email,
                x.IsPrimaryOwner))
            .ToListAsync(cancellationToken);

        List<AuthorRoleRow> roleRows = await (
            from userRole in _db.UserRoles
            join role in _db.Roles on userRole.RoleId equals role.Id
            where ids.Contains(userRole.UserId)
            select new AuthorRoleRow(userRole.UserId, role.Name))
            .ToListAsync(cancellationToken);

        Dictionary<string, string[]> rolesByUserId = roleRows
            .GroupBy(static x => x.UserId, StringComparer.Ordinal)
            .ToDictionary(
                static group => group.Key,
                static group => group
                    .Select(static x => x.RoleName)
                    .Where(static x => !string.IsNullOrWhiteSpace(x))
                    .Select(static x => x!)
                    .ToArray(),
                StringComparer.Ordinal);

        Dictionary<string, ForumAuthorProfile> profiles = new(StringComparer.Ordinal);
        for (int i = 0; i < users.Count; i++)
        {
            AuthorUserRow user = users[i];
            string effectiveRole = PortalRoles.ResolveEffectiveRole(
                rolesByUserId.GetValueOrDefault(user.Id) ?? [],
                user.IsPrimaryOwner);

            string displayName = ResolveDisplayName(user.DisplayName, user.UserName, user.Email, user.Id);
            profiles[user.Id] = new ForumAuthorProfile(
                user.Id,
                displayName,
                effectiveRole,
                GetBadgeLabel(effectiveRole),
                GetBadgeCssClass(effectiveRole));
        }

        return profiles;
    }

    private static string ResolveDisplayName(string? displayName, string? userName, string? email, string userId)
    {
        if (!string.IsNullOrWhiteSpace(displayName))
            return displayName.Trim();

        if (!string.IsNullOrWhiteSpace(userName))
            return userName.Trim();

        if (!string.IsNullOrWhiteSpace(email))
            return email.Trim();

        return userId;
    }

    private static string? GetBadgeLabel(string role)
    {
        return role switch
        {
            PortalRoles.PrimaryOwner => "Primary Owner",
            PortalRoles.Owner => "Owner",
            PortalRoles.Admin => "Admin",
            PortalRoles.Developer => "Developer",
            PortalRoles.Moderator => "Moderator",
            _ => null,
        };
    }

    private static string GetBadgeCssClass(string role)
    {
        return role switch
        {
            PortalRoles.PrimaryOwner => "forum-role-primary-owner",
            PortalRoles.Owner => "forum-role-owner",
            PortalRoles.Admin => "forum-role-admin",
            PortalRoles.Developer => "forum-role-developer",
            PortalRoles.Moderator => "forum-role-moderator",
            _ => "forum-role-user",
        };
    }

    private sealed record AuthorUserRow(
        string Id,
        string? DisplayName,
        string? UserName,
        string? Email,
        bool IsPrimaryOwner);

    private sealed record AuthorRoleRow(string UserId, string? RoleName);
}

public sealed record ForumAuthorProfile(
    string UserId,
    string DisplayName,
    string EffectiveRole,
    string? BadgeLabel,
    string BadgeCssClass);
