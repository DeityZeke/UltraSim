using Microsoft.EntityFrameworkCore;
using UltraSim.Portal.Data;

namespace UltraSim.Portal.Services;

public sealed class PortalSensitiveDataCleanup
{
    private readonly ApplicationDbContext _db;

    public PortalSensitiveDataCleanup(ApplicationDbContext db)
    {
        _db = db;
    }

    public Task ClearPersistedOwnershipTokensAsync(CancellationToken cancellationToken = default)
    {
        // Ownership tokens are server secrets and should not remain in portal storage.
        return _db.Database.ExecuteSqlRawAsync(
            "UPDATE \"ServerClaims\" SET \"OwnershipToken\" = NULL WHERE \"OwnershipToken\" IS NOT NULL;",
            cancellationToken);
    }
}
