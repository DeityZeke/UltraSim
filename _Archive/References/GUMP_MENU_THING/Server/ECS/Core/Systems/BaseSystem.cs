
using System;

using Godot;


public abstract class BaseSystem
{
    public abstract string Name { get; }

    public virtual void Enable() { }
    public virtual void Disable() { }
    public virtual void Update(double delta) { }
    public virtual void Save() { }
    public virtual void Load() { }
}