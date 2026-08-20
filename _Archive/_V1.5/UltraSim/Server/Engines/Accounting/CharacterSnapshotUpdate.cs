using System;

namespace UltraSim.Server.Engines.Accounting
{
    public readonly record struct CharacterSnapshotUpdate(
        ulong CharacterId,
        DateTimeOffset LastPlayedUtc,
        ushort ArchetypeId,
        float X,
        float Y,
        float Z,
        float YawRadians);
}
