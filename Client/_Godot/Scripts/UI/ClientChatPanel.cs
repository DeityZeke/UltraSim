using Godot;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class ClientChatPanel : PanelContainer
{
    public event Action<string>? ChatSubmitted;

    private RichTextLabel? _history;
    private LineEdit? _input;

    public bool HasInputFocus => _input?.HasFocus() == true;

    public override void _Ready()
    {
        BuildInterface();
        AppendSystemLine("Chat shell ready. Server chat wiring lands in G3.");
    }

    public void AppendSystemLine(string text)
    {
        _history?.AppendText($"[system] {text}\n");
    }

    public void AppendChatLine(string senderName, string text)
    {
        _history?.AppendText($"{senderName}: {text}\n");
    }

    private void BuildInterface()
    {
        CustomMinimumSize = new Vector2(420, 180);
        OffsetLeft = 16;
        OffsetTop = 520;
        OffsetRight = 436;
        OffsetBottom = 704;

        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        AddChild(margin);

        var column = new VBoxContainer();
        column.AddThemeConstantOverride("separation", 6);
        margin.AddChild(column);

        _history = new RichTextLabel
        {
            FitContent = false,
            ScrollFollowing = true,
            BbcodeEnabled = false,
            CustomMinimumSize = new Vector2(0, 120),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        column.AddChild(_history);

        _input = new LineEdit
        {
            PlaceholderText = "say, /command, [command..."
        };
        _input.TextSubmitted += HandleTextSubmitted;
        column.AddChild(_input);
    }

    private void HandleTextSubmitted(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _history?.AppendText($"> {text}\n");
        ChatSubmitted?.Invoke(text);
        if (_input is not null)
        {
            _input.Text = string.Empty;
        }
    }
}
