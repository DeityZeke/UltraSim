using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;

namespace UltraSim.Portal.Services;

public sealed class UserActivityTracker
{
    private static readonly TimeSpan UpdateInterval = TimeSpan.FromMinutes(15);

    private readonly ApplicationDbContext _db;

    public UserActivityTracker(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task TrackAsync(ClaimsPrincipal user, CancellationToken cancellationToken = default)
    {
        if (user.Identity?.IsAuthenticated != true)
            return;

        string? userId = user.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
            return;

        ApplicationUser? dbUser = await _db.Users.FirstOrDefaultAsync(x => x.Id == userId, cancellationToken);
        if (dbUser == null)
            return;

        DateTime nowUtc = DateTime.UtcNow;
        if (nowUtc - dbUser.LastActiveUtc < UpdateInterval)
            return;

        dbUser.LastActiveUtc = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);
    }
}
