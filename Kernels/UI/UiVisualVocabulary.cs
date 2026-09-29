namespace UltraSim.UI;

/// <summary>Renderer-neutral presentation of an authorized image role.</summary>
public enum UiImageMode : byte { Natural, Fit, Fill, Stretch, Tile }
public enum UiFrameFillMode : byte { Stretch, Tile }
public enum UiButtonImageState : byte { Normal, Hover, Pressed, Released, Active, Disabled, Focused }

public readonly record struct UiImageVisual
{
    public UiImageVisual(string role, UiImageMode mode = UiImageMode.Fit, float opacity = 1)
    {
        if (string.IsNullOrEmpty(role) || role.Length > 96 || role[0] is < 'a' or > 'z' ||
            role[^1] == '.' || role.Contains("..", StringComparison.Ordinal) ||
            role.Any(c => c is not (>= 'a' and <= 'z' or >= '0' and <= '9' or '.' or '-' or '_')) ||
            !Enum.IsDefined(mode) || !float.IsFinite(opacity) || opacity is < 0 or > 1)
            throw new ArgumentException("Invalid bounded UI image visual.");
        Role = role;
        Mode = mode;
        Opacity = opacity;
    }
    public string? Role { get; }
    public UiImageMode Mode { get; }
    public float Opacity { get; }
    public bool IsValid => Role is not null;
}

/// <summary>Nine independent semantic image roles; source texels do not define logical margins.</summary>
public sealed class UiNinePieceVisual
{
    private readonly UiImageVisual[] _pieces;
    public UiNinePieceVisual(IEnumerable<UiImageVisual> pieces, int left, int top, int right, int bottom,
        UiFrameFillMode edge = UiFrameFillMode.Stretch, UiFrameFillMode center = UiFrameFillMode.Stretch)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        _pieces = pieces.Take(10).ToArray();
        if (_pieces.Length != 9 || _pieces.Any(p => !p.IsValid) ||
            left is < 1 or > 256 || top is < 1 or > 256 || right is < 1 or > 256 || bottom is < 1 or > 256 ||
            !Enum.IsDefined(edge) || !Enum.IsDefined(center))
            throw new ArgumentException("Invalid bounded nine-piece frame.");
        Left = left; Top = top; Right = right; Bottom = bottom;
        Edge = edge; Center = center;
    }
    public UiImageVisual this[int index] => _pieces[index];
    public int Left { get; }
    public int Top { get; }
    public int Right { get; }
    public int Bottom { get; }
    public UiFrameFillMode Edge { get; }
    public UiFrameFillMode Center { get; }
    public int MinimumWidth => checked(Left + Right + 1);
    public int MinimumHeight => checked(Top + Bottom + 1);
}

/// <summary>Client-local interaction state selects an image without involving Game authority.</summary>
public sealed class UiButtonImageVisual
{
    public UiButtonImageVisual(UiImageVisual normal, UiImageVisual hover = default,
        UiImageVisual pressed = default, UiImageVisual released = default,
        UiImageVisual active = default, UiImageVisual disabled = default, UiImageVisual focused = default)
    {
        if (!normal.IsValid) throw new ArgumentException("Normal button image is required.");
        Normal = normal; Hover = hover; Pressed = pressed; Released = released;
        Active = active; Disabled = disabled; Focused = focused;
    }
    public UiImageVisual Normal { get; }
    public UiImageVisual Hover { get; }
    public UiImageVisual Pressed { get; }
    public UiImageVisual Released { get; }
    public UiImageVisual Active { get; }
    public UiImageVisual Disabled { get; }
    public UiImageVisual Focused { get; }
    public UiImageVisual Resolve(UiButtonImageState state, bool active = false) => state switch
    {
        UiButtonImageState.Disabled when Disabled.IsValid => Disabled,
        UiButtonImageState.Focused when Focused.IsValid => Focused,
        UiButtonImageState.Active when Active.IsValid => Active,
        UiButtonImageState.Pressed when Pressed.IsValid => Pressed,
        UiButtonImageState.Pressed when Hover.IsValid => Hover,
        UiButtonImageState.Hover when Hover.IsValid => Hover,
        UiButtonImageState.Released when Released.IsValid => Released,
        UiButtonImageState.Released when Hover.IsValid => Hover,
        _ => active && Active.IsValid ? Active : Normal,
    };
}

public readonly record struct UiToggleImageVisual
{
    public UiToggleImageVisual(UiImageVisual off, UiImageVisual on)
    {
        if (!off.IsValid || !on.IsValid) throw new ArgumentException("Both toggle states require an image.");
        Off = off; On = on;
    }
    public UiImageVisual Off { get; }
    public UiImageVisual On { get; }
}
