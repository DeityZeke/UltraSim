using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using UltraSim.Discovery;
using UltraSim.Portal.Data;
using UltraSim.Portal.Models;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Pages.Account.Servers;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly RegistryQueryClient _registryQueryClient;
    private readonly UserManager<ApplicationUser> _userManager;

    public IndexModel(
        ApplicationDbContext db,
        RegistryQueryClient registryQueryClient,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _registryQueryClient = registryQueryClient;
        _userManager = userManager;
    }

    public IReadOnlyList<ServerClaim> Claims { get; private set; } = [];

    public async Task OnGetAsync(CancellationToken cancellationToken)
    {
        ApplicationUser? user = await _userManager.GetUserAsync(User);
        if (user == null)
            return;

        List<ServerClaim> claims = await _db.ServerClaims
            .AsNoTracking()
            .Where(x => x.OwnerUserId == user.Id)
            .ToListAsync(cancellationToken);

        claims = claims
            .OrderByDescending(x => x.UpdatedUtc.UtcDateTime)
            .ToList();

        Claims = claims;

        DateTimeOffset nowUtc = DateTimeOffset.UtcNow;

        List<ServerClaim> claimsWithServerId = Claims
            .Where(c => !string.IsNullOrWhiteSpace(c.RequestedServerId))
            .ToList();

        if (claimsWithServerId.Count > 0)
        {
            ServerDiscoveryDetail?[] details = await Task.WhenAll(
                claimsWithServerId.Select(async claim =>
                {
                    try
                    {
                        return await _registryQueryClient.GetServerAsync(claim.RequestedServerId!, cancellationToken);
                    }
                    catch
                    {
                        // Keep the portal-side record intact if the registry is temporarily unavailable.
                        return null;
                    }
                }));

            for (int i = 0; i < claimsWithServerId.Count; i++)
            {
                ServerClaim claim = claimsWithServerId[i];
                ServerDiscoveryDetail? detail = details[i];
                if (detail == null)
                    continue;

                if (detail.VerifiedAtUtc.HasValue)
                {
                    if (claim.Status != ServerClaimStatus.Verified || !string.IsNullOrWhiteSpace(claim.LastError))
                    {
                        claim.Status = ServerClaimStatus.Verified;
                        claim.LastError = null;
                        claim.UpdatedUtc = nowUtc;
                    }
                }
                else if (detail.OwnershipClaimRequestedUtc.HasValue && !string.IsNullOrWhiteSpace(detail.OwnershipVerificationError))
                {
                    if (claim.Status != ServerClaimStatus.Failed || !StringComparer.Ordinal.Equals(claim.LastError, detail.OwnershipVerificationError))
                    {
                        claim.Status = ServerClaimStatus.Failed;
                        claim.LastError = detail.OwnershipVerificationError;
                        claim.UpdatedUtc = nowUtc;
                    }
                }
                else if (detail.OwnershipClaimRequestedUtc.HasValue)
                {
                    if (claim.Status != ServerClaimStatus.PendingVerification || !string.IsNullOrWhiteSpace(claim.LastError))
                    {
                        claim.Status = ServerClaimStatus.PendingVerification;
                        claim.LastError = "Verification pending.";
                        claim.UpdatedUtc = nowUtc;
                    }
                }
            }
        }

    }
}
