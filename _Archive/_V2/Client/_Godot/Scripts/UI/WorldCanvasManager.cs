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
    [Export] public bool ShowOverheadHealthBars { get; set; } = true;
    [Export(PropertyHint.Range, "1,256,1")] public int MaxVisibleWorldUi { get; set; } = 32;
    [Export(PropertyHint.Range, "0.5,3.0,0.05")] public float WorldUiScale { get; set; } = 1.0f;
    [Export(PropertyHint.Range, "0,96,1")] public float ScreenVerticalOffsetPixels { get; set; } = 24.0f;
    [Export] public Key NameplateRevealKey { get; set; } = Key.Alt;
    [Export] public string NameplateRevealAction { get; set; } = "Show_Nameplates";
    [Export] public string DebugRevealAction { get; set; } = "Show_Debug";
    [Export] public string NameplateTemplate { get; set; } = "{name}";
    [Export] public string OwnerNameplateTemplate { get; set; } = "{name}";
    [Export] public string UnknownNameTemplate { get; set; } = "Entity {entityId}";
    [Export] public string OverheadChatTemplate { get; set; } = "{text}";

    public ClientCharacterWorld? CharacterWorld { get; private set; }

    private readonly Dictionary<ulong, ActorOverlay> _overlays = new();
    private readonly Dictionary<ulong, ActorCombatHealthState> _combatHealthStates = new();
    private readonly List<VisibleActorOverlay> _visibleOverlays = new();
    private readonly List<ulong> _removeScratch = new();
    private Control? _overlayRoot;

    public override void _Ready()
    {
        CharacterWorld = CharacterWorldPath is null || CharacterWorldPath.IsEmpty
            ? null
            : GetNodeOrNull<ClientCharacterWorld>(CharacterWorldPath);
        EnsureOverlayRoot();
    }

    public override void _Process(double delta)
    {
        UpdateProjectedOverlays((float)delta);
    }

    public void ShowOverheadChat(ulong entityId, string text)
    {
        if (entityId == 0 || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        ActorOverlay overlay = GetOrCreateOverlay(entityId);
        overlay.ChatLines.Add(new ChatLine(text.Trim(), Mathf.Max(0.01f, (float)OverheadChatSeconds)));
        while (overlay.ChatLines.Count > 2)
        {
            overlay.ChatLines.RemoveAt(0);
        }
    }

    public void SetActorCombatHealth(ulong entityId, bool isInCombat, bool hasHealth, float healthFraction)
    {
        if (entityId == 0)
        {
            return;
        }

        if (!isInCombat || !hasHealth)
        {
            _combatHealthStates.Remove(entityId);
            return;
        }

        _combatHealthStates[entityId] = new ActorCombatHealthState(
            IsInCombat: true,
            HasHealth: true,
            HealthFraction: Mathf.Clamp(healthFraction, 0.0f, 1.0f));
    }

    public void ClearAll()
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
        _visibleOverlays.Clear();
        _combatHealthStates.Clear();
    }

    private void UpdateProjectedOverlays(float deltaSeconds)
    {
        if (CharacterWorld is null)
        {
            ClearAll();
            return;
        }

        Camera3D? camera = GetViewport().GetCamera3D();
        if (camera is null)
        {
            HideOverlays();
            return;
        }

        bool revealNameplates = ShowNameplates || IsNameplateRevealRequested();
        bool showDebugIds = IsDebugRevealRequested();
        _visibleOverlays.Clear();

        foreach (KeyValuePair<ulong, CharacterRoot> pair in CharacterWorld.Characters)
        {
            ulong entityId = pair.Key;
            CharacterRoot character = pair.Value;
            ActorOverlay overlay = GetOrCreateOverlay(entityId);
            overlay.Root.Visible = false;
            PruneExpiredChatLines(overlay, deltaSeconds);

            Vector3 anchor = character.NameplatePivot.GlobalPosition;
            if (camera.IsPositionBehind(anchor))
            {
                continue;
            }

            string displayName = ResolveDisplayName(entityId, character);
            bool showNameplate = revealNameplates && !string.IsNullOrWhiteSpace(displayName);
            bool showChat = ShowOverheadChatBubbles && overlay.ChatLines.Count > 0;
            bool showHealthBar = ShouldShowHealthBar(entityId, out float healthFraction);
            if (!showNameplate && !showChat && !showDebugIds && !showHealthBar)
            {
                continue;
            }

            Vector2 screen = camera.UnprojectPosition(anchor) + new Vector2(0.0f, -ScreenVerticalOffsetPixels);
            float distanceSquared = camera.GlobalPosition.DistanceSquaredTo(anchor);
            _visibleOverlays.Add(new VisibleActorOverlay(
                overlay,
                screen,
                distanceSquared,
                displayName,
                healthFraction,
                showNameplate,
                showChat,
                showHealthBar,
                showDebugIds));
        }

        _visibleOverlays.Sort(static (left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));

        int maxVisible = Math.Max(0, MaxVisibleWorldUi);
        for (int i = 0; i < _visibleOverlays.Count; i++)
        {
            VisibleActorOverlay visible = _visibleOverlays[i];
            if (i >= maxVisible)
            {
                continue;
            }

            UpdateOverlayVisual(visible);
        }

        _removeScratch.Clear();
        foreach (ulong entityId in _overlays.Keys)
        {
            if (!CharacterWorld.Characters.ContainsKey(entityId))
            {
                _removeScratch.Add(entityId);
                _combatHealthStates.Remove(entityId);
            }
        }

        for (int i = 0; i < _removeScratch.Count; i++)
        {
            RemoveOverlay(_removeScratch[i]);
        }

        _removeScratch.Clear();
        _visibleOverlays.Clear();
    }

    private ActorOverlay GetOrCreateOverlay(ulong entityId)
    {
        if (_overlays.TryGetValue(entityId, out ActorOverlay? existing))
        {
            return existing;
        }

        EnsureOverlayRoot();

        var root = new VBoxContainer
        {
            Name = $"WorldUi_{entityId}",
            Alignment = BoxContainer.AlignmentMode.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };

        var debugLabel = CreateOverlayLabel(new Color(0.65f, 0.78f, 1.0f));
        var previousChatLabel = CreateOverlayLabel(new Color(0.86f, 0.88f, 0.92f));
        var currentChatLabel = CreateOverlayLabel(Colors.White);
        var nameLabel = CreateOverlayLabel(new Color(1.0f, 0.93f, 0.66f));
        Control healthBar = CreateHealthBar(out ColorRect healthFill);

        previousChatLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        currentChatLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
        previousChatLabel.CustomMinimumSize = new Vector2(220.0f, 0.0f);
        currentChatLabel.CustomMinimumSize = new Vector2(220.0f, 0.0f);

        root.AddChild(debugLabel);
        root.AddChild(previousChatLabel);
        root.AddChild(currentChatLabel);
        root.AddChild(healthBar);
        root.AddChild(nameLabel);
        _overlayRoot!.AddChild(root);

        var overlay = new ActorOverlay(entityId, root, debugLabel, previousChatLabel, currentChatLabel, healthBar, healthFill, nameLabel);
        _overlays.Add(entityId, overlay);
        return overlay;
    }

    private void EnsureOverlayRoot()
    {
        if (_overlayRoot is not null)
        {
            return;
        }

        _overlayRoot = new Control
        {
            Name = "WorldUiRoot",
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _overlayRoot.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlayRoot.OffsetLeft = 0.0f;
        _overlayRoot.OffsetTop = 0.0f;
        _overlayRoot.OffsetRight = 0.0f;
        _overlayRoot.OffsetBottom = 0.0f;
        AddChild(_overlayRoot);
    }

    private static Label CreateOverlayLabel(Color fontColor)
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ClipText = false
        };
        label.AddThemeColorOverride("font_color", fontColor);
        label.AddThemeColorOverride("font_outline_color", Colors.Black);
        label.AddThemeConstantOverride("outline_size", 4);
        return label;
    }

    private static Control CreateHealthBar(out ColorRect fill)
    {
        var root = new Control
        {
            CustomMinimumSize = new Vector2(78.0f, 8.0f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Visible = false
        };

        var background = new ColorRect
        {
            Color = new Color(0.02f, 0.025f, 0.03f, 0.88f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        background.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(background);

        fill = new ColorRect
        {
            Color = new Color(0.25f, 0.88f, 0.34f, 0.94f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        fill.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.AddChild(fill);

        return root;
    }

    private void UpdateOverlayVisual(in VisibleActorOverlay visible)
    {
        ActorOverlay overlay = visible.Overlay;
        overlay.DebugLabel.Visible = visible.ShowDebugId;
        overlay.DebugLabel.Text = visible.ShowDebugId ? $"Entity {visible.Overlay.EntityId}" : string.Empty;

        string previousChat = visible.ShowChat && overlay.ChatLines.Count > 1
            ? FormatOverheadChat(overlay.ChatLines[^2].Text)
            : string.Empty;
        string currentChat = visible.ShowChat && overlay.ChatLines.Count > 0
            ? FormatOverheadChat(overlay.ChatLines[^1].Text)
            : string.Empty;

        overlay.PreviousChatLabel.Visible = !string.IsNullOrWhiteSpace(previousChat);
        overlay.PreviousChatLabel.Text = previousChat;
        overlay.CurrentChatLabel.Visible = !string.IsNullOrWhiteSpace(currentChat);
        overlay.CurrentChatLabel.Text = currentChat;
        overlay.HealthBarRoot.Visible = visible.ShowHealthBar;
        if (visible.ShowHealthBar)
        {
            float healthFraction = Mathf.Clamp(visible.HealthFraction, 0.0f, 1.0f);
            overlay.HealthBarFill.Scale = new Vector2(healthFraction, 1.0f);
            overlay.HealthBarFill.Color = ResolveHealthColor(healthFraction);
        }

        overlay.NameLabel.Visible = visible.ShowNameplate;
        overlay.NameLabel.Text = visible.ShowNameplate ? visible.DisplayName : string.Empty;

        float uiScale = Mathf.Clamp(WorldUiScale, 0.5f, 3.0f);
        overlay.Root.ResetSize();
        Vector2 size = overlay.Root.GetCombinedMinimumSize();
        overlay.Root.Size = size;
        overlay.Root.Scale = new Vector2(uiScale, uiScale);
        overlay.Root.Position = visible.ScreenPosition - new Vector2(size.X * uiScale * 0.5f, size.Y * uiScale);
        overlay.Root.Visible =
            overlay.DebugLabel.Visible ||
            overlay.PreviousChatLabel.Visible ||
            overlay.CurrentChatLabel.Visible ||
            overlay.HealthBarRoot.Visible ||
            overlay.NameLabel.Visible;
    }

    private bool ShouldShowHealthBar(ulong entityId, out float healthFraction)
    {
        healthFraction = 0.0f;
        if (!ShowOverheadHealthBars ||
            !_combatHealthStates.TryGetValue(entityId, out ActorCombatHealthState state) ||
            !state.IsInCombat ||
            !state.HasHealth)
        {
            return false;
        }

        healthFraction = Mathf.Clamp(state.HealthFraction, 0.0f, 1.0f);
        return true;
    }

    private static Color ResolveHealthColor(float healthFraction)
    {
        if (healthFraction <= 0.25f)
            return new Color(0.92f, 0.2f, 0.16f, 0.95f);
        if (healthFraction <= 0.55f)
            return new Color(0.95f, 0.78f, 0.22f, 0.95f);
        return new Color(0.25f, 0.88f, 0.34f, 0.94f);
    }

    private static void PruneExpiredChatLines(ActorOverlay overlay, float deltaSeconds)
    {
        for (int i = overlay.ChatLines.Count - 1; i >= 0; i--)
        {
            ChatLine line = overlay.ChatLines[i];
            float remaining = Mathf.Max(0.0f, line.TtlSeconds - Mathf.Max(0.0f, deltaSeconds));
            if (remaining <= 0.0f)
            {
                overlay.ChatLines.RemoveAt(i);
                continue;
            }

            overlay.ChatLines[i] = line with { TtlSeconds = remaining };
        }
    }

    private string ResolveDisplayName(ulong entityId, CharacterRoot character)
    {
        string? displayName = !string.IsNullOrWhiteSpace(character.DisplayNameOverride)
            ? character.DisplayNameOverride
            : character.Definition?.DisplayName;
        string resolvedName = string.IsNullOrWhiteSpace(displayName)
            ? FormatTemplate(UnknownNameTemplate, entityId, string.Empty, string.Empty)
            : displayName.Trim();
        string template = CharacterWorld?.LocalControlledEntityId == entityId
            ? OwnerNameplateTemplate
            : NameplateTemplate;
        return FormatTemplate(template, entityId, resolvedName, string.Empty);
    }

    private string FormatOverheadChat(string text)
    {
        return FormatTemplate(OverheadChatTemplate, 0, string.Empty, text.Trim());
    }

    private static string FormatTemplate(string template, ulong entityId, string name, string text)
    {
        string effectiveTemplate = string.IsNullOrWhiteSpace(template) ? "{name}" : template;
        return effectiveTemplate
            .Replace("{entityId}", entityId.ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{name}", name, StringComparison.Ordinal)
            .Replace("{text}", text, StringComparison.Ordinal)
            .Trim();
    }

    private bool IsNameplateRevealRequested()
    {
        if (!string.IsNullOrWhiteSpace(NameplateRevealAction) && InputMap.HasAction(NameplateRevealAction))
        {
            return Input.IsActionPressed(NameplateRevealAction);
        }

        return Input.IsKeyPressed(NameplateRevealKey);
    }

    private bool IsDebugRevealRequested()
    {
        return !string.IsNullOrWhiteSpace(DebugRevealAction) &&
            InputMap.HasAction(DebugRevealAction) &&
            Input.IsActionPressed(DebugRevealAction);
    }

    private void HideOverlays()
    {
        foreach (ActorOverlay overlay in _overlays.Values)
        {
            overlay.Root.Visible = false;
        }
    }

    private void RemoveOverlay(ulong entityId)
    {
        if (!_overlays.Remove(entityId, out ActorOverlay? overlay))
        {
            return;
        }

        overlay.Root.QueueFree();
    }

    private readonly record struct ChatLine(string Text, float TtlSeconds);

    private readonly record struct ActorCombatHealthState(
        bool IsInCombat,
        bool HasHealth,
        float HealthFraction);

    private readonly record struct VisibleActorOverlay(
        ActorOverlay Overlay,
        Vector2 ScreenPosition,
        float DistanceSquared,
        string DisplayName,
        float HealthFraction,
        bool ShowNameplate,
        bool ShowChat,
        bool ShowHealthBar,
        bool ShowDebugId);

    private sealed class ActorOverlay
    {
        public ActorOverlay(
            ulong entityId,
            VBoxContainer root,
            Label debugLabel,
            Label previousChatLabel,
            Label currentChatLabel,
            Control healthBarRoot,
            ColorRect healthBarFill,
            Label nameLabel)
        {
            EntityId = entityId;
            Root = root;
            DebugLabel = debugLabel;
            PreviousChatLabel = previousChatLabel;
            CurrentChatLabel = currentChatLabel;
            HealthBarRoot = healthBarRoot;
            HealthBarFill = healthBarFill;
            NameLabel = nameLabel;
        }

        public ulong EntityId { get; }
        public VBoxContainer Root { get; }
        public Label DebugLabel { get; }
        public Label PreviousChatLabel { get; }
        public Label CurrentChatLabel { get; }
        public Control HealthBarRoot { get; }
        public ColorRect HealthBarFill { get; }
        public Label NameLabel { get; }
        public List<ChatLine> ChatLines { get; } = new(2);
    }
}
