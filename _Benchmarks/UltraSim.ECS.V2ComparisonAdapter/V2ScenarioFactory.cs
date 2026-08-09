using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V2ComparisonAdapter;

internal static class V2ScenarioFactory
{
    internal static IReadOnlyList<ComparisonScenario> CreateAll()
    {
        var scenarios = new List<ComparisonScenario>();
        foreach (ScenarioDescriptor descriptor in ComparisonCatalog.CreateDescriptors())
        {
            if (descriptor.Strength == ComparisonStrength.Unsupported)
            {
                scenarios.Add(ComparisonCatalog.Unsupported(descriptor, descriptor.SemanticContract));
                continue;
            }

            scenarios.Add(new ComparisonScenario
            {
                Descriptor = descriptor,
                CreateWorkload = () => V2WorkloadFactory.Create(descriptor),
            });
        }
        return scenarios;
    }
}

internal static class V2WorkloadFactory
{
    internal static IComparisonWorkload Create(ScenarioDescriptor descriptor) => descriptor.Id switch
    {
        "lookup.read.position" => new V2LookupWorkload(descriptor, write: false),
        "lookup.write.position" => new V2LookupWorkload(descriptor, write: true),
        "lookup.read.position.bounded" => new V2LookupWorkload(descriptor, write: false),
        "lookup.write.position.bounded" => new V2LookupWorkload(descriptor, write: true),
        "query.read.position" => new V2ReadQueryWorkload(descriptor, componentCount: 1),
        "query.read.position-velocity" => new V2ReadQueryWorkload(descriptor, componentCount: 2),
        "query.read.position-velocity-health" => new V2ReadQueryWorkload(descriptor, componentCount: 3),
        "query.read-position.write-velocity" => new V2WriteQueryWorkload(descriptor),
        "structural.create.immediate" => new V2ImmediateCreateWorkload(descriptor),
        "structural.destroy.immediate" => new V2ImmediateDestroyWorkload(descriptor),
        "structural.migration.add-remove" => new V2MigrationRoundTripWorkload(descriptor),
        "lifecycle.destroy-create-reuse" => new V2ReuseWorkload(descriptor),
        "changes.collect-unchanged" => new V2ChangeCollectWorkload(descriptor, stride: 0),
        "changes.write-collect-10pct" => new V2ChangeCollectWorkload(descriptor, stride: 10),
        "changes.write-collect-100pct" => new V2ChangeCollectWorkload(descriptor, stride: 1),
        _ when descriptor.Id.StartsWith("structural.create.deferred.", StringComparison.Ordinal) => new V2DeferredStructuralWorkload(descriptor, V2StructuralOperation.Create),
        _ when descriptor.Id.StartsWith("structural.destroy.deferred.", StringComparison.Ordinal) => new V2DeferredStructuralWorkload(descriptor, V2StructuralOperation.Destroy),
        _ when descriptor.Id.StartsWith("structural.destroy.deferred-sparse-10pct.", StringComparison.Ordinal) => new V2DeferredStructuralWorkload(descriptor, V2StructuralOperation.Destroy, sparse: true),
        _ when descriptor.Id.StartsWith("structural.transition.add-default.", StringComparison.Ordinal) => new V2DeferredStructuralWorkload(descriptor, V2StructuralOperation.Add),
        _ when descriptor.Id.StartsWith("structural.transition.remove.", StringComparison.Ordinal) => new V2DeferredStructuralWorkload(descriptor, V2StructuralOperation.Remove),
        _ => throw new InvalidOperationException($"V2 adapter has no workload for '{descriptor.Id}'."),
    };
}
