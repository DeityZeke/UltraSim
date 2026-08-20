using System;
using System.Collections.Generic;

namespace UltraSim.Server.Engines.Chunking
{
    public readonly record struct ChunkCoord(int X, int Z)
    {
        public static ChunkCoord FromGridPosition(int gridX, int gridZ, int chunkSizeGridUnits)
        {
            if (chunkSizeGridUnits <= 0)
                throw new ArgumentOutOfRangeException(nameof(chunkSizeGridUnits));

            return new ChunkCoord(
                FloorDiv(gridX, chunkSizeGridUnits),
                FloorDiv(gridZ, chunkSizeGridUnits));
        }

        public static void FillSquare(in ChunkCoord center, int radius, ISet<ChunkCoord> destination)
        {
            ArgumentNullException.ThrowIfNull(destination);

            int r = Math.Max(0, radius);
            for (int z = center.Z - r; z <= center.Z + r; z++)
            {
                for (int x = center.X - r; x <= center.X + r; x++)
                    destination.Add(new ChunkCoord(x, z));
            }
        }

        private static int FloorDiv(int value, int divisor)
        {
            int q = value / divisor;
            int r = value % divisor;
            if (r != 0 && ((r < 0) != (divisor < 0)))
                q--;
            return q;
        }
    }
}
