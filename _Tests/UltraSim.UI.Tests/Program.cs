using System.Buffers.Binary;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("AuthoringCompilesToPreparedNumericSurface", AuthoringCompilesToPreparedNumericSurface),
    ("DynamicModelDoesNotChangeDefinitionHash", DynamicModelDoesNotChangeDefinitionHash),
    ("LayoutChangesDefinitionHash", LayoutChangesDefinitionHash),
    ("CompilationIsDeterministicInSourceOrder", CompilationIsDeterministicInSourceOrder),
    ("CompleteS0VocabularyCompiles", CompleteS0VocabularyCompiles),
    ("UnsupportedKindsRejectSafely", UnsupportedKindsRejectSafely),
    ("UnknownBindingsAndDuplicateDeclarationsReject", UnknownBindingsAndDuplicateDeclarationsReject),
    ("InvalidContainmentAndLimitsReject", InvalidContainmentAndLimitsReject),
    ("TypedModelValuesAndRowsAreBounded", TypedModelValuesAndRowsAreBounded),
    ("ActionAvailabilityIsDynamicAndPrepared", ActionAvailabilityIsDynamicAndPrepared),
    ("PreparedReferencesAvoidRuntimeBindingStrings", PreparedReferencesAvoidRuntimeBindingStrings),
    ("CrossDefinitionReferencesReject", CrossDefinitionReferencesReject),
    ("StaleDefinitionRevisionRejects", StaleDefinitionRevisionRejects),
    ("TypedActionInputRejectsWrongValue", TypedActionInputRejectsWrongValue),
    ("CanonicalFloatFormsHashIdentically", CanonicalFloatFormsHashIdentically),
    ("CompilerRevalidatesInjectedLayoutState", CompilerRevalidatesInjectedLayoutState),
    ("FailedAuthoringCallbacksDoNotPoisonBuilder", FailedAuthoringCallbacksDoNotPoisonBuilder),
    ("DefinitionKeysAndAnySlotsRemainBoundedAndUnambiguous", DefinitionKeysAndAnySlotsRemainBoundedAndUnambiguous),
    ("MixedLayoutAndElementSiblingOrderIsPreserved", MixedLayoutAndElementSiblingOrderIsPreserved),
    ("ListRowActionsHaveExplicitContext", ListRowActionsHaveExplicitContext),
    ("RowActionsRequireListProvenanceAndExistingRows", RowActionsRequireListProvenanceAndExistingRows),
    ("DynamicTextAndIdentityRejectMalformedOrUnboundedUnicode", DynamicTextAndIdentityRejectMalformedOrUnboundedUnicode),
    ("SemanticWireCodecIsCanonicalBoundedAndTransactional", SemanticWireCodecIsCanonicalBoundedAndTransactional),
    ("DefinitionArtifactIsCanonicalBoundedAndExact", DefinitionArtifactIsCanonicalBoundedAndExact),
    ("OutsidePointerPolicyIsCanonicalAndValidated", OutsidePointerPolicyIsCanonicalAndValidated),
    ("HotOpenModelRebindRequiresExactCompiledIdentity", HotOpenModelRebindRequiresExactCompiledIdentity),
    ("ActionReasonMustRespectCompiledTextLimit", ActionReasonMustRespectCompiledTextLimit),
    ("TruncatedDeltaCountsMustNotCauseLargeAllocations", TruncatedDeltaCountsMustNotCauseLargeAllocations),
};

static void OutsidePointerPolicyIsCanonicalAndValidated()
{
    var modelessSource = CreateOutsidePolicyFixture(UiOutsidePointerBehavior.PassThrough);
    CompiledUiDefinition modeless = UiDefinitionCompiler.Compile(modelessSource);
    Assert.Equal(UiOutsidePointerBehavior.PassThrough, modeless.OutsidePointerBehavior);
    Assert.False(modeless.OutsideClickAction.IsValid);

    var closeSource = CreateOutsidePolicyFixture(UiOutsidePointerBehavior.Close);
    CompiledUiDefinition closeDefinition = UiDefinitionCompiler.Compile(closeSource);
    Assert.Equal(UiOutsidePointerBehavior.Close, closeDefinition.OutsidePointerBehavior);
    Assert.Equal(closeDefinition.Bindings.RequireAction("close"), closeDefinition.OutsideClickAction);
    Assert.False(modeless.Hash == closeDefinition.Hash);
    Assert.True(UiDefinitionArtifactCodec.TryEncodeCanonical(closeDefinition, out byte[] artifact));
    Assert.True(UiDefinitionArtifactCodec.TryReadCanonical(
        artifact, out CompiledUiDefinition? restored));
    Assert.Equal(UiOutsidePointerBehavior.Close, restored!.OutsidePointerBehavior);
    Assert.Equal(restored.Bindings.RequireAction("close"), restored.OutsideClickAction);

    var modalSource = CreateOutsidePolicyFixture(UiOutsidePointerBehavior.Block);
    CompiledUiDefinition modal = UiDefinitionCompiler.Compile(modalSource);
    Assert.Equal(UiOutsidePointerBehavior.Block, modal.OutsidePointerBehavior);
    Assert.False(modal.Hash == modeless.Hash);

    var wrongOwner = new UiDefinitionBuilder("wrong-owner");
    UiActionReference wrongAction = wrongOwner.DeclareAction("close");
    var foreignTarget = new UiDefinitionBuilder("foreign-target");
    foreignTarget.DeclareAction("close");
    Assert.Throws<ArgumentException>(() => closeSource.SetOutsidePointerBehavior(
        UiOutsidePointerBehavior.Close, wrongAction));
    Assert.Throws<ArgumentException>(() => foreignTarget.SetOutsidePointerBehavior(
        UiOutsidePointerBehavior.PassThrough, wrongAction));

    var unbound = new UiDefinitionBuilder("unbound-close");
    UiActionReference unboundClose = unbound.DeclareAction("close");
    unbound.SetOutsidePointerBehavior(UiOutsidePointerBehavior.Close, unboundClose);
    unbound.AddCanvas(_ => { });
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(unbound));
}

static UiDefinitionBuilder CreateOutsidePolicyFixture(UiOutsidePointerBehavior behavior)
{
    var builder = new UiDefinitionBuilder("outside-policy");
    UiSlotReference label = builder.DeclareSlot("label", UiSlotValueKind.Text);
    UiActionReference close = builder.DeclareAction("close");
    if (behavior == UiOutsidePointerBehavior.Close)
        builder.SetOutsidePointerBehavior(behavior, close);
    else if (behavior != UiOutsidePointerBehavior.PassThrough)
        builder.SetOutsidePointerBehavior(behavior);
    builder.AddCanvas(canvas =>
    {
        canvas.AddPanel("surface", "surface",
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 10, 100, 100)));
        canvas.AddButton("close", close, label,
            UiChildLayout.Canvas(new UiCanvasPlacement(20, 20, 80, 30)));
    });
    return builder;
}

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} UI S0.3 tests passed.");

static void ActionReasonMustRespectCompiledTextLimit()
{
    var limits = new UiCompileLimits(maximumTextCharacters: 64);
    var builder = new UiDefinitionBuilder("action-reason-limit");
    builder.DeclareAction("act");
    builder.AddCanvas(_ => { });
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(builder, limits);
    UiActionId action = definition.Bindings.RequireAction("act");
    var model = new UiModel(definition);
    string excessive = new('x', 65);

    Assert.Throws<InvalidDataException>(() =>
        model.SetActionState(action, new UiActionState(false, excessive)));

    model.SetActionState(action, new UiActionState(false, new string('x', 64)));
    var delta = new UiModelDelta(
        definition.Handle,
        sequence: 1,
        actions: [new UiActionStatePatch(action, new UiActionState(false, excessive))]);
    byte[] encoded = new byte[UiSemanticWireLimits.Default.MaximumOperationBytes];
    Assert.True(UiSemanticWireCodec.TryWriteUpdate(
        encoded,
        new UiUpdateView(new UiViewInstanceId(1), delta),
        UiSemanticWireLimits.Default,
        out int written));
    Assert.True(UiSemanticWireCodec.TryReadUpdate(
        encoded.AsSpan(0, written), UiSemanticWireLimits.Default, out UiUpdateView decoded));
    Assert.Throws<InvalidDataException>(() => decoded.Delta.ValidateAgainst(definition));
    Assert.Equal(64, model.GetActionState(action).UnavailableReason!.Length);
}

static void TruncatedDeltaCountsMustNotCauseLargeAllocations()
{
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(CreateFixture());
    byte[] warmup = CreateTruncatedDelta(definition.Handle, scalarCount: 1);
    Assert.False(UiSemanticWireCodec.TryReadUpdate(warmup, UiSemanticWireLimits.Default, out _));

    AssertBounded(CreateTruncatedDelta(definition.Handle, scalarCount: ushort.MaxValue));
    AssertBounded(CreateTruncatedDelta(definition.Handle, listCount: ushort.MaxValue));
    AssertBounded(CreateTruncatedDelta(definition.Handle, actionCount: ushort.MaxValue));
    AssertBounded(CreateTruncatedReplacement(definition.Handle, 65_536));

    static void AssertBounded(byte[] payload)
    {
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool accepted = UiSemanticWireCodec.TryReadUpdate(
            payload, UiSemanticWireLimits.Default, out UiUpdateView operation);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.False(accepted);
        Assert.Equal(default(UiUpdateView), operation);
        Assert.True(allocated < 16 * 1024);
    }
}

static byte[] CreateTruncatedDelta(
    UiDefinitionHandle handle,
    int scalarCount = 0,
    int listCount = 0,
    int actionCount = 0)
{
    byte[] payload = new byte[44];
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(8), handle.Low);
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(16), handle.High);
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(24), 1);
    BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(32), scalarCount);
    BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(36), listCount);
    BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(40), actionCount);
    return payload;
}

static byte[] CreateTruncatedReplacement(UiDefinitionHandle handle, int rowCount)
{
    byte[] payload = new byte[59];
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(0), 1);
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(8), handle.Low);
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(16), handle.High);
    BinaryPrimitives.WriteUInt64BigEndian(payload.AsSpan(24), 1);
    BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(36), 1);
    BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(44), 0);
    payload[46] = (byte)UiListDeltaKind.Replace;
    BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(55), rowCount);
    return payload;
}

static void AuthoringCompilesToPreparedNumericSurface()
{
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(CreateFixture());

    Assert.Equal(new UiDefinitionKey("fixture.inventory"), definition.Key);
    Assert.True(definition.Handle.IsValid);
    Assert.True(definition.Hash.IsValid);
    Assert.Equal(3, definition.LayoutNodeCount);
    Assert.Equal(5, definition.ElementCount);
    Assert.Equal(3, definition.SlotCount);
    Assert.Equal(2, definition.ActionCount);

    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiActionId select = definition.Bindings.RequireAction("select");
    Assert.Equal((ushort)0, title.Value);
    Assert.Equal((ushort)1, select.Value);
    Assert.Equal(UiSlotValueKind.Text, definition.GetSlotValueKind(title));
    Assert.True(definition.GetAction(select).AcceptsRowContext);
    Assert.Equal(UiLayoutKind.Canvas, definition.LayoutNodes[0].Kind);
    Assert.Equal(UiLayoutKind.Vertical, definition.LayoutNodes[1].Kind);
    Assert.Equal(UiElementKind.List, definition.Elements[2].Kind);
}

static void DynamicModelDoesNotChangeDefinitionHash()
{
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(CreateFixture());
    UiModel first = new(definition);
    UiModel second = new(definition);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiSlotId rows = definition.Bindings.RequireSlot("rows");
    UiActionId select = definition.Bindings.RequireAction("select");

    first.SetText(title, "Alice");
    first.ReplaceRows(rows, new[]
    {
        new UiListRow(new UiRowId(1), new[] { UiValue.FromText("Sword") }),
    });
    second.SetText(title, "Bob");
    second.ReplaceRows(rows, new[]
    {
        new UiListRow(new UiRowId(2), new[] { UiValue.FromText("Shield") }),
        new UiListRow(new UiRowId(3), new[] { UiValue.FromText("Potion") }),
    });
    second.SetActionState(select, new UiActionState(false, "Not your turn."));

    Assert.Equal(first.Definition.Hash, second.Definition.Hash);
    Assert.Equal("Alice", first.Get(title).Text);
    Assert.Equal(1, first.GetRows(rows).Length);
    Assert.Equal(2, second.GetRows(rows).Length);
}

static void LayoutChangesDefinitionHash()
{
    CompiledUiDefinition original = UiDefinitionCompiler.Compile(CreateFixture(canvasWidth: 800));
    CompiledUiDefinition changed = UiDefinitionCompiler.Compile(CreateFixture(canvasWidth: 801));

    Assert.False(original.Hash == changed.Hash);
    Assert.False(original.Handle == changed.Handle);
}

static void CompilationIsDeterministicInSourceOrder()
{
    CompiledUiDefinition first = UiDefinitionCompiler.Compile(CreateFixture());
    CompiledUiDefinition second = UiDefinitionCompiler.Compile(CreateFixture());

    Assert.Equal(first.Hash, second.Hash);
    Assert.Equal(first.Handle, second.Handle);
    for (int index = 0; index < first.LayoutNodeCount; index++)
    {
        Assert.Equal(first.LayoutNodes[index].Id.Value, second.LayoutNodes[index].Id.Value);
        Assert.Equal(first.LayoutNodes[index].Key, second.LayoutNodes[index].Key);
    }
    for (int index = 0; index < first.ElementCount; index++)
        Assert.Equal(first.Elements[index].Key, second.Elements[index].Key);
}

static void CompleteS0VocabularyCompiles()
{
    var builder = new UiDefinitionBuilder("complete-vocabulary");
    builder.DeclareSlot("text", UiSlotValueKind.Text);
    builder.DeclareSlot("enabled", UiSlotValueKind.Boolean);
    builder.DeclareSlot("rows", UiSlotValueKind.List);
    builder.DeclareAction("click");
    builder.AddCanvas(canvas =>
    {
        canvas.AddPanel("panel", "surface",
            UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 400, 300)));
        canvas.AddText("text", "text",
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 10, 100, 20)));
        canvas.AddImage("image", "icon",
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 40, 32, 32)));
        canvas.AddButton("button", "click", "text",
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 80, 100, 24)));
        canvas.AddTextInput("input", "text",
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 110, 100, 24)));
        canvas.AddToggle("toggle", "enabled",
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 140, 100, 24)));
        canvas.AddList("list", "rows",
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 170, 200, 100)));
        canvas.AddOverlay("overlay", overlay =>
        {
            overlay.AddScroll("scroll", scroll => scroll.AddTextInput("nested-input", "text"),
                flow: new UiFlowOptions(spacing: 2));
        }, UiChildLayout.Canvas(new UiCanvasPlacement(220, 10, 160, 120)));
    });

    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(builder);
    Assert.Equal(3, definition.LayoutNodeCount);
    Assert.Equal(8, definition.ElementCount);
    bool hasOverlay = false;
    bool hasScroll = false;
    foreach (UiCompiledLayoutNode node in definition.LayoutNodes)
    {
        hasOverlay |= node.Kind == UiLayoutKind.Overlay;
        hasScroll |= node.Kind == UiLayoutKind.Scroll;
    }
    bool hasImage = false;
    bool hasTextInput = false;
    foreach (UiCompiledElement element in definition.Elements)
    {
        hasImage |= element.Kind == UiElementKind.Image;
        hasTextInput |= element.Kind == UiElementKind.TextInput;
    }
    Assert.True(hasOverlay);
    Assert.True(hasScroll);
    Assert.True(hasImage);
    Assert.True(hasTextInput);

    Assert.Throws<ArgumentOutOfRangeException>(() => UiChildLayout.Flow((UiSizing)99));
    Assert.Throws<ArgumentOutOfRangeException>(() => new UiFlowOptions(alignment: (UiAlignment)99));
}

static void UnknownBindingsAndDuplicateDeclarationsReject()
{
    var unknown = new UiDefinitionBuilder("unknown");
    unknown.AddCanvas(canvas => canvas.AddText(
        "label", "typo", UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 20))));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(unknown));

    var duplicateSlot = new UiDefinitionBuilder("duplicate-slot");
    duplicateSlot.DeclareSlot("value", UiSlotValueKind.Text);
    duplicateSlot.DeclareSlot("value", UiSlotValueKind.Text);
    duplicateSlot.AddCanvas(_ => { });
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(duplicateSlot));

    var duplicateAction = new UiDefinitionBuilder("duplicate-action");
    duplicateAction.DeclareAction("submit");
    duplicateAction.DeclareAction("submit");
    duplicateAction.AddCanvas(_ => { });
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(duplicateAction));

    var wrongType = new UiDefinitionBuilder("wrong-type");
    wrongType.DeclareSlot("enabled", UiSlotValueKind.Boolean);
    wrongType.AddCanvas(canvas => canvas.AddText(
        "label", "enabled", UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 20))));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(wrongType));
}

static void UnsupportedKindsRejectSafely()
{
    var badLayout = new UiDefinitionBuilder("unsupported-layout");
    var layoutRoot = new UiSourceLayoutNode(
        UiLayoutKind.Canvas, "root", default, default, UiCanvasOptions.Default);
    layoutRoot.Children.Add(new UiSourceLayoutNode(
        (UiLayoutKind)99,
        "unsupported",
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10)),
        default,
        default));
    badLayout.SetRootForTesting(layoutRoot);
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(badLayout));

    var badElement = new UiDefinitionBuilder("unsupported-element");
    var elementRoot = new UiSourceLayoutNode(
        UiLayoutKind.Canvas, "root", default, default, UiCanvasOptions.Default);
    elementRoot.Elements.Add(new UiSourceElement(
        (UiElementKind)99,
        "unsupported",
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10)),
        null,
        null,
        null,
        null));
    badElement.SetRootForTesting(elementRoot);
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(badElement));
}

static void InvalidContainmentAndLimitsReject()
{
    var flowWithCanvasPlacement = new UiDefinitionBuilder("bad-placement");
    flowWithCanvasPlacement.AddCanvas(canvas => canvas.AddVertical(
        "column",
        vertical => vertical.AddPanel("panel", "surface", UiChildLayout.Canvas(
            new UiCanvasPlacement(0, 0, 100, 100))),
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 100))));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(flowWithCanvasPlacement));

    var canvasWithoutPlacement = new UiDefinitionBuilder("missing-placement");
    canvasWithoutPlacement.AddCanvas(canvas => canvas.AddPanel("panel", "surface"));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(canvasWithoutPlacement));

    var deep = new UiDefinitionBuilder("deep");
    deep.AddCanvas(canvas => canvas.AddVertical("level-1", level1 =>
        level1.AddVertical("level-2", level2 => level2.AddVertical("level-3", _ => { }))));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(
        deep, new UiCompileLimits(maximumDepth: 2)));

    var tooMuchText = new UiDefinitionBuilder("text-limit");
    tooMuchText.DeclareSlot("text", UiSlotValueKind.Text);
    tooMuchText.AddCanvas(canvas => canvas.AddText(
        "label", "text", UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 20))));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(
        tooMuchText, new UiCompileLimits(maximumTextCharacters: 8)));

    var tooManyElements = new UiDefinitionBuilder("element-limit");
    tooManyElements.AddCanvas(canvas =>
    {
        canvas.AddPanel("one", "surface", UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10)));
        canvas.AddPanel("two", "surface", UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10)));
    });
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(
        tooManyElements, new UiCompileLimits(maximumElements: 1)));
}

static void TypedModelValuesAndRowsAreBounded()
{
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(CreateFixture());
    UiModel model = new(definition);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiSlotId enabled = definition.Bindings.RequireSlot("enabled");
    UiSlotId rows = definition.Bindings.RequireSlot("rows");

    model.SetText(title, "Ready");
    model.SetBoolean(enabled, true);
    Assert.Equal(UiValueKind.Text, model.Get(title).Kind);
    Assert.Equal(UiValueKind.Boolean, model.Get(enabled).Kind);
    Assert.Throws<InvalidOperationException>(() => model.SetNumber(title, 3));

    UiListRow[] source =
    [
        new UiListRow(new UiRowId(10), new[] { UiValue.FromText("A") }),
        new UiListRow(new UiRowId(11), new[] { UiValue.FromText("B") }),
    ];
    model.ReplaceRows(rows, source);
    Assert.Equal(2, model.GetRows(rows).Length);
    Assert.Equal(new UiRowId(11), model.GetRows(rows)[1].Id);
    Assert.Throws<InvalidDataException>(() => model.ReplaceRows(rows,
        new[]
        {
            new UiListRow(new UiRowId(12), ReadOnlySpan<UiValue>.Empty),
            new UiListRow(new UiRowId(12), ReadOnlySpan<UiValue>.Empty),
        }));
    CompiledUiDefinition limitedDefinition = UiDefinitionCompiler.Compile(
        CreateFixture(), new UiCompileLimits(maximumRowsPerList: 1));
    UiModel limitedModel = new(limitedDefinition);
    UiSlotId limitedRows = limitedDefinition.Bindings.RequireSlot("rows");
    Assert.Throws<InvalidDataException>(() => limitedModel.ReplaceRows(limitedRows,
        new[]
        {
            new UiListRow(new UiRowId(13), ReadOnlySpan<UiValue>.Empty),
            new UiListRow(new UiRowId(14), ReadOnlySpan<UiValue>.Empty),
        }));
}

static void ActionAvailabilityIsDynamicAndPrepared()
{
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(CreateFixture());
    UiModel model = new(definition);
    UiActionId select = definition.Bindings.RequireAction("select");
    UiActionId close = definition.Bindings.RequireAction("close");
    model.ReplaceRows(
        definition.Bindings.RequireSlot("rows"),
        [new UiListRow(new UiRowId(99), ReadOnlySpan<UiValue>.Empty)]);
    var request = new UiActionRequest(
        new UiViewInstanceId(42), definition.Handle, select, new UiRowId(99));

    model.ValidateAction(request);
    Assert.Throws<InvalidOperationException>(() => model.ValidateAction(
        new UiActionRequest(new UiViewInstanceId(42), definition.Handle, select, new UiRowId(100))));
    model.SetActionState(select, new UiActionState(false, "Not available."));
    Assert.Equal("Not available.", model.GetActionState(select).UnavailableReason);
    Assert.Throws<InvalidOperationException>(() => model.ValidateAction(request));

    var nonRowRequest = new UiActionRequest(new UiViewInstanceId(42), definition.Handle, close);
    model.ValidateAction(nonRowRequest);
    Assert.Throws<InvalidOperationException>(() => model.ValidateAction(
        new UiActionRequest(new UiViewInstanceId(42), definition.Handle, close, new UiRowId(1))));
    Assert.Throws<InvalidOperationException>(() => model.ValidateAction(
        new UiActionRequest(new UiViewInstanceId(42), UiDefinitionHandle.Invalid, close)));
}

static void PreparedReferencesAvoidRuntimeBindingStrings()
{
    var builder = new UiDefinitionBuilder("typed-authoring");
    UiSlotReference label = builder.DeclareSlot("label", UiSlotValueKind.Text);
    UiSlotReference enabled = builder.DeclareSlot("enabled", UiSlotValueKind.Boolean);
    UiActionReference submit = builder.DeclareAction("submit");
    UiActionReference changed = builder.DeclareAction("changed", inputKind: UiValueKind.Boolean);
    builder.AddCanvas(canvas => canvas.AddVertical(
        "content",
        column =>
        {
            column.AddText("label", label);
            column.AddButton("submit", submit, label);
            column.AddToggle("toggle", enabled, changed);
        },
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 100))));

    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(builder);
    UiSlotId labelId = definition.Prepare(label);
    UiSlotId enabledId = definition.Prepare(enabled);
    UiActionId submitId = definition.Prepare(submit);
    UiActionId changedId = definition.Prepare(changed);

    Assert.Equal((ushort)0, labelId.Value);
    Assert.Equal((ushort)1, enabledId.Value);
    Assert.Equal((ushort)0, submitId.Value);
    Assert.Equal(UiValueKind.Boolean, definition.GetAction(changedId).InputKind);
}

static void CrossDefinitionReferencesReject()
{
    var first = new UiDefinitionBuilder("first");
    UiSlotReference firstText = first.DeclareSlot("text", UiSlotValueKind.Text);
    UiActionReference firstAction = first.DeclareAction("act");
    first.AddCanvas(canvas => canvas.AddButton(
        "button", firstAction, firstText,
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10))));
    CompiledUiDefinition firstDefinition = UiDefinitionCompiler.Compile(first);

    var second = new UiDefinitionBuilder("second");
    second.DeclareSlot("text", UiSlotValueKind.Text);
    second.DeclareAction("act");
    second.AddCanvas(canvas => canvas.AddButton(
        "button", firstAction, firstText,
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10))));

    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(second));
    CompiledUiDefinition ownDefinition = UiDefinitionCompiler.Compile(CreateFixture());
    Assert.Throws<InvalidOperationException>(() => ownDefinition.Prepare(firstText));
    Assert.True(firstDefinition.Prepare(firstText).IsValid);
}

static void StaleDefinitionRevisionRejects()
{
    CompiledUiDefinition original = UiDefinitionCompiler.Compile(CreateFixture(canvasWidth: 800));
    CompiledUiDefinition changed = UiDefinitionCompiler.Compile(CreateFixture(canvasWidth: 801));
    UiActionId oldAction = original.Bindings.RequireAction("close");

    Assert.False(original.Handle == changed.Handle);
    Assert.Throws<InvalidOperationException>(() => changed.ValidateAction(new UiActionRequest(
        new UiViewInstanceId(1), original.Handle, oldAction)));
}

static void TypedActionInputRejectsWrongValue()
{
    var builder = new UiDefinitionBuilder("typed-action");
    UiSlotReference text = builder.DeclareSlot("text", UiSlotValueKind.Text);
    UiActionReference edit = builder.DeclareAction("edit", inputKind: UiValueKind.Text);
    builder.AddCanvas(canvas => canvas.AddTextInput(
        "input", text, edit,
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 20))));
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(builder);
    UiActionId action = definition.Prepare(edit);
    var instance = new UiViewInstanceId(1);

    definition.ValidateAction(new UiActionRequest(
        instance, definition.Handle, action, input: UiValue.FromText("ok")));
    Assert.Throws<InvalidOperationException>(() => definition.ValidateAction(new UiActionRequest(
        instance, definition.Handle, action, input: UiValue.FromBoolean(true))));

    var mismatch = new UiDefinitionBuilder("typed-action-mismatch");
    UiSlotReference mismatchText = mismatch.DeclareSlot("text", UiSlotValueKind.Text);
    UiActionReference booleanAction = mismatch.DeclareAction("edit", inputKind: UiValueKind.Boolean);
    mismatch.AddCanvas(canvas => canvas.AddTextInput(
        "input", mismatchText, booleanAction,
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 20))));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(mismatch));
}

static void CanonicalFloatFormsHashIdentically()
{
    CompiledUiDefinition positive = UiDefinitionCompiler.Compile(CreateZeroFixture(+0.0f));
    CompiledUiDefinition negative = UiDefinitionCompiler.Compile(CreateZeroFixture(-0.0f));
    Assert.Equal(positive.Hash, negative.Hash);
    Assert.Equal(positive.Handle, negative.Handle);
    Assert.Throws<ArgumentOutOfRangeException>(() => new UiCanvasPlacement(
        0, 0, 10, 10, anchorMinX: 0.75f, anchorMaxX: 0.25f));
}

static void CompilerRevalidatesInjectedLayoutState()
{
    var invalidCanvas = new UiDefinitionBuilder("invalid-injected-canvas");
    invalidCanvas.SetRootForTesting(new UiSourceLayoutNode(
        UiLayoutKind.Canvas, "root", default, default, default));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(invalidCanvas));

    var invalidPlacement = new UiDefinitionBuilder("invalid-injected-placement");
    var root = new UiSourceLayoutNode(
        UiLayoutKind.Canvas, "root", default, default, UiCanvasOptions.Default);
    root.Elements.Add(new UiSourceElement(
        UiElementKind.Panel,
        "panel",
        default,
        "surface",
        null,
        null,
        null));
    invalidPlacement.SetRootForTesting(root);
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(invalidPlacement));

    var nestedCanvas = new UiDefinitionBuilder("nested-canvas");
    var nestedRoot = new UiSourceLayoutNode(
        UiLayoutKind.Canvas, "root", default, default, UiCanvasOptions.Default);
    nestedRoot.AddChild(new UiSourceLayoutNode(
        UiLayoutKind.Canvas,
        "illegal",
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10)),
        default,
        UiCanvasOptions.Default,
        siblingOrder: 0));
    nestedCanvas.SetRootForTesting(nestedRoot);
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(nestedCanvas));

    var nullElementKey = new UiDefinitionBuilder("null-element-key");
    var nullKeyRoot = new UiSourceLayoutNode(
        UiLayoutKind.Canvas, "root", default, default, UiCanvasOptions.Default);
    nullKeyRoot.AddElement(new UiSourceElement(
        UiElementKind.Panel,
        null!,
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10)),
        "surface",
        null,
        null,
        null,
        siblingOrder: 0));
    nullElementKey.SetRootForTesting(nullKeyRoot);
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(nullElementKey));
}

static void FailedAuthoringCallbacksDoNotPoisonBuilder()
{
    var root = new UiDefinitionBuilder("retry-root");
    Assert.Throws<InvalidOperationException>(() => root.AddCanvas(_ =>
        throw new InvalidOperationException("injected")));
    root.AddCanvas(_ => { });
    Assert.True(UiDefinitionCompiler.Compile(root).Hash.IsValid);

    var child = new UiDefinitionBuilder("retry-child");
    child.AddCanvas(canvas =>
    {
        Assert.Throws<InvalidOperationException>(() => canvas.AddVertical(
            "failed", _ => throw new InvalidOperationException("injected"),
            UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10))));
        canvas.AddVertical(
            "accepted", _ => { },
            UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10)));
    });
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(child);
    Assert.Equal(2, definition.LayoutNodeCount);
    Assert.Equal("accepted", definition.LayoutNodes[1].Key);
}

static void DefinitionKeysAndAnySlotsRemainBoundedAndUnambiguous()
{
    Assert.Throws<ArgumentOutOfRangeException>(() => new UiDefinitionKey(new string('x', 257)));

    var builder = new UiDefinitionBuilder("any-slot");
    UiSlotReference any = builder.DeclareSlot("any", UiSlotValueKind.Any);
    builder.AddCanvas(_ => { });
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(builder);
    UiSlotId slot = definition.Prepare(any);
    UiModel model = new(definition);
    model.SetText(slot, "value");
    model.ReplaceRows(slot, new[] { new UiListRow(new UiRowId(1), ReadOnlySpan<UiValue>.Empty) });
    Assert.Equal(UiValueKind.Unset, model.Get(slot).Kind);
    model.SetBoolean(slot, true);
    Assert.Equal(0, model.GetRows(slot).Length);
}

static void MixedLayoutAndElementSiblingOrderIsPreserved()
{
    CompiledUiDefinition middleLayout = UiDefinitionCompiler.Compile(CreateMixedOrderFixture(layoutLast: false));
    CompiledUiDefinition finalLayout = UiDefinitionCompiler.Compile(CreateMixedOrderFixture(layoutLast: true));

    UiCompiledLayoutNode middleNested = FindLayout(middleLayout, "nested");
    UiCompiledLayoutNode finalNested = FindLayout(finalLayout, "nested");
    UiCompiledElement first = FindElement(middleLayout, "first");
    UiCompiledElement last = FindElement(middleLayout, "last");

    Assert.Equal(0, first.SiblingOrder);
    Assert.Equal(1, middleNested.SiblingOrder);
    Assert.Equal(2, last.SiblingOrder);
    Assert.Equal(2, finalNested.SiblingOrder);
    Assert.False(middleLayout.Hash == finalLayout.Hash);
}

static void ListRowActionsHaveExplicitContext()
{
    var valid = new UiDefinitionBuilder("list-action");
    UiSlotReference rows = valid.DeclareSlot("rows", UiSlotValueKind.List);
    UiActionReference select = valid.DeclareAction("select", acceptsRowContext: true);
    valid.AddCanvas(canvas => canvas.AddList(
        "rows", rows, select,
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 100))));
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(valid);
    Assert.Equal(definition.Prepare(select), definition.Elements[0].Action);

    var listWithoutContext = new UiDefinitionBuilder("bad-list-action");
    UiSlotReference badRows = listWithoutContext.DeclareSlot("rows", UiSlotValueKind.List);
    UiActionReference badSelect = listWithoutContext.DeclareAction("select");
    listWithoutContext.AddCanvas(canvas => canvas.AddList(
        "rows", badRows, badSelect,
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 100))));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(listWithoutContext));

    var buttonWithContext = new UiDefinitionBuilder("bad-button-action");
    UiSlotReference label = buttonWithContext.DeclareSlot("label", UiSlotValueKind.Text);
    UiActionReference rowAction = buttonWithContext.DeclareAction("row", acceptsRowContext: true);
    buttonWithContext.AddCanvas(canvas => canvas.AddButton(
        "button", rowAction, label,
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 20))));
    Assert.Throws<InvalidDataException>(() => UiDefinitionCompiler.Compile(buttonWithContext));
}

static void RowActionsRequireListProvenanceAndExistingRows()
{
    var builder = new UiDefinitionBuilder("row-provenance");
    UiSlotReference firstRows = builder.DeclareSlot("first", UiSlotValueKind.List);
    UiSlotReference secondRows = builder.DeclareSlot("second", UiSlotValueKind.List);
    UiActionReference select = builder.DeclareAction("select", acceptsRowContext: true);
    builder.AddCanvas(canvas => canvas.AddVertical("lists", flow =>
    {
        flow.AddList("first-list", firstRows, select);
        flow.AddList("second-list", secondRows, select);
    }, UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 100))));
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(builder);
    UiModel model = new(definition);
    UiSlotId first = definition.Prepare(firstRows);
    UiSlotId second = definition.Prepare(secondRows);
    UiActionId action = definition.Prepare(select);
    model.ReplaceRows(first, [new UiListRow(new UiRowId(1), ReadOnlySpan<UiValue>.Empty)]);
    model.ReplaceRows(second, [new UiListRow(new UiRowId(2), ReadOnlySpan<UiValue>.Empty)]);
    UiCompiledElement firstElement = definition.Elements.ToArray().Single(element => element.Slot == first);
    UiCompiledElement secondElement = definition.Elements.ToArray().Single(element => element.Slot == second);

    model.ValidateAction(new UiActionRequest(
        new UiViewInstanceId(1), definition.Handle, action, new UiRowId(1), source: firstElement.Id));
    Assert.Throws<InvalidOperationException>(() => model.ValidateAction(new UiActionRequest(
        new UiViewInstanceId(1), definition.Handle, action, new UiRowId(1), source: secondElement.Id)));
    Assert.Throws<InvalidOperationException>(() => model.ValidateAction(new UiActionRequest(
        new UiViewInstanceId(1), definition.Handle, action, new UiRowId(1))));
}

static void DynamicTextAndIdentityRejectMalformedOrUnboundedUnicode()
{
    string malformed = new(new[] { '\uD800' });
    Assert.Throws<ArgumentException>(() => UiValue.FromText(malformed));
    Assert.Throws<ArgumentException>(() => _ = new UiDefinitionKey("bad-" + malformed));
    Assert.Throws<ArgumentOutOfRangeException>(() => UiValue.FromText(
        new string('x', UiValue.MaximumTextCharacters + 1)));
    Assert.Throws<ArgumentOutOfRangeException>(() => _ = new UiListRow(
        new UiRowId(1), new UiValue[UiListRow.MaximumValues + 1]));

    var limited = new UiDefinitionBuilder("t");
    UiSlotReference text = limited.DeclareSlot("text", UiSlotValueKind.Text);
    limited.AddCanvas(canvas => canvas.AddText(
        "label", text, UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 10, 10))));
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(
        limited, new UiCompileLimits(maximumTextCharacters: 64));
    UiModel model = new(definition);
    Assert.Throws<InvalidDataException>(() => model.SetText(
        definition.Prepare(text), new string('x', 65)));
}

static void SemanticWireCodecIsCanonicalBoundedAndTransactional()
{
    CompiledUiDefinition definition = UiDefinitionCompiler.Compile(CreateFixture());
    var cacheKey = new UiDefinitionCacheKey(
        "server", "composition", 1, definition.Key, definition.Version, definition.Hash);
    var model = new UiModel(definition);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiSlotId rows = definition.Bindings.RequireSlot("rows");
    model.SetText(title, "Inventory");
    model.ReplaceRows(rows,
    [
        new UiListRow(new UiRowId(7), [UiValue.FromText("Bread")]),
    ]);
    var operation = new UiOpenView(
        cacheKey, definition, model, new UiViewInstanceId(11), sequence: 3);
    UiSemanticWireLimits limits = UiSemanticWireLimits.Default;
    byte[] encoded = new byte[limits.MaximumOperationBytes];
    Assert.True(UiSemanticWireCodec.TryWriteColdOpen(
        encoded, operation, limits, out int written));
    Assert.True(UiSemanticWireCodec.TryReadColdOpen(
        encoded.AsSpan(0, written), limits, out UiOpenView decoded));
    Assert.Equal(operation.Instance, decoded.Instance);
    Assert.Equal(operation.Sequence, decoded.Sequence);
    Assert.Equal(definition.Hash, decoded.Definition.Hash);
    Assert.Equal("Inventory", decoded.SnapshotModel().Get(title).Text);
    Assert.Equal("Bread", decoded.SnapshotModel().GetRows(rows)[0].Values[0].Text);

    for (int length = 0; length < written; length++)
        Assert.False(UiSemanticWireCodec.TryReadColdOpen(
            encoded.AsSpan(0, length), limits, out _));
    encoded[written] = 0;
    Assert.False(UiSemanticWireCodec.TryReadColdOpen(
        encoded.AsSpan(0, written + 1), limits, out _));

    var hot = new UiHotOpenView(cacheKey, model, new UiViewInstanceId(12), sequence: 4);
    Assert.True(UiSemanticWireCodec.TryWriteHotOpen(encoded, hot, limits, out written));
    Assert.True(UiSemanticWireCodec.TryReadHotOpen(
        encoded.AsSpan(0, written), limits,
        key => key == cacheKey ? definition : null,
        out UiHotOpenView decodedHot));
    Assert.Equal(hot.Instance, decodedHot.Instance);
    Assert.False(UiSemanticWireCodec.TryReadHotOpen(
        encoded.AsSpan(0, written), limits, _ => null, out _));
}

static void DefinitionArtifactIsCanonicalBoundedAndExact()
{
    CompiledUiDefinition source = UiDefinitionCompiler.Compile(CreateFixture());
    Assert.True(UiDefinitionArtifactCodec.TryEncodeCanonical(source, out byte[] first));
    Assert.True(UiDefinitionArtifactCodec.TryEncodeCanonical(source, out byte[] second));
    Assert.True(first.AsSpan().SequenceEqual(second));
    Assert.True(UiDefinitionArtifactCodec.TryReadCanonical(
        first, out CompiledUiDefinition? restored));
    CompiledUiDefinition actual = restored!;
    Assert.Equal(UiDefinitionIdentity.From(source), UiDefinitionIdentity.From(actual));
    Assert.Equal(source.LayoutNodeCount, actual.LayoutNodeCount);
    Assert.Equal(source.ElementCount, actual.ElementCount);
    Assert.Equal(source.SlotCount, actual.SlotCount);
    Assert.Equal(source.ActionCount, actual.ActionCount);

    for (int length = 0; length < first.Length; length++)
        Assert.False(UiDefinitionArtifactCodec.TryReadCanonical(first.AsSpan(0, length), out _));
    byte[] damaged = first.ToArray();
    damaged[^1] ^= 0x40;
    Assert.False(UiDefinitionArtifactCodec.TryReadCanonical(damaged, out _));
    Assert.False(UiDefinitionArtifactCodec.TryReadCanonical(
        first.AsSpan(0, first.Length - 1), out _));

    var tooSmall = new UiDefinitionArtifactLimits(64, UiCompileLimits.Default);
    Assert.False(UiDefinitionArtifactCodec.TryEncodeCanonical(source, out _, tooSmall));
}

static void HotOpenModelRebindRequiresExactCompiledIdentity()
{
    CompiledUiDefinition authored = UiDefinitionCompiler.Compile(CreateFixture());
    Assert.True(UiDefinitionArtifactCodec.TryEncodeCanonical(authored, out byte[] bytes));
    Assert.True(UiDefinitionArtifactCodec.TryReadCanonical(
        bytes, out CompiledUiDefinition? decoded));
    Assert.False(ReferenceEquals(authored, decoded));

    var cacheKey = new UiDefinitionCacheKey(
        "server", "composition", 1, authored.Key, authored.Version, authored.Hash);
    var model = new UiModel(authored);
    UiSlotId title = authored.Bindings.RequireSlot("title");
    model.SetText(title, "Exact passive artifact");
    var hot = new UiHotOpenView(cacheKey, model, new UiViewInstanceId(9), sequence: 2);
    UiModel rebound = hot.SnapshotModel(decoded!);
    Assert.True(ReferenceEquals(decoded, rebound.Definition));
    Assert.Equal("Exact passive artifact", rebound.Get(title).Text);

    CompiledUiDefinition changed = UiDefinitionCompiler.Compile(CreateFixture(canvasWidth: 801));
    Assert.Throws<InvalidOperationException>(() => hot.SnapshotModel(changed));
}

static UiDefinitionBuilder CreateMixedOrderFixture(bool layoutLast)
{
    var builder = new UiDefinitionBuilder("mixed-order");
    builder.AddCanvas(canvas => canvas.AddVertical(
        "content",
        vertical =>
        {
            vertical.AddPanel("first", "surface");
            if (!layoutLast)
                vertical.AddHorizontal("nested", _ => { });
            vertical.AddPanel("last", "surface");
            if (layoutLast)
                vertical.AddHorizontal("nested", _ => { });
        },
        UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 100))));
    return builder;
}

static UiCompiledLayoutNode FindLayout(CompiledUiDefinition definition, string key)
{
    foreach (UiCompiledLayoutNode node in definition.LayoutNodes)
    {
        if (node.Key == key)
            return node;
    }
    throw new InvalidOperationException($"Layout '{key}' was not found.");
}

static UiCompiledElement FindElement(CompiledUiDefinition definition, string key)
{
    foreach (UiCompiledElement element in definition.Elements)
    {
        if (element.Key == key)
            return element;
    }
    throw new InvalidOperationException($"Element '{key}' was not found.");
}

static UiDefinitionBuilder CreateZeroFixture(float zero)
{
    var builder = new UiDefinitionBuilder("canonical-zero");
    builder.AddCanvas(canvas => canvas.AddVertical(
        "content",
        _ => { },
        UiChildLayout.Canvas(new UiCanvasPlacement(zero, zero, 10, 10)),
        new UiFlowOptions(spacing: zero, padding: new UiPadding(zero, zero, zero, zero))));
    return builder;
}

static UiDefinitionBuilder CreateFixture(float canvasWidth = 800)
{
    var builder = new UiDefinitionBuilder("fixture.inventory");
    builder.DeclareSlot("title", UiSlotValueKind.Text);
    builder.DeclareSlot("rows", UiSlotValueKind.List);
    builder.DeclareSlot("enabled", UiSlotValueKind.Boolean);
    builder.DeclareAction("close");
    builder.DeclareAction("select", acceptsRowContext: true);
    builder.AddCanvas(new UiCanvasOptions(canvasWidth, 600), canvas =>
    {
        canvas.AddPanel("background", "inventory.surface",
            UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, canvasWidth, 600)));
        canvas.AddVertical("content", vertical =>
        {
            vertical.AddText("title-label", "title");
            vertical.AddList("item-list", "rows", "select");
            vertical.AddHorizontal("footer", horizontal =>
            {
                horizontal.AddToggle("enabled-toggle", "enabled");
                horizontal.AddButton("close-button", "close", "title");
            });
        }, UiChildLayout.Canvas(new UiCanvasPlacement(20, 20, 760, 540)),
        new UiFlowOptions(spacing: 4, alignment: UiAlignment.Stretch));
    });
    return builder;
}

static class Assert
{
    public static void True(bool value)
    {
        if (!value)
            throw new InvalidOperationException("Expected true.");
    }

    public static void False(bool value)
    {
        if (value)
            throw new InvalidOperationException("Expected false.");
    }

    public static void Equal<T>(T expected, T actual)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
        }
    }

    public static TException Throws<TException>(Action body)
        where TException : Exception
    {
        try
        {
            body();
        }
        catch (TException exception)
        {
            return exception;
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(
                $"Expected {typeof(TException).Name}, got {exception.GetType().Name}.", exception);
        }

        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }
}
