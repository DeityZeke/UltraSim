using System.Globalization;
using System.Reflection;

namespace UltraSim.ECS;

public sealed partial class World
{
    private static readonly MethodInfo s_inspectComponentMethod =
        typeof(World).GetMethod(nameof(InspectComponentTyped), BindingFlags.Static | BindingFlags.NonPublic)
        ?? throw new MissingMethodException(typeof(World).FullName, nameof(InspectComponentTyped));

    public bool TryInspectEntity(Entity entity, out EntityInspectionSnapshot snapshot)
    {
        if (!EntityStore.TryGetLocation(entity, out Archetype archetype, out _))
        {
            snapshot = new EntityInspectionSnapshot(entity, 0, []);
            return false;
        }

        ReadOnlySpan<ComponentId> componentIds = archetype.ComponentIds;
        var components = new EntityComponentInspectionSnapshot[componentIds.Length];
        for (int i = 0; i < componentIds.Length; i++)
        {
            ComponentId componentId = componentIds[i];
            ComponentTypeInfo info = Schema.GetComponentInfo(componentId);
            components[i] = InspectComponent(this, entity, componentId, info.ClrType);
        }

        snapshot = new EntityInspectionSnapshot(entity, componentIds.Length, components);
        return true;
    }

    public EntityInspectionSnapshot InspectEntity(Entity entity)
    {
        if (!TryInspectEntity(entity, out EntityInspectionSnapshot snapshot))
            throw new InvalidOperationException($"Entity not alive: {entity}");

        return snapshot;
    }

    private static EntityComponentInspectionSnapshot InspectComponent(
        World world,
        Entity entity,
        ComponentId componentId,
        Type componentType)
    {
        MethodInfo typed = s_inspectComponentMethod.MakeGenericMethod(componentType);
        return (EntityComponentInspectionSnapshot)typed.Invoke(null, [world, entity, componentId])!;
    }

    private static EntityComponentInspectionSnapshot InspectComponentTyped<T>(
        World world,
        Entity entity,
        ComponentId componentId)
        where T : unmanaged, IComponent<T>
    {
        if (!world.TryGetComponent(entity, out T value))
        {
            string missingTypeName = typeof(T).FullName ?? typeof(T).Name;
            return new EntityComponentInspectionSnapshot(
                componentId,
                missingTypeName,
                typeof(T).Name,
                "<missing>",
                []);
        }

        return new EntityComponentInspectionSnapshot(
            componentId,
            typeof(T).FullName ?? typeof(T).Name,
            typeof(T).Name,
            FormatInspectionValue(value),
            CapturePublicFieldSnapshots(value));
    }

    private static EntityComponentFieldInspectionSnapshot[] CapturePublicFieldSnapshots<T>(T value)
        where T : unmanaged, IComponent<T>
    {
        Type componentType = typeof(T);
        PropertyInfo[] properties = componentType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
        FieldInfo[] fields = componentType.GetFields(BindingFlags.Instance | BindingFlags.Public);
        var snapshots = new List<EntityComponentFieldInspectionSnapshot>(properties.Length + fields.Length);

        for (int i = 0; i < properties.Length; i++)
        {
            PropertyInfo property = properties[i];
            if (property.GetIndexParameters().Length != 0 || property.GetMethod is null)
                continue;

            snapshots.Add(new EntityComponentFieldInspectionSnapshot(
                property.Name,
                FormatTypeName(property.PropertyType),
                FormatInspectionValue(property.GetValue(value))));
        }

        for (int i = 0; i < fields.Length; i++)
        {
            FieldInfo field = fields[i];
            snapshots.Add(new EntityComponentFieldInspectionSnapshot(
                field.Name,
                FormatTypeName(field.FieldType),
                FormatInspectionValue(field.GetValue(value))));
        }

        snapshots.Sort(static (left, right) => string.CompareOrdinal(left.Name, right.Name));
        return snapshots.ToArray();
    }

    private static string FormatTypeName(Type type)
    {
        if (!type.IsGenericType)
            return type.FullName ?? type.Name;

        return type.Name;
    }

    private static string FormatInspectionValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => text,
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty,
        };
    }
}
