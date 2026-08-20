
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

using Godot;

public static class AssemblyManager
{
    private static List<Assembly> m_Assemblies = new();
    private static bool m_Initialized = false;

    public static void Init()
    {
        if (m_Initialized)
            return;

        m_Assemblies.Clear();
        m_Assemblies.AddRange(AppDomain.CurrentDomain.GetAssemblies());
        m_Assemblies = m_Assemblies
                        .GroupBy(a => (a.FullName, a.Location))
                        .Select(g => g.First())
                        .ToList();
        m_Initialized = true;
    }

    public static void Invoke(string methodName)
    {
        Init();

        //foreach (var asm in m_Assemblies)
            //GD.Print($"[ASSEMBLY] {asm.FullName}, LOC: {asm.Location}, HASH: {asm.GetHashCode()}");

        var methods = new List<(MethodInfo Method, int Priority)>();

        foreach (var assembly in m_Assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
                if (method != null && method.GetParameters().Length == 0)
                {
                    var attr = method.GetCustomAttribute<SortPriorityAttribute>()
                        ?? type.GetCustomAttribute<SortPriorityAttribute>();
                    int priority = attr?.Priority ?? int.MaxValue;
                    methods.Add((method, priority));
                }
            }
        }

        foreach (var item in methods.OrderBy(m => m.Priority))
        {
            //GD.Print($"Invoking {item.Method.DeclaringType.FullName}.{item.Method.Name} from {item.Method.DeclaringType.Assembly.FullName} LOC: {item.Method.DeclaringType.Assembly.Location} HASH: {item.Method.DeclaringType.Assembly.GetHashCode()}");

            try
            {
                item.Method.Invoke(null, null);
            }
            catch (Exception ex)
            {
                GD.Print($"Error invoking {item.Method.DeclaringType.FullName}.{item.Method.Name}: {ex}");
            }
        }
    }

    public static void RegisterAssembly(Assembly assembly)
    {
        // Check uniqueness by FullName/Location, not just reference
        if (!m_Assemblies.Any(a => a.FullName == assembly.FullName && a.Location == assembly.Location))
            m_Assemblies.Add(assembly);
    }

    public static IReadOnlyList<Assembly> Assemblies => m_Assemblies.AsReadOnly();
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = false, AllowMultiple = false)]
public sealed class SortPriorityAttribute : Attribute
{
    public int Priority { get; }
    public SortPriorityAttribute(int priority)
    {
        Priority = priority;
    }
}