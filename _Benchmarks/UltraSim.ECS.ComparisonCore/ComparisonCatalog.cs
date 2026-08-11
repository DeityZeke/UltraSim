namespace UltraSim.ECS.Comparison;

public static class ComparisonCatalog
{
    public static readonly int[] CommonScales = [1_000, 10_000, 50_000];

    public static IReadOnlyList<ScenarioDescriptor> CreateDescriptors()
    {
        var descriptors = new List<ScenarioDescriptor>();
        foreach (int scale in CommonScales)
        {
            Add(descriptors, "lookup.read.position", "Prepared/random position read", "Lookup", MeasurementBoundary.HotLoop, ComparisonStrength.Direct, scale, scale, 8, "Read every living entity by retained handle.", "All entities remain alive with unchanged Position values.");
            Add(descriptors, "lookup.write.position", "Tracked position write", "Lookup", MeasurementBoundary.HotLoop, ComparisonStrength.Direct, scale, scale, 8, "Write Position exactly once for every living entity by retained handle.", "Every entity has Position=(ordinal+7, ordinal*2+11).");
            Add(descriptors, "lookup.read.position.bounded", "Bounded prepared position read", "Lookup", MeasurementBoundary.HotLoop, ComparisonStrength.Provisional, scale, scale, 8, "Read every Position under one prepared execution boundary; V2 has no separate bounded-job API.", "All entities remain alive with unchanged Position values.");
            Add(descriptors, "lookup.write.position.bounded", "Bounded tracked position write", "Lookup", MeasurementBoundary.HotLoop, ComparisonStrength.Provisional, scale, scale, 8, "Write every Position under one prepared execution boundary; V2 has no separate bounded-job API.", "Every entity has Position=(ordinal+7, ordinal*2+11).");
            Add(descriptors, "query.read.position", "One-column chunk traversal", "Query", MeasurementBoundary.HotLoop, ComparisonStrength.Direct, scale, scale, 8, "Visit every Position row once through the warmed chunk/query path.", "Traversal checksum and full Position state match.");
            Add(descriptors, "query.read.position-velocity", "Two-column chunk traversal", "Query", MeasurementBoundary.HotLoop, ComparisonStrength.Direct, scale, scale, 16, "Visit every Position+Velocity row once through the warmed chunk/query path.", "Traversal checksum and full two-component state match.");
            Add(descriptors, "query.read.position-velocity-health", "Three-column chunk traversal", "Query", MeasurementBoundary.HotLoop, ComparisonStrength.Direct, scale, scale, 20, "Visit every Position+Velocity+Health row once through the warmed chunk/query path.", "Traversal checksum and full three-component state match.");
            Add(descriptors, "query.read-position.write-velocity", "One-read/one-write chunk traversal", "Query", MeasurementBoundary.HotLoop, ComparisonStrength.Direct, scale, scale, 16, "Read Position and write Velocity once for every matching row.", "Velocity becomes (Position.X*2, Position.Y*2) for every entity.");
            Add(descriptors, "structural.create.immediate", "Immediate default create", "Structural", MeasurementBoundary.EndToEnd, ComparisonStrength.Direct, scale, scale, 0, "Create default Position entities through the immediate checked API.", "Exactly scale living Position entities with zero values.");
            Add(descriptors, "structural.destroy.immediate", "Immediate destroy", "Structural", MeasurementBoundary.EndToEnd, ComparisonStrength.Direct, scale, scale, 0, "Destroy every captured Position entity through the immediate checked API.", "No living entities remain and every captured handle is stale.");
            AddStructuralTriplet(descriptors, "structural.create.deferred", "Deferred default create", scale, scale, "Record and/or play one default Position create per command.", "Exactly scale living Position entities with zero values.");
            AddStructuralTriplet(descriptors, "structural.destroy.deferred", "Deferred destroy", scale, scale, "Record and/or play one destroy per living Position entity.", "No living entities remain and every captured handle is stale.");
            AddStructuralTriplet(descriptors, "structural.destroy.deferred-sparse-10pct", "Deferred sparse destroy", scale, scale / 10L, "Record and/or play one destroy for every tenth Position entity.", "Exactly ten percent of captured handles are stale and survivors retain values.");
            AddStructuralTriplet(descriptors, "structural.churn.balanced-wide-10pct", "Deferred balanced wide churn", scale, scale / 5L, "Destroy every tenth Position+WideBlock64 entity and create the same count in that archetype during one barrier. V2 does not clear reused default rows, so replacement payload is excluded from the normalized comparison.", "Survivors retain all 72 component bytes; replacements are alive in the same archetype and occupy the destroyed logical ordinals; V3 separately validates default initialization.", ComparisonStrength.Provisional);
            AddStructuralTriplet(descriptors, "structural.transition.add-default", "Deferred add-default transition", scale, scale, "Record and/or play one Position to Position+Velocity transition with default Velocity.", "All entities retain Position and gain zero Velocity.");
            AddStructuralTriplet(descriptors, "structural.transition.remove", "Deferred remove transition", scale, scale, "Record and/or play one Position+Velocity to Position transition.", "All entities retain Position and have no Velocity.");
            Add(descriptors, "structural.migration.add-remove", "Deferred add/remove round trip", "Structural", MeasurementBoundary.EndToEnd, ComparisonStrength.Direct, scale, scale * 2L, 0, "Add default Velocity at one barrier and remove it at the next.", "All entities finish in Position-only state with retained values.");
            Add(descriptors, "lifecycle.destroy-create-reuse", "Immediate destroy/create reuse", "Lifecycle", MeasurementBoundary.EndToEnd, ComparisonStrength.Direct, scale, scale * 2L, 0, "Destroy every entity immediately, create the same count again, and verify stale-handle rejection plus generation advancement.", "All slots are reused, old handles are stale, and replacement Position values are zero.");
            Add(descriptors, "changes.collect-unchanged", "Collect unchanged state", "ChangeTracking", MeasurementBoundary.EndToEnd, ComparisonStrength.Provisional, scale, 1, 0, "Poll a scale-sized unchanged world once; V2 dirty-set and V3 cursor scanning have different internal work.", "No changed entities are returned and component state is unchanged.");
            Add(descriptors, "changes.write-collect-10pct", "Write and collect ten-percent changes", "ChangeTracking", MeasurementBoundary.EndToEnd, ComparisonStrength.Provisional, scale, scale / 10L, 12, "Write every tenth Scalar and collect exactly the changed logical entities.", "Independent normalized changed set and final component values match; V2 drain and V3 cursor mechanics differ.");
            Add(descriptors, "changes.write-collect-100pct", "Write and collect all changes", "ChangeTracking", MeasurementBoundary.EndToEnd, ComparisonStrength.Provisional, scale, scale, 12, "Write every Scalar and collect exactly every changed logical entity.", "Independent normalized changed set and final component values match; V2 drain and V3 cursor mechanics differ.");
        }

        AddUnsupported(descriptors, "query.filters.required-excluded-enabled", "Required/excluded/enabled query filter", "Query", 50_000, "V2 has no equivalent enable-bit row-filter contract.");
        AddUnsupported(descriptors, "changes.independent-cursors", "Independent delayed change consumers", "ChangeTracking", 50_000, "V2 dirty drain is destructive and cannot model independent cursors.");
        AddUnsupported(descriptors, "changes.multi-any-all", "Multi-column any/all changed semantics", "ChangeTracking", 50_000, "V2 has no equivalent multi-watch cursor semantics.");
        AddUnsupported(descriptors, "structural.deferred-token-chain", "Create/transition/destroy deferred token chain", "Structural", 10_000, "V2 deferred creates do not return same-barrier tokens.");
        AddUnsupported(descriptors, "structural.typed-add-payload", "Typed add payload playback", "Structural", 10_000, "V3 transition commands currently add store defaults only.");
        AddUnsupported(descriptors, "structural.capacity-failure", "Bounded structural capacity failure", "Structural", 10_000, "V2 structural storage grows dynamically and has no equivalent fixed-capacity result.");
        AddUnsupported(descriptors, "lifecycle.streams", "Created/added/removed/destroyed lifecycle streams", "Lifecycle", 50_000, "V3 lifecycle publication is not implemented yet.");
        AddUnsupported(descriptors, "scheduler.serial-tick", "Serial scheduled systems", "Scheduler", 10_000, "V3 scheduler is not implemented yet.");
        AddUnsupported(descriptors, "scheduler.parallel-tick", "Parallel scheduled systems", "Scheduler", 100_000, "V3 scheduler is not implemented yet.");
        AddUnsupported(descriptors, "prefab.spawn", "Compiled prefab spawn", "Prefab", 10_000, "V3 prefab compiler/runtime is not implemented yet.");
        AddUnsupported(descriptors, "persistence.filtered-view", "Filtered persistence view", "Persistence", 50_000, "V3 persistence view is not implemented yet.");
        AddUnsupported(descriptors, "persistence.round-trip-remap", "Persistence round trip with transient-ID remap", "Persistence", 50_000, "V3 persistence and load-remap pipeline are not implemented yet.");
        return descriptors;
    }

    public static ComparisonScenario Unsupported(ScenarioDescriptor descriptor, string reason) => new()
    {
        Descriptor = descriptor,
        UnsupportedReason = reason,
    };

    private static void AddStructuralTriplet(
        List<ScenarioDescriptor> descriptors,
        string id,
        string name,
        int scale,
        long operations,
        string semanticContract,
        string stateContract,
        ComparisonStrength strength = ComparisonStrength.Direct)
    {
        Add(descriptors, id + ".record", name + " record", "Structural", MeasurementBoundary.RecordOnly, strength, scale, operations, 0, semanticContract, stateContract);
        Add(descriptors, id + ".playback", name + " playback", "Structural", MeasurementBoundary.PlaybackOnly, strength, scale, operations, 0, semanticContract, stateContract);
        Add(descriptors, id + ".end-to-end", name + " end-to-end", "Structural", MeasurementBoundary.EndToEnd, strength, scale, operations, 0, semanticContract, stateContract);
    }

    private static void Add(
        List<ScenarioDescriptor> descriptors,
        string id,
        string name,
        string category,
        MeasurementBoundary boundary,
        ComparisonStrength strength,
        int scale,
        long operations,
        long bytes,
        string semanticContract,
        string stateContract) => descriptors.Add(new ScenarioDescriptor(
            id,
            name,
            category,
            boundary,
            strength,
            scale,
            operations,
            bytes,
            semanticContract,
            stateContract));

    private static void AddUnsupported(
        List<ScenarioDescriptor> descriptors,
        string id,
        string name,
        string category,
        int scale,
        string reason) => Add(
            descriptors,
            id,
            name,
            category,
            MeasurementBoundary.EndToEnd,
            ComparisonStrength.Unsupported,
            scale,
            0,
            0,
            reason,
            "Not yet canonically comparable.");
}
