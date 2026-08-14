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
            GD.Print("S0.8 GODOT ACCEPTANCE PASS");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"S0.8 GODOT ACCEPTANCE FAIL: {exception}");
            GetTree().Quit(1);
        }
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

        var client = new UiClientRuntime(new GodotUiRenderer(this));
        var link = new LocalSemanticLink(client);
        using var port = new NativeUiPort(link);
        using var admission = new AdmissionService(runtime.Game, composition, port);
        AdmissionResult waiting = admission.JoinAccountBacked(
            new ParticipantId(1),
            new TrustedLocalIdentity("alice-godot"));
        Expect(waiting.IsWaitingForCharacter, "Native account-backed admission did not open CharacterSelect.");
        UiViewInstanceId characterView = waiting.CharacterSelectView;
        Control characterRoot = GetNode<Control>($"UiView_{characterView.Value}");
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

    private sealed class NativeUiPort : IAdmissionUiPort, IInventoryUiPort, IDisposable
    {
        private readonly LocalSemanticLink _link;

        internal NativeUiPort(LocalSemanticLink link)
        {
            _link = link;
            _link.ActionSubmitted += Forward;
        }

        public event Action<UiActionRequest>? ActionSubmitted;

        public UiViewInstanceId OpenView(UiOpenView operation) => _link.OpenView(operation);
        public UiViewInstanceId OpenHotView(UiHotOpenView operation) => _link.OpenHotView(operation);
        public void UpdateView(UiUpdateView operation) => _link.UpdateView(operation);
        public bool CloseView(UiCloseView operation) => _link.CloseView(operation);

        public void Dispose()
        {
            _link.ActionSubmitted -= Forward;
            ActionSubmitted = null;
        }

        private void Forward(UiActionRequest request) => ActionSubmitted?.Invoke(request);
    }
}
