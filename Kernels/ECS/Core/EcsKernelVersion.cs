namespace UltraSim.ECS;

/// <summary>
/// Cold tooling identity for the active ECS engineering checkpoint. This is not
/// yet a public API compatibility contract; see ECS_VERSIONING.md.
/// </summary>
internal static class EcsKernelVersion
{
    internal const int Generation = 3;
    internal const int Milestone = 3;
    internal const int Revision = 0;
    internal const string Value = "3.4.0";
    internal const string SemanticProfile =
        "ecs-3.4.0-full-world-transactional-persistence";
}
