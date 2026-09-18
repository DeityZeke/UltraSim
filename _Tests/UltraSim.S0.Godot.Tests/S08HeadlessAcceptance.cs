using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Inventory;
using UltraSim.Game.UI;
using UltraSim.Server.Runtime;
using UltraSim.UI;

/// <summary>
/// Native Godot acceptance for the exact S0.8 game UI definitions. Server/game
/// dependencies remain test-only and do not enter the production Godot wrapper.
/// </summary>
public partial class S08HeadlessAcceptance : Control
{
    public override void _Ready()
    {
        try
        {
            RunAcceptance();
            RunNativeAdapterBoundaryAcceptance();
            RunOutsidePointerPolicyAcceptance();
            RunRemoteInventoryAcceptance();
            GD.Print("S0.8 / I0.11 GODOT ACCEPTANCE PASS");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"S0.8 GODOT ACCEPTANCE FAIL: {exception}");
            GetTree().Quit(1);
        }
    }

    private void RunNativeAdapterBoundaryAcceptance()
    {
        var toggleSource = new UiDefinitionBuilder("native-toggle-reset");
        toggleSource.DeclareSlot("enabled", UiSlotValueKind.Boolean);
        toggleSource.DeclareAction("toggle", inputKind: UiValueKind.Boolean);
        toggleSource.AddCanvas(canvas => canvas.AddToggle(
            "toggle",
            "enabled",
            "toggle",
            UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 120, 30))));
        CompiledUiDefinition toggleDefinition = UiDefinitionCompiler.Compile(toggleSource);
        UiSlotId enabled = toggleDefinition.Bindings.RequireSlot("enabled");
        var initial = new UiModel(toggleDefinition);
        initial.SetBoolean(enabled, true);
        int submittedActions = 0;
        using (var toggleClient = new UiClientRuntime(new GodotUiRenderer(this)))
        {
            var toggleLink = new LocalSemanticLink(toggleClient);
            toggleLink.ActionSubmitted += _ => submittedActions++;
            UiViewInstanceId view = toggleLink.OpenView(new UiOpenView(
                CacheKey(toggleDefinition), toggleDefinition, initial));
            CheckBox toggle = Find<CheckBox>(GetNode<Control>($"UiView_{view.Value}"), "Element_toggle");
            Expect(toggle.ButtonPressed, "The native Boolean fixture did not begin pressed.");
            toggleLink.UpdateView(new UiUpdateView(view, new UiModelDelta(
                toggleDefinition.Handle,
                sequence: 1,
                scalarSlots: [new UiScalarSlotPatch(enabled, UiValue.Unset)])));
            Expect(!toggle.ButtonPressed,
                "A true-to-Unset Boolean update retained stale pressed state in Godot.");
            Expect(submittedActions == 0,
                "The programmatic Boolean reset emitted client action intent.");
        }

        var oversizedSource = new UiDefinitionBuilder("native-flow-range");
        oversizedSource.AddCanvas(canvas => canvas.AddVertical(
            "oversized",
            _ => { },
            UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 100, 100)),
            new UiFlowOptions(spacing: float.MaxValue)));
        CompiledUiDefinition oversized = UiDefinitionCompiler.Compile(oversizedSource);
        var oversizedModel = new UiModel(oversized);
        using var oversizedClient = new UiClientRuntime(new GodotUiRenderer(this));
        UiClientRuntimeFaultedException rejected = ExpectThrows<UiClientRuntimeFaultedException>(() =>
            oversizedClient.OpenView(new UiOpenView(
                CacheKey(oversized), oversized, oversizedModel, new UiViewInstanceId(9_001))));
        Expect(rejected.InnerException is InvalidDataException,
            "Oversized native flow constants were not rejected by adapter admission.");
        Expect(GetNodeOrNull<Control>("UiView_9001") is null,
            "Rejected native UI admission retained a partial control root.");
    }

    private void RunOutsidePointerPolicyAcceptance()
    {
        Expect(InventoryUi.Definition.OutsidePointerBehavior ==
                UiOutsidePointerBehavior.PassThrough,
            "Inventory did not retain the default modeless outside-input policy.");
        Expect(CharacterSelectUi.Definition.OutsidePointerBehavior ==
                UiOutsidePointerBehavior.Block,
            "CharacterSelect did not declare its modal input policy.");
        Expect(InformationalSignUi.Definition.OutsidePointerBehavior ==
                UiOutsidePointerBehavior.Close &&
            InformationalSignUi.Definition.OutsideClickAction == InformationalSignUi.Close,
            "InformationalSign did not declare its semantic outside-close action.");

        var model = new UiModel(InformationalSignUi.Definition);
        model.SetText(InformationalSignUi.Title, "Sign");
        model.SetText(InformationalSignUi.Body, "Body");
        model.SetText(InformationalSignUi.CloseLabel, "Close");
        var renderer = new GodotUiRenderer(this);
        using var client = new UiClientRuntime(renderer);
        var link = new LocalSemanticLink(client);
        UiActionRequest submitted = default;
        int submissions = 0;
        link.ActionSubmitted += request =>
        {
            submitted = request;
            submissions++;
        };
        UiViewInstanceId view = link.OpenView(new UiOpenView(
            CacheKey(InformationalSignUi.Definition),
            InformationalSignUi.Definition,
            model));
        Control root = GetNode<Control>($"UiView_{view.Value}");
        Panel surface = Find<Panel>(root, "Element_background");
        Vector2 inside = surface.GetGlobalRect().GetCenter();
        Vector2 outside = surface.GetGlobalRect().End + new Vector2(8, 8);
        Expect(renderer.ResolveGameplayInput(new InputEventMouseButton
            {
                Position = inside,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }) == GodotUiGameplayInputDisposition.UiSurface && submissions == 0,
            "Pointer input over a compiled UI surface leaked through to gameplay.");
        Expect(renderer.ResolveGameplayInput(new InputEventMouseButton
            {
                Position = outside,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }) == GodotUiGameplayInputDisposition.OutsideCloseSubmitted &&
            submissions == 1 && submitted.Instance == view &&
            submitted.Action == InformationalSignUi.Close,
            "An outside sign click did not submit the ordinary semantic Close action.");
    }

    private static UiDefinitionCacheKey CacheKey(CompiledUiDefinition definition) => new(
        "native-test",
        "adapter-boundary",
        protocolVersion: 1,
        definition.Key,
        definition.Version,
        definition.Hash);

    private static TException ExpectThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException exception) { return exception; }
        throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
    }

    private void RunAcceptance()
    {
        var composition = new UltraSimGameComposition(new InventoryLimits(8, 16, 8));
        using var runtime = new ServerRuntime(
            composition,
            new ServerRuntimeOptions(
                UltraSimGameComposition.CreateWorldOptions(
                    64,
                    new PersistenceIdentityOptions(new PersistentWorldId(0x80_0080))),
                WorldClockOptions.Default));
        runtime.Initialize();
        runtime.Start();
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, Fixture>(
            (world, generationId, game) =>
            {
                Expect(world.TryInstantiate(game.Content.Character, out Entity alice),
                    "The native acceptance could not create Alice.");
                Expect(game.Inventory.TryCreateOwnedContainer(
                    world, game.Content.Backpack, alice, 8, out Entity backpack),
                    "The native acceptance could not create Backpack.");
                Expect(game.Inventory.TryCreateItem(
                    world, backpack, game.Content.Bread, 5, out Entity bread),
                    "The native acceptance could not create Bread.");
                Expect(game.Inventory.TryCreateItem(
                    world, backpack, game.Content.IronSword, 1, out Entity sword),
                    "The native acceptance could not create Iron Sword.");
                PersistentEntityId aliceId = world.GetOrAssignPersistentId(alice);
                _ = world.GetOrAssignPersistentId(backpack);
                _ = world.GetOrAssignPersistentId(bread);
                _ = world.GetOrAssignPersistentId(sword);
                Expect(world.TryGetPrefabProvenance(alice, out PrefabKey prefab),
                    "The native Alice fixture has no prefab provenance.");
                var account = new AccountId(1);
                game.Accounting.AddAccount(new AccountRecord(account));
                game.Accounting.BindTrustedIdentity(new TrustedLocalIdentity("alice-godot"), account);
                game.Accounting.AddCharacter(new CharacterRecord(
                    new CharacterId(1),
                    account,
                    0,
                    "Alice",
                    prefab,
                    new PersistentEntityKey(world.PersistentWorldId, aliceId)));
                return new Fixture(alice, backpack);
            },
            out Fixture fixture),
            "The native acceptance fixture did not run.");

        var renderer = new GodotUiRenderer(this);
        var client = new UiClientRuntime(renderer);
        var link = new LocalSemanticLink(client);
        using var port = new NativeUiPort(link);
        using var admission = new AdmissionService(runtime.Game, composition, port);
        AdmissionResult waiting = admission.JoinAccountBacked(
            new ParticipantId(1),
            new TrustedLocalIdentity("alice-godot"));
        Expect(waiting.IsWaitingForCharacter, "Native account-backed admission did not open CharacterSelect.");
        UiViewInstanceId characterView = waiting.CharacterSelectView;
        Control characterRoot = GetNode<Control>($"UiView_{characterView.Value}");
        Expect(renderer.BlocksGameplayInput &&
            renderer.ResolveGameplayInput(new InputEventKey
            {
                Keycode = Key.W,
                Pressed = true,
            }) == GodotUiGameplayInputDisposition.Blocked,
            "CharacterSelect did not block gameplay input while admission was incomplete.");
        VBoxContainer characterRows = Find<VBoxContainer>(characterRoot, "Element_characters");
        Expect(characterRows.GetChildCount() == 1,
            "The native CharacterSelect list did not render Alice.");
        Expect(ContainsLabel(characterRows, "Alice"),
            "The native CharacterSelect row did not show Alice's display name.");
        AdmissionResult hot = admission.ReopenCharacterSelect(new ParticipantId(1));
        Expect(hot.IsWaitingForCharacter, "Native CharacterSelect did not reopen through Admission.");
        UiViewInstanceId hotCharacter = hot.CharacterSelectView;
        Expect(GetNodeOrNull<Control>($"UiView_{hotCharacter.Value}") is not null,
            "The native CharacterSelect definition did not reopen from cache.");
        ReadOnlySpan<UiListRow> hotRows = client.GetRows(hotCharacter, CharacterSelectUi.Characters);
        client.SubmitAction(hotCharacter, CharacterSelectUi.Play, hotRows[0].Id);
        Expect(admission.TryGetSession(new ParticipantId(1), out AdmissionResult entered) &&
            entered.IsEntered && entered.Entity == fixture.Alice,
            "Native CharacterSelect Play did not enter Alice.");

        using var inventory = new InventoryUiController(runtime.Game, port);
        InventoryUiOpenResult opened = inventory.Open(fixture.Backpack);
        Expect(opened.Succeeded, "Native Inventory did not open.");
        UiViewInstanceId inventoryView = opened.View;
        Control inventoryRoot = GetNode<Control>($"UiView_{inventoryView.Value}");
        Panel inventorySurface = Find<Panel>(inventoryRoot, "Element_background");
        Expect(renderer.ResolveGameplayInput(new InputEventMouseButton
            {
                Position = inventorySurface.GetGlobalRect().GetCenter(),
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            }) == GodotUiGameplayInputDisposition.UiSurface,
            "Inventory allowed a world click through its visible UI surface.");
        Vector2 outsideInventory = inventorySurface.GetGlobalRect().End + new Vector2(8, 8);
        GodotUiGameplayInputDisposition inventoryOutsideDisposition =
            renderer.ResolveGameplayInput(new InputEventMouseButton
            {
                Position = outsideInventory,
                ButtonIndex = MouseButton.Left,
                Pressed = true,
            });
        Expect(inventoryOutsideDisposition == GodotUiGameplayInputDisposition.Gameplay &&
            !renderer.BlocksGameplayInput,
            $"Inventory did not allow outside gameplay input while remaining open " +
            $"(disposition={inventoryOutsideDisposition}, blocks={renderer.BlocksGameplayInput}).");
        VBoxContainer inventoryRows = Find<VBoxContainer>(inventoryRoot, "Element_items");
        Expect(inventoryRows.GetChildCount() == 2 &&
            ContainsLabel(inventoryRows, "Bread") &&
            ContainsLabel(inventoryRows, "Iron Sword"),
            "The native Inventory view did not show Bread and Iron Sword.");

        UiRowId bread = FindRow(client.GetRows(inventoryView, InventoryUi.Items), "Bread");
        client.SubmitAction(inventoryView, InventoryUi.Select, bread);
        client.SubmitAction(inventoryView, InventoryUi.Split);
        Expect(ReferenceEquals(inventoryRoot, GetNode<Control>($"UiView_{inventoryView.Value}")),
            "Inventory row deltas rebuilt the native root control.");
        Expect(inventoryRows.GetChildCount() == 3,
            "The native Split delta did not update and add rows.");

        UiRowId sword = FindRow(client.GetRows(inventoryView, InventoryUi.Items), "Iron Sword");
        client.SubmitAction(inventoryView, InventoryUi.Select, sword);
        client.SubmitAction(inventoryView, InventoryUi.Equip);
        Expect(inventoryRows.GetChildCount() == 2 && !ContainsLabel(inventoryRows, "Iron Sword"),
            "The native Equip delta did not remove Iron Sword.");
        Expect(inventory.Close(inventoryView), "The native Inventory view did not close.");
        InventoryUiOpenResult hotInventory = inventory.OpenHot(fixture.Backpack);
        Expect(hotInventory.Succeeded &&
            GetNodeOrNull<Control>($"UiView_{hotInventory.View.Value}") is not null,
            "The native Inventory definition did not reopen from cache.");
        runtime.Stop();
    }

    private static UiRowId FindRow(ReadOnlySpan<UiListRow> rows, string name)
    {
        foreach (UiListRow row in rows)
        {
            if (row.Values.Length != 0 && row.Values[0].Text == name) return row.Id;
        }
        throw new InvalidOperationException($"Native inventory row '{name}' was not found.");
    }

    private static T Find<T>(Node root, string name) where T : Node =>
        root.FindChild(name, recursive: true, owned: false) as T ??
        throw new InvalidOperationException($"Native control '{name}' was not found.");

    private static bool ContainsLabel(Node root, string text)
    {
        if (root is Label label && label.Text == text) return true;
        foreach (Node child in root.GetChildren())
        {
            if (ContainsLabel(child, text)) return true;
        }
        return false;
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private readonly record struct Fixture(Entity Alice, Entity Backpack);

    private sealed class NativeUiPort : IUiSemanticPort, IDisposable
    {
        private readonly LocalSemanticLink _link;
        private readonly Dictionary<UiViewInstanceId, Action<UiActionRequest>> _actions = [];

        internal NativeUiPort(LocalSemanticLink link)
        {
            _link = link;
            _link.ActionSubmitted += Forward;
        }

        public void BindActions(UiViewInstanceId instance, Action<UiActionRequest> handler) =>
            _actions.Add(instance, handler);
        public void UnbindActions(UiViewInstanceId instance) => _actions.Remove(instance);

        public UiViewInstanceId OpenView(UiOpenView operation) => _link.OpenView(operation);
        public UiViewInstanceId OpenHotView(UiHotOpenView operation) => _link.OpenHotView(operation);
        public void UpdateView(UiUpdateView operation) => _link.UpdateView(operation);
        public bool CloseView(UiCloseView operation) => _link.CloseView(operation);

        public void Dispose()
        {
            _link.ActionSubmitted -= Forward;
            _actions.Clear();
        }

        private void Forward(UiActionRequest request)
        {
            if (_actions.TryGetValue(request.Instance, out Action<UiActionRequest>? handler)) handler(request);
        }
    }
}
