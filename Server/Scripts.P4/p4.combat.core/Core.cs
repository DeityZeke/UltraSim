using UltraSim.Framework.RuntimeHost;

namespace P4.Combat.Core;

public sealed class CombatCore : IRuntimePackageLifecycle
{
    public void Register(RuntimePackageContext context) =>
        context.Provide(new RuntimeServiceId("p4.combat.service"), new CombatService());
    public void Configure(RuntimePackageContext context) { }
    public void Initialize(RuntimePackageContext context) { }
    public void Stop() { }
}
public sealed class CombatService { }
