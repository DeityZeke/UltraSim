using Godot;
using UltraSim.Client.Core;
using UltraSim.Game.Input;

namespace UltraSim.Client.Godot;

/// <summary>Concrete Godot InputMap names used to capture one neutral gameplay sample.</summary>
public readonly record struct GodotWorldInputMap(
    StringName MoveLeft,
    StringName MoveRight,
    StringName MoveForward,
    StringName MoveBackward,
    StringName Interact,
    StringName Primary)
{
    public static GodotWorldInputMap Default { get; } = new(
        "move_left",
        "move_right",
        "move_forward",
        "move_backward",
        "interact",
        "primary_action");
}

/// <summary>
/// Thin raw Godot adapter. It owns physical InputMap reads only; Core owns focus suppression,
/// vector normalization, held/pressed interpretation, and command sequencing.
/// </summary>
public static class GodotWorldInput
{
    public static ClientWorldInputSample Capture(
        in GodotWorldInputMap map,
        bool windowHasFocus)
    {
        float moveX = Input.GetActionStrength(map.MoveRight) -
            Input.GetActionStrength(map.MoveLeft);
        float moveZ = Input.GetActionStrength(map.MoveBackward) -
            Input.GetActionStrength(map.MoveForward);
        GamePlayerActions held = GamePlayerActions.None;
        if (Input.IsActionPressed(map.Interact)) held |= GamePlayerActions.Interact;
        if (Input.IsActionPressed(map.Primary)) held |= GamePlayerActions.Primary;
        return new ClientWorldInputSample(moveX, moveZ, held, windowHasFocus);
    }
}
