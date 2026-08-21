using System.Diagnostics;
using UltraSim.Client.Core;
using UltraSim.UI;

var tests = new (string Name, Action Body)[]
{
    ("ColdAndHotOpenUseExactDefinitionCache", ColdAndHotOpenUseExactDefinitionCache),
    ("SupersessionClosesOldViewsAndRejectsOldHotOpen", SupersessionClosesOldViewsAndRejectsOldHotOpen),
    ("SequenceAwareDeltaPatchesExistingView", SequenceAwareDeltaPatchesExistingView),
    ("DeltaApplicationIsAtomicOnValidationFailure", DeltaApplicationIsAtomicOnValidationFailure),
    ("ListReplaceAddUpdateRemoveRemainScopedToList", ListReplaceAddUpdateRemoveRemainScopedToList),
    ("ActionsCrossLocalLinkWithoutMutatingAuthority", ActionsCrossLocalLinkWithoutMutatingAuthority),
    ("SupersessionIsMonotonicAndVersionExact", SupersessionIsMonotonicAndVersionExact),
    ("MultipleListRowsCanPatchAtomically", MultipleListRowsCanPatchAtomically),
    ("OperationAndDeltaInputsAreSnapshotted", OperationAndDeltaInputsAreSnapshotted),
    ("RendererReceivesReadOnlyModelCapabilities", RendererReceivesReadOnlyModelCapabilities),
    ("ClientRuntimeIsOwnerThreadBound", ClientRuntimeIsOwnerThreadBound),
    ("RendererFailureFaultsWithoutPublishingModelState", RendererFailureFaultsWithoutPublishingModelState),
    ("RendererCannotSubmitDuringModelPublication", RendererCannotSubmitDuringModelPublication),
    ("ActionWithoutAuthorityEndpointRejects", ActionWithoutAuthorityEndpointRejects),
    ("DeltaValidationIsDefinitionBoundBeforeCloning", DeltaValidationIsDefinitionBoundBeforeCloning),
    ("CacheIdentityAndDefaultOperationsReject", CacheIdentityAndDefaultOperationsReject),
    ("ClosedInstanceIdsDoNotPermitActionAba", ClosedInstanceIdsDoNotPermitActionAba),
    ("ClientRetainedDefinitionsAndViewsAreBounded", ClientRetainedDefinitionsAndViewsAreBounded),
    ("ClientPerformanceBaselinesAreCaptured", ClientPerformanceBaselinesAreCaptured),
    ("ClientWorldSemanticCacheProductionContracts", ClientWorldSemanticCacheTests.Run),
    ("ClientPresentationWindowIsEngineNeutralAndTransactional", ClientPresentationWindowTests.Run),
};

foreach ((string name, Action body) in tests)
{
    body();
    Console.WriteLine($"PASS {name}");
}

Console.WriteLine($"All {tests.Length} Client Core tests passed.");

static void ColdAndHotOpenUseExactDefinitionCache()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiModel model = CreateModel(definition, "Cold");
    UiDefinitionCacheKey key = CreateCacheKey(definition);
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);

    UiViewInstanceId cold = link.OpenView(new UiOpenView(key, definition, model));
    Assert.Equal(1, client.CachedDefinitionCount);
    Assert.Equal(1, renderer.CreateCount);
    Assert.Equal("Cold", client.GetValue(cold, definition.Bindings.RequireSlot("title")).Text);
    Assert.True(link.CloseView(new UiCloseView(cold)));
    Assert.Equal(1, renderer.CloseCount);

    UiViewInstanceId hot = link.OpenHotView(new UiHotOpenView(key, model));
    Assert.Equal(2, renderer.CreateCount);
    Assert.Equal("Cold", client.GetValue(hot, definition.Bindings.RequireSlot("title")).Text);
    CompiledUiDefinition missingDefinition = CreateDefinition(801);
    Assert.Throws<UiViewSupersededException>(() => link.OpenHotView(new UiHotOpenView(
        CreateCacheKey(missingDefinition),
        missingDefinition == definition ? model : CreateModel(missingDefinition, "Missing"))));
}

static void SupersessionClosesOldViewsAndRejectsOldHotOpen()
{
    CompiledUiDefinition oldDefinition = CreateDefinition(800);
    CompiledUiDefinition newDefinition = CreateDefinition(801, version: 2);
    UiModel oldModel = CreateModel(oldDefinition, "Old");
    UiModel newModel = CreateModel(newDefinition, "New");
    UiDefinitionCacheKey oldKey = CreateCacheKey(oldDefinition);
    UiDefinitionCacheKey newKey = CreateCacheKey(newDefinition);
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);

    UiViewInstanceId oldView = link.OpenView(new UiOpenView(oldKey, oldDefinition, oldModel));
    link.OpenView(new UiOpenView(newKey, newDefinition, newModel));

    Assert.False(client.TryGetView(oldView, out _));
    Assert.Equal(1, renderer.CloseCount);
    Assert.Equal(1, client.CachedDefinitionCount);
    Assert.Throws<UiViewSupersededException>(() => link.OpenHotView(new UiHotOpenView(oldKey, oldModel)));
}

static void SequenceAwareDeltaPatchesExistingView()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiSlotId rows = definition.Bindings.RequireSlot("rows");
    UiActionId toggle = definition.Bindings.RequireAction("toggle");
    UiModel model = CreateModel(definition, "Before");
    model.ReplaceRows(rows, new[] { Row(1, "A"), Row(2, "B") });
    UiDefinitionCacheKey key = CreateCacheKey(definition);
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    UiViewInstanceId instance = link.OpenView(new UiOpenView(key, definition, model));

    link.UpdateView(new UiUpdateView(instance, new UiModelDelta(
        definition.Handle,
        sequence: 1,
        scalarSlots: new[] { new UiScalarSlotPatch(title, UiValue.FromText("After")) },
        lists: new[] { UiListPatch.Add(rows, Row(3, "C")) },
        actions: new[] { new UiActionStatePatch(toggle, new UiActionState(false, "Locked")) })));

    Assert.Equal(1, renderer.CreateCount);
    Assert.Equal(1, renderer.ApplyCount);
    Assert.Equal(1, renderer.LastDelta!.Lists.Length);
    Assert.Equal("After", client.GetValue(instance, title).Text);
    Assert.Equal(3, client.GetRows(instance, rows).Length);
    Assert.False(client.GetActionState(instance, toggle).Enabled);
    Assert.Equal((ulong)1, client.GetView(instance).LastSequence);

    Assert.Throws<InvalidOperationException>(() => link.UpdateView(new UiUpdateView(instance,
        new UiModelDelta(definition.Handle, 1,
            scalarSlots: new[] { new UiScalarSlotPatch(title, UiValue.FromText("Stale")) }))));
    Assert.Equal("After", client.GetValue(instance, title).Text);
    Assert.Equal(1, renderer.ApplyCount);
}

static void DeltaApplicationIsAtomicOnValidationFailure()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiSlotId rows = definition.Bindings.RequireSlot("rows");
    UiModel model = CreateModel(definition, "Stable");
    model.ReplaceRows(rows, new[] { Row(1, "A") });
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    UiViewInstanceId instance = link.OpenView(new UiOpenView(CreateCacheKey(definition), definition, model));

    Assert.Throws<InvalidDataException>(() => link.UpdateView(new UiUpdateView(instance,
        new UiModelDelta(definition.Handle, 1,
            scalarSlots: new[] { new UiScalarSlotPatch(title, UiValue.FromText("MustNotCommit")) },
            lists: new[] { UiListPatch.Add(rows, Row(1, "Duplicate")) }))));

    Assert.Equal("Stable", client.GetValue(instance, title).Text);
    Assert.Equal(1, client.GetRows(instance, rows).Length);
    Assert.Equal(0, renderer.ApplyCount);
}

static void ListReplaceAddUpdateRemoveRemainScopedToList()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiSlotId rows = definition.Bindings.RequireSlot("rows");
    UiModel model = CreateModel(definition, "Rows");
    model.ReplaceRows(rows, new[] { Row(1, "A") });
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    UiViewInstanceId instance = link.OpenView(new UiOpenView(CreateCacheKey(definition), definition, model));

    link.UpdateView(new UiUpdateView(instance, new UiModelDelta(definition.Handle, 1,
        lists: new[] { UiListPatch.Replace(rows, new[] { Row(2, "B") }) })));
    link.UpdateView(new UiUpdateView(instance, new UiModelDelta(definition.Handle, 2,
        lists: new[] { UiListPatch.Update(rows, Row(2, "B2")) })));
    link.UpdateView(new UiUpdateView(instance, new UiModelDelta(definition.Handle, 3,
        lists: new[] { UiListPatch.Add(rows, Row(3, "C")) })));
    link.UpdateView(new UiUpdateView(instance, new UiModelDelta(definition.Handle, 4,
        lists: new[] { UiListPatch.Remove(rows, new UiRowId(2)) })));

    Assert.Equal(1, renderer.CreateCount);
    Assert.Equal(4, renderer.ApplyCount);
    Assert.Equal(4, renderer.ListPatchApplyCount);
    Assert.Equal(new UiRowId(3), client.GetRows(instance, rows)[0].Id);
    Assert.Equal("C", client.GetRows(instance, rows)[0].Values[0].Text);
}

static void ActionsCrossLocalLinkWithoutMutatingAuthority()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiActionId select = definition.Bindings.RequireAction("select");
    UiActionId toggle = definition.Bindings.RequireAction("toggle");
    UiModel serverModel = CreateModel(definition, "Server");
    UiModel clientModel = serverModel.Clone();
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    UiActionRequest? received = null;
    link.ActionSubmitted += request => received = request;
    UiViewInstanceId instance = link.OpenView(new UiOpenView(
        CreateCacheKey(definition), definition, clientModel));

    client.SubmitAction(instance, select, new UiRowId(7), clientSequence: 12);
    Assert.True(received.HasValue);
    Assert.Equal(select, received!.Value.Action);
    Assert.Equal(new UiRowId(7), received.Value.Row);
    Assert.Equal((ulong)12, received.Value.ClientSequence);
    Assert.Equal("Server", serverModel.Get(definition.Bindings.RequireSlot("title")).Text);

    client.SubmitAction(instance, toggle, input: UiValue.FromBoolean(true));
    Assert.Equal(toggle, received.Value.Action);
    Assert.Equal(UiValueKind.Boolean, received.Value.Input.Kind);
}

static void SupersessionIsMonotonicAndVersionExact()
{
    CompiledUiDefinition v1 = CreateDefinition(800, version: 1);
    CompiledUiDefinition v2 = CreateDefinition(801, version: 2);
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    link.OpenView(new UiOpenView(CreateCacheKey(v1), v1, CreateModel(v1, "v1")));
    UiViewInstanceId current = link.OpenView(new UiOpenView(
        CreateCacheKey(v2), v2, CreateModel(v2, "v2")));

    Assert.Throws<UiViewSupersededException>(() => link.OpenView(new UiOpenView(
        CreateCacheKey(v1), v1, CreateModel(v1, "stale"))));
    Assert.True(client.TryGetView(current, out _));
    Assert.Equal(2, renderer.CreateCount);

    CompiledUiDefinition conflicting = CreateDefinition(802, version: 2);
    Assert.Throws<InvalidDataException>(() => link.OpenView(new UiOpenView(
        CreateCacheKey(conflicting), conflicting, CreateModel(conflicting, "conflict"))));
    Assert.True(client.TryGetView(current, out _));
    Assert.Equal(2, renderer.CreateCount);

    var replacementRenderer = new MemoryRenderer();
    var replacementClient = new UiClientRuntime(replacementRenderer);
    var replacementLink = new LocalSemanticLink(replacementClient);
    var retainedInstance = new UiViewInstanceId(77);
    replacementLink.OpenView(new UiOpenView(
        CreateCacheKey(v1), v1, CreateModel(v1, "v1"), retainedInstance));
    UiViewInstanceId replaced = replacementLink.OpenView(new UiOpenView(
        CreateCacheKey(v2), v2, CreateModel(v2, "v2"), retainedInstance));
    Assert.Equal(retainedInstance, replaced);
    Assert.Equal(1, replacementClient.OpenViewCount);
    Assert.Equal(1, replacementRenderer.CloseCount);
}

static void MultipleListRowsCanPatchAtomically()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiSlotId rows = definition.Bindings.RequireSlot("rows");
    UiModel model = CreateModel(definition, "Rows");
    model.ReplaceRows(rows, new[] { Row(1, "A") });
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    UiViewInstanceId instance = link.OpenView(new UiOpenView(
        CreateCacheKey(definition), definition, model));

    link.UpdateView(new UiUpdateView(instance, new UiModelDelta(
        definition.Handle,
        1,
        lists: new[]
        {
            UiListPatch.Update(rows, Row(1, "A2")),
            UiListPatch.Add(rows, Row(2, "B")),
            UiListPatch.Add(rows, Row(3, "C")),
        })));

    Assert.Equal(3, client.GetRows(instance, rows).Length);
    Assert.Equal("A2", client.GetRows(instance, rows)[0].Values[0].Text);
    Assert.Equal(3, renderer.ListPatchApplyCount);
    Assert.Throws<InvalidDataException>(() => new UiModelDelta(
        definition.Handle,
        2,
        lists: new[]
        {
            UiListPatch.Update(rows, Row(1, "X")),
            UiListPatch.Remove(rows, new UiRowId(1)),
        }));
    Assert.Throws<InvalidDataException>(() => new UiModelDelta(
        definition.Handle,
        2,
        lists: new[]
        {
            UiListPatch.Replace(rows, new[] { Row(1, "X") }),
            UiListPatch.Add(rows, Row(2, "Y")),
        }));
}

static void OperationAndDeltaInputsAreSnapshotted()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiModel source = CreateModel(definition, "Captured");
    var open = new UiOpenView(CreateCacheKey(definition), definition, source);
    source.SetText(title, "Mutated source");
    UiModel exposedSnapshot = open.SnapshotModel();
    exposedSnapshot.SetText(title, "Mutated snapshot");

    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    UiViewInstanceId instance = link.OpenView(open);
    Assert.Equal("Captured", client.GetValue(instance, title).Text);

    UiScalarSlotPatch[] patches =
    [
        new UiScalarSlotPatch(title, UiValue.FromText("Delta captured")),
    ];
    var delta = new UiModelDelta(definition.Handle, 1, scalarSlots: patches);
    patches[0] = new UiScalarSlotPatch(title, UiValue.FromText("Mutated array"));
    link.UpdateView(new UiUpdateView(instance, delta));
    Assert.Equal("Delta captured", client.GetValue(instance, title).Text);
}

static void RendererReceivesReadOnlyModelCapabilities()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    var renderer = new InspectingRenderer(definition.Bindings.RequireSlot("title"));
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    UiViewInstanceId instance = link.OpenView(new UiOpenView(
        CreateCacheKey(definition), definition, CreateModel(definition, "Read only")));

    Assert.Equal("Read only", renderer.LastTitle);
    Assert.True(renderer.ReceivedValidView);
    Assert.Equal("Read only", client.GetValue(instance, definition.Bindings.RequireSlot("title")).Text);
}

static void ClientRuntimeIsOwnerThreadBound()
{
    var client = new UiClientRuntime(new MemoryRenderer());
    Exception? observed = null;
    var thread = new Thread(() =>
    {
        try { _ = client.OpenViewCount; }
        catch (Exception exception) { observed = exception; }
    });
    thread.Start();
    thread.Join();
    Assert.True(observed is InvalidOperationException);
}

static void RendererFailureFaultsWithoutPublishingModelState()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    var renderer = new ThrowingRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    UiViewInstanceId instance = link.OpenView(new UiOpenView(
        CreateCacheKey(definition), definition, CreateModel(definition, "Stable")));
    renderer.ThrowOnApply = true;

    Assert.Throws<UiClientRuntimeFaultedException>(() => link.UpdateView(new UiUpdateView(
        instance,
        new UiModelDelta(
            definition.Handle,
            1,
            scalarSlots: new[] { new UiScalarSlotPatch(title, UiValue.FromText("Candidate")) }))));
    Assert.True(client.IsFaulted);
    Assert.Equal("Stable", client.GetValue(instance, title).Text);
    Assert.Equal((ulong)0, client.GetView(instance).LastSequence);
    Assert.Throws<UiClientRuntimeFaultedException>(() => link.UpdateView(new UiUpdateView(
        instance,
        new UiModelDelta(definition.Handle, 2))));
}

static void RendererCannotSubmitDuringModelPublication()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiActionId close = definition.Bindings.RequireAction("close");
    var renderer = new ReentrantRenderer(close);
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    int submitted = 0;
    link.ActionSubmitted += _ => submitted++;
    UiViewInstanceId instance = link.OpenView(new UiOpenView(
        CreateCacheKey(definition), definition, CreateModel(definition, "Stable")));
    renderer.SubmitDuringApply = true;

    Assert.Throws<UiClientRuntimeFaultedException>(() => link.UpdateView(new UiUpdateView(
        instance,
        new UiModelDelta(
            definition.Handle,
            1,
            scalarSlots: new[] { new UiScalarSlotPatch(title, UiValue.FromText("Candidate")) }))));

    Assert.True(client.IsFaulted);
    Assert.Equal(0, submitted);
    Assert.Equal("Stable", client.GetValue(instance, title).Text);
    Assert.Equal((ulong)0, client.GetView(instance).LastSequence);
}

static void ActionWithoutAuthorityEndpointRejects()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiActionId select = definition.Bindings.RequireAction("select");
    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    UiViewInstanceId instance = link.OpenView(new UiOpenView(
        CreateCacheKey(definition), definition, CreateModel(definition, "No endpoint")));

    Assert.Throws<InvalidOperationException>(() => client.SubmitAction(
        instance, select, new UiRowId(1)));
}

static void DeltaValidationIsDefinitionBoundBeforeCloning()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiSlotId rows = definition.Bindings.RequireSlot("rows");
    var renderer = new MemoryRenderer();
    var client = new UiClientRuntime(renderer);
    var link = new LocalSemanticLink(client);
    UiViewInstanceId instance = link.OpenView(new UiOpenView(
        CreateCacheKey(definition), definition, CreateModel(definition, "Stable")));

    Assert.Throws<InvalidDataException>(() => link.UpdateView(new UiUpdateView(
        instance,
        new UiModelDelta(
            definition.Handle,
            1,
            scalarSlots: new[] { new UiScalarSlotPatch(rows, UiValue.FromText("wrong")) }))));
    Assert.Throws<InvalidDataException>(() => link.UpdateView(new UiUpdateView(
        instance,
        new UiModelDelta(
            definition.Handle,
            1,
            lists: new[] { UiListPatch.Add(title, Row(1, "wrong")) }))));
    Assert.Equal(0, renderer.ApplyCount);
    Assert.Equal("Stable", client.GetValue(instance, title).Text);
}

static void CacheIdentityAndDefaultOperationsReject()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    Assert.Throws<ArgumentOutOfRangeException>(() => new UiDefinitionCacheKey(
        new string('x', UiDefinitionCacheKey.MaximumIdentityLength + 1),
        "composition",
        1,
        definition.Key,
        definition.Version,
        definition.Hash));
    Assert.Throws<ArgumentException>(() => new UiDefinitionCacheKey(
        "server\uD800",
        "composition",
        1,
        definition.Key,
        definition.Version,
        definition.Hash));

    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    Assert.Throws<ArgumentNullException>(() => link.OpenView(default));
    Assert.Throws<KeyNotFoundException>(() => link.OpenHotView(default));
}

static void ClosedInstanceIdsDoNotPermitActionAba()
{
    CompiledUiDefinition definition = CreateDefinition(800);
    UiDefinitionCacheKey key = CreateCacheKey(definition);
    var client = new UiClientRuntime(new MemoryRenderer());
    var link = new LocalSemanticLink(client);
    var explicitInstance = new UiViewInstanceId(50);
    link.OpenView(new UiOpenView(
        key, definition, CreateModel(definition, "first"), explicitInstance));
    Assert.True(link.CloseView(new UiCloseView(explicitInstance)));
    Assert.Throws<InvalidOperationException>(() => link.OpenHotView(new UiHotOpenView(
        key, CreateModel(definition, "reused"), explicitInstance)));

    UiViewInstanceId generated = link.OpenHotView(new UiHotOpenView(
        key, CreateModel(definition, "next")));
    Assert.Equal((ulong)51, generated.Value);
}

static void ClientRetainedDefinitionsAndViewsAreBounded()
{
    CompiledUiDefinition first = CreateDefinition(800);
    var client = new UiClientRuntime(
        new MemoryRenderer(),
        new UiClientLimits(MaximumCachedDefinitionFamilies: 1, MaximumOpenViews: 1));
    var link = new LocalSemanticLink(client);
    UiDefinitionCacheKey firstKey = CreateCacheKey(first);
    UiViewInstanceId firstView = link.OpenView(new UiOpenView(
        firstKey, first, CreateModel(first, "First")));
    Assert.Throws<InvalidOperationException>(() => link.OpenHotView(new UiHotOpenView(
        firstKey, CreateModel(first, "Second"))));
    Assert.True(link.CloseView(new UiCloseView(firstView)));

    var source = new UiDefinitionBuilder("client.other");
    source.AddCanvas(_ => { });
    CompiledUiDefinition second = UiDefinitionCompiler.Compile(source);
    Assert.Throws<InvalidOperationException>(() => link.OpenView(new UiOpenView(
        CreateCacheKey(second), second, new UiModel(second))));
}

static void ClientPerformanceBaselinesAreCaptured()
{
    const int warmup = 8;
    const int samples = 31;
    CompiledUiDefinition definition = CreateDefinition(800);
    UiDefinitionCacheKey key = CreateCacheKey(definition);
    UiSlotId title = definition.Bindings.RequireSlot("title");
    UiSlotId rows = definition.Bindings.RequireSlot("rows");
    UiModel model = CreateModel(definition, "Baseline");
    model.ReplaceRows(rows, new[] { Row(1, "A"), Row(2, "B") });

    for (int index = 0; index < warmup; index++)
    {
        var warmClient = new UiClientRuntime(new MemoryRenderer());
        var warmLink = new LocalSemanticLink(warmClient);
        UiViewInstanceId warmView = warmLink.OpenView(new UiOpenView(key, definition, model));
        warmLink.UpdateView(new UiUpdateView(warmView, new UiModelDelta(
            definition.Handle,
            1,
            scalarSlots: new[] { new UiScalarSlotPatch(title, UiValue.FromText("warm")) })));
        warmLink.UpdateView(new UiUpdateView(warmView, new UiModelDelta(
            definition.Handle,
            2,
            lists: new[] { UiListPatch.Update(rows, Row(1, "warm")) })));
        warmLink.CloseView(new UiCloseView(warmView));
    }

    Measurement cold = Measure(samples, 64, _ =>
    {
        var links = new LocalSemanticLink[64];
        var operations = new UiOpenView[64];
        var instances = new UiViewInstanceId[64];
        for (int index = 0; index < links.Length; index++)
        {
            var client = new UiClientRuntime(new MemoryRenderer());
            links[index] = new LocalSemanticLink(client);
            operations[index] = new UiOpenView(key, definition, model);
        }
        return new OperationBatch(
            () =>
            {
                for (int index = 0; index < links.Length; index++)
                    instances[index] = links[index].OpenView(operations[index]);
            },
            () =>
            {
                for (int index = 0; index < links.Length; index++)
                    links[index].CloseView(new UiCloseView(instances[index]));
            });
    });

    Measurement hot = Measure(samples, 256, _ =>
    {
        var client = new UiClientRuntime(new MemoryRenderer());
        var link = new LocalSemanticLink(client);
        UiViewInstanceId seed = link.OpenView(new UiOpenView(key, definition, model));
        link.CloseView(new UiCloseView(seed));
        var operation = new UiHotOpenView(key, model);
        var instances = new UiViewInstanceId[256];
        return new OperationBatch(
            () =>
            {
                for (int index = 0; index < instances.Length; index++)
                    instances[index] = link.OpenHotView(operation);
            },
            () =>
            {
                foreach (UiViewInstanceId instance in instances)
                    link.CloseView(new UiCloseView(instance));
            });
    });

    Measurement scalar = Measure(samples, 256, _ =>
    {
        var client = new UiClientRuntime(new MemoryRenderer());
        var link = new LocalSemanticLink(client);
        UiViewInstanceId instance = link.OpenView(new UiOpenView(key, definition, model));
        var deltas = new UiModelDelta[256];
        for (int index = 0; index < deltas.Length; index++)
        {
            deltas[index] = new UiModelDelta(
                definition.Handle,
                (ulong)index + 1,
                scalarSlots: new[]
                {
                    new UiScalarSlotPatch(title, UiValue.FromText(index.ToString())),
                });
        }
        return new OperationBatch(() =>
        {
            foreach (UiModelDelta delta in deltas)
            {
                link.UpdateView(new UiUpdateView(instance, delta));
            }
        });
    });

    Measurement list = Measure(samples, 256, _ =>
    {
        var client = new UiClientRuntime(new MemoryRenderer());
        var link = new LocalSemanticLink(client);
        UiViewInstanceId instance = link.OpenView(new UiOpenView(key, definition, model));
        var deltas = new UiModelDelta[256];
        for (int index = 0; index < deltas.Length; index++)
        {
            deltas[index] = new UiModelDelta(
                definition.Handle,
                (ulong)index + 1,
                lists: new[] { UiListPatch.Update(rows, Row(1, index.ToString())) });
        }
        return new OperationBatch(() =>
        {
            foreach (UiModelDelta delta in deltas)
                link.UpdateView(new UiUpdateView(instance, delta));
        });
    });

    Console.WriteLine(
        $"BASELINE S0.4 samples={samples} " +
        $"cold_open_mean={cold.Mean:F0}ns cold_open_p95={cold.P95:F0}ns cold_open_alloc={cold.MeanBytes:F0}B " +
        $"hot_open_mean={hot.Mean:F0}ns hot_open_p95={hot.P95:F0}ns hot_open_alloc={hot.MeanBytes:F0}B " +
        $"scalar_delta_mean={scalar.Mean:F0}ns scalar_delta_p95={scalar.P95:F0}ns scalar_delta_alloc={scalar.MeanBytes:F0}B " +
        $"list_row_delta_mean={list.Mean:F0}ns list_row_delta_p95={list.P95:F0}ns list_row_delta_alloc={list.MeanBytes:F0}B");
}

static Measurement Measure(
    int samples,
    int operationsPerSample,
    Func<int, OperationBatch> prepare)
{
    const int discardedWarmupSamples = 5;
    var elapsed = new long[samples];
    var bytes = new long[samples];
    for (int iteration = 0; iteration < samples + discardedWarmupSamples; iteration++)
    {
        OperationBatch operation = prepare(iteration);
        long beforeBytes = GC.GetAllocatedBytesForCurrentThread();
        long start = Stopwatch.GetTimestamp();
        operation.Body();
        long elapsedNanoseconds = Stopwatch.GetElapsedTime(start).Ticks * 100;
        long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - beforeBytes;
        operation.Cleanup?.Invoke();
        if (iteration < discardedWarmupSamples)
            continue;
        int sample = iteration - discardedWarmupSamples;
        elapsed[sample] = elapsedNanoseconds / operationsPerSample;
        bytes[sample] = allocatedBytes / operationsPerSample;
    }
    Array.Sort(elapsed);
    return new Measurement(
        elapsed.Average(static value => (double)value),
        elapsed[(int)Math.Ceiling(samples * 0.95) - 1],
        bytes.Average(static value => (double)value));
}

static CompiledUiDefinition CreateDefinition(float width, ushort version = 1)
{
    var builder = new UiDefinitionBuilder("client.fixture", version);
    UiSlotReference title = builder.DeclareSlot("title", UiSlotValueKind.Text);
    UiSlotReference rows = builder.DeclareSlot("rows", UiSlotValueKind.List);
    UiSlotReference enabled = builder.DeclareSlot("enabled", UiSlotValueKind.Boolean);
    UiActionReference select = builder.DeclareAction("select", acceptsRowContext: true);
    UiActionReference close = builder.DeclareAction("close");
    UiActionReference toggle = builder.DeclareAction("toggle", inputKind: UiValueKind.Boolean);
    builder.AddCanvas(new UiCanvasOptions(width, 600), canvas =>
    {
        canvas.AddText("title-label", title,
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 10, 200, 24)));
        canvas.AddList("list", rows, select,
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 50, 300, 200)));
        canvas.AddToggle("toggle", enabled, toggle,
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 270, 100, 24)));
        canvas.AddButton("close", close, title,
            UiChildLayout.Canvas(new UiCanvasPlacement(10, 310, 100, 24)));
    });
    return UiDefinitionCompiler.Compile(builder);
}

static UiModel CreateModel(CompiledUiDefinition definition, string title)
{
    var model = new UiModel(definition);
    model.SetText(definition.Bindings.RequireSlot("title"), title);
    model.SetBoolean(definition.Bindings.RequireSlot("enabled"), true);
    model.ReplaceRows(definition.Bindings.RequireSlot("rows"), [Row(7, "Target")]);
    return model;
}

static UiDefinitionCacheKey CreateCacheKey(CompiledUiDefinition definition) => new(
    "server",
    "composition",
    1,
    definition.Key,
    definition.Version,
    definition.Hash);

static UiListRow Row(ulong id, string text) =>
    new(new UiRowId(id), new[] { UiValue.FromText(text) });

readonly record struct Measurement(double Mean, double P95, double MeanBytes);

readonly record struct OperationBatch(Action Body, Action? Cleanup = null);

sealed class MemoryRenderer : IUiRenderer
{
    public int CreateCount { get; private set; }
    public int ApplyCount { get; private set; }
    public int CloseCount { get; private set; }
    public int ListPatchApplyCount { get; private set; }
    public UiModelDelta? LastDelta { get; private set; }

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        CreateCount++;
        return new MemoryHandle(instance);
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction)
    {
        ApplyCount++;
        LastDelta = delta;
        ListPatchApplyCount += delta.Lists.Length;
    }

    public void Close(IUiRenderHandle handle) => CloseCount++;

    private sealed record MemoryHandle(UiViewInstanceId Instance) : IUiRenderHandle;
}

sealed class InspectingRenderer : IUiRenderer
{
    private readonly UiSlotId _title;

    public InspectingRenderer(UiSlotId title) => _title = title;

    public bool ReceivedValidView { get; private set; }
    public string? LastTitle { get; private set; }

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        ReceivedValidView = model.IsValid;
        LastTitle = model.Get(_title).Text;
        return new Handle();
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction)
    { }

    public void Close(IUiRenderHandle handle) { }

    private sealed class Handle : IUiRenderHandle { }
}

sealed class ThrowingRenderer : IUiRenderer
{
    public bool ThrowOnApply { get; set; }

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction) => new Handle();

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction)
    {
        if (ThrowOnApply)
            throw new InvalidOperationException("Injected renderer failure.");
    }

    public void Close(IUiRenderHandle handle) { }

    private sealed class Handle : IUiRenderHandle { }
}

sealed class ReentrantRenderer : IUiRenderer
{
    private readonly UiActionId _action;

    public ReentrantRenderer(UiActionId action) => _action = action;

    public bool SubmitDuringApply { get; set; }

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction) => new Handle(instance);

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction)
    {
        if (SubmitDuringApply)
        {
            var typed = (Handle)handle;
            submitAction(new UiActionRequest(
                typed.Instance,
                definition.Handle,
                _action));
        }
    }

    public void Close(IUiRenderHandle handle) { }

    private sealed record Handle(UiViewInstanceId Instance) : IUiRenderHandle;
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
            throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
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
