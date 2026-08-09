using UltraSim.ECS.Comparison;

namespace UltraSim.ECS.V3ComparisonAdapter;

internal static class V3ScenarioFactory
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
            if (descriptor.Id == "query.read.position-velocity-health")
            {
                scenarios.Add(ComparisonCatalog.Unsupported(
                    descriptor,
                    "V3 has no generated 3-read/0-write family yet; substituting lookup or 3R1W would change the timed work."));
                continue;
            }

            scenarios.Add(new ComparisonScenario
            {
                Descriptor = descriptor,
                CreateWorkload = () => V3WorkloadFactory.Create(descriptor),
            });
        }
        return scenarios;
    }
}

internal static class V3WorkloadFactory
{
    internal static IComparisonWorkload Create(ScenarioDescriptor descriptor) => descriptor.Id switch
    {
        "lookup.read.position" => new V3LookupWorkload(descriptor, write: false),
        "lookup.write.position" => new V3LookupWorkload(descriptor, write: true),
        "lookup.read.position.bounded" => new V3BoundedLookupWorkload(descriptor, write: false),
        "lookup.write.position.bounded" => new V3BoundedLookupWorkload(descriptor, write: true),
        "query.read.position" => new V3ReadQueryWorkload(descriptor, componentCount: 1),
        "query.read.position-velocity" => new V3ReadQueryWorkload(descriptor, componentCount: 2),
        "query.read-position.write-velocity" => new V3WriteQueryWorkload(descriptor),
        "structural.create.immediate" => new V3ImmediateCreateWorkload(descriptor),
        "structural.destroy.immediate" => new V3ImmediateDestroyWorkload(descriptor),
        "structural.migration.add-remove" => new V3MigrationRoundTripWorkload(descriptor),
        "lifecycle.destroy-create-reuse" => new V3ReuseWorkload(descriptor),
        "changes.collect-unchanged" => new V3ChangeCollectWorkload(descriptor, stride: 0),
        "changes.write-collect-10pct" => new V3ChangeCollectWorkload(descriptor, stride: 10),
        "changes.write-collect-100pct" => new V3ChangeCollectWorkload(descriptor, stride: 1),
        _ when descriptor.Id.StartsWith("structural.create.deferred.", StringComparison.Ordinal) => new V3DeferredStructuralWorkload(descriptor, V3StructuralOperation.Create),
        _ when descriptor.Id.StartsWith("structural.destroy.deferred.", StringComparison.Ordinal) => new V3DeferredStructuralWorkload(descriptor, V3StructuralOperation.Destroy),
        _ when descriptor.Id.StartsWith("structural.destroy.deferred-sparse-10pct.", StringComparison.Ordinal) => new V3DeferredStructuralWorkload(descriptor, V3StructuralOperation.Destroy, sparse: true),
        _ when descriptor.Id.StartsWith("structural.transition.add-default.", StringComparison.Ordinal) => new V3DeferredStructuralWorkload(descriptor, V3StructuralOperation.Add),
        _ when descriptor.Id.StartsWith("structural.transition.remove.", StringComparison.Ordinal) => new V3DeferredStructuralWorkload(descriptor, V3StructuralOperation.Remove),
        _ => throw new InvalidOperationException($"V3 adapter has no workload for '{descriptor.Id}'."),
    };
}
