namespace UltraSim.ECS;

/// <summary>
/// Cold tooling identity for the active ECS engineering checkpoint. This is not
/// a public API compatibility contract; see the version/evidence section in the
/// ECS README.
/// </summary>
internal static class EcsKernelVersion
{
    internal const int Generation = 3;
    internal const int Milestone = 4;
    internal const int Revision = 2;
    internal const string Value = "3.4.2";
    internal const string SemanticProfile =
        "ecs-3.4.2-scheduler-typed-payload-epoch-hardening";
}
