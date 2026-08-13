using UltraSim.ECS;

namespace UltraSim.Server.Runtime;

/// <summary>
/// Game-composition seam used to configure one private server generation.
/// </summary>
/// <remarks>
/// The runtime owns construction, sealing, lifecycle, publication, ticking, and
/// disposal. Composition supplies the schema and registers game systems into a
/// cold builder; it does not receive or publish the generation root. The schema
/// is captured once when the runtime is constructed. The builder and its world
/// are cold callback-scoped configuration capabilities: implementations must not
/// retain them as an alternate active-world authority.
/// </remarks>
public interface IServerComposition
{
    ComponentSchema Schema { get; }

    /// <summary>Configures one private generation candidate before validation and publication.</summary>
    /// <remarks>
    /// This method may run again for later candidate generations. Authoritative
    /// mutable state belongs to the candidate world/system runtimes, not to the
    /// long-lived composition object.
    /// </remarks>
    void Configure(ServerGenerationBuilder generation);
}
