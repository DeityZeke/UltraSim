using System.Globalization;
using Godot;
using UltraSim.Framework.ControlPlane;

namespace UltraSim.Client.Godot;

/// <summary>
/// Cold, generic management viewer. It knows descriptors and values, never the owner behind a
/// provider. All controls are Godot nodes parented to the supplied shell host and retired with it.
/// No native RefCounted resource is manufactured or retained by this adapter.
/// </summary>
internal sealed class GodotControlPlaneView
{
    private readonly ControlPlaneViewer _viewer;
    private readonly Action<ControlResult> _completed;
    private int _requestSequence;

    internal GodotControlPlaneView(ControlPlaneViewer viewer, Action<ControlResult> completed)
    {
        _viewer = viewer;
        _completed = completed;
    }

    internal void Build(VBoxContainer host)
    {
        ArgumentNullException.ThrowIfNull(host);
        var tabs = new TabContainer { Name = "ManagementCategories",
            CustomMinimumSize = new Vector2(0, 460), SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        host.AddChild(tabs);
        foreach (IGrouping<string, ControlViewerCategory> group in _viewer.Capture()
            .GroupBy(category => category.Path.Value!.Split('/')[0], StringComparer.Ordinal))
        {
            var page = new VBoxContainer { Name = group.Key,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            tabs.AddChild(page);
            foreach (ControlViewerCategory category in group)
            {
                var section = new VBoxContainer { Name = "Management_" + category.Path.Value!.Replace('/', '_') };
                section.AddChild(new Label { Text = category.Path.Value, ThemeTypeVariation = "HeaderSmall" });
                page.AddChild(section);
                foreach (ControlViewerItem item in category.Items)
                {
                    switch (item.Descriptor)
                    {
                        case ControlDescriptor control when item.Control is not null:
                            BuildControl(section, item, control, item.Control);
                            break;
                        case DiagnosticDescriptor diagnostic when item.Diagnostic is not null:
                            section.AddChild(new Label { Text = diagnostic.Label + ": " + Format(item.Diagnostic.Value) });
                            break;
                        case ControlCollectionDescriptor collection when item.Collection is not null:
                            BuildCollection(section, collection, item.Collection);
                            break;
                        case ControlActionDescriptor action:
                            BuildAction(section, item, action);
                            break;
                    }
                }
            }
        }
    }

    private void BuildControl(VBoxContainer section, ControlViewerItem item, ControlDescriptor descriptor, ControlState state)
    {
        var row = new HBoxContainer { Name = "ManagementControl_" + descriptor.Id.Value };
        row.AddChild(new Label { Text = descriptor.Label, CustomMinimumSize = new Vector2(190, 0),
            TooltipText = descriptor.Description });
        bool enabled = state.Writable && descriptor.ApplyMode is not (ControlApplyMode.ReadOnly or ControlApplyMode.BuildTime);
        ControlValue initial = _viewer.TryGetDraft(item.Provider, item.Generation, descriptor.Id, out ControlValue draft)
            ? draft : state.Requested;
        Func<ControlValue> read = AddValueEditor(row, descriptor.Schema, initial, enabled);
        var apply = new Button { Text = "Apply", Disabled = !enabled };
        apply.Pressed += () => Deferred(() =>
        {
            try
            {
                ControlValue value = read();
                if (!_viewer.SetDraft(item.Provider, item.Generation, descriptor.Id, value))
                    throw new InvalidOperationException("The value or owner lifetime is no longer valid.");
                _completed(_viewer.Apply(NextRequest(), item.Provider, item.Generation, state.Revision));
            }
            catch (Exception error) when (error is not OutOfMemoryException)
            {
                ShowMessage(row, error.Message);
            }
        });
        row.AddChild(apply);
        section.AddChild(row);
        string stateText = $"Requested {Format(state.Requested)} ({state.RequestedProvenance.Kind}) · " +
            $"Effective {Format(state.Effective)} ({state.EffectiveProvenance.Kind})";
        if (state.Pending is { } pending) stateText += $" · Pending {Format(pending)}";
        if (descriptor.ApplyMode is not ControlApplyMode.Live and not ControlApplyMode.ReadOnly)
            stateText += $" · {descriptor.ApplyMode} required";
        if (!enabled) stateText += " · " + (state.DisabledReason.Length != 0 ? state.DisabledReason : "Read-only");
        section.AddChild(new Label { Text = stateText, AutowrapMode = TextServer.AutowrapMode.WordSmart });
    }

    private void BuildAction(VBoxContainer section, ControlViewerItem item, ControlActionDescriptor descriptor)
    {
        var row = new HBoxContainer { Name = "ManagementAction_" + descriptor.Id.Value };
        var arguments = new Func<ControlValue>[descriptor.Arguments.Count];
        for (int index = 0; index < arguments.Length; index++)
            arguments[index] = AddValueEditor(row, descriptor.Arguments[index], Default(descriptor.Arguments[index]), true);
        var button = new Button { Text = descriptor.Label, Disabled = !item.CanInvoke,
            TooltipText = item.CanInvoke ? descriptor.Description : "This viewer has read access only." };
        var confirmation = new HBoxContainer();
        button.Pressed += () => Deferred(() =>
        {
            try
            {
                ControlResult? result = _viewer.RequestAction(NextRequest(), item.Provider, item.Generation,
                    descriptor.Id, arguments.Select(read => read()).ToArray());
                if (result is not null) { _completed(result); return; }
                ControlPendingConfirmation ticket = _viewer.PendingConfirmation!;
                confirmation.AddChild(new Label { Text = ticket.Prompt });
                var yes = new Button { Text = "Confirm" };
                yes.Pressed += () => Deferred(() => _completed(_viewer.ConfirmAction()));
                confirmation.AddChild(yes);
                var no = new Button { Text = "Cancel" };
                no.Pressed += () => Deferred(() => { _viewer.CancelAction(); Clear(confirmation); });
                confirmation.AddChild(no);
            }
            catch (Exception error) when (error is not OutOfMemoryException) { ShowMessage(row, error.Message); }
        });
        row.AddChild(button);
        section.AddChild(row);
        section.AddChild(confirmation);
    }

    private static void BuildCollection(VBoxContainer section, ControlCollectionDescriptor descriptor,
        ControlCollectionSnapshot snapshot)
    {
        section.AddChild(new Label { Text = descriptor.Label + $" ({snapshot.Rows.Count} rows)" });
        const int pageSize = 32;
        int page = 0;
        var navigation = new HBoxContainer { Name = "ManagementCollectionNavigation" };
        var previous = new Button { Text = "Previous" };
        var next = new Button { Text = "Next" };
        var pageLabel = new Label();
        navigation.AddChild(previous);
        navigation.AddChild(pageLabel);
        navigation.AddChild(next);
        section.AddChild(navigation);
        var table = new GridContainer { Columns = descriptor.Columns.Count + 1 };
        section.AddChild(table);
        void RenderPage()
        {
            Clear(table);
            table.AddChild(new Label { Text = "ID" });
            foreach (ControlCollectionColumn column in descriptor.Columns)
                table.AddChild(new Label { Text = column.Label });
            foreach (ControlCollectionRow row in snapshot.Rows.Skip(page * pageSize).Take(pageSize))
            {
                table.AddChild(new Label { Text = row.Id.Value });
                foreach (ControlValue value in row.Values) table.AddChild(new Label { Text = Format(value) });
            }
            pageLabel.Text = $"Page {page + 1} / {Math.Max(1, (snapshot.Rows.Count + pageSize - 1) / pageSize)}";
            previous.Disabled = page == 0;
            next.Disabled = (page + 1) * pageSize >= snapshot.Rows.Count;
        }
        previous.Pressed += () => Deferred(() => { if (page > 0) { page--; RenderPage(); } });
        next.Pressed += () => Deferred(() =>
        {
            if ((page + 1) * pageSize < snapshot.Rows.Count) { page++; RenderPage(); }
        });
        RenderPage();
    }

    private static Func<ControlValue> AddValueEditor(HBoxContainer row, ControlValueSchema schema,
        ControlValue initial, bool enabled)
    {
        switch (schema.Kind)
        {
            case ControlValueKind.Bool:
                var toggle = new CheckBox { ButtonPressed = initial.AsBool(), Disabled = !enabled };
                row.AddChild(toggle);
                return () => ControlValue.Bool(toggle.ButtonPressed);
            case ControlValueKind.Option:
                var select = new OptionButton { Disabled = !enabled };
                for (int i = 0; i < schema.Options.Count; i++)
                {
                    select.AddItem(schema.Options[i].Value, i);
                    if (initial.AsOption() == schema.Options[i]) select.Select(i);
                }
                row.AddChild(select);
                return () => ControlValue.Option(schema.Options[select.GetSelectedId()]);
            case ControlValueKind.Int64 when schema.Minimum is { } min && schema.Maximum is { } max &&
                min.AsInt64() >= -1_000_000 && max.AsInt64() <= 1_000_000:
                var integer = new SpinBox { MinValue = min.AsInt64(), MaxValue = max.AsInt64(),
                    Step = 1, Rounded = true, Value = initial.AsInt64(), Editable = enabled };
                row.AddChild(integer);
                return () => ControlValue.Int64(checked((long)integer.Value));
            case ControlValueKind.Float64 when schema.Minimum is { } lower && schema.Maximum is { } upper:
                var number = new SpinBox { MinValue = lower.AsFloat64(), MaxValue = upper.AsFloat64(),
                    Step = .05, Value = initial.AsFloat64(), Editable = enabled };
                row.AddChild(number);
                return () => ControlValue.Float64(number.Value);
            default:
                var text = new LineEdit { Text = Format(initial), MaxLength = schema.Kind == ControlValueKind.Text
                    ? schema.MaximumText : 32, Editable = enabled, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
                row.AddChild(text);
                return () => schema.Kind switch
                {
                    ControlValueKind.Text => ControlValue.Text(text.Text),
                    ControlValueKind.Int64 => ControlValue.Int64(long.Parse(text.Text, CultureInfo.InvariantCulture)),
                    ControlValueKind.UInt64 => ControlValue.UInt64(ulong.Parse(text.Text, CultureInfo.InvariantCulture)),
                    ControlValueKind.Float64 => ControlValue.Float64(double.Parse(text.Text, CultureInfo.InvariantCulture)),
                    _ => throw new InvalidOperationException("Unsupported control scalar."),
                };
        }
    }

    private static ControlValue Default(ControlValueSchema schema) => schema.Kind switch
    {
        ControlValueKind.Bool => ControlValue.Bool(false),
        ControlValueKind.Int64 => schema.Minimum ?? ControlValue.Int64(0),
        ControlValueKind.UInt64 => schema.Minimum ?? ControlValue.UInt64(0),
        ControlValueKind.Float64 => schema.Minimum ?? ControlValue.Float64(0),
        ControlValueKind.Option => ControlValue.Option(schema.Options[0]),
        ControlValueKind.Text => ControlValue.Text(""),
        _ => throw new InvalidOperationException("Unsupported control scalar."),
    };

    private static string Format(ControlValue value) => value.Kind switch
    {
        ControlValueKind.Bool => value.AsBool() ? "On" : "Off",
        ControlValueKind.Int64 => value.AsInt64().ToString(CultureInfo.InvariantCulture),
        ControlValueKind.UInt64 => value.AsUInt64().ToString(CultureInfo.InvariantCulture),
        ControlValueKind.Float64 => value.AsFloat64().ToString("0.###", CultureInfo.InvariantCulture),
        ControlValueKind.Option => value.AsOption().Value!,
        ControlValueKind.Text => value.AsText(),
        _ => "N/A",
    };

    private ControlRequestId NextRequest() => new("viewer-" + (++_requestSequence).ToString(CultureInfo.InvariantCulture));
    private static void Deferred(Action callback) => Callable.From(callback).CallDeferred();
    private static void ShowMessage(Control row, string message) => row.TooltipText = message;
    private static void Clear(Node parent)
    {
        foreach (Node child in parent.GetChildren()) { parent.RemoveChild(child); child.QueueFree(); }
    }
}
