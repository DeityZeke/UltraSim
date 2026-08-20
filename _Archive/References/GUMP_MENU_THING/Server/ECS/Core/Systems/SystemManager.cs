using System;
using System.Collections.Generic;
using System.Diagnostics;
using Godot;

public static class SystemManager
{
    private static readonly Dictionary<Type, BaseSystem> m_Systems = new();
    private static readonly List<Type> m_EnabledSystems = new();

    public static void RegisterSystem<T>(bool enabled = true) where T : BaseSystem, new()
    {
        var type = typeof(T);
        if (!m_Systems.ContainsKey(type))
        {
            var system = new T();
            m_Systems[type] = system;
            if (enabled)
                EnableSystem<T>();
        }
    }

    public static bool IsSystemEnabled<T>() where T : BaseSystem
    {
        return m_EnabledSystems.Contains(typeof(T));
    }

    public static bool IsSystemRegistered<T>() where T : BaseSystem
    {
        return m_Systems.ContainsKey(typeof(T));
    }

    public static bool IsSystemRegistered(Type type)
    {
        return m_Systems.ContainsKey(type);
    }

    public static T GetSystem<T>() where T : BaseSystem
    {
        var type = typeof(T);
        if (m_Systems.TryGetValue(type, out var system))
            return (T)system;

        throw new KeyNotFoundException($"[SYSTEM] '{type.FullName}' Not Registered");
    }

    public static void EnableSystem<T>() where T : BaseSystem
    {
        var type = typeof(T);
        GD.Print($"[SYSTEM] '{type.FullName}' Enabling...");

        if (!m_Systems.ContainsKey(type))
            throw new KeyNotFoundException($"[SYSTEM]] '{type.FullName}' Not Registered.");

        if (!m_EnabledSystems.Contains(type))
        {
            m_EnabledSystems.Add(type);
            m_Systems[type].Enable();
            GD.Print($"[SYSTEM] '{type.FullName}' Enabled");
        }
        else
        {
            GD.Print($"FAILED! System Already Enabled.");
        }
    }

    public static void EnableSystems()
    {
    }

    public static void DisableSystem<T>() where T : BaseSystem
    {
        var type = typeof(T);
        if (!m_Systems.ContainsKey(type))
            throw new KeyNotFoundException($"[SYSTEM]] '{type.FullName}' Not Registered.");

        if (!m_EnabledSystems.Contains(type))
            m_Systems[type].Disable();
    }

    public static void UpdateAll(double delta)
    {
        foreach (var type in m_EnabledSystems)
        {
            if (m_Systems.TryGetValue(type.GetType(), out var system))
                system.Update(delta);
        }
    }

    public static void SaveAll()
    {
        foreach (var system in m_Systems.Values)
        {
            system.Save();
        }
    }

    public static void LoadAll()
    {
        foreach (var system in m_Systems.Values)
        {
            system.Load();
        }
    }
}