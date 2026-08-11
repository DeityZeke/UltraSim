using Godot;
using UltraSim.Client.UI;
using UltraSim.UI;

namespace UltraSim.Client.Godot.UI;

internal static class ServerDrivenPanelRenderer
{
    public static bool TryBuild(
        ClientUiPanelInstance panel,
        Action<UiActionDescriptor, string?>? actionPressed,
        out Control root,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(panel);

        var nodeById = new Dictionary<int, UiSchemaNodeDescriptor>(panel.Schema.Nodes.Count);
        for (int i = 0; i < panel.Schema.Nodes.Count; i++)
        {
            UiSchemaNodeDescriptor node = panel.Schema.Nodes[i];
            nodeById[node.NodeId] = node;
        }

        var modelBySlot = new Dictionary<string, UiModelSlotValue>(panel.Model.Values.Count, StringComparer.Ordinal);
        for (int i = 0; i < panel.Model.Values.Count; i++)
        {
            UiModelSlotValue value = panel.Model.Values[i];
            modelBySlot[value.SlotId] = value;
        }

        if (!TryBuildNode(panel.Schema.RootNodeId, panel.CompatibilityContext, nodeById, modelBySlot, panel.RepeaterRows, actionPressed, out root!, out error))
        {
            root = new Control();
            return false;
        }

        root.Name = MakeNodeName("ServerPanel", panel.PanelInstanceId);
        root.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
        root.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        root.SizeFlagsVertical = Control.SizeFlags.ShrinkBegin;
        return true;
    }

    private static bool TryBuildNode(
        int nodeId,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<int, UiSchemaNodeDescriptor> nodeById,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ClientUiRepeaterRowState>> repeaterRows,
        Action<UiActionDescriptor, string?>? actionPressed,
        out Control control,
        out string error)
    {
        if (!nodeById.TryGetValue(nodeId, out UiSchemaNodeDescriptor? node))
        {
            control = new Control();
            error = $"UI schema node {nodeId} is missing.";
            return false;
        }

        control = node.Kind switch
        {
            UiNodeKind.Panel => CreatePanel(node),
            UiNodeKind.Row => CreateRow(node),
            UiNodeKind.Pager => CreatePager(node),
            UiNodeKind.Column => CreateColumn(node),
            UiNodeKind.Group => CreateGroup(node),
            UiNodeKind.Section => CreateSection(node),
            UiNodeKind.ScrollRegion => CreateScrollRegion(node),
            UiNodeKind.Tabs => CreateTabs(node),
            UiNodeKind.Text => CreateText(node),
            UiNodeKind.DynamicText => CreateDynamicText(node, modelBySlot),
            UiNodeKind.Image => CreateImage(node),
            UiNodeKind.Button => CreateButton(node, compatibilityContext, actionPressed),
            UiNodeKind.IconButton => CreateButton(node, compatibilityContext, actionPressed),
            UiNodeKind.Separator => CreateSeparator(node),
            UiNodeKind.Spacer => CreateSpacer(node),
            UiNodeKind.TextInput => CreateTextInput(node, modelBySlot),
            UiNodeKind.NumberInput => CreateNumberInput(node, compatibilityContext, modelBySlot),
            UiNodeKind.Toggle => CreateToggle(node, modelBySlot),
            UiNodeKind.Slider => CreateSlider(node, compatibilityContext, modelBySlot),
            UiNodeKind.Stepper => CreateStepper(node, compatibilityContext, modelBySlot),
            UiNodeKind.Select => CreateSelect(node, compatibilityContext, modelBySlot),
            UiNodeKind.RadioGroup => CreateRadioGroup(node, compatibilityContext, modelBySlot),
            UiNodeKind.CheckboxGroup => CreateCheckboxGroup(node, compatibilityContext, modelBySlot),
            UiNodeKind.Repeater => CreateRepeater(node, compatibilityContext, repeaterRows, actionPressed),
            UiNodeKind.Table => CreateTable(node, compatibilityContext, repeaterRows, actionPressed),
            _ => CreateUnsupportedPlaceholder(node),
        };

        if (node.Children.Count > 0 && control is Label or BaseButton or LineEdit or HSeparator or TextureRect or GridContainer)
        {
            error = $"UI node kind '{node.Kind}' cannot host child controls in the minimal Godot renderer.";
            return false;
        }

        Control childHost = control;
        if (control is PanelContainer panel)
        {
            var content = new VBoxContainer
            {
                Name = "Content",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            };
            panel.AddChild(content);
            childHost = content;
        }
        else if (control is ScrollContainer scroll)
        {
            var content = new VBoxContainer
            {
                Name = "Content",
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            };
            scroll.AddChild(content);
            childHost = content;
        }

        IReadOnlyList<UiSelectOptionDescriptor>? tabOptions = null;
        int selectedTabIndex = -1;
        if (control is TabContainer &&
            !string.IsNullOrWhiteSpace(node.SlotId) &&
            compatibilityContext.TryGetSelectOptions(node.SlotId, out IReadOnlyList<UiSelectOptionDescriptor> options))
        {
            tabOptions = options;
            string selectedTabId = GetStringModelValue(node, modelBySlot);
            for (int i = 0; i < options.Count; i++)
            {
                if (selectedTabIndex < 0 && string.Equals(options[i].Value, selectedTabId, StringComparison.Ordinal))
                {
                    selectedTabIndex = i;
                }
            }
        }

        for (int i = 0; i < node.Children.Count; i++)
        {
            if (!TryBuildNode(
                node.Children[i],
                compatibilityContext,
                nodeById,
                modelBySlot,
                repeaterRows,
                actionPressed,
                out Control child,
                out error))
            {
                control.QueueFree();
                return false;
            }

            childHost.AddChild(child);
            if (control is TabContainer tabs && tabOptions is not null && i < tabOptions.Count)
            {
                tabs.SetTabTitle(i, tabOptions[i].Label);
                tabs.SetTabDisabled(i, tabOptions[i].Disabled);
            }
        }

        if (control is TabContainer tabContainer && selectedTabIndex >= 0)
        {
            tabContainer.CurrentTab = selectedTabIndex;
        }

        error = string.Empty;
        return true;
    }

    private static PanelContainer CreatePanel(UiSchemaNodeDescriptor node)
    {
        var panel = new PanelContainer
        {
            Name = MakeNodeName("Panel", node.NodeId),
            MouseFilter = Control.MouseFilterEnum.Stop,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            CustomMinimumSize = new Vector2(560.0f, 0.0f),
        };
        panel.AddThemeConstantOverride("margin_left", 10);
        panel.AddThemeConstantOverride("margin_top", 10);
        panel.AddThemeConstantOverride("margin_right", 10);
        panel.AddThemeConstantOverride("margin_bottom", 10);
        return panel;
    }

    private static HBoxContainer CreateRow(UiSchemaNodeDescriptor node)
    {
        return new HBoxContainer
        {
            Name = MakeNodeName("Row", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static HBoxContainer CreatePager(UiSchemaNodeDescriptor node)
    {
        return new HBoxContainer
        {
            Name = MakeNodeName("Pager", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static VBoxContainer CreateColumn(UiSchemaNodeDescriptor node)
    {
        return new VBoxContainer
        {
            Name = MakeNodeName("Column", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static VBoxContainer CreateGroup(UiSchemaNodeDescriptor node)
    {
        var group = new VBoxContainer
        {
            Name = MakeNodeName("Group", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        AddTitleLabelIfPresent(group, node, "GroupTitle");
        return group;
    }

    private static VBoxContainer CreateSection(UiSchemaNodeDescriptor node)
    {
        var section = new VBoxContainer
        {
            Name = MakeNodeName("Section", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        AddTitleLabelIfPresent(section, node, "SectionTitle");
        section.AddChild(new HSeparator
        {
            Name = MakeNodeName("SectionSeparator", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        return section;
    }

    private static ScrollContainer CreateScrollRegion(UiSchemaNodeDescriptor node)
    {
        return new ScrollContainer
        {
            Name = MakeNodeName("ScrollRegion", node.NodeId),
            CustomMinimumSize = new Vector2(320.0f, 180.0f),
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled,
            VerticalScrollMode = ScrollContainer.ScrollMode.Auto,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
    }

    private static TabContainer CreateTabs(UiSchemaNodeDescriptor node)
    {
        var tabs = new TabContainer
        {
            Name = MakeNodeName("Tabs", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };

        SetSlotMeta(tabs, node);
        return tabs;
    }

    private static Label CreateText(UiSchemaNodeDescriptor node)
    {
        return new Label
        {
            Name = MakeNodeName("Text", node.NodeId),
            Text = node.Text ?? string.Empty,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static Label CreateDynamicText(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        string text = node.Text ?? string.Empty;
        string? slotId = node.SlotId;
        if (!string.IsNullOrWhiteSpace(slotId)
            && modelBySlot.TryGetValue(slotId, out UiModelSlotValue? value)
            && value is not null)
        {
            text = FormatModelValue(value);
        }

        return new Label
        {
            Name = MakeNodeName("DynamicText", node.NodeId),
            Text = text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static TextureRect CreateImage(UiSchemaNodeDescriptor node)
    {
        var image = new TextureRect
        {
            Name = MakeNodeName("Image", node.NodeId),
            CustomMinimumSize = new Vector2(128.0f, 72.0f),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        if (!string.IsNullOrWhiteSpace(node.Text))
        {
            image.SetMeta("UiImageId", node.Text);
            if (node.Text.StartsWith("res://", StringComparison.Ordinal) ||
                node.Text.StartsWith("user://", StringComparison.Ordinal))
            {
                image.Texture = GD.Load<Texture2D>(node.Text);
            }
        }

        return image;
    }

    private static LineEdit CreateTextInput(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        var input = new LineEdit
        {
            Name = MakeNodeName("TextInput", node.NodeId),
            Text = GetStringModelValue(node, modelBySlot),
            PlaceholderText = node.Text ?? string.Empty,
            Secret = node.InputSecurity == UiInputSecurity.TrustedCredential,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        SetSlotMeta(input, node);
        if (node.InputSecurity == UiInputSecurity.TrustedCredential)
        {
            input.SetMeta("UiTrustedInput", true);
        }

        return input;
    }

    private static CheckBox CreateToggle(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        var toggle = new CheckBox
        {
            Name = MakeNodeName("Toggle", node.NodeId),
            Text = node.Text ?? node.SlotId ?? "Toggle",
            ButtonPressed = GetBooleanModelValue(node, modelBySlot),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        SetSlotMeta(toggle, node);
        return toggle;
    }

    private static Control CreateNumberInput(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        double value = GetNumberModelValue(node, modelBySlot);
        (double minValue, double maxValue) = GetNumberRange(node, compatibilityContext, value);
        var input = new SpinBox
        {
            Name = MakeNodeName("NumberInput", node.NodeId),
            MinValue = minValue,
            MaxValue = maxValue,
            Step = 0.1,
            Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        SetSlotMeta(input, node);
        return CreateLabeledSlotControl(node, input);
    }

    private static Control CreateSlider(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        double value = GetNumberModelValue(node, modelBySlot);
        (double minValue, double maxValue) = GetNumberRange(node, compatibilityContext, value);
        var slider = new HSlider
        {
            Name = MakeNodeName("Slider", node.NodeId),
            MinValue = minValue,
            MaxValue = maxValue,
            Step = 0.01,
            Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };

        SetSlotMeta(slider, node);
        return CreateLabeledSlotControl(node, slider);
    }

    private static Control CreateStepper(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        double value = GetNumberModelValue(node, modelBySlot);
        (double minValue, double maxValue) = GetNumberRange(node, compatibilityContext, value);
        var stepper = new SpinBox
        {
            Name = MakeNodeName("Stepper", node.NodeId),
            MinValue = minValue,
            MaxValue = maxValue,
            Step = 1.0,
            Value = value,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        SetSlotMeta(stepper, node);
        return CreateLabeledSlotControl(node, stepper);
    }

    private static Control CreateSelect(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        var select = new OptionButton
        {
            Name = MakeNodeName("Select", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        string value = GetStringModelValue(node, modelBySlot);
        int selectedIndex = -1;
        if (!string.IsNullOrWhiteSpace(node.SlotId) &&
            compatibilityContext.TryGetSelectOptions(node.SlotId, out IReadOnlyList<UiSelectOptionDescriptor> options))
        {
            for (int i = 0; i < options.Count; i++)
            {
                UiSelectOptionDescriptor option = options[i];
                select.AddItem(option.Label);
                select.SetItemMetadata(i, option.Value);
                select.SetItemDisabled(i, option.Disabled);
                if (selectedIndex < 0 && string.Equals(option.Value, value, StringComparison.Ordinal))
                {
                    selectedIndex = i;
                }
            }
        }

        if (selectedIndex >= 0)
        {
            select.Select(selectedIndex);
        }

        SetSlotMeta(select, node);
        return CreateLabeledSlotControl(node, select);
    }

    private static Control CreateRadioGroup(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        var group = new VBoxContainer
        {
            Name = MakeNodeName("RadioGroup", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        AddTitleLabelIfPresent(group, node, "RadioGroupTitle");
        SetSlotMeta(group, node);
        string selected = GetStringModelValue(node, modelBySlot);
        var buttonGroup = new ButtonGroup();
        if (!string.IsNullOrWhiteSpace(node.SlotId) &&
            compatibilityContext.TryGetSelectOptions(node.SlotId, out IReadOnlyList<UiSelectOptionDescriptor> options))
        {
            for (int i = 0; i < options.Count; i++)
            {
                UiSelectOptionDescriptor option = options[i];
                var button = new CheckBox
                {
                    Name = MakeNodeName("RadioOption", option.Value),
                    Text = option.Label,
                    Disabled = option.Disabled,
                    ButtonPressed = string.Equals(option.Value, selected, StringComparison.Ordinal),
                    ButtonGroup = buttonGroup,
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                };
                button.SetMeta("UiOptionValue", option.Value);
                group.AddChild(button);
            }
        }

        return group;
    }

    private static Control CreateCheckboxGroup(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        var group = new VBoxContainer
        {
            Name = MakeNodeName("CheckboxGroup", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        AddTitleLabelIfPresent(group, node, "CheckboxGroupTitle");
        SetSlotMeta(group, node);
        HashSet<string> selected = GetStringListModelValue(node, modelBySlot);
        if (!string.IsNullOrWhiteSpace(node.SlotId) &&
            compatibilityContext.TryGetSelectOptions(node.SlotId, out IReadOnlyList<UiSelectOptionDescriptor> options))
        {
            for (int i = 0; i < options.Count; i++)
            {
                UiSelectOptionDescriptor option = options[i];
                var checkbox = new CheckBox
                {
                    Name = MakeNodeName("CheckboxOption", option.Value),
                    Text = option.Label,
                    Disabled = option.Disabled,
                    ButtonPressed = selected.Contains(option.Value),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                };
                checkbox.SetMeta("UiOptionValue", option.Value);
                group.AddChild(checkbox);
            }
        }

        return group;
    }

    private static Control CreateRepeater(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ClientUiRepeaterRowState>> repeaterRows,
        Action<UiActionDescriptor, string?>? actionPressed)
    {
        var container = new VBoxContainer
        {
            Name = MakeNodeName("Repeater", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        AddTitleLabelIfPresent(container, node, "RepeaterTitle");
        SetSlotMeta(container, node);
        if (string.IsNullOrWhiteSpace(node.SlotId) ||
            !repeaterRows.TryGetValue(node.SlotId, out IReadOnlyDictionary<string, ClientUiRepeaterRowState>? rows))
        {
            return container;
        }

        UiActionDescriptor? rowAction = null;
        if (!string.IsNullOrWhiteSpace(node.ActionId))
        {
            _ = compatibilityContext.TryGetAction(node.ActionId, out rowAction);
        }

        foreach (ClientUiRepeaterRowState rowState in rows.Values)
        {
            var row = new HBoxContainer
            {
                Name = MakeNodeName("RepeaterRow", rowState.RowKey),
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            };

            for (int i = 0; i < rowState.Values.Count; i++)
            {
                UiModelSlotValue value = rowState.Values[i];
                row.AddChild(new Label
                {
                    Name = MakeNodeName("RepeaterCell", value.SlotId),
                    Text = FormatModelValue(value),
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                });
            }

            if (rowAction is not null && actionPressed is not null)
            {
                var button = new Button
                {
                    Name = MakeNodeName("RepeaterRowAction", rowState.RowKey),
                    Text = rowAction.Label,
                    Disabled = rowAction.Availability != UiActionAvailability.Available,
                    SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
                    SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                };
                button.SetMeta("UiActionId", rowAction.ActionId);
                button.SetMeta("UiRowKey", rowState.RowKey);
                string rowKey = rowState.RowKey;
                button.Pressed += () => actionPressed(rowAction, rowKey);
                row.AddChild(button);
            }

            container.AddChild(row);
        }

        return container;
    }

    private static Control CreateTable(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ClientUiRepeaterRowState>> repeaterRows,
        Action<UiActionDescriptor, string?>? actionPressed)
    {
        var container = new VBoxContainer
        {
            Name = MakeNodeName("Table", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        AddTitleLabelIfPresent(container, node, "TableTitle");
        SetSlotMeta(container, node);
        if (string.IsNullOrWhiteSpace(node.SlotId) ||
            !compatibilityContext.TryGetTableColumns(node.SlotId, out IReadOnlyList<UiTableColumnDescriptor> columns))
        {
            return container;
        }

        UiActionDescriptor? rowAction = null;
        if (!string.IsNullOrWhiteSpace(node.ActionId))
        {
            _ = compatibilityContext.TryGetAction(node.ActionId, out rowAction);
        }

        var grid = new GridContainer
        {
            Name = MakeNodeName("TableGrid", node.NodeId),
            Columns = columns.Count + (rowAction is not null ? 1 : 0),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        for (int i = 0; i < columns.Count; i++)
        {
            UiTableColumnDescriptor column = columns[i];
            Control header = CreateTableHeader(column, compatibilityContext, actionPressed);
            grid.AddChild(header);
        }

        if (rowAction is not null)
        {
            grid.AddChild(new Label
            {
                Name = MakeNodeName("TableHeader", "action"),
                Text = string.Empty,
                SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            });
        }

        if (repeaterRows.TryGetValue(node.SlotId, out IReadOnlyDictionary<string, ClientUiRepeaterRowState>? rows))
        {
            foreach (ClientUiRepeaterRowState rowState in rows.Values)
            {
                var valuesBySlot = new Dictionary<string, UiModelSlotValue>(rowState.Values.Count, StringComparer.Ordinal);
                for (int i = 0; i < rowState.Values.Count; i++)
                {
                    UiModelSlotValue value = rowState.Values[i];
                    valuesBySlot[value.SlotId] = value;
                }

                for (int i = 0; i < columns.Count; i++)
                {
                    UiTableColumnDescriptor column = columns[i];
                    grid.AddChild(new Label
                    {
                        Name = MakeNodeName("TableCell", $"{rowState.RowKey}_{column.CellSlotId}"),
                        Text = valuesBySlot.TryGetValue(column.CellSlotId, out UiModelSlotValue? value)
                            ? FormatModelValue(value)
                            : string.Empty,
                        SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    });
                }

                if (rowAction is not null && actionPressed is not null)
                {
                    var button = new Button
                    {
                        Name = MakeNodeName("TableRowAction", rowState.RowKey),
                        Text = rowAction.Label,
                        Disabled = rowAction.Availability != UiActionAvailability.Available,
                        SizeFlagsHorizontal = Control.SizeFlags.ShrinkEnd,
                        SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
                    };
                    button.SetMeta("UiActionId", rowAction.ActionId);
                    button.SetMeta("UiRowKey", rowState.RowKey);
                    string rowKey = rowState.RowKey;
                    button.Pressed += () => actionPressed(rowAction, rowKey);
                    grid.AddChild(button);
                }
            }
        }

        container.AddChild(grid);
        return container;
    }

    private static Control CreateTableHeader(
        UiTableColumnDescriptor column,
        UiSchemaCompatibilityContext compatibilityContext,
        Action<UiActionDescriptor, string?>? actionPressed)
    {
        if (column.Sortable &&
            !string.IsNullOrWhiteSpace(column.SortActionId) &&
            compatibilityContext.TryGetAction(column.SortActionId, out UiActionDescriptor? action) &&
            actionPressed is not null)
        {
            var button = new Button
            {
                Name = MakeNodeName("TableHeaderSort", column.CellSlotId),
                Text = column.Label,
                Disabled = action.Availability != UiActionAvailability.Available,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
            };
            button.SetMeta("UiActionId", action.ActionId);
            button.Pressed += () => actionPressed(action, null);
            return button;
        }

        return new Label
        {
            Name = MakeNodeName("TableHeader", column.CellSlotId),
            Text = column.Label,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
    }

    private static Button CreateButton(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        Action<UiActionDescriptor, string?>? actionPressed)
    {
        UiActionDescriptor? action = null;
        if (!string.IsNullOrWhiteSpace(node.ActionId))
        {
            _ = compatibilityContext.TryGetAction(node.ActionId, out action);
        }

        var button = new Button
        {
            Name = MakeNodeName("Button", node.NodeId),
            Text = node.Text ?? action?.Label ?? "Button",
            Disabled = action is null || action.Availability != UiActionAvailability.Available,
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkCenter,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        if (action is not null && actionPressed is not null)
        {
            button.SetMeta("UiActionId", action.ActionId);
            button.Pressed += () => actionPressed(action, null);
        }

        return button;
    }

    private static HSeparator CreateSeparator(UiSchemaNodeDescriptor node)
    {
        return new HSeparator
        {
            Name = MakeNodeName("Separator", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        };
    }

    private static Control CreateSpacer(UiSchemaNodeDescriptor node)
    {
        return new Control
        {
            Name = MakeNodeName("Spacer", node.NodeId),
            CustomMinimumSize = new Vector2(0.0f, 8.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static Control CreateUnsupportedPlaceholder(UiSchemaNodeDescriptor node)
    {
        return new VBoxContainer
        {
            Name = MakeNodeName($"Unsupported_{node.Kind}", node.NodeId),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };
    }

    private static HBoxContainer CreateLabeledSlotControl(UiSchemaNodeDescriptor node, Control slotControl)
    {
        var row = new HBoxContainer
        {
            Name = MakeNodeName($"{node.Kind}Row", node.NodeId),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        };

        row.AddChild(new Label
        {
            Name = MakeNodeName($"{node.Kind}Label", node.NodeId),
            Text = node.Text ?? node.SlotId ?? node.Kind.ToString(),
            SizeFlagsHorizontal = Control.SizeFlags.ShrinkBegin,
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter,
        });
        row.AddChild(slotControl);
        return row;
    }

    private static void AddTitleLabelIfPresent(VBoxContainer parent, UiSchemaNodeDescriptor node, string prefix)
    {
        if (string.IsNullOrWhiteSpace(node.Text))
        {
            return;
        }

        parent.AddChild(new Label
        {
            Name = MakeNodeName(prefix, node.NodeId),
            Text = node.Text,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
        });
    }

    private static void SetSlotMeta(Control control, UiSchemaNodeDescriptor node)
    {
        if (!string.IsNullOrWhiteSpace(node.SlotId))
        {
            control.SetMeta("UiSlotId", node.SlotId);
        }
    }

    private static string GetStringModelValue(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        if (!string.IsNullOrWhiteSpace(node.SlotId)
            && modelBySlot.TryGetValue(node.SlotId, out UiModelSlotValue? value)
            && value is not null
            && value.Kind == UiModelValueKind.String)
        {
            return value.StringValue ?? string.Empty;
        }

        return string.Empty;
    }

    private static bool GetBooleanModelValue(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        return !string.IsNullOrWhiteSpace(node.SlotId)
            && modelBySlot.TryGetValue(node.SlotId, out UiModelSlotValue? value)
            && value is not null
            && value.Kind == UiModelValueKind.Boolean
            && value.BooleanValue;
    }

    private static HashSet<string> GetStringListModelValue(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        if (!string.IsNullOrWhiteSpace(node.SlotId)
            && modelBySlot.TryGetValue(node.SlotId, out UiModelSlotValue? value)
            && value is not null
            && value.Kind == UiModelValueKind.StringList
            && value.StringListValue is not null)
        {
            return new HashSet<string>(value.StringListValue, StringComparer.Ordinal);
        }

        return new HashSet<string>(StringComparer.Ordinal);
    }

    private static double GetNumberModelValue(
        UiSchemaNodeDescriptor node,
        IReadOnlyDictionary<string, UiModelSlotValue> modelBySlot)
    {
        if (string.IsNullOrWhiteSpace(node.SlotId)
            || !modelBySlot.TryGetValue(node.SlotId, out UiModelSlotValue? value)
            || value is null)
        {
            return 0.0;
        }

        return value.Kind switch
        {
            UiModelValueKind.Integer => value.IntegerValue,
            UiModelValueKind.Number => value.NumberValue,
            _ => 0.0,
        };
    }

    private static (double MinValue, double MaxValue) GetNumberRange(
        UiSchemaNodeDescriptor node,
        UiSchemaCompatibilityContext compatibilityContext,
        double value)
    {
        double minValue = Math.Min(0.0, value);
        double maxValue = Math.Max(100.0, value);
        if (!string.IsNullOrWhiteSpace(node.SlotId)
            && compatibilityContext.TryGetPayloadSlot(node.SlotId, out UiPayloadSlotDescriptor slot))
        {
            minValue = slot.MinNumber ?? minValue;
            maxValue = slot.MaxNumber ?? maxValue;
        }

        if (maxValue < minValue)
        {
            maxValue = minValue;
        }

        return (minValue, maxValue);
    }

    private static string FormatModelValue(UiModelSlotValue value)
    {
        return value.Kind switch
        {
            UiModelValueKind.String => value.StringValue ?? string.Empty,
            UiModelValueKind.Boolean => value.BooleanValue ? "True" : "False",
            UiModelValueKind.Integer => value.IntegerValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
            UiModelValueKind.Number => value.NumberValue.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            UiModelValueKind.StringList => value.StringListValue is null ? string.Empty : string.Join(", ", value.StringListValue),
            _ => string.Empty,
        };
    }

    private static string MakeNodeName(string prefix, int nodeId)
    {
        return $"{prefix}_{nodeId}";
    }

    private static string MakeNodeName(string prefix, string value)
    {
        Span<char> buffer = stackalloc char[Math.Min(value.Length, 64)];
        int written = 0;
        for (int i = 0; i < value.Length && written < buffer.Length; i++)
        {
            char c = value[i];
            buffer[written++] = char.IsLetterOrDigit(c) ? c : '_';
        }

        return written == 0
            ? prefix
            : $"{prefix}_{new string(buffer[..written])}";
    }
}
