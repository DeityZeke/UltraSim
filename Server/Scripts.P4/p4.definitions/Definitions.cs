using System;
using P4.Items.Contracts;
using P4.Combat.Contracts;
using UltraSim.Framework.Definitions;
using UltraSim.Framework.RuntimeHost;

namespace P4.Definitions;

public sealed class DefinitionEntry : IRuntimePackageLifecycle
{
    private readonly DefinitionHolder _holder = new();
    public void Register(RuntimePackageContext context) =>
        context.Provide(new RuntimeServiceId("p4.catalog"), _holder);
    public void Configure(RuntimePackageContext context)
    {
        bool hasCore = context.TryGet<object>(new RuntimeServiceId("p4.combat.service"), out _);
        var composer = new DefinitionComposer();
        composer.RegisterFacet(ItemFacets.Item);
        composer.RegisterFacet(CombatFacets.Combat);
        var sword = new StableDefinitionId("p4.items.iron-sword");
        var owner = new DefinitionPackageId(context.Package.Value);
        composer.Define(owner, sword)
            .With(owner, ItemFacets.Item, new ItemFacet("Iron Sword", 1))
            .With(owner, CombatFacets.Combat, new CombatFacet(2, 6));
        composer.Alias(owner, new StableDefinitionId("p4.items.old-sword"), sword);
        composer.ContributeTag(owner, new DefinitionTagId("weapons.swords"), sword);
        _holder.Catalog = composer.Freeze(hasCore ? new[] { CombatFacets.Runtime } :
            Array.Empty<DefinitionCapabilityId>());
        DefinitionHandle handle;
        CombatFacet combat;
        if (!_holder.Catalog.TryResolve(sword, out handle) ||
            !_holder.Catalog.Bind(CombatFacets.Combat).TryGet(handle, out combat))
            throw new InvalidOperationException("Compiled Combat meaning is missing.");
        _holder.CombatMaximum = combat.Maximum;
        _holder.CombatRuntimeActive = _holder.Catalog.Bind(CombatFacets.Combat).RuntimeActive;
    }
    public void Initialize(RuntimePackageContext context) { }
    public void Stop() { _holder.Catalog = null; }
}

public sealed class DefinitionHolder
{
    public DefinitionCatalog? Catalog { get; set; }
    public bool CombatRuntimeActive { get; set; }
    public int CombatMaximum { get; set; }
}
