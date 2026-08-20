using System;

namespace UltraSim.Server.Engines.Accounting
{
    /// <summary>
    /// Persistent character record. Immutable sealed record — mutation produces a new instance.
    /// CharacterId is stable across server restarts; the ECS entity handle is not.
    /// </summary>
    public sealed record CharacterModel(
        ulong CharacterId,
        ulong AccountId,
        byte SlotIndex,
        string Name,
        ushort ArchetypeId,
        DateTimeOffset CreatedUtc,
        DateTimeOffset LastPlayedUtc,
        float X,
        float Y,
        float Z,
        float YawRadians
    )
    {
        /// <summary>Current schema version for binary serialization.</summary>
        public const byte SchemaVersion = 2;

        public CharacterModel WithLastPlayed(DateTimeOffset utc) =>
            this with { LastPlayedUtc = utc };

        public CharacterModel WithSnapshot(
            DateTimeOffset utc,
            ushort archetypeId,
            float x,
            float y,
            float z,
            float yawRadians) =>
            this with
            {
                ArchetypeId = archetypeId,
                LastPlayedUtc = utc,
                X = x,
                Y = y,
                Z = z,
                YawRadians = yawRadians,
            };
    }
}
