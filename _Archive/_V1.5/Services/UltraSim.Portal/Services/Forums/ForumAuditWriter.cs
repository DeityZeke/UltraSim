using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services;

namespace UltraSim.Portal.Services.Forums;

public sealed class ForumAuditWriter
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AdminAuditService _adminAuditService;

    public ForumAuditWriter(
        UserManager<ApplicationUser> userManager,
        AdminAuditService adminAuditService)
    {
        _userManager = userManager;
        _adminAuditService = adminAuditService;
    }

    public async Task WriteModerationAuditAsync(
        ClaimsPrincipal actorPrincipal,
        string? actorIpAddress,
        string actionType,
        string targetType,
        string targetId,
        string reason,
        string? details,
        CancellationToken cancellationToken = default)
    {
        string? actorUserId = ForumAccessEvaluator.GetUserId(actorPrincipal);
        if (string.IsNullOrWhiteSpace(actorUserId))
            return;

        ApplicationUser? actor = await _userManager.FindByIdAsync(actorUserId);
        if (actor == null)
            return;

        string actorRole = await PortalRoleResolver.GetEffectiveRoleAsync(_userManager, actor);
        await _adminAuditService.WriteAsync(
            actorUserId: actor.Id,
            actorEmail: actor.Email,
            actorRole: actorRole,
            actionType: actionType,
            targetType: targetType,
            targetId: targetId,
            reason: reason,
            result: "Success",
            details: details,
            actorIpAddress: actorIpAddress,
            cancellationToken: cancellationToken);
    }
}
