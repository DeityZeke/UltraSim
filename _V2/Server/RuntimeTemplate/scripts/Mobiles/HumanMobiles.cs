using System;
using UltraSim.Server.Gameplay;
using UltraSim.Server.Scripting;

namespace UltraSim.Scripts.Mobiles;

public sealed class MobilesModule : IUltraSimScriptModule
{
    public void Configure(IUltraSimScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Definitions.RegisterArchetype<BaseMobile>();
        context.Definitions.RegisterArchetype<BaseHumanoid>();
        context.Definitions.RegisterArchetype<Human>();
        context.Definitions.RegisterPrefab<HumanMobile>();
        context.Logger.Info("Default human mobile prefab configured.");
    }

    public void Initialize(IUltraSimScriptContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        context.Logger.Info("Default human mobile prefab ready (1).");
    }
}

public class BaseMobile : ArchetypeShape
{
    public override string Key => "mobiles/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        builder.AddComponent<ServerMovementBody>();
        builder.AddComponent<BiologicalProfile>();
    }
}

public class BaseHumanoid : BaseMobile
{
    public override string Key => "mobiles/humanoid/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        base.Configure(builder);
        builder.AddComponent<HumanoidProfile>();
    }
}

public class Human : BaseHumanoid
{
    public override string Key => "mobiles/human/base";

    protected override void Configure(ArchetypeBuilder builder)
    {
        base.Configure(builder);
        builder.AddComponent<HumanProfile>();
    }
}

public sealed class HumanMobile : PrefabDefinition<Human>
{
    public override string Key => "mobiles/human";

    protected override void Configure(PrefabBuilder builder)
    {
        builder.Set(new ServerMovementBody(0f, 0f, 0f, 0f));
        builder.Set(new BiologicalProfile(BiologicalSex.Male, CanBreed: true, Maturity: 1f, LineageSeed: 0UL));
        builder.Set(new HumanoidProfile(SkeletonKind: 0));
        builder.Set(new HumanProfile(Variant: 0));
    }
}
