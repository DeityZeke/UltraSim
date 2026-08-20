using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace UltraSim.Discovery
{
    public sealed class InMemoryServerDiscoveryQuery : IServerDiscoveryQuery
    {
        private IReadOnlyList<ServerDiscoveryRow> _rows;

        public InMemoryServerDiscoveryQuery(IReadOnlyList<ServerDiscoveryRow>? initialRows = null)
        {
            _rows = initialRows ?? Array.Empty<ServerDiscoveryRow>();
        }

        public ValueTask<IReadOnlyList<ServerDiscoveryRow>> QueryAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(_rows);

        public void ReplaceRows(IReadOnlyList<ServerDiscoveryRow>? rows)
        {
            _rows = rows?.ToArray() ?? Array.Empty<ServerDiscoveryRow>();
        }
    }
}
