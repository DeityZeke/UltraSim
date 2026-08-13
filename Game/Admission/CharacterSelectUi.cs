using UltraSim.UI;

namespace UltraSim.Game.Admission;

/// <summary>
/// Game-owned typed contract for the minimal CharacterSelect Gump. Source
/// names are used only while compiling this definition; runtime game code uses
/// the prepared numeric bindings exposed here.
/// </summary>
public static class CharacterSelectUi
{
    private static readonly Contract _contract = CreateContract();

    public static CompiledUiDefinition Definition => _contract.Definition;

    public static UiSlotId Title => _contract.Title;

    public static UiSlotId Characters => _contract.Characters;

    public static UiActionId Play => _contract.Play;

    private static Contract CreateContract()
    {
        var source = new UiDefinitionBuilder("game.admission.character-select", version: 1);
        UiSlotReference title = source.DeclareSlot("title", UiSlotValueKind.Text);
        UiSlotReference characters = source.DeclareSlot("characters", UiSlotValueKind.List);
        UiActionReference play = source.DeclareAction("play", acceptsRowContext: true);

        source.AddCanvas(canvas =>
        {
            canvas.AddVertical(
                "content",
                content =>
            {
                content.AddText("title", title);
                content.AddList("characters", characters, play);
            },
            UiChildLayout.Canvas(new UiCanvasPlacement(16, 16, 768, 568)));
        });

        CompiledUiDefinition definition = UiDefinitionCompiler.Compile(source);
        return new Contract(
            definition,
            definition.Bindings.RequireSlot("title"),
            definition.Bindings.RequireSlot("characters"),
            definition.Bindings.RequireAction("play"));
    }

    private sealed record Contract(
        CompiledUiDefinition Definition,
        UiSlotId Title,
        UiSlotId Characters,
        UiActionId Play);
}
