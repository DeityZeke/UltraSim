using System.Buffers;
using System.Buffers.Binary;
using System.Collections.ObjectModel;
using System.Security.Cryptography;
using System.Text;

namespace UltraSim.UI;

/// <summary>Immutable compiled layout node.</summary>
public readonly struct UiCompiledLayoutNode
{
    internal UiCompiledLayoutNode(
        UiLayoutNodeId id,
        UiLayoutNodeId parent,
        int siblingOrder,
        UiLayoutKind kind,
        string key,
        UiChildLayout placement,
        UiFlowOptions flow,
        UiCanvasOptions canvas)
    {
        Id = id;
        Parent = parent;
        SiblingOrder = siblingOrder;
        Kind = kind;
        Key = key;
        Placement = placement;
        Flow = flow;
        Canvas = canvas;
    }

    public UiLayoutNodeId Id { get; }
    public UiLayoutNodeId Parent { get; }
    public int SiblingOrder { get; }
    public UiLayoutKind Kind { get; }
    public string Key { get; }
    public UiChildLayout Placement { get; }
    public UiFlowOptions Flow { get; }
    public UiCanvasOptions Canvas { get; }
}

/// <summary>Immutable compiled element binding.</summary>
public readonly struct UiCompiledElement
{
    internal UiCompiledElement(
        UiElementId id,
        UiLayoutNodeId parent,
        int siblingOrder,
        UiElementKind kind,
        string key,
        UiChildLayout placement,
        UiSlotId slot,
        UiActionId action,
        string styleRole,
        string assetRole)
    {
        Id = id;
        Parent = parent;
        SiblingOrder = siblingOrder;
        Kind = kind;
        Key = key;
        Placement = placement;
        Slot = slot;
        Action = action;
        StyleRole = styleRole;
        AssetRole = assetRole;
    }

    public UiElementId Id { get; }
    public UiLayoutNodeId Parent { get; }
    public int SiblingOrder { get; }
    public UiElementKind Kind { get; }
    public string Key { get; }
    public UiChildLayout Placement { get; }
    public UiSlotId Slot { get; }
    public UiActionId Action { get; }
    public string StyleRole { get; }
    public string AssetRole { get; }
}

/// <summary>Immutable compiled dynamic slot declaration.</summary>
public readonly struct UiSlotDefinition
{
    internal UiSlotDefinition(UiSlotId id, string key, UiSlotValueKind valueKind)
    {
        Id = id;
        Key = key;
        ValueKind = valueKind;
    }

    public UiSlotId Id { get; }
    public string Key { get; }
    public UiSlotValueKind ValueKind { get; }
}

/// <summary>Immutable compiled action contract.</summary>
public readonly struct UiActionDefinition
{
    internal UiActionDefinition(
        UiActionId id,
        string key,
        bool acceptsRowContext,
        UiValueKind inputKind)
    {
        Id = id;
        Key = key;
        AcceptsRowContext = acceptsRowContext;
        InputKind = inputKind;
    }

    public UiActionId Id { get; }
    public string Key { get; }
    public bool AcceptsRowContext { get; }
    public UiValueKind InputKind { get; }
}

/// <summary>
/// Cold-only binding lookup emitted alongside a compiled definition.
/// </summary>
/// <remarks>
/// Source keys are retained here for diagnostics and authoring/tool output.
/// Prepared game code should retain the returned numeric IDs rather than doing
/// string lookup while creating or updating a model.
/// </remarks>
public sealed class UiPreparedBindings
{
    private readonly IReadOnlyDictionary<string, UiSlotId> _slots;
    private readonly IReadOnlyDictionary<string, UiActionId> _actions;

    internal UiPreparedBindings(
        IReadOnlyDictionary<string, UiSlotId> slots,
        IReadOnlyDictionary<string, UiActionId> actions)
    {
        _slots = new ReadOnlyDictionary<string, UiSlotId>(
            new Dictionary<string, UiSlotId>(slots, StringComparer.Ordinal));
        _actions = new ReadOnlyDictionary<string, UiActionId>(
            new Dictionary<string, UiActionId>(actions, StringComparer.Ordinal));
    }

    public bool TryGetSlot(string sourceKey, out UiSlotId slot) =>
        _slots.TryGetValue(sourceKey, out slot);

    public bool TryGetAction(string sourceKey, out UiActionId action) =>
        _actions.TryGetValue(sourceKey, out action);

    public UiSlotId RequireSlot(string sourceKey)
    {
        if (!TryGetSlot(sourceKey, out UiSlotId slot))
            throw new KeyNotFoundException($"Unknown prepared UI slot '{sourceKey}'.");
        return slot;
    }

    public UiActionId RequireAction(string sourceKey)
    {
        if (!TryGetAction(sourceKey, out UiActionId action))
            throw new KeyNotFoundException($"Unknown prepared UI action '{sourceKey}'.");
        return action;
    }
}

/// <summary>Immutable renderer-neutral result of UI definition compilation.</summary>
public sealed class CompiledUiDefinition
{
    private readonly UiCompiledLayoutNode[] _layoutNodes;
    private readonly UiCompiledElement[] _elements;
    private readonly UiSlotDefinition[] _slots;
    private readonly UiActionDefinition[] _actions;
    private readonly object _authoringIdentity;

    internal CompiledUiDefinition(
        UiDefinitionKey key,
        ushort version,
        UiDefinitionHandle handle,
        UiDefinitionHash hash,
        UiCompileLimits limits,
        UiCompiledLayoutNode[] layoutNodes,
        UiCompiledElement[] elements,
        UiSlotDefinition[] slots,
        UiActionDefinition[] actions,
        UiPreparedBindings bindings,
        object authoringIdentity)
    {
        Key = key;
        Version = version;
        Handle = handle;
        Hash = hash;
        Limits = limits;
        _layoutNodes = layoutNodes;
        _elements = elements;
        _slots = slots;
        _actions = actions;
        Bindings = bindings;
        _authoringIdentity = authoringIdentity;
    }

    public UiDefinitionKey Key { get; }
    public ushort Version { get; }
    public UiDefinitionHandle Handle { get; }
    public UiDefinitionHash Hash { get; }
    public UiCompileLimits Limits { get; }
    public UiPreparedBindings Bindings { get; }

    public int LayoutNodeCount => _layoutNodes.Length;
    public int ElementCount => _elements.Length;
    public int SlotCount => _slots.Length;
    public int ActionCount => _actions.Length;

    public ReadOnlySpan<UiCompiledLayoutNode> LayoutNodes => _layoutNodes;
    public ReadOnlySpan<UiCompiledElement> Elements => _elements;
    public ReadOnlySpan<UiSlotDefinition> Slots => _slots;
    public ReadOnlySpan<UiActionDefinition> Actions => _actions;

    public UiSlotDefinition GetSlot(UiSlotId id)
    {
        if (!id.IsValid || id.Value >= _slots.Length)
            throw new ArgumentOutOfRangeException(nameof(id));
        return _slots[id.Value];
    }

    public UiActionDefinition GetAction(UiActionId id)
    {
        if (!id.IsValid || id.Value >= _actions.Length)
            throw new ArgumentOutOfRangeException(nameof(id));
        return _actions[id.Value];
    }

    public UiSlotValueKind GetSlotValueKind(UiSlotId id) => GetSlot(id).ValueKind;

    /// <summary>Prepares a definition-local slot reference into its compact runtime ID.</summary>
    public UiSlotId Prepare(UiSlotReference reference)
    {
        if (!reference.IsValid || !ReferenceEquals(reference.Owner, _authoringIdentity) ||
            reference.Ordinal >= _slots.Length)
        {
            throw new InvalidOperationException("The slot reference does not belong to this UI definition.");
        }

        UiSlotDefinition slot = _slots[reference.Ordinal];
        if (!string.Equals(slot.Key, reference.Key, StringComparison.Ordinal) ||
            slot.ValueKind != reference.ValueKind)
        {
            throw new InvalidOperationException("The slot reference does not match the compiled declaration.");
        }
        return slot.Id;
    }

    /// <summary>Prepares a definition-local action reference into its compact runtime ID.</summary>
    public UiActionId Prepare(UiActionReference reference)
    {
        if (!reference.IsValid || !ReferenceEquals(reference.Owner, _authoringIdentity) ||
            reference.Ordinal >= _actions.Length)
        {
            throw new InvalidOperationException("The action reference does not belong to this UI definition.");
        }

        UiActionDefinition action = _actions[reference.Ordinal];
        if (!string.Equals(action.Key, reference.Key, StringComparison.Ordinal) ||
            action.AcceptsRowContext != reference.AcceptsRowContext ||
            action.InputKind != reference.InputKind)
        {
            throw new InvalidOperationException("The action reference does not match the compiled declaration.");
        }
        return action.Id;
    }

    /// <summary>Validates one transient action against the retained definition.</summary>
    public void ValidateAction(in UiActionRequest request, UiModel? model = null)
    {
        if (request.Definition != Handle)
            throw new InvalidOperationException("The action definition does not match the retained UI definition.");
        if (!request.Instance.IsValid)
            throw new InvalidOperationException("A UI action requires a valid view instance ID.");

        UiActionDefinition action = GetAction(request.Action);
        if (action.AcceptsRowContext != request.Row.IsValid)
        {
            throw new InvalidOperationException(
                action.AcceptsRowContext
                    ? "This UI action requires a row context."
                    : "This UI action does not accept a row context.");
        }
        if (request.Input.Kind != action.InputKind)
        {
            throw new InvalidOperationException(
                $"This UI action requires {action.InputKind} input, but received {request.Input.Kind}.");
        }

        if (model is not null)
        {
            if (!ReferenceEquals(model.Definition, this))
                throw new InvalidOperationException("The action model does not match the retained UI definition.");
            if (!model.GetActionState(request.Action).Enabled)
                throw new InvalidOperationException("The requested UI action is currently unavailable.");
        }
    }
}

/// <summary>Deterministic compiler for the bounded UI-0 authoring vocabulary.</summary>
public static class UiDefinitionCompiler
{
    public static CompiledUiDefinition Compile(
        UiDefinitionBuilder source,
        UiCompileLimits limits = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (limits.Equals(default(UiCompileLimits)))
            limits = UiCompileLimits.Default;

        UiSourceLayoutNode root = source.Root ??
            throw new InvalidOperationException("A UI definition requires one root Canvas.");
        if (root.Kind != UiLayoutKind.Canvas)
            throw new InvalidDataException("The root UI layout must be Canvas.");
        ValidateSourceDeclarations(source, limits);

        var slots = new UiSlotDefinition[source.Slots.Count];
        var slotIds = new Dictionary<string, UiSlotId>(StringComparer.Ordinal);
        var slotKinds = new Dictionary<string, UiSlotValueKind>(StringComparer.Ordinal);
        for (int index = 0; index < source.Slots.Count; index++)
        {
            UiSourceSlot slot = source.Slots[index];
            var id = new UiSlotId(checked((ushort)index));
            slots[index] = new UiSlotDefinition(id, slot.Key, slot.ValueKind);
            slotIds.Add(slot.Key, id);
            slotKinds.Add(slot.Key, slot.ValueKind);
        }

        var actions = new UiActionDefinition[source.Actions.Count];
        var actionIds = new Dictionary<string, UiActionId>(StringComparer.Ordinal);
        for (int index = 0; index < source.Actions.Count; index++)
        {
            UiSourceAction action = source.Actions[index];
            var id = new UiActionId(checked((ushort)index));
            actions[index] = new UiActionDefinition(
                id,
                action.Key,
                action.AcceptsRowContext,
                action.InputKind);
            actionIds.Add(action.Key, id);
        }

        var layoutNodes = new List<UiCompiledLayoutNode>();
        var elements = new List<UiCompiledElement>();
        var elementKeys = new HashSet<string>(StringComparer.Ordinal);
        int textCharacters = 0;
        AddText(ref textCharacters, source.Key.Value, limits);
        foreach (UiSourceSlot slot in source.Slots)
            AddText(ref textCharacters, slot.Key, limits);
        foreach (UiSourceAction action in source.Actions)
            AddText(ref textCharacters, action.Key, limits);
        FlattenNode(root, UiLayoutNodeId.Invalid, depth: 0, parentIsCanvas: false,
            layoutNodes, elements, elementKeys, slotIds, slotKinds, actionIds, source, limits,
            ref textCharacters);

        UiPreparedBindings bindings = new(slotIds, actionIds);
        UiDefinitionHash hash = ComputeHash(
            source.Key,
            source.Version,
            limits,
            layoutNodes,
            elements,
            slots,
            actions);
        UiDefinitionHandle handle = new(hash.Low, hash.High);

        return new CompiledUiDefinition(
            source.Key,
            source.Version,
            handle,
            hash,
            limits,
            layoutNodes.ToArray(),
            elements.ToArray(),
            slots,
            actions,
            bindings,
            source.Identity);
    }

    private static void ValidateSourceDeclarations(UiDefinitionBuilder source, UiCompileLimits limits)
    {
        if (source.Slots.Count > limits.MaximumSlots)
            throw new InvalidDataException("The UI slot limit was exceeded.");
        if (source.Actions.Count > limits.MaximumActions)
            throw new InvalidDataException("The UI action limit was exceeded.");

        var slotKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (UiSourceSlot slot in source.Slots)
        {
            ValidateCompiledSourceKey(slot.Key, "slot");
            if ((uint)slot.ValueKind > (uint)UiSlotValueKind.List)
                throw new InvalidDataException($"UI slot '{slot.Key}' has an unsupported value kind.");
            if (!slotKeys.Add(slot.Key))
                throw new InvalidDataException($"Duplicate UI slot declaration '{slot.Key}'.");
        }

        var actionKeys = new HashSet<string>(StringComparer.Ordinal);
        foreach (UiSourceAction action in source.Actions)
        {
            ValidateCompiledSourceKey(action.Key, "action");
            if ((uint)action.InputKind > (uint)UiValueKind.Boolean)
                throw new InvalidDataException($"UI action '{action.Key}' has an unsupported input kind.");
            if (!actionKeys.Add(action.Key))
                throw new InvalidDataException($"Duplicate UI action declaration '{action.Key}'.");
        }
    }

    private static void FlattenNode(
        UiSourceLayoutNode source,
        UiLayoutNodeId parent,
        int depth,
        bool parentIsCanvas,
        List<UiCompiledLayoutNode> nodes,
        List<UiCompiledElement> elements,
        HashSet<string> elementKeys,
        Dictionary<string, UiSlotId> slotIds,
        Dictionary<string, UiSlotValueKind> slotKinds,
        Dictionary<string, UiActionId> actionIds,
        UiDefinitionBuilder definitionSource,
        UiCompileLimits limits,
        ref int textCharacters)
    {
        if (depth > limits.MaximumDepth)
            throw new InvalidDataException("The UI layout depth limit was exceeded.");
        if (nodes.Count >= limits.MaximumLayoutNodes)
            throw new InvalidDataException("The UI layout-node limit was exceeded.");
        ValidateLayoutKind(source.Kind);
        if ((depth == 0) != (source.Kind == UiLayoutKind.Canvas))
            throw new InvalidDataException("Canvas is valid only as the root UI layout.");
        ValidateCompiledSourceKey(source.Key, "layout");
        ValidateLayoutOptions(source, depth == 0);
        ValidatePlacement(source.ParentPlacement, parentIsCanvas, source.Kind);
        ValidateSiblingOrder(source);
        AddText(ref textCharacters, source.Key, limits);

        var id = new UiLayoutNodeId(nodes.Count);
        nodes.Add(new UiCompiledLayoutNode(
            id,
            parent,
            source.SiblingOrder,
            source.Kind,
            source.Key,
            source.ParentPlacement,
            source.Flow,
            source.Canvas));

        bool childrenAreCanvas = source.Kind == UiLayoutKind.Canvas;
        foreach (UiSourceElement element in source.Elements)
        {
            if (elements.Count >= limits.MaximumElements)
                throw new InvalidDataException("The UI element limit was exceeded.");
            ValidateElement(element, source.Kind, childrenAreCanvas, slotIds, slotKinds, actionIds,
                definitionSource,
                limits, ref textCharacters);
            if (!elementKeys.Add(element.Key))
                throw new InvalidDataException($"Duplicate UI element key '{element.Key}'.");

            UiSlotId slot = element.SlotKey is null
                ? UiSlotId.Invalid
                : slotIds[element.SlotKey];
            UiActionId action = element.ActionKey is null
                ? UiActionId.Invalid
                : actionIds[element.ActionKey];
            elements.Add(new UiCompiledElement(
                new UiElementId(checked((ushort)elements.Count)),
                id,
                element.SiblingOrder,
                element.Kind,
                element.Key,
                element.Placement,
                slot,
                action,
                element.StyleRole ?? string.Empty,
                element.AssetRole ?? string.Empty));
        }

        foreach (UiSourceLayoutNode child in source.Children)
        {
            FlattenNode(child, id, checked(depth + 1), childrenAreCanvas,
                nodes, elements, elementKeys, slotIds, slotKinds, actionIds, definitionSource, limits,
                ref textCharacters);
        }
    }

    private static void ValidateElement(
        UiSourceElement element,
        UiLayoutKind parentKind,
        bool parentIsCanvas,
        Dictionary<string, UiSlotId> slotIds,
        Dictionary<string, UiSlotValueKind> slotKinds,
        Dictionary<string, UiActionId> actionIds,
        UiDefinitionBuilder definitionSource,
        UiCompileLimits limits,
        ref int textCharacters)
    {
        if ((uint)element.Kind > (uint)UiElementKind.List)
            throw new InvalidDataException("The UI element kind is unsupported.");
        ValidateCompiledSourceKey(element.Key, "element");
        ValidatePlacement(element.Placement, parentIsCanvas, parentKind);
        ValidateReference(element.SlotReference, definitionSource);
        ValidateReference(element.ActionReference, definitionSource);
        AddText(ref textCharacters, element.Key, limits);
        AddText(ref textCharacters, element.StyleRole, limits);
        AddText(ref textCharacters, element.AssetRole, limits);
        AddText(ref textCharacters, element.SlotKey, limits);
        AddText(ref textCharacters, element.ActionKey, limits);

        UiSlotValueKind required = element.Kind switch
        {
            UiElementKind.Text or UiElementKind.TextInput => UiSlotValueKind.Text,
            UiElementKind.Toggle => UiSlotValueKind.Boolean,
            UiElementKind.List => UiSlotValueKind.List,
            UiElementKind.Button => UiSlotValueKind.Text,
            _ => UiSlotValueKind.Any,
        };

        if (required != UiSlotValueKind.Any)
        {
            if (element.SlotKey is null || !slotIds.TryGetValue(element.SlotKey, out UiSlotId slot))
                throw new InvalidDataException($"Unknown or missing UI slot binding for element '{element.Key}'.");
            UiSlotValueKind actual = slotKinds[element.SlotKey];
            if (actual != UiSlotValueKind.Any && actual != required)
            {
                throw new InvalidDataException(
                    $"UI element '{element.Key}' requires a {required} slot, but '{element.SlotKey}' is {actual}.");
            }
        }

        if (element.SlotKey is not null && !slotIds.ContainsKey(element.SlotKey))
            throw new InvalidDataException($"Unknown UI slot binding '{element.SlotKey}'.");
        if (element.ActionKey is not null && !actionIds.ContainsKey(element.ActionKey))
            throw new InvalidDataException($"Unknown UI action binding '{element.ActionKey}'.");

        if (element.Kind == UiElementKind.Button && element.ActionKey is null)
            throw new InvalidDataException($"Button '{element.Key}' requires an action binding.");
        if (element.Kind == UiElementKind.Image && string.IsNullOrWhiteSpace(element.AssetRole))
            throw new InvalidDataException($"Image '{element.Key}' requires an asset role.");

        if (element.ActionKey is not null)
        {
            UiSourceAction action = definitionSource.Actions[actionIds[element.ActionKey].Value];
            bool requiresRowContext = element.Kind == UiElementKind.List;
            if (action.AcceptsRowContext != requiresRowContext)
            {
                throw new InvalidDataException(
                    requiresRowContext
                        ? $"List '{element.Key}' requires a row-context action."
                        : $"UI element '{element.Key}' cannot bind a row-context action outside a List.");
            }
            UiValueKind expectedInput = element.Kind switch
            {
                UiElementKind.TextInput => UiValueKind.Text,
                UiElementKind.Toggle => UiValueKind.Boolean,
                _ => UiValueKind.Unset,
            };
            if (action.InputKind != expectedInput)
            {
                throw new InvalidDataException(
                    $"UI element '{element.Key}' requires an action with {expectedInput} input, " +
                    $"but '{element.ActionKey}' accepts {action.InputKind}.");
            }
        }
    }

    private static void ValidateReference(UiSlotReference reference, UiDefinitionBuilder source)
    {
        if (!reference.IsValid)
            return;
        if (!ReferenceEquals(reference.Owner, source.Identity) || reference.Ordinal >= source.Slots.Count)
            throw new InvalidDataException("A UI element uses a slot reference from another definition.");
        UiSourceSlot declaration = source.Slots[reference.Ordinal];
        if (!string.Equals(declaration.Key, reference.Key, StringComparison.Ordinal) ||
            declaration.ValueKind != reference.ValueKind)
        {
            throw new InvalidDataException("A UI element slot reference does not match its declaration.");
        }
    }

    private static void ValidateReference(UiActionReference reference, UiDefinitionBuilder source)
    {
        if (!reference.IsValid)
            return;
        if (!ReferenceEquals(reference.Owner, source.Identity) || reference.Ordinal >= source.Actions.Count)
            throw new InvalidDataException("A UI element uses an action reference from another definition.");
        UiSourceAction declaration = source.Actions[reference.Ordinal];
        if (!string.Equals(declaration.Key, reference.Key, StringComparison.Ordinal) ||
            declaration.AcceptsRowContext != reference.AcceptsRowContext ||
            declaration.InputKind != reference.InputKind)
        {
            throw new InvalidDataException("A UI element action reference does not match its declaration.");
        }
    }

    private static void ValidateLayoutOptions(UiSourceLayoutNode source, bool root)
    {
        if (root)
        {
            if (!float.IsFinite(source.Canvas.Width) || source.Canvas.Width <= 0 ||
                !float.IsFinite(source.Canvas.Height) || source.Canvas.Height <= 0)
            {
                throw new InvalidDataException("The root Canvas requires finite positive logical dimensions.");
            }
        }

        if (!float.IsFinite(source.Flow.Spacing) || source.Flow.Spacing < 0 ||
            (uint)source.Flow.Sizing > (uint)UiSizing.Fill ||
            (uint)source.Flow.Alignment > (uint)UiAlignment.Stretch ||
            !IsValidPadding(source.Flow.Padding))
        {
            throw new InvalidDataException($"UI layout '{source.Key}' has invalid flow options.");
        }
    }

    private static void ValidateSiblingOrder(UiSourceLayoutNode source)
    {
        int childCount = checked(source.Children.Count + source.Elements.Count);
        if (childCount == 0)
            return;

        var seen = new bool[childCount];
        foreach (UiSourceLayoutNode child in source.Children)
            MarkSiblingOrder(source.Key, child.SiblingOrder, seen);
        foreach (UiSourceElement element in source.Elements)
            MarkSiblingOrder(source.Key, element.SiblingOrder, seen);
    }

    private static void MarkSiblingOrder(string key, int order, bool[] seen)
    {
        if (order < 0 || order >= seen.Length || seen[order])
            throw new InvalidDataException($"UI layout '{key}' has invalid or duplicate sibling order.");
        seen[order] = true;
    }

    private static bool IsValidPadding(UiPadding padding) =>
        float.IsFinite(padding.Left) && padding.Left >= 0 &&
        float.IsFinite(padding.Top) && padding.Top >= 0 &&
        float.IsFinite(padding.Right) && padding.Right >= 0 &&
        float.IsFinite(padding.Bottom) && padding.Bottom >= 0;

    private static void ValidatePlacement(UiChildLayout placement, bool parentIsCanvas, UiLayoutKind childKind)
    {
        if ((uint)placement.Sizing > (uint)UiSizing.Fill ||
            (uint)placement.Alignment > (uint)UiAlignment.Stretch)
        {
            throw new InvalidDataException($"UI child '{childKind}' has invalid flow placement options.");
        }
        if (parentIsCanvas && !placement.HasCanvasPlacement)
            throw new InvalidDataException($"Canvas child '{childKind}' requires a logical Canvas placement.");
        if (!parentIsCanvas && placement.HasCanvasPlacement)
            throw new InvalidDataException($"Flow child '{childKind}' cannot use Canvas placement.");
        if (placement.HasCanvasPlacement && !IsValidCanvasPlacement(placement.CanvasPlacement))
            throw new InvalidDataException($"Canvas child '{childKind}' has invalid logical placement.");
    }

    private static bool IsValidCanvasPlacement(UiCanvasPlacement placement) =>
        float.IsFinite(placement.X) && float.IsFinite(placement.Y) &&
        float.IsFinite(placement.Width) && placement.Width > 0 &&
        float.IsFinite(placement.Height) && placement.Height > 0 &&
        IsNormalized(placement.AnchorMinX) && IsNormalized(placement.AnchorMinY) &&
        IsNormalized(placement.AnchorMaxX) && IsNormalized(placement.AnchorMaxY) &&
        IsNormalized(placement.PivotX) && IsNormalized(placement.PivotY) &&
        placement.AnchorMinX <= placement.AnchorMaxX &&
        placement.AnchorMinY <= placement.AnchorMaxY;

    private static bool IsNormalized(float value) => float.IsFinite(value) && value >= 0 && value <= 1;

    private static void ValidateLayoutKind(UiLayoutKind kind)
    {
        if ((uint)kind > (uint)UiLayoutKind.Scroll)
            throw new InvalidDataException("The UI layout kind is unsupported.");
    }

    private static void ValidateCompiledSourceKey(string? key, string kind)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Trim().Length > UiDefinitionKey.MaximumLength)
            throw new InvalidDataException($"The UI {kind} key is missing or exceeds the source-key limit.");
    }

    private static void AddText(ref int total, string? value, UiCompileLimits limits)
    {
        if (string.IsNullOrEmpty(value))
            return;
        if (value.Length > limits.MaximumTextCharacters - total)
            throw new InvalidDataException("The UI source text limit was exceeded.");
        total += value.Length;
    }

    private static UiDefinitionHash ComputeHash(
        UiDefinitionKey key,
        ushort version,
        UiCompileLimits limits,
        List<UiCompiledLayoutNode> nodes,
        List<UiCompiledElement> elements,
        UiSlotDefinition[] slots,
        UiActionDefinition[] actions)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var stream = new UiCanonicalBufferWriter(buffer))
        {
            stream.WriteString(key.Value);
            stream.WriteUInt16(version);
            stream.WriteLimits(limits);
            stream.WriteInt32(nodes.Count);
            foreach (UiCompiledLayoutNode node in nodes)
            {
                stream.WriteInt32(node.Parent.Value);
                stream.WriteInt32(node.SiblingOrder);
                stream.WriteByte((byte)node.Kind);
                stream.WriteString(node.Key);
                stream.WriteChildLayout(node.Placement);
                stream.WriteFlow(node.Flow);
                stream.WriteCanvas(node.Canvas);
            }

            stream.WriteInt32(elements.Count);
            foreach (UiCompiledElement element in elements)
            {
                stream.WriteInt32(element.Parent.Value);
                stream.WriteInt32(element.SiblingOrder);
                stream.WriteByte((byte)element.Kind);
                stream.WriteString(element.Key);
                stream.WriteChildLayout(element.Placement);
                stream.WriteUInt16(element.Slot.Value);
                stream.WriteUInt16(element.Action.Value);
                stream.WriteString(element.StyleRole);
                stream.WriteString(element.AssetRole);
            }

            stream.WriteInt32(slots.Length);
            foreach (UiSlotDefinition slot in slots)
            {
                stream.WriteString(slot.Key);
                stream.WriteByte((byte)slot.ValueKind);
            }

            stream.WriteInt32(actions.Length);
            foreach (UiActionDefinition action in actions)
            {
                stream.WriteString(action.Key);
                stream.WriteBoolean(action.AcceptsRowContext);
                stream.WriteByte((byte)action.InputKind);
            }
        }

        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(buffer.WrittenSpan, digest);
        return UiDefinitionHash.FromDigest(digest);
    }
}

internal sealed class UiCanonicalBufferWriter : IDisposable
{
    private readonly ArrayBufferWriter<byte> _buffer;

    internal UiCanonicalBufferWriter(ArrayBufferWriter<byte> buffer) => _buffer = buffer;

    internal void WriteByte(byte value)
    {
        _buffer.GetSpan(1)[0] = value;
        _buffer.Advance(1);
    }

    internal void WriteBoolean(bool value) => WriteByte(value ? (byte)1 : (byte)0);

    internal void WriteUInt16(ushort value)
    {
        Span<byte> destination = _buffer.GetSpan(2);
        BinaryPrimitives.WriteUInt16LittleEndian(destination, value);
        _buffer.Advance(2);
    }

    internal void WriteUInt64(ulong value)
    {
        Span<byte> destination = _buffer.GetSpan(8);
        BinaryPrimitives.WriteUInt64LittleEndian(destination, value);
        _buffer.Advance(8);
    }

    internal void WriteInt32(int value)
    {
        Span<byte> destination = _buffer.GetSpan(4);
        BinaryPrimitives.WriteInt32LittleEndian(destination, value);
        _buffer.Advance(4);
    }

    internal void WriteSingle(float value) => WriteInt32(BitConverter.SingleToInt32Bits(value));

    internal void WriteString(string? value)
    {
        if (value is null)
        {
            WriteInt32(-1);
            return;
        }

        int byteCount = Encoding.UTF8.GetByteCount(value);
        WriteInt32(byteCount);
        Span<byte> destination = _buffer.GetSpan(byteCount);
        Encoding.UTF8.GetBytes(value, destination);
        _buffer.Advance(byteCount);
    }

    internal void WriteLimits(UiCompileLimits limits)
    {
        WriteInt32(limits.MaximumDepth);
        WriteInt32(limits.MaximumLayoutNodes);
        WriteInt32(limits.MaximumElements);
        WriteInt32(limits.MaximumSlots);
        WriteInt32(limits.MaximumActions);
        WriteInt32(limits.MaximumTextCharacters);
        WriteInt32(limits.MaximumRowsPerList);
    }

    internal void WriteChildLayout(UiChildLayout layout)
    {
        WriteByte((byte)layout.Sizing);
        WriteByte((byte)layout.Alignment);
        WriteBoolean(layout.HasCanvasPlacement);
        if (layout.HasCanvasPlacement)
            WriteCanvasPlacement(layout.CanvasPlacement);
    }

    internal void WriteFlow(UiFlowOptions flow)
    {
        WriteSingle(flow.Spacing);
        WritePadding(flow.Padding);
        WriteByte((byte)flow.Sizing);
        WriteByte((byte)flow.Alignment);
    }

    internal void WriteCanvas(UiCanvasOptions canvas)
    {
        WriteSingle(canvas.Width);
        WriteSingle(canvas.Height);
    }

    private void WritePadding(UiPadding padding)
    {
        WriteSingle(padding.Left);
        WriteSingle(padding.Top);
        WriteSingle(padding.Right);
        WriteSingle(padding.Bottom);
    }

    private void WriteCanvasPlacement(UiCanvasPlacement placement)
    {
        WriteSingle(placement.X);
        WriteSingle(placement.Y);
        WriteSingle(placement.Width);
        WriteSingle(placement.Height);
        WriteSingle(placement.AnchorMinX);
        WriteSingle(placement.AnchorMinY);
        WriteSingle(placement.AnchorMaxX);
        WriteSingle(placement.AnchorMaxY);
        WriteSingle(placement.PivotX);
        WriteSingle(placement.PivotY);
        WriteInt32(placement.ZOrder);
    }

    public void Dispose() { }
}
