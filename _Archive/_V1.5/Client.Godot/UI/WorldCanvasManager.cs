using System.Collections.Generic;
using Client.Godot.Prefabs.Mobiles;
using Client.Godot.World;
using Godot;
using UltraSim.Client.Runtime;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class WorldCanvasManager : UiCanvasManager
    {
        [Export] public NodePath? CharacterWorldPath { get; set; }
        [Export] public bool ShowOverheadChat { get; set; } = true;
        [Export] public bool ShowNameplates { get; set; } = true;
        [Export(PropertyHint.Range, "1,256,1")] public int MaxVisibleWorldUi { get; set; } = 32;
        [Export(PropertyHint.Range, "0.5,3.0,0.05")] public float WorldUiScale { get; set; } = 1.0f;
        [Export] public float ScreenVerticalOffsetPixels { get; set; } = 24.0f;
        [Export] public Key NameplateRevealKey { get; set; } = Key.Alt;
        [Export] public string NameplateRevealAction { get; set; } = "Show_Nameplates";
        [Export] public string DebugRevealAction { get; set; } = "Show_Debug";

        private readonly Dictionary<ulong, WorldUiEntry> _entries = new();
        private readonly List<VisibleWorldUiEntry> _visibleEntries = new();

        private ClientCharacterWorld? _characterWorld;
        private Control? _overlayRoot;

        public override void _Ready()
        {
            base._Ready();
            _characterWorld = CharacterWorldPath == null || CharacterWorldPath.IsEmpty
                ? null
                : GetNodeOrNull<ClientCharacterWorld>(CharacterWorldPath);
            EnsureOverlayRoot();
        }

        public override void _Process(double delta)
        {
            if (_entries.Count == 0)
                return;

            Camera3D? camera = GetViewport()?.GetCamera3D();
            if (camera == null || _characterWorld == null)
            {
                HideAllEntries();
                return;
            }

            float deltaSeconds = (float)delta;
            bool revealNameplates = ShowNameplates || IsNameplateRevealRequested();
            bool showDebugIds = IsDebugRevealRequested();
            _visibleEntries.Clear();

            foreach ((ulong entityId, WorldUiEntry entry) in _entries)
            {
                entry.Root.Visible = false;

                for (int i = entry.ChatLines.Count - 1; i >= 0; i--)
                {
                    ChatLine line = entry.ChatLines[i];
                    float remainingSeconds = Mathf.Max(0.0f, line.TtlSeconds - deltaSeconds);
                    if (remainingSeconds <= 0.0f)
                    {
                        entry.ChatLines.RemoveAt(i);
                        continue;
                    }

                    entry.ChatLines[i] = line with { TtlSeconds = remainingSeconds };
                }

                bool showChat = ShowOverheadChat && entry.ChatLines.Count > 0;
                bool showNameplate = revealNameplates &&
                    entry.HasNameplate &&
                    entry.Nameplate.Visible &&
                    !string.IsNullOrWhiteSpace(entry.Nameplate.PrimaryText);

                if (!showChat && !showNameplate)
                {
                    if (!entry.HasNameplate && entry.ChatLines.Count == 0)
                        entry.PendingRemoval = true;

                    continue;
                }

                if (!TryGetScreenPosition(entityId, camera, out Vector2 screenPosition, out float distanceSquared))
                    continue;

                _visibleEntries.Add(new VisibleWorldUiEntry(entry, screenPosition, distanceSquared, showNameplate, showChat, showDebugIds));
            }

            if (_visibleEntries.Count == 0)
            {
                RemovePendingEntries();
                return;
            }

            _visibleEntries.Sort(static (left, right) => left.DistanceSquared.CompareTo(right.DistanceSquared));

            int visibleBudget = Mathf.Max(1, MaxVisibleWorldUi);
            for (int i = 0; i < _visibleEntries.Count; i++)
            {
                VisibleWorldUiEntry candidate = _visibleEntries[i];
                if (i >= visibleBudget)
                {
                    candidate.Entry.Root.Visible = false;
                    continue;
                }

                UpdateEntryVisual(candidate);
            }

            RemovePendingEntries();
        }

        public void ShowChatBubble(ClientEntityViewId entityId, string text, float ttlSeconds)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            WorldUiEntry entry = GetOrCreateEntry(entityId);
            entry.ChatLines.Add(new ChatLine(text.Trim(), Mathf.Max(0.01f, ttlSeconds)));
            while (entry.ChatLines.Count > 2)
                entry.ChatLines.RemoveAt(0);
            entry.PendingRemoval = false;
        }

        public void UpdateNameplate(ClientEntityViewId entityId, NameplateState state)
        {
            WorldUiEntry entry = GetOrCreateEntry(entityId);
            entry.Nameplate = state;
            entry.HasNameplate = true;
            entry.PendingRemoval = false;
        }

        public void ClearEntity(ClientEntityViewId entityId)
        {
            if (!_entries.Remove(entityId.Value, out WorldUiEntry? entry))
                return;

            entry.Root.QueueFree();
        }

        public void ClearAll()
        {
            foreach (WorldUiEntry entry in _entries.Values)
                entry.Root.QueueFree();

            _entries.Clear();
            _visibleEntries.Clear();
        }

        private void EnsureOverlayRoot()
        {
            if (_overlayRoot != null)
                return;

            _overlayRoot = new Control
            {
                Name = "WorldUiRoot",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                AnchorRight = 1.0f,
                AnchorBottom = 1.0f,
                OffsetLeft = 0.0f,
                OffsetTop = 0.0f,
                OffsetRight = 0.0f,
                OffsetBottom = 0.0f
            };

            AddChild(_overlayRoot);
        }

        private void HideAllEntries()
        {
            foreach (WorldUiEntry entry in _entries.Values)
                entry.Root.Visible = false;
        }

        private bool TryGetScreenPosition(
            ulong entityId,
            Camera3D camera,
            out Vector2 screenPosition,
            out float distanceSquared)
        {
            screenPosition = Vector2.Zero;
            distanceSquared = 0.0f;

            CharacterRoot? character = _characterWorld?.TryGetCharacter(new ClientEntityViewId(entityId));
            if (character == null)
                return false;

            Node3D anchor = character.NameplatePivot ?? character;
            Vector3 worldPosition = anchor.GlobalPosition;
            if (camera.IsPositionBehind(worldPosition))
                return false;

            screenPosition = camera.UnprojectPosition(worldPosition) + new Vector2(0.0f, -ScreenVerticalOffsetPixels);
            distanceSquared = camera.GlobalPosition.DistanceSquaredTo(worldPosition);
            return true;
        }

        private WorldUiEntry GetOrCreateEntry(ClientEntityViewId entityId)
        {
            if (_entries.TryGetValue(entityId.Value, out WorldUiEntry? existing))
                return existing;

            EnsureOverlayRoot();

            VBoxContainer root = new()
            {
                Name = $"WorldUi_{entityId.Value}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                Visible = false,
                Alignment = BoxContainer.AlignmentMode.Center
            };

            Label debugLabel = CreateWorldLabel();
            Label previousChatLabel = CreateWorldLabel();
            previousChatLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            previousChatLabel.CustomMinimumSize = new Vector2(220.0f, 0.0f);
            Label currentChatLabel = CreateWorldLabel();
            currentChatLabel.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            currentChatLabel.CustomMinimumSize = new Vector2(220.0f, 0.0f);
            Label nameLabel = CreateWorldLabel();

            root.AddChild(debugLabel);
            root.AddChild(previousChatLabel);
            root.AddChild(currentChatLabel);
            root.AddChild(nameLabel);
            _overlayRoot!.AddChild(root);

            WorldUiEntry created = new(entityId.Value, root, debugLabel, previousChatLabel, currentChatLabel, nameLabel);
            _entries[entityId.Value] = created;
            return created;
        }

        private void UpdateEntryVisual(in VisibleWorldUiEntry candidate)
        {
            WorldUiEntry entry = candidate.Entry;

            entry.DebugLabel.Visible = candidate.ShowDebugId;
            entry.DebugLabel.Text = candidate.ShowDebugId ? $"Entity {entry.EntityId}" : string.Empty;
            entry.NameLabel.Visible = candidate.ShowNameplate && !string.IsNullOrWhiteSpace(entry.Nameplate.PrimaryText);
            entry.NameLabel.Text = entry.NameLabel.Visible ? entry.Nameplate.PrimaryText : string.Empty;

            string previousChat = candidate.ShowChat && entry.ChatLines.Count > 1
                ? entry.ChatLines[^2].Text
                : string.Empty;
            string currentChat = candidate.ShowChat && entry.ChatLines.Count > 0
                ? entry.ChatLines[^1].Text
                : string.Empty;
            entry.PreviousChatLabel.Visible = !string.IsNullOrWhiteSpace(previousChat);
            entry.PreviousChatLabel.Text = previousChat;
            entry.CurrentChatLabel.Visible = !string.IsNullOrWhiteSpace(currentChat);
            entry.CurrentChatLabel.Text = currentChat;

            entry.Root.ResetSize();
            Vector2 size = entry.Root.GetCombinedMinimumSize();
            float scale = Mathf.Clamp(WorldUiScale, 0.5f, 3.0f);
            entry.Root.Scale = new Vector2(scale, scale);
            entry.Root.Position = candidate.ScreenPosition - new Vector2(size.X * scale * 0.5f, size.Y * scale);
            entry.Root.Visible =
                entry.DebugLabel.Visible ||
                entry.PreviousChatLabel.Visible ||
                entry.CurrentChatLabel.Visible ||
                entry.NameLabel.Visible;
        }

        private bool IsNameplateRevealRequested()
        {
            if (!string.IsNullOrWhiteSpace(NameplateRevealAction) && InputMap.HasAction(NameplateRevealAction))
                return Input.IsActionPressed(NameplateRevealAction);

            return Input.IsKeyPressed(NameplateRevealKey);
        }

        private bool IsDebugRevealRequested()
        {
            if (string.IsNullOrWhiteSpace(DebugRevealAction) || !InputMap.HasAction(DebugRevealAction))
                return false;

            return Input.IsActionPressed(DebugRevealAction);
        }

        private void RemovePendingEntries()
        {
            if (_entries.Count == 0)
                return;

            List<ulong>? pending = null;
            foreach ((ulong entityId, WorldUiEntry entry) in _entries)
            {
                if (!entry.PendingRemoval)
                    continue;

                pending ??= new List<ulong>();
                pending.Add(entityId);
            }

            if (pending == null)
                return;

            for (int i = 0; i < pending.Count; i++)
                ClearEntity(new ClientEntityViewId(pending[i]));
        }

        private static Label CreateWorldLabel()
        {
            return new Label
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                MouseFilter = Control.MouseFilterEnum.Ignore,
                ClipText = false
            };
        }

        private readonly record struct ChatLine(string Text, float TtlSeconds);

        private sealed class WorldUiEntry
        {
            public WorldUiEntry(ulong entityId, VBoxContainer root, Label debugLabel, Label previousChatLabel, Label currentChatLabel, Label nameLabel)
            {
                EntityId = entityId;
                Root = root;
                DebugLabel = debugLabel;
                PreviousChatLabel = previousChatLabel;
                CurrentChatLabel = currentChatLabel;
                NameLabel = nameLabel;
            }

            public ulong EntityId { get; }
            public VBoxContainer Root { get; }
            public Label DebugLabel { get; }
            public Label PreviousChatLabel { get; }
            public Label CurrentChatLabel { get; }
            public Label NameLabel { get; }
            public NameplateState Nameplate { get; set; }
            public bool HasNameplate { get; set; }
            public List<ChatLine> ChatLines { get; } = new(2);
            public bool PendingRemoval { get; set; }
        }

        private readonly record struct VisibleWorldUiEntry(
            WorldUiEntry Entry,
            Vector2 ScreenPosition,
            float DistanceSquared,
            bool ShowNameplate,
            bool ShowChat,
            bool ShowDebugId);
    }
}
