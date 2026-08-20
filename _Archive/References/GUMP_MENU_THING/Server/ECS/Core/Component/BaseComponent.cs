
using System;

using Godot;

public abstract class BaseComponent
{
    public EntityID? Owner { get; private set; }
    public int RefID { get; private set; }
    private static int m_NextRefID = 1;

    protected BaseComponent()
    {
        RefID = m_NextRefID++;
    }
    protected BaseComponent(EntityID owner) : this()
    {
        Owner = owner;
    }

    // Called when attached via ComponentManager
    internal void Attach(EntityID owner)
    {
        Owner = owner;
    }
    
    public virtual void Save() { }
    public virtual void Load() { }
}