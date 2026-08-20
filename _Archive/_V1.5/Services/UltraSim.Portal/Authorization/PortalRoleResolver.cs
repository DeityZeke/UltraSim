using Microsoft.AspNetCore.Identity;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Authorization;

public static class PortalRoleResolver
{
    public static async Task<string> GetEffectiveRoleAsync(
        UserManager<ApplicationUser> userManager,
        ApplicationUser user)
    {
        IList<string> roles = await userManager.GetRolesAsync(user);
        return PortalRoles.ResolveEffectiveRole(roles, user.IsPrimaryOwner);
    }

    public static string[] GetAssignableRolesFor(string actorRole)
    {
        return actorRole switch
        {
            PortalRoles.PrimaryOwner => PortalRoles.OwnerAssignableRoles,
            PortalRoles.Owner => PortalRoles.OwnerAssignableRoles,
            PortalRoles.Admin => PortalRoles.AdminAssignableRoles,
            _ => [PortalRoles.User],
        };
    }

    public static bool CanManageTarget(string actorRole, string targetRole, bool targetIsPrimaryOwner)
    {
        if (targetIsPrimaryOwner)
            return false;

        return actorRole switch
        {
            PortalRoles.PrimaryOwner => true,
            PortalRoles.Owner => targetRole != PortalRoles.PrimaryOwner,
            PortalRoles.Admin => targetRole is not PortalRoles.PrimaryOwner and not PortalRoles.Owner and not PortalRoles.Admin,
            _ => false,
        };
    }
}
