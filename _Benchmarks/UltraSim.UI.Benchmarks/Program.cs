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
        Name = "ActionMenuDescriptor_Validate_10000",
        Category = "UiAction",
        Comparable = false,
        Scale = 10_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunActionMenuDescriptorScenario(10_000),
    };

    yield return CreateActionRequestValidationScenario(10_000);

    yield return CreateActionResultValidationScenario(10_000);

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

    yield return CreateTooltipValidationScenario(10_000);

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

    yield return new BenchmarkScenario
    {
        Name = "SchemaDocument_Validate_1000Nodes",
        Category = "UiSchemaValidation",
        Comparable = false,
        Scale = 1_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunSchemaDocumentValidationScenario(1_000),
    };

    yield return new BenchmarkScenario
    {
        Name = "ModelDocument_ValidateAgainstSchema_1000Slots",
        Category = "UiModelCompatibility",
        Comparable = false,
        Scale = 1_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunModelDocumentValidationScenario(1_000),
    };

    yield return CreatePanelOpenColdValidationScenario(1_000);

    yield return CreatePanelOpenHotValidationScenario(1_000);

    yield return new BenchmarkScenario
    {
        Name = "ModelUpdate_ValidateSlotPatch_1000Slots",
        Category = "UiModelCompatibility",
        Comparable = false,
        Scale = 1_000,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () => RunModelUpdateValidationScenario(1_000),
    };

    yield return CreateModelUpdateContextScenario(1_000);
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

static ScenarioMeasurement RunActionMenuDescriptorScenario(int iterations)
{
    var capabilities = new UiRendererCapabilities(
        ProtocolVersion: 1,
        CapabilityFlags: UiCapabilityFlags.ContextMenusV1 | UiCapabilityFlags.RadialMenusV1,
        MaxSchemaBytes: 64 * 1024,
        MaxModelBytes: 16 * 1024,
        MaxNodeCount: 512,
        MaxTreeDepth: 32,
        MaxRepeaterRows: 256,
        MaxTableColumns: 24);
    var menu = new UiActionMenuDescriptor(
        "entity/interaction/root",
        UiActionMenuPresentationKind.RadialMenu,
        UiActionContextKind.Entity,
        ContextId: "entity:target_42",
        Actions:
        [
            new UiActionDescriptor(
                "entity.talk",
                "Talk",
                "icons/actions/talk",
                UiActionRole.Primary,
                UiActionContextKind.Entity,
                RequiresConfirm: false,
                ChildMenuKey: "entity/interaction/talk",
                MenuGroupKey: "social"),
            new UiActionDescriptor(
                "entity.trade",
                "Trade",
                null,
                UiActionRole.Normal,
                UiActionContextKind.Entity,
                RequiresConfirm: false,
                ChildMenuKey: null,
                UiActionAvailability.OutOfRange,
                "Move closer",
                MenuGroupKey: "commerce"),
            new UiActionDescriptor(
                "entity.admin.inspect",
                "Inspect",
                null,
                UiActionRole.Debug,
                UiActionContextKind.Admin,
                RequiresConfirm: false,
                ChildMenuKey: null,
                MenuGroupKey: "debug"),
        ]);

    long checksum = 0;
    for (int i = 0; i < iterations; i++)
    {
        UiValidationResult result = menu.Validate(capabilities);
        if (!result.IsValid)
        {
            throw new InvalidOperationException(result.Error);
        }

        checksum += menu.Actions.Count + i;
    }

    return new ScenarioMeasurement(iterations, checksum);
}

static BenchmarkScenario CreateActionRequestValidationScenario(int iterations)
{
    (UiSchemaDocument schema, _) = CreateLargeModelCompatibilityPair(16);
    UiValidationResult contextResult = UiSchemaCompatibilityContext.TryCreate(
        schema,
        out UiSchemaCompatibilityContext? context,
        UiSchemaValidationLimits.Default with { MaxChildrenPerNode = 32 });
    if (!contextResult.IsValid || context is null)
    {
        throw new InvalidOperationException(contextResult.Error);
    }

    var request = new UiActionRequestDocument(
        "panel/bench",
        schema.Identity,
        schema.CatalogIdentity,
        ClientSequence: 1,
        "bench.refresh",
        UiActionContextKind.Panel,
        ContextId: null,
        FieldValues:
        [
            UiModelSlotValue.String("model.slot.0", "value-0"),
            UiModelSlotValue.String("model.slot.1", "value-1"),
        ]);

    return new BenchmarkScenario
    {
        Name = "ActionRequest_ValidateAgainstContext_10000",
        Category = "UiAction",
        Comparable = false,
        Scale = iterations,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () =>
        {
            long checksum = 0;
            for (int i = 0; i < iterations; i++)
            {
                UiValidationResult result = request.ValidateAgainst(context);
                if (!result.IsValid)
                {
                    throw new InvalidOperationException(result.Error);
                }

                checksum += request.ClientSequence + request.FieldValues.Count + i;
            }

            return new ScenarioMeasurement(iterations, checksum);
        },
    };
}

static BenchmarkScenario CreateActionResultValidationScenario(int iterations)
{
    (UiSchemaDocument schema, _) = CreateLargeModelCompatibilityPair(16);
    UiValidationResult contextResult = UiSchemaCompatibilityContext.TryCreate(
        schema,
        out UiSchemaCompatibilityContext? context,
        UiSchemaValidationLimits.Default with { MaxChildrenPerNode = 32 });
    if (!contextResult.IsValid || context is null)
    {
        throw new InvalidOperationException(contextResult.Error);
    }

    var resultDocument = new UiActionResultDocument(
        "panel/bench",
        schema.Identity,
        ClientSequence: 1,
        "bench.refresh",
        UiActionResultKind.Accepted,
        UiActionFailureReason.None,
        Message: null,
        ModelUpdates: []);

    return new BenchmarkScenario
    {
        Name = "ActionResult_ValidateAgainstContext_10000",
        Category = "UiAction",
        Comparable = false,
        Scale = iterations,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () =>
        {
            long checksum = 0;
            for (int i = 0; i < iterations; i++)
            {
                UiValidationResult result = resultDocument.ValidateAgainst(context);
                if (!result.IsValid)
                {
                    throw new InvalidOperationException(result.Error);
                }

                checksum += resultDocument.ClientSequence + resultDocument.ModelUpdates.Count + i;
            }

            return new ScenarioMeasurement(iterations, checksum);
        },
    };
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

static BenchmarkScenario CreateTooltipValidationScenario(int iterations)
{
    (UiSchemaDocument schema, _) = CreateLargeModelCompatibilityPair(16);
    UiValidationResult contextResult = UiSchemaCompatibilityContext.TryCreate(
        schema,
        out UiSchemaCompatibilityContext? context,
        UiSchemaValidationLimits.Default with { MaxChildrenPerNode = 32 });
    if (!contextResult.IsValid || context is null)
    {
        throw new InvalidOperationException(contextResult.Error);
    }

    var tooltip = new UiTooltipDescriptor(
        "tooltip/bench/item",
        "Iron Sword",
        "icons/items/iron_sword",
        UiTooltipVisualRole.Common,
        Lines:
        [
            new UiTooltipLineDescriptor(UiTooltipLineKind.Body, null, "A plain forged blade.", null),
            new UiTooltipLineDescriptor(UiTooltipLineKind.Stat, "Damage", "4-8", null, UiTooltipVisualRole.Positive),
            new UiTooltipLineDescriptor(UiTooltipLineKind.Requirement, "Level", null, "model.slot.0"),
            new UiTooltipLineDescriptor(UiTooltipLineKind.Advanced, null, "Hold detail for crafting lineage.", null, UiTooltipVisualRole.Muted),
        ]);

    return new BenchmarkScenario
    {
        Name = "TooltipDescriptor_ValidateAgainstContext_10000",
        Category = "UiTooltip",
        Comparable = false,
        Scale = iterations,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () =>
        {
            long checksum = 0;
            for (int i = 0; i < iterations; i++)
            {
                UiValidationResult result = tooltip.Validate(context);
                if (!result.IsValid)
                {
                    throw new InvalidOperationException(result.Error);
                }

                checksum += tooltip.Lines.Count + i;
            }

            return new ScenarioMeasurement(iterations, checksum);
        },
    };
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

static ScenarioMeasurement RunSchemaDocumentValidationScenario(int nodeCount)
{
    UiSchemaDocument schema = CreateLargeSchemaDocument(nodeCount);
    var limits = UiSchemaValidationLimits.Default with
    {
        MaxNodeCount = nodeCount + 8,
        MaxTreeDepth = nodeCount + 8,
    };
    var rendererCapabilities = new UiRendererCapabilities(
        ProtocolVersion: 1,
        CapabilityFlags: UiCapabilityFlags.RuntimeStateBindingsV1,
        MaxSchemaBytes: 512 * 1024,
        MaxModelBytes: 64 * 1024,
        MaxNodeCount: nodeCount + 8,
        MaxTreeDepth: nodeCount + 8,
        MaxRepeaterRows: 256,
        MaxTableColumns: 32);

    UiValidationResult result = schema.Validate(limits, rendererCapabilities);
    if (!result.IsValid)
    {
        throw new InvalidOperationException(result.Error);
    }

    return new ScenarioMeasurement(nodeCount, schema.Nodes.Count + schema.RootNodeId);
}

static ScenarioMeasurement RunModelDocumentValidationScenario(int slotCount)
{
    (UiSchemaDocument schema, UiModelDocument model) = CreateLargeModelCompatibilityPair(slotCount);
    var limits = UiSchemaValidationLimits.Default with
    {
        MaxNodeCount = slotCount + 8,
        MaxChildrenPerNode = slotCount + 8,
    };

    UiValidationResult result = model.ValidateAgainst(schema, limits);
    if (!result.IsValid)
    {
        throw new InvalidOperationException(result.Error);
    }

    return new ScenarioMeasurement(slotCount, model.Values.Count + schema.PayloadSlots.Count);
}

static BenchmarkScenario CreatePanelOpenColdValidationScenario(int slotCount)
{
    (UiSchemaDocument schema, UiModelDocument model) = CreateLargeModelCompatibilityPair(slotCount);
    var open = new UiPanelOpenDocument(
        "panel/bench/open",
        schema.Identity,
        schema.CatalogIdentity,
        model,
        schema);
    var limits = UiSchemaValidationLimits.Default with
    {
        MaxNodeCount = slotCount + 8,
        MaxChildrenPerNode = slotCount + 8,
    };

    return new BenchmarkScenario
    {
        Name = "PanelOpen_ValidateCold_1000Slots",
        Category = "UiCacheNegotiation",
        Comparable = false,
        Scale = slotCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () =>
        {
            UiValidationResult result = open.ValidateCold(limits);
            if (!result.IsValid)
            {
                throw new InvalidOperationException(result.Error);
            }

            return new ScenarioMeasurement(slotCount, open.Model.Values.Count + open.SchemaDefinition!.Nodes.Count);
        },
    };
}

static BenchmarkScenario CreatePanelOpenHotValidationScenario(int slotCount)
{
    (UiSchemaDocument schema, UiModelDocument model) = CreateLargeModelCompatibilityPair(slotCount);
    var limits = UiSchemaValidationLimits.Default with
    {
        MaxNodeCount = slotCount + 8,
        MaxChildrenPerNode = slotCount + 8,
    };
    UiValidationResult contextResult = UiSchemaCompatibilityContext.TryCreate(
        schema,
        out UiSchemaCompatibilityContext? context,
        limits);
    if (!contextResult.IsValid || context is null)
    {
        throw new InvalidOperationException(contextResult.Error);
    }

    var open = new UiPanelOpenDocument(
        "panel/bench/open",
        schema.Identity,
        schema.CatalogIdentity,
        model,
        SchemaDefinition: null);

    return new BenchmarkScenario
    {
        Name = "PanelOpen_ValidateHotAgainstContext_1000Slots",
        Category = "UiCacheNegotiation",
        Comparable = false,
        Scale = slotCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () =>
        {
            UiValidationResult result = open.ValidateHot(context);
            if (!result.IsValid)
            {
                throw new InvalidOperationException(result.Error);
            }

            return new ScenarioMeasurement(slotCount, open.Model.Values.Count + context.PayloadSlots.Count);
        },
    };
}

static ScenarioMeasurement RunModelUpdateValidationScenario(int slotCount)
{
    (UiSchemaDocument schema, UiModelDocument model) = CreateLargeModelCompatibilityPair(slotCount);
    var update = new UiModelUpdateDocument(
        "panel/bench",
        schema.Identity,
        schema.CatalogIdentity,
        Sequence: 1,
        UiModelUpdateKind.SlotPatch,
        ScopeId: null,
        SlotValues: model.Values,
        ClearedSlotIds: [],
        RowDeltas: []);

    var limits = UiSchemaValidationLimits.Default with
    {
        MaxNodeCount = slotCount + 8,
        MaxChildrenPerNode = slotCount + 8,
    };

    UiValidationResult result = update.ValidateAgainst(schema, limits);
    if (!result.IsValid)
    {
        throw new InvalidOperationException(result.Error);
    }

    return new ScenarioMeasurement(slotCount, update.SlotValues.Count + schema.PayloadSlots.Count);
}

static BenchmarkScenario CreateModelUpdateContextScenario(int slotCount)
{
    (UiSchemaDocument schema, UiModelDocument model) = CreateLargeModelCompatibilityPair(slotCount);
    var limits = UiSchemaValidationLimits.Default with
    {
        MaxNodeCount = slotCount + 8,
        MaxChildrenPerNode = slotCount + 8,
    };
    UiValidationResult contextResult = UiSchemaCompatibilityContext.TryCreate(
        schema,
        out UiSchemaCompatibilityContext? context,
        limits);
    if (!contextResult.IsValid || context is null)
    {
        throw new InvalidOperationException(contextResult.Error);
    }

    var update = new UiModelUpdateDocument(
        "panel/bench",
        schema.Identity,
        schema.CatalogIdentity,
        Sequence: 1,
        UiModelUpdateKind.SlotPatch,
        ScopeId: null,
        SlotValues: model.Values,
        ClearedSlotIds: [],
        RowDeltas: []);

    return new BenchmarkScenario
    {
        Name = "ModelUpdate_ValidateSlotPatchAgainstContext_1000Slots",
        Category = "UiModelCompatibility",
        Comparable = false,
        Scale = slotCount,
        WarmupCount = 1,
        IterationCount = 5,
        Execute = () =>
        {
            UiValidationResult result = update.ValidateAgainst(context);
            if (!result.IsValid)
            {
                throw new InvalidOperationException(result.Error);
            }

            return new ScenarioMeasurement(slotCount, update.SlotValues.Count + context.PayloadSlots.Count);
        },
    };
}

static UiSchemaDocument CreateLargeSchemaDocument(int nodeCount)
{
    if (!UiSchemaIdentity.TryCreate(
        "server/main",
        "bench.large_schema",
        1,
        new string('d', 64),
        1,
        out UiSchemaIdentity identity,
        out string error))
    {
        throw new InvalidOperationException(error);
    }

    var nodes = new UiSchemaNodeDescriptor[nodeCount];
    var payloadSlots = new List<UiPayloadSlotDescriptor>(nodeCount / 3 + 1);
    for (int i = 0; i < nodeCount; i++)
    {
        int nodeId = i + 1;
        UiNodeKind kind = i == 0
            ? UiNodeKind.Panel
            : i % 3 == 0
                ? UiNodeKind.DynamicText
                : UiNodeKind.Column;
        string? slotId = kind == UiNodeKind.DynamicText ? $"slot.{nodeId}" : null;
        if (slotId is not null)
        {
            payloadSlots.Add(new UiPayloadSlotDescriptor(slotId, UiModelValueKind.String, Required: false));
        }

        IReadOnlyList<int> children = nodeId < nodeCount ? [nodeId + 1] : [];
        nodes[i] = new UiSchemaNodeDescriptor(nodeId, kind, slotId, kind == UiNodeKind.DynamicText ? "Value" : null, children);
    }

    var metadata = new UiPanelMetadata(
        "bench.large_schema",
        UiSurfaceTarget.Menu,
        UiFocusMode.Normal,
        null,
        1,
        UiOpenPolicy.New,
        UiBackPolicy.None,
        UiTooltipPolicy.Allow,
        BlocksGameplayInput: false,
        PersistenceKey: null);

    return new UiSchemaDocument(
        identity,
        metadata,
        RootNodeId: 1,
        Nodes: nodes,
        PayloadSlots: payloadSlots,
        Actions: [],
        StateBindings: []);
}

static (UiSchemaDocument Schema, UiModelDocument Model) CreateLargeModelCompatibilityPair(int slotCount)
{
    if (!UiSchemaIdentity.TryCreate(
        "server/main",
        "bench.large_model",
        1,
        new string('e', 64),
        1,
        out UiSchemaIdentity identity,
        out string error))
    {
        throw new InvalidOperationException(error);
    }

    var children = new int[slotCount];
    var nodes = new UiSchemaNodeDescriptor[slotCount + 1];
    var payloadSlots = new UiPayloadSlotDescriptor[slotCount];
    var values = new UiModelSlotValue[slotCount];

    for (int i = 0; i < slotCount; i++)
    {
        int nodeId = i + 2;
        string slotId = $"model.slot.{i}";
        children[i] = nodeId;
        nodes[i + 1] = new UiSchemaNodeDescriptor(nodeId, UiNodeKind.DynamicText, slotId, "Value", []);
        payloadSlots[i] = new UiPayloadSlotDescriptor(slotId, UiModelValueKind.String, Required: true, MaxStringLength: 64);
        values[i] = UiModelSlotValue.String(slotId, $"value-{i}");
    }

    nodes[0] = new UiSchemaNodeDescriptor(1, UiNodeKind.Panel, null, null, children);

    var metadata = new UiPanelMetadata(
        "bench.large_model",
        UiSurfaceTarget.Menu,
        UiFocusMode.Normal,
        null,
        1,
        UiOpenPolicy.New,
        UiBackPolicy.None,
        UiTooltipPolicy.Allow,
        BlocksGameplayInput: false,
        PersistenceKey: null);

    var schema = new UiSchemaDocument(
        identity,
        metadata,
        RootNodeId: 1,
        Nodes: nodes,
        PayloadSlots: payloadSlots,
        Actions:
        [
            new UiActionDescriptor(
                "bench.refresh",
                "Refresh",
                null,
                UiActionRole.Normal,
                UiActionContextKind.Panel,
                RequiresConfirm: false,
                ChildMenuKey: null),
        ],
        StateBindings: []);

    var model = new UiModelDocument(identity, values);
    return (schema, model);
}
