using System.Reflection;
using System.Collections.Concurrent;
using UltraSim.ECS;

namespace UltraSim.Server.Scripting;

internal delegate void ScriptPrefabComponentWrite(Archetype archetype, ComponentId componentId, int slot, object value);

internal static class ScriptPrefabComponentWriter
{
    private static readonly MethodInfo s_writeGenericMethod =
        typeof(ScriptPrefabComponentWriter).GetMethod(nameof(WriteGeneric), BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new MissingMethodException(nameof(ScriptPrefabComponentWriter), nameof(WriteGeneric));
    private static readonly ConcurrentDictionary<Type, ScriptPrefabComponentWrite> s_writers = new();

    public static ScriptPrefabComponentWrite Create(Type componentType)
    {
        ScriptDefinitionValidation.ValidateComponentType(componentType);
        return s_writers.GetOrAdd(componentType, CreateUncached);
    }

    private static ScriptPrefabComponentWrite CreateUncached(Type componentType)
    {
        MethodInfo closed = s_writeGenericMethod.MakeGenericMethod(componentType);
        return (ScriptPrefabComponentWrite)Delegate.CreateDelegate(typeof(ScriptPrefabComponentWrite), closed);
    }

    private static void WriteGeneric<T>(Archetype archetype, ComponentId componentId, int slot, object value)
        where T : unmanaged, IComponent<T>
    {
        if (value is not T typed)
        {
            throw new InvalidOperationException(
                $"Prefab component value for '{typeof(T).FullName}' had incompatible runtime type '{value?.GetType().FullName ?? "<null>"}'.");
        }

        archetype.GetComponentSpan<T>(componentId)[slot] = typed;
    }
}
