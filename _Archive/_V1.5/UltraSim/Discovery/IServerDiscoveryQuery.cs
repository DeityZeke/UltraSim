using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace UltraSim.Discovery
{
    public interface IServerDiscoveryQuery
    {
        ValueTask<IReadOnlyList<ServerDiscoveryRow>> QueryAsync(
            CancellationToken cancellationToken = default);
    }
}
