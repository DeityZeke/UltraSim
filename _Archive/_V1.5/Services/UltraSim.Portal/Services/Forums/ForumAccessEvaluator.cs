using System.Security.Claims;
using UltraSim.Portal.Authorization;

namespace UltraSim.Portal.Services.Forums;

public static class ForumAccessEvaluator
{
    public static bool CanModerate(ClaimsPrincipal user)
    {
        return user.IsInRole(PortalRoles.PrimaryOwner)
            || user.IsInRole(PortalRoles.Owner)
            || user.IsInRole(PortalRoles.Admin)
            || user.IsInRole(PortalRoles.Moderator);
    }

    public static string? GetUserId(ClaimsPrincipal user)
    {
        return user.FindFirstValue(ClaimTypes.NameIdentifier);
    }
}
