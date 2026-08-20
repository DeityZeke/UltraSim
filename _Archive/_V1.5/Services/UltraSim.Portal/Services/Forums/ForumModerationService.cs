using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Authorization;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Services.Forums;

public sealed class ForumModerationService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly AdminAuditService _adminAuditService;

    public ForumModerationService(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        AdminAuditService adminAuditService)
    {
        _db = db;
        _userManager = userManager;
        _adminAuditService = adminAuditService;
    }

    public Task<ForumModerationActionResult?> ToggleThreadLockAsync(
        long threadId,
        string reason,
        ClaimsPrincipal actor,
        string? actorIpAddress,
        CancellationToken cancellationToken = default)
    {
        return ToggleThreadAsync(
            threadId,
            reason,
            actor,
            actorIpAddress,
            getState: static thread => thread.IsLocked,
            setState: static (thread, value) => thread.IsLocked = value,
            enabledActionType: "forum.thread.lock",
            disabledActionType: "forum.thread.unlock",
            enabledMessage: "Thread locked.",
            disabledMessage: "Thread unlocked.",
            cancellationToken);
    }

    public Task<ForumModerationActionResult?> ToggleThreadPinAsync(
        long threadId,
        string reason,
        ClaimsPrincipal actor,
        string? actorIpAddress,
        CancellationToken cancellationToken = default)
    {
        return ToggleThreadAsync(
            threadId,
            reason,
            actor,
            actorIpAddress,
            getState: static thread => thread.IsPinned,
            setState: static (thread, value) => thread.IsPinned = value,
            enabledActionType: "forum.thread.pin",
            disabledActionType: "forum.thread.unpin",
            enabledMessage: "Thread pinned.",
            disabledMessage: "Thread unpinned.",
            cancellationToken);
    }

    public Task<ForumModerationActionResult?> ToggleThreadHiddenAsync(
        long threadId,
        string reason,
        ClaimsPrincipal actor,
        string? actorIpAddress,
        CancellationToken cancellationToken = default)
    {
        return ToggleThreadAsync(
            threadId,
            reason,
            actor,
            actorIpAddress,
            getState: static thread => thread.IsHidden,
            setState: static (thread, value) => thread.IsHidden = value,
            enabledActionType: "forum.thread.hide",
            disabledActionType: "forum.thread.unhide",
            enabledMessage: "Thread hidden.",
            disabledMessage: "Thread restored.",
            cancellationToken);
    }

    public async Task<ForumModerationActionResult?> TogglePostHiddenAsync(
        long threadId,
        long postId,
        string reason,
        ClaimsPrincipal actor,
        string? actorIpAddress,
        CancellationToken cancellationToken = default)
    {
        ForumPost? post = await _db.ForumPosts.FirstOrDefaultAsync(
            x => x.Id == postId && x.ThreadId == threadId,
            cancellationToken);
        if (post == null)
            return null;

        post.IsHidden = !post.IsHidden;
        post.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await WriteModerationAuditAsync(
            actor,
            actionType: post.IsHidden ? "forum.post.hide" : "forum.post.unhide",
            targetType: "ForumPost",
            targetId: post.Id.ToString(),
            reason: reason,
            details: $"Thread {threadId} / Post {postId}",
            actorIpAddress: actorIpAddress,
            cancellationToken: cancellationToken);

        return new ForumModerationActionResult(post.IsHidden ? "Post hidden." : "Post restored.");
    }

    private async Task<ForumModerationActionResult?> ToggleThreadAsync(
        long threadId,
        string reason,
        ClaimsPrincipal actor,
        string? actorIpAddress,
        Func<ForumThread, bool> getState,
        Action<ForumThread, bool> setState,
        string enabledActionType,
        string disabledActionType,
        string enabledMessage,
        string disabledMessage,
        CancellationToken cancellationToken)
    {
        ForumThread? thread = await _db.ForumThreads.FirstOrDefaultAsync(x => x.Id == threadId, cancellationToken);
        if (thread == null)
            return null;

        bool nextState = !getState(thread);
        setState(thread, nextState);
        thread.UpdatedUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        await WriteModerationAuditAsync(
            actor,
            actionType: nextState ? enabledActionType : disabledActionType,
            targetType: "ForumThread",
            targetId: thread.Id.ToString(),
            reason: reason,
            details: thread.Title,
            actorIpAddress: actorIpAddress,
            cancellationToken: cancellationToken);

        return new ForumModerationActionResult(nextState ? enabledMessage : disabledMessage);
    }

    private async Task WriteModerationAuditAsync(
        ClaimsPrincipal actorPrincipal,
        string actionType,
        string targetType,
        string targetId,
        string reason,
        string? details,
        string? actorIpAddress,
        CancellationToken cancellationToken)
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

public sealed record ForumModerationActionResult(string StatusMessage);
