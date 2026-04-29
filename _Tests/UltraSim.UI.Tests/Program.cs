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
        Run(nameof(UiStateBindingDescriptor_ValidatesPerBindingFallback), UiStateBindingDescriptor_ValidatesPerBindingFallback);
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
