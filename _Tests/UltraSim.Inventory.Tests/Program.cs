using UltraSim.Inventory;

TestRunner.Run();

internal static class TestRunner
{
    public static void Run()
    {
        Run(nameof(InventoryRefs_ValidateOwnerAndRequesterShape), InventoryRefs_ValidateOwnerAndRequesterShape);
        Run(nameof(InventoryPlacementDescriptor_ValidatesModeSpecificShapes), InventoryPlacementDescriptor_ValidatesModeSpecificShapes);
        Run(nameof(InventoryPermissionDescriptor_ValidatesPublicAndScriptedRules), InventoryPermissionDescriptor_ValidatesPublicAndScriptedRules);
        Run(nameof(InventoryContainerSnapshot_ValidatesNeutralListContainer), InventoryContainerSnapshot_ValidatesNeutralListContainer);
        Run(nameof(InventoryContainerSnapshot_RejectsViewerPlacementMismatchAndDuplicateItems), InventoryContainerSnapshot_RejectsViewerPlacementMismatchAndDuplicateItems);
        Run(nameof(InventoryItemSnapshot_ValidatesStackAndFlags), InventoryItemSnapshot_ValidatesStackAndFlags);
        Run(nameof(InventoryMutationIntent_ValidatesMoveSplitMergeAndSwapShapes), InventoryMutationIntent_ValidatesMoveSplitMergeAndSwapShapes);
        Run(nameof(InventoryMutationIntent_ValidatesNpcAndTransactionShapes), InventoryMutationIntent_ValidatesNpcAndTransactionShapes);
        Run(nameof(InventoryMutationResult_ValidatesCorrelationFailureAndAffectedContainers), InventoryMutationResult_ValidatesCorrelationFailureAndAffectedContainers);
        Run(nameof(InventoryContainerViewerRegistry_FiltersDeltasByAccessAndScope), InventoryContainerViewerRegistry_FiltersDeltasByAccessAndScope);
        Run(nameof(InventoryContainerDelta_ValidatesItemAndPlacementShapes), InventoryContainerDelta_ValidatesItemAndPlacementShapes);
        Run(nameof(InventoryTransactionSnapshot_ValidatesSuspendedItemLifecycle), InventoryTransactionSnapshot_ValidatesSuspendedItemLifecycle);
        Run(nameof(InventoryListProjection_ProjectsListAndGridContainersWithoutOwningTruth), InventoryListProjection_ProjectsListAndGridContainersWithoutOwningTruth);

        Console.WriteLine("All inventory kernel contract tests passed.");
    }

    private static void InventoryRefs_ValidateOwnerAndRequesterShape()
    {
        var owner = new InventoryOwnerRef(InventoryOwnerKind.Entity, "entity:42");
        Assert.Valid(owner.Validate());

        var requester = new InventoryRequesterRef(InventoryRequesterKind.EntityAi, "entity:99");
        Assert.Valid(requester.Validate());

        Assert.InvalidContains((owner with { Kind = (InventoryOwnerKind)255 }).Validate(), "owner kind");
        Assert.InvalidContains((requester with { Id = "../bad" }).Validate(), "traversal");
    }

    private static void InventoryPlacementDescriptor_ValidatesModeSpecificShapes()
    {
        Assert.Valid(InventoryPlacementDescriptor.None.ValidateForContainerMode(InventoryPlacementMode.None));
        Assert.Valid(InventoryPlacementDescriptor.List(3).ValidateForContainerMode(InventoryPlacementMode.List));
        Assert.Valid(InventoryPlacementDescriptor.Grid(1, 2, 2, 3, rotated: true).ValidateForContainerMode(InventoryPlacementMode.Grid));
        Assert.Valid(InventoryPlacementDescriptor.Freeform(40, -12, zLayer: 5).ValidateForContainerMode(InventoryPlacementMode.Freeform));
        Assert.Valid(InventoryPlacementDescriptor.Slot("equipment/main_hand").ValidateForContainerMode(InventoryPlacementMode.Slot));

        Assert.InvalidContains(InventoryPlacementDescriptor.Grid(0, 0, 2, 2).ValidateForContainerMode(InventoryPlacementMode.List), "not compatible");
        Assert.InvalidContains(InventoryPlacementDescriptor.List(-1).Validate(), "non-negative");
        Assert.InvalidContains(InventoryPlacementDescriptor.Grid(0, 0, 0, 1).Validate(), "positive");
        Assert.InvalidContains(new InventoryPlacementDescriptor(InventoryPlacementMode.None, X: 1).Validate(), "None placement");
    }

    private static void InventoryPermissionDescriptor_ValidatesPublicAndScriptedRules()
    {
        Assert.Valid(InventoryPermissionDescriptor.PublicReadTake.Validate());

        var scripted = new InventoryPermissionDescriptor(
            InventoryPermissionKind.ScriptedRule,
            InventoryAccessFlags.Read | InventoryAccessFlags.Move,
            "house/rental/active");
        Assert.Valid(scripted.Validate());

        var nonScriptedWithRule = new InventoryPermissionDescriptor(
            InventoryPermissionKind.Owner,
            InventoryAccessFlags.Read,
            "should/not/be/here");
        Assert.InvalidContains(nonScriptedWithRule.Validate(), "Only scripted");

        var scriptedMissingRule = scripted with { RuleId = null };
        Assert.InvalidContains(scriptedMissingRule.Validate(), "RuleId");

        var noAccess = scripted with { Access = InventoryAccessFlags.None };
        Assert.InvalidContains(noAccess.Validate(), "cannot be None");
    }

    private static void InventoryContainerSnapshot_ValidatesNeutralListContainer()
    {
        InventoryContainerSnapshot container = CreateListContainer();
        Assert.Valid(container.Validate());
    }

    private static void InventoryContainerSnapshot_RejectsViewerPlacementMismatchAndDuplicateItems()
    {
        InventoryContainerSnapshot list = CreateListContainer();

        InventoryContainerSnapshot gridItemInList = list with
        {
            Items =
            [
                list.Items[0] with { Placement = InventoryPlacementDescriptor.Grid(0, 0, 1, 1) }
            ]
        };
        Assert.InvalidContains(gridItemInList.Validate(), "not compatible");

        InventoryContainerSnapshot duplicate = list with
        {
            Items =
            [
                list.Items[0],
                list.Items[0] with { Quantity = 1 }
            ]
        };
        Assert.InvalidContains(duplicate.Validate(), "duplicate item");

        InventoryContainerSnapshot noPermissions = list with { Permissions = [] };
        Assert.InvalidContains(noPermissions.Validate(), "at least one permission");
    }

    private static void InventoryItemSnapshot_ValidatesStackAndFlags()
    {
        var item = new InventoryItemSnapshot(
            "item:apple_stack",
            "item.apple",
            Quantity: 8,
            MaxStackQuantity: 20,
            InventoryPlacementDescriptor.List(0));
        Assert.Valid(item.Validate(InventoryPlacementMode.List));

        Assert.InvalidContains((item with { Quantity = 0 }).Validate(InventoryPlacementMode.List), "positive");
        Assert.InvalidContains((item with { Quantity = 21 }).Validate(InventoryPlacementMode.List), "cannot exceed");
        Assert.InvalidContains((item with { Flags = (InventoryItemFlags)(1u << 31) }).Validate(InventoryPlacementMode.List), "unknown");
    }

    private static void InventoryMutationIntent_ValidatesMoveSplitMergeAndSwapShapes()
    {
        InventoryMutationIntent move = CreateMoveIntent();
        Assert.Valid(move.Validate());

        InventoryMutationIntent split = move with
        {
            Kind = InventoryMutationKind.SplitStack,
            CorrelationId = "corr/split/1",
            Quantity = 3
        };
        Assert.Valid(split.Validate());

        InventoryMutationIntent merge = move with
        {
            Kind = InventoryMutationKind.MergeStack,
            CorrelationId = "corr/merge/1",
            DestinationItemInstanceId = "item:apple_stack/2",
            ProposedPlacement = null
        };
        Assert.Valid(merge.Validate());

        InventoryMutationIntent swap = move with
        {
            Kind = InventoryMutationKind.SwapItems,
            CorrelationId = "corr/swap/1",
            DestinationItemInstanceId = "item:shield/1",
            ProposedPlacement = null
        };
        Assert.Valid(swap.Validate());

        Assert.InvalidContains((move with { DestinationContainerId = null }).Validate(), "DestinationContainerId");
        Assert.InvalidContains((move with { Quantity = 0 }).Validate(), "quantity");
        Assert.InvalidContains((merge with { DestinationItemInstanceId = null }).Validate(), "DestinationItemInstanceId");
        Assert.InvalidContains((move with
        {
            ProposedPlacement = InventoryPlacementDescriptor.Grid(-1, 0, 1, 1)
        }).Validate(), "non-negative");
    }

    private static void InventoryMutationIntent_ValidatesNpcAndTransactionShapes()
    {
        var npcMove = CreateMoveIntent() with
        {
            CorrelationId = "corr/npc/loot/1",
            Requester = new InventoryRequesterRef(InventoryRequesterKind.EntityAi, "entity:npc/7"),
            ClientSequence = -1,
            SourceContainerId = "container:corpse/1",
            DestinationContainerId = "container:npc_pack/7"
        };
        Assert.Valid(npcMove.Validate());

        var open = new InventoryMutationIntent(
            "corr/open/1",
            new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:alpha"),
            ClientSequence: 11,
            InventoryMutationKind.OpenContainer,
            SourceContainerId: "container:barrel/1",
            DestinationContainerId: null,
            SourceItemInstanceId: null,
            DestinationItemInstanceId: null,
            Quantity: 0,
            ProposedPlacement: null);
        Assert.Valid(open.Validate());

        var beginTrade = new InventoryMutationIntent(
            "corr/trade/begin/1",
            new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:alpha"),
            ClientSequence: 12,
            InventoryMutationKind.BeginTransaction,
            SourceContainerId: "container:trade_offer/a",
            DestinationContainerId: "container:trade_offer/b",
            SourceItemInstanceId: null,
            DestinationItemInstanceId: null,
            Quantity: 0,
            ProposedPlacement: null,
            RuleContextId: "trade:alpha_beta/1");
        Assert.Valid(beginTrade.Validate());

        Assert.InvalidContains((beginTrade with { RuleContextId = null }).Validate(), "RuleContextId");
        Assert.InvalidContains((beginTrade with { Quantity = 1 }).Validate(), "transaction context");
        Assert.InvalidContains((open with { SourceItemInstanceId = "item:bad" }).Validate(), "source container");
    }

    private static void InventoryMutationResult_ValidatesCorrelationFailureAndAffectedContainers()
    {
        var accepted = new InventoryMutationResult(
            "corr/move/1",
            ClientSequence: 1,
            InventoryMutationKind.MoveItem,
            InventoryMutationResultKind.Accepted,
            InventoryMutationFailureReason.None,
            Message: null,
            AffectedContainerIds: ["container:backpack/1", "container:bank/1"]);
        Assert.Valid(accepted.Validate());

        var rejected = accepted with
        {
            ResultKind = InventoryMutationResultKind.Rejected,
            FailureReason = InventoryMutationFailureReason.Locked,
            Message = "Item is locked down.",
            AffectedContainerIds = []
        };
        Assert.Valid(rejected.Validate());

        Assert.InvalidContains((accepted with { FailureReason = InventoryMutationFailureReason.Locked }).Validate(), "Accepted");
        Assert.InvalidContains((rejected with { FailureReason = InventoryMutationFailureReason.None }).Validate(), "require");
        Assert.InvalidContains((accepted with
        {
            AffectedContainerIds = ["container:backpack/1", "container:backpack/1"]
        }).Validate(), "duplicate");
    }

    private static void InventoryContainerViewerRegistry_FiltersDeltasByAccessAndScope()
    {
        var registry = new InventoryContainerViewerRegistrySnapshot(
            "container:vendor/stock",
            Revision: 1,
            [
                new InventoryContainerViewSnapshot(
                    "view:buyer",
                    "container:vendor/stock",
                    new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:buyer"),
                    InventoryAccessFlags.Read,
                    VisibilityScopeId: "scope/vendor/display"),
                new InventoryContainerViewSnapshot(
                    "view:stock_admin",
                    "container:vendor/stock",
                    new InventoryRequesterRef(InventoryRequesterKind.Admin, "admin:gm"),
                    InventoryAccessFlags.Read | InventoryAccessFlags.Take | InventoryAccessFlags.Admin),
                new InventoryContainerViewSnapshot(
                    "view:public",
                    "container:vendor/stock",
                    new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:other"),
                    InventoryAccessFlags.Read,
                    VisibilityScopeId: "scope/public"),
            ]);
        Assert.Valid(registry.Validate());

        var displayDelta = new InventoryContainerDelta(
            "delta:vendor/display/1",
            "container:vendor/stock",
            ContainerRevision: 2,
            InventoryContainerDeltaKind.QuantityChanged,
            ItemInstanceId: "item:potion/1",
            Item: null,
            PreviousPlacement: null,
            CurrentPlacement: null,
            QuantityDelta: -1,
            InventoryAccessFlags.Read,
            VisibilityScopeId: "scope/vendor/display");
        Assert.Valid(displayDelta.Validate(InventoryPlacementMode.List));
        Assert.Equal(2, registry.CountRecipients(displayDelta), "display delta recipient count");

        var adminDelta = displayDelta with
        {
            DeltaId = "delta:vendor/admin/1",
            RequiredAccess = InventoryAccessFlags.Admin,
            VisibilityScopeId = null
        };
        Assert.Valid(adminDelta.Validate(InventoryPlacementMode.List));
        Assert.Equal(1, registry.CountRecipients(adminDelta), "admin delta recipient count");

        var duplicateView = registry with
        {
            Viewers = [registry.Viewers[0], registry.Viewers[0]]
        };
        Assert.InvalidContains(duplicateView.Validate(), "duplicate view");
    }

    private static void InventoryContainerDelta_ValidatesItemAndPlacementShapes()
    {
        var added = new InventoryContainerDelta(
            "delta:add/1",
            "container:backpack/1",
            ContainerRevision: 2,
            InventoryContainerDeltaKind.ItemAdded,
            ItemInstanceId: null,
            Item: new InventoryItemSnapshot(
                "item:apple_stack/2",
                "item.apple",
                Quantity: 3,
                MaxStackQuantity: 20,
                InventoryPlacementDescriptor.List(3)),
            PreviousPlacement: null,
            CurrentPlacement: null,
            QuantityDelta: 0,
            InventoryAccessFlags.Read);
        Assert.Valid(added.Validate(InventoryPlacementMode.List));

        var moved = new InventoryContainerDelta(
            "delta:move/1",
            "container:backpack/1",
            ContainerRevision: 3,
            InventoryContainerDeltaKind.ItemMoved,
            ItemInstanceId: "item:apple_stack/2",
            Item: null,
            PreviousPlacement: InventoryPlacementDescriptor.List(3),
            CurrentPlacement: InventoryPlacementDescriptor.List(0),
            QuantityDelta: 0,
            InventoryAccessFlags.Read);
        Assert.Valid(moved.Validate(InventoryPlacementMode.List));

        Assert.InvalidContains((added with
        {
            Item = added.Item! with { Placement = InventoryPlacementDescriptor.Grid(0, 0, 1, 1) }
        }).Validate(InventoryPlacementMode.List), "not compatible");

        Assert.InvalidContains((moved with { CurrentPlacement = null }).Validate(InventoryPlacementMode.List), "previous and current");
        Assert.InvalidContains((moved with { RequiredAccess = InventoryAccessFlags.None }).Validate(InventoryPlacementMode.List), "cannot be None");
    }

    private static void InventoryTransactionSnapshot_ValidatesSuspendedItemLifecycle()
    {
        var transaction = new InventoryTransactionSnapshot(
            "trade:alpha_beta/1",
            InventoryTransactionState.Open,
            ["container:trade/alpha", "container:trade/beta"],
            [
                new InventorySuspendedItemSnapshot(
                    "item:iron_sword/1",
                    "container:trade/alpha",
                    "container:trade/beta",
                    Quantity: 1)
            ]);
        Assert.Valid(transaction.Validate());

        Assert.InvalidContains((transaction with
        {
            ParticipantContainerIds = ["container:trade/alpha", "container:trade/alpha"]
        }).Validate(), "duplicate participant");

        Assert.InvalidContains((transaction with
        {
            SuspendedItems =
            [
                transaction.SuspendedItems[0] with { DestinationContainerId = "container:not/in_trade" }
            ]
        }).Validate(), "participant");

        Assert.InvalidContains((transaction with
        {
            SuspendedItems = [transaction.SuspendedItems[0], transaction.SuspendedItems[0]]
        }).Validate(), "duplicate suspended");
    }

    private static void InventoryListProjection_ProjectsListAndGridContainersWithoutOwningTruth()
    {
        InventoryContainerSnapshot listContainer = CreateListContainer();
        InventoryListProjectionSnapshot listProjection = InventoryListProjectionSnapshot.FromContainer(
            "projection:list/backpack",
            listContainer,
            containerRevision: 7,
            InventoryProjectionSortDescriptor.ByPlacementIndex,
            viewId: "view:backpack");
        Assert.Valid(listProjection.Validate());
        Assert.Equal(listContainer.Items.Count, listProjection.Rows.Count, "list projection row count");

        var gridContainer = new InventoryContainerSnapshot(
            "container:grid_pack/1",
            "container.grid_pack",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:alpha"),
            InventoryContainerLifecycle.Persistent,
            InventoryPlacementMode.Grid,
            [InventoryPermissionDescriptor.PublicReadTake],
            [
                new InventoryItemSnapshot(
                    "item:wide_sword/1",
                    "item.wide_sword",
                    Quantity: 1,
                    MaxStackQuantity: 1,
                    InventoryPlacementDescriptor.Grid(0, 0, width: 2, height: 1)),
                new InventoryItemSnapshot(
                    "item:gem/1",
                    "item.gem",
                    Quantity: 3,
                    MaxStackQuantity: 20,
                    InventoryPlacementDescriptor.Grid(2, 0, width: 1, height: 1)),
            ]);
        Assert.Valid(gridContainer.Validate());

        InventoryListProjectionSnapshot gridAsList = InventoryListProjectionSnapshot.FromContainer(
            "projection:list/grid_pack",
            gridContainer,
            containerRevision: 3,
            InventoryProjectionSortDescriptor.ByDefinitionId);
        Assert.Valid(gridAsList.Validate());
        Assert.Equal(InventoryPlacementMode.Grid, gridAsList.SourcePlacementMode, "grid projection source placement mode");

        Assert.InvalidContains((gridAsList with
        {
            Rows =
            [
                gridAsList.Rows[0],
                gridAsList.Rows[1] with { DisplayIndex = gridAsList.Rows[0].DisplayIndex }
            ]
        }).Validate(), "duplicate display");

        Assert.InvalidContains((gridAsList with
        {
            Rows = [gridAsList.Rows[0], gridAsList.Rows[0] with { DisplayIndex = 8 }]
        }).Validate(), "duplicate item");

        Assert.InvalidContains((gridAsList with
        {
            Sort = InventoryProjectionSortDescriptor.ByDefinitionId with { Direction = (InventoryProjectionSortDirection)255 }
        }).Validate(), "sort direction");
    }

    private static InventoryContainerSnapshot CreateListContainer()
    {
        return new InventoryContainerSnapshot(
            "container:backpack/1",
            "container.backpack",
            new InventoryOwnerRef(InventoryOwnerKind.Character, "character:alpha"),
            InventoryContainerLifecycle.Persistent,
            InventoryPlacementMode.List,
            [InventoryPermissionDescriptor.PublicReadTake],
            [
                new InventoryItemSnapshot(
                    "item:iron_sword/1",
                    "item.iron_sword",
                    Quantity: 1,
                    MaxStackQuantity: 1,
                    InventoryPlacementDescriptor.List(0),
                    InventoryItemFlags.NoDrop),
                new InventoryItemSnapshot(
                    "item:apple_stack/1",
                    "item.apple",
                    Quantity: 5,
                    MaxStackQuantity: 20,
                    InventoryPlacementDescriptor.List(1)),
            ]);
    }

    private static InventoryMutationIntent CreateMoveIntent()
    {
        return new InventoryMutationIntent(
            "corr/move/1",
            new InventoryRequesterRef(InventoryRequesterKind.PlayerSession, "session:alpha"),
            ClientSequence: 1,
            InventoryMutationKind.MoveItem,
            SourceContainerId: "container:backpack/1",
            DestinationContainerId: "container:bank/1",
            SourceItemInstanceId: "item:apple_stack/1",
            DestinationItemInstanceId: null,
            Quantity: 2,
            ProposedPlacement: InventoryPlacementDescriptor.List(4));
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
    public static void Valid(InventoryValidationResult result)
    {
        if (!result.IsValid)
        {
            throw new InvalidOperationException($"Expected valid result, got '{result.Error}'.");
        }
    }

    public static void InvalidContains(InventoryValidationResult result, string text)
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

    public static void Equal(int expected, int actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected {label} to equal {expected}, got {actual}.");
        }
    }

    public static void Equal(InventoryPlacementMode expected, InventoryPlacementMode actual, string label)
    {
        if (expected != actual)
        {
            throw new InvalidOperationException($"Expected {label} to equal {expected}, got {actual}.");
        }
    }
}
