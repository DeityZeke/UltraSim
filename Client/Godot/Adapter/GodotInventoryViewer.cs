using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.StandardUi;
using UltraSim.Framework.Definitions;
using UltraSim.UI;

namespace UltraSim.Client.Godot;

/// <summary>
/// Inventory-specific presentation over the retained, authority-owned UI model. No item mutation,
/// placement or selection truth lives here: every gesture submits an existing compiled action.
/// </summary>
internal sealed partial class GodotInventoryViewer
{
    private const float Cell = 56f;
    private const int MaximumNativeCells = 4_096;
    private readonly UiViewInstanceId _instance;
    private readonly CompiledUiDefinition _definition;
    private readonly IGodotUiSemanticImageResolver? _authoredImages;
    private readonly IGodotUiSemanticImageResolver? _derivedPortraits;
    private readonly Func<Texture2D> _fallbackIcon;
    private readonly UiDynamicSemanticImageBindings _bindings;
    private UiModelView _model;
    private Action<UiActionRequest> _submit;
    private ClientInventoryViewerProfile _preferred;
    private bool _retired;

    internal GodotInventoryViewer(UiViewInstanceId instance, CompiledUiDefinition definition,
        UiModelView model, Action<UiActionRequest> submit,
        ClientInventoryViewerProfile preferred, IGodotUiSemanticImageResolver? authoredImages,
        IGodotUiSemanticImageResolver? derivedPortraits,
        Func<Texture2D> fallbackIcon)
    {
        _instance = instance;
        _definition = definition;
        _model = model;
        _submit = submit;
        _preferred = preferred;
        _authoredImages = authoredImages;
        _derivedPortraits = derivedPortraits;
        _fallbackIcon = fallbackIcon;
        _bindings = new(instance, definition);
        Root = new Panel
        {
            Name = "Element_background",
            Size = new Vector2(768, 568),
            Position = new Vector2(16, 16),
            ThemeTypeVariation = "inventory.surface",
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        Render();
    }

    internal Panel Root { get; }
    internal ClientInventoryViewerProfile Effective { get; private set; }
    internal int RenderedItemCount { get; private set; }
    internal int RenderedFootprintCount { get; private set; }

    internal void Apply(UiModelView model, Action<UiActionRequest> submit)
    {
        if (_retired) return;
        _model = model;
        _submit = submit;
        Render();
    }

    internal void SetPreferred(ClientInventoryViewerProfile preferred)
    {
        if (_retired || _preferred == preferred) return;
        _preferred = preferred;
        Render();
    }

    internal void RefreshPresentation()
    {
        if (!_retired) Render();
    }

    internal void Retire()
    {
        _retired = true;
        _bindings.Retire();
        _model = default;
    }

    private void Render()
    {
        InventoryViewerProjection projection = InventoryViewerProjection.Read(_model, _preferred);
        _bindings.Reconcile(_instance, _definition, projection);
        Effective = projection.Effective;
        RenderedItemCount = projection.Items.Count;
        RenderedFootprintCount = projection.Items.Count(item => item.HasGridPlacement);
        foreach (Node child in Root.GetChildren())
        {
            Root.RemoveChild(child);
            child.QueueFree();
        }
        var outer = new MarginContainer { Name = "InventoryMargins" };
        outer.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        outer.AddThemeConstantOverride("margin_left", 12);
        outer.AddThemeConstantOverride("margin_top", 10);
        outer.AddThemeConstantOverride("margin_right", 12);
        outer.AddThemeConstantOverride("margin_bottom", 10);
        Root.AddChild(outer);
        var column = new VBoxContainer { Name = "InventoryContent" };
        column.AddThemeConstantOverride("separation", 6);
        outer.AddChild(column);

        var header = new HBoxContainer();
        header.AddChild(new Label
        {
            Name = "Element_title",
            Text = Text("title"),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        });
        header.AddChild(new Label
        {
            Name = "EffectiveInventoryViewer",
            Text = projection.Effective == ClientInventoryViewerProfile.D2Grid ? "D2 Grid" : "List",
            TooltipText = projection.FallbackReason ?? "Client-only presentation preference",
        });
        column.AddChild(header);
        if (projection.FallbackReason is not null)
            column.AddChild(new Label { Name = "InventoryViewerFallback", Text = projection.FallbackReason });

        var body = new HBoxContainer
        {
            Name = "InventoryBody",
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 300),
        };
        column.AddChild(body);
        var content = new ScrollContainer
        {
            Name = "InventoryScroll",
            CustomMinimumSize = new Vector2(490, 310),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        body.AddChild(content);
        var inventoryColumn = new VBoxContainer();
        inventoryColumn.AddThemeConstantOverride("separation", 4);
        content.AddChild(inventoryColumn);

        var list = new VBoxContainer { Name = "Element_items" };
        foreach (InventoryPresentedItem item in projection.Items)
            list.AddChild(MakeListRow(item));
        inventoryColumn.AddChild(list);
        if (projection.Effective == ClientInventoryViewerProfile.D2Grid)
        {
            list.Visible = false;
            var grid = new InventoryGridSurface(projection.GridWidth, projection.GridHeight, Cell,
                row => Submit("place-at", row));
            grid.Name = "InventoryGridCanvas";
            inventoryColumn.AddChild(grid);
            foreach (InventoryPresentedItem item in projection.Items)
                if (item.HasGridPlacement && !item.Equipped)
                    grid.AddChild(MakeGridItem(item));
        }

        var right = new VBoxContainer { CustomMinimumSize = new Vector2(210, 0) };
        body.AddChild(right);
        right.AddChild(new Label { Text = "Equipment / Details" });
        foreach (InventoryPresentedItem item in projection.Items)
            if (item.Equipped)
                right.AddChild(MakeEquipmentRow(item));
        right.AddChild(new Label
        {
            Name = "Element_details",
            Text = Text("details"),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            CustomMinimumSize = new Vector2(200, 80),
        });

        // List can present a Grid container without making its row order into placement truth.
        // One choice control reuses the exact authored cell-row identities and stays bounded even
        // for the largest supported Grid; D2 uses the drawn canvas instead.
        UiSlotId cellsSlot = _definition.Bindings.RequireSlot("grid-cells");
        ReadOnlySpan<UiListRow> cells = _model.GetRows(cellsSlot);
        if (cells.Length > MaximumNativeCells)
            throw new InvalidDataException("The native Inventory cell bound was exceeded.");
        if (projection.Effective == ClientInventoryViewerProfile.List &&
            projection.GridWidth > 0 && projection.GridHeight > 0)
        {
            var placement = new HBoxContainer { Name = "Element_grid-placement" };
            placement.AddChild(new Label { Text = "Place selected item at" });
            var position = new OptionButton { Name = "InventoryPlacementCell" };
            for (int index = 0; index < cells.Length; index++)
            {
                int x = index % projection.GridWidth, y = index / projection.GridWidth;
                position.AddItem($"({x},{y})", checked((int)cells[index].Id.Value));
            }
            placement.AddChild(position);
            var place = new Button
            {
                Name = "InventoryPlaceSelected",
                Text = "Place",
                Disabled = !Enabled("place-at"),
                TooltipText = Unavailable("place-at"),
            };
            place.Pressed += () => Submit("place-at", new UiRowId(
                checked((ulong)position.GetSelectedId())));
            placement.AddChild(place);
            column.AddChild(placement);
        }

        var transfer = new HBoxContainer { Name = "Element_transfer-destinations" };
        UiSlotId destinationSlot = _definition.Bindings.RequireSlot("transfer-destinations");
        foreach (UiListRow destination in _model.GetRows(destinationSlot))
        {
            UiRowId row = destination.Id;
            var button = new Button
            {
                Name = $"Row_{row.Value}",
                Text = destination.Values.Length == 0 ? "Move" : destination.Values[0].Text ?? "Move",
                Disabled = !Enabled("move-to"),
            };
            button.GuiInput += input =>
            {
                if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
                { Submit("move-to", row); button.AcceptEvent(); }
            };
            transfer.AddChild(button);
        }
        column.AddChild(transfer);
        var actions = new HBoxContainer();
        foreach (string action in new[] { "split", "equip", "unequip", "drop", "close" })
        {
            var button = new Button
            {
                Name = "Element_" + action,
                Text = action[..1].ToUpperInvariant() + action[1..],
                Disabled = !Enabled(action),
                TooltipText = Unavailable(action),
                ThemeTypeVariation = "inventory.action",
            };
            button.Pressed += () => Submit(action);
            actions.AddChild(button);
        }
        column.AddChild(actions);
        column.AddChild(new Label { Name = "Element_status", Text = Text("status") });
    }

    private Control MakeListRow(InventoryPresentedItem item)
    {
        var row = new HBoxContainer
        {
            Name = $"Row_{item.Row.Value}",
            MouseFilter = Control.MouseFilterEnum.Stop,
            CustomMinimumSize = new Vector2(0, 46),
        };
        row.AddChild(new TextureRect
        {
            Texture = Portrait(item),
            CustomMinimumSize = new Vector2(40, 40),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        row.AddChild(new Label
        {
            Text = item.Name + (item.Quantity > 1 ? $" ×{item.Quantity}" : "") +
                (item.Equipped ? "  [Main Hand]" : item.HasGridPlacement
                    ? $"  ({item.X},{item.Y}) {item.Width}×{item.Height}" : ""),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        row.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            { Submit("select", item.Row); row.AcceptEvent(); }
        };
        return row;
    }

    private Control MakeEquipmentRow(InventoryPresentedItem item) => MakeListRow(item);

    private Control MakeGridItem(InventoryPresentedItem item)
    {
        var card = new Panel
        {
            Name = $"GridItem_{item.Row.Value}",
            Position = new Vector2(item.X * Cell + 2, item.Y * Cell + 2),
            Size = new Vector2(item.Width * Cell - 4, item.Height * Cell - 4),
            ClipContents = true,
            TooltipText = item.Name,
            MouseFilter = Control.MouseFilterEnum.Stop,
            ThemeTypeVariation = "inventory.item",
        };
        var body = new VBoxContainer { Size = card.Size, ClipContents = true };
        card.AddChild(body);
        body.AddChild(new TextureRect
        {
            Texture = Portrait(item),
            CustomMinimumSize = new Vector2(34, 34),
            ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        body.AddChild(new Label
        {
            Text = item.Name + (item.Quantity > 1 ? $" ×{item.Quantity}" : ""),
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore,
        });
        card.GuiInput += input =>
        {
            if (input is InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true })
            { Submit("select", item.Row); card.AcceptEvent(); }
        };
        return card;
    }

    private Texture2D Portrait(InventoryPresentedItem item)
    {
        if (!_bindings.TryGet(_instance, _definition, item.Row,
            out SemanticPresentationReference reference)) return _fallbackIcon();
        return _authoredImages?.Resolve(reference) ??
            _authoredImages?.Resolve(reference with { Role = PresentationRoleId.World }) ??
            _derivedPortraits?.Resolve(reference) ?? _fallbackIcon();
    }

    private string Text(string slot) =>
        _model.Get(_definition.Bindings.RequireSlot(slot)).Text ?? string.Empty;

    private bool Enabled(string action) =>
        _model.GetActionState(_definition.Bindings.RequireAction(action)).Enabled;

    private string Unavailable(string action) =>
        _model.GetActionState(_definition.Bindings.RequireAction(action)).UnavailableReason ?? string.Empty;

    private void Submit(string action, UiRowId row = default)
    {
        if (_retired || !Enabled(action)) return;
        _submit(new UiActionRequest(_instance, _definition.Handle,
            _definition.Bindings.RequireAction(action), row));
    }

    private sealed partial class InventoryGridSurface : Control
    {
        private readonly int _columns, _rows;
        private readonly float _cell;
        private readonly Action<UiRowId> _selectCell;
        internal InventoryGridSurface(int columns, int rows, float cell, Action<UiRowId> selectCell)
        {
            _columns = columns; _rows = rows; _cell = cell; _selectCell = selectCell;
            CustomMinimumSize = new Vector2(columns * cell, rows * cell);
            Size = CustomMinimumSize;
            MouseFilter = MouseFilterEnum.Stop;
        }
        public override void _Draw()
        {
            for (int y = 0; y < _rows; y++)
            for (int x = 0; x < _columns; x++)
            {
                var rectangle = new Rect2(x * _cell, y * _cell, _cell, _cell);
                DrawRect(rectangle, new Color(.11f, .14f, .17f), true);
                DrawRect(rectangle, new Color(.42f, .49f, .55f), false, 1f);
            }
        }
        public override void _GuiInput(InputEvent inputEvent)
        {
            if (inputEvent is not InputEventMouseButton
                { ButtonIndex: MouseButton.Left, Pressed: true } click) return;
            int x = (int)(click.Position.X / _cell), y = (int)(click.Position.Y / _cell);
            if (x < 0 || y < 0 || x >= _columns || y >= _rows) return;
            _selectCell(new UiRowId(checked((ulong)(y * _columns + x + 1))));
            AcceptEvent();
        }
    }
}
