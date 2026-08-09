using UltraSim.ECS;
using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal static class V2WorldSupport
{
    internal static World CreateWorld()
    {
        var builder = new EcsSchemaBuilder();
        builder.RegisterComponent<Position>();
        builder.RegisterComponent<Velocity>();
        builder.RegisterComponent<Health>();
        builder.RegisterComponent<Scalar>();
        return new World(builder.Build());
    }

    internal static ArchetypeSignature Signature(World world, LogicalComponents components)
    {
        var ids = new List<ComponentId>(4);
        if ((components & LogicalComponents.Position) != 0)
            ids.Add(world.GetComponentId<Position>());
        if ((components & LogicalComponents.Velocity) != 0)
            ids.Add(world.GetComponentId<Velocity>());
        if ((components & LogicalComponents.Health) != 0)
            ids.Add(world.GetComponentId<Health>());
        if ((components & LogicalComponents.Scalar) != 0)
            ids.Add(world.GetComponentId<Scalar>());
        ids.Sort(static (left, right) => left.Value.CompareTo(right.Value));
        return new ArchetypeSignature(ids.ToArray());
    }

    internal static Entity[] Seed(World world, int count, LogicalComponents components)
    {
        ArchetypeSignature signature = Signature(world, components);
        var entities = new Entity[count];
        for (int ordinal = 0; ordinal < count; ordinal++)
        {
            Entity entity = world.CreateDefault(signature);
            entities[ordinal] = entity;
            if ((components & LogicalComponents.Position) != 0)
                world.SetComponent(entity, new Position(ordinal, ordinal + 1));
            if ((components & LogicalComponents.Velocity) != 0)
                world.SetComponent(entity, new Velocity(ordinal + 2, ordinal + 3));
            if ((components & LogicalComponents.Health) != 0)
                world.SetComponent(entity, new Health(ordinal + 4));
            if ((components & LogicalComponents.Scalar) != 0)
                world.SetComponent(entity, new Scalar(ordinal + 5));
        }
        DrainAllDirty(world, count);
        return entities;
    }

    internal static Entity[] CapturePositionEntities(World world, int expected)
    {
        var entities = new Entity[expected];
        int written = 0;
        foreach (World.QueryChunk<Position> chunk in world.QueryChunks<Position>())
        {
            for (int index = 0; index < chunk.Entities.Length; index++)
                entities[written++] = chunk.Entities[index];
        }
        if (written != expected)
            throw new InvalidOperationException($"Expected {expected} Position entities but found {written}.");
        Array.Sort(entities, static (left, right) => left.Index.CompareTo(right.Index));
        return entities;
    }

    internal static CanonicalState CaptureState(
        World world,
        ScenarioDescriptor descriptor,
        Entity[] entities,
        int lifecycleRevision = 0,
        bool[]? changed = null,
        int reusedSlots = 0)
    {
        int living = 0;
        for (int index = 0; index < entities.Length; index++)
        {
            if (world.Entities.IsAlive(entities[index]))
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
            bool alive = world.Entities.IsAlive(entity);
            LogicalComponents components = LogicalComponents.None;
            Position position = default;
            Velocity velocity = default;
            Health health = default;
            Scalar scalar = default;
            if (alive && world.TryGetComponent(entity, out position))
                components |= LogicalComponents.Position;
            if (alive && world.TryGetComponent(entity, out velocity))
                components |= LogicalComponents.Velocity;
            if (alive && world.TryGetComponent(entity, out health))
                components |= LogicalComponents.Health;
            if (alive && world.TryGetComponent(entity, out scalar))
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

    internal static void DrainAllDirty(World world, int count)
    {
        var scratch = new Entity[Math.Max(1, count)];
        _ = world.DrainDirtyTo(scratch);
        _ = world.DrainDirtyTo<Position>(scratch);
        _ = world.DrainDirtyTo<Velocity>(scratch);
        _ = world.DrainDirtyTo<Health>(scratch);
        _ = world.DrainDirtyTo<Scalar>(scratch);
    }
}
