using Godot;
using UltraSim.Client.Core;
using UltraSim.Client.Godot;
using UltraSim.UI;

/// <summary>Small local cold-open fixture for manually exercising the Godot renderer.</summary>
public partial class S04Demo : Control
{
    private UiClientRuntime _client = null!;

    public override void _Ready()
    {
        _client = new UiClientRuntime(new GodotUiRenderer(this));
        var link = new LocalSemanticLink(_client);
        link.ActionSubmitted += request => GD.Print(
            $"[S0.4] action={request.Action.Value} instance={request.Instance.Value}");

        var builder = new UiDefinitionBuilder("s0.4.demo");
        UiSlotReference title = builder.DeclareSlot("title", UiSlotValueKind.Text);
        UiSlotReference rows = builder.DeclareSlot("rows", UiSlotValueKind.List);
        UiActionReference close = builder.DeclareAction("close");
        builder.AddCanvas(new UiCanvasOptions(800, 600), canvas =>
        {
            canvas.AddPanel("background", "demo.background",
                UiChildLayout.Canvas(new UiCanvasPlacement(0, 0, 800, 600)));
            canvas.AddVertical("content", vertical =>
            {
                vertical.AddText("title", title);
                vertical.AddList("rows", rows);
                vertical.AddButton("close", close, title);
            }, UiChildLayout.Canvas(new UiCanvasPlacement(40, 40, 720, 520)),
            new UiFlowOptions(spacing: 8, alignment: UiAlignment.Stretch));
        });

        CompiledUiDefinition definition = UiDefinitionCompiler.Compile(builder);
        var model = new UiModel(definition);
        model.SetText(definition.Prepare(title), "UltraSim S0.4");
        model.ReplaceRows(definition.Prepare(rows), new[]
        {
            new UiListRow(new UiRowId(1), new[] { UiValue.FromText("Cold open") }),
            new UiListRow(new UiRowId(2), new[] { UiValue.FromText("Local model delta ready") }),
        });

        link.OpenView(new UiOpenView(
            new UiDefinitionCacheKey(
                "local-demo",
                "s0.4-demo",
                1,
                definition.Key,
                definition.Version,
                definition.Hash),
            definition,
            model));
    }
}
