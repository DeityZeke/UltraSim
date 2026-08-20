using UltraSim.ECS.Persistence;

namespace UltraSim.ECS
{
    /// <summary>
    /// Zero-payload marker component that opts an entity into world persistence.
    /// Only entities carrying this component are saved by the persistence system.
    ///
    /// Rules:
    /// - Zero bytes on the wire — <see cref="Serialize"/> writes nothing, <see cref="Deserialize"/> reads nothing.
    /// - Must survive save/load round-trips so loaded entities remain save-eligible.
    /// - Register with <see cref="ComponentManager"/> and <see cref="PersistenceTypeRegistry"/>
    ///   during startup, before the registries are sealed.
    /// </summary>
    [PersistenceId(1u)]
    public readonly struct PersistentTag : ISerializableComponent<PersistentTag>
    {
        // Zero-payload: no fields.

        public static void Serialize(ref BitWriter writer, in PersistentTag value)
        {
            // Intentionally empty — PersistentTag carries no data.
        }

        public static PersistentTag Deserialize(ref BitReader reader, ushort version)
        {
            // Intentionally empty — no bytes to consume.
            return default;
        }
    }
}
