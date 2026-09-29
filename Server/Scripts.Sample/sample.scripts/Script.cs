using Sample.Contracts;
using UltraSim.Framework.RuntimeHost;
using UltraSim.Framework.Definitions;

namespace Sample;

public sealed class ScriptEntry : IRuntimePackageLifecycle
{
    private readonly Consumer _consumer = new();
    public void Register(RuntimePackageContext context) =>
        context.Provide(new RuntimeServiceId("sample.consumer"), _consumer);
    public void Configure(RuntimePackageContext context)
    {
        _consumer.ContractsType = typeof(IGreeting).FullName!;
        _consumer.ContractsAssembly = typeof(IGreeting).Assembly.FullName!;
        _consumer.HasCore = context.TryGet<object>(new RuntimeServiceId(SampleIdentity.Greeting), out _);
        var composer = new DefinitionComposer();
        composer.RegisterFacet(SampleDefinitionFacets.Combat);
        var item = new StableDefinitionId("sample.item.iron-sword");
        var owner = new DefinitionPackageId("sample.scripts");
        composer.Define(owner, item).With(owner, SampleDefinitionFacets.Combat, new SwordCombatFacet(2, 6));
        composer.ContributeTag(owner, new DefinitionTagId("sample.weapons.swords"), item);
        _consumer.Definitions = composer.Freeze(_consumer.HasCore ?
            new[] { new DefinitionCapabilityId("sample.runtime") } : System.Array.Empty<DefinitionCapabilityId>());
        DefinitionHandle handle;
        if (!_consumer.Definitions.TryResolve(item, out handle))
            throw new System.InvalidOperationException("Compiled sword Definition is missing.");
        var combat = _consumer.Definitions.Bind(SampleDefinitionFacets.Combat);
        SwordCombatFacet meaning;
        if (!combat.TryGet(handle, out meaning))
            throw new System.InvalidOperationException("Compiled sword Combat facet is missing.");
        _consumer.CombatMinimum = meaning.MinimumDamage;
        _consumer.CombatRuntimeActive = combat.RuntimeActive;
    }
    public void Initialize(RuntimePackageContext context) { }
    public void Stop() { }
}

public sealed class Consumer
{
    public bool HasCore { get; set; }
    public string ContractsType { get; set; } = "";
    public string ContractsAssembly { get; set; } = "";
    public DefinitionCatalog? Definitions { get; set; }
    public int CombatMinimum { get; set; }
    public bool CombatRuntimeActive { get; set; }
}
