namespace UltraSim.Server.Gameplay
{
    public sealed record PlayerArchetypeDefinition(
        ushort ArchetypeId,
        string Key,
        string DisplayName,
        ushort ActorDefinitionId);
}
