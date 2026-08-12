using UltraSim.ECS;

namespace UltraSim.ECS.SimulationSlice0;

internal interface ITierMarker
{
    int LogicalId { get; }
}

[EcsComponent("simulation-slice-0.logical-identity")]
internal struct LogicalIdentity : IComponent<LogicalIdentity>
{
    public int Value { get; set; }
}

[EcsComponent("simulation-slice-0.position")]
internal struct Position : IComponent<Position>
{
    public int X { get; set; }
    public int Y { get; set; }
}

[EcsComponent("simulation-slice-0.velocity")]
internal struct Velocity : IComponent<Velocity>
{
    public int X { get; set; }
    public int Y { get; set; }
}

[EcsComponent("simulation-slice-0.needs")]
internal struct Needs : IComponent<Needs>
{
    public int Hunger { get; set; }
    public int Fatigue { get; set; }
}

[EcsComponent("simulation-slice-0.activity")]
internal struct Activity : IComponent<Activity>
{
    public int Value { get; set; }
}

[EcsComponent("simulation-slice-0.health")]
internal struct Health : IComponent<Health>
{
    public int Value { get; set; }
}

[EcsComponent("simulation-slice-0.cooldown")]
internal struct Cooldown : IComponent<Cooldown>
{
    public int Value { get; set; }
}

[EcsComponent("simulation-slice-0.target")]
internal struct Target : IComponent<Target>
{
    public int LogicalId { get; set; }
}

[EcsComponent("simulation-slice-0.spatial-cell")]
internal struct SpatialCell : IComponent<SpatialCell>
{
    public int X { get; set; }
    public int Y { get; set; }
}

// These are data-bearing query keys rather than tags. ECS 3.1.2 generated data
// query plans deliberately reject storage-free tag columns and the public
// generated facade does not yet expose filter authoring.
[EcsComponent("simulation-slice-0.tier-0")]
internal struct Tier0Marker : IComponent<Tier0Marker>, ITierMarker
{
    public int LogicalId { get; set; }
}

[EcsComponent("simulation-slice-0.tier-1")]
internal struct Tier1Marker : IComponent<Tier1Marker>, ITierMarker
{
    public int LogicalId { get; set; }
}

[EcsComponent("simulation-slice-0.tier-2")]
internal struct Tier2Marker : IComponent<Tier2Marker>, ITierMarker
{
    public int LogicalId { get; set; }
}

[EcsComponent("simulation-slice-0.tier-3")]
internal struct Tier3Marker : IComponent<Tier3Marker>, ITierMarker
{
    public int LogicalId { get; set; }
}

[EcsComponent("simulation-slice-0.dead-state")]
internal struct DeadState : IComponent<DeadState>
{
    public int Revision { get; set; }
}

[EcsComponent("simulation-slice-0.effect-identity")]
internal struct EffectIdentity : IComponent<EffectIdentity>
{
    public int Slot { get; set; }
    public int Generation { get; set; }
}

[EcsComponent("simulation-slice-0.lifetime")]
internal struct Lifetime : IComponent<Lifetime>
{
    public int Remaining { get; set; }
}

[EcsComponent("simulation-slice-0.world-item-state")]
internal struct WorldItemState : IComponent<WorldItemState>
{
    public int Value { get; set; }
}

[EcsSchema(
    typeof(LogicalIdentity),
    typeof(Position),
    typeof(Velocity),
    typeof(Needs),
    typeof(Activity),
    typeof(Health),
    typeof(Cooldown),
    typeof(Target),
    typeof(SpatialCell),
    typeof(Tier0Marker),
    typeof(Tier1Marker),
    typeof(Tier2Marker),
    typeof(Tier3Marker),
    typeof(DeadState),
    typeof(EffectIdentity),
    typeof(Lifetime),
    typeof(WorldItemState))]
internal static partial class SimulationSchema
{
}
