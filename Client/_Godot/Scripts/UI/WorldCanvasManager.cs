using Godot;
using UltraSim.Client.Godot.Mobiles;
using UltraSim.Client.Godot.World;

namespace UltraSim.Client.Godot.UI;

[GlobalClass]
public partial class WorldCanvasManager : CanvasLayer
{
    [Export] public NodePath? CharacterWorldPath { get; set; }
    [Export(PropertyHint.Range, "0.25,10.0,0.25,or_greater")] public double OverheadChatSeconds { get; set; } = 4.0;
    [Export] public bool ShowOverheadChatBubbles { get; set; } = true;
    [Export] public bool ShowNameplates { get; set; } = true;
    [Export(PropertyHint.Range, "1,256,1")] public int MaxVisibleWorldUi { get; set; } = 32;
    [Export(PropertyHint.Range, "0.5,3.0,0.05")] public float WorldUiScale { get; set; } = 1.0f;

    public ClientCharacterWorld? CharacterWorld { get; private set; }

    private readonly Dictionary<ulong, ActorOverlay> _overlays = new();
    private readonly List<ulong> _removeScratch = new();

    public override void _Ready()
    {
        CharacterWorld = CharacterWorldPath is null || CharacterWorldPath.IsEmpty
            ? null
            : GetNodeOrNull<ClientCharacterWorld>(CharacterWorldPath);
    }

    public override void _Process(double delta)
    {
        UpdateProjectedOverlays();
    }

    public void ShowOverheadChat(ulong entityId, string text)
    {
        if (!ShowOverheadChatBubbles || entityId == 0 || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        ActorOverlay overlay = GetOrCreateOverlay(entityId);
        overlay.ChatLabel.Text = text.Trim();
        overlay.ChatLabel.Visible = true;
        overlay.ChatExpiresAtSeconds = Time.GetTicksMsec() / 1000.0 + OverheadChatSeconds;
    }

    private void UpdateProjectedOverlays()
    {
        if (CharacterWorld is null)
        {
            ClearOverlays();
            return;
        }

        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null)
        {
            HideOverlays();
            return;
        }

        double nowSeconds = Time.GetTicksMsec() / 1000.0;
        int visibleCount = 0;
        int maxVisible = Math.Max(0, MaxVisibleWorldUi);
        float uiScale = Mathf.Clamp(WorldUiScale, 0.5f, 3.0f);
        foreach (KeyValuePair<ulong, CharacterRoot> pair in CharacterWorld.Characters)
        {
            ulong entityId = pair.Key;
            CharacterRoot character = pair.Value;
            ActorOverlay overlay = GetOrCreateOverlay(entityId);
            overlay.NameLabel.Text = ResolveDisplayName(entityId, character);
            overlay.NameLabel.Scale = new Vector2(uiScale, uiScale);
            overlay.ChatLabel.Scale = new Vector2(uiScale, uiScale);

            Vector3 anchor = character.NameplatePivot.GlobalPosition;
            bool visible = !camera.IsPositionBehind(anchor) && visibleCount < maxVisible;
            overlay.NameLabel.Visible = visible && ShowNameplates;
            overlay.ChatLabel.Visible = visible && ShowOverheadChatBubbles && overlay.ChatExpiresAtSeconds > nowSeconds;

            if (!visible)
            {
                continue;
            }

            visibleCount++;
            Vector2 screen = camera.UnprojectPosition(anchor);
            overlay.NameLabel.Position = new Vector2(screen.X - overlay.NameLabel.Size.X * uiScale * 0.5f, screen.Y - 20.0f);
            overlay.ChatLabel.Position = new Vector2(screen.X - overlay.ChatLabel.Size.X * uiScale * 0.5f, screen.Y - 48.0f);
        }

        _removeScratch.Clear();
        foreach (ulong entityId in _overlays.Keys)
        {
            if (!CharacterWorld.Characters.ContainsKey(entityId))
            {
                _removeScratch.Add(entityId);
            }
        }

        for (int i = 0; i < _removeScratch.Count; i++)
        {
            RemoveOverlay(_removeScratch[i]);
        }

        _removeScratch.Clear();
    }

    private ActorOverlay GetOrCreateOverlay(ulong entityId)
    {
        if (_overlays.TryGetValue(entityId, out ActorOverlay? existing))
        {
            return existing;
        }

        var overlay = new ActorOverlay(CreateOverlayLabel(), CreateOverlayLabel());
        overlay.ChatLabel.Visible = false;
        AddChild(overlay.NameLabel);
        AddChild(overlay.ChatLabel);
        _overlays.Add(entityId, overlay);
        return overlay;
    }

    private static Label CreateOverlayLabel()
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Size = new Vector2(180.0f, 20.0f)
        };
        label.AddThemeColorOverride("font_color", Colors.White);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    private static string ResolveDisplayName(ulong entityId, CharacterRoot character)
    {
        string? displayName = character.Definition?.DisplayName;
        return string.IsNullOrWhiteSpace(displayName)
            ? $"Entity {entityId}"
            : displayName;
    }

    private void HideOverlays()
    {
        foreach (ActorOverlay overlay in _overlays.Values)
        {
            overlay.NameLabel.Visible = false;
            overlay.ChatLabel.Visible = false;
        }
    }

    private void ClearOverlays()
    {
        foreach (ulong entityId in _overlays.Keys)
        {
            _removeScratch.Add(entityId);
        }

        for (int i = 0; i < _removeScratch.Count; i++)
        {
            RemoveOverlay(_removeScratch[i]);
        }

        _removeScratch.Clear();
    }

    private void RemoveOverlay(ulong entityId)
    {
        if (!_overlays.Remove(entityId, out ActorOverlay? overlay))
        {
            return;
        }

        overlay.NameLabel.QueueFree();
        overlay.ChatLabel.QueueFree();
    }

    private sealed class ActorOverlay
    {
        public ActorOverlay(Label nameLabel, Label chatLabel)
        {
            NameLabel = nameLabel;
            ChatLabel = chatLabel;
        }

        public Label NameLabel { get; }
        public Label ChatLabel { get; }
        public double ChatExpiresAtSeconds { get; set; }
    }
}
