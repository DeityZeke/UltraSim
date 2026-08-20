using System;
using System.Collections.Generic;
using UltraSim.Shared.Gameplay;

namespace UltraSim.Server.Gameplay
{
    public sealed class ServerPlayerArchetypeCatalog
    {
        private readonly Dictionary<ushort, PlayerArchetypeDefinition> _definitionsById;

        public ServerPlayerArchetypeCatalog(
            IReadOnlyList<PlayerArchetypeDefinition> definitions,
            ushort defaultArchetypeId = PlayerArchetypeIds.Default)
        {
            ArgumentNullException.ThrowIfNull(definitions);
            if (definitions.Count == 0)
                throw new ArgumentException("At least one player archetype definition is required.", nameof(definitions));

            _definitionsById = new Dictionary<ushort, PlayerArchetypeDefinition>(definitions.Count);
            foreach (PlayerArchetypeDefinition definition in definitions)
            {
                _definitionsById[definition.ArchetypeId] = definition;
            }

            if (!_definitionsById.ContainsKey(defaultArchetypeId))
                throw new ArgumentException($"Default player archetype id '{defaultArchetypeId}' is not registered.", nameof(defaultArchetypeId));

            DefaultArchetypeId = defaultArchetypeId;
        }

        public ushort DefaultArchetypeId { get; }

        public IReadOnlyDictionary<ushort, PlayerArchetypeDefinition> DefinitionsById => _definitionsById;

        public PlayerArchetypeDefinition Resolve(ushort archetypeId)
        {
            if (_definitionsById.TryGetValue(archetypeId, out PlayerArchetypeDefinition? definition))
                return definition;

            return _definitionsById[DefaultArchetypeId];
        }

        public PlayerArchetypeDefinition ResolveSpawnDefinition(int ownerConnectionId, ushort? requestedArchetypeId = null)
        {
            if (requestedArchetypeId.HasValue)
                return Resolve(requestedArchetypeId.Value);

            // Preserve the current default parity behavior until account/character selection owns this choice.
            ushort fallbackId = (ownerConnectionId & 1) == 0
                ? PlayerArchetypeIds.DefaultFemale
                : DefaultArchetypeId;

            return Resolve(fallbackId);
        }

        public static ServerPlayerArchetypeCatalog CreateDefault(ushort defaultArchetypeId = PlayerArchetypeIds.Default)
        {
            return new ServerPlayerArchetypeCatalog(
                new[]
                {
                    new PlayerArchetypeDefinition(
                        PlayerArchetypeIds.DefaultMale,
                        "human_male_default",
                        "Human Male",
                        SharedActorDefinitionIds.PlayerMale),
                    new PlayerArchetypeDefinition(
                        PlayerArchetypeIds.DefaultFemale,
                        "human_female_default",
                        "Human Female",
                        SharedActorDefinitionIds.PlayerFemale),
                },
                defaultArchetypeId);
        }
    }
}
