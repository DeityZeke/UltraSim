namespace UltraSim.ECS.BitmapExperiment;

internal static class ExperimentCatalog
{
    private static readonly PrototypeStorageKind[] Policies = Enum.GetValues<PrototypeStorageKind>();

    internal static IReadOnlyList<ExperimentScenarioDefinition> Build(string profile, int scale)
    {
        if (scale < 1_024)
            throw new ArgumentOutOfRangeException(nameof(scale), "Use at least 1,024 rows so word topology is meaningful.");
        var definitions = new List<ExperimentScenarioDefinition>();
        if (string.Equals(profile, "smoke", StringComparison.OrdinalIgnoreCase))
        {
            AddCreate(definitions, scale, new[] { 1, 16 });
            AddDestroy(definitions, scale, new[] { 1, 16 }, new[] { (10, HolePattern.Random), (100, HolePattern.Tail) });
            AddChurn(definitions, scale, new[] { 16 }, new[] { 0, 4 });
            AddTraversal(definitions, scale, new[] { (16, 2) }, new[] { (10, HolePattern.Random), (1, HolePattern.OnePerWord) });
            AddOccupancySweep(definitions, scale, 16, 2);
            AddRandomAccess(definitions, scale, new[] { 1, 16 });
            AddTransitions(definitions, scale, new[] { 16 }, new[] { DestinationShape.Empty, DestinationShape.MixedSparseAndEmptyWords });
            return definitions;
        }

        if (string.Equals(profile, "baseline", StringComparison.OrdinalIgnoreCase))
        {
            AddCreate(definitions, scale, new[] { 1, 4, 16, 64, 128 });
            AddDestroy(definitions, scale, new[] { 1, 16, 64, 128 }, new[]
            {
                (1, HolePattern.Random),
                (10, HolePattern.Random),
                (50, HolePattern.Random),
                (100, HolePattern.Tail),
            });
            AddChurn(definitions, scale, new[] { 1, 16, 64 }, new[] { 0, 1, 4, 16 });
            AddTraversal(definitions, scale, new[] { (16, 2), (64, 8) }, new[]
            {
                (0, HolePattern.Tail),
                (1, HolePattern.Random),
                (10, HolePattern.Random),
                (50, HolePattern.Random),
                (1, HolePattern.OnePerWord),
                (10, HolePattern.Clustered),
            });
            AddOccupancySweep(definitions, scale, 16, 2);
            AddRandomAccess(definitions, scale, new[] { 1, 16, 64 });
            AddTransitions(definitions, scale, new[] { 1, 16, 64 }, Enum.GetValues<DestinationShape>());
            return definitions;
        }

        if (!string.Equals(profile, "full", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Unknown profile '{profile}'. Use smoke, baseline, or full.", nameof(profile));

        int[] widths = { 1, 4, 16, 64, 128 };
        AddCreate(definitions, scale, widths);
        AddDestroy(definitions, scale, widths, new[]
        {
            (1, HolePattern.Random),
            (10, HolePattern.Random),
            (25, HolePattern.Random),
            (50, HolePattern.Random),
            (90, HolePattern.Random),
            (100, HolePattern.Tail),
            (10, HolePattern.Clustered),
            (1, HolePattern.OnePerWord),
        });
        AddChurn(definitions, scale, widths, new[] { 0, 1, 4, 16 });
        AddTraversal(definitions, scale, new[] { (1, 1), (4, 1), (4, 2), (16, 2), (16, 8), (64, 2), (64, 8), (128, 8) }, new[]
        {
            (0, HolePattern.Tail),
            (1, HolePattern.Random),
            (10, HolePattern.Random),
            (25, HolePattern.Random),
            (50, HolePattern.Random),
            (90, HolePattern.Random),
            (1, HolePattern.OnePerWord),
            (10, HolePattern.Clustered),
        });
        AddOccupancySweep(definitions, scale, 16, 2);
        AddOccupancySweep(definitions, scale, 64, 8);
        AddRandomAccess(definitions, scale, widths);
        AddTransitions(definitions, scale, new[] { 1, 4, 16, 64, 128 }, Enum.GetValues<DestinationShape>());
        return definitions;
    }

    private static void AddCreate(List<ExperimentScenarioDefinition> definitions, int scale, int[] widths)
    {
        foreach (int columns in widths)
        foreach (bool bulk in new[] { false, true })
        foreach (PrototypeStorageKind kind in Policies)
        {
            string category = bulk ? "create-bulk" : "create";
            string id = $"{category}/n={scale}/width={columns * 8}";
            Add(definitions, id, category, kind, scale, () => new CreateScenario(kind, scale, columns, bulk));
        }
    }

    private static void AddDestroy(
        List<ExperimentScenarioDefinition> definitions,
        int scale,
        int[] widths,
        (int Percentage, HolePattern Pattern)[] cases)
    {
        foreach (int columns in widths)
        foreach ((int percentage, HolePattern pattern) in cases)
        foreach (PrototypeStorageKind kind in Policies)
        {
            string id = $"destroy/n={scale}/width={columns * 8}/pct={percentage}/pattern={pattern}";
            Add(definitions, id, "destroy", kind, scale, () => new DestroyScenario(kind, scale, columns, percentage, pattern));
        }
    }

    private static void AddChurn(List<ExperimentScenarioDefinition> definitions, int scale, int[] widths, int[] scans)
    {
        foreach (int columns in widths)
        foreach (int scanCount in scans)
        foreach (PrototypeStorageKind kind in Policies)
        {
            string id = $"churn/n={scale}/width={columns * 8}/pct=10/scans={scanCount}";
            Add(definitions, id, scanCount == 0 ? "churn" : "mixed-tick", kind, scale,
                () => new ChurnScenario(kind, scale, columns, percentage: 10, scanCount));
        }
    }

    private static void AddTraversal(
        List<ExperimentScenarioDefinition> definitions,
        int scale,
        (int Width, int Query)[] widths,
        (int Percentage, HolePattern Pattern)[] topologies)
    {
        foreach ((int columns, int queryColumns) in widths)
        foreach ((int percentage, HolePattern pattern) in topologies)
        foreach (bool mutate in new[] { false, true })
        foreach (PrototypeStorageKind kind in Policies)
        {
            string id = $"traverse/n={scale}/width={columns * 8}/query={queryColumns * 8}/removed={percentage}/pattern={pattern}/mode={(mutate ? "rw" : "ro")}";
            Add(definitions, id, mutate ? "traversal-rw" : "traversal-ro", kind, scale,
                () => new TraversalScenario(kind, scale, columns, queryColumns, percentage, pattern, mutate));
        }
    }

    private static void AddTransitions(
        List<ExperimentScenarioDefinition> definitions,
        int scale,
        int[] widths,
        DestinationShape[] shapes)
    {
        foreach (int columns in widths)
        foreach (DestinationShape shape in shapes)
        foreach (PrototypeStorageKind kind in Policies)
        {
            string id = $"transition/n={scale}/width={columns * 8}/destination={shape}/source={HolePattern.Random}";
            Add(definitions, id, "transition", kind, scale,
                () => new TransitionScenario(kind, scale, columns, shape, HolePattern.Random));
        }
    }

    private static void AddOccupancySweep(
        List<ExperimentScenarioDefinition> definitions,
        int scale,
        int columns,
        int queryColumns)
    {
        foreach (int livePerWord in new[] { 8, 16, 32, 64 })
        foreach (bool mutate in new[] { false, true })
        foreach (PrototypeStorageKind kind in Policies)
        {
            string id = $"occupancy/live={scale}/width={columns * 8}/query={queryColumns * 8}/live-per-word={livePerWord}/mode={(mutate ? "rw" : "ro")}";
            Add(definitions, id, mutate ? "occupancy-rw" : "occupancy-ro", kind, scale,
                () => new OccupancySweepScenario(kind, scale, columns, queryColumns, livePerWord, mutate));
        }
    }

    private static void AddRandomAccess(List<ExperimentScenarioDefinition> definitions, int scale, int[] widths)
    {
        foreach (int columns in widths)
        foreach (bool write in new[] { false, true })
        foreach (PrototypeStorageKind kind in Policies)
        {
            string id = $"random-access/n={scale}/width={columns * 8}/removed=10/mode={(write ? "rw" : "ro")}";
            Add(definitions, id, write ? "random-access-rw" : "random-access-ro", kind, scale,
                () => new RandomAccessScenario(kind, scale, columns, percentageRemoved: 10, write));
        }
    }

    private static void Add(
        List<ExperimentScenarioDefinition> definitions,
        string logicalCaseId,
        string category,
        PrototypeStorageKind kind,
        int scale,
        Func<IExperimentScenario> create)
    {
        definitions.Add(new ExperimentScenarioDefinition(
            logicalCaseId,
            $"{logicalCaseId}/{kind}",
            category,
            kind,
            scale,
            create));
    }
}
