namespace UltraSim.ECS;

public enum ChunkArenaMemoryKind : byte
{
    NativeAligned = 1,
    PinnedManaged = 2,
}
