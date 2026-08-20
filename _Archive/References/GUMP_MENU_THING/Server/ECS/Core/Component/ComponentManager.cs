
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Godot;

public class ComponentManager : BaseSystem
{
    public static void Initialize()
    {
        SystemManager.RegisterSystem<ComponentManager>();
    }

    public static bool Running => SystemManager.IsSystemEnabled<ComponentManager>();

    private Dictionary<EntityID, List<BaseComponent>> m_ComponentsByEntity = new();

    public override string Name => "Component Manager";

    public override void Enable()
    {
        //Console.WriteLine($"{Name} Enabled!");
        //Debug.WriteLine($"{Name} Enabled!");
    }

    public override void Disable()
    {
        //Console.WriteLine($"{Name} Disabled!");
        //Debug.WriteLine($"{Name} Disabled!");
    }

    public override void Save()
    {
        // TODO: Serialize all components to disk or memory
    }

    public override void Load()
    {
        // TODO: Deserialize and rebuild all components from disk or memory
    }

    public override void Update(double delta)
    {
        /*
        foreach (var componentList in m_ComponentsByEntity.Values)
            foreach (var component in componentList)
                component.Update(delta);
                */
    }

    public void AddComponent(EntityID entityId, BaseComponent component)
    {
        if (!m_ComponentsByEntity.TryGetValue(entityId, out var componentList))
        {
            componentList = new List<BaseComponent>();
            m_ComponentsByEntity[entityId] = componentList;
        }
        componentList.Add(component);
        component.Attach(entityId);
    }

    public void RemoveComponent(EntityID entityId, BaseComponent component)
    {
        if (m_ComponentsByEntity.TryGetValue(entityId, out var componentList))
            componentList.Remove(component);
    }

    public IEnumerable<BaseComponent> GetComponents(EntityID entityId)
    {
        return m_ComponentsByEntity.TryGetValue(entityId, out var componentList) ? componentList : Enumerable.Empty<BaseComponent>();
    }

    public IEnumerable<T> GetComponentsOfType<T>() where T : BaseComponent
    {
        foreach (var componentList in m_ComponentsByEntity.Values)
            foreach (var component in componentList)
                if (component is T tComp)
                    yield return tComp;
    }
}