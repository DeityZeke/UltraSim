namespace UltraSim.UI;

/// <summary>Readable cold authoring source for one server-defined view.</summary>
public sealed class UiDefinitionBuilder
{
    private readonly object _identity = new();
    private readonly List<UiSourceSlot> _slots = [];
    private readonly List<UiSourceAction> _actions = [];
    private UiSourceLayoutNode? _root;

    public UiDefinitionBuilder(UiDefinitionKey key, ushort version = 1)
    {
        if (!key.IsValid)
            throw new ArgumentException("A valid UI definition key is required.", nameof(key));
        if (version == 0)
            throw new ArgumentOutOfRangeException(nameof(version));
        Key = key;
        Version = version;
    }

    public UiDefinitionBuilder(string key, ushort version = 1)
        : this(new UiDefinitionKey(key), version) { }

    public UiDefinitionKey Key { get; }

    public ushort Version { get; }

    /// <summary>Declares one dynamic scalar/list binding before layout authoring.</summary>
    public UiSlotReference DeclareSlot(string key, UiSlotValueKind valueKind)
    {
        ValidateSourceKey(key, nameof(key));
        if ((uint)valueKind > (uint)UiSlotValueKind.List)
            throw new ArgumentOutOfRangeException(nameof(valueKind));
        if (_slots.Count >= ushort.MaxValue)
            throw new InvalidOperationException("A UI definition cannot declare more than 65,535 slots.");
        string normalized = key.Trim();
        var reference = new UiSlotReference(_identity, (ushort)_slots.Count, normalized, valueKind);
        _slots.Add(new UiSourceSlot(normalized, valueKind));
        return reference;
    }

    /// <summary>Declares one prepared action contract before layout authoring.</summary>
    public UiActionReference DeclareAction(
        string key,
        bool acceptsRowContext = false,
        UiValueKind inputKind = UiValueKind.Unset)
    {
        ValidateSourceKey(key, nameof(key));
        if ((uint)inputKind > (uint)UiValueKind.Boolean)
            throw new ArgumentOutOfRangeException(nameof(inputKind));
        if (_actions.Count >= ushort.MaxValue)
            throw new InvalidOperationException("A UI definition cannot declare more than 65,535 actions.");
        string normalized = key.Trim();
        var reference = new UiActionReference(
            _identity,
            (ushort)_actions.Count,
            normalized,
            acceptsRowContext,
            inputKind);
        _actions.Add(new UiSourceAction(normalized, acceptsRowContext, inputKind));
        return reference;
    }

    /// <summary>Starts the single renderer-neutral Canvas root.</summary>
    public void AddCanvas(Action<UiCanvasBuilder> configure) =>
        AddCanvas(UiCanvasOptions.Default, configure);

    /// <summary>Starts the Canvas root with explicit logical dimensions.</summary>
    public void AddCanvas(UiCanvasOptions options, Action<UiCanvasBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_root is not null)
            throw new InvalidOperationException("A UI definition may contain exactly one root Canvas.");

        var root = new UiSourceLayoutNode(
            UiLayoutKind.Canvas,
            key: "root",
            parentPlacement: default,
            flow: default,
            canvas: options);
        configure(new UiCanvasBuilder(this, root));
        _root = root;
    }

    internal UiSourceLayoutNode? Root => _root;

    internal IReadOnlyList<UiSourceSlot> Slots => _slots;

    internal IReadOnlyList<UiSourceAction> Actions => _actions;

    internal object Identity => _identity;

    // Kept internal so validation tests can exercise the same guard used by
    // future deserializers without widening the authoring API.
    internal void SetRootForTesting(UiSourceLayoutNode root)
    {
        ArgumentNullException.ThrowIfNull(root);
        if (_root is not null)
            throw new InvalidOperationException("A UI definition may contain exactly one root Canvas.");
        _root = root;
    }

    internal static void ValidateSourceKey(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        if (value.Trim().Length > UiDefinitionKey.MaximumLength)
        {
            throw new ArgumentOutOfRangeException(
                parameterName,
                $"A UI source key is limited to {UiDefinitionKey.MaximumLength} characters.");
        }
    }

    internal static void ValidateChildLayout(UiChildLayout layout)
    {
        if ((uint)layout.Sizing > (uint)UiSizing.Fill)
            throw new ArgumentOutOfRangeException(nameof(layout));
        if ((uint)layout.Alignment > (uint)UiAlignment.Stretch)
            throw new ArgumentOutOfRangeException(nameof(layout));
    }
}

/// <summary>Cold lexical scope for children of a Canvas or flow container.</summary>
public abstract class UiContainerBuilder
{
    private readonly UiDefinitionBuilder _owner;
    private readonly UiSourceLayoutNode _node;

    internal UiContainerBuilder(UiDefinitionBuilder owner, UiSourceLayoutNode node)
    {
        _owner = owner;
        _node = node;
    }

    public void AddVertical(
        string key,
        Action<UiVerticalBuilder> configure,
        UiChildLayout placement = default,
        UiFlowOptions flow = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AddLayout(UiLayoutKind.Vertical, key, placement, flow,
            child => configure(new UiVerticalBuilder(_owner, child)));
    }

    public void AddHorizontal(
        string key,
        Action<UiHorizontalBuilder> configure,
        UiChildLayout placement = default,
        UiFlowOptions flow = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AddLayout(UiLayoutKind.Horizontal, key, placement, flow,
            child => configure(new UiHorizontalBuilder(_owner, child)));
    }

    public void AddOverlay(
        string key,
        Action<UiOverlayBuilder> configure,
        UiChildLayout placement = default,
        UiFlowOptions flow = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AddLayout(UiLayoutKind.Overlay, key, placement, flow,
            child => configure(new UiOverlayBuilder(_owner, child)));
    }

    public void AddScroll(
        string key,
        Action<UiScrollBuilder> configure,
        UiChildLayout placement = default,
        UiFlowOptions flow = default)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AddLayout(UiLayoutKind.Scroll, key, placement, flow,
            child => configure(new UiScrollBuilder(_owner, child)));
    }

    public void AddPanel(string key, string styleRole, UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.Panel, key, placement, styleRole, null,
            slotKey: null, actionKey: null));

    public void AddText(string key, string slotKey, UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.Text, key, placement, null, null,
            slotKey, actionKey: null));

    public void AddText(string key, UiSlotReference slot, UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.Text, key, placement, null, null,
            slot, default));

    public void AddImage(string key, string assetRole, UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.Image, key, placement, null, assetRole,
            slotKey: null, actionKey: null));

    public void AddButton(
        string key,
        string actionKey,
        string labelSlotKey,
        UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.Button, key, placement, null, null,
            labelSlotKey, actionKey));

    public void AddButton(
        string key,
        UiActionReference action,
        UiSlotReference labelSlot,
        UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.Button, key, placement, null, null,
            labelSlot, action));

    public void AddTextInput(string key, string slotKey, UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.TextInput, key, placement, null, null,
            slotKey, actionKey: null));

    public void AddTextInput(
        string key,
        string slotKey,
        string actionKey,
        UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.TextInput, key, placement, null, null,
            slotKey, actionKey));

    public void AddTextInput(
        string key,
        UiSlotReference slot,
        UiActionReference action,
        UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.TextInput, key, placement, null, null,
            slot, action));

    public void AddToggle(string key, string slotKey, UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.Toggle, key, placement, null, null,
            slotKey, actionKey: null));

    public void AddToggle(
        string key,
        string slotKey,
        string actionKey,
        UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.Toggle, key, placement, null, null,
            slotKey, actionKey));

    public void AddToggle(
        string key,
        UiSlotReference slot,
        UiActionReference action,
        UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.Toggle, key, placement, null, null,
            slot, action));

    public void AddList(string key, string rowsSlotKey, UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.List, key, placement, null, null,
            rowsSlotKey, actionKey: null));

    public void AddList(string key, UiSlotReference rows, UiChildLayout placement = default) =>
        AddElement(new UiSourceElement(UiElementKind.List, key, placement, null, null,
            rows, default));

    private void AddLayout(
        UiLayoutKind kind,
        string key,
        UiChildLayout placement,
        UiFlowOptions flow,
        Action<UiSourceLayoutNode> configure)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        UiDefinitionBuilder.ValidateSourceKey(key, nameof(key));
        ArgumentNullException.ThrowIfNull(configure);
        UiDefinitionBuilder.ValidateChildLayout(placement);
        int siblingOrder = _node.NextSiblingOrder;
        var child = new UiSourceLayoutNode(kind, key.Trim(), placement, flow, default, siblingOrder);
        configure(child);
        _node.AddChild(child);
    }

    private void AddElement(UiSourceElement element)
    {
        UiDefinitionBuilder.ValidateSourceKey(element.Key, nameof(element));
        UiDefinitionBuilder.ValidateChildLayout(element.Placement);
        _node.AddElement(element.WithSiblingOrder(_node.NextSiblingOrder));
    }
}

public sealed class UiCanvasBuilder : UiContainerBuilder
{
    internal UiCanvasBuilder(UiDefinitionBuilder owner, UiSourceLayoutNode node) : base(owner, node) { }
}

public sealed class UiVerticalBuilder : UiContainerBuilder
{
    internal UiVerticalBuilder(UiDefinitionBuilder owner, UiSourceLayoutNode node) : base(owner, node) { }
}

public sealed class UiHorizontalBuilder : UiContainerBuilder
{
    internal UiHorizontalBuilder(UiDefinitionBuilder owner, UiSourceLayoutNode node) : base(owner, node) { }
}

public sealed class UiOverlayBuilder : UiContainerBuilder
{
    internal UiOverlayBuilder(UiDefinitionBuilder owner, UiSourceLayoutNode node) : base(owner, node) { }
}

public sealed class UiScrollBuilder : UiContainerBuilder
{
    internal UiScrollBuilder(UiDefinitionBuilder owner, UiSourceLayoutNode node) : base(owner, node) { }
}

internal sealed class UiSourceLayoutNode
{
    internal UiSourceLayoutNode(
        UiLayoutKind kind,
        string key,
        UiChildLayout parentPlacement,
        UiFlowOptions flow,
        UiCanvasOptions canvas,
        int siblingOrder = -1)
    {
        Kind = kind;
        Key = key;
        ParentPlacement = parentPlacement;
        Flow = flow;
        Canvas = canvas;
        SiblingOrder = siblingOrder;
    }

    internal UiLayoutKind Kind { get; }
    internal string Key { get; }
    internal UiChildLayout ParentPlacement { get; }
    internal UiFlowOptions Flow { get; }
    internal UiCanvasOptions Canvas { get; }
    internal int SiblingOrder { get; }
    internal List<UiSourceLayoutNode> Children { get; } = [];
    internal List<UiSourceElement> Elements { get; } = [];
    internal int NextSiblingOrder { get; private set; }

    internal void AddChild(UiSourceLayoutNode child)
    {
        if (child.SiblingOrder != NextSiblingOrder)
            throw new InvalidOperationException("The UI child order is not contiguous.");
        Children.Add(child);
        NextSiblingOrder++;
    }

    internal void AddElement(UiSourceElement element)
    {
        if (element.SiblingOrder != NextSiblingOrder)
            throw new InvalidOperationException("The UI child order is not contiguous.");
        Elements.Add(element);
        NextSiblingOrder++;
    }
}

internal sealed class UiSourceElement
{
    internal UiSourceElement(
        UiElementKind kind,
        string key,
        UiChildLayout placement,
        string? styleRole,
        string? assetRole,
        string? slotKey,
        string? actionKey,
        int siblingOrder = -1)
    {
        Kind = kind;
        Key = key;
        Placement = placement;
        StyleRole = styleRole;
        AssetRole = assetRole;
        SlotKey = slotKey;
        ActionKey = actionKey;
        SiblingOrder = siblingOrder;
    }

    internal UiSourceElement(
        UiElementKind kind,
        string key,
        UiChildLayout placement,
        string? styleRole,
        string? assetRole,
        UiSlotReference slot,
        UiActionReference action)
        : this(kind, key, placement, styleRole, assetRole, slot.Key, action.Key)
    {
        SlotReference = slot;
        ActionReference = action;
    }

    internal UiElementKind Kind { get; }
    internal string Key { get; }
    internal UiChildLayout Placement { get; }
    internal string? StyleRole { get; }
    internal string? AssetRole { get; }
    internal string? SlotKey { get; }
    internal string? ActionKey { get; }
    internal UiSlotReference SlotReference { get; private init; }
    internal UiActionReference ActionReference { get; private init; }
    internal int SiblingOrder { get; }

    internal UiSourceElement WithSiblingOrder(int siblingOrder) => new(
        Kind,
        Key,
        Placement,
        StyleRole,
        AssetRole,
        SlotKey,
        ActionKey,
        siblingOrder)
    {
        SlotReference = SlotReference,
        ActionReference = ActionReference,
    };
}

internal readonly record struct UiSourceSlot(string Key, UiSlotValueKind ValueKind);

internal readonly record struct UiSourceAction(
    string Key,
    bool AcceptsRowContext,
    UiValueKind InputKind);
