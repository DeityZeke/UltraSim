using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.UI;

/// <summary>Executable headless acceptance for S0.4 native control behavior.</summary>
public partial class S04HeadlessAcceptance : Control
{
    public override void _Ready()
    {
        try
        {
            RunAcceptance();
            GD.Print("S0.4 GODOT ACCEPTANCE PASS");
            GetTree().Quit(0);
        }
        catch (Exception exception)
        {
            GD.PushError($"S0.4 GODOT ACCEPTANCE FAIL: {exception}");
            GetTree().Quit(1);
        }
    }

    private void RunAcceptance()
    {
        var builder = new UiDefinitionBuilder("s0.4.headless");
        UiSlotReference title = builder.DeclareSlot("title", UiSlotValueKind.Text);
        UiSlotReference rows = builder.DeclareSlot("rows", UiSlotValueKind.List);
        UiSlotReference enabled = builder.DeclareSlot("enabled", UiSlotValueKind.Boolean);
        UiActionReference selectAction = builder.DeclareAction(
            "select", acceptsRowContext: true);
        UiActionReference toggleAction = builder.DeclareAction(
            "toggle", inputKind: UiValueKind.Boolean);
        UiActionReference editAction = builder.DeclareAction(
            "edit", inputKind: UiValueKind.Text);
        UiActionReference closeAction = builder.DeclareAction("close");
        builder.AddCanvas(new UiCanvasOptions(320, 240), canvas =>
        {
            canvas.AddPanel("background", "surface",
                UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 320, 240, zOrder: 0)));
            canvas.AddVertical("content", vertical =>
            {
                vertical.AddText("title", title);
                vertical.AddList("rows", rows, selectAction);
                vertical.AddHorizontal("actions", horizontal =>
                {
                    horizontal.AddToggle("toggle", enabled, toggleAction);
                    horizontal.AddTextInput("input", title, editAction);
                    horizontal.AddButton("button", closeAction, title);
                });
                vertical.AddToggle("display-toggle", enabled);
                vertical.AddTextInput("display-input", title);
                vertical.AddImage("image", "missing.test.asset");
                vertical.AddOverlay("overlay", overlay =>
                {
                    overlay.AddPanel("overlay-panel", "surface");
                    overlay.AddText("overlay-title", title);
                });
                vertical.AddScroll(
                    "scroll",
                    scroll => scroll.AddText("scroll-title", title),
                    flow: new UiFlowOptions(spacing: 9, padding: new UiPadding(2, 3, 4, 5)));
            }, UiChildLayout.Canvas(new UiCanvasPlacement(10, 10, 300, 220, zOrder: 1)),
                new UiFlowOptions(spacing: 7));
        });

        CompiledUiDefinition definition = UiDefinitionCompiler.Compile(builder);
        UiSlotId titleId = definition.Prepare(title);
        UiSlotId rowsId = definition.Prepare(rows);
        var model = new UiModel(definition);
        model.SetText(titleId, "Before");
        model.SetBoolean(definition.Prepare(enabled), true);
        model.ReplaceRows(rowsId, new[]
        {
            Row(1, "A"),
            Row(2, "B"),
        });

        var client = new UiClientRuntime(new GodotUiRenderer(this));
        var link = new LocalSemanticLink(client);
        UiActionRequest? submitted = null;
        link.ActionSubmitted += request => submitted = request;
        UiViewInstanceId instance = link.OpenView(new UiOpenView(
            new UiDefinitionCacheKey(
                "local", "s0.4", 1, definition.Key, definition.Version, definition.Hash),
            definition,
            model));

        Control root = GetNode<Control>($"UiView_{instance.Value}");
        Label label = FindDescendant<Label>(root, "Element_title");
        VBoxContainer list = FindDescendant<VBoxContainer>(root, "Element_rows");
        CheckBox toggle = FindDescendant<CheckBox>(root, "Element_toggle");
        LineEdit input = FindDescendant<LineEdit>(root, "Element_input");
        LineEdit displayInput = FindDescendant<LineEdit>(root, "Element_display-input");
        CheckBox displayToggle = FindDescendant<CheckBox>(root, "Element_display-toggle");
        Button button = FindDescendant<Button>(root, "Element_button");
        _ = FindDescendant<TextureRect>(root, "Element_image");
        Panel background = FindDescendant<Panel>(root, "Element_background");
        VBoxContainer content = FindDescendant<VBoxContainer>(root, "Layout_content");
        Panel overlayPanel = FindDescendant<Panel>(root, "Element_overlay-panel");
        ScrollContainer scroll = FindDescendant<ScrollContainer>(root, "Layout_scroll");
        Expect(label.Text == "Before", "Cold-open Label value was not applied.");
        Expect(list.GetChildCount() == 2, "Cold-open list rows were not built.");
        Expect(input.Text == "Before" && button.Text == "Before",
            "TextInput or Button did not receive its initial slot value.");
        Expect(background.ZIndex == 0 && content.ZIndex == 1 &&
            content.OffsetLeft == 10 && content.OffsetTop == 10 &&
            content.OffsetRight == 310 && content.OffsetBottom == 230,
            "Canvas placement or z-order was not preserved by native controls.");
        Expect(overlayPanel.AnchorRight == 1 && overlayPanel.AnchorBottom == 1,
            "Overlay child did not receive shared full bounds.");
        Expect(scroll.FindChild("ScrollPadding_scroll", recursive: false, owned: false) is MarginContainer,
            "Scroll padding was not retained inside the scroll content host.");
        VBoxContainer scrollContent = FindDescendant<VBoxContainer>(root, "ScrollContent_scroll");
        Expect(content.GetThemeConstant("separation") == 7 &&
            scrollContent.GetThemeConstant("separation") == 9,
            "Flow spacing was not applied to ordinary and scroll content hosts.");
        Expect(background.ThemeTypeVariation == "surface",
            "Compiled StyleRole was not consumed by the native renderer.");
        Expect(!displayInput.Editable && displayToggle.Disabled,
            "Display-only inputs remained locally editable.");

        link.UpdateView(new UiUpdateView(instance, new UiModelDelta(
            definition.Handle,
            1,
            scalarSlots: new[]
            {
                new UiScalarSlotPatch(titleId, UiValue.FromText("After")),
            },
            lists: new[]
            {
                UiListPatch.Update(rowsId, Row(1, "A2", "extra")),
                UiListPatch.Remove(rowsId, new UiRowId(2)),
                UiListPatch.Add(rowsId, Row(3, "C")),
            })));

        Expect(ReferenceEquals(root, GetNode<Control>($"UiView_{instance.Value}")),
            "A model delta rebuilt the root control tree.");
        Expect(ReferenceEquals(label, FindDescendant<Label>(root, "Element_title")),
            "A scalar model delta replaced its target control.");
        Expect(label.Text == "After", "Scalar model delta did not patch Label text.");
        Expect(list.GetChildCount() == 2, "Scoped list patches produced the wrong row count.");
        Node updatedRow = list.GetNode("Row_1");
        Expect(updatedRow.GetChildCount() == 2,
            "Updating a list row did not reconcile its changed cell count.");

        submitted = null;
        var click = new InputEventMouseButton
        {
            ButtonIndex = MouseButton.Left,
            Pressed = true,
        };
        updatedRow.EmitSignal(Control.SignalName.GuiInput, click);
        Expect(submitted.HasValue, "Native list-row input did not submit action intent.");
        UiActionRequest rowAction = submitted.GetValueOrDefault();
        Expect(rowAction.Action == definition.Prepare(selectAction) && rowAction.Row == new UiRowId(1),
            "Native list row submitted the wrong action or row identity.");
        Expect(rowAction.Source.IsValid,
            "Native list-row action omitted compiled source provenance.");

        link.UpdateView(new UiUpdateView(instance, new UiModelDelta(
            definition.Handle,
            2,
            actions: new[]
            {
                new UiActionStatePatch(
                    definition.Prepare(selectAction),
                    new UiActionState(false, "Unavailable")),
            })));
        submitted = null;
        updatedRow.EmitSignal(Control.SignalName.GuiInput, click);
        Expect(!submitted.HasValue, "A disabled native list action still emitted intent.");

        submitted = null;
        toggle.EmitSignal(BaseButton.SignalName.Toggled, false);
        Expect(submitted.HasValue, "Native Toggle input did not submit typed action intent.");
        UiActionRequest action = submitted.GetValueOrDefault();
        Expect(action.Action == definition.Prepare(toggleAction),
            "Native Toggle submitted the wrong action.");
        Expect(action.Input.Kind == UiValueKind.Boolean && !action.Input.Boolean,
            "Native Toggle did not submit Boolean input.");

        submitted = null;
        input.EmitSignal(LineEdit.SignalName.TextChanged, "Typed");
        Expect(submitted.HasValue &&
            submitted.Value.Action == definition.Prepare(editAction) &&
            submitted.Value.Input.Kind == UiValueKind.Text &&
            submitted.Value.Input.Text == "Typed",
            "Native TextInput did not submit Text input.");

        submitted = null;
        button.EmitSignal(BaseButton.SignalName.Pressed);
        Expect(submitted.HasValue &&
            submitted.Value.Action == definition.Prepare(closeAction) &&
            submitted.Value.Input.Kind == UiValueKind.Unset,
            "Native Button did not submit its action intent.");

        Expect(link.CloseView(new UiCloseView(instance)), "Open view did not close.");
        Expect(root.GetParent() is null, "Closed root remained attached to the scene tree.");
    }

    private static UiListRow Row(ulong id, params string[] values)
    {
        var cells = new UiValue[values.Length];
        for (int index = 0; index < values.Length; index++)
            cells[index] = UiValue.FromText(values[index]);
        return new UiListRow(new UiRowId(id), cells);
    }

    private static T FindDescendant<T>(Node root, string name)
        where T : Node
    {
        T? node = root.FindChild(name, recursive: true, owned: false) as T;
        return node ?? throw new InvalidOperationException($"Native control '{name}' was not found.");
    }

    private static void Expect(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
    }
}
