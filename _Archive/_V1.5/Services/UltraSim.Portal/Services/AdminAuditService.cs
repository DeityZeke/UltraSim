using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Services;

public sealed class AdminAuditService
{
    private readonly ApplicationDbContext _db;

    public AdminAuditService(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task WriteAsync(
        string actorUserId,
        string? actorEmail,
        string actorRole,
        string actionType,
        string targetType,
        string? targetId,
        string reason,
        string result,
        string? details = null,
        string? actorIpAddress = null,
        CancellationToken cancellationToken = default)
    {
        _db.AdminAuditRecords.Add(new AdminAuditRecord
        {
            ActorUserId = actorUserId,
            ActorEmail = string.IsNullOrWhiteSpace(actorEmail) ? null : actorEmail.Trim(),
            ActorRole = actorRole,
            ActionType = actionType,
            TargetType = targetType,
            TargetId = string.IsNullOrWhiteSpace(targetId) ? null : targetId.Trim(),
            Reason = reason.Trim(),
            Result = result.Trim(),
            Details = string.IsNullOrWhiteSpace(details) ? null : details.Trim(),
            ActorIpAddress = string.IsNullOrWhiteSpace(actorIpAddress) ? null : actorIpAddress.Trim(),
            CreatedUtc = DateTimeOffset.UtcNow,
        });

        await _db.SaveChangesAsync(cancellationToken);
    }
}
