namespace UltraSim.ECS;

internal readonly record struct PersistentSystemStateRecord(
    ulong PersistenceId, ushort Version, bool Required, byte[] Bytes);

internal readonly record struct ParsedSystemState(
    ulong PersistenceId, ushort Version, bool Required, byte[] Bytes);
