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
}
