namespace UltraSim.ECS;

/// <summary>Cold class-shaped authoring input compiled into an ECS prefab.</summary>
/// <remarks>
/// This object is constructed only during explicit content registration and is
/// never retained by the compiled catalog or spawned entities. Derived base
/// definitions call <c>base.Define(builder)</c> before adding their own monotonic
/// composition. Concrete Object Scripts normally override only Configure.
/// </remarks>
public abstract class BaseClass
{
    protected virtual void Define(BaseClassBuilder builder) { }

    protected virtual void Configure(PrefabBuilder prefab) { }

    internal void Build(ContentDefinitionState state)
    {
        Define(new BaseClassBuilder(state));
        Configure(new PrefabBuilder(state));
    }
}
