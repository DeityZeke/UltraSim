using UltraSim.ECS.Benchmarks.Core;
using UltraSim.UI;

string outputPath = Path.Combine("_Benchmarks", "results", "ui-benchmarks.json");
for (int i = 0; i < args.Length - 1; i++)
{
    if (args[i] == "--output")
    {
        outputPath = args[i + 1];
    }
}

BenchmarkRunReport report = MicroBenchmark.RunSuite(
    suiteName: "UiKernel",
    kernelName: "UltraSim.UI",
    scenarios: CreateScenarios());
MicroBenchmark.WriteReport(report, outputPath);
Console.WriteLine($"Wrote UI benchmark report to {outputPath}");

static IEnumerable<BenchmarkScenario> CreateScenarios()
{
    yield return new BenchmarkScenario
    {
        Name = "SchemaIdentity_TryCreate_10000",
        Category = "UiIdentity",
        Comparable = false,
        Scale = 10_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunSchemaIdentityScenario(10_000),
    };

    yield return new BenchmarkScenario
    {
        Name = "PanelMetadata_Validate_10000",
        Category = "UiValidation",
        Comparable = false,
        Scale = 10_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunPanelMetadataScenario(10_000),
    };

    yield return new BenchmarkScenario
    {
        Name = "ActionDescriptor_Validate_10000",
        Category = "UiValidation",
        Comparable = false,
        Scale = 10_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunActionDescriptorScenario(10_000),
    };

    yield return new BenchmarkScenario
    {
        Name = "StateBinding_Validate_10000",
        Category = "UiValidation",
        Comparable = false,
        Scale = 10_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunStateBindingScenario(10_000),
    };

    yield return new BenchmarkScenario
    {
        Name = "PanelMetadata_JsonRoundTrip_1000",
        Category = "UiCodec",
        Comparable = false,
        Scale = 1_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunPanelMetadataJsonRoundTripScenario(1_000),
    };
}

static ScenarioMeasurement RunSchemaIdentityScenario(int iterations)
{
    string hash = new('a', 64);
    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        if (!UiSchemaIdentity.TryCreate(
            "server/main",
            $"panel.{i % 32}",
            i % 8,
            hash,
            1,
            out UiSchemaIdentity identity,
            out string error))
        {
            throw new InvalidOperationException(error);
        }

        checksum += identity.PanelType.Length + identity.SchemaRevision;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static ScenarioMeasurement RunPanelMetadataScenario(int iterations)
{
    var metadata = new UiPanelMetadata(
        "inventory.grid",
        UiSurfaceTarget.Menu,
        UiFocusMode.Normal,
        null,
        10,
        UiOpenPolicy.SingletonByKey,
        UiBackPolicy.CloseAndFocusParent,
        UiTooltipPolicy.Allow,
        BlocksGameplayInput: true,
        PersistenceKey: "inventory/main");

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        UiValidationResult result = metadata.Validate();
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Error);
        }

        checksum += metadata.ZOrderClass + i;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static ScenarioMeasurement RunActionDescriptorScenario(int iterations)
{
    var action = new UiActionDescriptor(
        "entity.interact",
        "Interact",
        "icons/actions/interact",
        UiActionRole.Normal,
        UiActionContextKind.Entity,
        RequiresConfirm: false,
        ChildMenuKey: "interact/root");

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        UiValidationResult result = action.Validate();
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Error);
        }

        checksum += action.ActionId.Length + i;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static ScenarioMeasurement RunStateBindingScenario(int iterations)
{
    var binding = new UiStateBindingDescriptor(
        "healthCurrent",
        "EntityState",
        "entity:42",
        "Health.Current",
        new UiStateBindingFallback(
            UiBindingFallbackBehavior.Stale,
            UiBindingFallbackBehavior.Hidden,
            UiBindingFallbackBehavior.Stale,
            1500));

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        UiValidationResult result = binding.Validate();
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Error);
        }

        checksum += binding.SlotId.Length + i;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static ScenarioMeasurement RunPanelMetadataJsonRoundTripScenario(int iterations)
{
    var metadata = new UiPanelMetadata(
        "login",
        UiSurfaceTarget.Exclusive,
        UiFocusMode.Exclusive,
        "SessionAdmission",
        100,
        UiOpenPolicy.ReplaceCurrent,
        UiBackPolicy.SendAction,
        UiTooltipPolicy.SuppressExternal,
        BlocksGameplayInput: true,
        PersistenceKey: null);

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        byte[] encoded = UiContractJsonCodec.Encode(metadata);
        UiPanelMetadata decoded = UiContractJsonCodec.Decode<UiPanelMetadata>(encoded);
        checksum += encoded.Length + decoded.ZOrderClass;
    }

    return new ScenarioMeasurement(iterations, checksum);
}
