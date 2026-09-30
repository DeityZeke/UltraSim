using UltraSim.Client.Core;
using UltraSim.Framework.Definitions;
using UltraSim.Game.Presentation.Contracts;
using UltraSim.UI;

namespace UltraSim.Client.Core;

/// <summary>One client-only reading of an already authorized Inventory UI row.</summary>
public readonly record struct InventoryPresentedItem(
    UiRowId Row,
    string Name,
    int Quantity,
    bool Equipped,
    bool HasGridPlacement,
    int X,
    int Y,
    int Width,
    int Height,
    SemanticPresentationReference Portrait,
    PublicFallbackBounds FallbackBounds)
{
    public bool HasAuthoredPortrait => Portrait.IsValid;
}

public sealed class InventoryViewerProjection
{
    private InventoryViewerProjection(ClientInventoryViewerProfile preferred,
        ClientInventoryViewerProfile effective, string? reason,
        int gridWidth, int gridHeight, InventoryPresentedItem[] items)
    {
        Preferred = preferred;
        Effective = effective;
        FallbackReason = reason;
        GridWidth = gridWidth;
        GridHeight = gridHeight;
        Items = Array.AsReadOnly(items);
    }

    public ClientInventoryViewerProfile Preferred { get; }
    public ClientInventoryViewerProfile Effective { get; }
    public string? FallbackReason { get; }
    public int GridWidth { get; }
    public int GridHeight { get; }
    public IReadOnlyList<InventoryPresentedItem> Items { get; }

    public static InventoryViewerProjection Read(UiModelView model,
        ClientInventoryViewerProfile preferred)
    {
        if (!model.IsValid || model.Definition.Key.Value != "game.inventory.gump" ||
            preferred is not (ClientInventoryViewerProfile.D2Grid or ClientInventoryViewerProfile.List))
            throw new ArgumentException("A valid Inventory UI model and viewer preference are required.");
        UiSlotId itemSlot = model.Definition.Bindings.RequireSlot("items");
        UiSlotId cellsSlot = model.Definition.Bindings.RequireSlot("grid-cells");
        ReadOnlySpan<UiListRow> rows = model.GetRows(itemSlot);
        if (rows.Length > 4_096)
            throw new InvalidDataException("The Inventory projection exceeded its bounded row capacity.");
        var items = new InventoryPresentedItem[rows.Length];
        bool typed = true;
        for (int index = 0; index < rows.Length; index++)
        {
            items[index] = ReadItem(rows[index], out bool hasTypedPlacement);
            typed &= hasTypedPlacement;
        }

        ReadOnlySpan<UiListRow> cells = model.GetRows(cellsSlot);
        int width = 0, height = 0;
        bool grid = typed && TryReadGrid(cells, out width, out height);
        if (grid)
        {
            var occupied = new bool[checked(width * height)];
            foreach (InventoryPresentedItem item in items)
            {
                if (item.Equipped) continue;
                if (!item.HasGridPlacement) { grid = false; break; }
                if (item.X < 0 || item.Y < 0 || item.Width <= 0 || item.Height <= 0 ||
                    item.X + item.Width > width || item.Y + item.Height > height)
                { grid = false; break; }
                for (int y = item.Y; y < item.Y + item.Height; y++)
                for (int x = item.X; x < item.X + item.Width; x++)
                {
                    int cell = y * width + x;
                    if (occupied[cell]) grid = false;
                    occupied[cell] = true;
                }
            }
        }
        var effective = preferred == ClientInventoryViewerProfile.D2Grid && grid
            ? ClientInventoryViewerProfile.D2Grid : ClientInventoryViewerProfile.List;
        string? reason = preferred == ClientInventoryViewerProfile.D2Grid && !grid
            ? "This container has no valid Grid placement projection; List is shown instead."
            : null;
        return new InventoryViewerProjection(preferred, effective, reason,
            grid ? width : 0, grid ? height : 0, items);
    }

    private static InventoryPresentedItem ReadItem(UiListRow row, out bool typed)
    {
        ReadOnlySpan<UiValue> values = row.Values;
        if (values.Length < 4 || values[0].Kind != UiValueKind.Text ||
            values[1].Kind != UiValueKind.Number || !TryInteger(values[1], 1, int.MaxValue, out int quantity))
            throw new InvalidDataException("An Inventory item row is malformed.");
        typed = values.Length >= InventoryPresentationRowFields.ItemFieldCount &&
            TryInteger(values[InventoryPresentationRowFields.PlacementMode], 0, 1, out _);
        bool equipped = typed
            ? TryInteger(values[InventoryPresentationRowFields.EquipmentKind], 0, 1, out int kind) && kind != 0
            : values[2].Text == "Main Hand";
        if (typed && !TryInteger(values[InventoryPresentationRowFields.EquipmentKind], 0, 1, out _)) typed = false;
        bool placed = typed && TryInteger(values[InventoryPresentationRowFields.PlacementMode], 0, 1, out int mode) && mode == 1;
        int x = 0, y = 0, width = 0, height = 0;
        if (placed && (!TryInteger(values[InventoryPresentationRowFields.GridX], 0, 64, out x) ||
            !TryInteger(values[InventoryPresentationRowFields.GridY], 0, 64, out y) ||
            !TryInteger(values[InventoryPresentationRowFields.GridWidth], 1, 64, out width) ||
            !TryInteger(values[InventoryPresentationRowFields.GridHeight], 1, 64, out height)))
            typed = false;
        SemanticPresentationReference portrait = default;
        PublicFallbackBounds bounds = default;
        if (row.Attachment.IsValid &&
            (row.Attachment.Kind != InventoryPortraitAttachmentCodec.AttachmentKind ||
             !InventoryPortraitAttachmentCodec.TryDecode(row.Attachment.Payload,
                 out portrait, out bounds)))
            throw new InvalidDataException("The typed Inventory portrait attachment is invalid.");
        return new(row.Id, values[0].Text!, quantity, equipped, placed,
            x, y, width, height, portrait, bounds);
    }

    private static bool TryReadGrid(ReadOnlySpan<UiListRow> cells, out int width, out int height)
    {
        width = height = 0;
        if (cells.Length is 0 or > 4_096) return false;
        foreach (UiListRow row in cells)
        {
            ReadOnlySpan<UiValue> values = row.Values;
            if (values.Length < 3 || !TryInteger(values[InventoryPresentationRowFields.CellX], 0, 63, out int x) ||
                !TryInteger(values[InventoryPresentationRowFields.CellY], 0, 63, out int y))
                return false;
            width = Math.Max(width, x + 1);
            height = Math.Max(height, y + 1);
        }
        if (checked(width * height) != cells.Length) return false;
        var seen = new bool[cells.Length];
        foreach (UiListRow row in cells)
        {
            int x = (int)row.Values[InventoryPresentationRowFields.CellX].Number;
            int y = (int)row.Values[InventoryPresentationRowFields.CellY].Number;
            int index = y * width + x;
            if (seen[index] || row.Id.Value != (ulong)(index + 1)) return false;
            seen[index] = true;
        }
        return true;
    }

    private static bool TryInteger(UiValue value, int minimum, int maximum, out int number)
    {
        number = 0;
        if (value.Kind != UiValueKind.Number || !double.IsFinite(value.Number) ||
            value.Number < minimum || value.Number > maximum || value.Number != Math.Truncate(value.Number))
            return false;
        number = (int)value.Number;
        return true;
    }
}

/// <summary>
/// Exact dynamic binding for one admitted view and compiled layout. Reconcile replaces the entire
/// bounded row set, so removed/replaced rows cannot retain an old portrait after model updates.
/// </summary>
public sealed class UiDynamicSemanticImageBindings
{
    private readonly Dictionary<UiRowId, SemanticPresentationReference> _rows = [];
    private bool _retired;
    public UiDynamicSemanticImageBindings(UiViewInstanceId instance, CompiledUiDefinition definition)
    {
        if (!instance.IsValid) throw new ArgumentException("A live view instance is required.", nameof(instance));
        ArgumentNullException.ThrowIfNull(definition);
        Instance = instance;
        Definition = definition.Handle;
    }
    public UiViewInstanceId Instance { get; }
    public UiDefinitionHandle Definition { get; }
    public void Reconcile(UiViewInstanceId instance, CompiledUiDefinition definition,
        InventoryViewerProjection projection)
    {
        ObjectDisposedException.ThrowIf(_retired, this);
        if (instance != Instance || definition.Handle != Definition)
            throw new InvalidOperationException("The dynamic portrait binding belongs to another view or layout.");
        ArgumentNullException.ThrowIfNull(projection);
        if (projection.Items.Count > 4_096) throw new InvalidDataException("Too many Inventory portrait rows.");
        _rows.Clear();
        foreach (InventoryPresentedItem item in projection.Items)
            if (item.Portrait.IsValid && !_rows.TryAdd(item.Row, item.Portrait))
                throw new InvalidDataException("An Inventory portrait row is duplicated.");
    }
    public bool TryGet(UiViewInstanceId instance, CompiledUiDefinition definition, UiRowId row,
        out SemanticPresentationReference reference)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (!_retired && instance == Instance && definition.Handle == Definition &&
            _rows.TryGetValue(row, out reference))
            return true;
        reference = default;
        return false;
    }

    public void Retire()
    {
        _rows.Clear();
        _retired = true;
    }
}
