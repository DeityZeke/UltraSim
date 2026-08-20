using Microsoft.EntityFrameworkCore;
using UltraSim.Discovery.Official;
using UltraSim.Portal.Data;
using UltraSim.Portal.Services.RegistryApi;

namespace UltraSim.Portal.Services;

public sealed class VoteProjectionService
{
    private readonly ApplicationDbContext _db;
    private readonly RegistryQueryClient _registryQueryClient;

    public VoteProjectionService(
        ApplicationDbContext db,
        RegistryQueryClient registryQueryClient)
    {
        _db = db;
        _registryQueryClient = registryQueryClient;
    }

    public async Task<int> PushServerVoteCountAsync(string serverId, CancellationToken cancellationToken = default)
    {
        int voteCount = await _db.ServerVotes.CountAsync(x => x.ServerId == serverId, cancellationToken);

        await _registryQueryClient.UpdateVoteCountAsync(
            serverId,
            new OfficialRegistryVoteCountUpdateRequest(voteCount),
            cancellationToken);

        return voteCount;
    }
}
