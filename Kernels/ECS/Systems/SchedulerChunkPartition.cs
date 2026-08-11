using System.Runtime.InteropServices;

namespace UltraSim.ECS;

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 16)]
internal readonly struct SchedulerChunkPartition
{
    internal SchedulerChunkPartition(int routeIndex, int firstChunkIndex, int chunkCount, int rowCount)
    {
        RouteIndex = routeIndex;
        FirstChunkIndex = firstChunkIndex;
        ChunkCount = chunkCount;
        RowCount = rowCount;
    }

    internal int RouteIndex { get; }
    internal int FirstChunkIndex { get; }
    internal int ChunkCount { get; }
    internal int RowCount { get; }
    internal bool IsValid => RouteIndex >= 0 && FirstChunkIndex >= 0 && ChunkCount > 0 && RowCount > 0;
}

[StructLayout(LayoutKind.Sequential, Pack = 4, Size = 8)]
internal readonly struct SchedulerQueryShape
{
    internal SchedulerQueryShape(int rows, int chunks) { Rows = rows; Chunks = chunks; }
    internal int Rows { get; }
    internal int Chunks { get; }
}
