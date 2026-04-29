using UltraSim.UI;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(UiSchemaIdentity_TryCreate_NormalizesAndRejectsUnsafeValues), UiSchemaIdentity_TryCreate_NormalizesAndRejectsUnsafeValues);
        Run(nameof(UiCatalogIdentity_TryCreate_CarriesLocaleVariantAndRejectsBadHash), UiCatalogIdentity_TryCreate_CarriesLocaleVariantAndRejectsBadHash);
        Run(nameof(UiPanelMetadata_ValidatesSurfaceFocusPolicyAndOptionalKeys), UiPanelMetadata_ValidatesSurfaceFocusPolicyAndOptionalKeys);
        Run(nameof(UiRendererCapabilities_ValidatesLimitsAndUnknownFlags), UiRendererCapabilities_ValidatesLimitsAndUnknownFlags);
        Run(nameof(UiActionDescriptor_ValidatesContextMenuShape), UiActionDescriptor_ValidatesContextMenuShape);
        Run(nameof(UiActionDescriptor_ValidatesServerAuthoredAvailability), UiActionDescriptor_ValidatesServerAuthoredAvailability);
        Run(nameof(UiStateBindingDescriptor_ValidatesPerBindingFallback), UiStateBindingDescriptor_ValidatesPerBindingFallback);
        Run(nameof(UiSchemaDocument_ValidatesReachableTreeActionsBindingsAndCapabilities), UiSchemaDocument_ValidatesReachableTreeActionsBindingsAndCapabilities);
        Run(nameof(UiSchemaDocument_RejectsInvalidTreeShapes), UiSchemaDocument_RejectsInvalidTreeShapes);
        Run(nameof(UiSchemaDocument_RejectsDuplicateActionsBindingsAndMissingCapabilities), UiSchemaDocument_RejectsDuplicateActionsBindingsAndMissingCapabilities);
        Run(nameof(UiModelDocument_ValidatesPayloadSlotsAgainstSchema), UiModelDocument_ValidatesPayloadSlotsAgainstSchema);
        Run(nameof(UiModelDocument_RejectsMissingUnknownDuplicateAndWrongTypeSlots), UiModelDocument_RejectsMissingUnknownDuplicateAndWrongTypeSlots);
        Run(nameof(UiModelSlotValue_ValidatesStringAndNumberConstraints), UiModelSlotValue_ValidatesStringAndNumberConstraints);
        Run(nameof(UiSchemaCompatibilityContext_ReusesValidatedSchemaForModelAndUpdateChecks), UiSchemaCompatibilityContext_ReusesValidatedSchemaForModelAndUpdateChecks);
        Run(nameof(UiSchemaCompatibilityContext_SeparatesExactIdentityFromSlotCompatibility), UiSchemaCompatibilityContext_SeparatesExactIdentityFromSlotCompatibility);
        Run(nameof(UiModelUpdateDocument_ValidatesFullPatchScopedAndRepeaterUpdates), UiModelUpdateDocument_ValidatesFullPatchScopedAndRepeaterUpdates);
        Run(nameof(UiModelUpdateDocument_RejectsInvalidUpdateShapes), UiModelUpdateDocument_RejectsInvalidUpdateShapes);
        Run(nameof(UiActionRequestAndResult_ValidateSequenceContextFieldsAndUpdates), UiActionRequestAndResult_ValidateSequenceContextFieldsAndUpdates);
        Run(nameof(UiActionRequestAndResult_RejectUnavailableStaleAndMismatchedResponses), UiActionRequestAndResult_RejectUnavailableStaleAndMismatchedResponses);
        Run(nameof(UiContractJsonCodec_RoundTripsCoreContracts), UiContractJsonCodec_RoundTripsCoreContracts);
        Run(nameof(UiNodeKind_ReservesExpectedEarlyNodeIds), UiNodeKind_ReservesExpectedEarlyNodeIds);

        Console.WriteLine("All UI kernel contract tests passed.");
    }

    private static void UiSchemaIdentity_TryCreate_NormalizesAndRejectsUnsafeValues()
    {
        string hash = new('A', 64);
        Assert.True(UiSchemaIdentity.TryCreate(
            " shard/main ",
            " login.character_select ",
            7,
            hash,
            1,
            out UiSchemaIdentity identity,
            out string error), error);

        Assert.Equal("shard/main", identity.ServerIdentity);
        Assert.Equal("login.character_select", identity.PanelType);
        Assert.Equal(7, identity.SchemaRevision);
        Assert.Equal(new string('a', 64), identity.SchemaHashHex);
        Assert.Equal(1, identity.ProtocolVersion);

        Assert.False(UiSchemaIdentity.TryCreate("server", "../login", 1, hash, 1, out _, out error));
        Assert.Equal("PanelType cannot contain traversal-like path segments.", error);

        Assert.False(UiSchemaIdentity.TryCreate("server", "login", -1, hash, 1, out _, out error));
        Assert.Equal("Schema revision cannot be negative.", error);

        Assert.False(UiSchemaIdentity.TryCreate("server", "login", 1, "abc", 1, out _, out error));
        Assert.Equal("SchemaHashHex must be 64 hexadecimal characters.", error);
    }

    private static void UiCatalogIdentity_TryCreate_CarriesLocaleVariantAndRejectsBadHash()
    {
        string hash = new('B', 64);
        Assert.True(UiCatalogIdentity.TryCreate(
            "server",
            "craft.blacksmithy",
            3,
            hash,
            1,
            " en-US ",
            out UiCatalogIdentity identity,
            out string error), error);

        Assert.Equal("server", identity.ServerIdentity);
        Assert.Equal("craft.blacksmithy", identity.CatalogType);
        Assert.Equal(3, identity.CatalogRevision);
        Assert.Equal(new string('b', 64), identity.CatalogHashHex);
        Assert.Equal("en-US", identity.LocaleOrVariant);

        Assert.False(UiCatalogIdentity.TryCreate("server", "craft", 1, new string('x', 64), 1, null, out _, out error));
        Assert.Equal("CatalogHashHex must be hexadecimal.", error);
    }

    private static void UiPanelMetadata_ValidatesSurfaceFocusPolicyAndOptionalKeys()
    {
        var metadata = new UiPanelMetadata(
            PanelType: "inventory.grid",
            SurfaceTarget: UiSurfaceTarget.Menu,
            FocusMode: UiFocusMode.Normal,
            ExclusiveGroup: null,
            ZOrderClass: 10,
            OpenPolicy: UiOpenPolicy.SingletonByKey,
            BackPolicy: UiBackPolicy.CloseAndFocusParent,
            TooltipPolicy: UiTooltipPolicy.Allow,
            BlocksGameplayInput: true,
            PersistenceKey: "inventory/main");

        Assert.Valid(metadata.Validate());

        var invalid = metadata with { SurfaceTarget = (UiSurfaceTarget)255 };
        Assert.InvalidContains(invalid.Validate(), "Surface target");

        invalid = metadata with { ExclusiveGroup = "../session" };
        Assert.InvalidContains(invalid.Validate(), "ExclusiveGroup");
    }

    private static void UiRendererCapabilities_ValidatesLimitsAndUnknownFlags()
    {
        var capabilities = new UiRendererCapabilities(
            ProtocolVersion: 1,
            CapabilityFlags: UiCapabilityFlags.CatalogCacheV1 | UiCapabilityFlags.RichTooltipsV1,
            MaxSchemaBytes: 64 * 1024,
            MaxModelBytes: 16 * 1024,
            MaxNodeCount: 512,
            MaxTreeDepth: 32,
            MaxRepeaterRows: 256,
            MaxTableColumns: 24);

        Assert.Valid(capabilities.Validate());

        Assert.InvalidContains((capabilities with { ProtocolVersion = 0 }).Validate(), "Protocol version");
        Assert.InvalidContains((capabilities with { MaxNodeCount = 0 }).Validate(), "Renderer limits");
        Assert.InvalidContains((capabilities with { CapabilityFlags = (UiCapabilityFlags)(1UL << 60) }).Validate(), "unknown");
    }

    private static void UiActionDescriptor_ValidatesContextMenuShape()
    {
        var action = new UiActionDescriptor(
            ActionId: "item.equip",
            Label: "Equip",
            IconId: "icons/items/equip",
            Role: UiActionRole.Primary,
            ContextKind: UiActionContextKind.Item,
            RequiresConfirm: false,
            ChildMenuKey: null);

        Assert.Valid(action.Validate());

        Assert.InvalidContains((action with { ActionId = "" }).Validate(), "ActionId");
        Assert.InvalidContains((action with { Label = "" }).Validate(), "label");
        Assert.InvalidContains((action with { ContextKind = (UiActionContextKind)99 }).Validate(), "context");

        var nested = action with { ActionId = "entity.talk.more", ContextKind = UiActionContextKind.Entity, ChildMenuKey = "talk/more" };
        Assert.Valid(nested.Validate());
    }

    private static void UiActionDescriptor_ValidatesServerAuthoredAvailability()
    {
        var unavailable = new UiActionDescriptor(
            "entity.talk",
            "Talk",
            null,
            UiActionRole.Normal,
            UiActionContextKind.Entity,
            RequiresConfirm: false,
            ChildMenuKey: null,
            UiActionAvailability.OutOfRange,
            "Move closer");
        Assert.Valid(unavailable.Validate());

        Assert.InvalidContains((unavailable with { Availability = (UiActionAvailability)255 }).Validate(), "availability");
        Assert.InvalidContains((unavailable with
        {
            Availability = UiActionAvailability.Available,
            AvailabilityReason = "not needed"
        }).Validate(), "Available actions");
    }

    private static void UiStateBindingDescriptor_ValidatesPerBindingFallback()
    {
        var descriptor = new UiStateBindingDescriptor(
            SlotId: "healthCurrent",
            SourceId: "EntityState",
            TargetId: "entity:123",
            FieldId: "Health.Current",
            Fallback: new UiStateBindingFallback(
                UiBindingFallbackBehavior.Stale,
                UiBindingFallbackBehavior.Hidden,
                UiBindingFallbackBehavior.Stale,
                StaleAfterMilliseconds: 1500));

        Assert.Valid(descriptor.Validate());

        Assert.InvalidContains((descriptor with { SlotId = "" }).Validate(), "SlotId");
        Assert.InvalidContains((descriptor with
        {
            Fallback = descriptor.Fallback with { StaleAfterMilliseconds = -1 }
        }).Validate(), "stale timeout");
    }

    private static void UiContractJsonCodec_RoundTripsCoreContracts()
    {
        var metadata = new UiPanelMetadata(
            PanelType: "login",
            SurfaceTarget: UiSurfaceTarget.Exclusive,
            FocusMode: UiFocusMode.Exclusive,
            ExclusiveGroup: "SessionAdmission",
            ZOrderClass: 100,
            OpenPolicy: UiOpenPolicy.ReplaceCurrent,
            BackPolicy: UiBackPolicy.SendAction,
            TooltipPolicy: UiTooltipPolicy.SuppressExternal,
            BlocksGameplayInput: true,
            PersistenceKey: null);

        byte[] encoded = UiContractJsonCodec.Encode(metadata);
        UiPanelMetadata decoded = UiContractJsonCodec.Decode<UiPanelMetadata>(encoded);

        Assert.Equal(metadata, decoded);
        Assert.Valid(decoded.Validate());

        var action = new UiActionDescriptor(
            "world.interact",
            "Interact",
            null,
            UiActionRole.Normal,
            UiActionContextKind.World,
            RequiresConfirm: false,
            ChildMenuKey: "interact/root");

        encoded = UiContractJsonCodec.Encode(action);
        UiActionDescriptor decodedAction = UiContractJsonCodec.Decode<UiActionDescriptor>(encoded);
        Assert.Equal(action, decodedAction);
        Assert.Valid(decodedAction.Validate());

        UiSchemaDocument schema = CreateValidSchemaDocument();
        encoded = UiContractJsonCodec.Encode(schema);
        UiSchemaDocument decodedSchema = UiContractJsonCodec.Decode<UiSchemaDocument>(encoded);
        Assert.Equal(schema.Identity, decodedSchema.Identity);
        Assert.Equal(schema.Metadata, decodedSchema.Metadata);
        Assert.Equal(schema.RootNodeId, decodedSchema.RootNodeId);
        Assert.Equal(schema.Nodes.Count, decodedSchema.Nodes.Count);
        Assert.Equal(schema.Actions.Count, decodedSchema.Actions.Count);
        Assert.Equal(schema.StateBindings.Count, decodedSchema.StateBindings.Count);
        Assert.Valid(decodedSchema.Validate(rendererCapabilities: CreateRendererCapabilities()));

        UiModelDocument model = CreateValidModelDocument(schema);
        encoded = UiContractJsonCodec.Encode(model);
        UiModelDocument decodedModel = UiContractJsonCodec.Decode<UiModelDocument>(encoded);
        Assert.Equal(model.SchemaIdentity, decodedModel.SchemaIdentity);
        Assert.Equal(model.Values.Count, decodedModel.Values.Count);
        Assert.Valid(decodedModel.ValidateAgainst(schema));

        UiModelUpdateDocument update = CreateValidSlotPatch(schema);
        encoded = UiContractJsonCodec.Encode(update);
        UiModelUpdateDocument decodedUpdate = UiContractJsonCodec.Decode<UiModelUpdateDocument>(encoded);
        Assert.Equal(update.PanelInstanceId, decodedUpdate.PanelInstanceId);
        Assert.Equal(update.Sequence, decodedUpdate.Sequence);
        Assert.Valid(decodedUpdate.ValidateAgainst(schema));

        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);

        var request = new UiActionRequestDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            ClientSequence: 77,
            ActionId: "hud.open_character",
            UiActionContextKind.Panel,
            ContextId: null,
            FieldValues: [UiModelSlotValue.Integer("levelValue", 12)]);
        encoded = UiContractJsonCodec.Encode(request);
        UiActionRequestDocument decodedRequest = UiContractJsonCodec.Decode<UiActionRequestDocument>(encoded);
        Assert.Equal(request.ClientSequence, decodedRequest.ClientSequence);
        Assert.Valid(decodedRequest.ValidateAgainst(context!));

        var result = new UiActionResultDocument(
            "panel/1",
            schema.Identity,
            ClientSequence: 77,
            ActionId: "hud.open_character",
            UiActionResultKind.Accepted,
            UiActionFailureReason.None,
            Message: null,
            ModelUpdates: []);
        encoded = UiContractJsonCodec.Encode(result);
        UiActionResultDocument decodedResult = UiContractJsonCodec.Decode<UiActionResultDocument>(encoded);
        Assert.Equal(result.ClientSequence, decodedResult.ClientSequence);
        Assert.Valid(decodedResult.ValidateAgainst(context!));
    }

    private static void UiSchemaDocument_ValidatesReachableTreeActionsBindingsAndCapabilities()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        Assert.Valid(schema.Validate(rendererCapabilities: CreateRendererCapabilities()));

        var noCapabilityRenderer = CreateRendererCapabilities() with
        {
            CapabilityFlags = UiCapabilityFlags.ContextMenusV1 | UiCapabilityFlags.RuntimeStateBindingsV1
        };
        Assert.Valid(schema.Validate(rendererCapabilities: noCapabilityRenderer));
    }

    private static void UiSchemaDocument_RejectsInvalidTreeShapes()
    {
        UiSchemaDocument valid = CreateValidSchemaDocument();

        UiSchemaDocument duplicateNode = valid with
        {
            Nodes =
            [
                .. valid.Nodes,
                valid.Nodes[0] with { Text = "duplicate" }
            ]
        };
        Assert.InvalidContains(duplicateNode.Validate(), "Duplicate node id");

        UiSchemaDocument missingChild = valid with
        {
            Nodes =
            [
                valid.Nodes[0] with { Children = [2, 999] },
                .. valid.Nodes.Skip(1),
            ]
        };
        Assert.InvalidContains(missingChild.Validate(), "does not exist");

        UiSchemaDocument cycle = valid with
        {
            Nodes =
            [
                valid.Nodes[0] with { Children = [2] },
                valid.Nodes[1] with { Children = [1] },
                .. valid.Nodes.Skip(2),
            ]
        };
        Assert.InvalidContains(cycle.Validate(), "cycle");

        UiSchemaDocument unreachable = valid with
        {
            Nodes =
            [
                valid.Nodes[0],
                .. valid.Nodes.Skip(1),
                new UiSchemaNodeDescriptor(99, UiNodeKind.Text, null, "orphan", []),
            ]
        };
        Assert.InvalidContains(unreachable.Validate(), "not reachable");

        var shallowLimits = UiSchemaValidationLimits.Default with { MaxTreeDepth = 1 };
        Assert.InvalidContains(valid.Validate(shallowLimits), "depth");
    }

    private static void UiSchemaDocument_RejectsDuplicateActionsBindingsAndMissingCapabilities()
    {
        UiSchemaDocument valid = CreateValidSchemaDocument();

        UiSchemaDocument duplicateActions = valid with
        {
            Actions =
            [
                .. valid.Actions,
                valid.Actions[0] with { Label = "Again" }
            ]
        };
        Assert.InvalidContains(duplicateActions.Validate(), "Duplicate action");

        UiSchemaDocument duplicateBindings = valid with
        {
            StateBindings =
            [
                .. valid.StateBindings,
                valid.StateBindings[0] with { SourceId = "LocalPlayerState" }
            ]
        };
        Assert.InvalidContains(duplicateBindings.Validate(), "Duplicate state binding");

        UiSchemaDocument fixedLayoutRequired = valid with
        {
            Nodes =
            [
                valid.Nodes[0],
                valid.Nodes[1],
                valid.Nodes[2] with { RequiredCapabilities = UiCapabilityFlags.FixedLayoutV1 },
                .. valid.Nodes.Skip(3),
            ]
        };
        Assert.InvalidContains(fixedLayoutRequired.Validate(rendererCapabilities: CreateRendererCapabilities()), "missing required");
    }

    private static void UiModelDocument_ValidatesPayloadSlotsAgainstSchema()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        UiModelDocument model = CreateValidModelDocument(schema);

        Assert.Valid(model.ValidateAgainst(schema));
    }

    private static void UiModelDocument_RejectsMissingUnknownDuplicateAndWrongTypeSlots()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        UiModelDocument valid = CreateValidModelDocument(schema);

        var missing = valid with
        {
            Values = [UiModelSlotValue.Integer("levelValue", 12)]
        };
        Assert.InvalidContains(missing.ValidateAgainst(schema), "missing required");

        var unknown = valid with
        {
            Values = [.. valid.Values, UiModelSlotValue.String("unknownSlot", "nope")]
        };
        Assert.InvalidContains(unknown.ValidateAgainst(schema), "unknown slot");

        var duplicate = valid with
        {
            Values = [.. valid.Values, UiModelSlotValue.String("titleText", "again")]
        };
        Assert.InvalidContains(duplicate.ValidateAgainst(schema), "duplicate slot");

        var wrongType = valid with
        {
            Values =
            [
                UiModelSlotValue.Integer("titleText", 42),
                UiModelSlotValue.Integer("levelValue", 12),
            ]
        };
        Assert.InvalidContains(wrongType.ValidateAgainst(schema), "expected String");

        Assert.True(UiSchemaIdentity.TryCreate(
            "server/main",
            "other.panel",
            1,
            new string('e', 64),
            1,
            out UiSchemaIdentity otherIdentity,
            out string error), error);
        var wrongIdentity = valid with { SchemaIdentity = otherIdentity };
        Assert.InvalidContains(wrongIdentity.ValidateAgainst(schema), "identity");
    }

    private static void UiModelSlotValue_ValidatesStringAndNumberConstraints()
    {
        var text = new UiPayloadSlotDescriptor("name", UiModelValueKind.String, Required: true, MaxStringLength: 4);
        Assert.Valid(UiModelSlotValue.String("name", "Rune").Validate(text));
        Assert.InvalidContains(UiModelSlotValue.String("name", "TooLong").Validate(text), "cannot exceed");

        var number = new UiPayloadSlotDescriptor("progress", UiModelValueKind.Number, Required: true, MinNumber: 0, MaxNumber: 1);
        Assert.Valid(UiModelSlotValue.Number("progress", 0.5).Validate(number));
        Assert.InvalidContains(UiModelSlotValue.Number("progress", double.NaN).Validate(number), "finite");
        Assert.InvalidContains(UiModelSlotValue.Number("progress", 1.5).Validate(number), "exceeds");

        var optional = new UiPayloadSlotDescriptor("optionalText", UiModelValueKind.String, Required: false);
        Assert.Valid(UiModelSlotValue.Null("optionalText").Validate(optional));

        var required = optional with { Required = true };
        Assert.InvalidContains(UiModelSlotValue.Null("optionalText").Validate(required), "Required");
    }

    private static void UiSchemaCompatibilityContext_ReusesValidatedSchemaForModelAndUpdateChecks()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);

        UiModelDocument model = CreateValidModelDocument(schema);
        Assert.Valid(model.ValidateAgainst(context!));
        Assert.Valid(CreateValidSlotPatch(schema).ValidateAgainst(context!));
    }

    private static void UiSchemaCompatibilityContext_SeparatesExactIdentityFromSlotCompatibility()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);

        Assert.True(UiSchemaIdentity.TryCreate(
            schema.Identity.ServerIdentity,
            schema.Identity.PanelType,
            schema.Identity.SchemaRevision + 1,
            new string('f', 64),
            schema.Identity.ProtocolVersion,
            out UiSchemaIdentity otherIdentity,
            out string error), error);

        UiModelDocument sameSlotsWrongIdentity = CreateValidModelDocument(schema) with
        {
            SchemaIdentity = otherIdentity
        };
        Assert.InvalidContains(sameSlotsWrongIdentity.ValidateAgainst(context!), "identity");

        UiModelDocument rightIdentityWrongSlots = CreateValidModelDocument(schema) with
        {
            Values = [UiModelSlotValue.String("unknownSlot", "nope")]
        };
        Assert.InvalidContains(rightIdentityWrongSlots.ValidateAgainst(context!), "unknown slot");
    }

    private static void UiModelUpdateDocument_ValidatesFullPatchScopedAndRepeaterUpdates()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();

        var full = new UiModelUpdateDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 1,
            UiModelUpdateKind.FullModel,
            ScopeId: null,
            SlotValues: CreateValidModelDocument(schema).Values,
            ClearedSlotIds: [],
            RowDeltas: []);
        Assert.Valid(full.ValidateAgainst(schema));

        Assert.Valid(CreateValidSlotPatch(schema).ValidateAgainst(schema));

        var scoped = new UiModelUpdateDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 3,
            UiModelUpdateKind.ScopedPatch,
            ScopeId: "skills/page/2",
            SlotValues: [UiModelSlotValue.Integer("levelValue", 14)],
            ClearedSlotIds: [],
            RowDeltas: []);
        Assert.Valid(scoped.ValidateAgainst(schema));

        var rowUpdate = new UiModelUpdateDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 4,
            UiModelUpdateKind.RepeaterRows,
            ScopeId: null,
            SlotValues: [],
            ClearedSlotIds: [],
            RowDeltas:
            [
                new UiRepeaterRowDelta(
                    "characters",
                    "character/1",
                    UiRepeaterRowUpdateKind.Add,
                    [UiModelSlotValue.String("name", "Lira")]),
                new UiRepeaterRowDelta(
                    "characters",
                    "character/2",
                    UiRepeaterRowUpdateKind.Remove,
                    []),
            ]);
        Assert.Valid(rowUpdate.ValidateAgainst(schema));

        var superseded = new UiModelUpdateDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 5,
            UiModelUpdateKind.SchemaSuperseded,
            ScopeId: null,
            SlotValues: [],
            ClearedSlotIds: [],
            RowDeltas: []);
        Assert.Valid(superseded.ValidateAgainst(schema));
    }

    private static void UiModelUpdateDocument_RejectsInvalidUpdateShapes()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();

        var patchUnknown = CreateValidSlotPatch(schema) with
        {
            SlotValues = [UiModelSlotValue.String("missing", "nope")]
        };
        Assert.InvalidContains(patchUnknown.ValidateAgainst(schema), "unknown slot");

        var patchClearRequired = CreateValidSlotPatch(schema) with
        {
            SlotValues = [],
            ClearedSlotIds = ["titleText"]
        };
        Assert.InvalidContains(patchClearRequired.ValidateAgainst(schema), "cannot clear required");

        var scopedMissingScope = CreateValidSlotPatch(schema) with
        {
            Kind = UiModelUpdateKind.ScopedPatch
        };
        Assert.InvalidContains(scopedMissingScope.ValidateAgainst(schema), "scope id");

        var rowWithDuplicateSlots = new UiModelUpdateDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 9,
            UiModelUpdateKind.RepeaterRows,
            ScopeId: null,
            SlotValues: [],
            ClearedSlotIds: [],
            RowDeltas:
            [
                new UiRepeaterRowDelta(
                    "characters",
                    "character/1",
                    UiRepeaterRowUpdateKind.Update,
                    [
                        UiModelSlotValue.String("name", "A"),
                        UiModelSlotValue.String("name", "B"),
                    ]),
            ]);
        Assert.InvalidContains(rowWithDuplicateSlots.ValidateAgainst(schema), "duplicate slot");

        var fullMissingRequired = new UiModelUpdateDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 10,
            UiModelUpdateKind.FullModel,
            ScopeId: null,
            SlotValues: [UiModelSlotValue.Integer("levelValue", 12)],
            ClearedSlotIds: [],
            RowDeltas: []);
        Assert.InvalidContains(fullMissingRequired.ValidateAgainst(schema), "missing required");
    }

    private static void UiActionRequestAndResult_ValidateSequenceContextFieldsAndUpdates()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);

        var request = new UiActionRequestDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            ClientSequence: 42,
            ActionId: "hud.open_character",
            UiActionContextKind.Panel,
            ContextId: "hud/status",
            FieldValues: [UiModelSlotValue.Integer("levelValue", 15)]);
        Assert.Valid(request.ValidateAgainst(context!));

        var result = new UiActionResultDocument(
            "panel/1",
            schema.Identity,
            ClientSequence: 42,
            ActionId: "hud.open_character",
            UiActionResultKind.Accepted,
            UiActionFailureReason.None,
            Message: "Opened",
            ModelUpdates: [CreateValidSlotPatch(schema)]);
        Assert.Valid(result.ValidateAgainst(context!));
    }

    private static void UiActionRequestAndResult_RejectUnavailableStaleAndMismatchedResponses()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        UiSchemaDocument unavailableSchema = schema with
        {
            Actions =
            [
                schema.Actions[0] with
                {
                    Availability = UiActionAvailability.OutOfRange,
                    AvailabilityReason = "Move closer"
                }
            ]
        };
        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(unavailableSchema, out UiSchemaCompatibilityContext? unavailableContext));
        Assert.NotNull(unavailableContext);

        var request = new UiActionRequestDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            ClientSequence: 42,
            ActionId: "hud.open_character",
            UiActionContextKind.Panel,
            ContextId: null,
            FieldValues: []);
        Assert.InvalidContains(request.ValidateAgainst(unavailableContext!), "not currently available");

        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);

        var unknownAction = request with { ActionId = "missing.action" };
        Assert.InvalidContains(unknownAction.ValidateAgainst(context!), "unknown action");

        var badField = request with { FieldValues = [UiModelSlotValue.String("missingSlot", "nope")] };
        Assert.InvalidContains(badField.ValidateAgainst(context!), "unknown field slot");

        var rejectedWithoutReason = new UiActionResultDocument(
            "panel/1",
            schema.Identity,
            ClientSequence: 42,
            ActionId: "hud.open_character",
            UiActionResultKind.Rejected,
            UiActionFailureReason.None,
            Message: "Nope",
            ModelUpdates: []);
        Assert.InvalidContains(rejectedWithoutReason.ValidateAgainst(context!), "require a failure reason");

        var staleTarget = rejectedWithoutReason with
        {
            FailureReason = UiActionFailureReason.StaleTarget,
            Message = "Target changed"
        };
        Assert.Valid(staleTarget.ValidateAgainst(context!));

        var acceptedWithReason = staleTarget with
        {
            Kind = UiActionResultKind.Accepted
        };
        Assert.InvalidContains(acceptedWithReason.ValidateAgainst(context!), "cannot carry");
    }

    private static void UiNodeKind_ReservesExpectedEarlyNodeIds()
    {
        Assert.Equal(1, (int)UiNodeKind.Panel);
        Assert.Equal(23, (int)UiNodeKind.Repeater);
        Assert.Equal(27, (int)UiNodeKind.ContextMenu);
        Assert.Equal(28, (int)UiNodeKind.RadialMenu);
        Assert.Equal(29, (int)UiNodeKind.InventoryGrid);
        Assert.Equal(30, (int)UiNodeKind.ContainerCanvas);
    }

    private static UiSchemaDocument CreateValidSchemaDocument()
    {
        Assert.True(UiSchemaIdentity.TryCreate(
            "server/main",
            "hud.status",
            1,
            new string('c', 64),
            1,
            out UiSchemaIdentity identity,
            out string error), error);

        var metadata = new UiPanelMetadata(
            PanelType: "hud.status",
            SurfaceTarget: UiSurfaceTarget.Hud,
            FocusMode: UiFocusMode.Passive,
            ExclusiveGroup: null,
            ZOrderClass: 1,
            OpenPolicy: UiOpenPolicy.SingletonByKey,
            BackPolicy: UiBackPolicy.None,
            TooltipPolicy: UiTooltipPolicy.Allow,
            BlocksGameplayInput: false,
            PersistenceKey: null);

        return new UiSchemaDocument(
            identity,
            metadata,
            RootNodeId: 1,
            Nodes:
            [
                new UiSchemaNodeDescriptor(1, UiNodeKind.Panel, null, null, [2]),
                new UiSchemaNodeDescriptor(2, UiNodeKind.Row, null, null, [3, 4, 5, 6]),
                new UiSchemaNodeDescriptor(3, UiNodeKind.DynamicText, "healthText", "Health", []),
                new UiSchemaNodeDescriptor(4, UiNodeKind.Button, null, "Open", []),
                new UiSchemaNodeDescriptor(5, UiNodeKind.DynamicText, "titleText", "Title", []),
                new UiSchemaNodeDescriptor(6, UiNodeKind.DynamicText, "levelValue", "Level", []),
            ],
            PayloadSlots:
            [
                new UiPayloadSlotDescriptor("titleText", UiModelValueKind.String, Required: true, MaxStringLength: 64),
                new UiPayloadSlotDescriptor("levelValue", UiModelValueKind.Integer, Required: false, MinNumber: 1, MaxNumber: 100),
            ],
            Actions:
            [
                new UiActionDescriptor(
                    "hud.open_character",
                    "Open Character",
                    null,
                    UiActionRole.Normal,
                    UiActionContextKind.Panel,
                    RequiresConfirm: false,
                    ChildMenuKey: null)
            ],
            StateBindings:
            [
                new UiStateBindingDescriptor(
                    "healthText",
                    "EntityState",
                    "entity:local",
                    "Health.Current",
                    new UiStateBindingFallback(
                        UiBindingFallbackBehavior.Stale,
                        UiBindingFallbackBehavior.Hidden,
                        UiBindingFallbackBehavior.Stale,
                        1500))
            ]);
    }

    private static UiModelDocument CreateValidModelDocument(UiSchemaDocument schema)
    {
        return new UiModelDocument(
            schema.Identity,
            Values:
            [
                UiModelSlotValue.String("titleText", "Status"),
                UiModelSlotValue.Integer("levelValue", 12),
            ]);
    }

    private static UiModelUpdateDocument CreateValidSlotPatch(UiSchemaDocument schema)
    {
        return new UiModelUpdateDocument(
            "panel/1",
            schema.Identity,
            CatalogIdentity: null,
            Sequence: 2,
            UiModelUpdateKind.SlotPatch,
            ScopeId: null,
            SlotValues: [UiModelSlotValue.Integer("levelValue", 13)],
            ClearedSlotIds: [],
            RowDeltas: []);
    }

    private static UiRendererCapabilities CreateRendererCapabilities()
    {
        return new UiRendererCapabilities(
            ProtocolVersion: 1,
            CapabilityFlags: UiCapabilityFlags.ContextMenusV1 | UiCapabilityFlags.RuntimeStateBindingsV1,
            MaxSchemaBytes: 64 * 1024,
            MaxModelBytes: 16 * 1024,
            MaxNodeCount: 512,
            MaxTreeDepth: 32,
            MaxRepeaterRows: 256,
            MaxTableColumns: 24);
    }

    private static void Run(string name, Action test)
    {
        try
        {
            test();
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
            Environment.ExitCode = 1;
            throw;
        }
    }
}

internal static class Assert
{
    public static void True(bool condition, string? message = null)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message ?? "Expected true.");
        }
    }

    public static void False(bool condition, string? message = null)
    {
        if (condition)
        {
            throw new InvalidOperationException(message ?? "Expected false.");
        }
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    public static void NotNull(object? value)
    {
        if (value is null)
        {
            throw new InvalidOperationException("Expected non-null value.");
        }
    }

    public static void Valid(UiValidationResult result)
    {
        if (!result.IsValid)
        {
            throw new InvalidOperationException($"Expected valid result, got '{result.Error}'.");
        }
    }

    public static void InvalidContains(UiValidationResult result, string text)
    {
        if (result.IsValid)
        {
            throw new InvalidOperationException("Expected invalid result.");
        }

        if (!result.Error.Contains(text, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Expected error containing '{text}', got '{result.Error}'.");
        }
    }
}
