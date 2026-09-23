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

    /// <summary>Captures the same neutral sample through the F0.1 resolved physical bindings.</summary>
    public static ClientWorldInputSample Capture(
        ResolvedClientSettings settings,
        bool windowHasFocus)
    {
        ArgumentNullException.ThrowIfNull(settings);
        float moveX = Strength(settings, ClientSemanticAction.MoveRight) -
            Strength(settings, ClientSemanticAction.MoveLeft);
        float moveZ = Strength(settings, ClientSemanticAction.MoveBackward) -
            Strength(settings, ClientSemanticAction.MoveForward);
        GamePlayerActions held = GamePlayerActions.None;
        if (IsPressed(settings, ClientSemanticAction.Interact))
            held |= GamePlayerActions.Interact;
        if (IsPressed(settings, ClientSemanticAction.Primary))
            held |= GamePlayerActions.Primary;
        if (IsPressed(settings, ClientSemanticAction.Jump))
            held |= GamePlayerActions.Jump;
        return new ClientWorldInputSample(moveX, moveZ, held, windowHasFocus);
    }

    /// <summary>Matches one edge-triggered key event against a resolved semantic binding.</summary>
    public static bool Matches(
        InputEventKey key,
        ResolvedClientSettings settings,
        ClientSemanticAction action)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryGetBinding(action, out ClientPhysicalInput binding) ||
            binding.Kind != ClientPhysicalInputKind.Keyboard ||
            !TryParseKey(binding.Code, out Key expected) ||
            CurrentModifiers() != binding.Modifiers)
            return false;
        Key observed = key.PhysicalKeycode != Key.None ? key.PhysicalKeycode : key.Keycode;
        return observed == expected;
    }

    /// <summary>Matches a mouse edge to the button portion of a semantic binding.</summary>
    public static bool Matches(
        InputEventMouseButton mouse,
        ResolvedClientSettings settings,
        ClientSemanticAction action)
    {
        ArgumentNullException.ThrowIfNull(mouse);
        ArgumentNullException.ThrowIfNull(settings);
        return settings.TryGetBinding(action, out ClientPhysicalInput binding) &&
            binding.Kind == ClientPhysicalInputKind.MouseButton &&
            TryParseMouseButton(binding.Code, out MouseButton expected) &&
            mouse.ButtonIndex == expected;
    }

    /// <summary>A distinct drag gesture can coexist with an edge action on the same button.</summary>
    public static bool Matches(
        InputEventMouseMotion motion,
        ResolvedClientSettings settings,
        ClientSemanticAction action)
    {
        ArgumentNullException.ThrowIfNull(motion);
        ArgumentNullException.ThrowIfNull(settings);
        if (!settings.TryGetBinding(action, out ClientPhysicalInput binding) ||
            binding.Kind != ClientPhysicalInputKind.MouseButton ||
            binding.Code?.EndsWith("_DRAG", StringComparison.Ordinal) != true ||
            !TryParseMouseButton(binding.Code[..^"_DRAG".Length], out MouseButton button))
            return false;
        MouseButtonMask mask = button switch
        {
            MouseButton.Left => MouseButtonMask.Left,
            MouseButton.Right => MouseButtonMask.Right,
            MouseButton.Middle => MouseButtonMask.Middle,
            _ => 0,
        };
        return mask != 0 && (motion.ButtonMask & mask) != 0;
    }

    private static float Strength(ResolvedClientSettings settings, ClientSemanticAction action) =>
        IsPressed(settings, action) ? 1f : 0f;

    private static bool IsPressed(ResolvedClientSettings settings, ClientSemanticAction action)
    {
        if (!settings.TryGetBinding(action, out ClientPhysicalInput binding)) return false;
        return binding.Kind switch
        {
            ClientPhysicalInputKind.Keyboard =>
                TryParseKey(binding.Code, out Key key) &&
                Input.IsPhysicalKeyPressed(key) &&
                CurrentModifiers() == binding.Modifiers,
            ClientPhysicalInputKind.MouseButton =>
                TryParseMouseButton(binding.Code, out MouseButton button) &&
                Input.IsMouseButtonPressed(button),
            ClientPhysicalInputKind.GamepadButton =>
                Enum.TryParse(binding.Code, ignoreCase: true, out JoyButton button) &&
                Input.IsJoyButtonPressed(0, button),
            _ => false,
        };
    }

    private static bool TryParseKey(string? code, out Key key) =>
        Enum.TryParse(code, ignoreCase: true, out key) && key != Key.None;

    private static bool TryParseMouseButton(string? code, out MouseButton button)
    {
        string value = code?.EndsWith("_DOUBLE", StringComparison.Ordinal) == true
            ? code[..^"_DOUBLE".Length]
            : code ?? string.Empty;
        return Enum.TryParse(value, ignoreCase: true, out button) && button != MouseButton.None;
    }

    private static ClientInputModifiers CurrentModifiers()
    {
        ClientInputModifiers result = ClientInputModifiers.None;
        if (Input.IsKeyPressed(Key.Shift)) result |= ClientInputModifiers.Shift;
        if (Input.IsKeyPressed(Key.Ctrl)) result |= ClientInputModifiers.Control;
        if (Input.IsKeyPressed(Key.Alt)) result |= ClientInputModifiers.Alt;
        if (Input.IsKeyPressed(Key.Meta)) result |= ClientInputModifiers.Meta;
        return result;
    }
}
