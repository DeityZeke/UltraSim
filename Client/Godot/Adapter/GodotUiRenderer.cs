using Godot;
using UltraSim.Client.Core;
using UltraSim.UI;

namespace UltraSim.Client.Godot;

/// <summary>Optional renderer lookup for server-declared image asset roles.</summary>
public interface IGodotUiAssetResolver
{
    Texture2D? Resolve(string assetRole);
}

/// <summary>
/// Minimal Godot UI adapter. Godot controls stay entirely in this project;
/// Kernels/UI and Client/Core remain renderer-neutral.
/// </summary>
public sealed class GodotUiRenderer : IUiRenderer
{
    private readonly IGodotUiAssetResolver? _assets;
    private readonly Control? _host;
    private readonly List<GodotUiRenderHandle> _openHandles = [];

    public GodotUiRenderer(Control? host = null, IGodotUiAssetResolver? assets = null)
    {
        _host = host;
        _assets = assets;
    }

    public IUiRenderHandle Create(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        GodotUiDefinitionAdmission.Validate(definition);
        var handle = new GodotUiRenderHandle(instance, definition, model, submitAction, _assets);
        if (_host is not null)
        {
            _host.AddChild(handle.Root);
            handle.CenterInHost();
        }
        _openHandles.Add(handle);
        return handle;
    }

    public void Apply(
        IUiRenderHandle handle,
        CompiledUiDefinition definition,
        UiModelView previous,
        UiModelView current,
        UiModelDelta delta,
        Action<UiActionRequest> submitAction)
    {
        if (handle is not GodotUiRenderHandle godotHandle)
            throw new ArgumentException("The render handle was not created by GodotUiRenderer.", nameof(handle));
        godotHandle.Apply(current, delta, submitAction);
    }

    public void Close(IUiRenderHandle handle)
    {
        if (handle is not GodotUiRenderHandle godotHandle)
            throw new ArgumentException("The render handle was not created by GodotUiRenderer.", nameof(handle));
        _openHandles.Remove(godotHandle);
        godotHandle.Close();
    }

    /// <summary>
    /// Resolves gameplay input against the topmost open compiled UI without consuming native
    /// GUI delivery. A UI-surface result tells the caller to skip world input while still
    /// allowing Godot's later <c>_GuiInput</c> phase to operate normally.
    /// </summary>
    public GodotUiGameplayInputDisposition ResolveGameplayInput(InputEvent inputEvent)
    {
        ArgumentNullException.ThrowIfNull(inputEvent);
        if (TryGetPointerPosition(inputEvent, out Vector2 position))
        {
            for (int index = _openHandles.Count - 1; index >= 0; index--)
            {
                GodotUiRenderHandle handle = _openHandles[index];
                if (handle.ContainsUiSurface(position))
                    return GodotUiGameplayInputDisposition.UiSurface;
            }

            if (inputEvent is InputEventMouseButton
                {
                    ButtonIndex: MouseButton.Left,
                    Pressed: true,
                })
            {
                for (int index = _openHandles.Count - 1; index >= 0; index--)
                {
                    GodotUiRenderHandle handle = _openHandles[index];
                    if (handle.TrySubmitOutsideClose())
                        return GodotUiGameplayInputDisposition.OutsideCloseSubmitted;
                }
            }
        }

        foreach (GodotUiRenderHandle handle in _openHandles)
        {
            if (handle.OutsidePointerBehavior == UiOutsidePointerBehavior.Block)
                return GodotUiGameplayInputDisposition.Blocked;
        }
        return GodotUiGameplayInputDisposition.Gameplay;
    }

    /// <summary>Whether an explicitly modal view currently revokes held gameplay input.</summary>
    public bool BlocksGameplayInput
    {
        get
        {
            foreach (GodotUiRenderHandle handle in _openHandles)
                if (handle.OutsidePointerBehavior == UiOutsidePointerBehavior.Block)
                    return true;
            return false;
        }
    }

    private static bool TryGetPointerPosition(InputEvent inputEvent, out Vector2 position)
    {
        switch (inputEvent)
        {
            case InputEventMouse mouse:
                position = mouse.Position;
                return true;
            case InputEventScreenTouch touch:
                position = touch.Position;
                return true;
            case InputEventScreenDrag drag:
                position = drag.Position;
                return true;
            default:
                position = default;
                return false;
        }
    }
}

public enum GodotUiGameplayInputDisposition : byte
{
    Gameplay = 0,
    UiSurface = 1,
    Blocked = 2,
    OutsideCloseSubmitted = 3,
}

internal static class GodotUiDefinitionAdmission
{
    internal static void Validate(CompiledUiDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        foreach (UiCompiledLayoutNode node in definition.LayoutNodes)
        {
            _ = ToThemeConstant(node.Flow.Spacing);
            _ = ToThemeConstant(node.Flow.Padding.Left);
            _ = ToThemeConstant(node.Flow.Padding.Top);
            _ = ToThemeConstant(node.Flow.Padding.Right);
            _ = ToThemeConstant(node.Flow.Padding.Bottom);
        }
    }

    internal static int ToThemeConstant(float value)
    {
        try { return checked((int)MathF.Round(value)); }
        catch (OverflowException exception)
        {
            throw new InvalidDataException(
                "A compiled UI flow spacing or padding value cannot be represented by Godot.",
                exception);
        }
    }
}

/// <summary>Native control tree for one open compiled definition.</summary>
public sealed class GodotUiRenderHandle : IUiRenderHandle
{
    private readonly CompiledUiDefinition _definition;
    private readonly UiViewInstanceId _instance;
    private readonly IGodotUiAssetResolver? _assets;
    private readonly Dictionary<UiLayoutNodeId, Control> _layouts = [];
    private readonly Dictionary<UiLayoutNodeId, Control> _layoutHosts = [];
    private readonly Dictionary<UiElementId, Control> _elements = [];
    private readonly Dictionary<UiElementId, UiCompiledElement> _elementDefinitions = [];
    private readonly Dictionary<UiSlotId, List<UiElementId>> _elementsBySlot = [];
    private readonly Dictionary<UiActionId, List<UiElementId>> _elementsByAction = [];
    private readonly Dictionary<UiElementId, VBoxContainer> _lists = [];
    private readonly Dictionary<UiElementId, Dictionary<UiRowId, Control>> _listRows = [];
    private readonly Dictionary<UiActionId, UiActionState> _actionStates = [];
    private readonly Action<UiActionRequest> _submitAction;
    private bool _updating;
    private bool _outsideCloseSubmitted;

    public GodotUiRenderHandle(
        UiViewInstanceId instance,
        CompiledUiDefinition definition,
        UiModelView model,
        Action<UiActionRequest> submitAction,
        IGodotUiAssetResolver? assets)
    {
        _instance = instance;
        _definition = definition;
        _assets = assets;
        _submitAction = submitAction;
        Root = new Control
        {
            Name = $"UiView_{instance.Value}",
            MouseFilter = Control.MouseFilterEnum.Pass,
        };

        UiCompiledLayoutNode root = FindRootLayout();
        Root.Size = new Vector2(root.Canvas.Width, root.Canvas.Height);
        _layouts.Add(root.Id, Root);
        BuildChildren(root.Id, model, submitAction);
    }

    public Control Root { get; }

    internal UiOutsidePointerBehavior OutsidePointerBehavior =>
        _definition.OutsidePointerBehavior;

    internal bool ContainsUiSurface(Vector2 viewportPosition)
    {
        if (!GodotObject.IsInstanceValid(Root) || !Root.IsVisibleInTree())
            return false;
        foreach (Control control in _elements.Values)
        {
            if (GodotObject.IsInstanceValid(control) && control.IsVisibleInTree() &&
                control.GetGlobalRect().HasPoint(viewportPosition))
                return true;
        }
        return false;
    }

    internal bool TrySubmitOutsideClose()
    {
        if (_definition.OutsidePointerBehavior != UiOutsidePointerBehavior.Close ||
            !_definition.OutsideClickAction.IsValid || _outsideCloseSubmitted ||
            !IsActionEnabled(_definition.OutsideClickAction))
            return false;
        try
        {
            _submitAction(new UiActionRequest(
                _instance,
                _definition.Handle,
                _definition.OutsideClickAction));
            _outsideCloseSubmitted = true;
            return true;
        }
        catch
        {
            _outsideCloseSubmitted = false;
            throw;
        }
    }

    internal void Apply(UiModelView model, UiModelDelta delta, Action<UiActionRequest> submitAction)
    {
        _updating = true;
        try
        {
            foreach (UiScalarSlotPatch patch in delta.ScalarSlots)
            {
                if (!_elementsBySlot.TryGetValue(patch.Slot, out List<UiElementId>? targets))
                    continue;
                foreach (UiElementId id in targets)
                {
                    UiCompiledElement element = _elementDefinitions[id];
                    if (element.Kind == UiElementKind.List)
                        RebuildList(id, model, submitAction);
                    else
                        UpdateElement(id, element, model);
                }
            }

            foreach (UiListPatch patch in delta.Lists)
            {
                if (!_elementsBySlot.TryGetValue(patch.Slot, out List<UiElementId>? targets))
                    continue;
                foreach (UiElementId id in targets)
                {
                    UiCompiledElement element = _elementDefinitions[id];
                    if (element.Kind == UiElementKind.List)
                        ApplyListPatch(id, patch, model, submitAction);
                    else
                        UpdateElement(id, element, model);
                }
            }

            foreach (UiActionStatePatch patch in delta.Actions)
            {
                if (_elementsByAction.TryGetValue(patch.Action, out List<UiElementId>? targets))
                    foreach (UiElementId id in targets)
                        UpdateActionState(id, patch.State);
            }
        }
        finally
        {
            _updating = false;
        }
    }

    internal void Close()
    {
        if (GodotObject.IsInstanceValid(Root))
        {
            Root.GetParent()?.RemoveChild(Root);
            Root.QueueFree();
        }
    }

    internal void CenterInHost()
    {
        Root.AnchorLeft = 0.5f;
        Root.AnchorTop = 0.5f;
        Root.AnchorRight = 0.5f;
        Root.AnchorBottom = 0.5f;
        Root.OffsetLeft = -Root.Size.X * 0.5f;
        Root.OffsetTop = -Root.Size.Y * 0.5f;
        Root.OffsetRight = Root.Size.X * 0.5f;
        Root.OffsetBottom = Root.Size.Y * 0.5f;
    }

    private UiCompiledLayoutNode FindRootLayout()
    {
        foreach (UiCompiledLayoutNode node in _definition.LayoutNodes)
        {
            if (!node.Parent.IsValid)
                return node;
        }
        throw new InvalidDataException("A compiled UI definition has no root layout.");
    }

    private void BuildChildren(
        UiLayoutNodeId parent,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        Control parentControl = _layouts[parent];
        Control host = _layoutHosts.TryGetValue(parent, out Control? content)
            ? content
            : parentControl;

        var children = new List<UiChild>(
            _definition.LayoutNodeCount + _definition.ElementCount);
        foreach (UiCompiledLayoutNode node in _definition.LayoutNodes)
        {
            if (node.Parent == parent)
                children.Add(UiChild.ForLayout(node.SiblingOrder, node));
        }
        foreach (UiCompiledElement element in _definition.Elements)
        {
            if (element.Parent == parent)
                children.Add(UiChild.ForElement(element.SiblingOrder, element));
        }
        children.Sort(static (left, right) => left.Order.CompareTo(right.Order));

        foreach (UiChild child in children)
        {
            if (child.Layout.HasValue)
            {
                UiCompiledLayoutNode node = child.Layout.Value;
                Control control = CreateLayout(node);
                ApplyChildLayout(parent, control, node.Placement);
                host.AddChild(control);
                _layouts.Add(node.Id, control);
                BuildChildren(node.Id, model, submitAction);
            }
            else
            {
                UiCompiledElement element = child.Element;
                Control control = CreateElement(element, model, submitAction);
                ApplyChildLayout(parent, control, element.Placement);
                host.AddChild(control);
                _elements.Add(element.Id, control);
                _elementDefinitions.Add(element.Id, element);
                if (element.Slot.IsValid)
                {
                    if (!_elementsBySlot.TryGetValue(element.Slot, out List<UiElementId>? slots))
                        _elementsBySlot.Add(element.Slot, slots = []);
                    slots.Add(element.Id);
                }
                if (element.Action.IsValid)
                {
                    if (!_elementsByAction.TryGetValue(element.Action, out List<UiElementId>? actions))
                        _elementsByAction.Add(element.Action, actions = []);
                    actions.Add(element.Id);
                }
                if (element.Action.IsValid)
                    UpdateActionState(element.Id, model.GetActionState(element.Action));
                if (element.Kind == UiElementKind.List && control is VBoxContainer list)
                {
                    _lists.Add(element.Id, list);
                    _listRows.Add(element.Id, []);
                    RebuildList(element.Id, model, submitAction);
                }
            }
        }
    }

    private Control CreateLayout(UiCompiledLayoutNode node)
    {
        Control control = node.Kind switch
        {
            UiLayoutKind.Vertical => new VBoxContainer(),
            UiLayoutKind.Horizontal => new HBoxContainer(),
            UiLayoutKind.Overlay => new Control(),
            UiLayoutKind.Scroll => new ScrollContainer(),
            _ => throw new InvalidDataException($"Unsupported non-root UI layout '{node.Kind}'."),
        };
        control.Name = $"Layout_{node.Key}";
        if (control is VBoxContainer vertical)
            vertical.AddThemeConstantOverride("separation", GodotUiDefinitionAdmission.ToThemeConstant(node.Flow.Spacing));
        if (control is HBoxContainer horizontal)
            horizontal.AddThemeConstantOverride("separation", GodotUiDefinitionAdmission.ToThemeConstant(node.Flow.Spacing));
        if (control is ScrollContainer scroll)
        {
            var content = new VBoxContainer
            {
                Name = $"ScrollContent_{node.Key}",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            };
            content.AddThemeConstantOverride("separation", GodotUiDefinitionAdmission.ToThemeConstant(node.Flow.Spacing));
            UiPadding scrollPadding = node.Flow.Padding;
            if (scrollPadding.Left == 0 && scrollPadding.Top == 0 &&
                scrollPadding.Right == 0 && scrollPadding.Bottom == 0)
            {
                scroll.AddChild(content);
            }
            else
            {
                var scrollMargin = CreateMargin($"ScrollPadding_{node.Key}", scrollPadding);
                scrollMargin.AddChild(content);
                scroll.AddChild(scrollMargin);
            }
            _layoutHosts.Add(node.Id, content);
        }
        UiPadding padding = node.Flow.Padding;
        if (padding.Left == 0 && padding.Top == 0 && padding.Right == 0 && padding.Bottom == 0)
            return control;
        if (node.Kind == UiLayoutKind.Scroll)
            return control;

        MarginContainer margin = CreateMargin($"Padding_{node.Key}", padding);
        margin.AddChild(control);
        _layoutHosts[node.Id] = control;
        return margin;
    }

    private static MarginContainer CreateMargin(string name, UiPadding padding)
    {
        var margin = new MarginContainer { Name = name };
        margin.AddThemeConstantOverride("margin_left", GodotUiDefinitionAdmission.ToThemeConstant(padding.Left));
        margin.AddThemeConstantOverride("margin_top", GodotUiDefinitionAdmission.ToThemeConstant(padding.Top));
        margin.AddThemeConstantOverride("margin_right", GodotUiDefinitionAdmission.ToThemeConstant(padding.Right));
        margin.AddThemeConstantOverride("margin_bottom", GodotUiDefinitionAdmission.ToThemeConstant(padding.Bottom));
        return margin;
    }

    private Control CreateElement(
        UiCompiledElement element,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        Control control = element.Kind switch
        {
            UiElementKind.Panel => new Panel(),
            UiElementKind.Text => new Label
            {
                AutowrapMode = TextServer.AutowrapMode.WordSmart,
            },
            UiElementKind.Image => CreateImage(element),
            UiElementKind.Button => new Button(),
            UiElementKind.TextInput => new LineEdit(),
            UiElementKind.Toggle => new CheckBox(),
            UiElementKind.List => new VBoxContainer(),
            _ => throw new InvalidDataException($"Unsupported UI element '{element.Kind}'."),
        };
        control.Name = $"Element_{element.Key}";
        if (!string.IsNullOrEmpty(element.StyleRole))
            control.ThemeTypeVariation = element.StyleRole;
        UpdateElementValue(control, element, model);
        if (element.Action.IsValid)
            ConnectAction(control, element, submitAction);
        else
        {
            if (control is LineEdit displayText)
                displayText.Editable = false;
            if (control is CheckBox displayToggle)
                displayToggle.Disabled = true;
        }
        return control;
    }

    private Control CreateImage(UiCompiledElement element)
    {
        var image = new TextureRect
        {
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
        };
        if (!string.IsNullOrEmpty(element.AssetRole))
            image.Texture = _assets?.Resolve(element.AssetRole) ??
                GodotPrimitiveSubstrate.MissingIcon;
        return image;
    }

    private void ConnectAction(Control control, UiCompiledElement element, Action<UiActionRequest> submitAction)
    {
        switch (control)
        {
            case CheckBox toggle:
                toggle.Toggled += pressed =>
                {
                    if (!_updating)
                        Submit(element, UiValue.FromBoolean(pressed), submitAction);
                };
                break;
            case Button button:
                button.Pressed += () => Submit(element, UiValue.Unset, submitAction);
                break;
            case LineEdit input:
                input.TextChanged += text =>
                {
                    if (!_updating)
                        Submit(element, UiValue.FromText(text), submitAction);
                };
                break;
        }
    }

    private void Submit(
        UiCompiledElement element,
        UiValue input,
        Action<UiActionRequest> submitAction,
        UiRowId row = default)
    {
        if (_updating)
            return;
        submitAction(new UiActionRequest(
            _instance,
            _definition.Handle,
            element.Action,
            row,
            input,
            source: element.Id));
    }

    private void UpdateElement(UiElementId id, UiCompiledElement element, UiModelView model)
    {
        if (_elements.TryGetValue(id, out Control? control))
            UpdateElementValue(control, element, model);
    }

    private static void UpdateElementValue(Control control, UiCompiledElement element, UiModelView model)
    {
        if (!element.Slot.IsValid)
            return;
        UiValue value = model.Get(element.Slot);
        switch (control)
        {
            case Label label:
                label.Text = Format(value);
                break;
            case CheckBox toggle:
                // Unset has the same visual meaning as a fresh unset Boolean projection. Always
                // assign the retained state so true -> Unset cannot leave a stale pressed toggle.
                toggle.ButtonPressed = value.Kind == UiValueKind.Boolean && value.Boolean;
                break;
            case Button button:
                button.Text = Format(value);
                break;
            case LineEdit input:
                input.Text = Format(value);
                break;
        }
    }

    private void RebuildList(
        UiElementId id,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        if (!_lists.TryGetValue(id, out VBoxContainer? list) ||
            !_elementDefinitions.TryGetValue(id, out UiCompiledElement element) ||
            !_listRows.TryGetValue(id, out Dictionary<UiRowId, Control>? rows))
            return;
        foreach (Node child in list.GetChildren())
        {
            list.RemoveChild(child);
            child.QueueFree();
        }
        rows.Clear();
        foreach (UiListRow row in model.GetRows(element.Slot))
        {
            Control rowControl = CreateRowControl(element, row, submitAction);
            list.AddChild(rowControl);
            rows.Add(row.Id, rowControl);
        }
    }

    private void ApplyListPatch(
        UiElementId id,
        UiListPatch patch,
        UiModelView model,
        Action<UiActionRequest> submitAction)
    {
        if (patch.Kind == UiListDeltaKind.Replace)
        {
            RebuildList(id, model, submitAction);
            return;
        }
        if (!_lists.TryGetValue(id, out VBoxContainer? list) ||
            !_listRows.TryGetValue(id, out Dictionary<UiRowId, Control>? rows) ||
            !_elementDefinitions.TryGetValue(id, out UiCompiledElement element))
            return;

        switch (patch.Kind)
        {
            case UiListDeltaKind.Add:
                {
                    Control rowControl = CreateRowControl(element, patch.Row, submitAction);
                    list.AddChild(rowControl);
                    rows.Add(patch.RowId, rowControl);
                    int targetIndex = 0;
                    foreach (UiListRow row in model.GetRows(patch.Slot))
                    {
                        if (row.Id == patch.RowId)
                            break;
                        targetIndex++;
                    }
                    list.MoveChild(rowControl, targetIndex);
                    break;
                }
            case UiListDeltaKind.Update:
                if (rows.TryGetValue(patch.RowId, out Control? existing))
                    UpdateRowControl(existing, patch.Row);
                break;
            case UiListDeltaKind.Remove:
                if (rows.Remove(patch.RowId, out Control? removed))
                {
                    list.RemoveChild(removed);
                    removed.QueueFree();
                }
                break;
        }
    }

    private Control CreateRowControl(
        UiCompiledElement element,
        UiListRow row,
        Action<UiActionRequest> submitAction)
    {
        var rowControl = new HBoxContainer
        {
            Name = $"Row_{row.Id.Value}",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        foreach (UiValue value in row.Values)
        {
            rowControl.AddChild(new Label
            {
                Text = Format(value),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
        }
        if (element.Action.IsValid)
        {
            rowControl.GuiInput += inputEvent =>
            {
                if (!_updating && IsActionEnabled(element.Action) &&
                    inputEvent is InputEventMouseButton
                    {
                        ButtonIndex: MouseButton.Left,
                        Pressed: true,
                    })
                {
                    Submit(element, UiValue.Unset, submitAction, row.Id);
                    rowControl.AcceptEvent();
                }
            };
        }
        return rowControl;
    }

    private static void UpdateRowControl(Control control, UiListRow row)
    {
        foreach (Node child in control.GetChildren())
        {
            control.RemoveChild(child);
            child.QueueFree();
        }
        foreach (UiValue value in row.Values)
        {
            control.AddChild(new Label
            {
                Text = Format(value),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            });
        }
    }

    private void UpdateActionState(UiElementId id, UiActionState state)
    {
        if (!_elements.TryGetValue(id, out Control? control) ||
            !_elementDefinitions.TryGetValue(id, out UiCompiledElement element))
            return;
        _actionStates[element.Action] = state;
        if (control is BaseButton button)
        {
            button.Disabled = !state.Enabled;
            button.TooltipText = state.UnavailableReason ?? string.Empty;
        }
        else if (control is LineEdit input)
        {
            input.Editable = state.Enabled;
            input.TooltipText = state.UnavailableReason ?? string.Empty;
        }
        else
        {
            control.TooltipText = state.UnavailableReason ?? string.Empty;
        }
    }

    private bool IsActionEnabled(UiActionId action) =>
        !_actionStates.TryGetValue(action, out UiActionState state) || state.Enabled;

    private static string Format(UiValue value) => value.Kind switch
    {
        UiValueKind.Text => value.Text ?? string.Empty,
        UiValueKind.Number => value.Number.ToString(System.Globalization.CultureInfo.InvariantCulture),
        UiValueKind.Boolean => value.Boolean ? "true" : "false",
        _ => string.Empty,
    };

    private static void ApplyPlacement(Control control, UiChildLayout placement)
    {
        if (!placement.HasCanvasPlacement)
            return;
        UiCanvasPlacement rect = placement.CanvasPlacement;
        control.AnchorLeft = rect.AnchorMinX;
        control.AnchorTop = rect.AnchorMinY;
        control.AnchorRight = rect.AnchorMaxX;
        control.AnchorBottom = rect.AnchorMaxY;
        control.OffsetLeft = rect.X;
        control.OffsetTop = rect.Y;
        control.OffsetRight = rect.X + rect.Width;
        control.OffsetBottom = rect.Y + rect.Height;
        control.PivotOffset = new Vector2(rect.Width * rect.PivotX, rect.Height * rect.PivotY);
        control.ZIndex = rect.ZOrder;
    }

    private void ApplyChildLayout(UiLayoutNodeId parent, Control control, UiChildLayout placement)
    {
        UiCompiledLayoutNode parentNode = _definition.LayoutNodes[parent.Value];
        if (parentNode.Kind == UiLayoutKind.Overlay && !placement.HasCanvasPlacement)
        {
            control.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
            return;
        }
        if (!placement.HasCanvasPlacement && placement.Equals(default(UiChildLayout)))
            placement = UiChildLayout.Flow(parentNode.Flow.Sizing, parentNode.Flow.Alignment);
        ApplyPlacement(control, placement);
        ApplyFlowFlags(control, placement);
    }

    private static void ApplyFlowFlags(Control control, UiChildLayout placement)
    {
        if (placement.HasCanvasPlacement)
            return;
        Control.SizeFlags horizontal = placement.Sizing == UiSizing.Fill
            ? Control.SizeFlags.ExpandFill
            : placement.Alignment switch
            {
                UiAlignment.Center => Control.SizeFlags.ShrinkCenter,
                UiAlignment.End => Control.SizeFlags.ShrinkEnd,
                UiAlignment.Stretch => Control.SizeFlags.Fill,
                _ => Control.SizeFlags.ShrinkBegin,
            };
        Control.SizeFlags vertical = placement.Sizing == UiSizing.Fill
            ? Control.SizeFlags.ExpandFill
            : placement.Alignment switch
            {
                UiAlignment.Center => Control.SizeFlags.ShrinkCenter,
                UiAlignment.End => Control.SizeFlags.ShrinkEnd,
                UiAlignment.Stretch => Control.SizeFlags.Fill,
                _ => Control.SizeFlags.ShrinkBegin,
            };
        control.SizeFlagsHorizontal = horizontal;
        control.SizeFlagsVertical = vertical;
    }

    private readonly struct UiChild
    {
        private UiChild(int order, UiCompiledLayoutNode? layout, UiCompiledElement element)
        {
            Order = order;
            Layout = layout;
            Element = element;
        }

        internal int Order { get; }
        internal UiCompiledLayoutNode? Layout { get; }
        internal UiCompiledElement Element { get; }

        internal static UiChild ForLayout(int order, UiCompiledLayoutNode node) =>
            new(order, node, default);

        internal static UiChild ForElement(int order, UiCompiledElement element) =>
            new(order, null, element);
    }
}
