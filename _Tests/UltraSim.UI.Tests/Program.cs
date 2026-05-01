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
        Run(nameof(UiActionMenuDescriptor_ValidatesContextAndRadialMenus), UiActionMenuDescriptor_ValidatesContextAndRadialMenus);
        Run(nameof(UiActionMenuDescriptor_RejectsInvalidCapabilitiesAndDuplicateActions), UiActionMenuDescriptor_RejectsInvalidCapabilitiesAndDuplicateActions);
        Run(nameof(UiStateBindingDescriptor_ValidatesPerBindingFallback), UiStateBindingDescriptor_ValidatesPerBindingFallback);
        Run(nameof(UiSchemaDocument_ValidatesReachableTreeActionsBindingsAndCapabilities), UiSchemaDocument_ValidatesReachableTreeActionsBindingsAndCapabilities);
        Run(nameof(UiSchemaDocument_RejectsInvalidTreeShapes), UiSchemaDocument_RejectsInvalidTreeShapes);
        Run(nameof(UiSchemaDocument_RejectsDuplicateActionsBindingsAndMissingCapabilities), UiSchemaDocument_RejectsDuplicateActionsBindingsAndMissingCapabilities);
        Run(nameof(UiModelDocument_ValidatesPayloadSlotsAgainstSchema), UiModelDocument_ValidatesPayloadSlotsAgainstSchema);
        Run(nameof(UiModelDocument_RejectsMissingUnknownDuplicateAndWrongTypeSlots), UiModelDocument_RejectsMissingUnknownDuplicateAndWrongTypeSlots);
        Run(nameof(UiModelSlotValue_ValidatesStringAndNumberConstraints), UiModelSlotValue_ValidatesStringAndNumberConstraints);
        Run(nameof(UiSchemaCompatibilityContext_ReusesValidatedSchemaForModelAndUpdateChecks), UiSchemaCompatibilityContext_ReusesValidatedSchemaForModelAndUpdateChecks);
        Run(nameof(UiSchemaCompatibilityContext_SeparatesExactIdentityFromSlotCompatibility), UiSchemaCompatibilityContext_SeparatesExactIdentityFromSlotCompatibility);
        Run(nameof(UiPanelOpenDocument_ValidatesColdAndHotOpenPaths), UiPanelOpenDocument_ValidatesColdAndHotOpenPaths);
        Run(nameof(UiPanelOpenDocument_RejectsMissingDefinitionsAndIdentityMismatches), UiPanelOpenDocument_RejectsMissingDefinitionsAndIdentityMismatches);
        Run(nameof(UiDefinitionCache_StoresSchemasAndReturnsHotCompatibilityContext), UiDefinitionCache_StoresSchemasAndReturnsHotCompatibilityContext);
        Run(nameof(UiDefinitionCache_ReportsMissSupersededAndCapacityEviction), UiDefinitionCache_ReportsMissSupersededAndCapacityEviction);
        Run(nameof(UiDefinitionCache_TracksCatalogIdentityHitsSupersessionAndServerClear), UiDefinitionCache_TracksCatalogIdentityHitsSupersessionAndServerClear);
        Run(nameof(UiModelUpdateDocument_ValidatesFullPatchScopedAndRepeaterUpdates), UiModelUpdateDocument_ValidatesFullPatchScopedAndRepeaterUpdates);
        Run(nameof(UiModelUpdateDocument_RejectsInvalidUpdateShapes), UiModelUpdateDocument_RejectsInvalidUpdateShapes);
        Run(nameof(UiActionRequestAndResult_ValidateSequenceContextFieldsAndUpdates), UiActionRequestAndResult_ValidateSequenceContextFieldsAndUpdates);
        Run(nameof(UiActionRequestAndResult_RejectUnavailableStaleAndMismatchedResponses), UiActionRequestAndResult_RejectUnavailableStaleAndMismatchedResponses);
        Run(nameof(UiTooltipDescriptor_ValidatesFlatStaticAndSlotBackedLines), UiTooltipDescriptor_ValidatesFlatStaticAndSlotBackedLines);
        Run(nameof(UiTooltipDescriptor_RejectsUnknownSlotsAndOversizedShapes), UiTooltipDescriptor_RejectsUnknownSlotsAndOversizedShapes);
        Run(nameof(UiPanelBuilder_BuildsValidatedSchemaModelAndOpenDocuments), UiPanelBuilder_BuildsValidatedSchemaModelAndOpenDocuments);
        Run(nameof(UiPanelBuilder_BuildsGroupsSectionsAndFormControls), UiPanelBuilder_BuildsGroupsSectionsAndFormControls);
        Run(nameof(UiStandardPanels_BuildMessageAndConfirmBoxesWithStableSchema), UiStandardPanels_BuildMessageAndConfirmBoxesWithStableSchema);
        Run(nameof(UiPanelBuilder_RejectsConflictingScopedDeclarations), UiPanelBuilder_RejectsConflictingScopedDeclarations);
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

        var grouped = action with { ActionId = "item.drop", MenuGroupKey = "inventory/actions" };
        Assert.Valid(grouped.Validate());
        Assert.InvalidContains((grouped with { MenuGroupKey = "../bad" }).Validate(), "MenuGroupKey");
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

    private static void UiActionMenuDescriptor_ValidatesContextAndRadialMenus()
    {
        var contextMenu = CreateValidContextMenu();
        Assert.Valid(contextMenu.Validate(CreateRendererCapabilities()));

        var radial = contextMenu with
        {
            MenuKey = "entity/interaction/root",
            PresentationKind = UiActionMenuPresentationKind.RadialMenu,
            ContextKind = UiActionContextKind.Entity,
            ContextId = "entity:local_target",
            CloseOnFocusLost = false,
            Actions =
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
            ]
        };
        Assert.Valid(radial.Validate(CreateRendererCapabilities()));
    }

    private static void UiActionMenuDescriptor_RejectsInvalidCapabilitiesAndDuplicateActions()
    {
        UiActionMenuDescriptor menu = CreateValidContextMenu();
        Assert.InvalidContains((menu with { MenuKey = "" }).Validate(), "MenuKey");
        Assert.InvalidContains((menu with { PresentationKind = (UiActionMenuPresentationKind)99 }).Validate(), "presentation");
        Assert.InvalidContains((menu with { ContextId = "../bad" }).Validate(), "ContextId");
        Assert.InvalidContains((menu with { Actions = [] }).Validate(), "at least one action");

        var duplicate = menu with
        {
            Actions =
            [
                menu.Actions[0],
                menu.Actions[0] with { Label = "Equip Again" },
            ]
        };
        Assert.InvalidContains(duplicate.Validate(), "duplicate action");

        var tooMany = menu with
        {
            Actions = Enumerable.Range(0, UiActionMenuDescriptor.MaxMenuActions + 1)
                .Select(i => menu.Actions[0] with { ActionId = $"item.action.{i}", Label = $"Action {i}" })
                .ToArray()
        };
        Assert.InvalidContains(tooMany.Validate(), "more than");

        var radial = menu with { PresentationKind = UiActionMenuPresentationKind.RadialMenu };
        var noRadialCapability = CreateRendererCapabilities() with
        {
            CapabilityFlags = UiCapabilityFlags.ContextMenusV1
        };
        Assert.InvalidContains(radial.Validate(noRadialCapability), "RadialMenusV1");
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

    private static void UiPanelBuilder_BuildsValidatedSchemaModelAndOpenDocuments()
    {
        UiBuiltPanel built = UiPanelBuilder
            .Create("server/main", "smoke.message_box", 1)
            .Panel(panel =>
            {
                panel.Column(column =>
                {
                    column.Text("Notice");
                    column.DynamicText("messageText", "Message", "Hello from the builder.", maxStringLength: 128);
                    column.Row(row => row.Button("message.ok", "OK", UiActionRole.Primary));
                });
            })
            .Build();

        Assert.Equal("smoke.message_box", built.Schema.Identity.PanelType);
        Assert.Equal("smoke.message_box", built.Schema.Metadata.PanelType);
        Assert.Equal(1, built.Schema.RootNodeId);
        UiSchemaNodeDescriptor root = built.Schema.Nodes.First(node => node.NodeId == built.Schema.RootNodeId);
        Assert.Equal(UiNodeKind.Panel, root.Kind);
        Assert.Equal(1, built.Schema.PayloadSlots.Count);
        Assert.Equal(1, built.Schema.Actions.Count);
        Assert.Equal("messageText", built.Schema.PayloadSlots[0].SlotId);
        Assert.Equal("message.ok", built.Schema.Actions[0].ActionId);
        Assert.Valid(built.Schema.Validate(rendererCapabilities: CreateRendererCapabilities()));
        Assert.Valid(built.Model.ValidateAgainst(built.Schema));

        UiPanelOpenDocument cold = built.ToColdOpen("panel/smoke/1");
        Assert.Valid(cold.ValidateCold(rendererCapabilities: CreateRendererCapabilities()));

        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(built.Schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);
        UiPanelOpenDocument hot = built.ToHotOpen("panel/smoke/2");
        Assert.Valid(hot.ValidateHot(context!));

        UiBuiltPanel rebuilt = UiPanelBuilder
            .Create("server/main", "smoke.message_box", 1)
            .Panel(panel =>
            {
                panel.Column(column =>
                {
                    column.Text("Notice");
                    column.DynamicText("messageText", "Message", "Hello from the builder.", maxStringLength: 128);
                    column.Row(row => row.Button("message.ok", "OK", UiActionRole.Primary));
                });
            })
            .Build();
        Assert.Equal(built.Schema.Identity.SchemaHashHex, rebuilt.Schema.Identity.SchemaHashHex);
    }

    private static void UiPanelBuilder_BuildsGroupsSectionsAndFormControls()
    {
        UiBuiltPanel built = UiPanelBuilder
            .Create("server/main", "settings.connection", 1)
            .Panel(panel =>
            {
                panel.Group("Connection", group =>
                {
                    group.Section("Account", section =>
                    {
                        section.TextInput("displayName", "Display Name", "Test Pilot", maxStringLength: 64);
                        section.Toggle("rememberAccount", "Remember Account", true);
                    });
                });
            })
            .Build();

        Assert.Valid(built.Schema.Validate(rendererCapabilities: CreateRendererCapabilities()));
        Assert.Valid(built.Model.ValidateAgainst(built.Schema));

        Assert.True(built.Schema.Nodes.Any(node => node.Kind == UiNodeKind.Group && string.Equals(node.Text, "Connection", StringComparison.Ordinal)));
        Assert.True(built.Schema.Nodes.Any(node => node.Kind == UiNodeKind.Section && string.Equals(node.Text, "Account", StringComparison.Ordinal)));
        Assert.True(built.Schema.Nodes.Any(node => node.Kind == UiNodeKind.TextInput && string.Equals(node.SlotId, "displayName", StringComparison.Ordinal)));
        Assert.True(built.Schema.Nodes.Any(node => node.Kind == UiNodeKind.Toggle && string.Equals(node.SlotId, "rememberAccount", StringComparison.Ordinal)));

        UiPayloadSlotDescriptor nameSlot = built.Schema.PayloadSlots.First(slot => string.Equals(slot.SlotId, "displayName", StringComparison.Ordinal));
        Assert.Equal(UiModelValueKind.String, nameSlot.ValueKind);
        Assert.Equal(64, nameSlot.MaxStringLength);

        UiPayloadSlotDescriptor toggleSlot = built.Schema.PayloadSlots.First(slot => string.Equals(slot.SlotId, "rememberAccount", StringComparison.Ordinal));
        Assert.Equal(UiModelValueKind.Boolean, toggleSlot.ValueKind);

        UiModelSlotValue nameValue = built.Model.Values.First(value => string.Equals(value.SlotId, "displayName", StringComparison.Ordinal));
        Assert.Equal("Test Pilot", nameValue.StringValue);

        UiModelSlotValue toggleValue = built.Model.Values.First(value => string.Equals(value.SlotId, "rememberAccount", StringComparison.Ordinal));
        Assert.True(toggleValue.BooleanValue);

        UiPanelOpenDocument cold = built.ToColdOpen("panel/settings/connection");
        Assert.Valid(cold.ValidateCold(rendererCapabilities: CreateRendererCapabilities()));
    }

    private static void UiStandardPanels_BuildMessageAndConfirmBoxesWithStableSchema()
    {
        UiBuiltPanel firstMessage = UiStandardPanels.MessageBox(
            "server/main",
            "Welcome",
            "Hello from the first model payload.");
        UiBuiltPanel secondMessage = UiStandardPanels.MessageBox(
            "server/main",
            "Warning",
            "Different body, same cached schema.");

        Assert.Equal(UiStandardPanels.MessageBoxPanelType, firstMessage.Schema.Identity.PanelType);
        Assert.Equal(UiSurfaceTarget.Modal, firstMessage.Schema.Metadata.SurfaceTarget);
        Assert.Equal(UiFocusMode.Modal, firstMessage.Schema.Metadata.FocusMode);
        Assert.True(firstMessage.Schema.Metadata.BlocksGameplayInput);
        Assert.Equal(firstMessage.Schema.Identity.SchemaHashHex, secondMessage.Schema.Identity.SchemaHashHex);
        Assert.Valid(firstMessage.Schema.Validate(rendererCapabilities: CreateRendererCapabilities()));
        Assert.Valid(firstMessage.Model.ValidateAgainst(firstMessage.Schema));
        Assert.Valid(secondMessage.Model.ValidateAgainst(firstMessage.Schema));

        UiModelSlotValue firstTitle = firstMessage.Model.Values.First(value => string.Equals(value.SlotId, UiStandardPanels.TitleSlotId, StringComparison.Ordinal));
        UiModelSlotValue secondTitle = secondMessage.Model.Values.First(value => string.Equals(value.SlotId, UiStandardPanels.TitleSlotId, StringComparison.Ordinal));
        Assert.Equal("Welcome", firstTitle.StringValue);
        Assert.Equal("Warning", secondTitle.StringValue);

        Assert.Equal(1, firstMessage.Schema.Actions.Count);
        Assert.Equal(UiStandardPanels.MessageOkActionId, firstMessage.Schema.Actions[0].ActionId);
        Assert.Equal(UiActionRole.Primary, firstMessage.Schema.Actions[0].Role);

        UiBuiltPanel confirm = UiStandardPanels.ConfirmBox(
            "server/main",
            "Delete Item",
            "Delete the selected item?",
            acceptText: "Delete",
            cancelText: "Keep");
        Assert.Equal(UiStandardPanels.ConfirmBoxPanelType, confirm.Schema.Identity.PanelType);
        Assert.Equal(2, confirm.Schema.Actions.Count);
        Assert.True(confirm.Schema.Actions.Any(action =>
            string.Equals(action.ActionId, UiStandardPanels.ConfirmAcceptActionId, StringComparison.Ordinal) &&
            action.Role == UiActionRole.Primary &&
            string.Equals(action.Label, "Delete", StringComparison.Ordinal)));
        Assert.True(confirm.Schema.Actions.Any(action =>
            string.Equals(action.ActionId, UiStandardPanels.ConfirmCancelActionId, StringComparison.Ordinal) &&
            action.Role == UiActionRole.Normal &&
            string.Equals(action.Label, "Keep", StringComparison.Ordinal)));
        Assert.Valid(confirm.ToColdOpen("panel/confirm/delete").ValidateCold(rendererCapabilities: CreateRendererCapabilities()));
    }

    private static void UiPanelBuilder_RejectsConflictingScopedDeclarations()
    {
        Assert.Throws<InvalidOperationException>(() =>
        {
            UiPanelBuilder
                .Create("server/main", "smoke.conflict", 1)
                .Panel(panel =>
                {
                    panel.DynamicText("messageText", "Message", "first", maxStringLength: 64);
                    panel.DynamicText("messageText", "Message", "second", maxStringLength: 64);
                })
                .Build();
        });

        var metadata = new UiPanelMetadata(
            "smoke.other",
            UiSurfaceTarget.Menu,
            UiFocusMode.Normal,
            ExclusiveGroup: null,
            ZOrderClass: 1,
            UiOpenPolicy.New,
            UiBackPolicy.CloseAndFocusParent,
            UiTooltipPolicy.Allow,
            BlocksGameplayInput: false,
            PersistenceKey: null);
        bool built = UiPanelBuilder
            .Create("server/main", "smoke.conflict", 1, metadata)
            .Panel(panel => panel.Text("Nope"))
            .TryBuild(out _, out string error);
        Assert.False(built);
        Assert.True(error.Contains("metadata panel type", StringComparison.OrdinalIgnoreCase));
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

        UiActionMenuDescriptor menu = CreateValidContextMenu();
        encoded = UiContractJsonCodec.Encode(menu);
        UiActionMenuDescriptor decodedMenu = UiContractJsonCodec.Decode<UiActionMenuDescriptor>(encoded);
        Assert.Equal(menu.MenuKey, decodedMenu.MenuKey);
        Assert.Equal(menu.Actions.Count, decodedMenu.Actions.Count);
        Assert.Valid(decodedMenu.Validate(CreateRendererCapabilities()));

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

        UiTooltipDescriptor tooltip = CreateValidTooltip();
        encoded = UiContractJsonCodec.Encode(tooltip);
        UiTooltipDescriptor decodedTooltip = UiContractJsonCodec.Decode<UiTooltipDescriptor>(encoded);
        Assert.Equal(tooltip.TooltipId, decodedTooltip.TooltipId);
        Assert.Equal(tooltip.Lines.Count, decodedTooltip.Lines.Count);
        Assert.Valid(decodedTooltip.Validate(context!));
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

        UiSchemaDocument missingNodeAction = valid with
        {
            Nodes =
            [
                .. valid.Nodes.Select(static node => node.Kind == UiNodeKind.Button
                    ? node with { ActionId = "missing.action" }
                    : node)
            ]
        };
        Assert.InvalidContains(missingNodeAction.Validate(rendererCapabilities: CreateRendererCapabilities()), "not declared");

        UiSchemaDocument actionOnText = valid with
        {
            Nodes =
            [
                .. valid.Nodes.Select(static node => node.Kind == UiNodeKind.Text || node.Kind == UiNodeKind.DynamicText
                    ? node with { ActionId = "hud.open_character" }
                    : node)
            ]
        };
        Assert.InvalidContains(actionOnText.Validate(rendererCapabilities: CreateRendererCapabilities()), "cannot declare");
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

    private static void UiPanelOpenDocument_ValidatesColdAndHotOpenPaths()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        UiModelDocument model = CreateValidModelDocument(schema);
        var cold = new UiPanelOpenDocument(
            "panel/open/status",
            schema.Identity,
            schema.CatalogIdentity,
            model,
            schema);
        Assert.Valid(cold.ValidateCold(rendererCapabilities: CreateRendererCapabilities()));

        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);
        var hot = cold with { SchemaDefinition = null };
        Assert.Valid(hot.ValidateHot(context!));

        byte[] encoded = UiContractJsonCodec.Encode(cold);
        UiPanelOpenDocument decoded = UiContractJsonCodec.Decode<UiPanelOpenDocument>(encoded);
        Assert.Equal(cold.PanelInstanceId, decoded.PanelInstanceId);
        Assert.Equal(cold.SchemaIdentity, decoded.SchemaIdentity);
        Assert.True(decoded.SchemaDefinition is not null, "cold open schema definition roundtrip");
        Assert.Valid(decoded.ValidateCold(rendererCapabilities: CreateRendererCapabilities()));
    }

    private static void UiPanelOpenDocument_RejectsMissingDefinitionsAndIdentityMismatches()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        UiModelDocument model = CreateValidModelDocument(schema);
        var hot = new UiPanelOpenDocument(
            "panel/open/status",
            schema.Identity,
            schema.CatalogIdentity,
            model,
            SchemaDefinition: null);
        Assert.InvalidContains(hot.ValidateCold(rendererCapabilities: CreateRendererCapabilities()), "inline schema");

        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);
        Assert.True(UiSchemaIdentity.TryCreate(
            "server/main",
            "hud.status",
            schema.Identity.SchemaRevision + 1,
            new string('d', 64),
            1,
            out UiSchemaIdentity otherIdentity,
            out string error), error);

        Assert.InvalidContains((hot with { SchemaIdentity = otherIdentity }).ValidateHot(context!), "model schema identity");
        Assert.InvalidContains((hot with
        {
            Model = model with { SchemaIdentity = otherIdentity }
        }).ValidateHot(context!), "model schema identity");

        Assert.True(UiCatalogIdentity.TryCreate(
            "server/main",
            "craft.blacksmith",
            1,
            new string('e', 64),
            1,
            "en-US",
            out UiCatalogIdentity catalogIdentity,
            out error), error);
        Assert.InvalidContains((hot with { CatalogIdentity = catalogIdentity }).ValidateHot(context!), "catalog");

        var mismatchedDefinition = new UiPanelOpenDocument(
            "panel/open/status",
            schema.Identity,
            CatalogIdentity: catalogIdentity,
            model,
            schema);
        Assert.InvalidContains(mismatchedDefinition.ValidateEnvelope(), "catalog identity");
    }

    private static void UiDefinitionCache_StoresSchemasAndReturnsHotCompatibilityContext()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        UiModelDocument model = CreateValidModelDocument(schema);
        var cache = new UiDefinitionCache();

        Assert.Valid(cache.PutSchema(schema, rendererCapabilities: CreateRendererCapabilities()));
        Assert.Equal(1, cache.SchemaCount);

        UiSchemaCacheLookupResult lookup = cache.TryGetSchema(schema.Identity);
        Assert.Equal(UiDefinitionCacheLookupKind.Hit, lookup.Kind);
        Assert.NotNull(lookup.Entry);
        Assert.Equal(schema.Identity, lookup.Entry!.Schema.Identity);

        var hot = new UiPanelOpenDocument(
            "panel/open/status",
            schema.Identity,
            schema.CatalogIdentity,
            model,
            SchemaDefinition: null);
        Assert.Valid(hot.ValidateHot(lookup.Entry.CompatibilityContext));
    }

    private static void UiDefinitionCache_ReportsMissSupersededAndCapacityEviction()
    {
        UiSchemaDocument schemaV1 = CreateValidSchemaDocument();
        UiSchemaDocument schemaV2 = CreateSchemaRevision(schemaV1, 2, 'd');
        var cache = new UiDefinitionCache(maxSchemaEntries: 1);

        Assert.Equal(UiDefinitionCacheLookupKind.Miss, cache.TryGetSchema(schemaV1.Identity).Kind);

        Assert.Valid(cache.PutSchema(schemaV1, rendererCapabilities: CreateRendererCapabilities()));
        UiSchemaCacheLookupResult staleLookup = cache.TryGetSchema(schemaV2.Identity);
        Assert.Equal(UiDefinitionCacheLookupKind.Superseded, staleLookup.Kind);
        Assert.True(staleLookup.Entry is null, "superseded lookup must not return a usable hot context");
        Assert.Equal(schemaV1.Identity, staleLookup.SupersededByIdentity);

        Assert.Valid(cache.PutSchema(schemaV2, rendererCapabilities: CreateRendererCapabilities()));
        Assert.Equal(UiDefinitionCacheLookupKind.Hit, cache.TryGetSchema(schemaV2.Identity).Kind);

        UiSchemaCacheLookupResult oldLookup = cache.TryGetSchema(schemaV1.Identity);
        Assert.Equal(UiDefinitionCacheLookupKind.Superseded, oldLookup.Kind);
        Assert.Equal(schemaV2.Identity, oldLookup.SupersededByIdentity);

        UiSchemaDocument otherPanel = CreateSchemaForPanel("inventory.grid", 'e');
        Assert.Valid(cache.PutSchema(otherPanel, rendererCapabilities: CreateRendererCapabilities()));
        Assert.Equal(1, cache.SchemaCount);
        Assert.Equal(UiDefinitionCacheLookupKind.Miss, cache.TryGetSchema(schemaV2.Identity).Kind);
        Assert.Equal(UiDefinitionCacheLookupKind.Hit, cache.TryGetSchema(otherPanel.Identity).Kind);
    }

    private static void UiDefinitionCache_TracksCatalogIdentityHitsSupersessionAndServerClear()
    {
        Assert.True(UiCatalogIdentity.TryCreate(
            "server/main",
            "craft.blacksmith",
            1,
            new string('a', 64),
            1,
            "en-US",
            out UiCatalogIdentity catalogV1,
            out string error), error);
        Assert.True(UiCatalogIdentity.TryCreate(
            "server/main",
            "craft.blacksmith",
            2,
            new string('b', 64),
            1,
            "en-US",
            out UiCatalogIdentity catalogV2,
            out error), error);

        UiSchemaDocument schema = CreateValidSchemaDocument();
        var cache = new UiDefinitionCache();
        Assert.Valid(cache.PutSchema(schema, rendererCapabilities: CreateRendererCapabilities()));
        Assert.Valid(cache.PutCatalogIdentity(catalogV1));

        UiCatalogCacheLookupResult hit = cache.TryGetCatalog(catalogV1);
        Assert.Equal(UiDefinitionCacheLookupKind.Hit, hit.Kind);
        Assert.NotNull(hit.Entry);

        UiCatalogCacheLookupResult staleLookup = cache.TryGetCatalog(catalogV2);
        Assert.Equal(UiDefinitionCacheLookupKind.Superseded, staleLookup.Kind);
        Assert.True(staleLookup.Entry is null, "superseded catalog lookup must not return stale data");
        Assert.Equal(catalogV1, staleLookup.SupersededByIdentity);

        Assert.Valid(cache.PutCatalogIdentity(catalogV2));
        Assert.Equal(UiDefinitionCacheLookupKind.Hit, cache.TryGetCatalog(catalogV2).Kind);
        Assert.Equal(UiDefinitionCacheLookupKind.Superseded, cache.TryGetCatalog(catalogV1).Kind);

        cache.ClearServer(" server/main ");
        Assert.Equal(0, cache.SchemaCount);
        Assert.Equal(0, cache.CatalogCount);
        Assert.Equal(UiDefinitionCacheLookupKind.Miss, cache.TryGetSchema(schema.Identity).Kind);
        Assert.Equal(UiDefinitionCacheLookupKind.Miss, cache.TryGetCatalog(catalogV2).Kind);
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

        var wrongContext = request with { ContextKind = UiActionContextKind.Item };
        Assert.InvalidContains(wrongContext.ValidateAgainst(context!), "context kind");

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

    private static void UiTooltipDescriptor_ValidatesFlatStaticAndSlotBackedLines()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);

        UiTooltipDescriptor tooltip = CreateValidTooltip();
        Assert.Valid(tooltip.Validate(context!));

        var simple = new UiTooltipDescriptor(
            "simple/help",
            Title: null,
            IconId: null,
            UiTooltipVisualRole.Normal,
            Lines: [new UiTooltipLineDescriptor(UiTooltipLineKind.Body, null, "A compact hint.", null)]);
        Assert.Valid(simple.Validate());
    }

    private static void UiTooltipDescriptor_RejectsUnknownSlotsAndOversizedShapes()
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        Assert.Valid(UiSchemaCompatibilityContext.TryCreate(schema, out UiSchemaCompatibilityContext? context));
        Assert.NotNull(context);

        var unknownSlot = CreateValidTooltip() with
        {
            Lines =
            [
                new UiTooltipLineDescriptor(UiTooltipLineKind.Stat, "Missing", null, "missingSlot")
            ]
        };
        Assert.InvalidContains(unknownSlot.Validate(context!), "unknown slot");

        var noLines = CreateValidTooltip() with { Lines = [] };
        Assert.InvalidContains(noLines.Validate(context!), "at least one line");

        var tooMany = CreateValidTooltip() with
        {
            Lines = Enumerable.Range(0, UiTooltipDescriptor.MaxTooltipLines + 1)
                .Select(i => new UiTooltipLineDescriptor(UiTooltipLineKind.Body, null, $"line {i}", null))
                .ToArray()
        };
        Assert.InvalidContains(tooMany.Validate(context!), "more than");
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
                new UiSchemaNodeDescriptor(4, UiNodeKind.Button, null, "Open", [], ActionId: "hud.open_character"),
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

    private static UiSchemaDocument CreateSchemaRevision(UiSchemaDocument schema, int revision, char hashChar)
    {
        Assert.True(UiSchemaIdentity.TryCreate(
            schema.Identity.ServerIdentity,
            schema.Identity.PanelType,
            revision,
            new string(hashChar, 64),
            schema.Identity.ProtocolVersion,
            out UiSchemaIdentity identity,
            out string error), error);
        return schema with { Identity = identity };
    }

    private static UiSchemaDocument CreateSchemaForPanel(string panelType, char hashChar)
    {
        UiSchemaDocument schema = CreateValidSchemaDocument();
        Assert.True(UiSchemaIdentity.TryCreate(
            schema.Identity.ServerIdentity,
            panelType,
            1,
            new string(hashChar, 64),
            schema.Identity.ProtocolVersion,
            out UiSchemaIdentity identity,
            out string error), error);
        return schema with
        {
            Identity = identity,
            Metadata = schema.Metadata with { PanelType = panelType },
        };
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

    private static UiTooltipDescriptor CreateValidTooltip()
    {
        return new UiTooltipDescriptor(
            "tooltip/hud/status",
            "Status",
            "icons/ui/status",
            UiTooltipVisualRole.Uncommon,
            Lines:
            [
                new UiTooltipLineDescriptor(UiTooltipLineKind.Body, null, "Current character status.", null),
                new UiTooltipLineDescriptor(UiTooltipLineKind.Stat, "Health", null, "healthText", UiTooltipVisualRole.Positive),
                new UiTooltipLineDescriptor(UiTooltipLineKind.Stat, "Level", null, "levelValue"),
                new UiTooltipLineDescriptor(UiTooltipLineKind.Advanced, null, "Hold detail for the full character panel.", null, UiTooltipVisualRole.Muted),
            ]);
    }

    private static UiActionMenuDescriptor CreateValidContextMenu()
    {
        return new UiActionMenuDescriptor(
            "item/context/root",
            UiActionMenuPresentationKind.ContextMenu,
            UiActionContextKind.Item,
            ContextId: "item:iron_sword/1",
            Actions:
            [
                new UiActionDescriptor(
                    "item.equip",
                    "Equip",
                    "icons/items/equip",
                    UiActionRole.Primary,
                    UiActionContextKind.Item,
                    RequiresConfirm: false,
                    ChildMenuKey: null,
                    MenuGroupKey: "inventory"),
                new UiActionDescriptor(
                    "item.destroy",
                    "Destroy",
                    null,
                    UiActionRole.Destructive,
                    UiActionContextKind.Item,
                    RequiresConfirm: true,
                    ChildMenuKey: null,
                    MenuGroupKey: "danger"),
            ]);
    }

    private static UiRendererCapabilities CreateRendererCapabilities()
    {
        return new UiRendererCapabilities(
            ProtocolVersion: 1,
            CapabilityFlags: UiCapabilityFlags.ContextMenusV1 | UiCapabilityFlags.RadialMenusV1 | UiCapabilityFlags.RuntimeStateBindingsV1,
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

    public static TException Throws<TException>(Action action)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException ex)
        {
            return ex;
        }

        throw new InvalidOperationException($"Expected exception of type {typeof(TException).Name}.");
    }
}
