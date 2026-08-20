using Godot;

namespace Client.Godot.UI
{
    [GlobalClass]
    public partial class ClientChatMenu : Control
    {
        [Export] public NodePath? RuntimeRootPath { get; set; }
        [Export] public NodePath? ChatScrollPath { get; set; }
        [Export] public NodePath? ChatMessagesPath { get; set; }
        [Export] public NodePath? ChatInputPath { get; set; }
        [Export] public NodePath? SendButtonPath { get; set; }

        public MenuPresentationMode PresentationMode => MenuPresentationMode.Shared;

        private GodotClientRuntimeRoot? _runtimeRoot;
        private ScrollContainer? _chatScroll;
        private VBoxContainer? _chatMessages;
        private LineEdit? _chatInput;
        private Button? _sendButton;
        private int _lastChatLineCount = -1;
        private string _lastChatLastLine = string.Empty;

        public override void _Ready()
        {
            _runtimeRoot = RuntimeRootPath == null || RuntimeRootPath.IsEmpty
                ? null
                : GetNodeOrNull<GodotClientRuntimeRoot>(RuntimeRootPath);
            _chatScroll = ChatScrollPath == null || ChatScrollPath.IsEmpty
                ? null
                : GetNodeOrNull<ScrollContainer>(ChatScrollPath);
            _chatMessages = ChatMessagesPath == null || ChatMessagesPath.IsEmpty
                ? null
                : GetNodeOrNull<VBoxContainer>(ChatMessagesPath);
            _chatInput = ChatInputPath == null || ChatInputPath.IsEmpty
                ? null
                : GetNodeOrNull<LineEdit>(ChatInputPath);
            _sendButton = SendButtonPath == null || SendButtonPath.IsEmpty
                ? null
                : GetNodeOrNull<Button>(SendButtonPath);

            if (_chatInput != null)
                _chatInput.TextSubmitted += OnChatSubmitted;
            if (_sendButton != null)
                _sendButton.Pressed += OnSendPressed;
        }

        public void SetChatVisible(bool visible)
        {
            Visible = visible;
            ProcessMode = visible ? ProcessModeEnum.Inherit : ProcessModeEnum.Disabled;

            if (!visible)
                _chatInput?.ReleaseFocus();
        }

        public override void _Process(double delta)
        {
            UpdateChat();
        }

        public override void _UnhandledInput(InputEvent @event)
        {
            if (_runtimeRoot == null || !_runtimeRoot.CanSendChat || _chatInput == null)
                return;

            if (@event is InputEventKey keyEvent &&
                keyEvent.Pressed &&
                !keyEvent.Echo &&
                (keyEvent.Keycode == Key.Enter || keyEvent.Keycode == Key.KpEnter))
            {
                if (!_chatInput.HasFocus())
                {
                    _chatInput.GrabFocus();
                    _chatInput.CaretColumn = _chatInput.Text?.Length ?? 0;
                    GetViewport().SetInputAsHandled();
                }
            }
        }

        private void OnChatSubmitted(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                if (_chatInput != null)
                    _chatInput.ReleaseFocus();
                GetViewport()?.SetInputAsHandled();
                return;
            }

            TrySendChat(text);
        }

        private void OnSendPressed()
        {
            if (_chatInput == null)
                return;

            TrySendChat(_chatInput.Text);
        }

        private void TrySendChat(string text)
        {
            if (_runtimeRoot == null)
                return;

            if (_runtimeRoot.TrySendChatText(text) && _chatInput != null)
            {
                _chatInput.Clear();
                _chatInput.ReleaseFocus();
                GetViewport()?.SetInputAsHandled();
            }
        }

        private void UpdateChat()
        {
            if (_chatMessages != null && _runtimeRoot != null)
            {
                System.Collections.Generic.IReadOnlyList<string> lines = _runtimeRoot.ChatFeedLines;
                string lastLine = lines.Count > 0 ? lines[^1] : string.Empty;
                if (lines.Count != _lastChatLineCount ||
                    !string.Equals(lastLine, _lastChatLastLine, System.StringComparison.Ordinal))
                {
                    RebuildChatMessages(lines);
                    _lastChatLineCount = lines.Count;
                    _lastChatLastLine = lastLine;
                }
            }

            bool canSend = _runtimeRoot?.CanSendChat ?? false;
            if (_chatInput != null)
                _chatInput.Editable = canSend;
            if (_sendButton != null)
                _sendButton.Disabled = !canSend;
        }

        private void RebuildChatMessages(System.Collections.Generic.IReadOnlyList<string> lines)
        {
            if (_chatMessages == null)
                return;

            foreach (Node child in _chatMessages.GetChildren())
                child.QueueFree();

            for (int i = 0; i < lines.Count; i++)
            {
                Label label = new()
                {
                    Text = lines[i],
                    AutowrapMode = TextServer.AutowrapMode.WordSmart,
                    SizeFlagsHorizontal = SizeFlags.ExpandFill
                };
                _chatMessages.AddChild(label);
            }

            if (_chatScroll != null)
                CallDeferred(nameof(ScrollChatToBottom));
        }

        private async void ScrollChatToBottom()
        {
            if (_chatScroll == null || !IsInstanceValid(_chatScroll))
                return;

            await ToSignal(GetTree(), SceneTree.SignalName.ProcessFrame);
            if (_chatScroll == null || !IsInstanceValid(_chatScroll))
                return;

            VScrollBar? scrollBar = _chatScroll.GetVScrollBar();
            if (scrollBar != null)
                _chatScroll.ScrollVertical = (int)scrollBar.MaxValue;
        }
    }
}
