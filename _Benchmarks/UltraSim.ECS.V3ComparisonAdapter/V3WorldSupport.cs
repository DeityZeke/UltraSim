using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V3ComparisonAdapter;

internal static class V3WorldSupport
{
    internal static ComponentSchema Schema => ComparisonSchema.CreateSchema();

    internal static ComponentId PositionId => ComparisonSchema.GetComponentId<Position>();

    internal static ComponentId VelocityId => ComparisonSchema.GetComponentId<Velocity>();

    internal static ComponentId HealthId => ComparisonSchema.GetComponentId<Health>();

    internal static ComponentId ScalarId => ComparisonSchema.GetComponentId<Scalar>();

    internal static ComponentId WideId => ComparisonSchema.GetComponentId<WideBlock64>();

    internal static ComponentId[] Signature(LogicalComponents components)
    {
        var ids = new List<ComponentId>(5);
        if ((components & LogicalComponents.Position) != 0)
            ids.Add(PositionId);
        if ((components & LogicalComponents.Velocity) != 0)
            ids.Add(VelocityId);
        if ((components & LogicalComponents.Health) != 0)
            ids.Add(HealthId);
        if ((components & LogicalComponents.Scalar) != 0)
            ids.Add(ScalarId);
        if ((components & LogicalComponents.Wide) != 0)
            ids.Add(WideId);
        ids.Sort(static (left, right) => left.Value.CompareTo(right.Value));
        return ids.ToArray();
    }

    internal static int MaximumChunks(ComponentSchema schema, ComponentId[] signature, int population)
    {
        Span<ComponentColumnLayout> columns = stackalloc ComponentColumnLayout[signature.Length];
        ChunkLayoutPlan plan = ChunkLayoutPlanner.Plan(schema, signature, ChunkLayoutOptions.KiB16, columns);
        return Math.Max(1, (population + plan.RowCapacity - 1) / plan.RowCapacity);
    }

    internal static WideBlock64 CreateWide(int ordinal) => new()
    {
        Value0 = ordinal + 101L,
        Value1 = ordinal * 3L + 103L,
        Value2 = ordinal * 5L + 107L,
        Value3 = ordinal * 7L + 109L,
        Value4 = ordinal * 11L + 113L,
        Value5 = ordinal * 13L + 127L,
        Value6 = ordinal * 17L + 131L,
        Value7 = ordinal * 19L + 137L,
    };

    internal static CanonicalState CaptureFacadeState(
        EcsWorld world,
        ScenarioDescriptor descriptor,
        Entity[] entities,
        in ComponentLookup<Position> positions,
        in ComponentLookup<Velocity> velocities,
        in ComponentLookup<Health> healths,
        in ComponentLookup<Scalar> scalars,
        int lifecycleRevision = 0,
        bool[]? changed = null,
        int reusedSlots = 0)
    {
        int living = 0;
        for (int index = 0; index < entities.Length; index++)
        {
            if (world.IsAlive(entities[index]))
                living++;
        }
        int changedCount = changed?.Count(static value => value) ?? 0;
        var builder = new LogicalStateBuilder(
            descriptor.Id,
            descriptor.Scale,
            living,
            living == 0 ? 0 : 1,
            changedCount,
            reusedSlots);
        LogicalComponents populatedSignature = LogicalComponents.None;
        for (int ordinal = 0; ordinal < entities.Length; ordinal++)
        {
            Entity entity = entities[ordinal];
            bool alive = world.IsAlive(entity);
            LogicalComponents components = LogicalComponents.None;
            Position position = default;
            Velocity velocity = default;
            Health health = default;
            Scalar scalar = default;
            if (alive && world.TryRead(entity, positions, out position))
                components |= LogicalComponents.Position;
            if (alive && world.TryRead(entity, velocities, out velocity))
                components |= LogicalComponents.Velocity;
            if (alive && world.TryRead(entity, healths, out health))
                components |= LogicalComponents.Health;
            if (alive && world.TryRead(entity, scalars, out scalar))
                components |= LogicalComponents.Scalar;
            if (alive)
                populatedSignature = components;
            builder.AddEntity(
                ordinal,
                alive,
                lifecycleRevision,
                components,
                position.X,
                position.Y,
                velocity.X,
                velocity.Y,
                health.Value,
                scalar.Value);
        }
        if (living != 0)
            builder.AddArchetype(populatedSignature, living);
        if (changed is not null)
        {
            for (int ordinal = 0; ordinal < changed.Length; ordinal++)
            {
                if (changed[ordinal])
                    builder.AddChangedEntity(ordinal);
            }
        }
        return builder.Finish();
    }
}
