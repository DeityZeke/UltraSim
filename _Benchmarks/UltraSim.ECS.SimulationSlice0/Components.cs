using UltraSim.ECS;

namespace UltraSim.ECS.SimulationSlice0;

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

[EcsComponent("simulation-slice-0.tier-0", IsTag = true)]
internal struct Tier0Marker : IComponent<Tier0Marker> { }

[EcsComponent("simulation-slice-0.tier-1", IsTag = true)]
internal struct Tier1Marker : IComponent<Tier1Marker> { }

[EcsComponent("simulation-slice-0.tier-2", IsTag = true)]
internal struct Tier2Marker : IComponent<Tier2Marker> { }

[EcsComponent("simulation-slice-0.tier-3", IsTag = true)]
internal struct Tier3Marker : IComponent<Tier3Marker> { }

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
