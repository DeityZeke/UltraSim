using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.Client.Network;
using UltraSim.ECS;
using UltraSim.Game.Accounting;
using UltraSim.Game.Admission;
using UltraSim.Game.Composition;
using UltraSim.Game.Inventory;
using UltraSim.Game.Persistence;
using UltraSim.Game.Protocol;
using UltraSim.Game.Replication;
using UltraSim.Game.UI;
using UltraSim.Network;
using UltraSim.Server.Persistence;
using UltraSim.Framework.Persistence;
using UltraSim.Server.Runtime;
using UltraSim.UI;

public partial class S08HeadlessAcceptance
{
    /// <summary>
    /// Real authenticated owner-private path: Game inventory authority -> generic Game UI
    /// protocol -> secure Network peer -> production client state -> native Godot controls.
    /// </summary>
    private void RunRemoteInventoryAcceptance()
    {
        string saveRoot = Path.Combine(
            Path.GetTempPath(), $"ultrasim-i08-remote-inventory-{Guid.NewGuid():N}");
        var composition = new UltraSimGameComposition(
            new InventoryLimits(32, 128, 32),
            runtimeProfile: GameRuntimeProfile.Networked);
        using var runtime = new ServerRuntime(
            composition,
            new ServerRuntimeOptions(
                UltraSimGameComposition.CreateWorldOptions(
                    256,
                    new PersistenceIdentityOptions(new PersistentWorldId(0x80_0088)),
                    GameRuntimeProfile.Networked),
                WorldClockOptions.Default));
        runtime.Initialize();
        runtime.Start();
        RemoteInventoryFixture fixture = PopulateRemoteInventory(runtime);

        Expect(ProtocolLoopback.TryCreate(
                NetworkProtocolLimits.Default,
                new DeterministicDatagramLimits(2_048, 1_472),
                default,
                out ProtocolLoopback? created,
                out _,
                UltraSimGameProtocol.Manifest),
            "The remote Inventory protocol loopback could not be established.");
        using ProtocolLoopback loopback = created!;
        var renderer = new GodotUiRenderer(this);
        var client = new UiClientRuntime(renderer);
        var bridge = new RemoteUiClientBridge(loopback.Client, client);
        var server = new RemoteGameApplicationSession(
            loopback.Server,
            runtime.Game,
            composition,
            AuthenticateRemoteInventory,
            new ParticipantId(88),
            applicationSessionEpoch: 8_001);
        InventoryUiController? inventory = null;

        try
        {
            AuthenticateAndEnterRemoteInventory(loopback, bridge, server, client);
            Expect(server.Admission.TryGetSession(
                    new ParticipantId(88), out AdmissionResult admitted) && admitted.IsEntered,
                "Remote Inventory authority did not retain the entered participant.");
            Entity alice = admitted.Entity;
            (Entity backpack, Entity pouch, Entity gridBackpack, Entity foreignBackpack) = ResolveRemoteContainers(
                runtime, fixture);

            inventory = new InventoryUiController(runtime.Game, server.Ui);
            Expect(!inventory.OpenOwned(foreignBackpack, alice).Succeeded,
                "Alice opened another actor's private inventory.");
            Expect(!inventory.OpenOwned(backpack, alice, foreignBackpack).Succeeded,
                "Alice admitted another actor's container as a transfer destination.");
            Expect(client.OpenViewCount == 0,
                "A rejected foreign Inventory open disclosed a client view.");

            InventoryUiOpenResult opened = inventory.OpenOwned(backpack, alice, pouch);
            Expect(opened.Succeeded, "The authorized remote Backpack did not open.");
            DriveRemote(loopback, bridge, server, () => client.OpenViewCount == 1);
            Control root = GetNode<Control>($"UiView_{opened.View.Value}");
            VBoxContainer rows = Find<VBoxContainer>(root, "Element_items");
            Expect(rows.GetChildCount() == 2 && ContainsLabel(rows, "Bread") &&
                ContainsLabel(rows, "Iron Sword") && !ContainsLabel(root, "Apple"),
                "The remote native Backpack projection was incomplete or disclosed foreign state.");

            UiRowId bread = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Bread");
            ClickRemoteRow(root, "Element_items", bread);
            DriveRemote(loopback, bridge, server, () =>
                client.GetActionState(opened.View, InventoryUi.MoveTo).Enabled);
            UiRowId pouchRow = client.GetRows(
                opened.View, InventoryUi.TransferDestinations)[0].Id;
            ClickRemoteRow(root, "Element_transfer-destinations", pouchRow);
            DriveRemote(loopback, bridge, server, () =>
                client.GetRows(opened.View, InventoryUi.Items).Length == 1);
            Expect(ReferenceEquals(root, GetNode<Control>($"UiView_{opened.View.Value}")),
                "The granular remote Move rebuilt the native Inventory root.");
            Expect(ReadRemoteItem(runtime, fixture.Bread).Parent == pouch,
                "The native remote Move did not commit authoritative Inventory truth.");
            Expect(inventory.Close(opened.View), "The remote Backpack did not close.");
            DriveRemote(loopback, bridge, server, () => client.OpenViewCount == 0);

            InventoryUiOpenResult pouchView = inventory.OpenOwned(pouch, alice, backpack);
            Expect(pouchView.Succeeded, "The authorized remote Pouch did not open.");
            DriveRemote(loopback, bridge, server, () => client.OpenViewCount == 1);
            root = GetNode<Control>($"UiView_{pouchView.View.Value}");
            bread = FindRow(client.GetRows(pouchView.View, InventoryUi.Items), "Bread");
            ClickRemoteRow(root, "Element_items", bread);
            DriveRemote(loopback, bridge, server, () =>
                client.GetActionState(pouchView.View, InventoryUi.Split).Enabled);
            PressRemoteButton(root, "Element_split");
            DriveRemote(loopback, bridge, server, () =>
                client.GetRows(pouchView.View, InventoryUi.Items).Length == 2);
            Expect(CountRows(client.GetRows(pouchView.View, InventoryUi.Items), "Bread") == 2,
                "The native remote Split did not publish both authoritative stack rows.");
            Expect(inventory.Close(pouchView.View), "The remote Pouch did not close.");
            DriveRemote(loopback, bridge, server, () => client.OpenViewCount == 0);

            opened = inventory.OpenOwned(backpack, alice, pouch);
            DriveRemote(loopback, bridge, server, () => client.OpenViewCount == 1);
            root = GetNode<Control>($"UiView_{opened.View.Value}");
            UiRowId sword = FindRow(client.GetRows(opened.View, InventoryUi.Items), "Iron Sword");
            ClickRemoteRow(root, "Element_items", sword);
            DriveRemote(loopback, bridge, server, () =>
                client.GetActionState(opened.View, InventoryUi.Equip).Enabled);
            PressRemoteButton(root, "Element_equip");
            DriveRemote(loopback, bridge, server, () =>
                ReadRowState(client, opened.View, sword) == "Main Hand");
            Expect(TryReadRemoteEquipped(runtime, fixture.Alice, fixture.Sword),
                "The native remote Equip did not establish authoritative equipment truth.");

            ClickRemoteRow(root, "Element_items", sword);
            DriveRemote(loopback, bridge, server, () =>
                client.GetActionState(opened.View, InventoryUi.Unequip).Enabled);
            PressRemoteButton(root, "Element_unequip");
            DriveRemote(loopback, bridge, server, () =>
                ReadRowState(client, opened.View, sword) == "Container");
            Expect(!TryReadRemoteEquipped(runtime, fixture.Alice, fixture.Sword),
                "The native remote Unequip retained authoritative equipment truth.");

            ulong rejectedBefore = server.Ui.GetSnapshot().RejectedActions;
            QueueRawRemoteAction(
                loopback.Client,
                8_001,
                new UiActionRequest(
                    opened.View,
                    InventoryUi.Definition.Handle,
                    InventoryUi.Unequip,
                    clientSequence: 1));
            QueueRawRemoteAction(
                loopback.Client,
                8_001,
                new UiActionRequest(
                    opened.View,
                    InventoryUi.Definition.Handle,
                    InventoryUi.Unequip,
                    clientSequence: 1));
            QueueRawRemoteAction(
                loopback.Client,
                8_001,
                new UiActionRequest(
                    new UiViewInstanceId(99_999),
                    InventoryUi.Definition.Handle,
                    InventoryUi.Split,
                    clientSequence: 999));
            DriveRemote(loopback, bridge, server, () =>
                server.Ui.GetSnapshot().RejectedActions >= rejectedBefore + 3);
            Expect(ReadRowState(client, opened.View, sword) == "Container" &&
                !TryReadRemoteEquipped(runtime, fixture.Alice, fixture.Sword),
                "Stale, duplicate, or foreign remote intent changed authoritative Inventory state.");

            Expect(inventory.Close(opened.View), "The remote Backpack did not close before Grid placement.");
            DriveRemote(loopback, bridge, server, () => client.OpenViewCount == 0);
            InventoryUiOpenResult gridView = inventory.OpenOwned(gridBackpack, alice);
            Expect(gridView.Succeeded, "The authorized remote Grid Backpack did not open.");
            DriveRemote(loopback, bridge, server, () => client.OpenViewCount == 1);
            root = GetNode<Control>($"UiView_{gridView.View.Value}");
            Expect(client.GetRows(gridView.View, InventoryUi.GridCells).Length == 25,
                "The remote Grid view did not project its 5x5 placement cells.");
            UiRowId gridApple = FindRow(client.GetRows(gridView.View, InventoryUi.Items), "Apple");
            ClickRemoteRow(root, "Element_items", gridApple);
            DriveRemote(loopback, bridge, server, () =>
                client.GetActionState(gridView.View, InventoryUi.PlaceAt).Enabled);
            ClickRemoteRow(root, "Element_grid-cells", new UiRowId(18));
            DriveRemote(loopback, bridge, server, () =>
                ReadRowPlacement(client, gridView.View, gridApple) == "Grid (2,3) 1×1");
            Expect(ReadRemotePlacement(runtime, fixture.GridApple) ==
                InventoryPlacement.Grid(2, 3, 1, 1),
                "The native remote Grid placement did not commit authoritative truth.");
            ulong acceptedBefore = server.Ui.GetSnapshot().AcceptedActions;
            QueueRawRemoteAction(
                loopback.Client,
                8_001,
                new UiActionRequest(
                    gridView.View,
                    InventoryUi.Definition.Handle,
                    InventoryUi.PlaceAt,
                    new UiRowId(999),
                    clientSequence: 1_000));
            DriveRemote(loopback, bridge, server, () =>
                server.Ui.GetSnapshot().AcceptedActions > acceptedBefore);
            Expect(ReadRemotePlacement(runtime, fixture.GridApple) ==
                InventoryPlacement.Grid(2, 3, 1, 1),
                "A hostile remote Grid cell intent changed authoritative placement.");

            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                    (world, generationId, game) => game.Inventory.SetEffectiveCapacity(
                        world,
                        ResolveRemoteInventory(world, fixture.GridBackpack),
                        new InventoryInstanceCapacity(24, 6, 5)).Accepted,
                    out bool upgraded) && upgraded,
                "The authoritative remote Grid capacity upgrade was rejected.");
            Expect(inventory.Refresh(gridView.View, "Backpack upgraded."),
                "The remote Grid view did not accept its authoritative capacity refresh.");
            DriveRemote(loopback, bridge, server, () =>
                client.GetRows(gridView.View, InventoryUi.GridCells).Length == 30 &&
                client.GetValue(gridView.View, InventoryUi.Status).Text == "Backpack upgraded.");
            Expect(Find<VBoxContainer>(root, "Element_grid-cells").GetChildCount() == 30,
                "The native remote Grid did not render its upgraded 6x5 capacity.");

            var persistence = new GameRuntimePersistence(
                runtime.Game,
                composition,
                RemoteInventoryRestoreOptions(),
                new SaveSetCoordinator(saveRoot));
            GameSaveResult saved = persistence.TrySave(
                new SaveSetContext(1, 1_700_008_008_001), static capture => capture());
            Expect(saved.Succeeded, "The remote Inventory generation could not save.");
            Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
                    (world, generationId, game) =>
                    {
                        Entity grid = ResolveRemoteInventory(world, fixture.GridBackpack);
                        return game.Inventory.Reposition(
                                world,
                                ResolveRemoteInventory(world, fixture.GridApple),
                                grid,
                                InventoryPlacement.Grid(4, 4, 1, 1)).Accepted &&
                            game.Inventory.SetEffectiveCapacity(
                                world, grid, new InventoryInstanceCapacity(30, 8, 8)).Accepted;
                    },
                    out bool diverged) && diverged,
                "The remote Inventory save fixture could not diverge.");

            GameLoadResult loaded = persistence.TryLoad();
            Expect(loaded.Succeeded, "The remote Inventory generation could not reload.");
            DriveRemote(loopback, bridge, server, () =>
                client.GetValue(gridView.View, InventoryUi.Status).Text ==
                "Inventory resynchronized.");
            Expect(ReadRowPlacement(client, gridView.View, gridApple) == "Grid (2,3) 1×1" &&
                client.GetRows(gridView.View, InventoryUi.GridCells).Length == 30 &&
                ReadRemotePlacement(runtime, fixture.GridApple) ==
                    InventoryPlacement.Grid(2, 3, 1, 1),
                "Generation replacement did not atomically restore capacity and Grid truth.");

            Expect(inventory.Close(gridView.View),
                "The resynchronized remote Inventory did not close.");
            DriveRemote(loopback, bridge, server, () => client.OpenViewCount == 0);
            inventory.Dispose();
            inventory = null;

            RemoteUiClientSnapshot firstClient = bridge.GetSnapshot();
            RemoteUiPortSnapshot firstServerUi = server.Ui.GetSnapshot();
            server.Disconnect();
            DriveRemote(loopback, bridge, server, () =>
                bridge.GetSnapshot().State == RemoteClientApplicationState.Detached);
            server.Dispose();
            bridge.Dispose();

            using var reconnectedRenderer = new GodotUiRenderer(this);
            using var reconnectedClient = new UiClientRuntime(reconnectedRenderer);
            using var reconnectedBridge = new RemoteUiClientBridge(
                loopback.Client, reconnectedClient);
            using var reconnectedServer = new RemoteGameApplicationSession(
                loopback.Server,
                runtime.Game,
                composition,
                AuthenticateRemoteInventory,
                new ParticipantId(89),
                applicationSessionEpoch: 8_002);
            AuthenticateAndEnterRemoteInventory(
                loopback, reconnectedBridge, reconnectedServer, reconnectedClient);
            Expect(reconnectedServer.Admission.TryGetSession(
                    new ParticipantId(89), out AdmissionResult reentered) && reentered.IsEntered,
                "The reconnected Inventory participant did not re-enter authority.");
            (backpack, pouch, gridBackpack, foreignBackpack) = ResolveRemoteContainers(runtime, fixture);
            using var reconnectedInventory = new InventoryUiController(
                runtime.Game, reconnectedServer.Ui);
            InventoryUiOpenResult resynced = reconnectedInventory.OpenOwned(
                pouch, reentered.Entity, backpack);
            Expect(resynced.Succeeded,
                "Reconnect could not authorize the restored owner-private Pouch.");
            DriveRemote(loopback, reconnectedBridge, reconnectedServer, () =>
                reconnectedClient.OpenViewCount == 1);
            Control reconnectedRoot = GetNode<Control>($"UiView_{resynced.View.Value}");
            Expect(CountRows(
                    reconnectedClient.GetRows(resynced.View, InventoryUi.Items), "Bread") == 2 &&
                !ContainsLabel(reconnectedRoot, "Apple"),
                "Reconnect did not resynchronize exact private Inventory truth.");

            Expect(reconnectedInventory.Close(resynced.View),
                "The reconnected Inventory did not close.");
            DriveRemote(loopback, reconnectedBridge, reconnectedServer, () =>
                reconnectedClient.OpenViewCount == 0);
            InventoryUiOpenResult reconnectedGrid = reconnectedInventory.OpenOwned(
                gridBackpack, reentered.Entity);
            Expect(reconnectedGrid.Succeeded,
                "Reconnect could not authorize the restored Grid Backpack.");
            DriveRemote(loopback, reconnectedBridge, reconnectedServer, () =>
                reconnectedClient.OpenViewCount == 1);
            Expect(reconnectedClient.GetRows(
                    reconnectedGrid.View, InventoryUi.GridCells).Length == 30,
                "Reconnect did not resynchronize the restored 6x5 Grid capacity.");
            Expect(reconnectedInventory.Close(reconnectedGrid.View),
                "The reconnected Grid Inventory did not close.");
            DriveRemote(loopback, reconnectedBridge, reconnectedServer, () =>
                reconnectedClient.OpenViewCount == 0);
            RemoteUiClientSnapshot secondClient = reconnectedBridge.GetSnapshot();
            RemoteUiPortSnapshot secondServerUi = reconnectedServer.Ui.GetSnapshot();
            ProtocolLoopbackSnapshot transport = loopback.GetSnapshot();
            GD.Print(
                $"I0.11 REMOTE INVENTORY METRICS " +
                $"operations={firstClient.OperationsApplied + secondClient.OperationsApplied} " +
                $"actions={firstClient.ActionsQueued + secondClient.ActionsQueued} " +
                $"accepted_actions={firstServerUi.AcceptedActions + secondServerUi.AcceptedActions} " +
                $"rejected_actions={firstServerUi.RejectedActions + secondServerUi.RejectedActions} " +
                $"transport_steps={transport.Step} datagrams={transport.Datagram.Submitted} " +
                $"rtt_steps={transport.Client.SmoothedRoundTripSteps} " +
                $"client_network_bytes={transport.Client.ReservedBytes} " +
                $"server_network_bytes={transport.Server.ReservedBytes} " +
                $"link_bytes={transport.Datagram.ReservedBytes}");
            reconnectedServer.Disconnect();
            DriveRemote(loopback, reconnectedBridge, reconnectedServer, () =>
                reconnectedBridge.GetSnapshot().State == RemoteClientApplicationState.Detached);
        }
        finally
        {
            if (inventory is not null)
            {
                try { inventory.Dispose(); }
                catch (Exception) { }
            }
            try
            {
                if (server.GetSnapshot().State != RemoteServerApplicationState.Detached)
                    server.Disconnect();
            }
            catch (ObjectDisposedException) { }
            try { server.Dispose(); }
            catch (ObjectDisposedException) { }
            try { bridge.Dispose(); }
            catch (ObjectDisposedException) { }
            client.Dispose();
            renderer.Dispose();
            runtime.Stop();
            if (Directory.Exists(saveRoot)) Directory.Delete(saveRoot, recursive: true);
        }
    }

    private static RemoteInventoryFixture PopulateRemoteInventory(ServerRuntime runtime)
    {
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, RemoteInventoryFixture>(
            (world, generationId, game) =>
            {
                Expect(world.TryInstantiate(game.Content.Character, out Entity alice),
                    "The remote Inventory fixture could not create Alice.");
                Expect(game.Inventory.TryCreateOwnedContainer(
                    world, game.Content.Backpack, alice, 8, out Entity backpack),
                    "The remote Inventory fixture could not create Backpack.");
                Expect(game.Inventory.TryCreateOwnedContainer(
                    world, game.Content.Backpack, alice, 8, out Entity pouch),
                    "The remote Inventory fixture could not create Pouch.");
                Expect(game.Inventory.TryCreateOwnedContainer(
                    world, game.Content.GridBackpack, alice, 20, out Entity gridBackpack),
                    "The remote Inventory fixture could not create Grid Backpack.");
                Expect(game.Inventory.TryCreateItem(
                    world, backpack, game.Content.Bread, 5, out Entity bread),
                    "The remote Inventory fixture could not create Bread.");
                Expect(game.Inventory.TryCreateItem(
                    world, backpack, game.Content.IronSword, 1, out Entity sword),
                    "The remote Inventory fixture could not create Iron Sword.");
                Expect(game.Inventory.TryCreateItem(
                    world,
                    gridBackpack,
                    game.Content.Apple,
                    3,
                    InventoryPlacement.Grid(0, 0, 1, 1),
                    out Entity gridApple),
                    "The remote Inventory fixture could not create Grid Apple.");
                Expect(world.TryInstantiate(game.Content.Character, out Entity bob),
                    "The remote Inventory fixture could not create Bob.");
                Expect(game.Inventory.TryCreateOwnedContainer(
                    world, game.Content.Backpack, bob, 8, out Entity foreignBackpack),
                    "The remote Inventory fixture could not create Bob's Backpack.");
                Expect(game.Inventory.TryCreateItem(
                    world, foreignBackpack, game.Content.Apple, 3, out Entity foreignItem),
                    "The remote Inventory fixture could not create Bob's private item.");

                PersistentEntityId aliceId = world.GetOrAssignPersistentId(alice);
                PersistentEntityId backpackId = world.GetOrAssignPersistentId(backpack);
                PersistentEntityId pouchId = world.GetOrAssignPersistentId(pouch);
                PersistentEntityId gridBackpackId = world.GetOrAssignPersistentId(gridBackpack);
                PersistentEntityId breadId = world.GetOrAssignPersistentId(bread);
                PersistentEntityId swordId = world.GetOrAssignPersistentId(sword);
                PersistentEntityId gridAppleId = world.GetOrAssignPersistentId(gridApple);
                _ = world.GetOrAssignPersistentId(bob);
                PersistentEntityId foreignBackpackId = world.GetOrAssignPersistentId(foreignBackpack);
                _ = world.GetOrAssignPersistentId(foreignItem);
                Expect(world.TryGetPrefabProvenance(alice, out PrefabKey prefab),
                    "The remote Inventory Alice has no prefab provenance.");
                var account = new AccountId(88);
                game.Accounting.AddAccount(new AccountRecord(account));
                game.Accounting.BindTrustedIdentity(
                    new TrustedIdentity("test", "alice-inventory"), account);
                game.Accounting.AddCharacter(new CharacterRecord(
                    new CharacterId(88),
                    account,
                    0,
                    "Alice",
                    prefab,
                    new PersistentEntityKey(world.PersistentWorldId, aliceId)));
                return new RemoteInventoryFixture(
                    aliceId,
                    backpackId,
                    pouchId,
                    gridBackpackId,
                    breadId,
                    swordId,
                    gridAppleId,
                    foreignBackpackId);
            }, out RemoteInventoryFixture result),
            "The remote Inventory fixture did not populate authority.");
        return result;
    }

    private static void AuthenticateAndEnterRemoteInventory(
        ProtocolLoopback loopback,
        RemoteUiClientBridge bridge,
        RemoteGameApplicationSession server,
        UiClientRuntime client)
    {
        Expect(bridge.TryBeginAuthentication(1, "test", "alice-token"),
            "The remote Inventory authentication request was not queued.");
        DriveRemote(loopback, bridge, server, () =>
            bridge.GetSnapshot().State == RemoteClientApplicationState.Active &&
            client.OpenViewCount == 1);
        Expect(server.Admission.TryGetSession(
                server.GetSnapshot().Participant,
                out AdmissionResult waiting) && waiting.IsWaitingForCharacter,
            "The remote Inventory session did not reach CharacterSelect.");
        UiListRow character = client.GetRows(
            waiting.CharacterSelectView, CharacterSelectUi.Characters)[0];
        Control root = server.GetSnapshot().Participant.IsValid
            ? client.OpenViewCount == 1
                ? FindRemoteViewRoot(waiting.CharacterSelectView)
                : throw new InvalidOperationException("CharacterSelect was not open.")
            : throw new InvalidOperationException("The authenticated participant was invalid.");
        ClickRemoteRow(root, "Element_characters", character.Id);
        DriveRemote(loopback, bridge, server, () =>
            server.Admission.TryGetSession(
                server.GetSnapshot().Participant, out AdmissionResult entered) &&
            entered.IsEntered && client.OpenViewCount == 0);
    }

    private static Control FindRemoteViewRoot(UiViewInstanceId view)
    {
        SceneTree tree = Engine.GetMainLoop() as SceneTree ??
            throw new InvalidOperationException("The Godot SceneTree is unavailable.");
        return tree.CurrentScene?.GetNodeOrNull<Control>($"UiView_{view.Value}") ??
            throw new InvalidOperationException($"Remote UI view {view.Value} has no native root.");
    }

    private static void DriveRemote(
        ProtocolLoopback loopback,
        RemoteUiClientBridge bridge,
        RemoteGameApplicationSession server,
        Func<bool> complete,
        int maximumSteps = 50_000)
    {
        for (int step = 0; step < maximumSteps; step++)
        {
            if (complete()) return;
            loopback.Pump();
            bridge.ProcessReceived();
            server.ProcessReceived();
        }
        throw new InvalidOperationException(
            "The remote Inventory application workflow did not converge.");
    }

    private static void ClickRemoteRow(Node root, string listName, UiRowId row)
    {
        VBoxContainer list = Find<VBoxContainer>(root, listName);
        Control control = list.GetNodeOrNull<Control>($"Row_{row.Value}") ??
            throw new InvalidOperationException(
                $"Native list '{listName}' does not contain row {row.Value}.");
        var click = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        };
        control.EmitSignal(Control.SignalName.GuiInput, click);
    }

    private static void PressRemoteButton(Node root, string buttonName)
    {
        Button button = Find<Button>(root, buttonName);
        Expect(!button.Disabled, $"Native button '{buttonName}' was disabled.");
        button.EmitSignal(BaseButton.SignalName.Pressed);
    }

    private static int CountRows(ReadOnlySpan<UiListRow> rows, string name)
    {
        int count = 0;
        foreach (UiListRow row in rows)
        {
            if (row.Values.Length != 0 && row.Values[0].Text == name) count++;
        }
        return count;
    }

    private static string ReadRowState(
        UiClientRuntime client,
        UiViewInstanceId view,
        UiRowId row)
    {
        foreach (UiListRow candidate in client.GetRows(view, InventoryUi.Items))
        {
            if (candidate.Id == row && candidate.Values.Length >= 3)
                return candidate.Values[2].Text ?? string.Empty;
        }
        return string.Empty;
    }

    private static string ReadRowPlacement(
        UiClientRuntime client,
        UiViewInstanceId view,
        UiRowId row)
    {
        foreach (UiListRow candidate in client.GetRows(view, InventoryUi.Items))
        {
            if (candidate.Id == row && candidate.Values.Length >= 4)
                return candidate.Values[3].Text ?? string.Empty;
        }
        return string.Empty;
    }

    private static void QueueRawRemoteAction(
        NetworkProtocolPeer client,
        ulong applicationSessionEpoch,
        UiActionRequest request)
    {
        byte[] semantic = new byte[UltraSimGameProtocol.DefaultUiWireLimits.MaximumOperationBytes];
        Expect(UiSemanticWireCodec.TryWriteAction(
                semantic,
                request,
                UltraSimGameProtocol.DefaultUiWireLimits,
                out int semanticBytes),
            "The hostile remote Inventory action did not encode.");
        byte[] message = new byte[UltraSimGameProtocol.MaximumMessageBytes];
        Expect(UltraSimGameProtocol.TryWriteUiAction(
                message,
                applicationSessionEpoch,
                semantic.AsSpan(0, semanticBytes),
                out int messageBytes),
            "The hostile remote Inventory action envelope did not encode.");
        Expect(client.TryQueue(
                RemoteUiActionHeaderCodec.Descriptor.Id,
                message.AsSpan(0, messageBytes)) == NetworkQueueStatus.Accepted,
            "The hostile remote Inventory action did not enter Network.");
    }

    private static (Entity Backpack, Entity Pouch, Entity GridBackpack, Entity ForeignBackpack)
        ResolveRemoteContainers(ServerRuntime runtime, RemoteInventoryFixture fixture)
    {
        Expect(runtime.Game.TryUseActiveGeneration(
            (world, generationId) => (
                ResolveRemoteInventory(world, fixture.Backpack),
                ResolveRemoteInventory(world, fixture.Pouch),
                ResolveRemoteInventory(world, fixture.GridBackpack),
                ResolveRemoteInventory(world, fixture.ForeignBackpack)),
            out (Entity Backpack, Entity Pouch, Entity GridBackpack, Entity ForeignBackpack) result),
            "The remote Inventory containers did not resolve.");
        return result;
    }

    private static InventoryItem ReadRemoteItem(
        ServerRuntime runtime,
        PersistentEntityId item)
    {
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, InventoryItem>(
            (world, generationId, game) =>
            {
                Entity entity = ResolveRemoteInventory(world, item);
                Expect(game.Inventory.TryReadItem(world, entity, out InventoryItem value),
                    "The authoritative remote Inventory item was unavailable.");
                return value;
            }, out InventoryItem result),
            "The authoritative remote Inventory item read did not run.");
        return result;
    }

    private static InventoryPlacement ReadRemotePlacement(
        ServerRuntime runtime,
        PersistentEntityId item)
    {
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, InventoryPlacement>(
            (world, generationId, game) =>
            {
                Entity entity = ResolveRemoteInventory(world, item);
                Expect(game.Inventory.TryProjectItem(world, entity, out InventoryItemView view),
                    "The authoritative remote Grid item was unavailable.");
                return view.Placement;
            }, out InventoryPlacement result),
            "The authoritative remote Grid placement read did not run.");
        return result;
    }

    private static bool TryReadRemoteEquipped(
        ServerRuntime runtime,
        PersistentEntityId owner,
        PersistentEntityId item)
    {
        Expect(runtime.Game.TryUseActiveGeneration<IGameGenerationStateProvider, bool>(
            (world, generationId, game) =>
            {
                Entity ownerEntity = ResolveRemoteInventory(world, owner);
                Entity itemEntity = ResolveRemoteInventory(world, item);
                return game.Inventory.Index.TryGetEquipped(
                    ownerEntity, InventoryEquipSlot.MainHand, out Entity equipped) &&
                    equipped == itemEntity;
            }, out bool result),
            "The authoritative remote equipment read did not run.");
        return result;
    }

    private static Entity ResolveRemoteInventory(EcsWorld world, PersistentEntityId id) =>
        world.TryResolvePersistentEntity(id, out Entity entity)
            ? entity
            : throw new InvalidOperationException(
                $"Remote Inventory entity {id.Value} did not resolve.");

    private static bool AuthenticateRemoteInventory(
        string provider,
        string credential,
        out TrustedIdentity identity)
    {
        if (provider == "test" && credential == "alice-token")
        {
            identity = new TrustedIdentity("test", "alice-inventory");
            return true;
        }
        identity = default;
        return false;
    }

    private static PersistenceRestoreOptions RemoteInventoryRestoreOptions() => new(
        new PersistenceLimits(
            maximumArtifactBytes: 16 * 1024 * 1024,
            maximumEntities: 100_000,
            maximumShapes: 64,
            maximumComponents: 64,
            maximumSections: 256,
            maximumComponentPayloadBytes: 8 * 1024 * 1024,
            maximumTemporaryBytes: 64 * 1024 * 1024),
        ChunkLayoutOptions.KiB16);

    private readonly record struct RemoteInventoryFixture(
        PersistentEntityId Alice,
        PersistentEntityId Backpack,
        PersistentEntityId Pouch,
        PersistentEntityId GridBackpack,
        PersistentEntityId Bread,
        PersistentEntityId Sword,
        PersistentEntityId GridApple,
        PersistentEntityId ForeignBackpack);
}
