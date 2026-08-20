using System;
using System.Collections.Generic;

namespace UltraSim.Server.Engines.Chunking
{
    public sealed class SpatialChunkIndex
    {
        private readonly Dictionary<ChunkCoord, SpatialChunk> _chunkByCoord;
        private readonly Dictionary<ulong, ChunkCoord> _chunkByEntityId;

        public SpatialChunkIndex(int chunkSizeGridUnits)
        {
            if (chunkSizeGridUnits <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSizeGridUnits));

            ChunkSizeGridUnits = chunkSizeGridUnits;
            _chunkByCoord = new Dictionary<ChunkCoord, SpatialChunk>(128);
            _chunkByEntityId = new Dictionary<ulong, ChunkCoord>(256);
        }

        public int ChunkSizeGridUnits { get; }

        public int ActiveChunkCount => _chunkByCoord.Count;

        public int TrackedEntityCount => _chunkByEntityId.Count;

        public void Clear()
        {
            _chunkByCoord.Clear();
            _chunkByEntityId.Clear();
        }

        public ChunkCoord GetChunkForGridPosition(int gridX, int gridZ) =>
            ChunkCoord.FromGridPosition(gridX, gridZ, ChunkSizeGridUnits);

        public bool TryGetChunkCoord(ulong networkEntityId, out ChunkCoord coord) =>
            _chunkByEntityId.TryGetValue(networkEntityId, out coord);

        public bool TryGetChunk(in ChunkCoord coord, out SpatialChunk chunk) =>
            _chunkByCoord.TryGetValue(coord, out chunk!);

        public bool UpsertEntity(ulong networkEntityId, in ChunkCoord coord)
        {
            if (_chunkByEntityId.TryGetValue(networkEntityId, out ChunkCoord existing) && existing == coord)
                return false;

            if (_chunkByEntityId.TryGetValue(networkEntityId, out existing))
            {
                if (_chunkByCoord.TryGetValue(existing, out SpatialChunk? oldChunk))
                {
                    oldChunk.Remove(networkEntityId);
                    if (oldChunk.MemberCount == 0)
                        _chunkByCoord.Remove(existing);
                }
            }

            if (!_chunkByCoord.TryGetValue(coord, out SpatialChunk? newChunk))
            {
                newChunk = new SpatialChunk();
                _chunkByCoord.Add(coord, newChunk);
            }

            newChunk.Add(networkEntityId);
            _chunkByEntityId[networkEntityId] = coord;
            return true;
        }

        public bool RemoveEntity(ulong networkEntityId)
        {
            if (!_chunkByEntityId.TryGetValue(networkEntityId, out ChunkCoord coord))
                return false;

            _chunkByEntityId.Remove(networkEntityId);
            if (_chunkByCoord.TryGetValue(coord, out SpatialChunk? chunk))
            {
                chunk.Remove(networkEntityId);
                if (chunk.MemberCount == 0)
                    _chunkByCoord.Remove(coord);
            }

            return true;
        }

        public void CollectEntitiesInSquare(in ChunkCoord center, int radius, ISet<ulong> destination)
        {
            ArgumentNullException.ThrowIfNull(destination);

            int r = Math.Max(0, radius);
            for (int z = center.Z - r; z <= center.Z + r; z++)
            {
                for (int x = center.X - r; x <= center.X + r; x++)
                {
                    if (!_chunkByCoord.TryGetValue(new ChunkCoord(x, z), out SpatialChunk? chunk))
                        continue;

                    foreach (ulong entityId in chunk.Members)
                        destination.Add(entityId);
                }
            }
        }
    }
}
